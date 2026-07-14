using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace BritBoxingFeeds.Core.Articles;

/// <summary>
/// The plan's ArticleGenerationService — port of pipeline/article.py. Builds
/// the Claude prompt from the bout context + both frozen JSONB snapshots and
/// returns the article JSON (title, slug, body, summary, tags). The system
/// prompt carries the full BritBoxing house style: UK-journalism tone, no
/// betting talk, write-for-variety, data discipline (never invent stats),
/// and the no-AI-tells rules (no em dashes etc).
/// </summary>
public class ArticleGenerator
{
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string Model = "claude-sonnet-5";

    // Verbatim from pipeline/article.py SYSTEM_PROMPT — keep the two in sync
    // until the Python pipeline is retired.
    // Shared identity, sourcing discipline and house style. A genre block
    // (confirmed preview vs rumour analysis) is appended per article, chosen
    // by the bout's fightStatus.
    private const string HouseStyleCore =
        "You are a British boxing journalist writing for BritBoxing " +
        "(britboxing.co.uk). Tone: UK broadsheet boxing desk — factual, " +
        "measured, no hype clichés ('all-action war', 'fireworks'). " +
        "THE STORY LEADS, THE STATS SUPPORT. Your primary material is the news " +
        "coverage in the prompt: what was reported, by whom, what's at stake, why a " +
        "British audience cares. The fighter stat blocks are background colour. Use " +
        "AT MOST two or three genuinely telling numbers across the whole piece, woven " +
        "into the story where they explain something; NEVER structure the article as " +
        "a statistical breakdown or tale of the tape (the page already shows a full " +
        "head-to-head stat card next to your article — reciting those numbers wastes " +
        "the reader's time). An article with no stats at all is fine if the story " +
        "carries it. " +
        "This is editorial, NOT a betting tip — do not discuss odds, who is the " +
        "favourite, or give a win/method prediction. " +
        "WRITE FOR VARIETY — every piece must feel written from scratch for THIS " +
        "story, not poured into a template. Vary your opening, your section headings " +
        "(zero, one or several <h2>s, as the material demands), your length and how " +
        "you close. Two articles side by side should read as clearly different pieces. " +
        "EMPHASIS: where one fighter is far more famous, weight the piece toward the " +
        "lesser-known opponent — readers arrive knowing the big name. " +
        "SOURCING DISCIPLINE: use ONLY what the prompt provides. Facts and claims " +
        "come from the coverage text; statistics come from the stat blocks. Never " +
        "invent records, rankings, dates, venues, quotes or biographical detail. " +
        "Quote a phrase ONLY if it appears verbatim in the coverage text, and " +
        "attribute it to the source that carried it. If something is null/unknown, " +
        "leave it out — never say a figure is missing. Form data marked unverified " +
        "may be described softly ('recent outings suggest') or omitted. Do not refer " +
        "to yourself, to AI, or to how the article was produced. " +
        "TITLE: a news headline — name the fighters and state the development " +
        "('X and Y agree September date', 'X called out by Y after Riyadh win'). " +
        "Never frame the title around statistics, records or 'what the numbers say'. " +
        "HOUSE STYLE — write like a person, not a model. Hard rules: NEVER use an em " +
        "dash (—) or en dash (–); use commas, full stops or brackets and recast the " +
        "sentence. Use straight quotes and apostrophes, not curly ones. Avoid these " +
        "tell-tale constructions: 'not just X but Y' and 'it's not X, it's Y'; " +
        "'not a … so much as …'; semicolon antithesis ('one has X; the other has Y'); " +
        "rule-of-three lists for rhythm; and ending on a rhetorical 'the question is " +
        "whether…'. Vary how each article closes. Avoid filler/marketing words " +
        "(testament, stark reminder, delve, tapestry, landscape, realm, navigate, " +
        "underscore, boasts, showcase, leverage, crucial, pivotal, robust) and hedging " +
        "openers (That said, Ultimately, It's worth noting, When it comes to, In a " +
        "world where). Vary sentence length; let some sentences be short and plain. " +
        "Return ONLY a JSON object with keys: title, slug, body, summary, tags. " +
        "'body' is HTML (<p>, <h2>, <ul>) — no <html>/<head>. 'tags' is an array of strings.";

    private const string ConfirmedGenre =
        " GENRE — CONFIRMED FIGHT, news-led preview. This fight is officially on. " +
        "Lead with the announcement itself: what was confirmed, where and when (if " +
        "known), what it took to make, what's at stake (a title, a rivalry, a " +
        "mandatory, a step up) and what it means for the British scene. Then give a " +
        "sense of how the fight shapes up, drawing on the coverage's framing before " +
        "the numbers. Titles for confirmed fights state the news plainly.";

    private const string RumouredGenre =
        " GENRE — RUMOURED FIGHT, news analysis. This fight is NOT confirmed; the " +
        "story IS the rumour. Report it as such: who is reporting or saying what " +
        "(promoter noises, a call-out, talks, a mandatory being ordered), how firm " +
        "the sourcing in the coverage actually sounds, what each side would gain, " +
        "the obstacles the coverage implies (rival promoters, broadcasters, other " +
        "commitments), and what would need to happen for it to get made. NEVER " +
        "present the fight as booked; keep the conditional register throughout. " +
        "The title must carry a hedge word (in talks, linked, eyed, mooted, targets, " +
        "wants). Stats appear only where they explain why the matchup is being " +
        "discussed at all.";

    private const string ResultGenre =
        " GENRE — FIGHT RESULT, report. The fight has happened; report what the " +
        "coverage says took place: the outcome, how it unfolded, any turning point " +
        "or scorecards the coverage mentions, and what the result means for the " +
        "winner, the loser and the British scene. State the result plainly and " +
        "early. The fighter stat blocks show each man's record BEFORE the fight " +
        "(frozen at announcement) — use them only as 'what the tape said " +
        "beforehand' colour, e.g. an unbeaten record ending or a favourite on " +
        "paper coming unstuck. Do not update or recompute records yourself. " +
        "Report only what the coverage states about the fight itself; if the " +
        "coverage lacks detail (rounds, scorecards), write a shorter piece rather " +
        "than padding.";

    private static string SystemPromptFor(string? fightStatus) => HouseStyleCore + fightStatus switch
    {
        "rumoured" => RumouredGenre,
        "result" => ResultGenre,
        _ => ConfirmedGenre,
    };

    private readonly HttpClient _http;
    private readonly ILogger<ArticleGenerator> _logger;

    public ArticleGenerator(HttpClient http, ILogger<ArticleGenerator> logger)
    {
        _http = http;
        _logger = logger;

        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? throw new InvalidOperationException("ANTHROPIC_API_KEY environment variable is not set.");

        _http.DefaultRequestHeaders.Clear();
        _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.Timeout = TimeSpan.FromMinutes(4);
    }

    /// <summary>The prompt persisted into bouts.prompt for audit/regeneration.</summary>
    public static JsonObject BuildPromptRecord(JsonObject bout, JsonObject snapA, JsonObject snapB) => new()
    {
        ["system"] = SystemPromptFor(bout["fightStatus"]?.GetValue<string>()),
        ["user"] = BuildPrompt(bout, snapA, snapB),
        ["model"] = Model,
    };

    /// <summary>Generate the article JSON, or null when the response couldn't be parsed (caller leaves the bout at bout_created for a later retry).</summary>
    public async Task<JsonObject?> GenerateAsync(JsonObject bout, JsonObject snapA, JsonObject snapB, CancellationToken ct = default)
    {
        var article = await GenerateOnceAsync(bout, snapA, snapB, extraInstruction: null, ct);
        if (article is null) return null;

        var lint = ArticleLinter.Lint(article);
        if (lint.FixCount > 0)
        {
            _logger.LogInformation("Lint fixed {Count} hard-rule characters in article for '{Headline}'",
                lint.FixCount, bout["headline"]?.GetValue<string>());
        }
        if (lint.Warnings.Count > 0)
        {
            _logger.LogWarning("Lint: AI-tell phrases in article for '{Headline}': {Warnings}",
                bout["headline"]?.GetValue<string>(), string.Join("; ", lint.Warnings));

            // Opt-in single retry — an extra API call, so off by default.
            if (Environment.GetEnvironmentVariable("ARTICLE_LINT_RETRY") == "1")
            {
                var retry = await GenerateOnceAsync(bout, snapA, snapB,
                    "IMPORTANT: your previous draft used these banned phrasings, which you must avoid this time: "
                    + string.Join("; ", lint.Warnings), ct);
                if (retry is not null)
                {
                    var retryLint = ArticleLinter.Lint(retry);
                    if (retryLint.Warnings.Count < lint.Warnings.Count) return retry;
                }
            }
        }
        return article;
    }

    private async Task<JsonObject?> GenerateOnceAsync(JsonObject bout, JsonObject snapA, JsonObject snapB, string? extraInstruction, CancellationToken ct)
    {
        var userPrompt = BuildPrompt(bout, snapA, snapB);
        if (extraInstruction is not null) userPrompt = $"{userPrompt}\n\n{extraInstruction}";

        var requestBody = new JsonObject
        {
            ["model"] = Model,
            // Long-form writing benefits from the model's default adaptive
            // thinking, so it is left on; budget covers thinking + article.
            ["max_tokens"] = 4000,
            ["system"] = SystemPromptFor(bout["fightStatus"]?.GetValue<string>()),
            ["messages"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["content"] = userPrompt,
            }),
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
        {
            Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json"),
        };

        try
        {
            var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(responseJson);

            var text = doc.RootElement
                .GetProperty("content")
                .EnumerateArray()
                .Where(b => b.GetProperty("type").GetString() == "text")
                .Select(b => b.GetProperty("text").GetString())
                .FirstOrDefault() ?? "";

            var cleaned = text.Trim();
            if (cleaned.StartsWith("```json")) cleaned = cleaned["```json".Length..];
            else if (cleaned.StartsWith("```")) cleaned = cleaned[3..];
            if (cleaned.EndsWith("```")) cleaned = cleaned[..^3];

            // The model sometimes emits literal newlines inside the body
            // string, which strict JSON rejects — escape them before parsing.
            return JsonNode.Parse(EscapeControlCharsInStrings(cleaned.Trim()))?.AsObject();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Article generation failed for '{Headline}'", bout["headline"]?.GetValue<string>());
            return null;
        }
    }

    /// <summary>Escape raw newline/tab control characters that appear inside JSON string literals (tracks in-string state including backslash escapes).</summary>
    private static string EscapeControlCharsInStrings(string json)
    {
        var sb = new StringBuilder(json.Length + 64);
        bool inString = false, escaped = false;
        foreach (var ch in json)
        {
            if (inString)
            {
                if (escaped) { sb.Append(ch); escaped = false; continue; }
                switch (ch)
                {
                    case '\\': sb.Append(ch); escaped = true; continue;
                    case '"': inString = false; sb.Append(ch); continue;
                    case '\n': sb.Append("\\n"); continue;
                    case '\r': sb.Append("\\r"); continue;
                    case '\t': sb.Append("\\t"); continue;
                    default: sb.Append(ch); continue;
                }
            }
            if (ch == '"') inString = true;
            sb.Append(ch);
        }
        return sb.ToString();
    }

    // The news coverage leads; extractor facts follow; the stat snapshots come
    // last, explicitly demoted to supporting colour. (The Python-era
    // stats-first prompt this replaced lives in git history.)
    private static string BuildPrompt(JsonObject bout, JsonObject snapA, JsonObject snapB)
    {
        var aName = snapA["_meta"]!["name"]!.GetValue<string>();
        var bName = snapB["_meta"]!["name"]!.GetValue<string>();
        var options = new JsonSerializerOptions { WriteIndented = true };
        var status = bout["fightStatus"]?.GetValue<string>();
        var isRumour = status == "rumoured";
        var isResult = status == "result";

        var sb = new StringBuilder();
        sb.AppendLine(isResult
            ? $"Write a fight-report article about the result of {aName} vs {bName}."
            : isRumour
                ? $"Write a news-analysis article about the RUMOURED fight between {aName} and {bName}."
                : $"Write a news-led preview article about the confirmed fight between {aName} and {bName}.");
        sb.AppendLine();

        // ---- The news: what each source actually said. -------------------
        sb.AppendLine("## The news (your primary material — this is what was reported)");
        if (bout["coverage"] is JsonArray coverage && coverage.Count > 0)
        {
            var n = 0;
            foreach (var c in coverage.OfType<JsonObject>())
            {
                n++;
                sb.AppendLine($"### Report {n} — {c["source"]?.GetValue<string>() ?? "unknown source"}");
                if (c["headline"]?.GetValue<string>() is { Length: > 0 } h) sb.AppendLine($"Headline: {h}");
                if (c["fullText"]?.GetValue<string>() is { Length: > 0 } full)
                    sb.AppendLine($"Story text: {full}");
                else if (c["summary"]?.GetValue<string>() is { Length: > 0 } s)
                    sb.AppendLine($"Summary: {s}");
                sb.AppendLine();
            }
        }
        else
        {
            // Degenerate case (old data, retry with no seen rows): all we know
            // is the headline the bout was created from — say so explicitly,
            // or the model pads the announcement story with invented detail
            // ("camps agreed terms" etc.).
            sb.AppendLine($"Headline: {bout["headline"]?.GetValue<string>() ?? "(none available)"}");
            sb.AppendLine("No coverage text is available for this fight. Do NOT invent announcement details " +
                          "(negotiations, terms, venues, how the fight was made, who said what). Write about " +
                          "the matchup itself and keep any announcement framing to the bare fact above.");
            sb.AppendLine();
        }
        sb.AppendLine("Quote a phrase only if it appears verbatim in the text above, attributed to its source. Never invent quotes.");
        sb.AppendLine();

        // ---- Extractor facts (omit unknowns entirely). -------------------
        sb.AppendLine("## Fight facts");
        sb.AppendLine($"- Status: {(isResult ? "COMPLETED, this is a result report" : isRumour ? "RUMOURED, not confirmed" : "confirmed")}");
        sb.AppendLine($"- Matchup: {aName} vs {bName}");
        if (isResult && bout["result"] is JsonObject res)
        {
            if (res["winner"]?.GetValue<string>() is { Length: > 0 } winner) sb.AppendLine($"- Winner: {winner}");
            if (res["method"]?.GetValue<string>() is { Length: > 0 } method) sb.AppendLine($"- Method: {method}");
            if (res["round"] is not null) sb.AppendLine($"- Ended in round: {res["round"]}");
        }
        if (bout["eventDate"]?.GetValue<string>() is { Length: > 0 } date) sb.AppendLine($"- Date: {date}");
        if (bout["venue"]?.GetValue<string>() is { Length: > 0 } venue) sb.AppendLine($"- Venue: {venue}");
        if (bout["city"]?.GetValue<string>() is { Length: > 0 } city) sb.AppendLine($"- City: {city}");
        if (bout["weightClass"]?.GetValue<string>() is { Length: > 0 } wc) sb.AppendLine($"- Weight class: {wc}");
        if (bout["titleOnTheLine"]?.GetValue<string>() is { Length: > 0 } belt) sb.AppendLine($"- Title on the line: {belt}");
        if (bout["broadcaster"]?.GetValue<string>() is { Length: > 0 } tv) sb.AppendLine($"- Broadcaster: {tv}");
        sb.AppendLine();

        // ---- Stat snapshots, explicitly demoted. --------------------------
        sb.AppendLine(isResult
            ? "## Fighter backgrounds (records BEFORE the fight, frozen at announcement — 'what the tape said beforehand' colour only; do not update them yourself)"
            : "## Fighter backgrounds (supporting colour ONLY — at most two or three numbers in the whole piece; the page already shows the full stat card)");
        sb.AppendLine($"### {aName}");
        sb.AppendLine(snapA.ToJsonString(options));
        sb.AppendLine($"### {bName}");
        sb.AppendLine(snapB.ToJsonString(options));
        sb.AppendLine();

        sb.AppendLine(isResult
            ? """
              ## Checklist (cover in whatever order serves the story)
              - The result, stated plainly and early: who won, how, when it ended (as the coverage states).
              - How the fight unfolded per the coverage: turning points, scorecards, anything notable.
              - What it means: for the winner's standing, the loser's options, the British scene.
              - Only what the coverage supports; a short accurate report beats a padded one.
              Invent nothing; use only the material above. Omit anything you don't have. Return the JSON object only.
              """
            : isRumour
            ? """
              ## Checklist (cover in whatever order serves the story)
              - What is actually being reported, by whom, and how firm it sounds from the text above.
              - Why the matchup is being talked about: what each side gains, the British angle.
              - Obstacles and conditions the coverage implies; what would need to happen next.
              - Keep the conditional register: this fight is not made. Hedge word in the title.
              Invent nothing; use only the material above. Omit anything you don't have. Return the JSON object only.
              """
            : """
              ## Checklist (cover in whatever order serves the story)
              - The announcement itself: what's now official, where/when if stated, what it took to make.
              - The stakes: title, rivalry, mandatory, step up, what it means for the British scene.
              - How the fight shapes up, led by the coverage's framing, with at most 2-3 supporting numbers.
              Invent nothing; use only the material above. Omit anything you don't have. Return the JSON object only.
              """);

        return sb.ToString();
    }
}

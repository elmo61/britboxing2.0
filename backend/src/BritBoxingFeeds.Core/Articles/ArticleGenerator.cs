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
    private const string SystemPrompt =
        "You are a British boxing journalist writing fight-preview articles for " +
        "BritBoxing (britboxing.co.uk). Tone: UK broadsheet boxing desk — factual, " +
        "measured, no hype clichés ('all-action war', 'fireworks'). " +
        "PURPOSE: help the reader understand the two boxers and how the fight shapes " +
        "up. This is editorial about the fighters, NOT a betting tip — do not discuss " +
        "odds, who is the favourite, or give a win/method prediction. " +
        "WRITE FOR VARIETY — this is important. Every preview must feel written from " +
        "scratch for THIS fight, not poured into a template. Before writing, find the " +
        "single most interesting angle this matchup's data suggests — a huge " +
        "experience gap, two unbeaten punchers, a reach mismatch, a veteran slowing " +
        "down, a prospect's step up — and build the piece around THAT. Vary your " +
        "opening every time (never default to 'When a fight involves…'); vary the " +
        "order you introduce the fighters; vary your section headings and how many " +
        "sections you use; vary article length to fit how much the data supports. The " +
        "structure below is a checklist of what to COVER, not a running order to " +
        "follow. Two previews placed side by side should read as clearly different pieces. " +
        "EMPHASIS: where one fighter is far more famous, weight the piece toward the " +
        "lesser-known opponent — readers arrive knowing the big name. Where both are " +
        "comparably known, treat them evenly. " +
        "DATA DISCIPLINE: use ONLY the statistics provided in the prompt. Never invent " +
        "records, rankings, dates, venues, quotes or biographical detail. If a stat is " +
        "null/unknown, simply leave it out — never state that a figure is missing or " +
        "unavailable. Use a stat only when it is genuinely informative (e.g. a clear " +
        "reach or experience advantage); skip differences that are marginal. Form data " +
        "marked unverified may be described softly ('recent outings suggest') or omitted. " +
        "Do not refer to yourself, to AI, or to how the article was produced. " +
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
        ["system"] = SystemPrompt,
        ["user"] = BuildPrompt(bout, snapA, snapB),
        ["model"] = Model,
    };

    /// <summary>Generate the article JSON, or null when the response couldn't be parsed (caller leaves the bout at bout_created for a later retry).</summary>
    public async Task<JsonObject?> GenerateAsync(JsonObject bout, JsonObject snapA, JsonObject snapB, CancellationToken ct = default)
    {
        var requestBody = new JsonObject
        {
            ["model"] = Model,
            // Long-form writing benefits from the model's default adaptive
            // thinking, so it is left on; budget covers thinking + article.
            ["max_tokens"] = 4000,
            ["system"] = SystemPrompt,
            ["messages"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["content"] = BuildPrompt(bout, snapA, snapB),
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

    // Port of article.py build_prompt().
    private static string BuildPrompt(JsonObject bout, JsonObject snapA, JsonObject snapB)
    {
        var aName = snapA["_meta"]!["name"]!.GetValue<string>();
        var bName = snapB["_meta"]!["name"]!.GetValue<string>();
        var options = new JsonSerializerOptions { WriteIndented = true };

        return $"""
            Write a fight-preview article for the following bout.

            ## Bout
            - Matchup: {aName} vs {bName}
            - Announced via: {bout["source"]?.GetValue<string>()} — headline: "{bout["headline"]?.GetValue<string>()}"
            - Source link: {bout["link"]?.GetValue<string>()}
            - Event date: {bout["eventDate"]?.GetValue<string>() ?? "to be confirmed"}
            - Weight class (heaviest division both fighters share): {bout["weightClass"]?.GetValue<string>() ?? "unknown"}

            ## Fighter A — {aName}
            {snapA.ToJsonString(options)}

            ## Fighter B — {bName}
            {snapB.ToJsonString(options)}

            ## What to cover (a checklist, NOT a running order — arrange it your own way for this fight)
            - A sense of each fighter: record, how they win (KO vs decision), recent outings.
            - The genuinely informative contrasts (a clear experience, finishing-rate, reach
              or age gap). Skip stats that are missing or marginal.
            - The stylistic questions the numbers raise — what each fighter must do.
            Lead with whatever is most interesting about THIS matchup; vary your structure,
            headings and opening from other previews. No odds, no favourite, no prediction.

            Remember: invent nothing; use only the JSON above. Omit anything you don't have
            data for — never mention that a figure is missing. Return the JSON object only.
            """;
    }
}

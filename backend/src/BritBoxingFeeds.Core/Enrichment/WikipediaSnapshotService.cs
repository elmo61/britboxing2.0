using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.Extensions.Logging;

namespace BritBoxingFeeds.Core.Enrichment;

/// <summary>
/// The plan's FighterEnrichmentService: resolves a fighter name to a JSONB
/// snapshot via the English Wikipedia MediaWiki API. Port of
/// pipeline/snapshots.py + the low-level helpers in britboxing_wikifetch.py —
/// same field parsers, same snapshot shape, so C# and Python snapshots are
/// interchangeable in the fighters/bouts tables.
///
/// Wikimedia etiquette: a descriptive User-Agent with contact info is
/// required by API policy (set in the constructor); volume stays low
/// (~2 requests per fighter) and requests send maxlag=5 with backoff.
/// Wikipedia text is CC BY-SA 4.0 — attribute Wikipedia when displaying
/// derived text. The last-5 form derivation is best-effort and flagged
/// _unverified, as bout-table layouts vary.
/// </summary>
public class WikipediaSnapshotService
{
    private const string ApiUrl = "https://en.wikipedia.org/w/api.php";
    private const string UserAgent = "BritBoxingDataBot/0.1 (https://britboxing.co.uk; contact@britboxing.co.uk)";

    private readonly HttpClient _http;
    private readonly ILogger<WikipediaSnapshotService> _logger;

    public WikipediaSnapshotService(HttpClient http, ILogger<WikipediaSnapshotService> logger)
    {
        _http = http;
        _logger = logger;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }

    /// <summary>
    /// Resolve a fighter name to a snapshot. allowSparse=false returns null
    /// when there's no usable Wikipedia article (rejects feed noise);
    /// allowSparse=true returns a minimal name-only snapshot instead, so a
    /// star-vs-unknown bout can still be created with the data we do have.
    /// </summary>
    public async Task<JsonObject?> BuildSnapshotAsync(string name, bool allowSparse = false, CancellationToken ct = default)
    {
        try
        {
            var title = await SearchTitleAsync(name, ct);
            if (title is not null && !TitleMatchesQuery(name, title))
            {
                // Full-text search matched the name inside someone else's
                // article ("Richard Rivera" -> Ben Whittaker's page) or a
                // side article ("Boxing career of X"). Treat as not found
                // rather than building a snapshot of the wrong person.
                _logger.LogWarning("Wikipedia search for '{Name}' resolved to unrelated article '{Title}', rejecting", name, title);
                title = null;
            }
            var wikitext = title is not null ? await FetchWikitextAsync(title, ct) : null;
            if (wikitext is null)
            {
                _logger.LogWarning("No usable Wikipedia article for '{Name}'", name);
                return allowSparse ? SparseSnapshot(name) : null;
            }
            return Build(title!, wikitext);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Wikipedia fetch failed for '{Name}'", name);
            return allowSparse ? SparseSnapshot(name) : null;
        }
    }

    /// <summary>True if the snapshot carries a parseable win count (real Wikipedia data, not sparse/noise).</summary>
    public static bool HasRecord(JsonObject? snapshot) =>
        snapshot?["record"]?["wins"] is not null;

    /// <summary>
    /// Sanity check on the search result: the resolved article must be about
    /// the person searched for, not a page that merely mentions them. Career
    /// sub-articles and list pages are rejected, and the display title must
    /// contain the queried surname.
    /// </summary>
    private static bool TitleMatchesQuery(string query, string title)
    {
        var lowered = title.ToLowerInvariant();
        if (lowered.Contains("career of") || lowered.StartsWith("list of")) return false;

        var display = Regex.Replace(title, @"\s*\([^)]*\)", "").Trim().ToLowerInvariant();
        var surname = query.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.ToLowerInvariant();
        return surname is not null && display.Contains(surname);
    }

    // ----------------------------------------------------------------- //
    // MediaWiki API
    // ----------------------------------------------------------------- //
    private async Task<JsonObject?> GetAsync(Dictionary<string, string> parameters, CancellationToken ct)
    {
        var query = HttpUtility.ParseQueryString("");
        foreach (var (k, v) in parameters) query[k] = v;
        query["format"] = "json";
        query["maxlag"] = "5";

        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                var response = await _http.GetAsync($"{ApiUrl}?{query}", ct);
                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(ct);
                    var data = JsonNode.Parse(body)?.AsObject();
                    if (data?["error"]?["code"]?.GetValue<string>() == "maxlag")
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2 * (attempt + 1)), ct);
                        continue;
                    }
                    return data;
                }
            }
            catch (HttpRequestException) when (attempt < 3)
            {
                // fall through to backoff
            }
            await Task.Delay(TimeSpan.FromSeconds(1.5 * (attempt + 1)), ct);
        }
        return null;
    }

    private async Task<string?> SearchTitleAsync(string query, CancellationToken ct)
    {
        var data = await GetAsync(new()
        {
            ["action"] = "query",
            ["list"] = "search",
            ["srsearch"] = $"{query} boxer",
            ["srlimit"] = "1",
        }, ct);
        return data?["query"]?["search"] is JsonArray { Count: > 0 } hits
            ? hits[0]?["title"]?.GetValue<string>()
            : null;
    }

    private async Task<string?> FetchWikitextAsync(string title, CancellationToken ct)
    {
        var data = await GetAsync(new()
        {
            ["action"] = "query",
            ["prop"] = "revisions",
            ["titles"] = title,
            ["rvprop"] = "content",
            ["rvslots"] = "main",
            ["redirects"] = "1",
        }, ct);
        if (data?["query"]?["pages"] is not JsonObject pages) return null;
        foreach (var (_, page) in pages)
        {
            if (page is not JsonObject p) continue;
            if (p.ContainsKey("missing")) return null;
            if (p["revisions"] is JsonArray { Count: > 0 } revs)
                return revs[0]?["slots"]?["main"]?["*"]?.GetValue<string>();
        }
        return null;
    }

    // ----------------------------------------------------------------- //
    // Wikitext parsing (ports of britboxing_wikifetch.py helpers)
    // ----------------------------------------------------------------- //

    /// <summary>Strip refs, comments, links and bold/italic markup from a param value.</summary>
    private static string Clean(string val)
    {
        val = Regex.Replace(val, @"<ref[^>]*?/>", "");
        val = Regex.Replace(val, @"<ref[^>]*?>.*?</ref>", "", RegexOptions.Singleline);
        val = Regex.Replace(val, @"<!--.*?-->", "", RegexOptions.Singleline);
        val = Regex.Replace(val, @"\[\[(?:[^\]|]*\|)?([^\]]*)\]\]", "$1"); // [[link|text]] -> text
        val = Regex.Replace(val, @"'''?", "");
        return val.Trim();
    }

    /// <summary>The Infobox boxer params as {lowercased_key: raw_value} — brace-balanced scan (the Python fallback path, no mwparserfromhell equivalent needed).</summary>
    private static Dictionary<string, string> ParseInfobox(string wikitext)
    {
        var m = Regex.Match(wikitext, @"\{\{\s*infobox (?:boxer|sportsperson)", RegexOptions.IgnoreCase);
        if (!m.Success) return new Dictionary<string, string>();

        int i = m.Index, depth = 0, start = m.Index;
        while (i < wikitext.Length)
        {
            if (i + 1 < wikitext.Length && wikitext[i] == '{' && wikitext[i + 1] == '{') { depth++; i += 2; continue; }
            if (i + 1 < wikitext.Length && wikitext[i] == '}' && wikitext[i + 1] == '}')
            {
                depth--; i += 2;
                if (depth == 0) break;
                continue;
            }
            i++;
        }

        var block = wikitext[start..Math.Min(i, wikitext.Length)];
        var parameters = new Dictionary<string, string>();
        foreach (var part in Regex.Split(block, @"\n\s*\|"))
        {
            var separator = part.IndexOf('=');
            if (separator > 0)
            {
                parameters[part[..separator].Trim().ToLowerInvariant()] = part[(separator + 1)..].Trim();
            }
        }
        return parameters;
    }

    private static int? ParseInt(Dictionary<string, string> parameters, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (parameters.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw))
            {
                var digits = Regex.Replace(Clean(raw), @"[^\d]", "");
                if (digits.Length > 0 && int.TryParse(digits, out var n)) return n;
            }
        }
        return null;
    }

    private static string? ParseStance(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var v = Clean(raw).ToLowerInvariant();
        if (v.Contains("southpaw")) return "Southpaw";
        if (v.Contains("orthodox")) return "Orthodox";
        var cleaned = Clean(raw);
        return cleaned.Length > 0 ? cleaned : null;
    }

    /// <summary>'7' -> 7.0, '7.5' -> 7.5, '7+1/2' -> 7.5.</summary>
    private static double ParseFraction(string value)
    {
        value = value.Trim();
        var m = Regex.Match(value, @"^(\d+)\+(\d+)/(\d+)$");
        if (m.Success)
        {
            return int.Parse(m.Groups[1].Value)
                + (double)int.Parse(m.Groups[2].Value) / int.Parse(m.Groups[3].Value);
        }
        return double.Parse(value);
    }

    /// <summary>Age from a {{Birth date and age|Y|M|D}} / {{bda|...}} template, tolerant of case and named flags (df=y) anywhere in the args.</summary>
    private static int? ParseAge(string raw)
    {
        var today = DateTime.UtcNow.Date;
        var m = Regex.Match(raw, @"\b(?:birth date and age|bda|birth year and age)\b([^}]*)", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var nums = Regex.Split(m.Groups[1].Value, @"\|")
            .Where(x => x.Trim().All(char.IsDigit) && x.Trim().Length > 0)
            .Select(x => int.Parse(x.Trim()))
            .ToList();
        if (nums.Count >= 3)
        {
            var (y, mo, d) = (nums[0], nums[1], nums[2]);
            return today.Year - y - ((today.Month, today.Day).CompareTo((mo, d)) < 0 ? 1 : 0);
        }
        return nums.Count > 0 ? today.Year - nums[0] : null;
    }

    /// <summary>Birth date as ISO 'YYYY-MM-DD' — stored for stable fighter IDs (more durable than age).</summary>
    private static string? ParseDob(string raw)
    {
        var m = Regex.Match(raw, @"\b(?:birth date and age|bda|birth date)\b([^}]*)", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var nums = Regex.Split(m.Groups[1].Value, @"\|")
            .Where(x => x.Trim().All(char.IsDigit) && x.Trim().Length > 0)
            .Select(x => int.Parse(x.Trim()))
            .ToList();
        if (nums.Count >= 3)
        {
            try { return new DateOnly(nums[0], nums[1], nums[2]).ToString("yyyy-MM-dd"); }
            catch (ArgumentOutOfRangeException) { return null; }
        }
        return null;
    }

    /// <summary>Length in whole inches from the many formats Wikipedia uses.</summary>
    private static int? ParseLengthInches(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        raw = Regex.Replace(raw, @"<ref.*?(/>|</ref>)", "", RegexOptions.Singleline).Replace("&nbsp;", " ");

        // 5 ft 7.5 in  /  5 ft 8+1/2 in
        var m = Regex.Match(raw, @"(\d+)\s*ft\s*(\d+(?:\.\d+)?(?:\+\d+/\d+)?)\s*in", RegexOptions.IgnoreCase);
        if (m.Success) return (int)Math.Round(int.Parse(m.Groups[1].Value) * 12 + ParseFraction(m.Groups[2].Value));

        // {{convert|5|ft|8|in}}
        m = Regex.Match(raw, @"\{\{convert\|(\d+)\|ft\|(\d+)\|in", RegexOptions.IgnoreCase);
        if (m.Success) return int.Parse(m.Groups[1].Value) * 12 + int.Parse(m.Groups[2].Value);

        // bare inches: 72 in  /  70+1/2 in
        m = Regex.Match(raw, @"(\d+(?:\.\d+)?(?:\+\d+/\d+)?)\s*in\b", RegexOptions.IgnoreCase);
        if (m.Success) return (int)Math.Round(ParseFraction(m.Groups[1].Value));

        // {{convert|180|cm}}  /  180 cm
        m = Regex.Match(raw, @"\{\{convert\|(\d+(?:\.\d+)?)\|cm");
        if (!m.Success) m = Regex.Match(raw, @"(\d+(?:\.\d+)?)\s*cm");
        if (m.Success) return (int)Math.Round(double.Parse(m.Groups[1].Value) / 2.54);

        return null;
    }

    /// <summary>The list of divisions from a {{plainlist|*[[X]]*[[Y]]}} weight/division param.</summary>
    private static List<string>? ParseWeightClasses(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var links = Regex.Matches(raw, @"\[\[([^\]|]+)(?:\|[^\]]+)?\]\]").Select(m => Clean(m.Groups[1].Value)).ToList();
        if (links.Count > 0) return links;
        var parts = Regex.Split(raw, @"[\*\n]+")
            .Select(p => p.Trim())
            .Where(p => p.Length > 0 && !p.Contains("plainlist", StringComparison.OrdinalIgnoreCase))
            .Select(Clean)
            .ToList();
        return parts.Count > 0 ? parts : null;
    }

    // Divisions light -> heavy; synonyms normalise to one key for comparison.
    private static readonly string[] DivisionOrder =
    [
        "minimumweight", "light flyweight", "flyweight", "super flyweight",
        "bantamweight", "super bantamweight", "featherweight", "super featherweight",
        "lightweight", "super lightweight", "welterweight", "super welterweight",
        "middleweight", "super middleweight", "light heavyweight", "cruiserweight",
        "heavyweight",
    ];

    private static readonly Dictionary<string, string> DivisionSynonyms = new()
    {
        ["strawweight"] = "minimumweight",
        ["junior flyweight"] = "light flyweight",
        ["junior bantamweight"] = "super flyweight",
        ["junior featherweight"] = "super bantamweight",
        ["junior lightweight"] = "super featherweight",
        ["junior welterweight"] = "super lightweight",
        ["light welterweight"] = "super lightweight",
        ["junior middleweight"] = "super welterweight",
        ["light middleweight"] = "super welterweight",
    };

    private static string? NormaliseDivision(string name)
    {
        var key = name.ToLowerInvariant();
        key = Regex.Replace(key, @"\([^)]*\)", " ");   // drop "(boxing)" etc.
        key = key.Replace("-", " ");
        key = Regex.Replace(key, @"\s+", " ").Trim();
        key = DivisionSynonyms.GetValueOrDefault(key, key);
        return DivisionOrder.Contains(key) ? key : null;
    }

    /// <summary>Heaviest division both fighters share — the likely division for the bout.</summary>
    public static string? BoutWeightClass(JsonObject snapA, JsonObject snapB)
    {
        static HashSet<string> Divisions(JsonObject snap) =>
            (snap["_meta"]?["weightClasses"] as JsonArray ?? [])
                .Select(n => n?.GetValue<string>())
                .Where(s => s is not null)
                .Select(s => NormaliseDivision(s!))
                .Where(d => d is not null)
                .Select(d => d!)
                .ToHashSet();

        var shared = Divisions(snapA).Intersect(Divisions(snapB)).ToList();
        if (shared.Count == 0) return null;
        var heaviest = shared.MaxBy(d => Array.IndexOf(DivisionOrder, d))!;
        return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(heaviest);
    }

    // ----------------------------------------------------------------- //
    // Form (recent record) — still best-effort / unverified
    // ----------------------------------------------------------------- //
    // Case-sensitive result match: avoids notes' "Won"/"Lost".
    private static readonly Regex ResultRegex = new(@"\b(Win|Loss|Draw|NC)\b", RegexOptions.Compiled);
    private static readonly Regex MethodRegex = new(@"\b(KO|TKO|UD|SD|MD|RTD|DQ|PTS)\b", RegexOptions.Compiled);

    private static JsonObject? ParseRecentForm(string wikitext, int n = 5)
    {
        var heading = Regex.Match(wikitext, @"==+\s*Professional (?:boxing )?record\s*==+", RegexOptions.IgnoreCase);
        if (!heading.Success) return null;
        var tail = wikitext[(heading.Index + heading.Length)..];

        // Scan every wikitable under the heading, keep the one with the most result rows.
        var best = new List<(string Letter, string Method)>();
        foreach (Match table in Regex.Matches(tail, @"\{\|.*?\|\}", RegexOptions.Singleline))
        {
            var rows = new List<(string, string)>();
            foreach (var row in Regex.Split(table.Value, @"\n\|-"))
            {
                var rm = ResultRegex.Match(row);
                if (!rm.Success) continue;
                var mm = MethodRegex.Match(row);
                var letter = rm.Groups[1].Value switch
                {
                    "Win" => "W", "Loss" => "L", "Draw" => "D", _ => "N",
                };
                rows.Add((letter, mm.Success ? mm.Groups[1].Value : ""));
            }
            if (rows.Count > best.Count) best = rows;
        }
        if (best.Count == 0) return null;

        var recent = best.Take(n).ToList(); // Wikipedia lists most-recent first
        var last5 = recent.Select(r => r.Letter switch
        {
            "W" => r.Method is "KO" or "TKO" ? "WKO" : "WDEC",
            "L" => r.Method is "KO" or "TKO" ? "LKO" : "LDEC",
            "D" => "DRAW",
            _ => "NC",
        }).ToList();

        var first = recent[0].Letter;
        var streak = recent.TakeWhile(r => r.Letter == first).Count();
        var onWinStreak = first == "W";

        return new JsonObject
        {
            ["currentStreak"] = onWinStreak ? streak : 0,
            ["streakType"] = onWinStreak
                ? (recent.Take(streak).Any(r => r.Method is "KO" or "TKO") ? "KO" : "Decision")
                : null,
            ["last5"] = new JsonArray(last5.Select(s => (JsonNode)s).ToArray()),
            ["kosInLastFive"] = last5.Count(s => s == "WKO"),
            ["_unverified"] = true,
        };
    }

    // ----------------------------------------------------------------- //
    // Snapshot assembly
    // ----------------------------------------------------------------- //
    private static JsonObject Build(string title, string wikitext)
    {
        var p = ParseInfobox(wikitext);
        var needsCheck = new List<string>();
        // Display name without the "(boxer)" disambiguation; keep `title` for the URL.
        var displayName = Regex.Replace(title, @"\s*\([^)]*\)", "").Trim();

        var wins = ParseInt(p, "wins");
        var ko = ParseInt(p, "ko");
        var losses = ParseInt(p, "losses");
        var draws = ParseInt(p, "draws");
        var total = ParseInt(p, "total");
        var noContests = ParseInt(p, "no_contests", "no contests");
        int? winsDec = (wins is not null && ko is not null) ? wins - ko : null;
        if (total is not null && wins is not null && losses is not null && draws is not null)
        {
            var computed = wins + losses + draws + (noContests ?? 0);
            if (computed != total)
                needsCheck.Add($"total ({total}) != W+L+D+NC ({computed})");
        }

        var birthRaw = p.GetValueOrDefault("birth_date", "") + p.GetValueOrDefault("birth date", "");
        var weightClasses = ParseWeightClasses(
            p.GetValueOrDefault("weight", "").Length > 0 ? p["weight"] : p.GetValueOrDefault("division", ""));

        return new JsonObject
        {
            ["capturedAt"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ssK"),
            ["version"] = 1,
            ["record"] = new JsonObject
            {
                ["wins"] = wins, ["losses"] = losses, ["draws"] = draws,
                ["winsKo"] = ko, ["winsDec"] = winsDec, ["noContests"] = noContests,
            },
            ["form"] = ParseRecentForm(wikitext) ?? new JsonObject
            {
                ["currentStreak"] = null, ["streakType"] = null, ["last5"] = new JsonArray(),
                ["kosInLastFive"] = null, ["avgRoundsLastFive"] = null,
            },
            ["physical"] = new JsonObject
            {
                ["age"] = ParseAge(birthRaw),
                ["dob"] = ParseDob(birthRaw),
                ["heightInches"] = ParseLengthInches(p.GetValueOrDefault("height", "")),
                ["reachInches"] = ParseLengthInches(p.GetValueOrDefault("reach", "")),
                ["stance"] = ParseStance(p.GetValueOrDefault("stance", "").Length > 0 ? p["stance"] : p.GetValueOrDefault("style", "")),
            },
            ["standing"] = new JsonObject
            {
                // Wikipedia infoboxes don't carry sanctioning rankings — pull from
                // the WBC/WBO/IBF/WBA ratings pages later.
                ["titlesHeld"] = null,
                ["rankings"] = new JsonObject { ["wbc"] = null, ["wbo"] = null, ["ibf"] = null, ["wba"] = null },
            },
            ["_meta"] = new JsonObject
            {
                ["name"] = displayName,
                ["realName"] = NullIfEmpty(Clean(p.GetValueOrDefault("realname", "").Length > 0 ? p["realname"] : p.GetValueOrDefault("real_name", ""))),
                ["nationality"] = NullIfEmpty(Clean(p.GetValueOrDefault("nationality", ""))),
                ["weightClasses"] = weightClasses is null ? null : new JsonArray(weightClasses.Select(w => (JsonNode)w).ToArray()),
                ["hasWikipedia"] = true,
                ["source"] = $"https://en.wikipedia.org/wiki/{title.Replace(' ', '_')}",
                ["license"] = "Text CC BY-SA 4.0 — attribute Wikipedia on display.",
                ["needsVerification"] = needsCheck.Count > 0 ? new JsonArray(needsCheck.Select(s => (JsonNode)s).ToArray()) : null,
                ["formIsUnverified"] = true,
            },
        };
    }

    /// <summary>A minimal record for a fighter with no usable Wikipedia article. Stats are null; flagged hasWikipedia:false so downstream knows it is incomplete.</summary>
    private static JsonObject SparseSnapshot(string name) => new()
    {
        ["capturedAt"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ssK"),
        ["version"] = 1,
        ["record"] = new JsonObject
        {
            ["wins"] = null, ["losses"] = null, ["draws"] = null,
            ["winsKo"] = null, ["winsDec"] = null, ["noContests"] = null,
        },
        ["form"] = new JsonObject
        {
            ["currentStreak"] = null, ["streakType"] = null, ["last5"] = new JsonArray(),
            ["kosInLastFive"] = null, ["avgRoundsLastFive"] = null,
        },
        ["physical"] = new JsonObject
        {
            ["age"] = null, ["dob"] = null, ["heightInches"] = null,
            ["reachInches"] = null, ["stance"] = null,
        },
        ["standing"] = new JsonObject
        {
            ["titlesHeld"] = null,
            ["rankings"] = new JsonObject { ["wbc"] = null, ["wbo"] = null, ["ibf"] = null, ["wba"] = null },
        },
        ["_meta"] = new JsonObject
        {
            ["name"] = name,
            ["realName"] = null,
            ["nationality"] = null,
            ["weightClasses"] = null,
            ["hasWikipedia"] = false,
            ["source"] = null,
            ["license"] = null,
            ["needsVerification"] = new JsonArray("no Wikipedia article found — stats unknown"),
            ["formIsUnverified"] = true,
        },
    };

    private static string? NullIfEmpty(string s) => s.Length > 0 ? s : null;
}

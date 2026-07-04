using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace BritBoxingFeeds.Core.Enrichment;

/// <summary>
/// Finds a fight's scheduled date using Claude's server-side web search tool.
/// Feed announcements rarely carry a firm date, and an LLM must NEVER guess one
/// from memory — so this grounds every answer in a live web result and returns
/// a date only when the model cites a real source. No source / not found => null.
/// </summary>
public class FightDateEnricher
{
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string Model = "claude-sonnet-5";

    private readonly HttpClient _http;
    private readonly ILogger<FightDateEnricher> _logger;

    public FightDateEnricher(HttpClient http, ILogger<FightDateEnricher> logger)
    {
        _http = http;
        _logger = logger;
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? throw new InvalidOperationException("ANTHROPIC_API_KEY environment variable is not set.");
        _http.DefaultRequestHeaders.Clear();
        _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        // Web search + generation can be slow; give it headroom.
        _http.Timeout = TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// Outcome of a web-search enrichment. <see cref="Date"/> is null when no
    /// source-backed date could be verified. <see cref="Status"/> is the model's
    /// grounded read of whether the fight is officially confirmed: "confirmed"
    /// (signed/announced), "rumoured" (only talks/speculation), or "unknown"
    /// (sources didn't say either way — caller should not act on it).
    /// </summary>
    public record EnrichResult(string? Date, string? Venue, string? SourceUrl, string Confidence, string Status);

    /// <summary>
    /// Web-searches for the fight and returns both a grounded scheduled date
    /// (only when a source states one — never guessed) and a grounded verdict on
    /// whether the fight is officially confirmed vs merely rumoured. Returns null
    /// only on a hard API/parse failure.
    /// </summary>
    public async Task<EnrichResult?> FindDateAsync(string fighterA, string fighterB, string? weightClass, CancellationToken ct = default)
    {
        var div = string.IsNullOrWhiteSpace(weightClass) ? "" : $" ({weightClass})";
        var prompt =
            $"Search the web for the upcoming boxing fight between {fighterA} and {fighterB}{div}. " +
            "Use current, reliable sources (boxing press, promoters, Wikipedia). " +
            "Determine two things: (1) its scheduled date, if any source states one; " +
            "(2) whether the fight is OFFICIALLY confirmed (signed, or announced with both fighters agreed) " +
            "versus merely rumoured (talks, speculation, a mandatory being ordered, or just discussed).\n" +
            "Return ONLY a JSON object, no other text:\n" +
            "{\"date\": \"YYYY-MM-DD\" | null, \"venue\": string | null, \"sourceUrl\": string | null, " +
            "\"confidence\": \"high\" | \"medium\" | \"low\", \"officiallyConfirmed\": true | false | null}\n" +
            "Rules: give a date ONLY if a specific, reliable source states it — include that source's URL. " +
            "If you cannot find a specific scheduled date, return \"date\": null. Never guess or estimate a date. " +
            "Set officiallyConfirmed=true only if sources say it is signed/officially announced; " +
            "false if sources treat it as only rumoured/proposed/being discussed; null if sources don't make it clear.";

        var requestBody = new
        {
            model = Model,
            max_tokens = 1500,
            tools = new[]
            {
                new { type = "web_search_20260209", name = "web_search", max_uses = 3 }
            },
            messages = new[]
            {
                new { role = "user", content = prompt }
            }
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"),
            };
            var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Date search failed for {A} vs {B} [{Status}]: {Body}", fighterA, fighterB, response.StatusCode, err[..Math.Min(err.Length, 300)]);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            // Concatenate every text block; the model's JSON answer is in there
            // amongst the web_search_tool_result blocks.
            var text = string.Concat(doc.RootElement.GetProperty("content").EnumerateArray()
                .Where(b => b.TryGetProperty("type", out var t) && t.GetString() == "text")
                .Select(b => b.GetProperty("text").GetString()));

            var match = Regex.Match(text, @"\{.*\}", RegexOptions.Singleline);
            if (!match.Success) return null;

            var dto = JsonSerializer.Deserialize<DateDto>(match.Value, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (dto is null) return null;

            // Status verdict — safe to act on even when no date is found.
            var status = dto.OfficiallyConfirmed switch
            {
                true => "confirmed",
                false => "rumoured",
                null => "unknown",
            };

            var conf = (dto.Confidence ?? "").ToLowerInvariant();

            // A date is kept only when it's specific, high/medium-confidence and
            // cites a source — otherwise treat it as "no date found".
            string? date = null;
            if (dto.Date is not null
                && Regex.IsMatch(dto.Date, @"^\d{4}-\d{2}-\d{2}$")
                && (conf == "high" || conf == "medium")
                && !string.IsNullOrWhiteSpace(dto.SourceUrl))
            {
                date = dto.Date;
            }

            return new EnrichResult(date, dto.Venue, dto.SourceUrl, conf, status);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Date search errored for {A} vs {B}", fighterA, fighterB);
            return null;
        }
    }

    private class DateDto
    {
        public string? Date { get; set; }
        public string? Venue { get; set; }
        public string? SourceUrl { get; set; }
        public string? Confidence { get; set; }
        public bool? OfficiallyConfirmed { get; set; }
    }
}

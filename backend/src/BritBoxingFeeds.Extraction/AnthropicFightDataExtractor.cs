using System.Text;
using System.Text.Json;
using BritBoxingFeeds.Core.Models;
using Microsoft.Extensions.Logging;

namespace BritBoxingFeeds.Extraction;

/// <summary>
/// LLM fallback: extracts structured fight fields the regex pass couldn't
/// resolve, via Anthropic's Messages API over raw HTTP. Reads ANTHROPIC_API_KEY
/// from the environment and throws at construction if it's missing — by design,
/// the app fails fast rather than silently running regex-only. Swap in
/// RegexFightDataExtractor directly for a key-free local run.
/// </summary>
public class AnthropicFightDataExtractor : IFightDataExtractor
{
    // Cheap, fast model for high-volume extraction. Bump to claude-sonnet-4-6 /
    // claude-opus-4-8 for higher accuracy at higher cost.
    private const string Model = "claude-haiku-4-5";
    private const string Endpoint = "https://api.anthropic.com/v1/messages";

    private static readonly JsonSerializerOptions ReadOpts = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly ILogger<AnthropicFightDataExtractor> _logger;
    private readonly string _apiKey;

    public AnthropicFightDataExtractor(HttpClient http, ILogger<AnthropicFightDataExtractor> logger)
    {
        _http = http;
        _logger = logger;
        _apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? throw new InvalidOperationException(
                "ANTHROPIC_API_KEY is not set. Set it, or use RegexFightDataExtractor for a key-free run.");
    }

    public async Task<FightAnnouncement> ExtractAsync(FightAnnouncement raw, CancellationToken ct = default)
    {
        var requestBody = new
        {
            model = Model,
            max_tokens = 500,
            system = SystemPrompt,
            messages = new[]
            {
                new { role = "user", content = $"Headline: {raw.RawHeadline}\n\nArticle: {raw.ArticleBody ?? "(none)"}" }
            }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        req.Headers.Add("x-api-key", _apiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");
        req.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("Anthropic extraction failed [{Status}] for '{Headline}'",
                (int)resp.StatusCode, raw.RawHeadline);
            return raw;
        }

        var body = await resp.Content.ReadAsStringAsync(ct);
        var text = ExtractText(body);
        var dto = text is null ? null : TryParseDto(text);
        if (dto is null)
            return raw;

        var eventDate = DateTimeOffset.TryParse(dto.EventDate, out var parsed) ? parsed : raw.EventDate;

        return raw with
        {
            Fighter1 = dto.Fighter1 ?? raw.Fighter1,
            Fighter2 = dto.Fighter2 ?? raw.Fighter2,
            EventDate = eventDate,
            Venue = dto.Venue ?? raw.Venue,
            City = dto.City ?? raw.City,
            WeightClass = dto.WeightClass ?? raw.WeightClass,
            TitleOnTheLine = dto.TitleOnTheLine ?? raw.TitleOnTheLine,
            Broadcaster = dto.Broadcaster ?? raw.Broadcaster
        };
    }

    /// <summary>Pull the first text block out of the Messages API response.</summary>
    private static string? ExtractText(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);
        if (!doc.RootElement.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var block in content.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var t) && t.GetString() == "text"
                && block.TryGetProperty("text", out var txt))
                return txt.GetString();
        }
        return null;
    }

    private static ExtractedFightDataDto? TryParseDto(string text)
    {
        var json = text.Trim();
        // Strip a ```json ... ``` fence if the model wrapped its output.
        if (json.StartsWith("```"))
        {
            var first = json.IndexOf('{');
            var last = json.LastIndexOf('}');
            if (first >= 0 && last > first) json = json[first..(last + 1)];
        }
        try { return JsonSerializer.Deserialize<ExtractedFightDataDto>(json, ReadOpts); }
        catch { return null; }
    }

    private const string SystemPrompt =
        "You extract structured data about a boxing fight from a news headline and article body. " +
        "Only report a fight if the text describes a SPECIFIC upcoming or scheduled bout between two named boxers " +
        "(a bout being made, ordered, confirmed, signed or rescheduled). For results of past fights, rankings, " +
        "retirements or general news, return all nulls. Never guess: use null when the text does not state a field. " +
        "Return ONLY a JSON object with keys: fighter1, fighter2, eventDate (ISO 8601 date or null), venue, city, " +
        "weightClass, titleOnTheLine, broadcaster.";
}

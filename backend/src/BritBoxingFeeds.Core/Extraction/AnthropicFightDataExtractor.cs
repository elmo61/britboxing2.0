using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BritBoxingFeeds.Core.Models;
using Microsoft.Extensions.Logging;

namespace BritBoxingFeeds.Extraction;

/// <summary>
/// Calls the Anthropic Messages API to pull structured fight data out of
/// headline + article body text. Used for anything the cheap regex pass
/// (RegexFightDataExtractor) couldn't fill in — handles relative dates
/// ("this Saturday"), venue/city mentioned in body text rather than the
/// headline, weight class, title implications, broadcaster, etc.
///
/// Requires an ANTHROPIC_API_KEY environment variable. Get one at
/// https://console.anthropic.com — never hardcode the key in source.
/// </summary>
public class AnthropicFightDataExtractor : IFightDataExtractor
{
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string Model = "claude-sonnet-5";

    private readonly HttpClient _http;
    private readonly ILogger<AnthropicFightDataExtractor> _logger;

    public AnthropicFightDataExtractor(HttpClient http, ILogger<AnthropicFightDataExtractor> logger)
    {
        _http = http;
        _logger = logger;

        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? throw new InvalidOperationException("ANTHROPIC_API_KEY environment variable is not set.");

        _http.DefaultRequestHeaders.Clear();
        _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<FightAnnouncement> ExtractAsync(FightAnnouncement raw, CancellationToken ct = default)
    {
        var prompt = BuildPrompt(raw);

        var requestBody = new
        {
            model = Model,
            max_tokens = 500,
            // Sonnet 5 defaults to adaptive thinking when this is omitted —
            // unneeded for a simple field extraction, and it costs output tokens.
            thinking = new { type = "disabled" },
            messages = new[]
            {
                new { role = "user", content = prompt }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
        };

        try
        {
            var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(responseJson);

            // The content array can carry non-text blocks (e.g. thinking, when
            // enabled) ahead of the answer — take the first text block, not [0].
            var text = doc.RootElement
                .GetProperty("content")
                .EnumerateArray()
                .Where(b => b.GetProperty("type").GetString() == "text")
                .Select(b => b.GetProperty("text").GetString())
                .FirstOrDefault() ?? "";

            var cleaned = text.Replace("```json", "").Replace("```", "").Trim();
            var dto = JsonSerializer.Deserialize<ExtractedFightDataDto>(cleaned, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (dto is null)
            {
                _logger.LogWarning("Extractor: model response did not parse as JSON for '{Headline}'", raw.RawHeadline);
                return raw;
            }

            DateTimeOffset? eventDate = null;
            if (!string.IsNullOrWhiteSpace(dto.EventDate) && DateTimeOffset.TryParse(dto.EventDate, out var parsed))
            {
                eventDate = parsed;
            }

            return raw with
            {
                Fighter1 = dto.Fighter1 ?? raw.Fighter1,
                Fighter2 = dto.Fighter2 ?? raw.Fighter2,
                EventDate = eventDate ?? raw.EventDate,
                Venue = dto.Venue ?? raw.Venue,
                City = dto.City ?? raw.City,
                WeightClass = dto.WeightClass ?? raw.WeightClass,
                TitleOnTheLine = dto.TitleOnTheLine ?? raw.TitleOnTheLine,
                Broadcaster = dto.Broadcaster ?? raw.Broadcaster,
                IsUpcoming = dto.IsUpcoming ?? raw.IsUpcoming,
                FightStatus = dto.Status ?? raw.FightStatus
            };
        }
        catch (Exception ex)
        {
            // Extraction failing for one item shouldn't kill the batch —
            // return the unenriched raw record so it still flows through
            // the pipeline (just with nulls where the LLM would've filled in).
            _logger.LogWarning(ex, "Extractor: failed for '{Headline}'", raw.RawHeadline);
            return raw;
        }
    }

    private static string BuildPrompt(FightAnnouncement raw)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Extract British boxing fight details from the text below. Return ONLY a JSON object, no other text, no markdown fences.");
        sb.AppendLine();
        sb.AppendLine("Schema (all fields nullable — use null if the text doesn't actually state it, never guess):");
        sb.AppendLine("""
        {
          "fighter1": string | null,
          "fighter2": string | null,
          "eventDate": string | null,   // ISO 8601 date e.g. "2026-07-20", null if no specific date given
          "venue": string | null,
          "city": string | null,
          "weightClass": string | null,
          "titleOnTheLine": string | null,  // e.g. "WBC", "IBF", null if not a title fight
          "broadcaster": string | null,     // e.g. "DAZN", "Sky Sports", null if not mentioned
          "isUpcoming": boolean | null,     // true = an announced/scheduled fight; false = a report/result of a fight that already happened; null = can't tell
          "status": string | null           // "confirmed" = officially announced/signed; "rumoured" = talks/speculation/being ordered; "cancelled" = called off/postponed; null = can't tell
        }
        """);
        sb.AppendLine();
        sb.AppendLine($"Headline: {raw.RawHeadline}");
        if (!string.IsNullOrWhiteSpace(raw.ArticleBody))
        {
            sb.AppendLine($"Article text: {raw.ArticleBody}");
        }

        return sb.ToString();
    }
}

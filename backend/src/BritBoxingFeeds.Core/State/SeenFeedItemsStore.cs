using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using BritBoxingFeeds.Core.Models;
using Microsoft.Extensions.Logging;

namespace BritBoxingFeeds.Core.State;

/// <summary>
/// Tracks which RSS items have already been through extraction in a prior
/// run, backed by the `seen_feed_items` Supabase table (service-role key
/// only — never the public site's key). Lets the App skip AI extraction on
/// items it has processed before, so the same headline/URL reappearing in a
/// feed doesn't cost another LLM call. Falls back to "nothing is seen" (no
/// filtering) if Supabase isn't configured or unreachable, so the app still
/// runs standalone without a DB and a Supabase outage degrades to
/// "reprocess everything" rather than silently dropping items.
/// </summary>
public class SeenFeedItemsStore
{
    private const string TableName = "seen_feed_items";

    private readonly HttpClient _http;
    private readonly ILogger<SeenFeedItemsStore> _logger;
    private readonly string? _baseUrl;
    private readonly string? _secretKey;

    public bool Enabled => _baseUrl is not null && _secretKey is not null;

    public SeenFeedItemsStore(HttpClient http, ILogger<SeenFeedItemsStore> logger)
    {
        _http = http;
        _logger = logger;
        _baseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL")?.TrimEnd('/');
        _secretKey = Environment.GetEnvironmentVariable("SUPABASE_SECRET_KEY");
    }

    /// <summary>
    /// The item's identity for "have we processed this before" purposes.
    /// Prefers a semantic key — normalized fighter-pair + event month/year —
    /// so the same fight reported under a different URL, or with its exact
    /// date firmed up later, is still recognized as the same fight rather
    /// than costing another LLM call. Using month/year rather than the exact
    /// date also means a date correction (announced 20th, confirmed 21st)
    /// doesn't look like a new fight. The same two fighters meeting again
    /// months apart gets a different key, since the month/year differs.
    /// Falls back to the raw item's URL (or a hash of source+headline) when
    /// fighter names aren't known yet, e.g. before the regex pass has run,
    /// or a headline regex/the LLM never resolves to two names.
    ///
    /// Each item is recorded under BOTH keys (see MarkSeenAsync), and an item
    /// counts as seen if ANY of its keys is — otherwise an item whose fighters
    /// only the LLM could extract would be re-paid-for every run, because the
    /// pre-LLM regex check can never reproduce its fight: key.
    /// </summary>
    public static IReadOnlyList<string> ComputeKeys(FightAnnouncement item)
    {
        var keys = new List<string> { $"item:{ComputeItemKey(item)}" };

        if (!string.IsNullOrWhiteSpace(item.Fighter1) && !string.IsNullOrWhiteSpace(item.Fighter2))
        {
            var pair = NormalizedPairKey(item.Fighter1, item.Fighter2);
            var monthYear = item.EventDate?.ToString("yyyy-MM") ?? "unknown-date";
            keys.Add($"fight:{pair}|{monthYear}");
        }

        return keys;
    }

    private static string ComputeItemKey(FightAnnouncement item)
    {
        if (!string.IsNullOrWhiteSpace(item.SourceUrl))
        {
            return item.SourceUrl.Trim().ToLowerInvariant();
        }

        var basis = $"{item.SourceName}|{item.RawHeadline}".Trim().ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(basis));
        return Convert.ToHexString(hash);
    }

    private static string NormalizedPairKey(string fighter1, string fighter2)
    {
        var names = new[] { NormalizeName(fighter1), NormalizeName(fighter2) };
        Array.Sort(names, StringComparer.Ordinal);
        return string.Join("|", names);
    }

    private static string NormalizeName(string name) =>
        name.Trim().ToLowerInvariant().Replace(".", "").Replace("-", " ");

    /// <summary>
    /// Verifies Supabase is actually reachable before the run does anything
    /// else — there's no point spending time on RSS/LLM work if the results
    /// (and this dedup table) can't be persisted afterwards anyway.
    /// </summary>
    public async Task<bool> CheckConnectionAsync(CancellationToken ct = default)
    {
        if (!Enabled)
        {
            _logger.LogError("SUPABASE_URL / SUPABASE_SECRET_KEY are not set.");
            return false;
        }

        try
        {
            using var request = BuildRequest(HttpMethod.Get, $"{_baseUrl}/rest/v1/{TableName}?select=item_key&limit=1");
            var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Supabase connectivity check failed [{Status}]: {Body}", response.StatusCode, body);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Supabase connectivity check failed");
            return false;
        }
    }

    /// <summary>
    /// Loads item keys first seen within <paramref name="lookback"/>, plus the
    /// most recent first-seen timestamp (used as the "last run" marker for the
    /// grace-period cutoff in Program.cs).
    /// </summary>
    public async Task<SeenItemsSnapshot> LoadRecentAsync(TimeSpan lookback, CancellationToken ct = default)
    {
        if (!Enabled) return SeenItemsSnapshot.Empty;

        var cutoff = Uri.EscapeDataString((DateTimeOffset.UtcNow - lookback).ToString("o"));
        var url = $"{_baseUrl}/rest/v1/{TableName}?select=item_key,first_seen_at&first_seen_at=gte.{cutoff}";

        try
        {
            using var request = BuildRequest(HttpMethod.Get, url);
            var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var rows = await response.Content.ReadFromJsonAsync<List<SeenRow>>(cancellationToken: ct) ?? [];
            var keys = rows.Select(r => r.ItemKey).ToHashSet();
            var lastSeenAt = rows.Count > 0 ? rows.Max(r => r.FirstSeenAt) : (DateTimeOffset?)null;
            return new SeenItemsSnapshot(keys, lastSeenAt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SeenFeedItemsStore: failed to load seen items, treating everything as new this run");
            return SeenItemsSnapshot.Empty;
        }
    }

    /// <summary>Upserts every item as seen, regardless of what extraction found — noise items shouldn't be reprocessed either.</summary>
    public async Task MarkSeenAsync(IReadOnlyList<FightAnnouncement> items, CancellationToken ct = default)
    {
        if (!Enabled || items.Count == 0) return;

        // One row per key (URL key + fight key where known). Distinct by key:
        // two sources reporting the same fight map to the same fight: key, and
        // PostgREST rejects a batch touching one key twice.
        var rows = items
            .SelectMany(item => ComputeKeys(item).Select(key => new SeenRowUpsert(
                key, item.SourceName, item.RawHeadline, item.SourceUrl, item.PublishedAt)))
            .DistinctBy(r => r.ItemKey)
            .ToList();

        try
        {
            using var request = BuildRequest(HttpMethod.Post, $"{_baseUrl}/rest/v1/{TableName}?on_conflict=item_key");
            request.Content = JsonContent.Create(rows);
            request.Headers.Add("Prefer", "resolution=merge-duplicates,return=minimal");

            var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("SeenFeedItemsStore: upsert failed [{Status}]: {Body}", response.StatusCode, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SeenFeedItemsStore: failed to mark items as seen");
        }
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("apikey", _secretKey);
        request.Headers.Add("Authorization", $"Bearer {_secretKey}");
        return request;
    }

    private record SeenRow(
        [property: JsonPropertyName("item_key")] string ItemKey,
        [property: JsonPropertyName("first_seen_at")] DateTimeOffset FirstSeenAt);

    private record SeenRowUpsert(
        [property: JsonPropertyName("item_key")] string ItemKey,
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("headline")] string Headline,
        [property: JsonPropertyName("source_url")] string SourceUrl,
        [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt);
}

public record SeenItemsSnapshot(IReadOnlySet<string> Keys, DateTimeOffset? LastRunAt)
{
    public static readonly SeenItemsSnapshot Empty = new(new HashSet<string>(), null);

    public bool Contains(string key) => Keys.Contains(key);
}

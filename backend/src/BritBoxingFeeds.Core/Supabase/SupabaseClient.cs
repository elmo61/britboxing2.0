using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BritBoxingFeeds.Core.Supabase;

/// <summary>
/// Thin PostgREST client for the pipeline's Supabase writes, mirroring
/// pipeline/supabase_db.py. Uses the service-role secret key (writes bypass
/// the public read-only RLS), so this must never be used from anything
/// user-facing. Rows are JsonObjects keyed by column name — the callers own
/// the row shapes, this owns auth/upsert mechanics.
/// </summary>
public class SupabaseClient
{
    private readonly HttpClient _http;
    private readonly string? _baseUrl;
    private readonly string? _secretKey;

    public bool Enabled => _baseUrl is not null && _secretKey is not null;

    public SupabaseClient(HttpClient http)
    {
        _http = http;
        _baseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL")?.TrimEnd('/');
        _secretKey = Environment.GetEnvironmentVariable("SUPABASE_SECRET_KEY");
    }

    /// <summary>GET rows; <paramref name="query"/> is a raw PostgREST query string, e.g. "select=*&amp;status=eq.new".</summary>
    public async Task<JsonArray> SelectAsync(string table, string query, CancellationToken ct = default)
    {
        EnsureEnabled();
        using var request = BuildRequest(HttpMethod.Get, $"{_baseUrl}/rest/v1/{table}?{query}");
        var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, $"select {table}", ct);
        return await response.Content.ReadFromJsonAsync<JsonArray>(cancellationToken: ct) ?? [];
    }

    /// <summary>Bulk upsert (Prefer: merge-duplicates). PostgREST needs every row to carry the same keys, so rows are normalised to the union.</summary>
    public async Task UpsertAsync(string table, IReadOnlyList<JsonObject> rows, string onConflict, CancellationToken ct = default)
    {
        EnsureEnabled();
        if (rows.Count == 0) return;

        var keys = rows.SelectMany(r => r.Select(kv => kv.Key)).ToHashSet();
        var normalised = new JsonArray(rows.Select(r =>
        {
            var row = new JsonObject();
            foreach (var key in keys)
            {
                row[key] = r.TryGetPropertyValue(key, out var v) ? v?.DeepClone() : null;
            }
            return (JsonNode)row;
        }).ToArray());

        using var request = BuildRequest(HttpMethod.Post, $"{_baseUrl}/rest/v1/{table}?on_conflict={onConflict}");
        request.Content = new StringContent(normalised.ToJsonString(), Encoding.UTF8, "application/json");
        request.Headers.Add("Prefer", "resolution=merge-duplicates,return=minimal");

        var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, $"upsert {table}", ct);
    }

    /// <summary>PATCH the rows matching <paramref name="filter"/> (raw PostgREST filter, e.g. "item_key=eq.abc").</summary>
    public async Task UpdateAsync(string table, string filter, JsonObject patch, CancellationToken ct = default)
    {
        EnsureEnabled();
        using var request = BuildRequest(HttpMethod.Patch, $"{_baseUrl}/rest/v1/{table}?{filter}");
        request.Content = new StringContent(patch.ToJsonString(), Encoding.UTF8, "application/json");
        request.Headers.Add("Prefer", "return=minimal");

        var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, $"update {table}", ct);
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("apikey", _secretKey);
        request.Headers.Add("Authorization", $"Bearer {_secretKey}");
        return request;
    }

    private void EnsureEnabled()
    {
        if (!Enabled)
            throw new InvalidOperationException("SUPABASE_URL / SUPABASE_SECRET_KEY are not set.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException($"supabase {operation} failed [{(int)response.StatusCode}]: {body[..Math.Min(body.Length, 300)]}");
    }
}

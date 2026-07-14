using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace BritBoxingFeeds.Core.Articles;

/// <summary>
/// Best-effort fetch of the news article a feed item links to, so the writer
/// can work from the actual story (quotes, stakes, context) instead of the
/// one-line RSS summary. The text is used as generation CONTEXT only — never
/// republished. Strictly best-effort: paywalls, consent walls, 403s and
/// timeouts all just mean "no full text", and the caller falls back to the
/// RSS summary. Controlled by the FETCH_SOURCE_PAGES env var (unset/1 = on,
/// 0 = off) so the scheduled pipeline can disable it independently of code.
/// </summary>
public class SourcePageFetcher
{
    private const int MaxBytes = 500_000;
    private const int MaxChars = 3_000;

    private readonly HttpClient _http;
    private readonly ILogger<SourcePageFetcher> _logger;

    public static bool Enabled =>
        Environment.GetEnvironmentVariable("FETCH_SOURCE_PAGES") is not "0";

    public SourcePageFetcher(HttpClient http, ILogger<SourcePageFetcher> logger)
    {
        _http = http;
        _logger = logger;
        _http.Timeout = TimeSpan.FromSeconds(10);
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("BritBoxingBot/1.0 (+https://britboxing.co.uk)");
        }
    }

    /// <summary>Plain text of the page at <paramref name="url"/>, or null when anything at all goes wrong.</summary>
    public async Task<string?> FetchTextAsync(string url, CancellationToken ct = default)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Source page fetch skipped ({Status}) for {Url}", (int)response.StatusCode, url);
                return null;
            }

            // Read at most MaxBytes — news pages are front-loaded and the
            // writer only ever sees the first few thousand characters anyway.
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);
            var buffer = new char[MaxBytes];
            var read = await reader.ReadBlockAsync(buffer, ct);
            var html = new string(buffer, 0, read);

            return ExtractText(html);
        }
        catch (Exception ex)
        {
            _logger.LogInformation("Source page fetch failed for {Url}: {Reason}", url, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Crude but dependency-free HTML → text: drop script/style/nav chrome,
    /// prefer the &lt;article&gt; element when the page has one, then strip tags.
    /// </summary>
    internal static string? ExtractText(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;

        // The <article> element, when present, skips most nav/footer noise.
        var articleMatch = Regex.Match(html, @"<article[\s>].*?</article>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (articleMatch.Success) html = articleMatch.Value;

        html = Regex.Replace(html, @"<(script|style|noscript|svg|nav|header|footer|aside|form)[\s>].*?</\1>", " ",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<!--.*?-->", " ", RegexOptions.Singleline);
        var text = Regex.Replace(html, @"<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ").Trim();

        if (text.Length < 200) return null; // consent wall / empty shell, not an article
        return text.Length > MaxChars ? text[..MaxChars] : text;
    }
}

using System.Text.RegularExpressions;
using System.Xml.Linq;
using BritBoxingFeeds.Core.Interfaces;
using BritBoxingFeeds.Core.Models;
using Microsoft.Extensions.Logging;

namespace BritBoxingFeeds.Sources;

/// <summary>
/// The Ring's site (ringmagazine.com) is a Next.js app with no RSS/Atom feed,
/// so this is not an RssFightSourceBase like the others. It has a public,
/// robots.txt-permitted sitemap that updates within minutes of publishing
/// (https://www.ringmagazine.com/api/sitemap/sitemap-articles-1) — exactly
/// the discovery mechanism sitemaps exist for. That sitemap gives only a URL
/// and a last-modified timestamp, no title, so each recent article's page is
/// fetched (a bounded prefix — the &lt;title&gt;/og:description meta sits in the
/// first ~10KB of a ~1.6MB page) to pull a real headline and summary.
/// </summary>
public class RingMagazineSource : IFightSource
{
    public string SourceName => "The Ring";

    private const string SitemapUrl = "https://www.ringmagazine.com/api/sitemap/sitemap-articles-1";

    // How far back to look for "new" articles by the sitemap's lastmod. Wider
    // than the 3-hourly run cadence so a missed run or two doesn't drop
    // anything — the pipeline's own seen-items dedup handles re-fetches
    // cheaply (skipped before extraction, no extra LLM cost).
    private static readonly TimeSpan LookbackWindow = TimeSpan.FromHours(48);

    // The page is ~1.6MB (Next.js bundle); the head meta tags sit in the
    // first ~10KB. Read a bounded prefix rather than the whole page.
    private const int ArticlePrefixChars = 30_000;

    private static readonly Regex TitleTag = new(@"<title>([^<]*)</title>", RegexOptions.Compiled);
    private static readonly Regex OgDescriptionTag = new(
        @"<meta property=""og:description"" content=""([^""]*)""", RegexOptions.Compiled);

    private readonly HttpClient _http;
    private readonly ILogger<RingMagazineSource> _logger;

    public RingMagazineSource(HttpClient http, ILogger<RingMagazineSource> logger)
    {
        _http = http;
        _logger = logger;
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("BritBoxingBot/1.0 (+https://britboxing.co.uk)");
        }
    }

    public async Task<IReadOnlyList<FightAnnouncement>> GetLatestFightsAsync(CancellationToken ct = default)
    {
        var sitemapXml = await _http.GetStringAsync(SitemapUrl, ct);
        var doc = XDocument.Parse(sitemapXml);
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";

        var cutoff = DateTimeOffset.UtcNow - LookbackWindow;
        var recent = doc.Root?.Elements(ns + "url")
            .Select(u => new
            {
                Loc = u.Element(ns + "loc")?.Value,
                LastMod = DateTimeOffset.TryParse(u.Element(ns + "lastmod")?.Value, out var d) ? d : (DateTimeOffset?)null,
            })
            .Where(u => u.Loc is not null && u.LastMod is not null && u.LastMod >= cutoff)
            .ToList() ?? [];

        var results = new List<FightAnnouncement>();
        foreach (var item in recent)
        {
            ct.ThrowIfCancellationRequested();
            var article = await FetchArticleMetaAsync(item.Loc!, item.LastMod!.Value, ct);
            if (article is not null) results.Add(article);
        }

        return results;
    }

    private async Task<FightAnnouncement?> FetchArticleMetaAsync(string url, DateTimeOffset publishedAt, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);
            var buffer = new char[ArticlePrefixChars];
            var read = await reader.ReadBlockAsync(buffer, ct);
            var html = new string(buffer, 0, read);

            var titleMatch = TitleTag.Match(html);
            if (!titleMatch.Success) return null;

            var title = System.Net.WebUtility.HtmlDecode(titleMatch.Groups[1].Value).Trim();
            title = Regex.Replace(title, @"\s*\|\s*The Ring\s*$", ""); // strip the site-name suffix

            var descMatch = OgDescriptionTag.Match(html);
            var summary = descMatch.Success ? System.Net.WebUtility.HtmlDecode(descMatch.Groups[1].Value).Trim() : null;

            return new FightAnnouncement
            {
                SourceName = SourceName,
                SourceUrl = url,
                RawHeadline = title,
                ArticleBody = summary,
                PublishedAt = publishedAt,
                RetrievedAt = DateTimeOffset.UtcNow,
            };
        }
        catch (Exception ex)
        {
            // One broken article page shouldn't drop the rest of this run's batch.
            _logger.LogInformation("Ring Magazine: failed to read {Url}: {Reason}", url, ex.Message);
            return null;
        }
    }
}

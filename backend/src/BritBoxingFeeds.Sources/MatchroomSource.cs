using AngleSharp;
using BritBoxingFeeds.Core.Interfaces;
using BritBoxingFeeds.Core.Models;

namespace BritBoxingFeeds.Sources;

/// <summary>
/// Scrapes the Matchroom Boxing news listing page. Matchroom has no RSS feed,
/// so this is HTML-scrape based and WILL break if their markup changes —
/// the CSS selectors below are placeholders and need to be checked/updated
/// against the live site before relying on this. Wrapped in try/catch at the
/// aggregator level so a broken selector here doesn't take down the pipeline.
///
/// NOTE: BritBoxing policy (see ../../../CLAUDE.md) is NOT to scrape promoter
/// sites — news RSS is the announcement trigger. Kept here for completeness /
/// the example's shape, but it is not registered by default in Program.cs.
/// </summary>
public class MatchroomSource : IFightSource
{
    public string SourceName => "Matchroom Boxing";

    private const string NewsUrl = "https://www.matchroomboxing.com/news";

    private readonly HttpClient _http;
    private readonly IBrowsingContext _browser;

    public MatchroomSource(HttpClient http)
    {
        _http = http;
        _browser = BrowsingContext.New(Configuration.Default);
    }

    public async Task<IReadOnlyList<FightAnnouncement>> GetLatestFightsAsync(CancellationToken ct = default)
    {
        var html = await _http.GetStringAsync(NewsUrl, ct);
        var doc = await _browser.OpenAsync(req => req.Content(html), ct);

        var results = new List<FightAnnouncement>();

        // TODO: confirm real selector — placeholder assumes news cards have class
        // "news-card" with a ".title" element and an <a> link.
        foreach (var card in doc.QuerySelectorAll(".news-card"))
        {
            var titleEl = card.QuerySelector(".title");
            var linkEl = card.QuerySelector("a");

            var title = titleEl?.TextContent.Trim();
            if (string.IsNullOrWhiteSpace(title))
                continue;

            var href = linkEl?.GetAttribute("href") ?? "";
            var fullUrl = href.StartsWith("http") ? href : new Uri(new Uri(NewsUrl), href).ToString();

            results.Add(new FightAnnouncement
            {
                SourceName = SourceName,
                RawHeadline = title,
                SourceUrl = fullUrl,
                RetrievedAt = DateTimeOffset.UtcNow
            });
        }

        return results;
    }
}

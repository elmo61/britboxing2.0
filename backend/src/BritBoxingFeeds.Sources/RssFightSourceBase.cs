using System.ServiceModel.Syndication;
using System.Xml;
using BritBoxingFeeds.Core.Interfaces;
using BritBoxingFeeds.Core.Models;

namespace BritBoxingFeeds.Sources;

/// <summary>
/// Shared logic for any source that's a plain RSS/Atom feed. Concrete classes
/// just supply the feed URL and source name. Fighter/date/venue fields are
/// left null here on purpose — RSS headlines are free text and get resolved
/// by the downstream extraction step, not by the source itself.
/// </summary>
public abstract class RssFightSourceBase : IFightSource
{
    private readonly HttpClient _http;
    private readonly string _feedUrl;

    public abstract string SourceName { get; }

    protected RssFightSourceBase(HttpClient http, string feedUrl)
    {
        _http = http;
        _feedUrl = feedUrl;
    }

    public virtual async Task<IReadOnlyList<FightAnnouncement>> GetLatestFightsAsync(CancellationToken ct = default)
    {
        await using var stream = await _http.GetStreamAsync(_feedUrl, ct);
        using var reader = XmlReader.Create(stream);
        var feed = SyndicationFeed.Load(reader);

        return feed.Items.Select(item => new FightAnnouncement
        {
            SourceName = SourceName,
            RawHeadline = item.Title?.Text ?? "",
            SourceUrl = item.Links.FirstOrDefault()?.Uri?.ToString() ?? "",
            ArticleBody = item.Summary?.Text,
            RetrievedAt = DateTimeOffset.UtcNow
        }).ToList();
    }
}

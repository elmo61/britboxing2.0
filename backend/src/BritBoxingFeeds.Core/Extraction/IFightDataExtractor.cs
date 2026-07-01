using BritBoxingFeeds.Core.Models;

namespace BritBoxingFeeds.Extraction;

/// <summary>
/// Takes a FightAnnouncement that only has RawHeadline/ArticleBody filled in
/// (what every IFightSource produces) and returns a copy with Fighter1,
/// Fighter2, EventDate, Venue, City, WeightClass etc filled in wherever the
/// text actually supports it. Implementations should leave a field null
/// rather than guess — a null is honest, a wrong value pollutes the
/// database and the dedup step downstream.
/// </summary>
public interface IFightDataExtractor
{
    Task<FightAnnouncement> ExtractAsync(FightAnnouncement raw, CancellationToken ct = default);
}

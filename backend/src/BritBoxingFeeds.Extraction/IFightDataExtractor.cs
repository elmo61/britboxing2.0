using BritBoxingFeeds.Core.Models;

namespace BritBoxingFeeds.Extraction;

/// <summary>
/// Takes a raw FightAnnouncement (headline / article body) and returns an
/// enriched copy with structured fields (fighters, date, venue, ...) filled
/// in wherever they can be resolved.
/// </summary>
public interface IFightDataExtractor
{
    Task<FightAnnouncement> ExtractAsync(FightAnnouncement raw, CancellationToken ct = default);
}

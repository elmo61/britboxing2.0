using BritBoxingFeeds.Core.Models;

namespace BritBoxingFeeds.Deduplication;

/// <summary>
/// Merges records that describe the same fight (reported by multiple sources)
/// into single entries. Takes the full list in, returns the deduped list.
/// </summary>
public interface IFightDeduplicator
{
    Task<IReadOnlyList<FightAnnouncement>> DeduplicateAsync(
        IReadOnlyList<FightAnnouncement> items,
        CancellationToken ct = default);
}

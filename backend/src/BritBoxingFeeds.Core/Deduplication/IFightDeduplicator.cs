using BritBoxingFeeds.Core.Models;

namespace BritBoxingFeeds.Deduplication;

/// <summary>
/// Takes the full collected/extracted set of FightAnnouncements (which will
/// contain the same real-world fight reported separately by BBC, BoxingScene,
/// Matchroom etc) and merges duplicates into single records, populating
/// MergedFromSources/AllSourceUrls on the survivor.
/// </summary>
public interface IFightDeduplicator
{
    Task<IReadOnlyList<FightAnnouncement>> DeduplicateAsync(
        IReadOnlyList<FightAnnouncement> items,
        CancellationToken ct = default);
}

using BritBoxingFeeds.Core.Models;

namespace BritBoxingFeeds.Core.Interfaces;

/// <summary>
/// Implemented once per upstream source (BBC RSS, Matchroom scraper, etc).
/// Each implementation is responsible for fetching its own raw content and
/// mapping it into the normalized FightAnnouncement shape.
/// </summary>
public interface IFightSource
{
    string SourceName { get; }

    Task<IReadOnlyList<FightAnnouncement>> GetLatestFightsAsync(CancellationToken ct = default);
}

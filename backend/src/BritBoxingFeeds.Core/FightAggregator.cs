using BritBoxingFeeds.Core.Interfaces;
using BritBoxingFeeds.Core.Models;
using Microsoft.Extensions.Logging;

namespace BritBoxingFeeds.Core;

/// <summary>
/// Fans out to every registered IFightSource in parallel. A failure in one
/// source is logged and skipped rather than failing the whole run — RSS
/// feeds and scrapers both go down independently and often.
/// </summary>
public class FightAggregator
{
    private readonly IEnumerable<IFightSource> _sources;
    private readonly ILogger<FightAggregator> _logger;

    public FightAggregator(IEnumerable<IFightSource> sources, ILogger<FightAggregator> logger)
    {
        _sources = sources;
        _logger = logger;
    }

    public async Task<IReadOnlyList<FightAnnouncement>> CollectAllAsync(CancellationToken ct = default)
    {
        var tasks = _sources.Select(async source =>
        {
            try
            {
                var results = await source.GetLatestFightsAsync(ct);
                _logger.LogInformation("{Source}: retrieved {Count} items", source.SourceName, results.Count);
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{Source}: failed, skipping this run", source.SourceName);
                return (IReadOnlyList<FightAnnouncement>)Array.Empty<FightAnnouncement>();
            }
        });

        var results = await Task.WhenAll(tasks);
        return results.SelectMany(r => r).ToList();
    }
}

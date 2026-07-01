using BritBoxingFeeds.Core.Models;

namespace BritBoxingFeeds.Deduplication;

/// <summary>
/// Groups records by normalized fighter-name pair (order-independent — "A vs B"
/// matches "B vs A") plus a date tolerance window, then merges each group into
/// one record. Records missing either fighter name are passed through
/// unmerged, since matching on names is the only reliable signal this has —
/// without both names there's nothing trustworthy to group on.
///
/// Limitations worth knowing about:
/// - Name matching is exact-after-normalization (lowercased, punctuation
///   stripped). It will NOT match "Tyson Fury" to "T. Fury" or catch
///   nicknames/spelling variants across sources. Good enough for sources
///   that use full fighter names consistently (BBC, BoxingScene etc
///   generally do); will under-merge if a source abbreviates names.
/// - If neither record in a potential pair has an EventDate, they're merged
///   on name match alone — fine for one-off announcements, but two
///   genuinely separate fights between the same pair (a rematch months
///   later) with no date info on either record would incorrectly merge.
///   In practice this is rare since most fight announcements include a date.
/// </summary>
public class FightDeduplicator : IFightDeduplicator
{
    private readonly TimeSpan _dateTolerance;

    /// <param name="dateTolerance">
    /// How far apart two EventDates can be and still be considered the same
    /// fight — accounts for sources reporting slightly different dates
    /// (timezone differences, "fight week" vs exact date). Defaults to 2 days.
    /// </param>
    public FightDeduplicator(TimeSpan? dateTolerance = null)
    {
        _dateTolerance = dateTolerance ?? TimeSpan.FromDays(2);
    }

    public Task<IReadOnlyList<FightAnnouncement>> DeduplicateAsync(
        IReadOnlyList<FightAnnouncement> items,
        CancellationToken ct = default)
    {
        var matchable = items.Where(i => i.Fighter1 is not null && i.Fighter2 is not null).ToList();
        var unmatchable = items.Where(i => i.Fighter1 is null || i.Fighter2 is null).ToList();

        var groups = new List<List<FightAnnouncement>>();

        foreach (var item in matchable.OrderBy(i => i.RetrievedAt))
        {
            var key = NormalizedPairKey(item.Fighter1!, item.Fighter2!);

            var group = groups.FirstOrDefault(g =>
            {
                var representative = g[0];
                if (NormalizedPairKey(representative.Fighter1!, representative.Fighter2!) != key)
                    return false;

                if (item.EventDate.HasValue && representative.EventDate.HasValue)
                {
                    return (item.EventDate.Value - representative.EventDate.Value).Duration() <= _dateTolerance;
                }

                // One or both dates missing — merge on name match alone (see class summary).
                return true;
            });

            if (group is not null)
                group.Add(item);
            else
                groups.Add(new List<FightAnnouncement> { item });
        }

        var merged = groups.Select(MergeGroup).Concat(unmatchable).ToList();
        return Task.FromResult<IReadOnlyList<FightAnnouncement>>(merged);
    }

    private static string NormalizedPairKey(string fighter1, string fighter2)
    {
        var names = new[] { Normalize(fighter1), Normalize(fighter2) };
        Array.Sort(names, StringComparer.Ordinal);
        return string.Join("|", names);
    }

    private static string Normalize(string name) =>
        name.Trim().ToLowerInvariant().Replace(".", "").Replace("-", " ");

    private static FightAnnouncement MergeGroup(List<FightAnnouncement> group)
    {
        if (group.Count == 1)
            return group[0];

        // Earliest-reported record forms the base; fields are then backfilled
        // from later records wherever the base record left them null.
        var primary = group.OrderBy(g => g.RetrievedAt).First();

        return primary with
        {
            Fighter1 = group.Select(g => g.Fighter1).FirstOrDefault(v => v is not null),
            Fighter2 = group.Select(g => g.Fighter2).FirstOrDefault(v => v is not null),
            EventDate = group.Select(g => g.EventDate).FirstOrDefault(v => v.HasValue),
            Venue = group.Select(g => g.Venue).FirstOrDefault(v => v is not null),
            City = group.Select(g => g.City).FirstOrDefault(v => v is not null),
            WeightClass = group.Select(g => g.WeightClass).FirstOrDefault(v => v is not null),
            TitleOnTheLine = group.Select(g => g.TitleOnTheLine).FirstOrDefault(v => v is not null),
            Broadcaster = group.Select(g => g.Broadcaster).FirstOrDefault(v => v is not null),
            MergedFromSources = group.Select(g => g.SourceName).Distinct().ToList(),
            AllSourceUrls = group.Select(g => g.SourceUrl).Distinct().ToList()
        };
    }
}

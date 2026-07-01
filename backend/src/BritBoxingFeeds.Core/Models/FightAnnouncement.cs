namespace BritBoxingFeeds.Core.Models;

/// <summary>
/// Normalized output shape returned by every IFightSource implementation.
/// Only SourceName, SourceUrl, RawHeadline and RetrievedAt are guaranteed —
/// everything else is nullable because different sources (RSS headlines vs
/// structured promoter pages) can supply different levels of detail.
/// </summary>
public record FightAnnouncement
{
    public required string SourceName { get; init; }
    public required string SourceUrl { get; init; }
    public required string RawHeadline { get; init; }
    public DateTimeOffset RetrievedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>The feed's own publish date for this item (RSS pubDate/Atom updated), if given. Null, not RetrievedAt's value, means the source didn't supply one.</summary>
    public DateTimeOffset? PublishedAt { get; init; }

    public string? Fighter1 { get; init; }
    public string? Fighter2 { get; init; }
    public DateTimeOffset? EventDate { get; init; }
    public string? Venue { get; init; }
    public string? City { get; init; }
    public string? WeightClass { get; init; }
    public string? TitleOnTheLine { get; init; }
    public string? Broadcaster { get; init; }

    /// <summary>Full article/press release body, if the source provides it. Used by the downstream extractor.</summary>
    public string? ArticleBody { get; init; }

    /// <summary>
    /// Populated by the deduplicator when this record was merged from
    /// multiple sources reporting the same fight. Null for un-merged,
    /// single-source records — check this before assuming SourceName is
    /// the only place this fight was reported.
    /// </summary>
    public IReadOnlyList<string>? MergedFromSources { get; init; }

    /// <summary>All distinct source URLs that reported this fight, populated alongside MergedFromSources.</summary>
    public IReadOnlyList<string>? AllSourceUrls { get; init; }
}

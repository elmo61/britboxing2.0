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

    /// <summary>Set by the LLM extractor: true = an announced/upcoming fight, false = coverage of a fight that already happened, null = extractor couldn't tell (or regex-only pass).</summary>
    public bool? IsUpcoming { get; init; }

    /// <summary>Set by the LLM extractor: "confirmed" (officially announced), "rumoured" (talks/speculation), "cancelled" (called off), or null when it couldn't tell.</summary>
    public string? FightStatus { get; init; }

    /// <summary>For result items (IsUpcoming == false): who won, if the text states it. Null for upcoming fights or a draw/unclear outcome.</summary>
    public string? ResultWinner { get; init; }

    /// <summary>For result items: how it ended — KO/TKO/UD/SD/MD/DQ/RTD, if stated.</summary>
    public string? ResultMethod { get; init; }

    /// <summary>For result items: the round the fight ended in, if stated (null for decisions).</summary>
    public int? ResultRound { get; init; }

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

    /// <summary>
    /// Every source's own text for this fight, populated by the deduplicator
    /// when records merge (the flat ArticleBody only survives from the primary
    /// record). Null for un-merged items — fall back to
    /// SourceName/SourceUrl/RawHeadline/ArticleBody.
    /// </summary>
    public IReadOnlyList<SourceReport>? SourceReports { get; init; }
}

/// <summary>One source's report of a fight: where it ran and what it said.</summary>
public record SourceReport(string Source, string Url, string? Headline, string? Summary);

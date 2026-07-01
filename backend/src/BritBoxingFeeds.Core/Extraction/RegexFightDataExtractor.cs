using System.Text.RegularExpressions;
using BritBoxingFeeds.Core.Models;

namespace BritBoxingFeeds.Extraction;

/// <summary>
/// Pulls out what it can using plain regex over RawHeadline — fighter names
/// from "X vs Y" patterns and a small set of common date phrasings. Cheap
/// and instant, but limited: misses anything not in "Name vs Name" form,
/// and the date matching only covers a handful of explicit formats (no
/// "this Saturday" / relative date handling). Intended as a fast first pass
/// that CompositeFightDataExtractor falls back from to the LLM extractor
/// when it can't fill in enough fields.
/// </summary>
public class RegexFightDataExtractor : IFightDataExtractor
{
    // "Fury vs Usyk", "Fury v Usyk", "Fury vs. Usyk" — case-insensitive,
    // captures up to the next comma/dash/end so trailing context isn't swallowed.
    private static readonly Regex VersusPattern = new(
        @"\b([A-Z][a-zA-Z'\.-]+(?:\s[A-Z][a-zA-Z'\.-]+){0,2})\s+v(?:s\.?)?\s+([A-Z][a-zA-Z'\.-]+(?:\s[A-Z][a-zA-Z'\.-]+){0,2})\b",
        RegexOptions.Compiled);

    // "July 20", "20 July", optionally with a year — deliberately narrow,
    // not a general date parser.
    private static readonly Regex DatePattern = new(
        @"\b(?:(?<day>\d{1,2})\s+(?<month>January|February|March|April|May|June|July|August|September|October|November|December)|(?<month2>January|February|March|April|May|June|July|August|September|October|November|December)\s+(?<day2>\d{1,2}))(?:,?\s*(?<year>\d{4}))?\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public Task<FightAnnouncement> ExtractAsync(FightAnnouncement raw, CancellationToken ct = default)
    {
        var text = raw.RawHeadline;
        string? fighter1 = null, fighter2 = null;
        DateTimeOffset? eventDate = null;

        var versusMatch = VersusPattern.Match(text);
        if (versusMatch.Success)
        {
            fighter1 = versusMatch.Groups[1].Value.Trim();
            fighter2 = versusMatch.Groups[2].Value.Trim();
        }

        var dateMatch = DatePattern.Match(text);
        if (dateMatch.Success)
        {
            var month = dateMatch.Groups["month"].Success ? dateMatch.Groups["month"].Value : dateMatch.Groups["month2"].Value;
            var day = dateMatch.Groups["day"].Success ? dateMatch.Groups["day"].Value : dateMatch.Groups["day2"].Value;
            var year = dateMatch.Groups["year"].Success ? dateMatch.Groups["year"].Value : DateTime.UtcNow.Year.ToString();

            if (DateTimeOffset.TryParse($"{day} {month} {year}", out var parsed))
            {
                eventDate = parsed;
            }
        }

        var result = raw with
        {
            Fighter1 = fighter1,
            Fighter2 = fighter2,
            EventDate = eventDate
        };

        return Task.FromResult(result);
    }
}

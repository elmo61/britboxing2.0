namespace BritBoxingFeeds.Extraction;

/// <summary>
/// Matches the JSON schema we instruct the model to return. Every field is
/// nullable on purpose — the prompt tells the model to use null rather than
/// guess when the text doesn't actually say.
/// </summary>
internal class ExtractedFightDataDto
{
    public string? Fighter1 { get; set; }
    public string? Fighter2 { get; set; }
    public string? EventDate { get; set; }       // ISO 8601 date string, or null
    public string? Venue { get; set; }
    public string? City { get; set; }
    public string? WeightClass { get; set; }
    public string? TitleOnTheLine { get; set; }
    public string? Broadcaster { get; set; }
    public bool? IsUpcoming { get; set; }        // true = announced/scheduled, false = result/report of a past fight, null = can't tell
}

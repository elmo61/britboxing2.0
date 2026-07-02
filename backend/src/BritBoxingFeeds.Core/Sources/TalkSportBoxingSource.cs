namespace BritBoxingFeeds.Sources;

public class TalkSportBoxingSource : RssFightSourceBase
{
    public override string SourceName => "talkSPORT Boxing";

    // The feed lives under /football/ in their URL scheme but is the boxing
    // section's feed (verified by content). ~100 items per fetch — the
    // last-run date cutoff keeps the first-seen batch manageable.
    public TalkSportBoxingSource(HttpClient http)
        : base(http, "https://talksport.com/football/boxing/feed/")
    {
    }
}

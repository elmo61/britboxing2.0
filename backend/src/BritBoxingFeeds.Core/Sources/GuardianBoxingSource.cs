namespace BritBoxingFeeds.Sources;

public class GuardianBoxingSource : RssFightSourceBase
{
    public override string SourceName => "The Guardian Boxing";

    public GuardianBoxingSource(HttpClient http)
        : base(http, "https://www.theguardian.com/sport/boxing/rss")
    {
    }
}

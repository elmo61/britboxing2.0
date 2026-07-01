namespace BritBoxingFeeds.Sources;

public class BbcBoxingSource : RssFightSourceBase
{
    public override string SourceName => "BBC Sport Boxing";

    public BbcBoxingSource(HttpClient http)
        : base(http, "https://feeds.bbci.co.uk/sport/boxing/rss.xml")
    {
    }
}

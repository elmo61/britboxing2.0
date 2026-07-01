namespace BritBoxingFeeds.Sources;

public class WorldBoxingNewsSource : RssFightSourceBase
{
    public override string SourceName => "World Boxing News";

    public WorldBoxingNewsSource(HttpClient http)
        : base(http, "https://www.worldboxingnews.net/feed/")
    {
    }
}

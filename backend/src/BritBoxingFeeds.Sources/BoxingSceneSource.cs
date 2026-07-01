namespace BritBoxingFeeds.Sources;

public class BoxingSceneSource : RssFightSourceBase
{
    public override string SourceName => "BoxingScene";

    public BoxingSceneSource(HttpClient http)
        : base(http, "https://www.boxingscene.com/rss")
    {
    }
}

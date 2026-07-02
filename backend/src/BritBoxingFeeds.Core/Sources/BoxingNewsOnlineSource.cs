namespace BritBoxingFeeds.Sources;

public class BoxingNewsOnlineSource : RssFightSourceBase
{
    public override string SourceName => "Boxing News";

    // boxingnewsonline.net — Boxing News, the UK boxing magazine (est. 1909).
    // Note: the www. host 301s to the bare domain; use the final URL.
    public BoxingNewsOnlineSource(HttpClient http)
        : base(http, "https://boxingnewsonline.net/feed/")
    {
    }
}

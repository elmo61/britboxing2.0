namespace BritBoxingFeeds.Sources;

/// <summary>
/// YouTube exposes a per-channel RSS feed at a fixed URL pattern, no API key
/// needed. Reuse this for IFL TV, Boxing Social, etc — just supply the
/// channel ID (found in the channel's page source or via the YouTube API)
/// and a friendly name.
/// </summary>
public class YouTubeChannelSource : RssFightSourceBase
{
    public override string SourceName { get; }

    public YouTubeChannelSource(HttpClient http, string sourceName, string channelId)
        : base(http, $"https://www.youtube.com/feeds/videos.xml?channel_id={channelId}")
    {
        SourceName = sourceName;
    }
}

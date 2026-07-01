using System.Text.Json;
using BritBoxingFeeds.Core;
using BritBoxingFeeds.Core.Interfaces;
using BritBoxingFeeds.Deduplication;
using BritBoxingFeeds.Extraction;
using BritBoxingFeeds.Sources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

LoadDotEnv();

// --json: print the deduped list as JSON to stdout instead of the human report.
// --json=<path>: same, but also write the JSON to that file.
var jsonArg = args.FirstOrDefault(a => a.StartsWith("--json"));
var jsonMode = jsonArg is not null;
var jsonOutPath = jsonArg?.Contains('=') == true ? jsonArg.Split('=', 2)[1] : null;

var services = new ServiceCollection();

services.AddLogging(b =>
{
    // In --json mode, keep stdout pure JSON — send log noise to stderr instead.
    if (jsonMode)
    {
        // Keep stdout pure JSON: no console provider at all in json mode.
        b.SetMinimumLevel(LogLevel.None);
    }
    else
    {
        b.AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.TimestampFormat = "HH:mm:ss ";
        });
    }
});

services.AddHttpClient();

// Register every IFightSource implementation here. Adding a new source
// later is: write the class, add one line below — nothing else changes.
// (Promoter scrapers like MatchroomSource are intentionally NOT registered —
// BritBoxing policy is news RSS as the announcement trigger, not scraping.)
services.AddTransient<IFightSource, BbcBoxingSource>();
services.AddTransient<IFightSource, BoxingSceneSource>();
services.AddTransient<IFightSource, WorldBoxingNewsSource>();

// YouTube sources need a channel ID per channel, so register instances directly:
// services.AddTransient<IFightSource>(sp =>
//     new YouTubeChannelSource(sp.GetRequiredService<HttpClient>(), "IFL TV", "UCxxxxxxxxxxxxxxxxxxxxxx"));

services.AddTransient<FightAggregator>();

services.AddTransient<RegexFightDataExtractor>();
services.AddHttpClient<AnthropicFightDataExtractor>();
services.AddTransient<CompositeFightDataExtractor>();

services.AddSingleton<IFightDeduplicator>(_ => new FightDeduplicator());

await using var provider = services.BuildServiceProvider();

var aggregator = provider.GetRequiredService<FightAggregator>();
var results = await aggregator.CollectAllAsync();

// Extraction. Use the LLM composite when ANTHROPIC_API_KEY is set, otherwise
// fall back to the free regex-only pass so the app still runs without a key.
IFightDataExtractor extractor = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") is { Length: > 0 }
    ? provider.GetRequiredService<CompositeFightDataExtractor>()
    : provider.GetRequiredService<RegexFightDataExtractor>();

var enriched = new List<BritBoxingFeeds.Core.Models.FightAnnouncement>();
foreach (var item in results)
{
    enriched.Add(await extractor.ExtractAsync(item));
}

// Merge duplicate reports of the same fight (BBC/BoxingScene/WBN all covering
// the same announcement) into single records.
var deduplicator = provider.GetRequiredService<IFightDeduplicator>();
var deduped = await deduplicator.DeduplicateAsync(enriched);

var ordered = deduped.OrderByDescending(r => r.RetrievedAt).ToList();

if (jsonMode)
{
    var json = JsonSerializer.Serialize(ordered, new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    });

    if (jsonOutPath is not null)
    {
        File.WriteAllText(jsonOutPath, json);
    }

    Console.WriteLine(json);
    return;
}

Console.WriteLine();
Console.WriteLine($"Collected {enriched.Count} items, {deduped.Count} after dedup:");
Console.WriteLine();

foreach (var item in ordered)
{
    var sources = item.MergedFromSources is { Count: > 0 }
        ? string.Join(", ", item.MergedFromSources)
        : item.SourceName;

    Console.WriteLine($"[{sources}] {item.RawHeadline}");
    Console.WriteLine($"  Fighters: {item.Fighter1 ?? "?"} vs {item.Fighter2 ?? "?"}");
    Console.WriteLine($"  Date: {item.EventDate?.ToString("d") ?? "?"}  Venue: {item.Venue ?? "?"}  City: {item.City ?? "?"}");
    Console.WriteLine($"  {item.SourceUrl}");
    Console.WriteLine();
}

// Loads backend/.env into the process environment, project-scoped (never a
// machine/user env var, so it can't leak into other projects). Searches
// upward from the executable's directory so it works regardless of the
// working directory `dotnet run` was invoked from. Mirrors the Python
// pipeline's _load_dotenv(): a real environment variable already set always
// wins (setdefault-style), .env is only a fallback for local dev.
static void LoadDotEnv()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        var candidate = Path.Combine(dir.FullName, ".env");
        if (File.Exists(candidate))
        {
            foreach (var rawLine in File.ReadAllLines(candidate))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;

                var separator = line.IndexOf('=');
                if (separator <= 0) continue;

                var key = line[..separator].Trim();
                var value = line[(separator + 1)..].Trim().Trim('"');

                if (Environment.GetEnvironmentVariable(key) is null)
                {
                    Environment.SetEnvironmentVariable(key, value);
                }
            }
            return;
        }
        dir = dir.Parent;
    }
}

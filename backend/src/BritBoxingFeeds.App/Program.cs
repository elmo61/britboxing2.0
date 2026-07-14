using System.Text.Json;
using BritBoxingFeeds.Core;
using BritBoxingFeeds.Core.Articles;
using BritBoxingFeeds.Core.Deploy;
using BritBoxingFeeds.Core.Enrichment;
using BritBoxingFeeds.Core.Fighters;
using BritBoxingFeeds.Core.Interfaces;
using BritBoxingFeeds.Core.Processing;
using BritBoxingFeeds.Core.State;
using BritBoxingFeeds.Core.Supabase;
using BritBoxingFeeds.Deduplication;
using BritBoxingFeeds.Extraction;
using BritBoxingFeeds.Sources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

LoadDotEnv();

// How far before the last run an item's own PublishedAt can sit and still
// count as "new" — covers feeds that backdate items or arrive late.
var gracePeriod = TimeSpan.FromHours(3);

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
services.AddTransient<IFightSource, WorldBoxingNewsSource>();
services.AddTransient<IFightSource, GuardianBoxingSource>();
services.AddTransient<IFightSource, TalkSportBoxingSource>();
services.AddTransient<IFightSource, BoxingNewsOnlineSource>();
// BoxingSceneSource removed 2026-07-02: their RSS 403s all automated requests.

// YouTube sources need a channel ID per channel, so register instances directly:
// services.AddTransient<IFightSource>(sp =>
//     new YouTubeChannelSource(sp.GetRequiredService<HttpClient>(), "IFL TV", "UCxxxxxxxxxxxxxxxxxxxxxx"));

services.AddTransient<FightAggregator>();

services.AddTransient<RegexFightDataExtractor>();
services.AddHttpClient<AnthropicFightDataExtractor>();
services.AddTransient<CompositeFightDataExtractor>();
services.AddHttpClient<SeenFeedItemsStore>();

// The decide → bout → article stage.
services.AddHttpClient<SupabaseClient>();
services.AddHttpClient<WikipediaSnapshotService>();
services.AddHttpClient<ArticleGenerator>();
services.AddHttpClient<SourcePageFetcher>();
services.AddTransient<FighterStore>();
services.AddTransient<BoutProcessor>();
services.AddHttpClient<FightDateEnricher>();
services.AddHttpClient<SiteDeployTrigger>();

services.AddSingleton<IFightDeduplicator>(_ => new FightDeduplicator());

await using var provider = services.BuildServiceProvider();

// Fail fast: without Supabase there's nowhere to persist results or the
// seen-items dedup table, so there's no point spending time on RSS/LLM work
// at all. Checked before anything else runs.
var seenStore = provider.GetRequiredService<SeenFeedItemsStore>();
if (!await seenStore.CheckConnectionAsync())
{
    Console.Error.WriteLine(
        "Fatal: cannot reach the seen_feed_items table in Supabase. " +
        "Check SUPABASE_URL / SUPABASE_SECRET_KEY in backend/.env, and that " +
        "db/schema.sql + db/policies.sql have been applied. Aborting — " +
        "nothing downstream can be persisted without this.");
    Environment.Exit(1);
    return;
}

// Separate job: `enrich-dates` fills missing fight dates via web search,
// independent of the feed run (run it on its own, lower-frequency schedule).
if (args.Contains("enrich-dates"))
{
    // Optional numeric arg caps how many bouts to process (for a cheap smoke
    // test): `enrich-dates 3`. No arg => all dateless confirmed bouts.
    var limit = args.Select(x => int.TryParse(x, out var n) ? n : 0).FirstOrDefault(n => n > 0);
    await RunDateEnrichmentAsync(provider, limit == 0 ? int.MaxValue : limit);
    return;
}

// Validation tool: `preview-article <bout-slug> [--write]` regenerates one
// bout's article with the current prompt and prints the prompt + result +
// lint report. Nothing is persisted unless --write is passed. Exactly one
// API call per invocation — the cheap way to iterate on article quality.
if (args.Contains("preview-article"))
{
    var slugArg = args.SkipWhile(a => a != "preview-article").Skip(1).FirstOrDefault(a => !a.StartsWith("--"));
    if (slugArg is null)
    {
        Console.Error.WriteLine("Usage: preview-article <bout-slug> [--write]");
        Environment.Exit(2);
        return;
    }
    await RunPreviewArticleAsync(provider, slugArg, write: args.Contains("--write"));
    return;
}

var aggregator = provider.GetRequiredService<FightAggregator>();
var results = await aggregator.CollectAllAsync();

// Skip items already run through extraction in a prior run — the same fight
// reappearing shouldn't cost another LLM call.
var seen = await seenStore.LoadRecentAsync(TimeSpan.FromDays(30));
var cutoff = seen.LastRunAt is { } lastRunAt ? lastRunAt - gracePeriod : (DateTimeOffset?)null;

var dateFiltered = results.Where(item =>
    cutoff is null || item.PublishedAt is not { } published || published >= cutoff
).ToList();

// Free regex pass, purely so the seen-check can key on fighter-pair +
// event month/year rather than just the raw URL — the same fight reported
// under a different URL is still recognized, without needing the LLM to
// know that yet. Extraction below reruns its own regex pass on survivors
// (idempotent, no added cost) and only escalates to the LLM if still incomplete.
var regexExtractor = provider.GetRequiredService<RegexFightDataExtractor>();
var candidates = new List<BritBoxingFeeds.Core.Models.FightAnnouncement>();
foreach (var item in dateFiltered)
{
    var regexed = await regexExtractor.ExtractAsync(item);
    if (!SeenFeedItemsStore.ComputeKeys(regexed).Any(seen.Contains))
    {
        candidates.Add(item);
    }
}

// Extraction. Use the LLM composite when ANTHROPIC_API_KEY is set, otherwise
// fall back to the free regex-only pass so the app still runs without a key.
IFightDataExtractor extractor = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") is { Length: > 0 }
    ? provider.GetRequiredService<CompositeFightDataExtractor>()
    : provider.GetRequiredService<RegexFightDataExtractor>();

var enriched = new List<BritBoxingFeeds.Core.Models.FightAnnouncement>();
foreach (var item in candidates)
{
    enriched.Add(await extractor.ExtractAsync(item));
}

// Merge duplicate reports of the same fight (BBC/BoxingScene/WBN all covering
// the same announcement) into single records.
var deduplicator = provider.GetRequiredService<IFightDeduplicator>();
var deduped = await deduplicator.DeduplicateAsync(enriched);

// Record every item just processed as seen, using its final (post-LLM where
// applicable) fields — noise items that never resolved to a fight are still
// recorded under their URL/headline key, so they aren't reprocessed either.
await seenStore.MarkSeenAsync(enriched);

// Decide → bout → article. Candidates are this run's deduped fights plus
// any status=new fight rows left over from earlier runs (resumed from
// their stored extraction), deduped against each other by fight key.
var pending = await seenStore.LoadPendingAsync();
var thisRunKeys = deduped
    .SelectMany(SeenFeedItemsStore.ComputeKeys)
    .Where(k => k.StartsWith("fight:"))
    .ToHashSet();
var resumed = pending
    .Where(p => !thisRunKeys.Contains(p.ItemKey))
    .ToList();

var processor = provider.GetRequiredService<BoutProcessor>();
var summary = await processor.ProcessAsync([
    .. deduped.Select(d => (d, (string?)null)),
    .. resumed.Select(p => (p.Item, (string?)p.ItemKey)),
]);
var recoveredArticles = await processor.RetryMissingArticlesAsync();

// The site is statically generated — a rebuild is what actually publishes
// new content, so trigger one only when this run created something.
if (summary.BoutsCreated + summary.ArticlesCreated + summary.ArticlesRegenerated + recoveredArticles > 0)
{
    await provider.GetRequiredService<SiteDeployTrigger>().TriggerAsync();
}

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
Console.WriteLine(
    $"Collected {results.Count} items " +
    $"({results.Count - dateFiltered.Count} older than last run, " +
    $"{dateFiltered.Count - candidates.Count} already seen — skipped), " +
    $"{enriched.Count} processed, {deduped.Count} after dedup.");
Console.WriteLine(
    $"Processing: {summary.Considered} considered ({resumed.Count} resumed from earlier runs), " +
    $"{summary.Ignored} ignored, {summary.AlreadyExisted} existing bouts, " +
    $"{summary.BoutsCreated} bouts created, {summary.ArticlesCreated} articles written, " +
    $"{summary.ArticlesAppended} sources added to open articles, " +
    $"{summary.ArticlesRegenerated} regenerated on confirmation, " +
    $"{recoveredArticles} missing articles recovered.");
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

// Fills missing fight dates for still-live bouts via web-search grounding.
// Regenerates one bout's article with the current prompt for quality
// iteration. Prints the full prompt, the generated JSON and the lint report;
// persists (PATCH onto the bout's latest article, or INSERT if none) only
// when --write is passed.
static async Task RunPreviewArticleAsync(IServiceProvider provider, string boutSlug, bool write)
{
    var supabase = provider.GetRequiredService<SupabaseClient>();
    var generator = provider.GetRequiredService<ArticleGenerator>();
    var processor = provider.GetRequiredService<BoutProcessor>();

    var bouts = await supabase.SelectAsync("bouts",
        $"select=slug,status,weight_class,event_date,fighter_a_snapshot,fighter_b_snapshot&slug=eq.{Uri.EscapeDataString(boutSlug)}");
    if (bouts.Count == 0)
    {
        Console.Error.WriteLine($"No bout found with slug '{boutSlug}'.");
        Environment.Exit(2);
        return;
    }

    var row = bouts[0]!.AsObject();
    var snapA = row["fighter_a_snapshot"]!.AsObject();
    var snapB = row["fighter_b_snapshot"]!.AsObject();
    var (coverage, sources) = await processor.CoverageFromSeenItemsAsync(boutSlug);
    var bout = new System.Text.Json.Nodes.JsonObject
    {
        ["fighter_a"] = snapA["_meta"]?["name"]?.DeepClone(),
        ["fighter_b"] = snapB["_meta"]?["name"]?.DeepClone(),
        ["weightClass"] = row["weight_class"]?.DeepClone(),
        ["eventDate"] = row["event_date"]?.DeepClone(),
        ["fightStatus"] = row["status"]?.DeepClone(),
        ["coverage"] = coverage,
    };

    var promptRecord = ArticleGenerator.BuildPromptRecord(bout, snapA, snapB);
    Console.WriteLine("================ SYSTEM PROMPT ================");
    Console.WriteLine(promptRecord["system"]?.GetValue<string>());
    Console.WriteLine();
    Console.WriteLine("================ USER PROMPT ==================");
    Console.WriteLine(promptRecord["user"]?.GetValue<string>());
    Console.WriteLine();

    Console.WriteLine("================ GENERATING (1 API call) ======");
    var article = await generator.GenerateAsync(bout, snapA, snapB);
    if (article is null)
    {
        Console.Error.WriteLine("Generation failed — see log output above.");
        Environment.Exit(1);
        return;
    }

    Console.WriteLine("================ RESULT =======================");
    Console.WriteLine(article.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

    // GenerateAsync already lint-fixed the article; run a fresh detection pass
    // purely to report the outcome.
    var lint = ArticleLinter.Lint(article);
    Console.WriteLine();
    Console.WriteLine($"Lint: {lint.FixCount} residual hard-rule fixes, {lint.Warnings.Count} warnings"
        + (lint.Warnings.Count > 0 ? ": " + string.Join("; ", lint.Warnings) : "."));

    if (!write)
    {
        Console.WriteLine();
        Console.WriteLine("Dry run — nothing persisted. Re-run with --write to save.");
        return;
    }

    var latest = await supabase.SelectAsync("articles",
        $"select=id&bout_slug=eq.{Uri.EscapeDataString(boutSlug)}&order=published_at.desc&limit=1");
    if (latest.Count > 0)
    {
        await supabase.UpdateAsync("articles", $"id=eq.{latest[0]!["id"]!.GetValue<long>()}", new System.Text.Json.Nodes.JsonObject
        {
            ["title"] = article["title"]?.DeepClone(),
            ["summary"] = article["summary"]?.DeepClone(),
            ["body"] = article["body"]?.DeepClone(),
            ["tags"] = article["tags"]?.DeepClone(),
            ["sources"] = sources,
        });
        Console.WriteLine("Updated the bout's latest article in place (slug/published_at kept).");
    }
    else
    {
        await supabase.InsertAsync("articles", new System.Text.Json.Nodes.JsonObject
        {
            ["bout_slug"] = boutSlug,
            ["slug"] = "preview",
            ["status"] = row["status"]?.DeepClone(),
            ["title"] = article["title"]?.DeepClone(),
            ["summary"] = article["summary"]?.DeepClone(),
            ["body"] = article["body"]?.DeepClone(),
            ["tags"] = article["tags"]?.DeepClone(),
            ["ai_generated"] = true,
            ["published_at"] = DateTimeOffset.UtcNow.ToString("o"),
            ["sources"] = sources,
        });
        Console.WriteLine("Inserted a new article for the bout.");
    }
    await provider.GetRequiredService<SiteDeployTrigger>().TriggerAsync();
}

// Web-searches each dateless confirmed bout for (a) a source-backed date and
// (b) whether sources actually treat it as officially confirmed. A date is only
// stored when the model cited a source (see FightDateEnricher). When no date is
// found AND sources say the fight is only rumoured, the bout is demoted to
// 'rumoured' — this doubles as a cleanup pass for the over-eager status
// classifier. A confirmed-but-undated fight (or an ambiguous one) is left alone.
static async Task RunDateEnrichmentAsync(IServiceProvider provider, int limit)
{
    var supabase = provider.GetRequiredService<SupabaseClient>();
    var enricher = provider.GetRequiredService<FightDateEnricher>();

    // Only confirmed fights — a specific date rarely exists before a fight is
    // confirmed, so searching rumoured bouts wastes web-search calls.
    var bouts = await supabase.SelectAsync("bouts",
        "select=slug,weight_class,fighter_a_snapshot,fighter_b_snapshot&event_date=is.null&status=eq.confirmed");

    int checkedCount = 0, found = 0, demoted = 0;
    foreach (var b in bouts.OfType<System.Text.Json.Nodes.JsonObject>())
    {
        if (checkedCount >= limit) break;
        var a = b["fighter_a_snapshot"]?["_meta"]?["name"]?.GetValue<string>();
        var c = b["fighter_b_snapshot"]?["_meta"]?["name"]?.GetValue<string>();
        if (a is null || c is null) continue;
        checkedCount++;

        var slug = b["slug"]!.GetValue<string>();
        var res = await enricher.FindDateAsync(a, c, b["weight_class"]?.GetValue<string>());
        if (res is null)
        {
            Console.WriteLine($"  {slug}: (no result — search/parse failed)");
            continue;
        }

        if (res.Date is not null)
        {
            await supabase.UpdateAsync("bouts", $"slug=eq.{Uri.EscapeDataString(slug)}",
                new System.Text.Json.Nodes.JsonObject { ["event_date"] = res.Date });
            found++;
            Console.WriteLine($"  {slug}: DATE {res.Date} ({res.Confidence}) — {res.SourceUrl}");
        }
        else if (res.Status == "rumoured")
        {
            await supabase.UpdateAsync("bouts", $"slug=eq.{Uri.EscapeDataString(slug)}",
                new System.Text.Json.Nodes.JsonObject { ["status"] = "rumoured" });
            demoted++;
            Console.WriteLine($"  {slug}: no date, sources say rumoured -> demoted to rumoured");
        }
        else
        {
            Console.WriteLine($"  {slug}: no date, status left as confirmed ({res.Status})");
        }
    }
    Console.WriteLine($"Date enrichment: checked {checkedCount} dateless confirmed bouts, set {found} dates, demoted {demoted} to rumoured.");
    if (found > 0 || demoted > 0) await provider.GetRequiredService<SiteDeployTrigger>().TriggerAsync();
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

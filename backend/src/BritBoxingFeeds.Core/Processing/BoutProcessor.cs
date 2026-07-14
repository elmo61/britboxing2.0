using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BritBoxingFeeds.Core.Articles;
using BritBoxingFeeds.Core.Enrichment;
using BritBoxingFeeds.Core.Fighters;
using BritBoxingFeeds.Core.Models;
using BritBoxingFeeds.Core.State;
using BritBoxingFeeds.Core.Supabase;
using Microsoft.Extensions.Logging;

namespace BritBoxingFeeds.Core.Processing;

/// <summary>
/// The decide → bout → article stage (port of pipeline.py's discover/_commit
/// flow). Takes fight-keyed announcements (from this run's dedup output plus
/// any status=new rows resumed from earlier runs), decides whether each
/// deserves a bout — upcoming, both fighters resolvable — then creates the
/// bouts row with frozen Wikipedia snapshots, refreshes the fighters table,
/// generates the preview article, and advances the seen_feed_items status
/// (ignored / bout_created / article_created) with the bout_slug link.
/// </summary>
public class BoutProcessor
{
    // Result-flavoured headline verbs — used only when the LLM extractor
    // didn't say whether the fight is upcoming (IsUpcoming == null).
    private static readonly Regex PastResultRegex = new(
        @"\b(beats?|stops|stopped|knocks out|knocked out|outpoints|outpointed|defeats|defeated|retains|retained|survives|survived|drops|dropped|wins|won|scorecard|victory over)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly SupabaseClient _supabase;
    private readonly WikipediaSnapshotService _wikipedia;
    private readonly FighterStore _fighters;
    private readonly ArticleGenerator _articles;
    private readonly SeenFeedItemsStore _seenStore;
    private readonly SourcePageFetcher _pages;
    private readonly ILogger<BoutProcessor> _logger;

    public BoutProcessor(
        SupabaseClient supabase,
        WikipediaSnapshotService wikipedia,
        FighterStore fighters,
        ArticleGenerator articles,
        SeenFeedItemsStore seenStore,
        SourcePageFetcher pages,
        ILogger<BoutProcessor> logger)
    {
        _supabase = supabase;
        _wikipedia = wikipedia;
        _fighters = fighters;
        _articles = articles;
        _seenStore = seenStore;
        _pages = pages;
        _logger = logger;
    }

    public record ProcessSummary(int Considered, int Ignored, int BoutsCreated, int ArticlesCreated, int ArticlesAppended, int ArticlesRegenerated, int AlreadyExisted);

    // A fight announced now shows up across many feeds within days; those all
    // roll into ONE article (citing multiple sources). A fresh item after this
    // window opens a NEW article on the same bout.
    private static readonly TimeSpan ArticleWindow = TimeSpan.FromDays(7);

    /// <summary>Candidates carry the seen-row key they were loaded from (null for items found this run) so status updates hit the right row.</summary>
    public async Task<ProcessSummary> ProcessAsync(IReadOnlyList<(FightAnnouncement Item, string? SourceKey)> candidates, CancellationToken ct = default)
    {
        int ignored = 0, boutsCreated = 0, articlesCreated = 0, articlesAppended = 0, articlesRegenerated = 0, alreadyExisted = 0;

        foreach (var (item, sourceKey) in candidates)
        {
            if (string.IsNullOrWhiteSpace(item.Fighter1) || string.IsNullOrWhiteSpace(item.Fighter2))
            {
                continue; // not fight-keyed; nothing to decide
            }

            // A cancellation report isn't a new fight — but if we've already
            // previewed this bout, flip its status so the site shows it's off.
            if (item.FightStatus == "cancelled")
            {
                var cancelled = await TryCancelExistingBoutAsync(item, ct);
                await _seenStore.SetStatusAsync(item, "ignored",
                    cancelled ? "cancellation — existing bout marked cancelled" : "cancelled fight, no bout to update",
                    sourceKey: sourceKey, ct: ct);
                _logger.LogInformation("Cancellation for '{F1} vs {F2}' ({Outcome})",
                    item.Fighter1, item.Fighter2, cancelled ? "bout updated" : "no existing bout");
                ignored++;
                continue;
            }

            // A result report closes out a bout we've been covering: the bout
            // flips to completed (with the result stored) and gets a result
            // article. Results for fights we never tracked stay ignored —
            // covering every fight that ever happens is noise, not news.
            if (item.IsUpcoming == false)
            {
                var (resultOutcome, resultSlug) = await TryProcessResultAsync(item, ct);
                if (resultOutcome is "created" or "appended" or "regenerated")
                {
                    if (resultOutcome == "created") { articlesCreated++; _logger.LogInformation("Result article for {Slug}", resultSlug); }
                    else { articlesAppended++; _logger.LogInformation("Added result source to open article for {Slug}", resultSlug); }
                    await _seenStore.SetStatusAsync(item, "article_created", boutSlug: resultSlug, sourceKey: sourceKey, ct: ct);
                }
                else if (resultOutcome == "failed")
                {
                    await _seenStore.SetStatusAsync(item, "bout_created", boutSlug: resultSlug, sourceKey: sourceKey, ct: ct);
                    _logger.LogWarning("Result article generation failed for {Slug}", resultSlug);
                }
                else
                {
                    await _seenStore.SetStatusAsync(item, "ignored", "result for a fight we never tracked", sourceKey: sourceKey, ct: ct);
                    _logger.LogInformation("Ignored result '{F1} vs {F2}': no tracked bout", item.Fighter1, item.Fighter2);
                    ignored++;
                }
                continue;
            }

            // ---- Decide -------------------------------------------------
            var reason = DecideIgnoreReason(item);
            if (reason is not null)
            {
                await _seenStore.SetStatusAsync(item, "ignored", reason, sourceKey: sourceKey, ct: ct);
                _logger.LogInformation("Ignored '{F1} vs {F2}': {Reason}", item.Fighter1, item.Fighter2, reason);
                ignored++;
                continue;
            }

            // LLM-confirmed announcements may enrich best-effort (sparse on
            // missing wiki); regex-only items stay strict — both fighters
            // must resolve with real records — to keep feed noise out.
            var aiConfirmed = item.IsUpcoming == true;
            var snapA = await _wikipedia.BuildSnapshotAsync(item.Fighter1, allowSparse: aiConfirmed, ct);
            var snapB = await _wikipedia.BuildSnapshotAsync(item.Fighter2, allowSparse: aiConfirmed, ct);
            if (snapA is null || snapB is null
                || (!aiConfirmed && !(WikipediaSnapshotService.HasRecord(snapA) && WikipediaSnapshotService.HasRecord(snapB))))
            {
                await _seenStore.SetStatusAsync(item, "ignored", "fighters did not resolve to boxers on Wikipedia", sourceKey: sourceKey, ct: ct);
                _logger.LogInformation("Ignored '{F1} vs {F2}': fighters did not resolve", item.Fighter1, item.Fighter2);
                ignored++;
                continue;
            }

            // ---- Bout ---------------------------------------------------
            var fidA = await _fighters.UpsertAsync(snapA, ct);
            var fidB = await _fighters.UpsertAsync(snapB, ct);
            if (fidA == fidB)
            {
                // Both names resolved to the same person — a bad extraction
                // or a wrong Wikipedia match, never a real fight.
                await _seenStore.SetStatusAsync(item, "ignored", "both fighters resolved to the same person", sourceKey: sourceKey, ct: ct);
                _logger.LogInformation("Ignored '{F1} vs {F2}': both resolved to {Fid}", item.Fighter1, item.Fighter2, fidA);
                ignored++;
                continue;
            }
            var slug = $"{fidA}-vs-{fidB}";
            // The same matchup may already exist under the reversed fighter
            // order (sources name the fighters in either order) — check both.
            var reversedSlug = $"{fidB}-vs-{fidA}";
            var existing = await _supabase.SelectAsync("bouts",
                $"select=slug,event_date&slug=in.({Uri.EscapeDataString($"{slug},{reversedSlug}")})", ct);

            // Article-generation context — names + stats from the snapshots we
            // just built, plus everything the feeds actually SAID about the
            // fight (coverage + extractor facts). The news material is what the
            // writer leads with; the snapshots are supporting colour.
            var bout = new JsonObject
            {
                ["fighter_a"] = snapA["_meta"]!["name"]!.GetValue<string>(),
                ["fighter_b"] = snapB["_meta"]!["name"]!.GetValue<string>(),
                ["fighterAId"] = fidA,
                ["fighterBId"] = fidB,
                ["weightClass"] = WikipediaSnapshotService.BoutWeightClass(snapA, snapB) ?? item.WeightClass,
                ["eventDate"] = item.EventDate?.ToString("yyyy-MM-dd"),
                ["headline"] = item.RawHeadline,
                ["source"] = item.MergedFromSources is { Count: > 0 } merged ? string.Join(", ", merged) : item.SourceName,
                ["link"] = item.SourceUrl,
                ["fightStatus"] = item.FightStatus ?? (item.EventDate is not null ? "confirmed" : "rumoured"),
                ["venue"] = item.Venue,
                ["city"] = item.City,
                ["titleOnTheLine"] = item.TitleOnTheLine,
                ["broadcaster"] = item.Broadcaster,
                ["coverage"] = CoverageFromItem(item),
            };

            string boutSlug;
            if (existing.Count > 0)
            {
                boutSlug = existing[0]!["slug"]!.GetValue<string>();
                alreadyExisted++;
                // Backfill a date if we now have one and the bout was dateless.
                if (item.EventDate is { } d && existing[0]!["event_date"] is null)
                {
                    await _supabase.UpdateAsync("bouts", $"slug=eq.{Uri.EscapeDataString(boutSlug)}",
                        new JsonObject { ["event_date"] = d.ToString("yyyy-MM-dd"), ["status"] = item.FightStatus ?? "confirmed" }, ct);
                }
            }
            else
            {
                boutSlug = slug;
                await _supabase.UpsertAsync("bouts", [new JsonObject
                {
                    ["slug"] = slug,
                    ["fighter_a_id"] = fidA,
                    ["fighter_b_id"] = fidB,
                    // No LLM verdict -> dated announcement reads confirmed, undated rumoured.
                    ["status"] = item.FightStatus ?? (item.EventDate is not null ? "confirmed" : "rumoured"),
                    ["weight_class"] = bout["weightClass"]?.DeepClone(),
                    ["event_date"] = bout["eventDate"]?.DeepClone(),
                    // FROZEN at announcement time — never rewritten when a fighter updates.
                    ["fighter_a_snapshot"] = snapA.DeepClone(),
                    ["fighter_b_snapshot"] = snapB.DeepClone(),
                }], "slug", ct);
                boutsCreated++;
                _logger.LogInformation("Created bout {Slug}", slug);
            }

            // ---- Article (windowed: one article per burst of coverage) ------
            var outcome = await UpsertArticleForBoutAsync(boutSlug, bout, snapA, snapB, item, ct);
            if (outcome == "created") { articlesCreated++; _logger.LogInformation("New article for {Slug}", boutSlug); }
            else if (outcome == "appended") { articlesAppended++; _logger.LogInformation("Added source to open article for {Slug}", boutSlug); }
            else if (outcome == "regenerated") { articlesRegenerated++; _logger.LogInformation("Rumour confirmed — regenerated article for {Slug}", boutSlug); }
            else { _logger.LogWarning("Article generation failed for {Slug}", boutSlug); }

            await _seenStore.SetStatusAsync(item, outcome == "failed" ? "bout_created" : "article_created",
                boutSlug: boutSlug, sourceKey: sourceKey, ct: ct);
        }

        return new ProcessSummary(candidates.Count, ignored, boutsCreated, articlesCreated, articlesAppended, articlesRegenerated, alreadyExisted);
    }

    /// <summary>
    /// Adds this feed item's coverage to the bout. If the bout has an article
    /// whose window is still open (published within ArticleWindow), the item
    /// is recorded as another source on it — no new article, no extra AI spend.
    /// Otherwise a fresh article is generated. Returns "created", "appended" or "failed".
    /// </summary>
    private async Task<string> UpsertArticleForBoutAsync(string boutSlug, JsonObject bout, JsonObject snapA, JsonObject snapB, FightAnnouncement item, CancellationToken ct)
    {
        // One sources entry per report, so each source's own headline and
        // summary text survive for later regenerations and window articles.
        var newEntries = SourceEntriesFromItem(item);

        var latest = await _supabase.SelectAsync("articles",
            $"select=id,status,published_at,sources&bout_slug=eq.{Uri.EscapeDataString(boutSlug)}&order=published_at.desc&limit=1", ct);

        var isResultItem = bout["fightStatus"]?.GetValue<string>() == "result";

        if (latest.Count > 0)
        {
            var art = latest[0]!.AsObject();
            var publishedAt = DateTimeOffset.TryParse(art["published_at"]?.GetValue<string>(), out var p) ? p : DateTimeOffset.MinValue;
            // A result never rolls into an open PREVIEW window — it's a new
            // story and gets its own article. Result reports do window with
            // each other (second source of the same result appends).
            var latestIsResult = art["status"]?.GetValue<string>() == "result";
            if (DateTimeOffset.UtcNow - publishedAt <= ArticleWindow && isResultItem == latestIsResult)
            {
                var sources = art["sources"]?.DeepClone().AsArray() ?? [];
                var appendedAny = false;
                foreach (var entry in newEntries)
                {
                    var url = entry["url"]?.GetValue<string>();
                    var already = !string.IsNullOrEmpty(url)
                        && sources.Any(s => s?["url"]?.GetValue<string>() == url);
                    if (!already) { sources.Add(entry.DeepClone()); appendedAny = true; }
                }

                // A rumour firming up into an announced fight is the one moment
                // stale prose really hurts (the piece still says "in talks").
                // Regenerate ONCE onto the same row; every other in-window
                // arrival stays a cheap source append.
                var wasRumoured = art["status"]?.GetValue<string>() == "rumoured";
                if (wasRumoured && item.FightStatus == "confirmed")
                {
                    bout["coverage"] = CoverageFromSources(sources) is { Count: > 0 } cov ? cov : bout["coverage"]?.DeepClone();
                    await HydrateCoverageAsync(bout["coverage"], ct);
                    var regen = await _articles.GenerateAsync(bout, snapA, snapB, ct);
                    if (regen is not null)
                    {
                        // Same row: slug and published_at survive, so the URL is
                        // stable and the coverage window doesn't reset.
                        await _supabase.UpdateAsync("articles", $"id=eq.{art["id"]!.GetValue<long>()}", new JsonObject
                        {
                            ["status"] = "confirmed",
                            ["title"] = regen["title"]?.DeepClone(),
                            ["summary"] = regen["summary"]?.DeepClone(),
                            ["body"] = regen["body"]?.DeepClone(),
                            ["tags"] = regen["tags"]?.DeepClone(),
                            ["sources"] = sources,
                        }, ct);
                        return "regenerated";
                    }
                    // Regeneration failing shouldn't lose the append/status bump.
                }

                var patch = new JsonObject();
                if (appendedAny) patch["sources"] = sources;
                // A fight can firm up within the window (rumoured -> confirmed).
                // Result articles keep their status regardless of what the
                // extractor said about the item.
                if (!isResultItem && item.FightStatus is { } st) patch["status"] = st;
                if (patch.Count > 0)
                {
                    await _supabase.UpdateAsync("articles", $"id=eq.{art["id"]!.GetValue<long>()}", patch, ct);
                }
                return "appended";
            }
        }

        await HydrateCoverageAsync(bout["coverage"], ct);
        var article = await _articles.GenerateAsync(bout, snapA, snapB, ct);
        if (article is null) return "failed";

        var title = article["title"]?.GetValue<string>() ?? "Preview";
        await _supabase.InsertAsync("articles", new JsonObject
        {
            ["bout_slug"] = boutSlug,
            ["slug"] = await UniqueArticleSlugAsync(boutSlug, title, ct),
            ["status"] = isResultItem ? "result" : item.FightStatus ?? (item.EventDate is not null ? "confirmed" : "rumoured"),
            ["title"] = article["title"]?.DeepClone(),
            ["summary"] = article["summary"]?.DeepClone(),
            ["body"] = article["body"]?.DeepClone(),
            ["tags"] = article["tags"]?.DeepClone(),
            ["ai_generated"] = true,
            ["published_at"] = DateTimeOffset.UtcNow.ToString("o"),
            ["sources"] = new JsonArray(newEntries.Select(e => (JsonNode)e.DeepClone()).ToArray()),
        }, ct);
        return "created";
    }

    /// <summary>
    /// One entry per source that reported this item, carrying the source's own
    /// headline and cleaned summary text (so regenerations and later window
    /// articles can quote what was actually said, not just cite a URL).
    /// </summary>
    private static List<JsonObject> SourceEntriesFromItem(FightAnnouncement item)
    {
        var seenAt = DateTimeOffset.UtcNow.ToString("o");
        var reports = item.SourceReports is { Count: > 0 } r
            ? r
            : [new SourceReport(item.SourceName, item.SourceUrl, item.RawHeadline, item.ArticleBody)];
        return reports.Select(rep => new JsonObject
        {
            ["source"] = rep.Source,
            ["url"] = rep.Url,
            ["headline"] = rep.Headline,
            ["summary"] = CleanSummary(rep.Summary),
            ["seen_at"] = seenAt,
        }).ToList();
    }

    /// <summary>Coverage array for the writer prompt, straight from this item's reports.</summary>
    private static JsonArray CoverageFromItem(FightAnnouncement item)
    {
        var arr = new JsonArray();
        foreach (var e in SourceEntriesFromItem(item))
        {
            arr.Add(new JsonObject
            {
                ["source"] = e["source"]?.DeepClone(),
                ["headline"] = e["headline"]?.DeepClone(),
                ["summary"] = e["summary"]?.DeepClone(),
                ["url"] = e["url"]?.DeepClone(),
            });
        }
        return arr;
    }

    /// <summary>Coverage array rebuilt from an article's stored sources (older entries may lack summaries).</summary>
    private static JsonArray CoverageFromSources(JsonArray sources)
    {
        var arr = new JsonArray();
        foreach (var s in sources.OfType<JsonObject>())
        {
            arr.Add(new JsonObject
            {
                ["source"] = s["source"]?.DeepClone(),
                ["headline"] = s["headline"]?.DeepClone(),
                ["summary"] = s["summary"]?.DeepClone(),
                ["url"] = s["url"]?.DeepClone(),
            });
        }
        return arr;
    }

    /// <summary>
    /// Fetches each coverage entry's linked page and attaches the story text
    /// as "fullText" (context for the writer only). Only called on paths that
    /// actually generate — appends never fetch. Best-effort per entry.
    /// </summary>
    private async Task HydrateCoverageAsync(JsonNode? coverage, CancellationToken ct)
    {
        if (coverage is not JsonArray arr || !SourcePageFetcher.Enabled) return;
        foreach (var entry in arr.OfType<JsonObject>().Take(4))
        {
            var url = entry["url"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(url)) continue;
            var text = await _pages.FetchTextAsync(url, ct);
            if (text is not null) entry["fullText"] = text;
        }
    }

    /// <summary>RSS summaries arrive as HTML fragments; the writer wants plain text, capped.</summary>
    private static string? CleanSummary(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        var text = Regex.Replace(html, @"<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ").Trim();
        if (text.Length == 0) return null;
        return text.Length > 1500 ? text[..1500] : text;
    }

    private static string Slugify(string s)
    {
        var kebab = Regex.Replace(s.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        if (kebab.Length == 0) return "article";
        return kebab.Length > 90 ? kebab[..90].Trim('-') : kebab;
    }

    /// <summary>A title-derived article slug, unique within the bout (nested URL /fights/{bout}/{slug}).</summary>
    private async Task<string> UniqueArticleSlugAsync(string boutSlug, string title, CancellationToken ct)
    {
        var baseSlug = Slugify(title);
        var taken = (await _supabase.SelectAsync("articles", $"select=slug&bout_slug=eq.{Uri.EscapeDataString(boutSlug)}", ct))
            .Select(a => a?["slug"]?.GetValue<string>()).Where(s => s is not null).ToHashSet();
        var slug = baseSlug;
        for (var n = 2; taken.Contains(slug); n++) slug = $"{baseSlug}-{n}";
        return slug;
    }

    /// <summary>
    /// Generates an article for any bout that has none (e.g. generation failed
    /// on the run that created the bout). Uses the bout's frozen snapshots.
    /// </summary>
    public async Task<int> RetryMissingArticlesAsync(CancellationToken ct = default)
    {
        var bouts = await _supabase.SelectAsync("bouts",
            "select=slug,status,weight_class,event_date,fighter_a_snapshot,fighter_b_snapshot", ct);
        var withArticles = (await _supabase.SelectAsync("articles", "select=bout_slug", ct))
            .Select(a => a?["bout_slug"]?.GetValue<string>())
            .ToHashSet();

        var recovered = 0;
        foreach (var row in bouts.OfType<JsonObject>())
        {
            var slug = row["slug"]!.GetValue<string>();
            if (withArticles.Contains(slug)) continue;

            var snapA = row["fighter_a_snapshot"]!.AsObject();
            var snapB = row["fighter_b_snapshot"]!.AsObject();

            // Recover the news material from the feed items that created this
            // bout — without it the writer only has stat blocks to work from.
            var (coverage, sources) = await CoverageFromSeenItemsAsync(slug, ct);
            var bout = new JsonObject
            {
                ["fighter_a"] = snapA["_meta"]?["name"]?.DeepClone(),
                ["fighter_b"] = snapB["_meta"]?["name"]?.DeepClone(),
                ["weightClass"] = row["weight_class"]?.DeepClone(),
                ["eventDate"] = row["event_date"]?.DeepClone(),
                ["fightStatus"] = row["status"]?.DeepClone(),
                ["coverage"] = coverage,
            };
            await HydrateCoverageAsync(bout["coverage"], ct);

            var article = await _articles.GenerateAsync(bout, snapA, snapB, ct);
            if (article is null)
            {
                _logger.LogWarning("Article retry failed for {Slug}", slug);
                continue;
            }

            var title = article["title"]?.GetValue<string>() ?? "Preview";
            await _supabase.InsertAsync("articles", new JsonObject
            {
                ["bout_slug"] = slug,
                ["slug"] = await UniqueArticleSlugAsync(slug, title, ct),
                ["status"] = row["status"]?.DeepClone(),
                ["title"] = article["title"]?.DeepClone(),
                ["summary"] = article["summary"]?.DeepClone(),
                ["body"] = article["body"]?.DeepClone(),
                ["tags"] = article["tags"]?.DeepClone(),
                ["ai_generated"] = true,
                ["published_at"] = DateTimeOffset.UtcNow.ToString("o"),
                ["sources"] = sources,
            }, ct);
            await _supabase.UpdateAsync("seen_feed_items",
                $"bout_slug=eq.{Uri.EscapeDataString(slug)}",
                new JsonObject { ["status"] = "article_created" }, ct);
            recovered++;
            _logger.LogInformation("Recovered article for {Slug}", slug);
        }
        return recovered;
    }

    /// <summary>
    /// Rebuilds the writer's coverage array (and a matching sources array for
    /// the articles row) from the seen_feed_items that were linked to this
    /// bout — each row's `extracted` column holds the full FightAnnouncement.
    /// Public so the preview-article CLI can reuse it.
    /// </summary>
    public async Task<(JsonArray Coverage, JsonArray Sources)> CoverageFromSeenItemsAsync(string boutSlug, CancellationToken ct = default)
    {
        var coverage = new JsonArray();
        var sources = new JsonArray();
        var seenUrls = new HashSet<string>();

        var rows = await _supabase.SelectAsync("seen_feed_items",
            $"select=extracted&bout_slug=eq.{Uri.EscapeDataString(boutSlug)}&order=first_seen_at.asc", ct);
        foreach (var row in rows.OfType<JsonObject>())
        {
            if (row["extracted"] is not JsonObject ex) continue;
            // extracted is a camelCase-serialized FightAnnouncement.
            var reports = new List<(string? Source, string? Url, string? Headline, string? Summary)>();
            if (ex["sourceReports"] is JsonArray reps && reps.Count > 0)
            {
                foreach (var r in reps.OfType<JsonObject>())
                {
                    reports.Add((r["source"]?.GetValue<string>(), r["url"]?.GetValue<string>(),
                                 r["headline"]?.GetValue<string>(), r["summary"]?.GetValue<string>()));
                }
            }
            else
            {
                reports.Add((ex["sourceName"]?.GetValue<string>(), ex["sourceUrl"]?.GetValue<string>(),
                             ex["rawHeadline"]?.GetValue<string>(), ex["articleBody"]?.GetValue<string>()));
            }

            foreach (var (src, url, headline, summary) in reports)
            {
                if (url is null || !seenUrls.Add(url)) continue;
                var cleaned = CleanSummary(summary);
                coverage.Add(new JsonObject { ["source"] = src, ["headline"] = headline, ["summary"] = cleaned, ["url"] = url });
                sources.Add(new JsonObject
                {
                    ["source"] = src,
                    ["url"] = url,
                    ["headline"] = headline,
                    ["summary"] = cleaned,
                    ["seen_at"] = DateTimeOffset.UtcNow.ToString("o"),
                });
            }
        }
        return (coverage, sources);
    }

    /// <summary>
    /// Handles a result report: if the fighter pair matches a bout we track,
    /// mark it completed with the result and write a result article (windowed
    /// like previews, so a second report of the same result appends as a
    /// source). Returns ("no_bout", null) when the pairing was never tracked.
    /// </summary>
    private async Task<(string Outcome, string? BoutSlug)> TryProcessResultAsync(FightAnnouncement item, CancellationToken ct)
    {
        // Resolve names to ids without creating anything — sparse is fine for lookup.
        var lookupA = await _wikipedia.BuildSnapshotAsync(item.Fighter1!, allowSparse: true, ct);
        var lookupB = await _wikipedia.BuildSnapshotAsync(item.Fighter2!, allowSparse: true, ct);
        if (lookupA is null || lookupB is null) return ("no_bout", null);

        var fidA = await _fighters.FighterIdAsync(lookupA["_meta"]!["name"]!.GetValue<string>(), lookupA["physical"]?["dob"]?.GetValue<string>(), ct);
        var fidB = await _fighters.FighterIdAsync(lookupB["_meta"]!["name"]!.GetValue<string>(), lookupB["physical"]?["dob"]?.GetValue<string>(), ct);

        var slugs = $"{fidA}-vs-{fidB},{fidB}-vs-{fidA}";
        var existing = await _supabase.SelectAsync("bouts",
            $"select=slug,status,weight_class,event_date,fighter_a_snapshot,fighter_b_snapshot,result&slug=in.({Uri.EscapeDataString(slugs)})", ct);
        if (existing.Count == 0) return ("no_bout", null);

        var row = existing[0]!.AsObject();
        var boutSlug = row["slug"]!.GetValue<string>();

        // First result report flips the bout; later ones don't overwrite it.
        if (row["status"]?.GetValue<string>() != "completed")
        {
            var result = new JsonObject
            {
                ["winner"] = item.ResultWinner,
                ["method"] = item.ResultMethod,
                ["round"] = item.ResultRound,
                ["sourceUrl"] = item.SourceUrl,
            };
            var patch = new JsonObject { ["status"] = "completed", ["result"] = result };
            // Many bouts sat at TBC before the fight — the result report is
            // often the first thing that dates them (schedule's "Recent
            // results" sorts on this).
            if (row["event_date"] is null && item.EventDate is { } fought)
            {
                patch["event_date"] = fought.ToString("yyyy-MM-dd");
            }
            await _supabase.UpdateAsync("bouts", $"slug=eq.{Uri.EscapeDataString(boutSlug)}", patch, ct);
            _logger.LogInformation("Marked bout {Slug} completed ({Winner} by {Method})",
                boutSlug, item.ResultWinner ?? "result", item.ResultMethod ?? "unstated method");
        }

        // The article works from the bout's FROZEN snapshots — for a result
        // piece they are "what the tape said beforehand", which is the point.
        var snapA = row["fighter_a_snapshot"]!.AsObject();
        var snapB = row["fighter_b_snapshot"]!.AsObject();
        var bout = new JsonObject
        {
            ["fighter_a"] = snapA["_meta"]?["name"]?.DeepClone(),
            ["fighter_b"] = snapB["_meta"]?["name"]?.DeepClone(),
            ["weightClass"] = row["weight_class"]?.DeepClone(),
            ["eventDate"] = row["event_date"]?.DeepClone(),
            ["headline"] = item.RawHeadline,
            ["source"] = item.MergedFromSources is { Count: > 0 } m ? string.Join(", ", m) : item.SourceName,
            ["link"] = item.SourceUrl,
            ["fightStatus"] = "result",
            ["result"] = new JsonObject
            {
                ["winner"] = item.ResultWinner ?? row["result"]?["winner"]?.GetValue<string>(),
                ["method"] = item.ResultMethod ?? row["result"]?["method"]?.GetValue<string>(),
                ["round"] = item.ResultRound ?? row["result"]?["round"]?.GetValue<int>(),
            },
            ["coverage"] = CoverageFromItem(item),
        };

        var outcome = await UpsertArticleForBoutAsync(boutSlug, bout, snapA, snapB, item, ct);
        return (outcome, boutSlug);
    }

    /// <summary>Marks the existing bout row (either fighter order) cancelled. Returns false when no bout exists for this pairing.</summary>
    private async Task<bool> TryCancelExistingBoutAsync(FightAnnouncement item, CancellationToken ct)
    {
        // Resolve names to ids the same way bout creation does, but without
        // creating anything — sparse snapshots are fine for a slug lookup.
        var snapA = await _wikipedia.BuildSnapshotAsync(item.Fighter1!, allowSparse: true, ct);
        var snapB = await _wikipedia.BuildSnapshotAsync(item.Fighter2!, allowSparse: true, ct);
        if (snapA is null || snapB is null) return false;

        var fidA = await _fighters.FighterIdAsync(snapA["_meta"]!["name"]!.GetValue<string>(), snapA["physical"]?["dob"]?.GetValue<string>(), ct);
        var fidB = await _fighters.FighterIdAsync(snapB["_meta"]!["name"]!.GetValue<string>(), snapB["physical"]?["dob"]?.GetValue<string>(), ct);

        var slugs = $"{fidA}-vs-{fidB},{fidB}-vs-{fidA}";
        var existing = await _supabase.SelectAsync("bouts", $"select=slug&slug=in.({Uri.EscapeDataString(slugs)})", ct);
        if (existing.Count == 0) return false;

        var slug = existing[0]!["slug"]!.GetValue<string>();
        await _supabase.UpdateAsync("bouts", $"slug=eq.{Uri.EscapeDataString(slug)}",
            new JsonObject { ["status"] = "cancelled" }, ct);
        _logger.LogInformation("Marked bout {Slug} cancelled", slug);
        return true;
    }

    /// <summary>
    /// Null = process it; otherwise the ignore_reason to record. Result items
    /// (IsUpcoming == false) never reach here — they take the result path in
    /// ProcessAsync.
    /// </summary>
    private static string? DecideIgnoreReason(FightAnnouncement item)
    {
        if (item.EventDate is { } date && date < DateTimeOffset.UtcNow.AddDays(-1))
        {
            return $"event date {date:yyyy-MM-dd} is in the past";
        }

        // No LLM verdict (regex-only extraction) — fall back to a headline
        // heuristic so obvious results don't get preview articles.
        if (item.IsUpcoming is null && PastResultRegex.IsMatch(item.RawHeadline))
        {
            return "headline reads as a result, not an announcement";
        }

        return null;
    }
}

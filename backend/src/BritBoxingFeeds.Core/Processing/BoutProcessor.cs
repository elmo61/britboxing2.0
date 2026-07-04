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
    private readonly ILogger<BoutProcessor> _logger;

    public BoutProcessor(
        SupabaseClient supabase,
        WikipediaSnapshotService wikipedia,
        FighterStore fighters,
        ArticleGenerator articles,
        SeenFeedItemsStore seenStore,
        ILogger<BoutProcessor> logger)
    {
        _supabase = supabase;
        _wikipedia = wikipedia;
        _fighters = fighters;
        _articles = articles;
        _seenStore = seenStore;
        _logger = logger;
    }

    public record ProcessSummary(int Considered, int Ignored, int BoutsCreated, int ArticlesCreated, int ArticlesAppended, int AlreadyExisted);

    // A fight announced now shows up across many feeds within days; those all
    // roll into ONE article (citing multiple sources). A fresh item after this
    // window opens a NEW article on the same bout.
    private static readonly TimeSpan ArticleWindow = TimeSpan.FromDays(7);

    /// <summary>Candidates carry the seen-row key they were loaded from (null for items found this run) so status updates hit the right row.</summary>
    public async Task<ProcessSummary> ProcessAsync(IReadOnlyList<(FightAnnouncement Item, string? SourceKey)> candidates, CancellationToken ct = default)
    {
        int ignored = 0, boutsCreated = 0, articlesCreated = 0, articlesAppended = 0, alreadyExisted = 0;

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
            // just built. Used whether the bout is new or already exists.
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
            else { _logger.LogWarning("Article generation failed for {Slug}", boutSlug); }

            await _seenStore.SetStatusAsync(item, outcome == "failed" ? "bout_created" : "article_created",
                boutSlug: boutSlug, sourceKey: sourceKey, ct: ct);
        }

        return new ProcessSummary(candidates.Count, ignored, boutsCreated, articlesCreated, articlesAppended, alreadyExisted);
    }

    /// <summary>
    /// Adds this feed item's coverage to the bout. If the bout has an article
    /// whose window is still open (published within ArticleWindow), the item
    /// is recorded as another source on it — no new article, no extra AI spend.
    /// Otherwise a fresh article is generated. Returns "created", "appended" or "failed".
    /// </summary>
    private async Task<string> UpsertArticleForBoutAsync(string boutSlug, JsonObject bout, JsonObject snapA, JsonObject snapB, FightAnnouncement item, CancellationToken ct)
    {
        var sourceEntry = new JsonObject
        {
            ["source"] = item.MergedFromSources is { Count: > 0 } m ? string.Join(", ", m) : item.SourceName,
            ["url"] = item.SourceUrl,
            ["headline"] = item.RawHeadline,
            ["seen_at"] = DateTimeOffset.UtcNow.ToString("o"),
        };

        var latest = await _supabase.SelectAsync("articles",
            $"select=id,published_at,sources&bout_slug=eq.{Uri.EscapeDataString(boutSlug)}&order=published_at.desc&limit=1", ct);

        if (latest.Count > 0)
        {
            var art = latest[0]!.AsObject();
            var publishedAt = DateTimeOffset.TryParse(art["published_at"]?.GetValue<string>(), out var p) ? p : DateTimeOffset.MinValue;
            if (DateTimeOffset.UtcNow - publishedAt <= ArticleWindow)
            {
                var sources = art["sources"]?.DeepClone().AsArray() ?? [];
                var already = !string.IsNullOrEmpty(item.SourceUrl)
                    && sources.Any(s => s?["url"]?.GetValue<string>() == item.SourceUrl);
                if (!already)
                {
                    sources.Add(sourceEntry.DeepClone());
                    var id = art["id"]!.GetValue<long>();
                    await _supabase.UpdateAsync("articles", $"id=eq.{id}", new JsonObject { ["sources"] = sources }, ct);
                }
                return "appended";
            }
        }

        var article = await _articles.GenerateAsync(bout, snapA, snapB, ct);
        if (article is null) return "failed";

        await _supabase.InsertAsync("articles", new JsonObject
        {
            ["bout_slug"] = boutSlug,
            ["title"] = article["title"]?.DeepClone(),
            ["summary"] = article["summary"]?.DeepClone(),
            ["body"] = article["body"]?.DeepClone(),
            ["tags"] = article["tags"]?.DeepClone(),
            ["ai_generated"] = true,
            ["published_at"] = DateTimeOffset.UtcNow.ToString("o"),
            ["sources"] = new JsonArray(sourceEntry.DeepClone()),
        }, ct);
        return "created";
    }

    /// <summary>
    /// Generates an article for any bout that has none (e.g. generation failed
    /// on the run that created the bout). Uses the bout's frozen snapshots.
    /// </summary>
    public async Task<int> RetryMissingArticlesAsync(CancellationToken ct = default)
    {
        var bouts = await _supabase.SelectAsync("bouts",
            "select=slug,weight_class,event_date,fighter_a_snapshot,fighter_b_snapshot", ct);
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
            var bout = new JsonObject
            {
                ["fighter_a"] = snapA["_meta"]?["name"]?.DeepClone(),
                ["fighter_b"] = snapB["_meta"]?["name"]?.DeepClone(),
                ["weightClass"] = row["weight_class"]?.DeepClone(),
                ["eventDate"] = row["event_date"]?.DeepClone(),
            };

            var article = await _articles.GenerateAsync(bout, snapA, snapB, ct);
            if (article is null)
            {
                _logger.LogWarning("Article retry failed for {Slug}", slug);
                continue;
            }

            await _supabase.InsertAsync("articles", new JsonObject
            {
                ["bout_slug"] = slug,
                ["title"] = article["title"]?.DeepClone(),
                ["summary"] = article["summary"]?.DeepClone(),
                ["body"] = article["body"]?.DeepClone(),
                ["tags"] = article["tags"]?.DeepClone(),
                ["ai_generated"] = true,
                ["published_at"] = DateTimeOffset.UtcNow.ToString("o"),
                ["sources"] = new JsonArray(),
            }, ct);
            await _supabase.UpdateAsync("seen_feed_items",
                $"bout_slug=eq.{Uri.EscapeDataString(slug)}",
                new JsonObject { ["status"] = "article_created" }, ct);
            recovered++;
            _logger.LogInformation("Recovered article for {Slug}", slug);
        }
        return recovered;
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

    /// <summary>Null = process it; otherwise the ignore_reason to record.</summary>
    private static string? DecideIgnoreReason(FightAnnouncement item)
    {
        if (item.IsUpcoming == false)
        {
            return "reports a fight that already happened";
        }

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

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

    public record ProcessSummary(int Considered, int Ignored, int BoutsCreated, int ArticlesCreated, int AlreadyExisted);

    /// <summary>Candidates carry the seen-row key they were loaded from (null for items found this run) so status updates hit the right row.</summary>
    public async Task<ProcessSummary> ProcessAsync(IReadOnlyList<(FightAnnouncement Item, string? SourceKey)> candidates, CancellationToken ct = default)
    {
        int ignored = 0, boutsCreated = 0, articlesCreated = 0, alreadyExisted = 0;

        foreach (var (item, sourceKey) in candidates)
        {
            if (string.IsNullOrWhiteSpace(item.Fighter1) || string.IsNullOrWhiteSpace(item.Fighter2))
            {
                continue; // not fight-keyed; nothing to decide
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
                $"select=slug&slug=in.({Uri.EscapeDataString($"{slug},{reversedSlug}")})", ct);
            if (existing.Count > 0)
            {
                slug = existing[0]!["slug"]!.GetValue<string>();
                var existingArticle = await _supabase.SelectAsync("articles", $"select=slug&slug=eq.{Uri.EscapeDataString(slug)}", ct);
                await _seenStore.SetStatusAsync(item,
                    existingArticle.Count > 0 ? "article_created" : "bout_created",
                    boutSlug: slug, sourceKey: sourceKey, ct: ct);
                _logger.LogInformation("Bout {Slug} already exists, linked and skipped", slug);
                alreadyExisted++;
                continue;
            }

            // The bout context handed to the prompt builder (camelCase, same
            // shape as the Python pipeline's bout dict).
            var bout = new JsonObject
            {
                ["fighter_a"] = snapA["_meta"]!["name"]!.GetValue<string>(),
                ["fighter_b"] = snapB["_meta"]!["name"]!.GetValue<string>(),
                ["fighterAId"] = fidA,
                ["fighterBId"] = fidB,
                ["weightClass"] = WikipediaSnapshotService.BoutWeightClass(snapA, snapB) ?? item.WeightClass,
                ["eventDate"] = item.EventDate?.ToString("yyyy-MM-dd"),
                ["announcedAt"] = item.PublishedAt?.ToString("o"),
                ["headline"] = item.RawHeadline,
                ["source"] = item.MergedFromSources is { Count: > 0 } merged ? string.Join(", ", merged) : item.SourceName,
                ["link"] = item.SourceUrl,
            };

            var boutRow = new JsonObject
            {
                ["slug"] = slug,
                ["fighter_a_id"] = fidA,
                ["fighter_b_id"] = fidB,
                ["weight_class"] = bout["weightClass"]?.DeepClone(),
                ["event_date"] = bout["eventDate"]?.DeepClone(),
                ["announced_at"] = bout["announcedAt"]?.DeepClone(),
                ["headline"] = item.RawHeadline,
                ["source"] = bout["source"]?.DeepClone(),
                ["source_url"] = item.SourceUrl,
                // FROZEN at announcement time — never rewritten when a fighter updates.
                ["fighter_a_snapshot"] = snapA.DeepClone(),
                ["fighter_b_snapshot"] = snapB.DeepClone(),
                ["prompt"] = ArticleGenerator.BuildPromptRecord(bout, snapA, snapB),
            };
            await _supabase.UpsertAsync("bouts", [boutRow], "slug", ct);
            await _seenStore.SetStatusAsync(item, "bout_created", boutSlug: slug, sourceKey: sourceKey, ct: ct);
            boutsCreated++;
            _logger.LogInformation("Created bout {Slug}", slug);

            // ---- Article ------------------------------------------------
            var article = await _articles.GenerateAsync(bout, snapA, snapB, ct);
            if (article is null)
            {
                // Left at bout_created — a later run can regenerate from bouts.prompt.
                _logger.LogWarning("Article generation failed for {Slug}; bout saved without article", slug);
                continue;
            }

            var articleRow = new JsonObject
            {
                // slug must equal the bout slug (FK) — the model's suggested slug is ignored.
                ["slug"] = slug,
                ["title"] = article["title"]?.DeepClone(),
                ["summary"] = article["summary"]?.DeepClone(),
                ["body"] = article["body"]?.DeepClone(),
                ["tags"] = article["tags"]?.DeepClone(),
                ["ai_generated"] = true,
            };
            await _supabase.UpsertAsync("articles", [articleRow], "slug", ct);
            await _seenStore.SetStatusAsync(item, "article_created", boutSlug: slug, sourceKey: sourceKey, ct: ct);
            articlesCreated++;
            _logger.LogInformation("Published article for {Slug}", slug);
        }

        return new ProcessSummary(candidates.Count, ignored, boutsCreated, articlesCreated, alreadyExisted);
    }

    /// <summary>
    /// Regenerate articles for bouts left at bout_created by an earlier run
    /// (article generation failed). Rebuilds the bout context from the bouts
    /// row itself, so no re-extraction or Wikipedia refetch is needed.
    /// </summary>
    public async Task<int> RetryMissingArticlesAsync(CancellationToken ct = default)
    {
        var bouts = await _supabase.SelectAsync("bouts",
            "select=slug,weight_class,event_date,announced_at,headline,source,source_url,fighter_a_snapshot,fighter_b_snapshot", ct);
        var articleSlugs = (await _supabase.SelectAsync("articles", "select=slug", ct))
            .Select(a => a?["slug"]?.GetValue<string>())
            .ToHashSet();

        var recovered = 0;
        foreach (var row in bouts.OfType<JsonObject>())
        {
            var slug = row["slug"]!.GetValue<string>();
            if (articleSlugs.Contains(slug)) continue;

            var snapA = row["fighter_a_snapshot"]!.AsObject();
            var snapB = row["fighter_b_snapshot"]!.AsObject();
            var bout = new JsonObject
            {
                ["fighter_a"] = snapA["_meta"]?["name"]?.DeepClone(),
                ["fighter_b"] = snapB["_meta"]?["name"]?.DeepClone(),
                ["weightClass"] = row["weight_class"]?.DeepClone(),
                ["eventDate"] = row["event_date"]?.DeepClone(),
                ["announcedAt"] = row["announced_at"]?.DeepClone(),
                ["headline"] = row["headline"]?.DeepClone(),
                ["source"] = row["source"]?.DeepClone(),
                ["link"] = row["source_url"]?.DeepClone(),
            };

            var article = await _articles.GenerateAsync(bout, snapA, snapB, ct);
            if (article is null)
            {
                _logger.LogWarning("Article retry failed for {Slug}", slug);
                continue;
            }

            await _supabase.UpsertAsync("articles", [new JsonObject
            {
                ["slug"] = slug,
                ["title"] = article["title"]?.DeepClone(),
                ["summary"] = article["summary"]?.DeepClone(),
                ["body"] = article["body"]?.DeepClone(),
                ["tags"] = article["tags"]?.DeepClone(),
                ["ai_generated"] = true,
            }], "slug", ct);
            await _supabase.UpdateAsync("seen_feed_items",
                $"bout_slug=eq.{Uri.EscapeDataString(slug)}",
                new JsonObject { ["status"] = "article_created" }, ct);
            recovered++;
            _logger.LogInformation("Recovered article for {Slug}", slug);
        }
        return recovered;
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

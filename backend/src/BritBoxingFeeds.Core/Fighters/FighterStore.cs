using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BritBoxingFeeds.Core.Supabase;

namespace BritBoxingFeeds.Core.Fighters;

/// <summary>
/// The canonical fighter "table" against Supabase — port of
/// pipeline/fighters.py (stable slug IDs, DOB disambiguation) plus
/// supabase_db.py's fighter_row shaping (promoted columns mirroring
/// db/schema.sql). Every bout creation refreshes the fighter's
/// latest_snapshot here, so the table stays current; the snapshot frozen
/// inside the bout row never changes.
/// </summary>
public class FighterStore
{
    private readonly SupabaseClient _supabase;

    public FighterStore(SupabaseClient supabase)
    {
        _supabase = supabase;
    }

    /// <summary>Stable id: ascii-folded, dashed name; disambiguated by birth year only when an existing *different* fighter (different DOB) already owns the slug.</summary>
    public async Task<string> FighterIdAsync(string name, string? dob, CancellationToken ct = default)
    {
        var baseId = Slug(name);
        var existing = await _supabase.SelectAsync("fighters", $"select=id,dob&id=eq.{Uri.EscapeDataString(baseId)}", ct);
        if (existing.Count > 0 && dob is not null)
        {
            var existingDob = existing[0]?["dob"]?.GetValue<string>();
            if (existingDob is not null && existingDob != dob)
            {
                return $"{baseId}-{dob[..4]}"; // genuine name clash between two people
            }
        }
        return baseId;
    }

    /// <summary>
    /// Create or update the fighter row for this snapshot and return the
    /// fighter id. Keeps the richest data seen: a sparse (no-Wikipedia)
    /// snapshot never overwrites a row that already has real data.
    /// </summary>
    public async Task<string> UpsertAsync(JsonObject snapshot, CancellationToken ct = default)
    {
        var meta = snapshot["_meta"]!.AsObject();
        var name = meta["name"]!.GetValue<string>();
        var dob = snapshot["physical"]?["dob"]?.GetValue<string>();
        var id = await FighterIdAsync(name, dob, ct);
        var hasWikipedia = meta["hasWikipedia"]?.GetValue<bool>() ?? false;

        if (!hasWikipedia)
        {
            var existing = await _supabase.SelectAsync("fighters", $"select=id,has_wikipedia&id=eq.{Uri.EscapeDataString(id)}", ct);
            if (existing.Count > 0)
            {
                return id; // sparse data, richer row already there — leave it alone
            }
        }

        var record = snapshot["record"]!.AsObject();
        var physical = snapshot["physical"]!.AsObject();
        var row = new JsonObject
        {
            ["id"] = id,
            ["name"] = name,
            ["dob"] = dob,
            ["nationality"] = meta["nationality"]?.DeepClone(),
            ["wikipedia_title"] = hasWikipedia
                ? meta["source"]?.GetValue<string>()?.Split('/').LastOrDefault()
                : null,
            ["has_wikipedia"] = hasWikipedia,
            ["wins"] = record["wins"]?.DeepClone(),
            ["losses"] = record["losses"]?.DeepClone(),
            ["draws"] = record["draws"]?.DeepClone(),
            ["wins_ko"] = record["winsKo"]?.DeepClone(),
            ["wins_dec"] = record["winsDec"]?.DeepClone(),
            ["no_contests"] = record["noContests"]?.DeepClone(),
            ["age"] = physical["age"]?.DeepClone(),
            ["stance"] = physical["stance"]?.DeepClone(),
            ["height_inches"] = physical["heightInches"]?.DeepClone(),
            ["reach_inches"] = physical["reachInches"]?.DeepClone(),
            ["weight_classes"] = meta["weightClasses"]?.DeepClone(),
            ["latest_snapshot"] = snapshot.DeepClone(),
            ["updated_at"] = DateTimeOffset.UtcNow.ToString("o"),
        };

        await _supabase.UpsertAsync("fighters", [row], "id", ct);
        return id;
    }

    private static string Slug(string name)
    {
        var raw = Regex.Replace(name, @"\([^)]*\)", ""); // drop "(boxer)" disambiguation
        var normalized = raw.Normalize(NormalizationForm.FormKD);
        var ascii = new string(normalized.Where(c => c < 128).ToArray());
        // Drop apostrophes/periods rather than turning them into separators, so
        // "M'billi" == "Mbilli" and "St. Pierre" == "St Pierre".
        ascii = ascii.Replace("'", "").Replace("’", "").Replace(".", "");
        return Regex.Replace(ascii.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
    }
}

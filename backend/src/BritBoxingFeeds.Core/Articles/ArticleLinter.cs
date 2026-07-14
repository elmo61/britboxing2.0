using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BritBoxingFeeds.Core.Articles;

/// <summary>
/// Deterministic enforcement of the house style the system prompt asks for.
/// The model usually complies; this guarantees the hard rules (no em/en
/// dashes, straight quotes) can't slip into the database, and surfaces the
/// softer AI-tell phrases as warnings so prompt drift is visible in the logs.
/// </summary>
public static class ArticleLinter
{
    // Softer tells: detected and reported, not auto-fixed (a rewrite needs the
    // model, not a regex). Mirrors the system prompt's ban list.
    private static readonly Regex[] TellPatterns =
    [
        new(@"\bnot just \w[\w\s]{0,30}, but\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bit'?s not [^.;]{1,40}, it'?s\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bnot (?:a|an|so much) [^.;]{1,40} (?:as|but) (?:a|an)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bthe question is whether\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\b(testament to|stark reminder|delve|tapestry|underscores?|boasts a record)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\b(That said|Ultimately|It'?s worth noting|When it comes to|In a world where)\b", RegexOptions.Compiled),
    ];

    public record LintResult(int FixCount, IReadOnlyList<string> Warnings);

    /// <summary>
    /// Fixes the hard-rule violations in place across the article's string
    /// fields and returns what was fixed plus any remaining soft warnings.
    /// </summary>
    public static LintResult Lint(JsonObject article)
    {
        var fixes = 0;
        var warnings = new List<string>();

        foreach (var key in new[] { "title", "summary", "body" })
        {
            if (article[key]?.GetValue<string>() is not { } text) continue;
            var (clean, fixCount) = FixHardRules(text);
            if (fixCount > 0) { article[key] = clean; fixes += fixCount; }

            foreach (var pattern in TellPatterns)
            {
                var m = pattern.Match(clean);
                if (m.Success) warnings.Add($"{key}: banned phrase \"{m.Value}\"");
            }
        }

        if (article["tags"] is JsonArray tags)
        {
            for (var i = 0; i < tags.Count; i++)
            {
                if (tags[i]?.GetValue<string>() is not { } tag) continue;
                var (clean, fixCount) = FixHardRules(tag);
                if (fixCount > 0) { tags[i] = clean; fixes += fixCount; }
            }
        }

        return new LintResult(fixes, warnings);
    }

    /// <summary>Em/en dashes, curly quotes and ellipsis characters — always safe to fix mechanically.</summary>
    internal static (string Text, int Fixes) FixHardRules(string text)
    {
        var fixes = 0;
        var sb = new System.Text.StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            switch (ch)
            {
                case '—': // em dash
                case '–': // en dash
                    fixes++;
                    // " — " / "—" both become ", " (drop surrounding spaces).
                    while (sb.Length > 0 && sb[^1] == ' ') sb.Length--;
                    sb.Append(", ");
                    while (i + 1 < text.Length && text[i + 1] == ' ') i++;
                    break;
                case '‘': case '’': fixes++; sb.Append('\''); break;
                case '“': case '”': fixes++; sb.Append('"'); break;
                case '…': fixes++; sb.Append("..."); break;
                default: sb.Append(ch); break;
            }
        }
        return (sb.ToString(), fixes);
    }
}

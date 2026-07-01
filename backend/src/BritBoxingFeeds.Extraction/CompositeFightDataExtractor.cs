using BritBoxingFeeds.Core.Models;
using Microsoft.Extensions.Logging;

namespace BritBoxingFeeds.Extraction;

/// <summary>
/// Runs the free regex pass first. Only calls the (paid, slower) LLM
/// extractor if the regex pass didn't get both fighter names AND an event
/// date — the two fields that matter most for actually scheduling/matching
/// a fight. This keeps API spend down: clean "X vs Y, July 20" headlines
/// from BBC/BoxingScene resolve for free; messier headlines or anything
/// needing the article body get the LLM treatment.
/// </summary>
public class CompositeFightDataExtractor : IFightDataExtractor
{
    private readonly RegexFightDataExtractor _regexExtractor;
    private readonly AnthropicFightDataExtractor _llmExtractor;
    private readonly ILogger<CompositeFightDataExtractor> _logger;

    public CompositeFightDataExtractor(
        RegexFightDataExtractor regexExtractor,
        AnthropicFightDataExtractor llmExtractor,
        ILogger<CompositeFightDataExtractor> logger)
    {
        _regexExtractor = regexExtractor;
        _llmExtractor = llmExtractor;
        _logger = logger;
    }

    public async Task<FightAnnouncement> ExtractAsync(FightAnnouncement raw, CancellationToken ct = default)
    {
        var afterRegex = await _regexExtractor.ExtractAsync(raw, ct);

        var hasCoreFields = afterRegex.Fighter1 is not null
            && afterRegex.Fighter2 is not null
            && afterRegex.EventDate is not null;

        if (hasCoreFields)
        {
            return afterRegex;
        }

        _logger.LogInformation("Regex pass incomplete for '{Headline}', falling back to LLM", raw.RawHeadline);
        return await _llmExtractor.ExtractAsync(afterRegex, ct);
    }
}

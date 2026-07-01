using Microsoft.Extensions.Logging;

namespace BritBoxingFeeds.Core.Deploy;

/// <summary>
/// Kicks off a rebuild of the statically-generated Nuxt site by POSTing the
/// host's deploy hook (Render static site). The site bakes Supabase content
/// into HTML at build time, so a rebuild is what actually publishes new
/// bouts/articles — the pipeline calls this only on runs that created
/// something. No-op when RENDER_DEPLOY_HOOK_URL isn't configured, and a
/// failed hook is logged rather than failing the run (the content is safely
/// in the DB; the next successful hook picks it up).
/// </summary>
public class SiteDeployTrigger
{
    private readonly HttpClient _http;
    private readonly ILogger<SiteDeployTrigger> _logger;
    private readonly string? _hookUrl;

    public bool Enabled => _hookUrl is not null;

    public SiteDeployTrigger(HttpClient http, ILogger<SiteDeployTrigger> logger)
    {
        _http = http;
        _logger = logger;
        _hookUrl = Environment.GetEnvironmentVariable("RENDER_DEPLOY_HOOK_URL");
    }

    public async Task TriggerAsync(CancellationToken ct = default)
    {
        if (!Enabled)
        {
            _logger.LogInformation("RENDER_DEPLOY_HOOK_URL not set — skipping site rebuild trigger");
            return;
        }

        try
        {
            var response = await _http.PostAsync(_hookUrl, content: null, ct);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Site rebuild triggered");
            }
            else
            {
                _logger.LogWarning("Site rebuild trigger failed [{Status}]", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Site rebuild trigger failed");
        }
    }
}

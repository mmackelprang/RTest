using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Infrastructure.External;

namespace Radio.Infrastructure.DependencyInjection;

/// <summary>
/// Registers server-side GV media fetch and caching (ADR-029 D3, D8).
/// </summary>
/// <remarks>
/// A standalone extension rather than an addition to AddSoundFlowAudio, deliberately. ADR-029 §5
/// says "beside its sibling in AudioServiceExtensions.cs", but that sibling is registered inside
/// AddPhoneIntegration, which is only reachable through AddSoundFlowAudio — so registering there
/// would bury a feature with its own Enabled flag inside the audio graph, and any test that
/// resolved it would initialise real audio hardware. That is exactly why
/// ActiveSourceAccessorRegistrationTests can only inspect descriptors, and therefore exactly why no
/// guard in this repo would catch a missing registration today. Keeping this separate is what makes
/// GvMediaRegistrationTests a real build-and-resolve guard.
/// </remarks>
public static class GvMediaServiceExtensions
{
  /// <summary>
  /// Registers <see cref="GvMediaClient"/>, its bounded on-disk cache, the API-side
  /// X-RotaryPhone-Auth handler, and the boot check ADR-029 §10.2 asks for.
  /// </summary>
  public static IServiceCollection AddGvMedia(
    this IServiceCollection services,
    IConfiguration configuration)
  {
    services.Configure<GvMediaOptions>(configuration.GetSection(GvMediaOptions.SectionName));

    services.AddTransient<GvMediaAuthHandler>();
    services.AddSingleton<GvMediaCache>();

    var options = configuration.GetSection(GvMediaOptions.SectionName).Get<GvMediaOptions>()
      ?? new GvMediaOptions();

    services
      .AddHttpClient<GvMediaClient>(client =>
      {
        // BaseAddress is deliberately NOT set. GvMediaClient builds one absolute, validated URI in
        // exactly one place; a BaseAddress would create a second, implicit resolution site with the
        // RFC 3986 relative-reference hazard PR 1's review found.
        client.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.FetchTimeoutSeconds));
      })
      .AddHttpMessageHandler<GvMediaAuthHandler>();

    services.AddHostedService<GvMediaStartupCheck>();

    return services;
  }
}

/// <summary>
/// Logs the one boot warning ADR-029 §10.2 requires: GvMedia:Enabled true with an empty AuthKey.
/// </summary>
/// <remarks>
/// It also warns on the specific divergence §10.2 names as the real cost of D8 — the same secret
/// living under two keys, where a mismatch surfaces only as a 401 on voicemail playback.
///
/// ⚠ What that second branch can and cannot see, stated precisely because the obvious assumption is
/// false: this check reads RotaryPhone:Gv:AuthKey from Radio.API's OWN configuration. On the
/// appliance the two services do NOT share one per-machine overlay — /opt/radio-console/api/ and
/// /opt/radio-console/web/ each hold their own appsettings.Production.json, and they diverged
/// months ago because Deploy-ToLinux.ps1 excludes that file from rsync and only seeds it when it is
/// absent. Radio.Web's key is therefore invisible here unless it has also been placed in Radio.API's
/// own configuration or environment (RotaryPhone__Gv__AuthKey). So the "set under the other key"
/// branch is the narrow case, and the plain "AuthKey is empty" warning is what normally fires —
/// which is still the correct message. The branch is kept because it is cheap and it is right
/// whenever the key IS visible.
/// </remarks>
internal sealed class GvMediaStartupCheck : IHostedService
{
  private readonly ILogger<GvMediaStartupCheck> _logger;
  private readonly IOptionsMonitor<GvMediaOptions> _options;
  private readonly IConfiguration _configuration;

  public GvMediaStartupCheck(
    ILogger<GvMediaStartupCheck> logger,
    IOptionsMonitor<GvMediaOptions> options,
    IConfiguration configuration)
  {
    _logger = logger;
    _options = options;
    _configuration = configuration;
  }

  public Task StartAsync(CancellationToken cancellationToken)
  {
    var options = _options.CurrentValue;
    if (!options.Enabled)
    {
      return Task.CompletedTask;
    }

    if (string.IsNullOrEmpty(options.AuthKey))
    {
      var webKey = _configuration.GetValue<string>("RotaryPhone:Gv:AuthKey");
      if (!string.IsNullOrEmpty(webKey))
      {
        _logger.LogWarning(
          "GvMedia:Enabled is true and GvMedia:AuthKey is empty, but RotaryPhone:Gv:AuthKey is set. "
          + "These are the same secret under two keys; voicemail fetches will fail with 401 until "
          + "GvMedia:AuthKey matches it in appsettings.Production.json.");
      }
      else
      {
        _logger.LogWarning(
          "GvMedia:Enabled is true and GvMedia:AuthKey is empty. This is correct only while "
          + "RotaryPhone's /api/gvbridge/* auth gate is off; set the key when it ships.");
      }
    }

    return Task.CompletedTask;
  }

  public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Services;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;

namespace Radio.Web.Tests.TestHelpers;

/// <summary>
/// PHN-11: registers what the incoming-call banner needs, offline, for tests that render one of its hosts
/// (<c>MainLayout</c>, the <c>/sleep</c> page) without caring about calls. Every registration is a
/// <c>TryAdd</c>, so a test that wires its own <see cref="PhoneHubService"/> or API clients keeps them.
/// </summary>
/// <remarks>
/// With nothing ringing the banner renders only its two empty live regions. Its start-up status read goes
/// to <see cref="NoNetworkHandler"/>, which fails it the way a stopped RotaryPhone would.
/// </remarks>
public static class IncomingCallBannerTestRig
{
  public static IServiceCollection AddOfflineIncomingCallBanner(this IServiceCollection services)
  {
    services.TryAddSingleton(sp => new PhoneHubService(
      NullLogger<PhoneHubService>.Instance,
      sp.GetService<IConfiguration>() ?? new ConfigurationBuilder().Build()));
    services.TryAddScoped(_ => new PhoneApiService(Offline(), NullLogger<PhoneApiService>.Instance));
    services.TryAddScoped(_ => new PbapApiService(Offline(), NullLogger<PbapApiService>.Instance));
    services.TryAddScoped(sp => new ContactResolutionService(
      sp.GetRequiredService<PbapApiService>(), NullLogger<ContactResolutionService>.Instance));
    services.TryAddScoped(sp => new IncomingCallBannerService(
      sp.GetRequiredService<PhoneHubService>(),
      sp.GetRequiredService<PhoneApiService>(),
      sp.GetService<IConfiguration>() ?? new ConfigurationBuilder().Build(),
      NullLogger<IncomingCallBannerService>.Instance,
      sp.GetRequiredService<ContactResolutionService>()));
    return services;
  }

  private static HttpClient Offline() =>
    new(new NoNetworkHandler()) { BaseAddress = new Uri(HermeticTestRig.PhoneApiBaseUrl) };
}

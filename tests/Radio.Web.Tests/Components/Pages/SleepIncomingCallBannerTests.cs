using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Radzen;
using Radio.Web.Components.Pages;
using Radio.Web.Models;
using Radio.Web.Services;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Components.Pages;

/// <summary>
/// PHN-11 on the <c>/sleep</c> page (spec §9): a call lights the panel by reporting the sleep screen hidden
/// while the banner covers it, does not wake the console, and reports the screen visible again when the
/// banner goes. The API's side of "hidden lights a dark panel" is <c>PanelPowerService</c>'s, pinned there.
/// </summary>
public class SleepIncomingCallBannerTests : TestContext
{
  private const string Number = "5550137424";
  private readonly IncomingCallBannerHarness _h = new();
  private readonly FakeNavigationManager _nav;

  public SleepIncomingCallBannerTests()
  {
    Services.AddHermeticTestRig();
    JSInterop.Mode = JSRuntimeMode.Loose;

    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?> { { "ApiBaseUrl", HermeticTestRig.ApiBaseUrl } })
      .Build();
    Services.AddSingleton<IConfiguration>(configuration);
    Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
    Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    Services.AddRadzenComponents();
    Services.Configure<DisplayOptions>(_ => { });
    Services.AddOptions<Radio.Core.Configuration.WeatherDisplayOptions>();

    // The sleep-screen reports and any wake go through the harness's handler, which records them.
    Services.AddSingleton(new SystemApiService(_h.Client, NullLogger<SystemApiService>.Instance));
    Services.AddHttpClient<AudioApiService>();
    Services.AddHttpClient<WeatherApiService>();
    Services.AddSingleton(sp => new AudioStateHubService(
      NullLogger<AudioStateHubService>.Instance, sp.GetRequiredService<IConfiguration>(),
      transport: new OfflineHubTransport()));
    Services.AddSingleton(sp => new EncoderHudService(
      sp.GetRequiredService<AudioStateHubService>(), new Microsoft.Extensions.Time.Testing.FakeTimeProvider()));
    Services.AddSingleton(_h.Service);

    _nav = (FakeNavigationManager)Services.GetRequiredService<NavigationManager>();
  }

  private string[] SleepScreenReports() =>
    _h.Phone.Requests.Where(r => r.Contains("/api/system/sleep-screen", StringComparison.Ordinal))
      .Select(r => r[(r.IndexOf('{'))..])
      .ToArray();

  private int WakeRequests() =>
    _h.Phone.Requests.Count(r => r.StartsWith("POST /api/system/sleep ", StringComparison.Ordinal));

  [Fact]
  public void ACallOnTheSleepScreen_ReportsItHidden_ThenVisibleAgainWhenTheBannerGoes()
  {
    var cut = RenderComponent<Sleep>();
    cut.WaitForAssertion(() => Assert.Equal(new[] { "{\"visible\":true}" }, SleepScreenReports()));

    _h.Hub.RaiseIncomingCallForTest("default", Number);
    cut.WaitForAssertion(() =>
      Assert.Equal(new[] { "{\"visible\":true}", "{\"visible\":false}" }, SleepScreenReports()));

    _h.Hub.RaiseCallStateChangedForTest("default", "Idle");
    _h.Time.Advance(IncomingCallBannerService.ExitHold);
    _h.Time.Advance(IncomingCallBannerService.ExitFade);

    cut.WaitForAssertion(() => Assert.Equal(
      new[] { "{\"visible\":true}", "{\"visible\":false}", "{\"visible\":true}" }, SleepScreenReports()));
    Assert.Equal(0, WakeRequests());
  }

  [Fact]
  public void ATouchOnTheBanner_ClosesOnlyTheBanner_AndNeverWakesTheConsole()
  {
    var cut = RenderComponent<Sleep>();
    _h.Hub.RaiseIncomingCallForTest("default", Number);
    cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".icb-scrim")));

    cut.Find(".icb-scrim").Click();
    _h.Time.Advance(IncomingCallBannerService.ExitFade);

    cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".icb-scrim")));
    // No wake request and no navigation home: the tap stopped at the banner (it is a sibling of the
    // .sleep-screen tap-to-wake surface, not a child of it).
    Assert.Equal(0, WakeRequests());
    Assert.Empty(_nav.History);
    Assert.Single(cut.FindAll(".sleep-screen"));
  }

  [Fact]
  public async Task ACallAlreadyRingingWhenThePageMounts_IsReportedHidden_AndNeverVisibleFirst()
  {
    // The idle timer (or a sleep press) taking the console to /sleep mid-ring. The banner's first
    // after-render runs before the page's, so the page must read the banner's state, not race it.
    _h.Phone.StatusJson = $"{{\"callState\":\"Ringing\",\"incomingNumber\":\"{Number}\"}}";
    _h.Start();
    await _h.Service.Seeded;

    var cut = RenderComponent<Sleep>();

    cut.WaitForAssertion(() => Assert.Equal(new[] { "{\"visible\":false}" }, SleepScreenReports()));
    Assert.Single(cut.FindAll(".icb-scrim"));
  }
}

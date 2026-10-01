using System.Net;
using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Radzen;
using Radio.Web.Components.Shared;
using Radio.Web.Models;
using Radio.Web.Services;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// The visualizer's BAND view (AUD-76 PR 2): the empty, aged and sweeping states, the Scan button,
/// the refresh cadence and tap-to-tune.
///
/// <para>
/// Every API call goes to a <see cref="RoutedApiHandler"/>, so the "server" is a table the test
/// controls. The saved preference is BAND, so the panel opens on it. The refresh timer is armed
/// against a <see cref="FakeTimeProvider"/>, and refreshes are counted through
/// <c>CompletedBandRefreshes</c> rather than timed (CLAUDE.md § Test Timing).
/// </para>
/// </summary>
public class VisualizerPanelBandTests : TestContext
{
  private readonly FakeTimeProvider _clock = new();
  private readonly RoutedApiHandler _api = new();

  public VisualizerPanelBandTests()
  {
    Services.AddHermeticTestRig();
    JSInterop.Mode = JSRuntimeMode.Loose;
    BunitJSModuleInterop module = JSInterop.SetupModule("./js/visualizer.js");
    module.Mode = JSRuntimeMode.Loose;
    module.Setup<bool>("visualizer.init", _ => true).SetResult(true);

    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?> { { "ApiBaseUrl", HermeticTestRig.ApiBaseUrl } })
      .Build();
    Services.AddSingleton<IConfiguration>(configuration);
    Services.AddSingleton<ILoggerFactory>(new NullLoggerFactory());
    Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    Services.AddRadzenComponents();

    Services.AddSingleton(_ => new ConfigurationApiService(Client(), NullLogger<ConfigurationApiService>.Instance));
    Services.AddSingleton(_ => new RadioApiService(Client(), NullLogger<RadioApiService>.Instance));
    Services.AddSingleton(_ => new SourcesApiService(Client(), NullLogger<SourcesApiService>.Instance));
    Services.AddSingleton(sp => new AudioVisualizationHubService(
      NullLogger<AudioVisualizationHubService>.Instance, sp.GetRequiredService<IConfiguration>(),
      transport: new OfflineHubTransport()));
    Services.AddSingleton(sp => new AudioStateHubService(
      NullLogger<AudioStateHubService>.Instance, sp.GetRequiredService<IConfiguration>(),
      transport: new OfflineHubTransport()));
    Services.AddSingleton<VisualizerTelemetryService>();

    // Defaults: BAND is the saved mode, the radio is not the active source, no presets, no map.
    GetJson("/api/configuration/ui.visualizer", new Dictionary<string, object> { ["defaultMode"] = "Band" });
    _api.Route(HttpMethod.Get, "/api/radio/state", HttpStatusCode.BadRequest, "{\"error\":\"Radio is not the active source\"}");
    GetJson("/api/radio/presets", Array.Empty<object>());
    GetJson("/api/radio/bandmap", EmptyMap());
    _api.Post("/api/sources");
    _api.Post("/api/radio/frequency");
    _api.Post("/api/radio/band");
  }

  private HttpClient Client() => new(_api, disposeHandler: false) { BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl) };

  private void GetJson(string path, object body) =>
    _api.Route(HttpMethod.Get, path, HttpStatusCode.OK, JsonSerializer.Serialize(body, JsonSerializerOptions.Web));

  private static object Sweep(bool isSweeping = false, double? remaining = null, double progress = 0) =>
    new { isSweeping, trigger = isSweeping ? "request" : null, path = isSweeping ? "idle" : null, progress, estimatedSecondsRemaining = remaining };

  private static object EmptyMap(bool isSweeping = false, double? remaining = null) => new
  {
    band = "FM",
    scannedAtUtc = (DateTimeOffset?)null,
    ageSeconds = (double?)null,
    channels = Array.Empty<object>(),
    sweep = Sweep(isSweeping, remaining),
  };

  /// <summary>A full map at -60 dB with a strong station at 99.5 MHz.</summary>
  private static object MapWithStation(double ageSeconds, bool isSweeping = false, double? remaining = null)
  {
    var channels = new List<object>();
    for (long hz = FmBandMath.FirstChannelHz; hz <= FmBandMath.LastChannelHz; hz += FmBandMath.ChannelSpacingHz)
    {
      channels.Add(new { frequencyHz = hz, levelDbfs = hz == 99_500_000 ? -30f : -60f });
    }

    return new
    {
      band = "FM",
      scannedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-ageSeconds),
      ageSeconds,
      channels,
      sweep = Sweep(isSweeping, remaining),
    };
  }

  private static object FmState(double frequencyHz, string band = "FM") => new
  {
    frequency = frequencyHz,
    band,
    step = 200_000,
    signalStrength = 50,
    isScanning = false,
    scanDirection = (string?)null,
    scanStopThreshold = -36.0,
    gain = 0,
    autoGain = false,
    equalizer = "Normal",
    deviceVolume = 50,
  };

  private IRenderedComponent<VisualizerPanel> RenderBand()
  {
    var cut = RenderComponent<VisualizerPanel>(p => p.Add(x => x.Clock, _clock));
    cut.WaitForAssertion(() =>
    {
      cut.FindAll(".band-overlay").Should().HaveCount(1, "the canvas initialised and BAND is the saved mode");
      cut.Instance.CompletedBandRefreshes.Should().BeGreaterThanOrEqualTo(1);
    }, TimeSpan.FromSeconds(5));
    return cut;
  }

  private void WaitForRefreshes(IRenderedComponent<VisualizerPanel> cut, int atLeast) =>
    cut.WaitForAssertion(() => cut.Instance.CompletedBandRefreshes.Should().BeGreaterThanOrEqualTo(atLeast),
      TimeSpan.FromSeconds(5));

  private int IndexOf(HttpMethod method, string path) =>
    _api.Requests.ToList().FindIndex(r => r.Method == method && r.Path == path);

  // ── states ───────────────────────────────────────────────────────────────

  [Fact]
  public void EmptyMap_ShowsNoScanYet_AndNotTheAudioWaitingOverlay()
  {
    var cut = RenderBand();

    cut.Find(".band-empty-title").TextContent.Should().Be("No scan yet");
    cut.FindAll(".visualizer-waiting").Should().BeEmpty("BAND has no audio stream to wait for");
    cut.FindAll(".band-status-age").Should().BeEmpty();
  }

  [Fact]
  public void StoredMap_ShowsItsAge()
  {
    GetJson("/api/radio/bandmap", MapWithStation(ageSeconds: 3 * 3600 + 120));

    var cut = RenderBand();

    cut.WaitForAssertion(() => cut.Find(".band-status-age").TextContent.Should().Be("scanned 3 h ago"));
    cut.FindAll(".band-empty").Should().BeEmpty();
    cut.FindAll(".visualizer-waiting").Should().BeEmpty();
  }

  [Fact]
  public void Sweeping_ShowsTheCountdown_AndDisablesScan()
  {
    GetJson("/api/radio/bandmap", EmptyMap(isSweeping: true, remaining: 12));

    var cut = RenderBand();

    cut.WaitForAssertion(() => cut.Find(".band-status-scanning").TextContent.Should().Be("Scanning… 12 s"));
    cut.Find(".band-scan-btn").HasAttribute("disabled").Should().BeTrue();
  }

  [Fact]
  public void BandAxis_ShowsMhz()
  {
    var cut = RenderBand();

    cut.FindAll(".visualizer-axis.is-band span").Select(s => s.TextContent)
      .Should().Equal("88", "92", "96", "100", "104", "108");
  }

  // ── Scan button ──────────────────────────────────────────────────────────

  [Fact]
  public void ScanButton_PostsTheScan()
  {
    _api.Route(HttpMethod.Post, "/api/radio/bandmap/scan", HttpStatusCode.Accepted, "{}");
    var cut = RenderBand();

    cut.Find(".band-scan-btn").Click();

    cut.WaitForAssertion(() => IndexOf(HttpMethod.Post, "/api/radio/bandmap/scan").Should().BeGreaterThanOrEqualTo(0));
  }

  [Fact]
  public void ScanButton_Unavailable_ShowsTheApiReason()
  {
    _api.Route(HttpMethod.Post, "/api/radio/bandmap/scan", HttpStatusCode.ServiceUnavailable,
      "{\"error\":\"No SDR device is available\"}");
    var cut = RenderBand();

    cut.Find(".band-scan-btn").Click();

    cut.WaitForAssertion(() =>
    {
      var message = cut.Find(".band-status-message");
      message.TextContent.Should().Be("No SDR device is available");
      message.ClassList.Should().Contain("is-error");
    });
  }

  // ── refresh cadence ──────────────────────────────────────────────────────

  [Fact]
  public void Idle_RefreshesOnTheThirtySecondCadence()
  {
    var cut = RenderBand();
    int start = cut.Instance.CompletedBandRefreshes;

    _clock.Advance(VisualizerPanel.BandIdleRefresh - TimeSpan.FromSeconds(1));
    cut.Instance.CompletedBandRefreshes.Should().Be(start, "the idle cadence has not elapsed");

    _clock.Advance(TimeSpan.FromSeconds(1));
    WaitForRefreshes(cut, start + 1);
  }

  [Fact]
  public void Sweeping_RefreshesEverySecond()
  {
    GetJson("/api/radio/bandmap", EmptyMap(isSweeping: true, remaining: 12));
    var cut = RenderBand();
    int start = cut.Instance.CompletedBandRefreshes;

    _clock.Advance(VisualizerPanel.BandSweepingRefresh);
    WaitForRefreshes(cut, start + 1);

    _clock.Advance(VisualizerPanel.BandSweepingRefresh);
    WaitForRefreshes(cut, start + 2);
  }

  [Fact]
  public void LeavingBand_StopsTheRefreshTimer()
  {
    var cut = RenderBand();
    cut.FindAll(".visualizer-mode").First(b => b.TextContent.Trim() == "Spectrum").Click();
    cut.WaitForAssertion(() => cut.FindAll(".band-overlay").Should().BeEmpty());
    int requestsAfterLeaving = _api.Requests.Count(r => r.Path == "/api/radio/bandmap");

    _clock.Advance(VisualizerPanel.BandIdleRefresh * 4);

    // The timer is disposed synchronously when BAND is left, so a fake-clock advance cannot fire it.
    _api.Requests.Count(r => r.Path == "/api/radio/bandmap").Should().Be(requestsAfterLeaving);
  }

  // ── tap to tune ──────────────────────────────────────────────────────────

  [Fact]
  public async Task Tap_RadioNotActive_SwitchesToRadioBeforeTuning()
  {
    GetJson("/api/radio/bandmap", MapWithStation(ageSeconds: 60));
    var cut = RenderBand();

    // A tap 0.3 MHz below the station snaps to it.
    double fraction = FmBandMath.HzToFraction(99_200_000);
    await cut.InvokeAsync(() => cut.Instance.OnBandTap(fraction));

    int switchAt = IndexOf(HttpMethod.Post, "/api/sources");
    int tuneAt = IndexOf(HttpMethod.Post, "/api/radio/frequency");
    switchAt.Should().BeGreaterThanOrEqualTo(0, "the radio was not the active source");
    tuneAt.Should().BeGreaterThan(switchAt, "the source switch has to land before the frequency is set");

    _api.Requests[switchAt].Body.Should().Contain("\"sourceType\":\"Radio\"");
    JsonDocument.Parse(_api.Requests[tuneAt].Body!).RootElement.GetProperty("frequency").GetDouble()
      .Should().Be(99_500_000);
    cut.WaitForAssertion(() => cut.Find(".band-status-message").TextContent.Should().Be("Tuning 99.5 FM"));
  }

  [Fact]
  public async Task Tap_RadioActive_TunesWithoutSwitching()
  {
    GetJson("/api/radio/bandmap", MapWithStation(ageSeconds: 60));
    GetJson("/api/radio/state", FmState(101_100_000));
    var cut = RenderBand();

    await cut.InvokeAsync(() => cut.Instance.OnBandTap(FmBandMath.HzToFraction(99_200_000)));

    IndexOf(HttpMethod.Post, "/api/sources").Should().Be(-1, "the radio was already the active source");
    IndexOf(HttpMethod.Post, "/api/radio/band").Should().Be(-1, "the radio was already on FM");
    int tuneAt = IndexOf(HttpMethod.Post, "/api/radio/frequency");
    tuneAt.Should().BeGreaterThanOrEqualTo(0);
    JsonDocument.Parse(_api.Requests[tuneAt].Body!).RootElement.GetProperty("frequency").GetDouble()
      .Should().Be(99_500_000);
  }

  [Fact]
  public async Task Tap_RadioOnAnotherBand_SelectsFmBeforeTuning()
  {
    GetJson("/api/radio/state", FmState(1_010_000, band: "AM"));
    var cut = RenderBand();

    await cut.InvokeAsync(() => cut.Instance.OnBandTap(FmBandMath.HzToFraction(95_060_000)));

    int bandAt = IndexOf(HttpMethod.Post, "/api/radio/band");
    int tuneAt = IndexOf(HttpMethod.Post, "/api/radio/frequency");
    bandAt.Should().BeGreaterThanOrEqualTo(0);
    tuneAt.Should().BeGreaterThan(bandAt);
    JsonDocument.Parse(_api.Requests[tuneAt].Body!).RootElement.GetProperty("frequency").GetDouble()
      .Should().Be(95_100_000, "with no map the tap snaps to the nearest channel");
  }

  [Fact]
  public async Task Tap_SwitchFails_DoesNotTune()
  {
    _api.Post("/api/sources", HttpStatusCode.InternalServerError);
    var cut = RenderBand();

    await cut.InvokeAsync(() => cut.Instance.OnBandTap(0.5));

    IndexOf(HttpMethod.Post, "/api/radio/frequency").Should().Be(-1);
    cut.WaitForAssertion(() => cut.Find(".band-status-message").TextContent.Should().Be("Could not switch to the radio"));
  }
}

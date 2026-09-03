using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Radzen;
using Radio.Web.Components.Shared;
using Radio.Web.Models;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// The band pills' one-state guard (ENC-5 Task 15).
///
/// <para>
/// D7 folds the radio bands into the SOURCE knob's list, so a band can now be committed from the
/// knob with nobody touching the screen. The pills already follow
/// <see cref="AudioStateHubService.RadioStateChanged"/> (<c>RadioControlPanel.razor:6, :981,
/// :1047, :1626</c>), so the requirement is met today and the correct action is to stop it
/// regressing rather than to re-plumb it. These two tests are that guard.
/// </para>
///
/// <para>
/// What the overlay and the pills share is the <b>active band</b> — the authoritative copy lives in
/// the API process and reaches both by broadcast. They deliberately do NOT share list composition:
/// the pills render all six device-agnostic bands, the overlay carries the handoff's four.
/// </para>
/// </summary>
public class RadioControlPanelBandSyncTests : TestContext
{
  public RadioControlPanelBandSyncTests()
  {
    // Hermetic: no outbound HTTP and no SignalR negotiate, so the result never depends on whether
    // radio-api happens to be running locally.
    Services.AddHermeticTestRig();
    JSInterop.Mode = JSRuntimeMode.Loose;

    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        { "ApiBaseUrl", HermeticTestRig.ApiBaseUrl },
      })
      .Build();

    Services.AddSingleton<IConfiguration>(configuration);
    Services.AddSingleton<ILoggerFactory>(new NullLoggerFactory());
    Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    Services.AddRadzenComponents();
    Services.AddOptions<RdsScrollOptions>();

    Services.AddSingleton(sp =>
      new AudioStateHubService(
        NullLogger<AudioStateHubService>.Instance,
        sp.GetRequiredService<IConfiguration>(),
        transport: new OfflineHubTransport()));
  }

  [Fact]
  public async Task BandPills_FollowARadioStateBroadcast_WithoutAClick()
  {
    // The regression guard for a band committed from the SOURCE knob: nothing on screen was
    // touched, and the pill still has to move when the broadcast says the band changed.
    var cut = RenderPanel(State("FM", 101_500_000));

    ActiveBandLabel(cut).Should().Be("FM");

    await RaiseRadioStateAsync(cut, State("AM", 1_010_000));

    ActiveBandLabel(cut).Should().Be("AM");
  }

  [Fact]
  public void BandPills_DoNotHoldTheirOwnBandField()
  {
    // Why this is worth a test rather than a code comment: the pills' active band is derived from
    // _radioState, which the hub broadcast replaces wholesale. A private string field caching the
    // band would go stale the first time the band moved from somewhere other than a pill click —
    // which, after ENC-5, it does. The one permitted field is gesture state: which pill the
    // pointer went down on, for the long-press-to-save-preset gesture.
    var permitted = new[] { "_bandPointerDownBand" };

    var offenders = typeof(RadioControlPanel)
      .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
      .Where(f => f.FieldType == typeof(string))
      .Where(f => f.Name.Contains("band", StringComparison.OrdinalIgnoreCase))
      .Select(f => f.Name)
      .Except(permitted)
      .ToList();

    offenders.Should().BeEmpty(
      "the active band is read from _radioState, which AudioStateHubService.RadioStateChanged "
      + "replaces; a cached copy would survive a band change made anywhere but a pill click");
  }

  // ── rig ───────────────────────────────────────────────────────────────────

  private static RadioStateDto State(string band, double frequencyHz) => new(
    Frequency: frequencyHz,
    Band: band,
    Step: 100_000,
    SignalStrength: 50,
    IsScanning: false,
    ScanDirection: null,
    ScanStopThreshold: -36.0,
    Gain: 0,
    AutoGain: false,
    Equalizer: "Normal",
    DeviceVolume: 50);

  private static Radio.Core.Models.RadioBandModel Band(
    string type, string name, long minHz, long maxHz, string range) => new()
    {
      Type = type,
      Name = name,
      MinFrequencyHz = minHz,
      MaxFrequencyHz = maxHz,
      DefaultStepHz = 100_000,
      AllowedStepSizes = new long[] { 100_000 },
      DefaultModulation = type == "FM" ? "WFM" : "AM",
      DefaultBandwidthHz = 200_000,
      Description = name,
      Range = range,
      BandPresetCapacity = 16,
    };

  private IRenderedComponent<RadioControlPanel> RenderPanel(RadioStateDto state)
  {
    var handler = new BandStubHandler(state,
    [
      Band("FM", "FM Broadcast", 87_500_000, 108_000_000, "87.5–108 MHz"),
      Band("AM", "AM Broadcast", 530_000, 1_700_000, "530–1700 kHz"),
    ]);

    Services.AddSingleton(_ =>
    {
      var client = new HttpClient(handler) { BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl) };
      return new RadioApiService(client, NullLogger<RadioApiService>.Instance);
    });

    var cut = RenderComponent<RadioControlPanel>();
    cut.WaitForAssertion(
      () => Assert.NotEmpty(cut.FindAll(".rcp-band-btn")),
      timeout: TimeSpan.FromSeconds(2));
    return cut;
  }

  private static string ActiveBandLabel(IRenderedComponent<RadioControlPanel> cut)
  {
    var active = cut.FindAll(".rcp-band-active");
    active.Count.Should().Be(1, "exactly one band is active at a time");
    return active[0].QuerySelector(".rcp-band-label")!.TextContent.Trim();
  }

  /// <summary>
  /// Drives the typed hub event the way SignalR would. <c>RadioStateChanged</c> is a field-like
  /// event, so the test reaches its compiler-generated backing field — the same pattern
  /// NowPlayingPanelTests uses for this event.
  /// </summary>
  private async Task RaiseRadioStateAsync(
    IRenderedComponent<RadioControlPanel> cut, RadioStateDto dto)
  {
    var hub = Services.GetRequiredService<AudioStateHubService>();
    var field = typeof(AudioStateHubService).GetField(
      nameof(AudioStateHubService.RadioStateChanged),
      BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.NotNull(field);

    var handler = (Func<RadioStateDto, Task>?)field!.GetValue(hub);
    Assert.NotNull(handler);

    await cut.InvokeAsync(() => handler!.Invoke(dto));
  }

  /// <summary>
  /// Serves the two GETs the panel makes on initialise. Presets are empty: this fixture is about
  /// the band pills, and an empty bank renders the placeholder rather than an error banner.
  /// </summary>
  private sealed class BandStubHandler : HttpMessageHandler
  {
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private readonly RadioStateDto _state;
    private readonly IEnumerable<Radio.Core.Models.RadioBandModel> _bands;

    public BandStubHandler(RadioStateDto state, IEnumerable<Radio.Core.Models.RadioBandModel> bands)
    {
      _state = state;
      _bands = bands;
    }

    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
    {
      var path = request.RequestUri?.AbsolutePath ?? string.Empty;
      HttpResponseMessage response = (request.Method.Method, path) switch
      {
        ("GET", "/api/radio/state") => Ok(_state),
        ("GET", "/api/radio/presets") => Ok(Array.Empty<RadioPresetDto>()),
        ("GET", "/api/RadioBands") => Ok(_bands),
        ("POST", _) => new HttpResponseMessage(HttpStatusCode.OK),
        _ => new HttpResponseMessage(HttpStatusCode.NotFound),
      };
      return Task.FromResult(response);
    }

    private static HttpResponseMessage Ok<T>(T payload) =>
      new(HttpStatusCode.OK)
      {
        Content = new StringContent(
          JsonSerializer.Serialize(payload, Options), Encoding.UTF8, "application/json"),
      };
  }
}

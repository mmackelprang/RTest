using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Radzen;
using Radio.Web.Components.Shared;
using Radio.Web.Models;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// bUnit tests for <see cref="RadioControlPanel"/> after PR 1 of the Radio
/// Controller Polish arc — signal-meter clamp + CLIP pill + dBu readout +
/// 4-tick scale strip, and the AGC two-cell grid that always renders a value
/// in the right cell regardless of AGC state.
/// </summary>
public class RadioControlPanelTests : TestContext
{
  private readonly ILoggerFactory _loggerFactory;

  public RadioControlPanelTests()
  {
    _loggerFactory = new NullLoggerFactory();
    JSInterop.Mode = JSRuntimeMode.Loose;

    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        { "ApiBaseUrl", "http://localhost:5000" },
      })
      .Build();

    Services.AddSingleton<IConfiguration>(configuration);
    Services.AddSingleton(_loggerFactory);
    Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

    Services.AddRadzenComponents();

    // SignalR hub service — never connects in tests, just provides the event
    // surface so subscriptions don't NRE.
    Services.AddSingleton(sp =>
      new AudioStateHubService(
        NullLogger<AudioStateHubService>.Instance,
        sp.GetRequiredService<IConfiguration>()));
  }

  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      _loggerFactory?.Dispose();
    }
    base.Dispose(disposing);
  }

  /// <summary>
  /// Registers a stub <see cref="RadioApiService"/> backed by a handler that
  /// responds to <c>GET /api/radio/state</c> with the supplied DTO. Presets
  /// and bands return empty lists so the panel renders without async error
  /// banners.
  /// </summary>
  private void UseRadioState(RadioStateDto state)
  {
    var handler = new RadioStateStubHandler(state);
    Services.AddSingleton(_ =>
    {
      var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };
      return new RadioApiService(client, NullLogger<RadioApiService>.Instance);
    });
  }

  private static RadioStateDto BuildState(
    int? signalStrength = 50,
    bool clip = false,
    double rssiDbu = -30.0,
    double appliedGain = 0.0,
    bool autoGain = false,
    int? gain = 0,
    double scanStopThreshold = -36.0)
  {
    return new RadioStateDto(
      Frequency: 101_500_000,
      Band: "FM",
      Step: 100_000,
      SignalStrength: signalStrength,
      IsScanning: false,
      ScanDirection: null,
      ScanStopThreshold: scanStopThreshold,
      Gain: gain,
      AutoGain: autoGain,
      Equalizer: "Normal",
      DeviceVolume: 50,
      IsStereo: false,
      RdsStationName: null,
      RdsProgramType: null,
      Clip: clip,
      RssiDbu: rssiDbu,
      AppliedGain: appliedGain);
  }

  private IRenderedComponent<RadioControlPanel> RenderPanel(RadioStateDto state)
  {
    UseRadioState(state);
    var cut = RenderComponent<RadioControlPanel>();

    // The panel kicks off async state loads in OnInitializedAsync; bUnit's
    // WaitForAssertion gives those tasks room to complete and re-render the
    // meter from the skeleton placeholder.
    cut.WaitForAssertion(() =>
    {
      Assert.DoesNotContain("skeleton", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }, timeout: TimeSpan.FromSeconds(2));

    return cut;
  }

  [Fact]
  public void SignalMeter_RendersClampedValue()
  {
    // Even with a hypothetical overdriven raw of 118%, the server-side clamp
    // means the DTO carries SignalStrength = 100. The meter must paint
    // exactly 20 lit segments and never surface a literal "118" or a "%"
    // glyph anywhere in the meter region (the inline <style> uses "100%"
    // for CSS heights — assertions scope to the meter element only).
    var state = BuildState(signalStrength: 100, clip: true, rssiDbu: 0.0);
    var cut = RenderPanel(state);

    Assert.DoesNotContain("118", cut.Markup);

    var meter = cut.Find(".rcp-meter");
    Assert.DoesNotContain("%", meter.TextContent);
    Assert.DoesNotContain("118", meter.TextContent);

    // 20 segments total; all should carry one of the lit segment classes.
    var segs = cut.FindAll(".rcp-meter-seg");
    Assert.Equal(20, segs.Count);
    var litCount = segs.Count(IsLitSegment);
    Assert.Equal(20, litCount);
  }

  [Fact]
  public void SignalMeter_ShowsClipPill_WhenClipTrue()
  {
    var clipState = BuildState(signalStrength: 100, clip: true, rssiDbu: 0.0);
    var cut = RenderPanel(clipState);

    var pills = cut.FindAll(".rcp-clip-pill");
    Assert.Single(pills);
    Assert.Equal("CLIP", pills[0].TextContent.Trim());
  }

  [Fact]
  public void SignalMeter_HidesClipPill_WhenClipFalse()
  {
    var state = BuildState(signalStrength: 70, clip: false, rssiDbu: -18.0);
    var cut = RenderPanel(state);

    Assert.Empty(cut.FindAll(".rcp-clip-pill"));
  }

  [Fact]
  public void SignalMeter_RendersDbuReadout()
  {
    var state = BuildState(signalStrength: 70, clip: false, rssiDbu: -18.0);
    var cut = RenderPanel(state);

    var readout = cut.Find(".rcp-meter-dbu");
    // RssiDbu rendered with "0" format then normalized to U+2212 math-minus
    // so the readout typographically matches the scale labels below.
    Assert.Equal("−18 dBu", readout.TextContent.Trim());
  }

  [Fact]
  public void SignalMeter_RendersScaleLabels()
  {
    var state = BuildState();
    var cut = RenderPanel(state);

    var scale = cut.Find(".rcp-meter-scale");
    var labels = scale.Children.Select(c => c.TextContent.Trim()).ToArray();
    Assert.Equal(new[] { "−60", "−30", "−12", "0 dBu" }, labels);
  }

  [Fact]
  public void AgcStrip_TwoCellGrid_BothPopulated_WhenAgcOn()
  {
    var state = BuildState(autoGain: true, appliedGain: 28.0, gain: 0);
    var cut = RenderPanel(state);

    var grid = cut.Find(".rcp-sdr-grid");
    var directChildren = grid.Children.ToArray();
    Assert.Equal(2, directChildren.Length);

    // Left cell — AGC button with AUTO chip
    var agcChip = cut.Find(".rcp-agc-chip-auto");
    Assert.Equal("AUTO", agcChip.TextContent.Trim());
    Assert.Empty(cut.FindAll(".rcp-agc-chip-off"));

    // Right cell — "Tuner is choosing" hint + numeric gain
    var hint = cut.Find(".rcp-gain-auto-hint");
    Assert.Equal("Tuner is choosing", hint.TextContent.Trim());
    var value = cut.Find(".rcp-gain-auto-value");
    Assert.Contains("28.0", value.TextContent);
    Assert.Contains("dB", value.TextContent);
  }

  [Fact]
  public void AgcStrip_AgcOff_ShowsSlider_AndPill_AndRangeHint()
  {
    var state = BuildState(autoGain: false, appliedGain: 28.0, gain: 28);
    var cut = RenderPanel(state);

    // Pill stays a fixed-width amber chip with "28 dB"
    var pill = cut.Find(".rcp-gain-pill");
    Assert.Contains("28", pill.TextContent);
    Assert.Contains("dB", pill.TextContent);

    // Range hint visible
    var hint = cut.Find(".rcp-gain-range-hint");
    Assert.Contains("0", hint.TextContent);
    Assert.Contains("50", hint.TextContent);

    // Slider present (Radzen renders an input + slider DOM)
    Assert.NotEmpty(cut.FindAll(".rz-slider"));

    // AGC chip in OFF state
    var offChip = cut.Find(".rcp-agc-chip-off");
    Assert.Equal("OFF", offChip.TextContent.Trim());
    Assert.Empty(cut.FindAll(".rcp-agc-chip-auto"));
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public void AgcStrip_NoEmptyCell_RegardlessOfAgcState(bool autoGain)
  {
    // Both AGC on and AGC off must produce exactly two grid children — no
    // collapsed/empty cell artefacts that would let the strip change height.
    var state = BuildState(autoGain: autoGain, appliedGain: 12.5, gain: 12);
    var cut = RenderPanel(state);

    var grid = cut.Find(".rcp-sdr-grid");
    Assert.Equal(2, grid.Children.Length);

    // Both cells always present, regardless of AGC.
    Assert.NotNull(cut.Find(".rcp-agc-cell"));
    Assert.NotNull(cut.Find(".rcp-gain-cell"));
  }

  [Fact]
  public void ScanStopThreshold_IsDouble()
  {
    // Greenfield rename — confirm via reflection that the DTO's threshold is
    // a double, not an int (PR 1 re-types from percent → dBu).
    var prop = typeof(RadioStateDto).GetProperty(
      nameof(RadioStateDto.ScanStopThreshold),
      BindingFlags.Public | BindingFlags.Instance);
    Assert.NotNull(prop);
    Assert.Equal(typeof(double), prop!.PropertyType);
  }

  private static bool IsLitSegment(IElement segment)
  {
    var cls = segment.ClassName ?? string.Empty;
    return cls.Contains("seg-green") || cls.Contains("seg-amber") || cls.Contains("seg-red");
  }

  /// <summary>
  /// Tiny HTTP stub that returns a fixed <see cref="RadioStateDto"/> for
  /// <c>/api/radio/state</c> and empty arrays for the auxiliary endpoints
  /// the panel hits during <c>OnInitializedAsync</c> (<c>/api/radio/presets</c>,
  /// <c>/api/RadioBands</c>). Anything else gets 404 — the panel logs and
  /// continues so the page still renders.
  /// </summary>
  private sealed class RadioStateStubHandler : HttpMessageHandler
  {
    private readonly RadioStateDto _state;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public RadioStateStubHandler(RadioStateDto state)
    {
      _state = state;
    }

    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
    {
      var path = request.RequestUri?.AbsolutePath ?? string.Empty;
      var response = path switch
      {
        "/api/radio/state" => Ok(_state),
        "/api/radio/presets" => Ok(Array.Empty<RadioPresetDto>()),
        "/api/RadioBands" => Ok(Array.Empty<Radio.Core.Models.RadioBandModel>()),
        _ => new HttpResponseMessage(HttpStatusCode.NotFound),
      };
      return Task.FromResult(response);
    }

    private static HttpResponseMessage Ok<T>(T payload)
    {
      var json = JsonSerializer.Serialize(payload, Options);
      return new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
      };
    }
  }
}

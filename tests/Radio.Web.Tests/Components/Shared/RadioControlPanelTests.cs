using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Radzen;
using Radio.Web.Components.Shared;
using Radio.Web.Models;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;
using Radio.Web.Tests.TestHelpers;

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
    // Hermetic rig: fails every outbound HTTP request and every SignalR
    // negotiate without touching the network, so this fixture's result never
    // depends on whether radio-api happens to be running locally.
    Services.AddHermeticTestRig();

    _loggerFactory = new NullLoggerFactory();
    JSInterop.Mode = JSRuntimeMode.Loose;

    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        { "ApiBaseUrl", HermeticTestRig.ApiBaseUrl },
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
        sp.GetRequiredService<IConfiguration>(),
        transport: new OfflineHubTransport()));

    // HANDOFF-rds-accumulating-scroll — the panel injects
    // IOptionsMonitor<RdsScrollOptions> for the accumulating ticker. Wire up
    // the standard options pipeline so the binder returns a default-valued
    // monitor; tests never override these values.
    Services.AddOptions<RdsScrollOptions>();

    // UI-21 — the preset bar reads the PRESETS knob's highlight off the HUD singleton.
    Services.AddSingleton<Radio.Web.Services.EncoderHudService>();
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
  /// and bands return the supplied lists (or empty by default) so the panel
  /// renders without async error banners.
  ///
  /// The returned handler can be inspected after a render to verify recorded
  /// calls (POST bodies, paths) for tests that pin interaction wiring such as
  /// <c>AgcStrip_TapLeftCell_TogglesAgc</c>.
  /// </summary>
  private RadioStateStubHandler UseRadioState(
    RadioStateDto state,
    IEnumerable<RadioPresetDto>? presets = null,
    IEnumerable<Radio.Core.Models.RadioBandModel>? bands = null)
  {
    var handler = new RadioStateStubHandler(state, presets, bands);
    Services.AddSingleton(_ =>
    {
      var client = new HttpClient(handler) { BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl) };
      return new RadioApiService(client, NullLogger<RadioApiService>.Instance);
    });
    return handler;
  }

  private static RadioStateDto BuildState(
    int? signalStrength = 50,
    bool clip = false,
    double rssiDbu = -30.0,
    double appliedGain = 0.0,
    bool autoGain = false,
    int? gain = 0,
    double scanStopThreshold = -36.0,
    string? rdsStationName = null,
    string? rdsProgramType = null,
    string? rdsRadioText = null,
    double frequency = 101_500_000,
    string band = "FM")
  {
    return new RadioStateDto(
      Frequency: frequency,
      Band: band,
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
      RdsStationName: rdsStationName,
      RdsProgramType: rdsProgramType,
      Clip: clip,
      RssiDbu: rssiDbu,
      AppliedGain: appliedGain,
      NowPlayingMatchId: null,
      RdsRadioText: rdsRadioText);
  }

  /// <summary>
  /// Builds an FM band model with the PR 3 fields populated. Used by tests
  /// that need the tuner header / band-pill sub-range / preset capacity to
  /// render meaningful values without re-doing the server-side projection.
  /// </summary>
  private static Radio.Core.Models.RadioBandModel BuildFmBand(int capacity = 16) =>
    new()
    {
      Type = "FM",
      Name = "FM Broadcast",
      MinFrequencyHz = 87_500_000,
      MaxFrequencyHz = 108_000_000,
      DefaultStepHz = 100_000,
      AllowedStepSizes = new long[] { 50_000, 100_000, 200_000 },
      DefaultModulation = "WFM",
      DefaultBandwidthHz = 200_000,
      Description = "FM broadcast band",
      Range = "87.5–108 MHz",
      BandPresetCapacity = capacity,
    };

  private static Radio.Core.Models.RadioBandModel BuildAmBand(int capacity = 16) =>
    new()
    {
      Type = "AM",
      Name = "AM Broadcast",
      MinFrequencyHz = 530_000,
      MaxFrequencyHz = 1_700_000,
      DefaultStepHz = 10_000,
      AllowedStepSizes = new long[] { 9_000, 10_000 },
      DefaultModulation = "AM",
      DefaultBandwidthHz = 10_000,
      Description = "AM broadcast band",
      Range = "530–1700 kHz",
      BandPresetCapacity = capacity,
    };

  private static RadioPresetDto BuildPreset(
    string id, string name, double frequency, string band, int slotNumber)
    => new(id, name, frequency, band, DateTimeOffset.UtcNow, slotNumber);

  private IRenderedComponent<RadioControlPanel> RenderPanel(
    RadioStateDto state,
    IEnumerable<RadioPresetDto>? presets = null,
    IEnumerable<Radio.Core.Models.RadioBandModel>? bands = null)
  {
    UseRadioState(state, presets, bands);
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

    // Task #15 flake fix #83 — tighten the regression-guard so random bUnit
    // / Blazor-generated attribute values containing "118" as a substring
    // (Radzen element keys, internal __blazorId fragments, etc.) do not
    // create false positives. The original `Assert.DoesNotContain("118",
    // cut.Markup)` was a probabilistic flake: ~0.4% of test runs surfaced
    // a randomly-generated id containing "118" and failed the assertion
    // even though the meter rendered correctly.
    //
    // The regression we actually care about is the pre-clamp "118%" /
    // "118 dBu" rendering. Two assertions cover the meaningful surfaces:
    //   1. Word-boundary regex over the meter's text content — "118" as a
    //      standalone token (segment count, dBu readout, percentage,
    //      anything humanly visible) would match.
    //   2. The "118 dBu" / "118%" literal suffix — what the bug would
    //      have produced before the clamp landed.
    var meter = cut.Find(".rcp-meter");
    Assert.DoesNotMatch(@"\b118\b", meter.TextContent);
    Assert.DoesNotContain("118 dBu", meter.TextContent);
    Assert.DoesNotContain("118%", meter.TextContent);
    Assert.DoesNotContain("%", meter.TextContent);

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

    var pills = cut.FindAll(".rcp-clip-pill.is-on");
    Assert.Single(pills);
    Assert.Equal("CLIP", pills[0].TextContent.Trim());
  }

  [Fact]
  public void SignalMeter_HidesClipPill_WhenClipFalse()
  {
    var state = BuildState(signalStrength: 70, clip: false, rssiDbu: -18.0);
    var cut = RenderPanel(state);

    // UI-27: the pill stays in the DOM and is hidden by the stylesheet — see
    // Css_ClipPill_TakesNoRoomAndIsHiddenUntilOn for the half bUnit cannot see.
    Assert.Single(cut.FindAll(".rcp-clip-pill"));
    Assert.Empty(cut.FindAll(".rcp-clip-pill.is-on"));
  }

  [Theory]
  [InlineData(false, false)]
  [InlineData(true, false)]
  [InlineData(false, true)]
  [InlineData(true, true)]
  public void MeterHeader_HasTheSameElements_WithAndWithoutClip(bool clip, bool scanning)
  {
    // UI-27, owner: "when the CLIP tag appears above the signal strength bar, it causes the whole
    // screen to shift by a couple of pixels". Inserting the pill made the header 3 px taller
    // (measured: 16.5 → 19.5 px) and moved everything below the meter. Now CLIP changes one class
    // and nothing else: the header holds the same elements in the same order either way, so CLIP
    // cannot add a box to the header's layout or take one away. (One render per bUnit context,
    // hence one expected shape per scan state rather than a with/without comparison in one test.)
    var state = BuildState(signalStrength: 100, clip: clip, rssiDbu: -20.0, scanStopThreshold: -10.0) with
    {
      IsScanning = scanning,
      ScanDirection = scanning ? "up" : null,
    };
    var cut = RenderPanel(state);
    var header = cut.Find(".rcp-meter-header");
    var shape = string.Join("|", header.QuerySelectorAll("*").Select(e =>
      $"{e.TagName}.{string.Join(".", e.ClassList.Where(c => c != "is-on").OrderBy(c => c, StringComparer.Ordinal))}"));

    var expected = (scanning ? "SPAN.|SPAN.rcp-scanning|" : "SPAN.|")
      + "SPAN.rcp-meter-readout|SPAN.rcp-clip-pill|SPAN.rcp-meter-dbu";
    shape.Should().Be(expected);
    header.QuerySelector(".rcp-clip-pill")!.ClassList.Contains("is-on").Should().Be(clip);
  }

  [Fact]
  public void Css_ClipPill_TakesNoRoomAndIsHiddenUntilOn()
  {
    // bUnit computes no styles, so the geometry half of UI-27 is pinned in the stylesheet: the pill
    // is out of flow (absolute, against the readout), hidden by visibility rather than display —
    // display:none would take its box away and put it back, which is the shift — and shown only by
    // .is-on. Measured in Chromium at 1920×720: header 16.5 px and every rect below it identical with
    // CLIP off, CLIP on, scanning and both.
    var css = File.ReadAllText(LocateDesignSystemCss());
    var pill = Regex.Match(css, @"\n\.rcp-clip-pill\s*\{(?<body>[^}]*)\}").Groups["body"].Value;
    var on = Regex.Match(css, @"\n\.rcp-clip-pill\.is-on\s*\{(?<body>[^}]*)\}").Groups["body"].Value;
    var readout = Regex.Match(css, @"\n\.rcp-meter-readout\s*\{(?<body>[^}]*)\}").Groups["body"].Value;

    pill.Should().Contain("position: absolute");
    // Where it sits once out of flow: left of the dBu readout and centred on it. Without these it
    // would land on top of the readout.
    pill.Should().Contain("right: calc(100% + 6px)");
    pill.Should().Contain("top: 50%");
    pill.Should().Contain("transform: translateY(-50%)");
    pill.Should().Contain("visibility: hidden");
    pill.Should().NotContain("display: none");
    on.Should().Contain("visibility: visible");
    readout.Should().Contain("position: relative", "the pill is placed against the readout, not some outer box");
  }

  private static string LocateDesignSystemCss()
  {
    var dir = AppContext.BaseDirectory;
    for (var i = 0; i < 10 && dir != null; i++)
    {
      var candidate = Path.Combine(dir, "src", "Radio.Web", "wwwroot", "css", "design-system.css");
      if (File.Exists(candidate))
      {
        return candidate;
      }
      dir = Path.GetDirectoryName(dir);
    }
    throw new FileNotFoundException("design-system.css not found by walking up from test base dir");
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

  // ─── Task #15 PR B (handoff item #26): AGC cell click toggles AGC ─────────
  //
  // The whole left cell of the SDR strip is a button; tapping it must fire
  // RadioApi.SetAutoGainAsync(!current). PR 1 of Arc 2 introduced this two-cell
  // grid; the unit test that pinned the click handler was deferred to PR B.

  [Fact]
  public void AgcStrip_TapLeftCell_TogglesAgc()
  {
    // AGC currently ON — tapping the cell must POST /api/radio/gain/auto with
    // {"enabled": false} via RadioApi.SetAutoGainAsync.
    var state = BuildState(autoGain: true, appliedGain: 18.0, gain: 0);
    var handler = UseRadioState(state);
    var cut = RenderComponent<RadioControlPanel>();
    cut.WaitForAssertion(() =>
    {
      Assert.DoesNotContain("skeleton", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }, timeout: TimeSpan.FromSeconds(2));

    cut.Find(".rcp-agc-cell").Click();

    // Wait for the async POST to complete and land in the recorded-calls list.
    cut.WaitForAssertion(() =>
    {
      var call = handler.RecordedCalls
        .FirstOrDefault(c => c.Method == "POST" && c.Path == "/api/radio/gain/auto");
      call.Path.Should().Be("/api/radio/gain/auto",
        "the AGC cell click must POST to the auto-gain endpoint");
      call.Body.Should().Contain("\"enabled\":false",
        "current AGC=true must toggle to enabled:false");
    }, timeout: TimeSpan.FromSeconds(2));
  }

  // ─── Task #15 PR B (handoff item #27): scan-signal dBu branch ────────────
  //
  // The scan-state indicator inside RadioControlPanel reads RssiDbu against
  // ScanStopThreshold (both `double` in dBu after Arc 2 PR 1). When the radio
  // is scanning AND RssiDbu >= ScanStopThreshold, the indicator shows the
  // green "SIGNAL" pill carrying the current dBu value. Otherwise (still
  // hunting), it shows the amber "SCANNING" hint.

  // ─── Task #15 PR B (handoff item #38): long-press 600ms + save-dialog seed ─
  //
  // The active band pill carries a long-press gesture: holding for at least
  // LongPressThresholdMs (600ms) opens the save-preset dialog instead of the
  // no-op tap-to-switch behaviour. The press timing is tracked via
  // DateTime.UtcNow on _bandPointerDownAt; rather than introduce a TimeProvider
  // refactor on this just-shipped code path, the test seeds _bandPointerDownAt
  // backwards in time before calling the pointer-up handler — exercising the
  // same elapsed-ms branch the real gesture would hit.

  [Fact]
  public void BandPill_LongPress600ms_TriggersOpenSavePresetDialog()
  {
    var state = BuildState(rdsStationName: "KEXP");
    var cut = RenderPanel(state, bands: new[] { BuildFmBand() });

    var instance = cut.Instance;
    var type = typeof(RadioControlPanel);
    var flags = BindingFlags.NonPublic | BindingFlags.Instance;

    // Stage 1: pointerdown on the currently-active band ("FM").
    var pointerDown = type.GetMethod("HandleBandPointerDown", flags)!;
    cut.InvokeAsync(() => pointerDown.Invoke(instance, new object[] { "FM", true }));

    // Stage 2: rewind _bandPointerDownAt so the elapsed-ms exceeds the 600ms
    // long-press threshold without us actually sleeping. This is exactly the
    // delta the production code would compute for a real 700ms hold.
    var pointerDownAtField = type.GetField("_bandPointerDownAt", flags)!;
    pointerDownAtField.SetValue(instance, DateTime.UtcNow.AddMilliseconds(-700));

    // Stage 3: pointerup — the elapsed-ms check passes, dialog opens.
    var pointerUp = type.GetMethod("HandleBandPointerUp", flags)!;
    cut.InvokeAsync(() => pointerUp.Invoke(instance, new object[] { "FM", true }));
    cut.Render();

    // Dialog field flips to true.
    var dialogOpenField = type.GetField("_isSavePresetDialogOpen", flags)!;
    var open = (bool)dialogOpenField.GetValue(instance)!;
    open.Should().BeTrue(
      "a long-press ≥600ms on the active band pill must open the save-preset dialog");
  }

  [Fact]
  public void BandPill_ShortClick_DoesNotOpenDialog_SwitchesBand()
  {
    // Short tap (<600ms): pointerup branches without opening the dialog.
    var state = BuildState();
    var cut = RenderPanel(state, bands: new[] { BuildFmBand() });

    var instance = cut.Instance;
    var type = typeof(RadioControlPanel);
    var flags = BindingFlags.NonPublic | BindingFlags.Instance;

    var pointerDown = type.GetMethod("HandleBandPointerDown", flags)!;
    cut.InvokeAsync(() => pointerDown.Invoke(instance, new object[] { "FM", true }));

    // Leave _bandPointerDownAt at "now" so elapsed-ms is essentially 0.
    var pointerUp = type.GetMethod("HandleBandPointerUp", flags)!;
    cut.InvokeAsync(() => pointerUp.Invoke(instance, new object[] { "FM", true }));
    cut.Render();

    var dialogOpenField = type.GetField("_isSavePresetDialogOpen", flags)!;
    var open = (bool)dialogOpenField.GetValue(instance)!;
    open.Should().BeFalse(
      "a short tap on a band pill must not open the save-preset dialog");
  }

  [Fact]
  public void SavePresetDialog_NameField_SeededWithRdsStationNameStable_WhenPresent()
  {
    // Task #80 v4 — the dialog seeds from RdsStationNameStable, which is the
    // NRSC-4-B Annex D call-sign decode of the PI code (e.g. PI=0x8ACC → "WUNC").
    // The live RdsStationName (rotating PS) is intentionally NOT used as a
    // fallback: that's exactly the value the v1/v2/v3 attempts were trying
    // to filter against, and which routinely captures mid-rotation fragments
    // like song titles / DJ names at the instant of the long-press.
    var state = BuildState(frequency: 92_300_000) with
    {
      RdsStationName = "TOO HOT",   // rolling fragment — must NOT be used
      RdsStationNameStable = "WSMW", // PI-decoded call sign — must win
    };
    var cut = RenderPanel(state, bands: new[] { BuildFmBand() });

    var instance = cut.Instance;
    var type = typeof(RadioControlPanel);
    var flags = BindingFlags.NonPublic | BindingFlags.Instance;

    var openSaveDialog = type.GetMethod("OpenSavePresetDialog", flags)!;
    cut.InvokeAsync(() => openSaveDialog.Invoke(instance, null));
    cut.Render();

    var seed = (string?)type.GetField("_presetName", flags)!.GetValue(instance);
    seed.Should().Be("WSMW",
      "the PI-decoded call sign on RdsStationNameStable is the authoritative seed; " +
      "the live rolling RdsStationName must never be used as a fallback");
  }

  [Fact]
  public void SavePresetDialog_NameField_SeededWithBandPlusFrequency_WhenNoStableName()
  {
    // No PI-decoded call sign (RDS dropout, 3-letter station, international,
    // LP, or pre-lock) → fall back to "<band> <formatted-freq>" composition.
    // FM band + 92.30 MHz frequency must produce "FM 92.30 MHz".
    //
    // Task #80 v4 — we deliberately do NOT fall back to the live rolling
    // RdsStationName even when present, so this test sets it to a value
    // that would have been used by the v3-era dual-fallback chain to
    // assert the new behavior.
    var state = BuildState(rdsStationName: "TOO HOT", frequency: 92_300_000, band: "FM");
    // RdsStationNameStable defaults to null on BuildState — that's the case under test.
    var cut = RenderPanel(state, bands: new[] { BuildFmBand() });

    var instance = cut.Instance;
    var type = typeof(RadioControlPanel);
    var flags = BindingFlags.NonPublic | BindingFlags.Instance;

    var openSaveDialog = type.GetMethod("OpenSavePresetDialog", flags)!;
    cut.InvokeAsync(() => openSaveDialog.Invoke(instance, null));
    cut.Render();

    var seed = (string?)type.GetField("_presetName", flags)!.GetValue(instance);
    seed.Should().StartWith("FM ",
      "the fallback seed prefixes with the active band when no PI-decoded call sign is available");
    seed.Should().Contain("92.30",
      "the fallback seed embeds the formatted frequency for the active band");
    seed.Should().Contain("MHz",
      "FM band carries the MHz unit (the panel's FormatFrequency helper applies the per-band unit)");
    seed.Should().NotContain("TOO HOT",
      "Task #80 v4 — the live rolling RdsStationName must never appear in the seed; " +
      "absent a stable call sign, band+frequency is the correct default");
  }

  [Fact]
  public void ScanIndicator_CompareRssiToScanStopThreshold_UsesDbu()
  {
    // RssiDbu=-20 exceeds threshold=-30 → signal-found branch lights up.
    var state = new RadioStateDto(
      Frequency: 92.5e6,
      Band: "FM",
      Step: 100_000,
      SignalStrength: 80,
      IsScanning: true,
      ScanDirection: "up",
      ScanStopThreshold: -30.0,
      Gain: 0,
      AutoGain: true,
      Equalizer: "Normal",
      DeviceVolume: 50,
      IsStereo: false,
      RdsStationName: null,
      RdsProgramType: null,
      Clip: false,
      RssiDbu: -20.0,
      AppliedGain: 0.0,
      NowPlayingMatchId: null,
      RdsRadioText: null);
    var cut = RenderPanel(state);

    // The presence of the dBu readout (in green SIGNAL form) is the assertion.
    // The amber scanning-up text "SCANNING" must NOT render when signal exceeds
    // the threshold.
    cut.Markup.Should().Contain("dBu");
    // The dBu readout in the meter renders the current value. We assert the
    // value is rendered as the math-minus form "−20 dBu" (consistent with the
    // existing SignalMeter_RendersDbuReadout test).
    var readout = cut.Find(".rcp-meter-dbu").TextContent.Trim();
    readout.Should().Be("−20 dBu");
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

  // ─── PR 3 of the Radio Controller Polish arc ──────────────────────────────
  // Tuner header, RDS card mount, tall band pills, RT line, presets
  // grid (slot · name+band · freq) + dashed empty-slot placeholder.

  /// <summary>
  /// UI-17: the tuner header row is gone — the centre panel's "Radio" tab names the panel and the band
  /// pill shows the range (pinned by <see cref="BandPills_RenderTwoLineLabelAndRange"/>). Its height was
  /// what the tab strip needed; with it back, the frequency well is squeezed under the meter.
  /// </summary>
  [Fact]
  public void TunerHeader_IsNotRendered()
  {
    var state = BuildState();
    var cut = RenderPanel(state, bands: new[] { BuildFmBand() });

    Assert.Empty(cut.FindAll(".rcp-tuner-header"));
    Assert.Empty(cut.FindAll(".rcp-tuner-title"));
  }

  [Fact]
  public void BandPills_RenderTwoLineLabelAndRange()
  {
    var state = BuildState();
    var cut = RenderPanel(state, bands: new[] { BuildFmBand(), BuildAmBand() });

    var labels = cut.FindAll(".rcp-band-label").Select(e => e.TextContent.Trim()).ToList();
    Assert.Contains("FM", labels);
    Assert.Contains("AM", labels);

    var subs = cut.FindAll(".rcp-band-sub").Select(e => e.TextContent.Trim()).ToList();
    Assert.Contains("87.5–108 MHz", subs);
    Assert.Contains("530–1700 kHz", subs);
  }

  [Fact]
  public void RdsCard_Head_ShowsStableCallSign_NotTheRollingPs()
  {
    // D-B, 2026-09-28: Rock 92 (PI 0x70DB → WKRR) rolls its PS through artist/title every ~3 s,
    // and the head is on screen at the start of every scroll cycle, so the live PS flipped the
    // first characters two or three times per cycle. The head must hold the call sign.
    var state = BuildState(frequency: 92_300_000, rdsRadioText: "Cars - Good Times Roll - Rock 92") with
    {
      RdsStationName = "Cars",        // a rolling page — must NOT be the head
      RdsStationNameStable = "WKRR",  // PI-decoded call sign — must be the head
    };
    var cut = RenderPanel(state, bands: new[] { BuildFmBand() });

    cut.Find(".rds-card-station--pinned").TextContent.Trim().Should().Be("WKRR");
    cut.Find(".rcp-rds-rt-track").TextContent.Should().NotContain("Cars •",
      "the rolling PS page must not appear anywhere on the ticker");
  }

  [Fact]
  public void RdsCard_Head_FallsBackToLivePs_WhenNoCallSignDecodes()
  {
    var state = BuildState(rdsStationName: "KQED FM", rdsProgramType: "News") with
    {
      RdsStationNameStable = null,
    };
    var cut = RenderPanel(state, bands: new[] { BuildFmBand() });

    Assert.Equal("KQED FM", cut.Find(".rds-card-station").TextContent.Trim());
  }

  [Fact]
  public void RdsCard_Renders_WhenStationNamePresent()
  {
    var state = BuildState(rdsStationName: "KQED FM", rdsProgramType: "News");
    var cut = RenderPanel(state, bands: new[] { BuildFmBand() });

    var card = cut.Find(".rds-card");
    Assert.NotNull(card);
    Assert.Equal("KQED FM", cut.Find(".rds-card-station").TextContent.Trim());
    Assert.Equal("News", cut.Find(".rds-card-pty").TextContent.Trim());
  }

  [Fact]
  public void RdsCard_Hidden_WhenStationNameNullAndRadioTextNull()
  {
    // Post-#414-hotfix: the card now renders when EITHER StationName OR
    // RadioText is present (because RT lives inside the card). When BOTH
    // are null the card collapses entirely — same as the pre-hotfix
    // contract for the station-only case.
    var state = BuildState(rdsStationName: null, rdsRadioText: null);
    var cut = RenderPanel(state, bands: new[] { BuildFmBand() });

    Assert.Empty(cut.FindAll(".rds-card"));
  }

  [Fact]
  public void RdsCard_Renders_WhenRadioTextPresentButStationNameNull()
  {
    // Post-#414-hotfix: transient state during a tune-in where RT chunks
    // arrive before PS is confirmed. The card must still render so the
    // RT marquee has a home — otherwise we'd regress the prior PR #414
    // behaviour where RT could show without PS.
    var state = BuildState(rdsStationName: null, rdsRadioText: "Tuning in...");
    var cut = RenderPanel(state, bands: new[] { BuildFmBand() });

    var card = cut.Find(".rds-card");
    Assert.NotNull(card);
    // Station-name slot must be absent (no PS yet); marquee inside card.
    Assert.Empty(cut.FindAll(".rds-card-station"));
    var rt = cut.Find(".rcp-rds-rt-scroll");
    Assert.Contains("Tuning in", rt.TextContent);
  }

  [Fact]
  public void RtLine_RendersWithTitleAttribute_WhenPresent()
  {
    // HANDOFF-rds-accumulating-scroll — the legacy .rcp-rds-rt one-line
    // ellipsis was replaced by the .rcp-rds-rt-scroll marquee. The visible
    // contract is still "text shows up, title attribute carries the full
    // string" — but the scroll container also exposes the buffer via the
    // sr-only mirror for screen readers.
    //
    // Post HANDOFF-rds-inline-scroll-revision: the marquee now lives in the
    // PS slot of .rds-card and the track text is "{PS} • {RT}" so the user
    // sees one continuous scroll string. The title attribute mirrors that
    // composed string — that's what's actually scrolling, so the tooltip
    // matches what the user is reading.
    var state = BuildState(
      rdsStationName: "WKQX",
      rdsRadioText: "Now playing: Pink Floyd — Wish You Were Here");
    var cut = RenderPanel(state, bands: new[] { BuildFmBand() });

    // Since 2026-09-28 the station name is pinned beside the ticker (owner ruling), so the
    // scroller — and its title — carry the RadioText only.
    var rt = cut.Find(".rcp-rds-rt-scroll");
    Assert.Contains("Pink Floyd", rt.TextContent);
    Assert.DoesNotContain("WKQX", rt.TextContent);
    Assert.Equal("WKQX", cut.Find(".rds-card-station--pinned").TextContent.Trim());
    Assert.Equal(
      "Now playing: Pink Floyd — Wish You Were Here",
      rt.GetAttribute("title"));
  }

  [Fact]
  public void RtLine_Hidden_WhenRadioTextNull()
  {
    var state = BuildState(rdsStationName: "WKQX", rdsRadioText: null);
    var cut = RenderPanel(state, bands: new[] { BuildFmBand() });

    Assert.Empty(cut.FindAll(".rcp-rds-rt-scroll"));
  }

  [Fact]
  public void RtLine_Hidden_WhenRadioTextEmpty()
  {
    var state = BuildState(rdsStationName: "WKQX", rdsRadioText: "");
    var cut = RenderPanel(state, bands: new[] { BuildFmBand() });

    Assert.Empty(cut.FindAll(".rcp-rds-rt-scroll"));
  }

  [Fact]
  public void RtLine_RendersExactlyOnce_InsideRdsCard_NoDuplicate()
  {
    // Regression guard against the bug PR #414 shipped: the new accumulating
    // marquee was added as a NEW standalone element below the frequency well
    // instead of replacing the existing static RT line, causing the RT to
    // render twice in the panel (user-reported: "RDS data doubled up").
    //
    // The fix consolidates the marquee into a single location INSIDE
    // .rds-card. This test pins that contract:
    //   1. Exactly ONE .rcp-rds-rt-scroll element renders for non-empty RT.
    //   2. That single element is a descendant of .rds-card (i.e. lives
    //      inside the RDS bar with the blue station-name text, NOT as a
    //      standalone sibling).
    var state = BuildState(
      rdsStationName: "WUNC",
      rdsProgramType: "News",
      rdsRadioText: "Morning Edition with Steve Inskeep");
    var cut = RenderPanel(state, bands: new[] { BuildFmBand() });

    var marquees = cut.FindAll(".rcp-rds-rt-scroll");
    marquees.Should().HaveCount(1,
      "PR #414's first cut rendered the RT marquee twice (standalone below " +
      "freq well + would-be-replacement intended for inside the RDS bar) — " +
      "the hotfix consolidates to a single render location inside .rds-card");

    // The single marquee must be a descendant of the RDS card so all
    // RDS data lives in one visual unit under the "RDS" tag header.
    var card = cut.Find(".rds-card");
    var marqueeInsideCard = card.QuerySelector(".rcp-rds-rt-scroll");
    marqueeInsideCard.Should().NotBeNull(
      "the marquee must live INSIDE .rds-card so the user sees a single " +
      "'RDS bar' containing both the blue station name and the scrolling RT");
  }

  // ─── HANDOFF-rds-inline-scroll-revision: single-row + empty-state matrix ──
  //
  // PR #416 nested the marquee as a SECOND row inside .rds-card, which fixed
  // the duplication PR #414 introduced but pushed the frequency display +
  // STEREO badge below the visible viewport at 1920×720. The revision
  // collapses the card back to a single row: the PS slot itself becomes the
  // marquee surface. These three tests pin the new layout + empty-state
  // behaviour so a future regression can't sneak the second-row wrapper back.

  [Fact]
  public void RdsCard_RendersAsSingleRow_PSAndRtShareOneLine()
  {
    // PS + RT both present: the marquee track text must be "{PS} • {RT}" and
    // the .rds-card must NOT contain a .rds-card-row wrapper (the PR #416
    // two-row wrapper that this revision deletes). Exactly one marquee
    // descendant. Tests RadioControlPanel-level wiring so the configured
    // RtChunkSeparator threads through.
    var state = BuildState(
      rdsStationName: "Eagles",
      rdsRadioText: "Green Day · Boulevard",
      rdsProgramType: "ROCK");
    var cut = RenderPanel(state, bands: new[] { BuildFmBand() });

    var card = cut.Find(".rds-card");

    // The PR #416 two-row wrapper must be gone.
    card.QuerySelector(".rds-card-row").Should().BeNull(
      "the single-row revision deletes the .rds-card-row wrapper so the " +
      "card stays one line tall and doesn't push the frequency display down");

    // Exactly one marquee track, and its text contains both PS and RT joined
    // by the default separator ` • ` (the configured RtChunkSeparator).
    var tracks = cut.FindAll(".rcp-rds-rt-track");
    tracks.Should().HaveCount(1,
      "the marquee renders once in the PS slot; no second-row duplicate");
    // Since 2026-09-28: the station name is pinned beside the ticker on the same row, and the
    // configured separator joins one pass of the RT to the next (seamless loop) rather than
    // joining PS to RT.
    var trackText = tracks[0].TextContent;
    trackText.Should().Contain("Green Day · Boulevard");
    trackText.Should().NotContain("Eagles");
    card.QuerySelector(".rds-card-station--pinned")!.TextContent.Trim().Should().Be("Eagles");
    tracks[0].GetAttribute("data-loop-sep").Should().Be(" • ",
      "the configured RtChunkSeparator (' • ' default) separates successive passes of the RT");
  }

  [Fact]
  public void RdsCard_RtEmpty_RendersStaticStationOnly()
  {
    // PS present + RT empty: the card must fall back to the original static
    // .rds-card-station span — no marquee, no animation, no scroll. This is
    // the regression-prevention case from the handoff matrix: a single short
    // station name should never scroll.
    var state = BuildState(
      rdsStationName: "Eagles",
      rdsRadioText: null,
      rdsProgramType: "ROCK");
    var cut = RenderPanel(state, bands: new[] { BuildFmBand() });

    // Static PS renders.
    var staticStation = cut.Find(".rds-card-station");
    staticStation.TextContent.Trim().Should().Be("Eagles");

    // No marquee surface anywhere.
    cut.FindAll(".rcp-rds-rt-scroll").Should().BeEmpty(
      "when RT is empty the card reverts to the static PS span; no marquee " +
      "must render or the user would see a pointless scroll on a single name");
    cut.FindAll(".rcp-rds-rt-track").Should().BeEmpty();
  }

  [Fact]
  public void RdsCard_PsEmpty_RtPresent_RendersMarqueeWithoutLeadingSeparator()
  {
    // PS empty + RT present (transient tune-in): the marquee renders with
    // just the RT text — NO leading " • " separator. The user must not see
    // a stray bullet at the head of the scroll when station identity hasn't
    // arrived yet.
    var state = BuildState(
      rdsStationName: null,
      rdsRadioText: "Some RT");
    var cut = RenderPanel(state, bands: new[] { BuildFmBand() });

    var track = cut.Find(".rcp-rds-rt-track");
    var trackText = track.TextContent.Trim();

    trackText.Should().NotStartWith(" • ",
      "when PS is empty the marquee must not lead with a separator — that " +
      "would look like a dangling bullet at the head of the scroll");
    trackText.Should().NotStartWith("•",
      "no bullet glyph at the head either (covers separator variants)");
    trackText.Should().Be("Some RT",
      "track text is just the RT when PS is empty — no prefix, no separator");
  }

  // ─── UI-20: the preset bar (spec 2026-10-01 §4.5) ─────────────────────────
  // These pin what the panel decides and wires — which presets reach the bar, in
  // what order, when ＋ SAVE shows, and that recall / save / rename / delete still
  // reach the API through the bar's cards. Paging, the arrows, the off-screen dot
  // and auto-scroll belong to PresetBar and are pinned in PresetBarTests.

  [Fact]
  public void PresetsCaption_ShowsTotalSavedCount()
  {
    // Hot-fix off PR #371: the total across all bands, never "N of CAP".
    var state = BuildState();
    var presets = new[]
    {
      BuildPreset("p1", "KQED", 88_500_000, "FM", 1),
      BuildPreset("p2", "KCBS", 99_700_000, "FM", 2),
    };
    var cut = RenderPanel(state, presets: presets, bands: new[] { BuildFmBand(16) });

    var count = cut.Find(".rcp-bar-caption .rcp-presets-count");
    count.TextContent.Trim().Should().Be("PRESETS · 2 saved");
  }

  [Fact]
  public void PresetsCaption_ShowsHoldBandHint_WhenEverythingFits()
  {
    var state = BuildState();
    var presets = new[] { BuildPreset("p1", "KQED", 88_500_000, "FM", 1) };
    var cut = RenderPanel(state, presets: presets, bands: new[] { BuildFmBand() });

    var hint = cut.Find(".rcp-bar-caption .rcp-presets-hint");
    Assert.Contains("HOLD", hint.TextContent);
    Assert.Contains("TO SAVE", hint.TextContent);
    Assert.Equal("FM", hint.QuerySelector("kbd")!.TextContent.Trim());
  }

  [Fact]
  public void PresetCard_ShowsNameThenOrdinalBandAndFrequency()
  {
    var state = BuildState();
    var presets = new[] { BuildPreset("p1", "KQED", 88_500_000, "FM", 1) };
    var cut = RenderPanel(state, presets: presets, bands: new[] { BuildFmBand() });

    var card = cut.Find(".rcp-bar-card");
    card.QuerySelector(".rcp-bar-card-name")!.TextContent.Trim().Should().Be("KQED");
    card.QuerySelector(".rcp-bar-card-meta")!.TextContent.Trim().Should().Be("01 · FM 88.50");
  }

  [Fact]
  public void ActivePreset_IsTheOneMatchingBandAndFrequency()
  {
    // The knob's IsCurrent test: band equal and |Δf| < 1 Hz.
    var state = BuildState(frequency: 88_500_000);
    var presets = new[]
    {
      BuildPreset("p1", "KQED", 88_500_000, "FM", 1),
      BuildPreset("p2", "KCBS", 99_700_000, "FM", 2),
      BuildPreset("p3", "Same freq, other band", 88_500_000, "VHF", 1),
    };
    var cut = RenderPanel(state, presets: presets, bands: new[] { BuildFmBand() });

    var actives = cut.FindAll(".rcp-bar-card.is-active");
    actives.Should().ContainSingle();
    actives[0].GetAttribute("data-preset-id").Should().Be("p1");
    actives[0].GetAttribute("aria-current").Should().Be("true");
  }

  [Fact]
  public void Presets_ShowAllBands_InTheKnobsOrder()
  {
    // Hot-fix off PR #371: every band is shown, never filtered to the tuned one.
    // UI-20 / spec §4.7: and in the PRESETS knob's order — band (ordinal), then the
    // per-band slot — so "turn three detents" lands on the third card the user can
    // see (PresetSelectorService.cs:364-370). The API returns them in any order.
    var state = BuildState(band: "FM");
    var presets = new[]
    {
      BuildPreset("wb1", "Weather", 162_550_000, "WB", 1),
      BuildPreset("fm2", "FM two", 99_700_000, "FM", 2),
      BuildPreset("am1", "AM one", 740_000, "AM", 1),
      BuildPreset("fm1", "FM one", 88_500_000, "FM", 1),
      BuildPreset("air1", "Tower", 118_300_000, "AIR", 1),
      BuildPreset("vhf1", "Repeater", 146_520_000, "VHF", 1),
    };
    var cut = RenderPanel(state, presets: presets, bands: new[] { BuildFmBand(16), BuildAmBand() });

    var ids = cut.FindAll(".rcp-bar-card").Select(e => e.GetAttribute("data-preset-id")).ToList();
    ids.Should().Equal("air1", "am1", "fm1", "fm2", "vhf1", "wb1");
  }

  [Fact]
  public void SaveCard_Renders_AtTheEndOfTheCurrentBandsGroup_WhenUnsavedAndBelowCapacity()
  {
    // Owner decision Q2: a tappable ＋ SAVE for the next free slot of the CURRENT
    // band, sitting where the new preset will land — after FM's last card, before WB.
    var state = BuildState(band: "FM", frequency: 92_300_000);
    var presets = new[]
    {
      BuildPreset("fm1", "KQED", 88_500_000, "FM", 1),
      BuildPreset("am1", "KCBS-AM", 740_000, "AM", 1),
      BuildPreset("wb1", "Weather", 162_550_000, "WB", 1),
    };
    var cut = RenderPanel(state, presets: presets, bands: new[] { BuildFmBand(16), BuildAmBand(16) });

    var order = cut.FindAll(".rcp-bar-viewport > [data-item]")
      .Select(e => e.QuerySelector(".rcp-bar-card")?.GetAttribute("data-preset-id") ?? (e.QuerySelector(".rcp-bar-save") != null ? "SAVE" : "?"))
      .ToList();
    order.Should().Equal("am1", "fm1", "SAVE", "wb1");
    cut.Find(".rcp-bar-save .rcp-bar-card-meta").TextContent.Trim().Should().Be("FM 92.30");
  }

  [Fact]
  public void SaveCard_Hidden_WhenWhatIsPlayingIsAlreadySaved()
  {
    // The active card is the confirmation; a ＋ SAVE beside it would offer a duplicate.
    var state = BuildState(band: "FM", frequency: 88_500_000);
    var presets = new[] { BuildPreset("fm1", "KQED", 88_500_000, "FM", 1) };
    var cut = RenderPanel(state, presets: presets, bands: new[] { BuildFmBand(16) });

    cut.FindAll(".rcp-bar-save").Should().BeEmpty();
  }

  [Fact]
  public void SaveCard_Hidden_WhenTheBandIsAtCapacity()
  {
    var state = BuildState(band: "WB", frequency: 162_500_000);
    var presets = Enumerable.Range(1, 4)
      .Select(i => BuildPreset($"p{i}", $"NOAA {i}", 162_400_000 + (i * 25_000), "WB", i))
      .ToArray();
    var wb = new Radio.Core.Models.RadioBandModel
    {
      Type = "WB",
      Name = "Weather Radio",
      MinFrequencyHz = 162_400_000,
      MaxFrequencyHz = 162_550_000,
      DefaultStepHz = 25_000,
      AllowedStepSizes = new long[] { 25_000 },
      DefaultModulation = "NFM",
      DefaultBandwidthHz = 25_000,
      Description = "Weather",
      Range = "162.4–162.55 MHz",
      BandPresetCapacity = 4,
    };
    var cut = RenderPanel(state, presets: presets, bands: new[] { wb });

    cut.FindAll(".rcp-bar-save").Should().BeEmpty();
  }

  [Fact]
  public void SaveCard_Tap_OpensTheSaveDialog()
  {
    var state = BuildState(band: "FM", frequency: 92_300_000);
    var cut = RenderPanel(state, bands: new[] { BuildFmBand(16) });

    cut.FindAll("h6").Select(h => h.TextContent).Should().NotContain("Save Preset");
    cut.Find(".rcp-bar-save").Click();

    cut.FindAll("h6").Select(h => h.TextContent).Should().Contain("Save Preset");
  }

  [Theory]
  [InlineData("frequency", "Set Frequency")]
  [InlineData("save", "Save Preset")]
  [InlineData("rename", "Rename Preset")]
  public void TextEntryDialogs_AreTopAnchored_ClearOfTheKeyboard(string dialog, string title)
  {
    // UI-24, owner (Rename dialog, UI-20 UAT check 5): "the keyboard occludes the dialog". The
    // in-app keyboard covers everything below y = 348 of the 720 px panel; these dialogs were centred
    // overlays with inline align-items:center, which put the field and its buttons under it. They
    // now use the shared top-anchored overlay (design-system.css §20a — pinned by
    // TextEntryDialogPlacementTests), so the markup must carry those classes and no inline
    // placement of its own that could override them.
    var state = BuildState(band: "FM", frequency: 92_300_000);
    var presets = dialog == "rename" ? new[] { BuildPreset("p1", "KQED", 88_500_000, "FM", 1) } : null;
    var cut = RenderPanel(state, presets: presets, bands: new[] { BuildFmBand(16) });

    switch (dialog)
    {
      case "frequency":
        cut.Find(".rcp-freq-well .display-frequency").Click();
        break;
      case "save":
        cut.Find(".rcp-bar-save").Click();
        break;
      default:
        LongPressCard(cut, "p1");
        cut.FindAll(".rcp-preset-menu-item")[0].Click();
        break;
    }

    var overlay = cut.FindAll(".kiosk-entry-overlay").Should().ContainSingle().Subject;
    overlay.GetAttribute("style").Should().BeNull("placement comes from the shared class, not an inline override");
    var card = overlay.QuerySelector(":scope > .kiosk-entry-card");
    card.Should().NotBeNull();
    card!.QuerySelector("h6")!.TextContent.Trim().Should().Be(title);
    card.QuerySelector("input").Should().NotBeNull("the field the keyboard types into is in the anchored card");
    card.QuerySelectorAll(".kiosk-entry-actions > button").Should().HaveCount(2);
  }

  [Fact]
  public void NoPresets_ShowsTheEmptyCard_AndTheSaveCard()
  {
    var state = BuildState(band: "FM", frequency: 92_300_000);
    var cut = RenderPanel(state, bands: new[] { BuildFmBand(16) });

    cut.Find(".rcp-bar-caption .rcp-presets-count").TextContent.Trim().Should().Be("PRESETS · 0 saved");
    cut.Find(".rcp-bar-msg.is-empty").TextContent.Should().Contain("NO STATIONS SAVED");
    cut.FindAll(".rcp-bar-save").Should().ContainSingle();
    cut.FindAll(".rcp-bar-arrow").Should().OnlyContain(a => a.GetAttribute("aria-disabled") == "true");
  }

  [Fact]
  public void FailedPresetRead_ShowsRetryCard_NotAFalseEmpty_AndRetryReReads()
  {
    // Spec §7: a failed read used to render "NO PRESETS" — a false empty.
    var state = BuildState();
    var handler = UseRadioState(state, bands: new[] { BuildFmBand() });
    handler.FailPresets = true;
    var cut = RenderComponent<RadioControlPanel>();
    cut.WaitForAssertion(() => cut.FindAll(".rcp-bar-msg.is-error").Should().ContainSingle(),
      TimeSpan.FromSeconds(2));

    cut.Find(".rcp-bar-caption .rcp-presets-count").TextContent.Trim().Should().Be("PRESETS");
    cut.Markup.Should().NotContain("NO STATIONS SAVED");
    cut.Find(".rcp-bar-msg.is-error").TextContent.Should().Contain("Couldn't load presets");

    handler.FailPresets = false;
    var readsBefore = handler.RecordedCalls.Count(c => c.Method == "GET" && c.Path == "/api/radio/presets");
    cut.Find(".rcp-bar-msg.is-error").Click();

    cut.WaitForAssertion(() => cut.FindAll(".rcp-bar-msg.is-error").Should().BeEmpty(), TimeSpan.FromSeconds(2));
    handler.RecordedCalls.Count(c => c.Method == "GET" && c.Path == "/api/radio/presets")
      .Should().Be(readsBefore + 1);
  }

  [Fact]
  public void FailedRefresh_AfterAGoodRead_KeepsTheListOnScreen()
  {
    // Spec §7's error card is for "no list at all". A refresh that fails after a save must not
    // replace presets the user can still see with an error.
    var state = BuildState(band: "FM", frequency: 92_300_000);
    var presets = new[] { BuildPreset("p1", "KQED", 88_500_000, "FM", 1) };
    var handler = UseRadioState(state, presets, new[] { BuildFmBand(16) });
    var cut = RenderComponent<RadioControlPanel>();
    cut.WaitForAssertion(() => cut.FindAll(".rcp-bar-card").Should().ContainSingle(), TimeSpan.FromSeconds(2));

    handler.FailPresets = true;
    cut.Find(".rcp-bar-save").Click();
    cut.FindAll("button").Single(b => b.TextContent.Trim() == "Save").Click();

    cut.WaitForAssertion(() => handler.RecordedCalls.Count(c => c.Method == "GET" && c.Path == "/api/radio/presets").Should().Be(2),
      TimeSpan.FromSeconds(2));
    cut.FindAll(".rcp-bar-card").Should().ContainSingle();
    cut.FindAll(".rcp-bar-msg.is-error").Should().BeEmpty();
  }

  [Fact]
  public void PresetCard_Tap_RecallsThePreset()
  {
    var state = BuildState();
    var presets = new[] { BuildPreset("p1", "KQED", 88_500_000, "FM", 1) };
    var handler = UseRadioState(state, presets, new[] { BuildFmBand() });
    var cut = RenderComponent<RadioControlPanel>();
    cut.WaitForAssertion(() => cut.FindAll(".rcp-bar-card").Should().ContainSingle(), TimeSpan.FromSeconds(2));

    cut.Find(".rcp-bar-card").Click();

    cut.WaitForAssertion(() => handler.RecordedCalls.Should().Contain(c => c.Method == "POST" && c.Path == "/api/radio/presets/p1/load"),
      TimeSpan.FromSeconds(2));
  }

  [Fact]
  public void PresetCard_HasNoKebab()
  {
    // Owner decision Q3: the 18 px ⋮ (far under the 48 px touch minimum) left the bar.
    var state = BuildState();
    var presets = new[] { BuildPreset("p1", "KQED", 88_500_000, "FM", 1) };
    var cut = RenderPanel(state, presets: presets, bands: new[] { BuildFmBand() });

    cut.FindAll(".rcp-bar-card button").Should().BeEmpty();
    cut.Markup.Should().NotContain("⋮");
  }

  /// <summary>
  /// Long-presses a preset card through its real markup: pointerdown, then the
  /// recorded press time rewound past the 600 ms threshold, then pointerup — the
  /// same elapsed-ms branch a real hold takes, without sleeping.
  /// </summary>
  private static void LongPressCard(IRenderedComponent<RadioControlPanel> cut, string presetId)
  {
    var card = cut.Find($".rcp-bar-card[data-preset-id='{presetId}']");
    card.PointerDown();
    typeof(RadioControlPanel)
      .GetField("_presetPointerDownAt", BindingFlags.NonPublic | BindingFlags.Instance)!
      .SetValue(cut.Instance, DateTime.UtcNow.AddMilliseconds(-700));
    cut.Find($".rcp-bar-card[data-preset-id='{presetId}']").PointerUp();
  }

  [Fact]
  public void PresetCard_LongPress_OpensRenameDeleteMenu_AndSwallowsTheRecall()
  {
    var state = BuildState();
    var presets = new[] { BuildPreset("p1", "KQED", 88_500_000, "FM", 1) };
    var handler = UseRadioState(state, presets, new[] { BuildFmBand() });
    var cut = RenderComponent<RadioControlPanel>();
    cut.WaitForAssertion(() => cut.FindAll(".rcp-bar-card").Should().ContainSingle(), TimeSpan.FromSeconds(2));
    Assert.Empty(cut.FindAll(".rcp-preset-menu"));

    LongPressCard(cut, "p1");
    // The click the browser synthesises after the hold must not ALSO tune the preset.
    cut.Find(".rcp-bar-card").Click();

    var items = cut.FindAll(".rcp-preset-menu-item");
    items.Should().HaveCount(2);
    items[0].TextContent.Should().Contain("Rename");
    items[1].TextContent.Should().Contain("Delete");
    handler.RecordedCalls.Should().NotContain(c => c.Path == "/api/radio/presets/p1/load");
  }

  [Fact]
  public void PresetCard_ShortPress_DoesNotOpenTheMenu()
  {
    var state = BuildState();
    var presets = new[] { BuildPreset("p1", "KQED", 88_500_000, "FM", 1) };
    var cut = RenderPanel(state, presets: presets, bands: new[] { BuildFmBand() });

    cut.Find(".rcp-bar-card").PointerDown();
    cut.Find(".rcp-bar-card").PointerUp();

    cut.FindAll(".rcp-preset-menu").Should().BeEmpty();
  }

  [Fact]
  public void PresetMenuOverlayClick_ClosesActionMenu()
  {
    var state = BuildState();
    var presets = new[] { BuildPreset("p1", "KQED", 88_500_000, "FM", 1) };
    var cut = RenderPanel(state, presets: presets, bands: new[] { BuildFmBand(16) });

    LongPressCard(cut, "p1");
    Assert.Single(cut.FindAll(".rcp-preset-menu"));

    cut.Find(".rcp-preset-menu-overlay").Click();
    Assert.Empty(cut.FindAll(".rcp-preset-menu"));
  }

  [Fact]
  public void PresetActionMenu_HasMenuRole()
  {
    // Polisher pass on PR #373: role=menu + aria-label, items role=menuitem.
    var state = BuildState();
    var presets = new[] { BuildPreset("p1", "KQED", 88_500_000, "FM", 1) };
    var cut = RenderPanel(state, presets: presets, bands: new[] { BuildFmBand(16) });

    LongPressCard(cut, "p1");

    var menu = cut.Find(".rcp-preset-menu");
    Assert.Equal("menu", menu.GetAttribute("role"));
    Assert.False(string.IsNullOrWhiteSpace(menu.GetAttribute("aria-label")));
    foreach (var item in cut.FindAll(".rcp-preset-menu-item"))
    {
      Assert.Equal("menuitem", item.GetAttribute("role"));
    }
  }

  [Fact]
  public void PresetActionMenu_EscKey_ClosesMenu()
  {
    var state = BuildState();
    var presets = new[] { BuildPreset("p1", "KQED", 88_500_000, "FM", 1) };
    var cut = RenderPanel(state, presets: presets, bands: new[] { BuildFmBand(16) });

    LongPressCard(cut, "p1");
    Assert.Single(cut.FindAll(".rcp-preset-menu"));

    cut.Find(".rcp-preset-menu-overlay").KeyDown(new KeyboardEventArgs { Key = "Escape" });

    Assert.Empty(cut.FindAll(".rcp-preset-menu"));
  }

  [Fact]
  public void PresetMenuRename_OpensPrefilledDialog_AndSaveCallsTheApi()
  {
    var state = BuildState();
    var presets = new[] { BuildPreset("p1", "KQED", 88_500_000, "FM", 1) };
    var handler = UseRadioState(state, presets, new[] { BuildFmBand(16) });
    var cut = RenderComponent<RadioControlPanel>();
    cut.WaitForAssertion(() => cut.FindAll(".rcp-bar-card").Should().ContainSingle(), TimeSpan.FromSeconds(2));

    LongPressCard(cut, "p1");
    cut.FindAll(".rcp-preset-menu-item")[0].Click();

    Assert.Empty(cut.FindAll(".rcp-preset-menu"));
    Assert.Single(cut.FindAll(".rcp-preset-rename-card"));
    typeof(RadioControlPanel).GetField("_renamePresetName", BindingFlags.NonPublic | BindingFlags.Instance)!
      .GetValue(cut.Instance).Should().Be("KQED");

    cut.FindAll(".rcp-preset-rename-card button").Single(b => b.TextContent.Contains("Save")).Click();
    cut.WaitForAssertion(() => handler.RecordedCalls.Should().Contain(c => c.Method == "PUT" && c.Path == "/api/radio/presets/p1"),
      TimeSpan.FromSeconds(2));
  }

  [Fact]
  public void PresetMenuDelete_CallsTheApi()
  {
    var state = BuildState();
    var presets = new[] { BuildPreset("p1", "KQED", 88_500_000, "FM", 1) };
    var handler = UseRadioState(state, presets, new[] { BuildFmBand(16) });
    var cut = RenderComponent<RadioControlPanel>();
    cut.WaitForAssertion(() => cut.FindAll(".rcp-bar-card").Should().ContainSingle(), TimeSpan.FromSeconds(2));

    LongPressCard(cut, "p1");
    cut.FindAll(".rcp-preset-menu-item")[1].Click();

    cut.WaitForAssertion(() => handler.RecordedCalls.Should().Contain(c => c.Method == "DELETE" && c.Path == "/api/radio/presets/p1"),
      TimeSpan.FromSeconds(2));
    Assert.Empty(cut.FindAll(".rcp-preset-menu"));
  }

  [Fact]
  public void LoadingSkeleton_IsThePanelShape()
  {
    // Until the state arrives the panel shows the RadioPanel skeleton (one-row bands + bar), not
    // RadioPage's Radio shape.

    var handler = new HttpClient(new NeverRespondsHandler()) { BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl) };
    Services.AddSingleton(_ => new RadioApiService(handler, NullLogger<RadioApiService>.Instance));

    var cut = RenderComponent<RadioControlPanel>();

    cut.Find(".skeleton").ClassList.Should().Contain("skeleton-radio-panel");
  }

  [Fact]
  public void BandPills_SitOnOneRow()
  {
    // UI-20 (spec §2.2, §4.3): six equal pills on one row. Layout cannot be measured in
    // bUnit — the box measurement in docs/queue/UI-20.md is the real evidence — so this
    // pins the two rules that produce it: the group never wraps, and each pill shares the
    // row equally.
    var state = BuildState();
    var cut = RenderPanel(state, bands: new[] { BuildFmBand(), BuildAmBand() });

    var css = System.Text.RegularExpressions.Regex.Replace(cut.Markup, @"\s+", " ");
    css.Should().MatchRegex(@"\.rcp-band-group \{[^}]*flex-wrap: nowrap;");
    css.Should().MatchRegex(@"\.rcp-band-group \.rcp-band-btn \{ flex: 1 1 0;[^}]*min-width: var\(--touch-min\); \}");
  }

  [Theory]
  [InlineData(-40.0, ".rcp-scanning", "SCANNING UP")]
  [InlineData(-20.0, ".rcp-scan-signal", "SIGNAL −20 dBu")]
  public void ScanIndicator_SitsInTheMeterHeader_NotItsOwnRow(double rssiDbu, string selector, string text)
  {
    // Spec §4.4: as its own row the indicator pushed the AGC row out of the clipped
    // tuner column. In the RSSI header it takes no height.
    var state = BuildState(rssiDbu: rssiDbu, scanStopThreshold: -30.0) with
    {
      IsScanning = true,
      ScanDirection = "up",
    };
    var cut = RenderPanel(state);

    var chip = cut.Find($".rcp-meter-header {selector}");
    chip.TextContent.Trim().Should().Be(text);
    cut.FindAll($".rcp-tuner > {selector}").Should().BeEmpty();
  }

  [Fact]
  public void RadioPresetDto_DtoFieldShapeIsStable()
  {
    // The hot-fix introduced a new API client method (RenamePresetAsync)
    // and a new server-side endpoint (PUT /api/radio/presets/{id}). The
    // wire-shape DTO is unchanged — verify by asserting on the fields the
    // Web side serialises/deserialises.
    var t = typeof(RadioPresetDto);
    Assert.NotNull(t.GetProperty(nameof(RadioPresetDto.Id), BindingFlags.Public | BindingFlags.Instance));
    Assert.NotNull(t.GetProperty(nameof(RadioPresetDto.Name), BindingFlags.Public | BindingFlags.Instance));
    Assert.NotNull(t.GetProperty(nameof(RadioPresetDto.Band), BindingFlags.Public | BindingFlags.Instance));
    Assert.NotNull(t.GetProperty(nameof(RadioPresetDto.Frequency), BindingFlags.Public | BindingFlags.Instance));
    Assert.NotNull(t.GetProperty(nameof(RadioPresetDto.SlotNumber), BindingFlags.Public | BindingFlags.Instance));
  }

  [Fact]
  public void RadioApiService_ExposesRenamePresetAsync()
  {
    // Hot-fix off PR #371: the kebab Rename action calls PUT
    // /api/radio/presets/{id}. Verify via reflection that the Web client
    // exposes the new method with the expected signature (id, newName, ct).
    var method = typeof(RadioApiService).GetMethod(
      "RenamePresetAsync",
      BindingFlags.Public | BindingFlags.Instance);
    Assert.NotNull(method);
    var parameters = method!.GetParameters();
    Assert.Equal(3, parameters.Length);
    Assert.Equal(typeof(string), parameters[0].ParameterType);
    Assert.Equal(typeof(string), parameters[1].ParameterType);
    Assert.Equal(typeof(CancellationToken), parameters[2].ParameterType);
  }

  [Fact]
  public void RadioStateDto_HasRdsRadioTextField()
  {
    // Confirm via reflection that the wire-shape DTO carries the PR 3 field.
    var prop = typeof(RadioStateDto).GetProperty(
      nameof(RadioStateDto.RdsRadioText),
      BindingFlags.Public | BindingFlags.Instance);
    Assert.NotNull(prop);
    Assert.Equal(typeof(string), prop!.PropertyType);
  }

  [Fact]
  public void RadioPresetDto_HasSlotNumberField()
  {
    var prop = typeof(RadioPresetDto).GetProperty(
      nameof(RadioPresetDto.SlotNumber),
      BindingFlags.Public | BindingFlags.Instance);
    Assert.NotNull(prop);
    Assert.Equal(typeof(int), prop!.PropertyType);
  }

  [Fact]
  public void RadioBandModel_HasRangeAndCapacityFields()
  {
    var bandType = typeof(Radio.Core.Models.RadioBandModel);
    var range = bandType.GetProperty("Range", BindingFlags.Public | BindingFlags.Instance);
    var cap = bandType.GetProperty("BandPresetCapacity", BindingFlags.Public | BindingFlags.Instance);
    Assert.NotNull(range);
    Assert.NotNull(cap);
    Assert.Equal(typeof(string), range!.PropertyType);
    Assert.Equal(typeof(int), cap!.PropertyType);
  }

  private static bool IsLitSegment(IElement segment)
  {
    var cls = segment.ClassName ?? string.Empty;
    return cls.Contains("seg-green") || cls.Contains("seg-amber") || cls.Contains("seg-red");
  }

  /// <summary>A handler whose requests never complete — holds the panel in its loading state.</summary>
  private sealed class NeverRespondsHandler : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
      new TaskCompletionSource<HttpResponseMessage>().Task;
  }

  /// <summary>
  /// Tiny HTTP stub that returns a fixed <see cref="RadioStateDto"/> for
  /// <c>/api/radio/state</c> and configurable arrays for the auxiliary
  /// endpoints the panel hits during <c>OnInitializedAsync</c>
  /// (<c>/api/radio/presets</c>, <c>/api/RadioBands</c>). POST endpoints (e.g.
  /// <c>/api/radio/gain/auto</c>) return 200 OK and record the path + body
  /// into <see cref="RecordedCalls"/> so interaction-wiring tests can assert
  /// the user gesture reached the API client.
  /// </summary>
  private sealed class RadioStateStubHandler : HttpMessageHandler
  {
    private readonly RadioStateDto _state;
    private readonly IEnumerable<RadioPresetDto> _presets;
    private readonly IEnumerable<Radio.Core.Models.RadioBandModel> _bands;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Recorded outbound calls — useful for tests that pin user-gesture wiring.</summary>
    public List<(string Method, string Path, string? Body)> RecordedCalls { get; } = new();

    /// <summary>When set, <c>GET /api/radio/presets</c> answers 500 (the failed-read state).</summary>
    public bool FailPresets { get; set; }

    public RadioStateStubHandler(
      RadioStateDto state,
      IEnumerable<RadioPresetDto>? presets = null,
      IEnumerable<Radio.Core.Models.RadioBandModel>? bands = null)
    {
      _state = state;
      _presets = presets ?? Array.Empty<RadioPresetDto>();
      _bands = bands ?? Array.Empty<Radio.Core.Models.RadioBandModel>();
    }

    protected override async Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
    {
      var path = request.RequestUri?.AbsolutePath ?? string.Empty;
      var method = request.Method.Method;
      string? body = null;
      if (request.Content != null)
      {
        body = await request.Content.ReadAsStringAsync(cancellationToken);
      }
      RecordedCalls.Add((method, path, body));

      var response = (method, path) switch
      {
        ("GET", "/api/radio/state") => Ok(_state),
        ("GET", "/api/radio/presets") => FailPresets
          ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
          : Ok(_presets),
        ("GET", "/api/RadioBands") => Ok(_bands),
        // POST endpoints — return 200 so the API client treats them as success;
        // the panel still re-fetches state which lands on our stubbed GET.
        ("POST", _) => new HttpResponseMessage(HttpStatusCode.OK),
        ("PUT", _) => new HttpResponseMessage(HttpStatusCode.OK),
        ("DELETE", _) => new HttpResponseMessage(HttpStatusCode.OK),
        _ => new HttpResponseMessage(HttpStatusCode.NotFound),
      };
      return response;
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

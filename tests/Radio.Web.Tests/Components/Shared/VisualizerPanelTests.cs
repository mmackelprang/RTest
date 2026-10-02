using System.Reflection;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Radzen;
using Radio.Web.Components.Shared;
using Radio.Web.Services;
using Radio.Web.Services.ApiClients;
using Radio.Web.Models;
using Radio.Web.Services.Hub;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// bUnit tests for the <see cref="VisualizerPanel"/> mode picker.
///
/// PR 4 of the design tightening arc (handoff §P1·3) promoted the mode
/// picker from a floating chip group into a full-width header with six
/// segments. AUD-76 then replaced Fall (the audio spectrogram) with BAND
/// (the stored FM band map) in the same position and removed VU, leaving
/// five. UI-29 moved BAND to the end and made it selectable only while the
/// radio is the active source:
///
/// <list type="bullet">
///   <item>Wave (Waveform)</item>
///   <item>Spectrum (Spectrum) — the default</item>
///   <item>Ring (Circular)</item>
///   <item>Phase (PhaseScope)</item>
///   <item>BAND (Band) — disabled unless the radio is active</item>
/// </list>
///
/// These tests lock the contract that exactly those five segments render
/// in the header and that clicking each enabled segment activates it. This
/// fixture's API is unreachable, so the radio is never active here and BAND
/// is always disabled. BAND's own behaviour, including UI-29's fallback, is in
/// <see cref="VisualizerPanelBandTests"/>; the hub's disconnected state
/// (UI-30) is in <see cref="VisualizerPanelConnectionTests"/>.
/// </summary>
public class VisualizerPanelTests : TestContext
{
  private readonly ILoggerFactory _loggerFactory;

  public VisualizerPanelTests()
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
        { "ApiBaseUrl", HermeticTestRig.ApiBaseUrl }
      })
      .Build();

    Services.AddSingleton<IConfiguration>(configuration);
    Services.AddSingleton(_loggerFactory);
    Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    Services.AddRadzenComponents();

    // ConfigurationApiService needs an HttpClient; it'll fail to connect
    // (no server running in the test rig) which is fine — the panel
    // swallows preference-load exceptions and stays at its default mode.
    Services.AddHttpClient<ConfigurationApiService>();

    // BAND (AUD-76) reads the radio API and may switch sources; hermetic like the rest.
    Services.AddHttpClient<RadioApiService>();
    Services.AddHttpClient<SourcesApiService>();
    Services.AddSingleton(sp =>
      new AudioStateHubService(
        NullLogger<AudioStateHubService>.Instance,
        sp.GetRequiredService<IConfiguration>(),
        transport: new OfflineHubTransport()));

    // The hub services connect lazily; without a server they stay
    // disconnected. The panel handles that path (IsConnected=false → dot
    // stays red, no data callbacks fire).
    Services.AddSingleton(sp =>
      new AudioVisualizationHubService(
        NullLogger<AudioVisualizationHubService>.Instance,
        sp.GetRequiredService<IConfiguration>(),
        transport: new OfflineHubTransport()
      )
    );

    Services.AddSingleton<VisualizerTelemetryService>();
  }

  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      _loggerFactory?.Dispose();
    }
    base.Dispose(disposing);
  }

  [Fact]
  public void ModePicker_RendersExactlyFiveModeButtons()
  {
    // AUD-76 removed VU and replaced Fall with BAND. A regression in PR 4 once dropped segments
    // from the UI while the wiring was intact — this locks the count both ways.
    var cut = RenderComponent<VisualizerPanel>();
    cut.FindAll(".visualizer-mode").Count.Should().Be(5);
  }

  [Fact]
  public void ModePicker_RendersAllFiveLabelsInOrder()
  {
    // UI-29: BAND is last (AUD-76 had put it where Fall was). VU is gone. UI-31: it reads Radio.
    var cut = RenderComponent<VisualizerPanel>();
    var labels = cut.FindAll(".visualizer-mode")
      .Select(e => e.TextContent.Trim())
      .ToList();
    labels.Should().Equal("Wave", "Spectrum", "Ring", "Phase", "Radio");
  }

  [Fact]
  public void ModePicker_DefaultsToSpectrumModeActive()
  {
    // _currentMode defaults to Spectrum when no saved preference loads
    // (the config call fails in the test rig and is swallowed). AUD-76: it was VU.
    var cut = RenderComponent<VisualizerPanel>();
    var buttons = cut.FindAll(".visualizer-mode").ToList();
    var spectrum = buttons.Single(b => b.TextContent.Trim() == "Spectrum");
    (spectrum.GetAttribute("class") ?? string.Empty).Should().Contain("is-active");
    spectrum.GetAttribute("aria-selected").Should().Be("true");

    foreach (var b in buttons.Where(b => b != spectrum))
    {
      (b.GetAttribute("class") ?? string.Empty).Should().NotContain("is-active");
      b.GetAttribute("aria-selected").Should().Be("false");
    }
  }

  [Theory]
  [InlineData("Spectrogram", "Band")]
  [InlineData("spectrogram", "Band")]
  [InlineData("VUMeter", "Spectrum")]
  [InlineData("garbage", "Spectrum")]
  [InlineData("", "Spectrum")]
  [InlineData(null, "Spectrum")]
  [InlineData("2", "Spectrum")]
  [InlineData("Band", "Band")]
  [InlineData("Waveform", "Waveform")]
  [InlineData("Circular", "Circular")]
  [InlineData("PhaseScope", "PhaseScope")]
  [InlineData("Spectrum", "Spectrum")]
  public void SavedPreference_MapsRemovedModesOntoTheirSuccessors(string? saved, string expected)
  {
    // AUD-76 removed two modes that a saved ui.visualizer/defaultMode can still name: "Spectrogram"
    // (Fall) lands on BAND, which took its place; "VUMeter" and anything unreadable land on the new
    // default, Spectrum. A numeric string is not a mode name and must not select by ordinal.
    VisualizerPanel.ParseSavedMode(saved).ToString().Should().Be(expected);
  }

  [Fact]
  public void ModePicker_RadioButton_HasSignalMapAriaLabel()
  {
    // UI-31: the band map's tab reads Radio (RADIO on screen: the picker upper-cases every label); VisualizationMode.Band behind it is unchanged.
    var cut = RenderComponent<VisualizerPanel>();
    var radio = cut.FindAll(".visualizer-mode").First(b => b.TextContent.Trim() == "Radio");
    radio.GetAttribute("aria-label").Should().Be("Radio signal map mode");
  }

  [Fact]
  public void ModePicker_RingButton_HasCircularAriaLabel()
  {
    // Ring is the user-facing label for VisualizationMode.Circular.
    var cut = RenderComponent<VisualizerPanel>();
    var ring = cut.FindAll(".visualizer-mode").First(b => b.TextContent.Trim() == "Ring");
    (ring.GetAttribute("aria-label") ?? string.Empty).Should().ContainAll("Circular", "mode");
  }

  [Fact]
  public void ModePicker_PhaseButton_HasPhaseScopeAriaLabel()
  {
    // Phase is the user-facing label for VisualizationMode.PhaseScope.
    var cut = RenderComponent<VisualizerPanel>();
    var phase = cut.FindAll(".visualizer-mode").First(b => b.TextContent.Trim() == "Phase");
    (phase.GetAttribute("aria-label") ?? string.Empty).Should().ContainAll("Phase", "mode");
  }

  [Fact]
  public async Task ModePicker_Band_IsDisabledAndInert_WhenTheRadioIsNotActive()
  {
    // UI-29. This fixture's API is unreachable, so the radio is not known to be active. The tab is
    // rendered disabled (out of the tab order, taps ignored by the browser) and SelectMode refuses it
    // as well, for a tap that lands before the re-render. Enabling it is VisualizerPanelBandTests'.
    var cut = RenderComponent<VisualizerPanel>();
    var band = cut.FindAll(".visualizer-mode").First(b => b.TextContent.Trim() == "Radio");
    band.HasAttribute("disabled").Should().BeTrue();
    band.GetAttribute("aria-disabled").Should().Be("true");
    band.GetAttribute("aria-label").Should().Be("Radio signal map mode", "the name stays fixed; the hint is a description");
    band.HasAttribute("title").Should().BeFalse("UI-31: the owner wants no tooltip on the greyed tab");
    var hintId = band.GetAttribute("aria-describedby");
    hintId.Should().NotBeNullOrEmpty();
    cut.Find($"#{hintId}").TextContent.Should().Be(VisualizerPanel.BandUnavailableHint);

    // Drive the handler directly: a disabled button's click never reaches it in a browser, so this
    // is the guard inside SelectMode, not the attribute.
    await cut.InvokeAsync(() => band.Click());

    var bandAfter = cut.FindAll(".visualizer-mode").First(b => b.TextContent.Trim() == "Radio");
    (bandAfter.GetAttribute("class") ?? string.Empty).Should().NotContain("is-active");
    var spectrumAfter = cut.FindAll(".visualizer-mode").First(b => b.TextContent.Trim() == "Spectrum");
    (spectrumAfter.GetAttribute("class") ?? string.Empty).Should().Contain("is-active");
  }

  [Fact]
  public void ModePicker_ClickingRing_ActivatesRingSegment()
  {
    var cut = RenderComponent<VisualizerPanel>();
    var ring = cut.FindAll(".visualizer-mode").First(b => b.TextContent.Trim() == "Ring");
    ring.Click();

    cut.WaitForAssertion(() =>
    {
      var ringAfter = cut.FindAll(".visualizer-mode").First(b => b.TextContent.Trim() == "Ring");
      (ringAfter.GetAttribute("class") ?? string.Empty).Should().Contain("is-active");
      ringAfter.GetAttribute("aria-selected").Should().Be("true");
    }, timeout: TimeSpan.FromSeconds(2));
  }

  [Fact]
  public void ModePicker_ClickingPhase_ActivatesPhaseSegment()
  {
    var cut = RenderComponent<VisualizerPanel>();
    var phase = cut.FindAll(".visualizer-mode").First(b => b.TextContent.Trim() == "Phase");
    phase.Click();

    cut.WaitForAssertion(() =>
    {
      var phaseAfter = cut.FindAll(".visualizer-mode").First(b => b.TextContent.Trim() == "Phase");
      (phaseAfter.GetAttribute("class") ?? string.Empty).Should().Contain("is-active");
      phaseAfter.GetAttribute("aria-selected").Should().Be("true");
    }, timeout: TimeSpan.FromSeconds(2));
  }

  [Fact]
  public void ModePicker_HasNoRemoteModeChannel()
  {
    // ENC-9. The replacement for the two ENC-9a tests that used to live here, and it asserts the
    // DECISION rather than merely the absence: visualiser mode is local to this circuit.
    //
    // ENC-7 took encoder index 2 for PRESETS, which removed VisualizationModeService's only writer,
    // so ModeChanged could never fire, the VisualizationModeChanged broadcast could never be sent,
    // and the subscription ENC-9a added was inert. All three layers are deleted. This test fails if
    // a remote channel is reintroduced without the writer that would justify it - which is how the
    // dead chain came back into existence the first time.
    var hubEvents = typeof(AudioStateHubService)
      .GetEvents(BindingFlags.Public | BindingFlags.Instance)
      .Select(e => e.Name)
      .ToList();

    // ⚠ Prove the instrument before trusting its silence. A NotContain against an empty list passes
    // for the wrong reason, so pin a surviving event first: if this reflection call ever stops seeing
    // the hub's events, THIS line fails rather than the one below quietly passing forever.
    hubEvents.Should().Contain("EncoderConfigStatusChanged");
    hubEvents.Should().NotContain("VisualizationModeChanged");

    // And the capability itself still works locally - see ModePicker_ClickingPhase_ActivatesPhaseSegment
    // above, which drives the five-segment picker and is what the owner actually uses.
    var cut = RenderComponent<VisualizerPanel>();
    cut.FindAll(".visualizer-mode").Count.Should().Be(5);
  }

  [Fact]
  public void ModePicker_RoleTablistAndAriaLabel_AreDeclared()
  {
    // ARIA contract for the picker container — preserved from PR 4.
    var cut = RenderComponent<VisualizerPanel>();
    var picker = cut.Find(".visualizer-mode-picker");
    picker.GetAttribute("role").Should().Be("tablist");
    picker.GetAttribute("aria-label").Should().Be("Visualizer mode");
  }
}

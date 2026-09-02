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
/// picker from a floating chip group into a full-width header. The
/// original spec referenced only three modes (VU / Wave / Spectrum)
/// because the spec author worked from a 3-mode screenshot, but the
/// underlying <c>VisualizationMode</c> enum, the JS visualizer module,
/// and the SignalR subscribe / unsubscribe paths were always wired for
/// six modes:
///
/// <list type="bullet">
///   <item>VU (VUMeter)</item>
///   <item>Wave (Waveform)</item>
///   <item>Spectrum (Spectrum)</item>
///   <item>Fall (Spectrogram)</item>
///   <item>Ring (Circular)</item>
///   <item>Phase (PhaseScope)</item>
/// </list>
///
/// These tests lock the contract that all six segments render in the
/// header and that clicking each segment activates that segment.
/// </summary>
public class VisualizerPanelTests : TestContext
{
  private readonly ILoggerFactory _loggerFactory;
  private readonly AudioStateHubService _stateHub;

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

    // ENC-9a: the panel now also subscribes to AudioStateHubService for out-of-band mode
    // changes. Kept as a field so a test can raise the event the server would have sent.
    _stateHub = new AudioStateHubService(
      NullLogger<AudioStateHubService>.Instance,
      configuration,
      transport: new OfflineHubTransport()
    );
    Services.AddSingleton(_stateHub);

    Services.AddSingleton<VisualizerTelemetryService>();
  }

  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      _loggerFactory?.Dispose();
      _stateHub?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
    base.Dispose(disposing);
  }

  [Fact]
  public void ModePicker_RendersExactlySixModeButtons()
  {
    // The picker exposes every mode the visualizer.js module supports.
    // A regression in PR 4 dropped Fall / Ring / Phase from the UI even
    // though the wiring (enum, JS calls, subscribe paths) was intact —
    // this test locks against that regression returning.
    var cut = RenderComponent<VisualizerPanel>();
    cut.FindAll(".visualizer-mode").Count.Should().Be(6);
  }

  [Fact]
  public void ModePicker_RendersAllSixLabelsInOrder()
  {
    // Visual order matches the handoff sequence (primary → secondary):
    // VU, Wave, Spectrum, Fall, Ring, Phase.
    var cut = RenderComponent<VisualizerPanel>();
    var labels = cut.FindAll(".visualizer-mode")
      .Select(e => e.TextContent.Trim())
      .ToList();
    labels.Should().Equal("VU", "Wave", "Spectrum", "Fall", "Ring", "Phase");
  }

  [Fact]
  public void ModePicker_DefaultsToVuModeActive()
  {
    // _currentMode defaults to VUMeter when no saved preference loads
    // (the config call fails in the test rig and is swallowed).
    var cut = RenderComponent<VisualizerPanel>();
    var buttons = cut.FindAll(".visualizer-mode").ToList();
    (buttons[0].GetAttribute("class") ?? string.Empty).Should().Contain("is-active");
    buttons[0].GetAttribute("aria-selected").Should().Be("true");

    foreach (var b in buttons.Skip(1))
    {
      (b.GetAttribute("class") ?? string.Empty).Should().NotContain("is-active");
      b.GetAttribute("aria-selected").Should().Be("false");
    }
  }

  [Fact]
  public void ModePicker_FallButton_HasSpectrogramAriaLabel()
  {
    // Fall is the user-facing label for VisualizationMode.Spectrogram.
    var cut = RenderComponent<VisualizerPanel>();
    var fall = cut.FindAll(".visualizer-mode").First(b => b.TextContent.Trim() == "Fall");
    (fall.GetAttribute("aria-label") ?? string.Empty).Should().ContainAll("Spectrogram", "mode");
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
  public void ModePicker_ClickingFall_ActivatesFallSegment()
  {
    // Clicking Fall flips _currentMode to Spectrogram. The is-active class
    // and aria-selected attribute should move from VU to Fall.
    var cut = RenderComponent<VisualizerPanel>();
    var fall = cut.FindAll(".visualizer-mode").First(b => b.TextContent.Trim() == "Fall");
    fall.Click();

    // SelectMode assigns _currentMode only after `await UnsubscribeFromCurrentMode()`,
    // so the re-render is not guaranteed to have happened when Click() returns. See
    // the class remarks for why that await's timing varies. Same assertions, waited
    // for rather than raced.
    cut.WaitForAssertion(() =>
    {
      var fallAfter = cut.FindAll(".visualizer-mode").First(b => b.TextContent.Trim() == "Fall");
      (fallAfter.GetAttribute("class") ?? string.Empty).Should().Contain("is-active");
      fallAfter.GetAttribute("aria-selected").Should().Be("true");

      var vuAfter = cut.FindAll(".visualizer-mode").First(b => b.TextContent.Trim() == "VU");
      (vuAfter.GetAttribute("class") ?? string.Empty).Should().NotContain("is-active");
      vuAfter.GetAttribute("aria-selected").Should().Be("false");
    }, timeout: TimeSpan.FromSeconds(2));
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
  public void ModePicker_RemoteModeChange_MovesTheActiveSegment()
  {
    // ENC-9a. The mode can change without this picker being what changed it — the encoder's
    // visualizer knob, another browser, an API call. The server has always broadcast
    // VisualizationModeChanged; before this, nothing consumed it, so the picker kept showing the
    // old segment until a page reload.
    var cut = RenderComponent<VisualizerPanel>();

    var vuBefore = cut.FindAll(".visualizer-mode").First(b => b.TextContent.Trim() == "VU");
    (vuBefore.GetAttribute("class") ?? string.Empty).Should().Contain("is-active");

    RaiseRemoteModeChange("Spectrum");

    cut.WaitForAssertion(() =>
    {
      var spectrum = cut.FindAll(".visualizer-mode").First(b => b.TextContent.Trim() == "SPECTRUM"
        || b.TextContent.Trim() == "Spectrum");
      (spectrum.GetAttribute("class") ?? string.Empty).Should().Contain("is-active");
      spectrum.GetAttribute("aria-selected").Should().Be("true");

      var vuAfter = cut.FindAll(".visualizer-mode").First(b => b.TextContent.Trim() == "VU");
      (vuAfter.GetAttribute("class") ?? string.Empty).Should().NotContain("is-active");
    }, timeout: TimeSpan.FromSeconds(2));
  }

  [Fact]
  public void ModePicker_RemoteModeChange_WithUnrecognisedMode_LeavesSelectionAlone()
  {
    // The API models modes as strings; this component declares its own enum. If those drift, the
    // safe failure is to keep showing the mode that is actually running rather than to guess.
    var cut = RenderComponent<VisualizerPanel>();

    RaiseRemoteModeChange("NotARealMode");

    var vu = cut.FindAll(".visualizer-mode").First(b => b.TextContent.Trim() == "VU");
    (vu.GetAttribute("class") ?? string.Empty).Should().Contain("is-active");
  }

  /// <summary>Raises the event the server's VisualizationModeChanged broadcast would raise.</summary>
  private void RaiseRemoteModeChange(string mode)
  {
    var handler = typeof(AudioStateHubService)
      .GetField("VisualizationModeChanged", BindingFlags.Instance | BindingFlags.NonPublic)?
      .GetValue(_stateHub) as Func<VisualizationModeDto, Task>;

    handler.Should().NotBeNull("the panel should have subscribed to VisualizationModeChanged");
    handler!.Invoke(new VisualizationModeDto { Mode = mode, IsEnabled = true })
      .GetAwaiter().GetResult();
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

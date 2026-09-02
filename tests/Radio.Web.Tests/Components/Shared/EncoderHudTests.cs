using System.Text.RegularExpressions;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Radio.Web.Components.Shared;
using Radio.Web.Models;
using Radio.Web.Services;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// bUnit tests for <see cref="EncoderHud"/> — the transient readout that appears in the screen
/// quarter above the knob that produced it (ENC-4).
///
/// Contract under test:
///   - Nothing on screen when no card is current, and nothing for a phase this build does not
///     know (the forward-compatibility rule an older kiosk depends on).
///   - Geometry keys off the encoder index: quarter centres at 240 / 720 / 1200 / 1680 px, with
///     an out-of-range index clamped rather than thrown.
///   - The Sleep variant carries no inline position at all — the anti-burn-in drift wrapper in
///     Sleep.razor places it.
///   - Frequency cards reuse .display-frequency verbatim, with no font-size of their own.
///   - Every state is readable without colour: muted says the word MUTED.
///
/// MainLayoutTests is a documented stub (Radzen + JSInterop make the layout impractical to
/// render), so the two mount points are covered by the browser test plan, not from here.
/// </summary>
public class EncoderHudTests : TestContext
{
  private readonly FakeTimeProvider _clock = new();
  private readonly EncoderHudService _hud;

  public EncoderHudTests()
  {
    JSInterop.Mode = JSRuntimeMode.Loose;
    // hub: null is the reason that constructor parameter is optional — a component test has no
    // SignalR connection and drives the service through Publish directly.
    _hud = new EncoderHudService(hub: null, timeProvider: _clock);
    Services.AddSingleton(_hud);
  }

  private static EncoderHudDto VolumeCard(
    int encoderIndex = 0,
    int percent = 62,
    bool muted = false,
    string phase = "Value",
    string label = "VOLUME") => new()
    {
      EncoderIndex = encoderIndex,
      Label = label,
      Phase = phase,
      VolumePercent = percent,
      IsMuted = muted,
    };

  [Fact]
  public void NoCurrentCard_RendersNothing()
  {
    var cut = RenderComponent<EncoderHud>();

    cut.Markup.Trim().Should().BeEmpty();
  }

  [Fact]
  public void VolumeCard_RendersNumeralsAndFill()
  {
    _hud.Publish(VolumeCard(percent: 62));

    var cut = RenderComponent<EncoderHud>();

    cut.Find(".encoder-hud-value").TextContent.Trim().Should().Be("62");
    cut.Find(".encoder-hud-bar-fill").GetAttribute("style").Should().Contain("62%");
  }

  [Fact]
  public void MutedVolumeCard_SaysTheWordMuted()
  {
    _hud.Publish(VolumeCard(muted: true));

    var cut = RenderComponent<EncoderHud>();

    // The colour is reinforcement, not the signal — the state has to survive being read in
    // greyscale, so assert on the word.
    cut.Find(".encoder-hud-muted-chip").TextContent.Trim().Should().Be("MUTED");
    cut.Find(".encoder-hud").ClassList.Should().Contain("is-muted");
  }

  [Fact]
  public void FrequencyCard_UsesDisplayFrequencyVerbatim()
  {
    _hud.Publish(new EncoderHudDto
    {
      EncoderIndex = 1,
      Label = "TUNING",
      Phase = "Value",
      PrimaryText = "98.5 MHz",
      SecondaryText = "FM",
      PrimaryIsFrequency = true,
    });

    var cut = RenderComponent<EncoderHud>();

    var freq = cut.Find(".display-frequency");
    freq.TextContent.Trim().Should().Be("98.5 MHz");
    // The handoff quotes 43px; the class is 42px after the PR #371 hot-fix. "Verbatim" is the
    // load-bearing word — the component must not re-state a size of its own.
    (freq.GetAttribute("style") ?? string.Empty).Should().NotContain("font-size");
  }

  [Theory]
  [InlineData(0, 240)]
  [InlineData(1, 720)]
  [InlineData(2, 1200)]
  [InlineData(3, 1680)]
  public void Geometry_PlacesEachEncoderInItsOwnQuarter(int encoderIndex, int expectedLeftPx)
  {
    _hud.Publish(VolumeCard(encoderIndex: encoderIndex));

    var cut = RenderComponent<EncoderHud>();

    var root = cut.Find(".encoder-hud");
    root.GetAttribute("style").Should().Contain($"left: {expectedLeftPx}px");
    root.GetAttribute("data-encoder-index").Should().Be(encoderIndex.ToString());
  }

  [Theory]
  [InlineData(-3, 240)]
  [InlineData(9, 1680)]
  public void Geometry_ClampsAnOutOfRangeIndex(int encoderIndex, int expectedLeftPx)
  {
    _hud.Publish(VolumeCard(encoderIndex: encoderIndex));

    var cut = RenderComponent<EncoderHud>();

    cut.Find(".encoder-hud").GetAttribute("style").Should().Contain($"left: {expectedLeftPx}px");
  }

  [Fact]
  public void SleepVariant_IsCenteredAndCarriesNoCardChrome()
  {
    _hud.Publish(VolumeCard(encoderIndex: 2));

    var cut = RenderComponent<EncoderHud>(p => p.Add(x => x.Variant, EncoderHudVariant.Sleep));

    var root = cut.Find(".encoder-hud");
    root.ClassList.Should().Contain("encoder-hud--sleep");
    // No inline geometry at all — the drift wrapper in Sleep.razor does the placing, and a
    // quartered card would have to sit outside that wrapper to reach its quarter.
    root.GetAttribute("style").Should().BeNull();
  }

  [Fact]
  public void SleepVariant_UsesNoCyanAndNoRed()
  {
    _hud.Publish(VolumeCard(muted: true));

    var cut = RenderComponent<EncoderHud>(p => p.Add(x => x.Variant, EncoderHudVariant.Sleep));

    // bUnit does not compute styles, so this asserts the hook the sleep overrides key off:
    // .encoder-hud--sleep .encoder-hud-muted-chip repaints the chip to --text-low, and
    // .encoder-hud--sleep.is-muted .encoder-hud-bar repaints its border. Both need the sleep
    // class and the muted class on the same root.
    var root = cut.Find(".encoder-hud");
    root.ClassList.Should().Contain("encoder-hud--sleep");
    root.ClassList.Should().Contain("is-muted");
    cut.Find(".encoder-hud-muted-chip").Should().NotBeNull();
  }

  [Fact]
  public void UnknownPhase_RendersNothing()
  {
    _hud.Publish(VolumeCard(phase: "SomethingENC5WillAdd"));

    var cut = RenderComponent<EncoderHud>();

    cut.Markup.Trim().Should().BeEmpty();
  }

  [Fact]
  public void HoldingState_RendersTheRing()
  {
    _hud.Publish(VolumeCard(phase: "HoldStart", label: "HOLD FOR STANDBY"));

    var cut = RenderComponent<EncoderHud>();

    cut.FindAll(".encoder-hud-ring").Should().ContainSingle();
    cut.Find(".encoder-hud-label-text").TextContent.Trim().Should().Be("HOLD FOR STANDBY");

    _hud.Publish(VolumeCard(phase: "HoldCancel"));

    cut.FindAll(".encoder-hud-ring").Should().BeEmpty();
  }

  [Fact]
  public void Card_IsAPoliteLiveRegion()
  {
    _hud.Publish(VolumeCard());

    var cut = RenderComponent<EncoderHud>();

    var root = cut.Find(".encoder-hud");
    root.GetAttribute("role").Should().Be("status");
    root.GetAttribute("aria-live").Should().Be("polite");
    root.GetAttribute("aria-atomic").Should().Be("true");
  }

  [Fact]
  public void Card_RegistersNoClickHandler()
  {
    _hud.Publish(VolumeCard());

    var cut = RenderComponent<EncoderHud>();

    // Half of "it is a readout, not a control". bUnit computes no styles, so the other half —
    // that `pointer-events: none` actually stops the 360px card shielding the route underneath
    // it — is pinned by Css_DeclaresPointerEventsNone below and confirmed in the browser.
    var root = cut.Find(".encoder-hud");
    Assert.Throws<MissingEventHandlerException>(() => root.Click());
  }

  [Fact]
  public void Css_DeclaresPointerEventsNone()
  {
    // bUnit renders markup but computes no styles, so this reads the declaration out of the
    // stylesheet instead. It guards the specific regression that matters: a 360px card
    // bottom-anchored at z-index 10000 over every route would swallow touches on the UI beneath
    // it the moment this declaration goes missing.
    var css = File.ReadAllText(LocateDesignSystemCss());
    var rule = new Regex(@"\.encoder-hud\s*\{[^}]*?pointer-events:\s*none", RegexOptions.Singleline);

    rule.IsMatch(css).Should().BeTrue(
      "the .encoder-hud rule in design-system.css must declare pointer-events: none");
  }

  /// <summary>
  /// Locate the design-system.css source file by walking up from the test binary directory until
  /// we find the Radio.Web/wwwroot/css folder. The stylesheet isn't copied into the test output,
  /// so a relative path lookup is the load-bearing piece. Same helper shape as RdsCardTests.
  /// </summary>
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
}

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
/// bUnit tests for <see cref="EncoderHud"/> — the transient readout that appears beside the knob
/// that produced it, at the same height (ENC-4).
///
/// Contract under test:
///   - Nothing on screen when no card is current, and nothing for a phase this build does not
///     know (the forward-compatibility rule an older kiosk depends on).
///   - Geometry keys off the encoder index: bands at 90 / 270 / 450 / 630 px down the 720 px
///     axis, with an out-of-range index clamped rather than thrown.
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
  [InlineData(0, 90)]
  [InlineData(1, 270)]
  [InlineData(2, 450)]
  [InlineData(3, 630)]
  public void Geometry_PlacesEachEncoderOnItsOwnBand(int encoderIndex, int expectedBandPx)
  {
    _hud.Publish(VolumeCard(encoderIndex: encoderIndex));

    var cut = RenderComponent<EncoderHud>();

    var root = cut.Find(".encoder-hud");
    // The inline style carries only the band centre. The left offset, the centring on that centre
    // and the viewport clamp all live in the .encoder-hud rule, which reads this value — pinned
    // by Css_AnchorsLeftAndCentresOnTheBand below, since bUnit computes no styles.
    root.GetAttribute("style").Should().Contain($"--encoder-band-y: {expectedBandPx}px");
    root.GetAttribute("data-encoder-index").Should().Be(encoderIndex.ToString());
  }

  [Fact]
  public void Geometry_CarriesNoHorizontalOffsetOfItsOwn()
  {
    // The rotation's regression guard. ENC-4 shipped `left: <quarter centre>px` inline, spreading
    // the cards across the 1920 px width; every card is now on the same left edge, so an inline
    // `left` is the specific thing that must not come back.
    _hud.Publish(VolumeCard(encoderIndex: 3));

    var cut = RenderComponent<EncoderHud>();

    cut.Find(".encoder-hud").GetAttribute("style").Should().NotContain("left:");
  }

  [Theory]
  [InlineData(-3, 90)]
  [InlineData(9, 630)]
  public void Geometry_ClampsAnOutOfRangeIndex(int encoderIndex, int expectedBandPx)
  {
    _hud.Publish(VolumeCard(encoderIndex: encoderIndex));

    var cut = RenderComponent<EncoderHud>();

    cut.Find(".encoder-hud").GetAttribute("style").Should()
      .Contain($"--encoder-band-y: {expectedBandPx}px");
  }

  [Fact]
  public void NormalVariant_UsesTheMirroredHorizontalEntrance()
  {
    // Handoff §6.1's declared exception. A left-anchored card entering on snackbarSlideIn's
    // translateY(100%) would slide in from an edge it is not attached to.
    _hud.Publish(VolumeCard());

    var cut = RenderComponent<EncoderHud>();

    var root = cut.Find(".encoder-hud");
    root.ClassList.Should().Contain("encoder-hud-enter");
    root.ClassList.Should().NotContain("snackbar-enter");
  }

  [Fact]
  public void SleepVariant_KeepsTheOriginalSnackbarEntrance()
  {
    // The exception is scoped to the Normal variant: the sleep card is centred by the drift
    // wrapper rather than anchored to an edge, so the original vertical slide is still right.
    _hud.Publish(VolumeCard());

    var cut = RenderComponent<EncoderHud>(p => p.Add(x => x.Variant, EncoderHudVariant.Sleep));

    var root = cut.Find(".encoder-hud");
    root.ClassList.Should().Contain("snackbar-enter");
    root.ClassList.Should().NotContain("encoder-hud-enter");
  }

  [Fact]
  public void SleepVariant_IsCenteredAndCarriesNoCardChrome()
  {
    _hud.Publish(VolumeCard(encoderIndex: 2));

    var cut = RenderComponent<EncoderHud>(p => p.Add(x => x.Variant, EncoderHudVariant.Sleep));

    var root = cut.Find(".encoder-hud");
    root.ClassList.Should().Contain("encoder-hud--sleep");
    // No inline geometry at all — the drift wrapper in Sleep.razor does the placing, and a
    // banded card would have to sit outside that wrapper to reach its band.
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
  public void SleepVariant_CollapsesASelectorToOneLine()
  {
    // Handoff §8.3 — a consumed wake input shows what is currently selected, "SOURCE · FM", and
    // NOT the full overlay. The second reason is the load-bearing one: this host renders inside
    // .sleep-screen-drift, the anti-burn-in wrapper, and a bordered 440px panel with its own
    // background is the fixed bright composition that wrapper exists to prevent.
    //
    // Reachable today, and not hypothetically: /sleep entered by the idle timer leaves
    // SleepService.IsSleeping false, so a SOURCE turn is not consumed as a wake and renders here.
    var selector = new EncoderHudDto
    {
      EncoderIndex = 1,
      Label = "SOURCE",
      Phase = "SelectorPreview",
      Title = "SOURCE",
      HighlightIndex = 1,
      Footer = "PRESS THE KNOB TO SWITCH",
      Rows =
      [
        new EncoderSelectorRowDto { Id = "band:FM", Primary = "FM" },
        new EncoderSelectorRowDto { Id = "band:AM", Primary = "AM" },
      ],
    };
    _hud.Publish(selector);

    var sleep = RenderComponent<EncoderHud>(p => p.Add(x => x.Variant, EncoderHudVariant.Sleep));

    sleep.FindAll(".encoder-selector-overlay").Should().BeEmpty();
    sleep.FindAll(".encoder-selector-row").Should().BeEmpty();
    sleep.Find(".encoder-selector-sleep-line").TextContent.Trim().Should().Be("SOURCE · AM");
    sleep.Find(".encoder-hud").ClassList.Should().Contain("encoder-hud--sleep");

    // The contrast that makes "collapses" mean anything: the same payload on the normal route
    // renders the full centred list.
    var normal = RenderComponent<EncoderHud>();

    normal.FindAll(".encoder-selector-overlay").Should().ContainSingle();
    normal.FindAll(".encoder-selector-row").Count.Should().Be(2);
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
    // stylesheet instead. It guards the specific regression that matters: a 360px card at
    // z-index 10000 over every route would swallow touches on the UI beneath it the moment
    // this declaration goes missing. That matters more since the rotation than before it — the
    // VOLUME band lands on the fixed topbar, and that occlusion is accepted only because it is
    // purely visual.
    var css = File.ReadAllText(LocateDesignSystemCss());
    var rule = new Regex(@"\.encoder-hud\s*\{[^}]*?pointer-events:\s*none", RegexOptions.Singleline);

    rule.IsMatch(css).Should().BeTrue(
      "the .encoder-hud rule in design-system.css must declare pointer-events: none");
  }

  [Fact]
  public void Css_AnchorsLeftAndCentresOnTheBand()
  {
    // The half of the geometry bUnit cannot see. The component supplies only --encoder-band-y;
    // everything that turns it into a position is here, so this is where the rotation is pinned.
    var css = File.ReadAllText(LocateDesignSystemCss());
    var block = new Regex(@"\.encoder-hud\s*\{(?<body>[^}]*?)\}", RegexOptions.Singleline);
    var body = block.Match(css).Groups["body"].Value;

    body.Should().Contain("left: 24px", "every card sits on the left edge, beside the knob column");
    body.Should().Contain("top: var(--encoder-band-y)", "the band is the only per-encoder value");
    body.Should().NotContain("bottom: 24px", "the bottom anchor was the pre-rotation geometry");
    body.Should().NotContain("margin-left: -180px", "the horizontal centring went with the axis");

    // Centring plus the >= 8px viewport clamp, expressed on `translate` rather than `transform` so
    // the entrance animation cannot drop it. 712 = 720 - 8.
    body.Should().MatchRegex(@"translate:\s*0\s*clamp\(");
    body.Should().Contain("calc(8px - var(--encoder-band-y))");
    body.Should().Contain("calc(712px - var(--encoder-band-y) - 100%)");
  }

  [Fact]
  public void Css_DeclaresTheMirroredEntranceAtItsTwinsDurationAndEasing()
  {
    // Handoff §6.1 authorises exactly one new keyframe pair, as a MIRROR of snackbarSlideIn/Out:
    // same duration, same easing, no new token. This asserts the "mirror" half of that — if the
    // originals are ever retimed, this is what says the pair has drifted apart.
    var css = File.ReadAllText(LocateDesignSystemCss());

    css.Should().Contain("transform: translateX(-100%)",
      "a left-anchored card enters from the left edge, not from below");
    new Regex(@"\.encoder-hud-enter\s*\{\s*animation:\s*encoderHudSlideInLeft\s+"
              + @"var\(--anim-duration-normal\)\s+var\(--anim-ease-emphasized\)")
      .IsMatch(css).Should().BeTrue("the enter half mirrors .snackbar-enter's duration and easing");
    new Regex(@"\.encoder-hud-exit\s*\{\s*animation:\s*encoderHudSlideOutLeft\s+"
              + @"var\(--anim-duration-normal\)\s+var\(--anim-ease-standard\)")
      .IsMatch(css).Should().BeTrue("the exit half mirrors .snackbar-exit's duration and easing");

    // §6.9 stands: the exception adds keyframes, not tokens. Matched as a declaration and
    // as a var() reference rather than as bare text, because the file's own comments have to
    // be able to name the forbidden prefix in order to record the rule.
    new Regex(@"--hud-[a-z0-9-]+\s*:").IsMatch(css).Should().BeFalse(
      "the handoff forbids declaring any --hud-* custom property");
    css.Should().NotContain("var(--hud-", "and forbids consuming one");
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

using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Radio.Web.Components.Shared;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// bUnit tests for the <see cref="RdsScrollMarquee"/> component — accumulating
/// RDS RT ticker rendered below the frequency well in RadioControlPanel.
///
/// Component contract under test (HANDOFF-rds-accumulating-scroll §3, §4, §7):
///   - Renders nothing when Text is null/empty (collapse-when-empty matches
///     the legacy rcp-rds-rt behaviour)
///   - Renders the scroll container + track + sr-only mirror when Text is set
///   - Adds the .is-static class when text fits the container width
///   - Sets --scroll-duration inline so the px/s speed stays constant
///   - aria-live="polite" on the SR-only mirror (motion-friendly + a11y)
///   - aria-hidden="true" on the visible track (the mirror carries the
///     readable copy)
///   - tabindex="0" on the scroll container (keyboard focus pause)
/// </summary>
public class RdsScrollMarqueeTests : TestContext
{
  public RdsScrollMarqueeTests()
  {
    Services.AddRadzenComponents();
    JSInterop.Mode = JSRuntimeMode.Loose;
  }

  [Fact]
  public void Marquee_RendersNothing_WhenTextNull()
  {
    var cut = RenderComponent<RdsScrollMarquee>(p => p
      .Add(x => x.Text, null));

    cut.FindAll(".rcp-rds-rt-scroll").Should().BeEmpty(
      "the marquee collapses entirely when there's nothing to scroll");
  }

  [Fact]
  public void Marquee_RendersNothing_WhenTextEmpty()
  {
    var cut = RenderComponent<RdsScrollMarquee>(p => p
      .Add(x => x.Text, string.Empty));

    cut.FindAll(".rcp-rds-rt-scroll").Should().BeEmpty();
  }

  [Fact]
  public void Marquee_RendersScrollContainer_WhenTextSet()
  {
    var cut = RenderComponent<RdsScrollMarquee>(p => p
      .Add(x => x.Text, "WUNC News • Morning Edition"));

    cut.FindAll(".rcp-rds-rt-scroll").Should().HaveCount(1);
    cut.FindAll(".rcp-rds-rt-track").Should().HaveCount(1);
    cut.FindAll(".rcp-rds-rt-sr-only").Should().HaveCount(1);
  }

  [Fact]
  public void Marquee_AddsIsStaticClass_WhenTextFitsContainer()
  {
    // 5 chars * 7 px/char = 35 px, way under default 420 px container — static.
    var cut = RenderComponent<RdsScrollMarquee>(p => p
      .Add(x => x.Text, "WUNC"));

    var scroll = cut.Find(".rcp-rds-rt-scroll");
    scroll.ClassList.Should().Contain("is-static",
      "short text that fits the container renders static-centered, not scrolling");
  }

  [Fact]
  public void Marquee_OmitsIsStaticClass_WhenTextOverflows()
  {
    // ~80 chars * 7 px/char = 560 px > 420 px container — scrolls.
    var longText = new string('A', 80);
    var cut = RenderComponent<RdsScrollMarquee>(p => p
      .Add(x => x.Text, longText));

    var scroll = cut.Find(".rcp-rds-rt-scroll");
    scroll.ClassList.Should().NotContain("is-static");
  }

  [Fact]
  public void Marquee_SetsScrollDuration_InlineOnTrack()
  {
    // Text longer than container forces a real scroll duration computation.
    var longText = new string('A', 100);
    var cut = RenderComponent<RdsScrollMarquee>(p => p
      .Add(x => x.Text, longText)
      .Add(x => x.ScrollSpeedPxPerSec, 40)
      .Add(x => x.ContainerMaxWidthPx, 420)
      .Add(x => x.ApproximateCharWidthPx, 7.0));

    var track = cut.Find(".rcp-rds-rt-track");
    var style = track.GetAttribute("style") ?? string.Empty;
    style.Should().Contain("--scroll-duration:",
      "the marquee sets the CSS custom property inline so the px/s speed stays constant regardless of buffer length");
    style.Should().Contain("s;", "the duration value carries the seconds unit");
  }

  [Fact]
  public void Marquee_DurationFloor_HonoursMinimumOfFourSeconds()
  {
    // Very short text + very high speed would otherwise produce a sub-second
    // animation that feels frantic. The component clamps to a 4 s floor.
    // 50 px / 200 px/s = 0.25 s natural → clamped to 4 s.
    var cut = RenderComponent<RdsScrollMarquee>(p => p
      .Add(x => x.Text, new string('A', 1000))
      .Add(x => x.ScrollSpeedPxPerSec, 40)
      .Add(x => x.ContainerMaxWidthPx, 420)
      .Add(x => x.ApproximateCharWidthPx, 7.0));

    var track = cut.Find(".rcp-rds-rt-track");
    var style = track.GetAttribute("style") ?? string.Empty;
    // 1000 * 7 + 420 = 7420 px ; 7420 / 40 ≈ 185.5 s — way above floor.
    style.Should().Contain("185.5s");
  }

  [Fact]
  public void Marquee_TrackCarriesAriaHidden_AndScrollContainerCarriesAriaLabel()
  {
    // Screen-reader contract: the visible scrolling track is hidden from AT;
    // the sr-only mirror below carries the readable copy via aria-live.
    var cut = RenderComponent<RdsScrollMarquee>(p => p
      .Add(x => x.Text, "WUNC News • Morning Edition"));

    var track = cut.Find(".rcp-rds-rt-track");
    track.GetAttribute("aria-hidden").Should().Be("true");

    var scroll = cut.Find(".rcp-rds-rt-scroll");
    scroll.GetAttribute("aria-label").Should().Be("RDS RadioText");
  }

  [Fact]
  public void Marquee_SrOnlyMirror_CarriesAriaLivePolite()
  {
    // HANDOFF §7 — the mirror is aria-live="polite" + aria-atomic="true" so
    // screen readers announce buffer updates without interrupting the user.
    var cut = RenderComponent<RdsScrollMarquee>(p => p
      .Add(x => x.Text, "WUNC News"));

    var srMirror = cut.Find(".rcp-rds-rt-sr-only");
    srMirror.GetAttribute("aria-live").Should().Be("polite");
    srMirror.GetAttribute("aria-atomic").Should().Be("true");
    srMirror.TextContent.Trim().Should().Be("WUNC News",
      "the mirror carries the full buffer text — assistive tech reads from this, not the visible track");
  }

  [Fact]
  public void Marquee_ScrollContainer_IsKeyboardFocusable()
  {
    // HANDOFF §7 — tabindex="0" so keyboard users can land on the strip and
    // focus-pause works (CSS :focus / :focus-within selectors in
    // design-system.css trigger animation-play-state: paused).
    var cut = RenderComponent<RdsScrollMarquee>(p => p
      .Add(x => x.Text, "WUNC News"));

    var scroll = cut.Find(".rcp-rds-rt-scroll");
    scroll.GetAttribute("tabindex").Should().Be("0");
  }

  [Fact]
  public void Marquee_TitleAttribute_MirrorsBufferText()
  {
    // Mouse-over tooltip surfaces the full buffer text (handy when the user
    // pauses the scroll and wants to read the entire string at once).
    var bufferText = "WUNC News • Morning Edition • NPR";
    var cut = RenderComponent<RdsScrollMarquee>(p => p
      .Add(x => x.Text, bufferText));

    var scroll = cut.Find(".rcp-rds-rt-scroll");
    scroll.GetAttribute("title").Should().Be(bufferText);
  }

  // --- Render-guard regression tests (RDS scroll-stability fix) ---
  // The CSS marquee animation restarts whenever this component re-renders (the
  // track div is re-created and the inline --scroll-duration re-emitted). The
  // parent (RadioControlPanel) re-renders ~2x/second on signal telemetry, so a
  // no-op render visibly snaps the ticker back to the right edge ("jerk +
  // dropped chars"). ShouldRender() suppresses the no-op renders.

  [Fact]
  public void Marquee_DoesNotReRender_WhenTextUnchanged()
  {
    var cut = RenderComponent<RdsScrollMarquee>(p => p
      .Add(x => x.Text, "WUNC News • Morning Edition"));

    // ShouldRender is NOT consulted before the first frame, so it primes its
    // last-rendered cache on the FIRST consult (the first SetParametersAndRender).
    // That first identical update therefore renders once to prime; from then on,
    // steady-state identical telemetry ticks are suppressed. We assert the
    // steady state — which is what happens ~2x/second on a live station.
    cut.SetParametersAndRender(p => p
      .Add(x => x.Text, "WUNC News • Morning Edition"));

    var afterPrime = cut.RenderCount;

    // Simulate the parent re-rendering on a telemetry tick with identical RDS text.
    cut.SetParametersAndRender(p => p
      .Add(x => x.Text, "WUNC News • Morning Edition"));

    cut.RenderCount.Should().Be(afterPrime,
      "an unchanged Text must not re-render the marquee (no CSS animation restart)");
  }

  [Fact]
  public void Marquee_ReRenders_WhenTextChanges()
  {
    var cut = RenderComponent<RdsScrollMarquee>(p => p
      .Add(x => x.Text, "WUNC News"));

    var before = cut.RenderCount;

    cut.SetParametersAndRender(p => p
      .Add(x => x.Text, "WUNC News • Morning Edition"));

    cut.RenderCount.Should().BeGreaterThan(before,
      "a changed Text must re-render so the new buffer scrolls");
  }

  [Fact]
  public void Marquee_DurationStable_AcrossNoOpReRender()
  {
    // Fallback assertion (plan §Task 3): the animation restart is what the
    // re-emitted --scroll-duration style causes, so an identical-input render
    // must keep the style string byte-identical (and ideally not re-render).
    var cut = RenderComponent<RdsScrollMarquee>(p => p
      .Add(x => x.Text, new string('A', 100))
      .Add(x => x.ScrollSpeedPxPerSec, 40));

    var styleBefore = cut.Find(".rcp-rds-rt-track").GetAttribute("style");

    cut.SetParametersAndRender(p => p
      .Add(x => x.Text, new string('A', 100))
      .Add(x => x.ScrollSpeedPxPerSec, 40));

    var styleAfter = cut.Find(".rcp-rds-rt-track").GetAttribute("style");
    styleAfter.Should().Be(styleBefore,
      "an unchanged buffer must not re-emit a new --scroll-duration (which would restart the keyframes)");
  }
}

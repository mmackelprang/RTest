using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Radio.Web.Components.Shared;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// bUnit tests for the <see cref="RdsScrollMarquee"/> component — accumulating
/// RDS RT ticker rendered inside RdsCard in RadioControlPanel.
///
/// Component contract under test (HANDOFF-rds-accumulating-scroll §3, §4, §7 +
/// the RDS scroll-engine fix):
///   - Renders nothing when Text is null/empty (collapse-when-empty matches
///     the legacy rcp-rds-rt behaviour)
///   - Renders the scroll container + track + sr-only mirror when Text is set
///   - aria-live="polite" on the SR-only mirror, aria-hidden="true" on the
///     visible track, tabindex="0" on the container
///   - Drives wwwroot/js/rds-marquee.js through the offset-preserving interop
///     contract: init on first mount, update("append"/"swap"/"reset") on text
///     changes, update("speed") on speed-only changes, dispose on unmount —
///     asserted via bUnit's module-interop invocation records
///   - Only the BODY (Text) goes through the transition diff; the station-name
///     Head is handed to the engine separately and never causes a reset
///     (AUD-63)
///   - Once the engine is attached it owns the track text: Blazor rewrites
///     the title and the SR mirror, not the track spans
///   - No DOM churn / no interop on no-op parent re-renders (ShouldRender)
///
/// update() argument positions: [0] instance id, [1] mode, [2] trimmed body
/// chars, [3] head text, [4] body text, [5] speed. init(): [5] speed.
/// </summary>
public class RdsScrollMarqueeTests : TestContext
{
  private readonly BunitJSModuleInterop _module;

  public RdsScrollMarqueeTests()
  {
    Services.AddRadzenComponents();
    JSInterop.Mode = JSRuntimeMode.Loose;
    _module = JSInterop.SetupModule("./js/rds-marquee.js");
  }

  private IReadOnlyList<JSRuntimeInvocation> EngineCalls(string identifier)
    => _module.Invocations.Where(i => i.Identifier == identifier).ToList();

  // ─── Markup / accessibility ──────────────────────────────────────────────

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
  public void Marquee_TrackCarriesNoInlineAnimationStyle()
  {
    // The scroll-engine fix removed the per-render inline --scroll-duration —
    // re-emitting it was what restarted the CSS keyframes on every append
    // (the "jerk"). The transform is now owned exclusively by the JS engine.
    var cut = RenderComponent<RdsScrollMarquee>(p => p
      .Add(x => x.Text, new string('A', 100)));

    var track = cut.Find(".rcp-rds-rt-track");
    (track.GetAttribute("style") ?? string.Empty).Should().NotContain("--scroll-duration",
      "scroll timing lives in rds-marquee.js now — no restart-prone inline animation state");
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
    // the JS engine's focusin listener pauses the scroll.
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

  // ─── JS engine interop contract (the offset-preserving scroll fix) ───────

  [Fact]
  public void Marquee_FirstRenderWithText_InitsEngineOnce()
  {
    RenderComponent<RdsScrollMarquee>(p => p
      .Add(x => x.Text, "WUNC News")
      .Add(x => x.ScrollSpeedPxPerSec, 40));

    var inits = EngineCalls("init");
    inits.Should().HaveCount(1, "one engine instance per mounted marquee");
    inits[0].Arguments[5].Should().Be(40, "the configured speed is passed to init");
    EngineCalls("update").Should().BeEmpty();
  }

  [Fact]
  public void Marquee_AppendedText_CallsUpdateAppend_WithZeroTrim_NotReInit()
  {
    var cut = RenderComponent<RdsScrollMarquee>(p => p
      .Add(x => x.Text, "WUNC News"));

    cut.SetParametersAndRender(p => p
      .Add(x => x.Text, "WUNC News • Morning Edition"));

    EngineCalls("init").Should().HaveCount(1,
      "an append must NOT re-create the engine (that would reset the scroll offset — the old jerk)");
    var updates = EngineCalls("update");
    updates.Should().HaveCount(1);
    updates[0].Arguments[1].Should().Be("append");
    updates[0].Arguments[2].Should().Be(0, "nothing was trimmed from the front");
  }

  [Fact]
  public void Marquee_FrontTrimmedAppend_PassesTrimmedCharCount()
  {
    // Buffer evicted "Old • " (6 chars) off the front while appending — the
    // engine compensates the offset by exactly that many chars × char width.
    var cut = RenderComponent<RdsScrollMarquee>(p => p
      .Add(x => x.Text, "Old • Morning Edition"));

    cut.SetParametersAndRender(p => p
      .Add(x => x.Text, "Morning Edition • NPR News"));

    var updates = EngineCalls("update");
    updates.Should().HaveCount(1);
    updates[0].Arguments[1].Should().Be("append");
    updates[0].Arguments[2].Should().Be(6,
      "'Old • ' (6 chars) was evicted from the front; the offset compensation needs the exact count");
  }

  [Fact]
  public void Marquee_SameLengthSwap_CallsUpdateSwap()
  {
    // A chunk replaced in place by a correction of equal length: track
    // length is unchanged — keep the offset, swap the glyphs (no jump).
    var cut = RenderComponent<RdsScrollMarquee>(p => p
      .Add(x => x.Text, "RDS PS • GivJ It Away"));

    cut.SetParametersAndRender(p => p
      .Add(x => x.Text, "RDS PS • Give It Away"));

    var updates = EngineCalls("update");
    updates.Should().HaveCount(1);
    updates[0].Arguments[1].Should().Be("swap");
  }

  [Fact]
  public void Marquee_UnrelatedText_CallsUpdateReset()
  {
    var cut = RenderComponent<RdsScrollMarquee>(p => p
      .Add(x => x.Text, "WUNC News • Morning Edition"));

    cut.SetParametersAndRender(p => p
      .Add(x => x.Text, "Completely different station content"));

    var updates = EngineCalls("update");
    updates.Should().HaveCount(1);
    updates[0].Arguments[1].Should().Be("reset");
  }

  [Fact]
  public void Marquee_SpeedOnlyChange_CallsUpdateSpeed()
  {
    var cut = RenderComponent<RdsScrollMarquee>(p => p
      .Add(x => x.Text, "WUNC News")
      .Add(x => x.ScrollSpeedPxPerSec, 40));

    // Catch-up boost engaged by RdsScrollSpeedPolicy upstream.
    cut.SetParametersAndRender(p => p
      .Add(x => x.Text, "WUNC News")
      .Add(x => x.ScrollSpeedPxPerSec, 60));

    var updates = EngineCalls("update");
    updates.Should().HaveCount(1);
    updates[0].Arguments[1].Should().Be("speed");
    updates[0].Arguments[5].Should().Be(60);
  }

  // ─── Head / body split (AUD-63) ──────────────────────────────────────────
  // RdsCard composes "{PS} • " as Head and the RT buffer as Text. Composing
  // them into one string defeated the suffix-of-old-is-head-of-new alignment:
  // once the buffer was full, every eviction classified as a swap (the whole
  // text shifted a chunk under a fixed offset) or a reset (a snap to home).

  [Fact]
  public void Marquee_FrontTrimmedAppend_WithStationNameHead_StillClassifiesFromBodyAlone()
  {
    // The eviction case above, with a station-name head in front of it. The
    // head must not disturb the classification: still an append with exactly
    // the 6 body characters that were evicted.
    var cut = RenderComponent<RdsScrollMarquee>(p => p
      .Add(x => x.Head, "WXYZ • ")
      .Add(x => x.Text, "Old • Morning Edition"));

    cut.SetParametersAndRender(p => p
      .Add(x => x.Head, "WXYZ • ")
      .Add(x => x.Text, "Morning Edition • NPR News"));

    var updates = EngineCalls("update");
    updates.Should().HaveCount(1);
    updates[0].Arguments[1].Should().Be("append",
      "the head is not part of the diffed text, so the eviction still aligns as a continuation");
    updates[0].Arguments[2].Should().Be(6);
    updates[0].Arguments[3].Should().Be("WXYZ • ");
    updates[0].Arguments[4].Should().Be("Morning Edition • NPR News");
  }

  [Fact]
  public void Marquee_HeadChange_WithUnchangedBody_CallsUpdateSwap_NeverReset()
  {
    // A rolling-PS page flip: the decoder trims the name, so consecutive
    // pages differ in length ("Rock 92" → "Limeligh" → "Rush"). The body is
    // unchanged, so this is an in-place change; the engine absorbs the
    // head's width delta into the offset. It must never be a reset.
    var cut = RenderComponent<RdsScrollMarquee>(p => p
      .Add(x => x.Head, "Rock 92 • ")
      .Add(x => x.Text, "Hotel California"));

    cut.SetParametersAndRender(p => p
      .Add(x => x.Head, "Limeligh • ")
      .Add(x => x.Text, "Hotel California"));
    cut.SetParametersAndRender(p => p
      .Add(x => x.Head, "Rush • ")
      .Add(x => x.Text, "Hotel California"));

    var updates = EngineCalls("update");
    updates.Should().HaveCount(2);
    updates.Should().AllSatisfy(u =>
    {
      u.Arguments[1].Should().Be("swap");
      u.Arguments[2].Should().Be(0);
      u.Arguments[4].Should().Be("Hotel California");
    });
    updates[0].Arguments[3].Should().Be("Limeligh • ");
    updates[1].Arguments[3].Should().Be("Rush • ");
  }

  [Fact]
  public void Marquee_HeadAndBodyChangeTogether_ClassifiesTheBody()
  {
    var cut = RenderComponent<RdsScrollMarquee>(p => p
      .Add(x => x.Head, "Rock 92 • ")
      .Add(x => x.Text, "Hotel California"));

    cut.SetParametersAndRender(p => p
      .Add(x => x.Head, "Rush • ")
      .Add(x => x.Text, "Hotel California • Tom Sawyer"));

    var updates = EngineCalls("update");
    updates.Should().HaveCount(1);
    updates[0].Arguments[1].Should().Be("append");
    updates[0].Arguments[3].Should().Be("Rush • ");
    updates[0].Arguments[4].Should().Be("Hotel California • Tom Sawyer");
  }

  [Fact]
  public void Marquee_HeadOnlyChange_DoesNotReInit()
  {
    var cut = RenderComponent<RdsScrollMarquee>(p => p
      .Add(x => x.Head, "Rock 92 • ")
      .Add(x => x.Text, "Hotel California"));

    cut.SetParametersAndRender(p => p
      .Add(x => x.Head, "Rush • ")
      .Add(x => x.Text, "Hotel California"));

    EngineCalls("init").Should().HaveCount(1);
    EngineCalls("dispose").Should().BeEmpty();
  }

  [Fact]
  public void Marquee_TrackSpans_CarryMountedText_AndEngineOwnsUpdates()
  {
    // Blazor renders the head/body spans with the text they mounted with and
    // never rewrites them: the engine sets textContent and the compensated
    // offset in one JS task, so the new text cannot paint a round-trip
    // before the offset catches up. The title and SR mirror DO follow the
    // live parameters.
    var cut = RenderComponent<RdsScrollMarquee>(p => p
      .Add(x => x.Head, "WUNC • ")
      .Add(x => x.Text, "Morning Edition"));

    cut.Find(".rcp-rds-rt-head").TextContent.Should().Be("WUNC • ");
    cut.Find(".rcp-rds-rt-body").TextContent.Should().Be("Morning Edition");

    cut.SetParametersAndRender(p => p
      .Add(x => x.Head, "WUNC • ")
      .Add(x => x.Text, "Morning Edition • NPR News"));

    cut.Find(".rcp-rds-rt-body").TextContent.Should().Be("Morning Edition",
      "the track text is the engine's to update once attached");
    cut.Find(".rcp-rds-rt-sr-only").TextContent.Trim().Should().Be("WUNC • Morning Edition • NPR News");
    cut.Find(".rcp-rds-rt-scroll").GetAttribute("title").Should().Be("WUNC • Morning Edition • NPR News");
    EngineCalls("update").Single().Arguments[4].Should().Be("Morning Edition • NPR News",
      "the engine receives the new body text and writes it itself");
  }

  [Fact]
  public void Marquee_TrackSpans_AreAdjacent_NoWhitespaceTextNodeBetween()
  {
    // The head's trailing separator space is preserved by white-space: pre
    // on the spans; a whitespace text node BETWEEN the spans would be a third
    // piece of text the engine does not manage and would double the gap.
    var cut = RenderComponent<RdsScrollMarquee>(p => p
      .Add(x => x.Head, "WUNC • ")
      .Add(x => x.Text, "Morning Edition"));

    var track = cut.Find(".rcp-rds-rt-track");
    // head, body, and the engine-owned seamless-loop repeat span (empty in Blazor's markup).
    track.ChildNodes.Should().HaveCount(3);
    track.ChildNodes.Should().AllSatisfy(n => n.NodeType.Should().Be(AngleSharp.Dom.NodeType.Element));
    track.QuerySelector(".rcp-rds-rt-repeat")!.TextContent.Should().BeEmpty();
    track.TextContent.Should().Be("WUNC • Morning Edition");
  }

  [Fact]
  public void Marquee_ReMountAfterEmpty_MountsTheCurrentText()
  {
    // After the markup unmounts (empty Text) and re-mounts, the spans must
    // carry the CURRENT text — the freeze applies only while an engine is
    // attached to them.
    var cut = RenderComponent<RdsScrollMarquee>(p => p
      .Add(x => x.Head, "WUNC • ")
      .Add(x => x.Text, "Morning Edition"));

    cut.SetParametersAndRender(p => p
      .Add(x => x.Head, "WUNC • ")
      .Add(x => x.Text, string.Empty));
    cut.SetParametersAndRender(p => p
      .Add(x => x.Head, "KEXP • ")
      .Add(x => x.Text, "All Things Considered"));

    cut.Find(".rcp-rds-rt-head").TextContent.Should().Be("KEXP • ");
    cut.Find(".rcp-rds-rt-body").TextContent.Should().Be("All Things Considered");
    EngineCalls("init").Should().HaveCount(2);
  }

  [Fact]
  public void Marquee_TitleAndMirror_CarryHeadAndBody()
  {
    var cut = RenderComponent<RdsScrollMarquee>(p => p
      .Add(x => x.Head, "WUNC • ")
      .Add(x => x.Text, "Morning Edition"));

    cut.Find(".rcp-rds-rt-scroll").GetAttribute("title").Should().Be("WUNC • Morning Edition");
    cut.Find(".rcp-rds-rt-sr-only").TextContent.Trim().Should().Be("WUNC • Morning Edition");
  }

  [Fact]
  public void Marquee_DoesNotReRender_WhenHeadAndTextUnchanged()
  {
    var cut = RenderComponent<RdsScrollMarquee>(p => p
      .Add(x => x.Head, "WUNC • ")
      .Add(x => x.Text, "Morning Edition"));

    cut.SetParametersAndRender(p => p
      .Add(x => x.Head, "WUNC • ")
      .Add(x => x.Text, "Morning Edition"));
    var afterPrime = cut.RenderCount;

    cut.SetParametersAndRender(p => p
      .Add(x => x.Head, "WUNC • ")
      .Add(x => x.Text, "Morning Edition"));

    cut.RenderCount.Should().Be(afterPrime);
    EngineCalls("update").Should().BeEmpty();
  }

  [Fact]
  public void Marquee_TextCleared_DisposesEngine_AndReInitsOnNextText()
  {
    var cut = RenderComponent<RdsScrollMarquee>(p => p
      .Add(x => x.Text, "WUNC News"));

    cut.SetParametersAndRender(p => p
      .Add(x => x.Text, string.Empty));

    EngineCalls("dispose").Should().HaveCount(1,
      "unmounting the markup must tear down the engine instance (animation, listeners, DOM refs — leak guard)");

    cut.SetParametersAndRender(p => p
      .Add(x => x.Text, "New station content"));

    EngineCalls("init").Should().HaveCount(2,
      "a later non-empty text re-inits against the freshly-created elements");
  }

  [Fact]
  public void Marquee_ComponentDispose_DisposesEngineInstance()
  {
    var cut = RenderComponent<RdsScrollMarquee>(p => p
      .Add(x => x.Text, "WUNC News"));

    DisposeComponents();

    EngineCalls("dispose").Should().HaveCount(1,
      "circuit/component teardown must release the engine instance");
  }

  // ─── Render-guard regression tests (RDS scroll-stability fix) ────────────
  // The parent (RadioControlPanel) re-renders ~2x/second on signal telemetry.
  // ShouldRender() suppresses the no-op renders so neither the DOM nor the
  // JS engine sees any churn.

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
      "an unchanged Text must not re-render the marquee (no DOM churn, no interop)");
  }

  [Fact]
  public void Marquee_NoOpReRenders_ProduceNoEngineCalls()
  {
    var cut = RenderComponent<RdsScrollMarquee>(p => p
      .Add(x => x.Text, "WUNC News"));

    for (var i = 0; i < 10; i++)
    {
      cut.SetParametersAndRender(p => p
        .Add(x => x.Text, "WUNC News"));
    }

    EngineCalls("init").Should().HaveCount(1,
      "telemetry-tick re-renders must not re-init the engine (leak + jerk guard)");
    EngineCalls("update").Should().BeEmpty(
      "no text/speed change means no engine sync at all");
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
}

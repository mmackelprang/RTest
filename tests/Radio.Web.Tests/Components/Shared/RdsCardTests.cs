using System.IO;
using System.Text.RegularExpressions;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Radio.Web.Components.Shared;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// bUnit tests for the <see cref="RdsCard"/> widget introduced by PR 3 of the
/// Radio Controller Polish arc. The card mounts above the frequency well in
/// <c>RadioControlPanel</c>. Renders when EITHER <c>StationName</c> OR
/// <c>RadioText</c> is non-empty, and is hidden only when both are absent
/// (post HANDOFF-rds-inline-scroll-revision — the RT marquee lives in the
/// PS slot so the card stays useful during transient tune-in states where
/// RT chunks arrive before PS confirms). PTY chip renders only when
/// supplied.
/// </summary>
public class RdsCardTests : TestContext
{
  public RdsCardTests()
  {
    Services.AddRadzenComponents();
    JSInterop.Mode = JSRuntimeMode.Loose;
  }

  [Fact]
  public void RdsCard_RendersNothing_WhenBothStationNameAndRadioTextNull()
  {
    // Post HANDOFF-rds-inline-scroll-revision the render gate is
    // (!IsNullOrEmpty(StationName) || !IsNullOrEmpty(RadioText)). The
    // card hides ONLY in the both-empty case — RadioText alone is now
    // enough to keep the card on screen during transient tune-ins. Pass
    // RadioText explicitly so the both-empty intent is obvious at the
    // assertion level rather than relying on the parameter default.
    var cut = RenderComponent<RdsCard>(p => p
      .Add(x => x.StationName, null)
      .Add(x => x.RadioText, null));

    // No .rds-card root in DOM — the card collapses entirely so the
    // surrounding layout doesn't have to skirt an empty box.
    Assert.Empty(cut.FindAll(".rds-card"));
  }

  [Fact]
  public void RdsCard_RendersNothing_WhenBothStationNameAndRadioTextEmpty()
  {
    // Empty-string variant of the both-absent gate — IsNullOrEmpty treats
    // null and "" the same, so both forms must collapse the card.
    var cut = RenderComponent<RdsCard>(p => p
      .Add(x => x.StationName, string.Empty)
      .Add(x => x.RadioText, string.Empty));

    Assert.Empty(cut.FindAll(".rds-card"));
  }

  [Fact]
  public void RdsCard_RendersStationName_WhenProvided()
  {
    var cut = RenderComponent<RdsCard>(p => p
      .Add(x => x.StationName, "KQED FM"));

    var station = cut.Find(".rds-card-station");
    Assert.Equal("KQED FM", station.TextContent.Trim());

    // The mono "RDS" label is always present alongside the station name.
    var label = cut.Find(".rds-card-label");
    Assert.Equal("RDS", label.TextContent.Trim());
  }

  [Fact]
  public void RdsCard_RendersProgramType_WhenProvided()
  {
    var cut = RenderComponent<RdsCard>(p => p
      .Add(x => x.StationName, "KQED FM")
      .Add(x => x.ProgramType, "News"));

    var pty = cut.Find(".rds-card-pty");
    Assert.Equal("News", pty.TextContent.Trim());
  }

  [Fact]
  public void RdsCard_HidesProgramType_WhenEmpty()
  {
    var cut = RenderComponent<RdsCard>(p => p
      .Add(x => x.StationName, "KQED FM")
      .Add(x => x.ProgramType, ""));

    Assert.Empty(cut.FindAll(".rds-card-pty"));
  }

  [Fact]
  public void RdsCard_HidesProgramType_WhenNull()
  {
    var cut = RenderComponent<RdsCard>(p => p
      .Add(x => x.StationName, "KQED FM")
      .Add(x => x.ProgramType, null));

    Assert.Empty(cut.FindAll(".rds-card-pty"));
  }

  // ─── Task #15 PR B (handoff item #39): cyan accent on station name ────────
  //
  // The station name on RdsCard is the design's call-out colour: --accent-primary
  // (cyan). bUnit doesn't fully resolve CSS variables on getComputedStyle, so
  // we pin both the class (component contract) AND the design-system rule
  // (the only place the colour is bound). Together they prove the wire path:
  // the element receives the class, and the class binds to the cyan token.

  [Fact]
  public void RdsCard_StationName_ComputedColorIsAccentPrimary()
  {
    // Component contract: the station-name span carries the .rds-card-station
    // class that the design-system stylesheet targets with the cyan colour rule.
    var cut = RenderComponent<RdsCard>(p => p
      .Add(x => x.StationName, "KEXP"));

    var station = cut.Find(".rds-card-station");
    station.ClassList.Should().Contain("rds-card-station",
      "the station name span must carry the class that the cyan rule targets");

    // Stylesheet contract: the .rds-card-station rule must bind colour to
    // --accent-primary (the cyan token). If a future refactor recolours the
    // station name to anything else, this assertion trips.
    var cssPath = LocateDesignSystemCss();
    var css = File.ReadAllText(cssPath);
    var rulePattern = new Regex(
      @"\.rds-card-station\s*\{[^}]*?color:\s*var\(--accent-primary\)",
      RegexOptions.Singleline);
    rulePattern.IsMatch(css).Should().BeTrue(
      "the .rds-card-station rule in design-system.css must bind colour to --accent-primary");
  }

  // --- Render-guard parity test (RDS scroll-stability fix) ---
  // RdsCard composes the marquee track + passes it to the nested
  // RdsScrollMarquee. Its ShouldRender guard stops telemetry-tick churn from
  // re-running the nested-component diff when nothing the user sees changed.

  [Fact]
  public void Card_DoesNotReRender_WhenInputsUnchanged()
  {
    var cut = RenderComponent<RdsCard>(p => p
      .Add(x => x.StationName, "WUNC")
      .Add(x => x.RadioText, "Morning Edition"));

    // ShouldRender primes its cache on the first consult (the first identical
    // update below), so we assert the steady-state: the SECOND identical
    // update — i.e. the ~2x/second telemetry ticks on a live station — is
    // suppressed and doesn't re-run the nested marquee diff.
    cut.SetParametersAndRender(p => p
      .Add(x => x.StationName, "WUNC")
      .Add(x => x.RadioText, "Morning Edition"));

    var afterPrime = cut.RenderCount;

    cut.SetParametersAndRender(p => p
      .Add(x => x.StationName, "WUNC")
      .Add(x => x.RadioText, "Morning Edition"));

    cut.RenderCount.Should().Be(afterPrime,
      "unchanged RDS inputs must not re-run the nested marquee diff");
  }

  [Fact]
  public void Card_ReRenders_WhenRadioTextChanges()
  {
    var cut = RenderComponent<RdsCard>(p => p
      .Add(x => x.StationName, "WUNC")
      .Add(x => x.RadioText, "Morning Edition"));

    var before = cut.RenderCount;

    cut.SetParametersAndRender(p => p
      .Add(x => x.StationName, "WUNC")
      .Add(x => x.RadioText, "All Things Considered"));

    cut.RenderCount.Should().BeGreaterThan(before,
      "a changed RadioText must re-render so the new buffer scrolls");
  }

  /// <summary>
  /// Locate the design-system.css source file by walking up from the test
  /// binary directory until we find the Radio.Web/wwwroot/css folder. The
  /// stylesheet isn't copied into the test output, so a relative path
  /// lookup is the load-bearing piece.
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

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
  public void RdsCard_KeepsItsSlot_WithANoRdsPlaceholder_WhenBothStationNameAndRadioTextNull()
  {
    // The card used to be REMOVED when there was no RDS, and because it sits in a flex column above
    // the frequency well, tuning between an RDS and a non-RDS station moved the whole tuner. It now
    // always renders, at a fixed height (design-system.css), with a dim placeholder.
    var cut = RenderComponent<RdsCard>(p => p
      .Add(x => x.StationName, null)
      .Add(x => x.RadioText, null));

    Assert.Single(cut.FindAll(".rds-card"));
    Assert.Equal("—", cut.Find(".rds-card-empty").TextContent.Trim());
  }

  [Fact]
  public void RdsCard_KeepsItsSlot_WithANoRdsPlaceholder_WhenBothStationNameAndRadioTextEmpty()
  {
    // Empty-string variant: IsNullOrEmpty treats null and "" the same.
    var cut = RenderComponent<RdsCard>(p => p
      .Add(x => x.StationName, string.Empty)
      .Add(x => x.RadioText, string.Empty));

    Assert.Single(cut.FindAll(".rds-card"));
    Assert.Single(cut.FindAll(".rds-card-empty"));
  }

  [Fact]
  public void RdsCard_ShowsNoPlaceholder_WhenThereIsRds()
  {
    var cut = RenderComponent<RdsCard>(p => p.Add(x => x.StationName, "KQED FM"));

    Assert.Empty(cut.FindAll(".rds-card-empty"));
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

  // --- Head / body split (AUD-63) ---
  // The card hands the station name (plus separator) and the RT buffer to
  // the marquee as two parameters, so only the RT is diffed for transitions.

  [Fact]
  public void Card_PinsStationName_BesideTheTicker_AndScrollsOnlyRadioText()
  {
    // Owner ruling 2026-09-28: the station name is pinned and always visible; only the RT scrolls,
    // looping with the separator between passes (the head/body split of AUD-63 remains in the
    // marquee, but the card no longer uses the head).
    var cut = RenderComponent<RdsCard>(p => p
      .Add(x => x.StationName, "WUNC")
      .Add(x => x.RadioText, "Morning Edition")
      .Add(x => x.Separator, " • "));

    cut.Find(".rds-card-station--pinned").TextContent.Should().Be("WUNC");
    cut.Find(".rcp-rds-rt-head").TextContent.Should().BeEmpty();
    cut.Find(".rcp-rds-rt-body").TextContent.Should().Be("Morning Edition");
    cut.Find(".rcp-rds-rt-track").GetAttribute("data-loop-sep").Should().Be(" • ",
      "the configured separator is what joins one pass of the RT to the next");
  }

  [Fact]
  public void Card_WithoutStationName_RendersEmptyHead_NoLeadingSeparator()
  {
    var cut = RenderComponent<RdsCard>(p => p
      .Add(x => x.StationName, null)
      .Add(x => x.RadioText, "Morning Edition"));

    cut.Find(".rcp-rds-rt-head").TextContent.Should().BeEmpty();
    cut.Find(".rcp-rds-rt-track").TextContent.Should().Be("Morning Edition");
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

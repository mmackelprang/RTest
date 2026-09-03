using System.Text.RegularExpressions;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Radio.Core.Configuration;
using Radzen;
using Radio.Web.Components.Shared;
using Radio.Web.Models;
using Radio.Web.Services;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// bUnit tests for <see cref="EncoderSelectorOverlay"/> — the centred selection list the SOURCE
/// knob opens (ENC-5) and the PRESETS knob will reuse unchanged (ENC-7).
///
/// Contract under test:
///   - The five handoff §6.6 states all render: A previewing, B dimmed-with-a-reason and the
///     instructional empty list, C the blocked flash, D a commit in flight, E a commit that failed.
///     D and E replace the LIST and leave the card up — an overlay that dismissed into silence is
///     how a person concludes the knob is broken and presses it again.
///   - The list windows to seven rows around the highlight. SOURCE never needs it; ENC-7's bank
///     caps at 50, and building the window here is the difference between ENC-7 consuming this
///     component and rewriting it.
///   - It is a readout, not a control: no row is a button.
/// </summary>
public class EncoderSelectorOverlayTests : TestContext
{
  public EncoderSelectorOverlayTests()
  {
    JSInterop.Mode = JSRuntimeMode.Loose;
    Services.AddRadzenComponents();
  }

  private static EncoderSelectorRowDto Row(
    string id,
    string primary,
    string? secondary = null,
    bool isCurrent = false,
    bool isAvailable = true,
    string? unavailableReason = null,
    string? ordinal = null,
    string? icon = null,
    string? accentVar = null) => new()
    {
      Id = id,
      Primary = primary,
      Secondary = secondary,
      IsCurrent = isCurrent,
      IsAvailable = isAvailable,
      UnavailableReason = unavailableReason,
      Ordinal = ordinal,
      Icon = icon,
      AccentVar = accentVar,
    };

  private static List<EncoderSelectorRowDto> DefaultRows() =>
  [
    Row("band:FM", "FM", "101.5 MHz", isCurrent: true, accentVar: "--source-radio"),
    Row("band:AM", "AM", "1010 kHz", accentVar: "--source-radio"),
    Row("source:Bluetooth", "BLUETOOTH", "Pixel 8", accentVar: "--source-bluetooth"),
  ];

  private static EncoderHudDto Overlay(
    string phase = "SelectorPreview",
    int highlight = 0,
    List<EncoderSelectorRowDto>? rows = null,
    string? title = "SOURCE",
    string? titleSuffix = null,
    string? footer = "PRESS THE KNOB TO SWITCH",
    string? primaryText = null,
    string? secondaryText = null,
    string? emptyPrimary = null,
    string? emptySecondary = null) => new()
    {
      EncoderIndex = 1,
      Label = "SOURCE",
      Phase = phase,
      DurationMs = EncoderInteractionTimings.SelectorIdleDismissMs,
      Rows = rows ?? DefaultRows(),
      HighlightIndex = highlight,
      Title = title,
      TitleSuffix = titleSuffix,
      Footer = footer,
      PrimaryText = primaryText,
      SecondaryText = secondaryText,
      EmptyPrimary = emptyPrimary,
      EmptySecondary = emptySecondary,
    };

  private IRenderedComponent<EncoderSelectorOverlay> RenderOverlay(EncoderHudDto hud)
    => RenderComponent<EncoderSelectorOverlay>(p => p.Add(x => x.Hud, hud));

  [Fact]
  public void RendersTitleAndFooter()
  {
    var cut = RenderOverlay(Overlay(titleSuffix: "4 saved"));

    cut.Find(".encoder-selector-title-text").TextContent.Trim().Should().Be("SOURCE");
    cut.Find(".encoder-selector-title-suffix").TextContent.Trim().Should().Be("4 saved");
    cut.Find(".encoder-selector-footer").TextContent.Trim().Should().Be("PRESS THE KNOB TO SWITCH");
  }

  [Fact]
  public void HighlightedRow_CarriesTheHighlightClass()
  {
    var cut = RenderOverlay(Overlay(highlight: 1));

    var rows = cut.FindAll(".encoder-selector-row");
    rows.Count.Should().Be(3);
    rows[0].ClassList.Should().NotContain("is-highlighted");
    rows[1].ClassList.Should().Contain("is-highlighted");
    rows[2].ClassList.Should().NotContain("is-highlighted");

    // The accent bar is a child of the highlighted row and of nothing else — it is both the
    // 2px marker and the element the wrap animation is hung on, and an element that is created
    // when the highlight lands on it starts that animation unconditionally.
    cut.FindAll(".encoder-selector-bar").Should().ContainSingle();
    rows[1].QuerySelector(".encoder-selector-bar").Should().NotBeNull();
  }

  [Fact]
  public void CurrentRow_CarriesTheCurrentClass_IndependentlyOfTheHighlight()
  {
    // "Current" is what is playing; "highlighted" is what a press would commit. Spinning the knob
    // moves one and not the other, which is the whole preview-then-commit mechanism.
    var rows = new List<EncoderSelectorRowDto>
    {
      Row("band:FM", "FM"),
      Row("band:AM", "AM"),
      Row("source:Bluetooth", "BLUETOOTH", isCurrent: true),
    };

    var cut = RenderOverlay(Overlay(highlight: 0, rows: rows));

    var rendered = cut.FindAll(".encoder-selector-row");
    rendered[0].ClassList.Should().Contain("is-highlighted");
    rendered[0].ClassList.Should().NotContain("is-current");
    rendered[2].ClassList.Should().Contain("is-current");
    rendered[2].ClassList.Should().NotContain("is-highlighted");

    // The marker is a glyph, not a colour — handoff §15 wants the state readable in greyscale.
    rendered[2].QuerySelector(".encoder-selector-marker")!.TextContent.Trim().Should().Be("◀");
    rendered[0].QuerySelector(".encoder-selector-marker")!.TextContent.Trim().Should().BeEmpty();
  }

  [Fact]
  public void UnavailableRow_IsDimmedAndStatesItsReason()
  {
    // Handoff §6.6 State B is "dimmed WITH A REASON". Dimming alone is a dead end, so the reason
    // being on screen is the requirement and the class is only half of it.
    var rows = new List<EncoderSelectorRowDto>
    {
      Row("band:FM", "FM", "101.5 MHz"),
      Row("source:Bluetooth", "BLUETOOTH", "no device", isAvailable: false,
        unavailableReason: "no device paired"),
    };

    var cut = RenderOverlay(Overlay(highlight: 0, rows: rows));

    var bt = cut.FindAll(".encoder-selector-row")[1];
    bt.ClassList.Should().Contain("is-unavailable");
    bt.QuerySelector(".encoder-selector-secondary")!.TextContent.Trim()
      .Should().Be("no device · no device paired");
  }

  [Fact]
  public void UnavailableRow_WithNoSecondary_RendersTheReasonWithoutALeadingSeparator()
  {
    // SourceBubble.razor:29's idiom. Without the TrimStart a row with no secondary of its own
    // reads " · no tuner detected", which looks like a rendering fault rather than an explanation.
    var rows = new List<EncoderSelectorRowDto>
    {
      Row("band:FM", "FM", isAvailable: false, unavailableReason: "no tuner detected"),
    };

    var cut = RenderOverlay(Overlay(highlight: 0, rows: rows));

    cut.Find(".encoder-selector-secondary").TextContent.Trim().Should().Be("no tuner detected");
  }

  [Fact]
  public void BlockedPhase_FlashesOnlyTheHighlightedRow()
  {
    // State C — a commit that landed on an unavailable row is never a silent no-op, and the flash
    // belongs to the row the commit was aimed at rather than to every dimmed row in the list.
    var rows = new List<EncoderSelectorRowDto>
    {
      Row("band:FM", "FM"),
      Row("source:Bluetooth", "BLUETOOTH", isAvailable: false, unavailableReason: "no device paired"),
      Row("source:Vinyl", "PHONO", isAvailable: false, unavailableReason: "no input detected"),
    };

    var cut = RenderOverlay(Overlay(phase: "SelectorBlocked", highlight: 1, rows: rows));

    var rendered = cut.FindAll(".encoder-selector-row");
    rendered[1].ClassList.Should().Contain("is-blocked");
    rendered[0].ClassList.Should().NotContain("is-blocked");
    rendered[2].ClassList.Should().NotContain("is-blocked",
      "the other unavailable row was not the one the press landed on");

    // A blocked card still carries the whole list, so the flash annotates the list rather than
    // replacing it — the contrast with States D and E below, which do replace it.
    rendered.Count.Should().Be(3);
  }

  [Fact]
  public void CommittingPhase_ReplacesTheListWithASpinnerAndStaysUp()
  {
    var cut = RenderOverlay(Overlay(
      phase: "SelectorCommitting",
      primaryText: "Switching to Bluetooth…",
      secondaryText: "Pixel 8"));

    cut.FindAll(".encoder-selector-row").Should().BeEmpty("the list is replaced, not annotated");
    cut.FindAll(".encoder-selector-spinner").Should().ContainSingle();
    cut.Find(".encoder-selector-message-primary").TextContent.Trim()
      .Should().Be("Switching to Bluetooth…");
    // "Stays up" is the load-bearing half: the card is still on screen with the heading on it.
    cut.Find(".encoder-selector-overlay").Should().NotBeNull();
    cut.Find(".encoder-selector-title-text").TextContent.Trim().Should().Be("SOURCE");
  }

  [Fact]
  public void FailedPhase_ShowsTheReasonAndWhatIsStillPlaying()
  {
    // State E. Both halves matter: why it failed, and that something is still playing — otherwise
    // the user concludes the knob is broken and spins it again, which is the input pattern that
    // provokes this project's capture-lifecycle bug.
    var cut = RenderOverlay(Overlay(
      phase: "SelectorFailed",
      primaryText: "Couldn't switch to Bluetooth",
      secondaryText: "Still playing FM 101.5"));

    cut.FindAll(".encoder-selector-row").Should().BeEmpty();
    cut.FindAll(".encoder-selector-spinner").Should().BeEmpty("nothing is in flight any more");
    cut.Find(".encoder-selector-message-primary").TextContent.Trim()
      .Should().Be("Couldn't switch to Bluetooth");
    cut.Find(".encoder-selector-message-secondary").TextContent.Trim()
      .Should().Be("Still playing FM 101.5");
  }

  [Fact]
  public void EmptyRows_RenderTheInstructionalEmptyState_AndOmitTheFooter()
  {
    // Nothing in ENC-5 reaches this — the SOURCE list always has rows, dimmed when unavailable.
    // ENC-7's empty preset bank is why it exists here rather than in ENC-7.
    var cut = RenderOverlay(Overlay(
      highlight: -1,
      rows: [],
      title: "PRESETS",
      emptyPrimary: "No presets saved",
      emptySecondary: "Hold the knob on a station to save it"));

    cut.Find(".encoder-selector-empty-primary").TextContent.Trim().Should().Be("No presets saved");
    cut.Find(".encoder-selector-empty-secondary").TextContent.Trim()
      .Should().Be("Hold the knob on a station to save it");
    cut.FindAll(".encoder-selector-footer").Should().BeEmpty(
      "a \"press the knob\" instruction under an empty list instructs nothing");
  }

  [Theory]
  // A list that fits shows all of it, wherever the highlight is.
  [InlineData(7, 0, 0)]
  [InlineData(7, 3, 0)]
  [InlineData(7, 6, 0)]
  // ENC-7's 50-preset bank: the window centres on the highlight and clamps at both ends.
  [InlineData(50, 0, 0)]
  [InlineData(50, 25, 22)]
  [InlineData(50, 49, 43)]
  public void WindowStart_KeepsTheHighlightVisible(int total, int highlight, int expectedStart)
  {
    int start = EncoderSelectorOverlay.WindowStart(
      total, highlight, EncoderInteractionTimings.SelectorVisibleRows);

    start.Should().Be(expectedStart);
    // The property the arithmetic exists for, asserted rather than assumed.
    highlight.Should().BeGreaterThanOrEqualTo(start);
    highlight.Should().BeLessThan(start + EncoderInteractionTimings.SelectorVisibleRows);
  }

  [Fact]
  public void MoreThanSevenRows_RendersExactlySeven()
  {
    var rows = Enumerable.Range(0, 12)
      .Select(i => Row($"preset:{i}", $"STATION {i}", ordinal: $"{i:00}"))
      .ToList();

    var cut = RenderOverlay(Overlay(highlight: 11, rows: rows, title: "PRESETS"));

    var rendered = cut.FindAll(".encoder-selector-row");
    rendered.Count.Should().Be(EncoderInteractionTimings.SelectorVisibleRows);
    // Clamped to the end of the list, so the window is rows 5..11 and the highlight is the last.
    rendered[0].QuerySelector(".encoder-selector-primary")!.TextContent.Trim().Should().Be("STATION 5");
    rendered[6].ClassList.Should().Contain("is-highlighted");
  }

  [Fact]
  public void Overlay_IsNotClickable()
  {
    var cut = RenderOverlay(Overlay(highlight: 1));

    // bUnit computes no styles, so this asserts the two things it CAN see — the root carries the
    // class the stylesheet declares pointer-events on, and no row is a button — plus the
    // declaration itself, read out of the stylesheet source. A computed-style assertion is not
    // available here and is deliberately not faked.
    cut.Find("div.encoder-selector-overlay").Should().NotBeNull();
    cut.FindAll("button").Should().BeEmpty(
      "every function the overlay offers already has a touch equivalent; a tappable row would be a "
      + "second, divergent way to switch sources");

    var css = File.ReadAllText(LocateDesignSystemCss());
    new Regex(@"\.encoder-selector-overlay\s*\{[^}]*?pointer-events:\s*none", RegexOptions.Singleline)
      .IsMatch(css).Should().BeTrue(
        "a 440px card at z-index 10000 over the middle of Home would swallow taps meant for the "
        + "panel underneath it the moment this declaration goes missing");
  }

  [Fact]
  public void UnknownPhase_RendersNothing()
  {
    // The forward-compatibility rule, checked on the selector branch specifically: EncoderHud gates
    // on EncoderHudService.IsKnownPhase, so a selector-shaped phase a newer API invents must
    // degrade to silence rather than to a half-drawn overlay.
    var clock = new FakeTimeProvider();
    using var hud = new EncoderHudService(hub: null, timeProvider: clock);
    Services.AddSingleton(hud);

    hud.Publish(Overlay(phase: "SelectorSomethingANewerApiInvented"));

    var cut = RenderComponent<EncoderHud>();

    cut.Markup.Trim().Should().BeEmpty();
  }

  /// <summary>
  /// Locate design-system.css by walking up from the test binary directory; the stylesheet is not
  /// copied into the test output. Same helper shape as EncoderHudTests and RdsCardTests.
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

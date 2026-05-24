using System.IO;
using System.Text.RegularExpressions;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Radio.Core.Models;
using Radio.Web.Components.Shared;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// bUnit tests for <see cref="SleepForecastPane"/> — v2 visual redesign
/// (HANDOFF-sleep-weather-visual-redesign.md).
///
/// Locks the two-region structure (primary block + sub-line + optional
/// 3-card forecast row), the byte-identical wall-clock typography on the
/// primary temperature numeral, the partial-day cases (3/2/1), and the
/// stale-state affordance. The v1 fixture was structural enough that the
/// new selectors required a near-total rewrite — Designer's spec §9 calls
/// out the selector renames (.sleep-forecast-day → .sleep-forecast-card-day,
/// .sleep-forecast-footer → .sleep-forecast-subline, etc.).
/// </summary>
public class SleepForecastPaneTests : TestContext
{
  public SleepForecastPaneTests()
  {
    Services.AddRadzenComponents();
    JSInterop.Mode = JSRuntimeMode.Loose;
  }

  // ── Helpers ─────────────────────────────────────────────────────────────

  /// <summary>
  /// Build a fresh test forecast with N days. Day 0 is Sunday with the
  /// reference-image data (60/77/66, "Partly Sunny") so the assertions stay
  /// easy to read.
  /// </summary>
  private static WeatherForecast BuildForecast(int dayCount, bool isStale = false)
  {
    var days = new List<WeatherDay>
    {
      new(new DateOnly(2026, 5, 24), "Today", 77, 66, 25, 19, "Partly Sunny",
          "Partly sunny with a high near 77.", 20, "partly-cloudy", null),
      new(new DateOnly(2026, 5, 25), "Mon", 76, 68, 24, 20, "Cloudy",
          "Mostly cloudy.", 30, "cloudy", null),
      new(new DateOnly(2026, 5, 26), "Tue", 80, 66, 27, 19, "Sunny",
          "Sunny.", 5, "sunny", null),
    };

    return new WeatherForecast(
      Zip: "27312",
      LocationName: "Pittsboro, NC",
      // Fresh: 1 hour old; Stale: 25 hours old so the "yesterday at HH:mm"
      // sub-line branch fires.
      GeneratedAtUtc: DateTime.UtcNow.AddHours(isStale ? -25 : -1),
      FetchedAtUtc: DateTime.UtcNow.AddMinutes(-1),
      IsStale: isStale,
      Days: days.Take(dayCount).ToList());
  }

  // ── Region 1 — primary block contract ───────────────────────────────────

  [Fact]
  public void Pane_RendersPrimaryBlock_WithIconTempUnitAndRightColumn()
  {
    var cut = RenderComponent<SleepForecastPane>(p => p
      .Add(x => x.Forecast, BuildForecast(3))
      .Add(x => x.TemperatureUnit, "F"));

    // The four primary-block children must all render — this is the
    // load-bearing structural contract for the new layout.
    cut.Find(".sleep-forecast-primary").Should().NotBeNull();
    cut.Find(".sleep-forecast-primary-icon").Should().NotBeNull();
    cut.Find(".sleep-forecast-primary-temp").Should().NotBeNull();
    cut.Find(".sleep-forecast-primary-unit").Should().NotBeNull();
    cut.Find(".sleep-forecast-primary-right").Should().NotBeNull();
    cut.Find(".sleep-forecast-primary-condition").Should().NotBeNull();
    cut.Find(".sleep-forecast-primary-hl").Should().NotBeNull();
  }

  [Fact]
  public void Pane_PrimaryTemp_RendersTodayHighInFahrenheit_WhenUnitIsF()
  {
    var cut = RenderComponent<SleepForecastPane>(p => p
      .Add(x => x.Forecast, BuildForecast(3))
      .Add(x => x.TemperatureUnit, "F"));

    var temp = cut.Find(".sleep-forecast-primary-temp");
    // Today.HighF=77 — the spec uses today's high as the current-temp
    // display (no separate "current" field in the data model; §9 code-
    // behind: CurrentTempDisplay = Today.HighF or HighC).
    temp.TextContent.Should().Contain("77");
    temp.TextContent.Should().Contain("°");
  }

  [Fact]
  public void Pane_PrimaryTemp_RendersTodayHighInCelsius_WhenUnitIsC()
  {
    var cut = RenderComponent<SleepForecastPane>(p => p
      .Add(x => x.Forecast, BuildForecast(3))
      .Add(x => x.TemperatureUnit, "C"));

    var temp = cut.Find(".sleep-forecast-primary-temp");
    temp.TextContent.Should().Contain("25"); // Today.HighC
  }

  [Fact]
  public void Pane_PrimaryTemp_FallsBackToFahrenheit_WhenUnitIsBoth()
  {
    // Spec §3 State F + §9: in "both" mode the 96 px primary block keeps a
    // single unit (the 480 px-wide "60°F · 16°C" string would blow the
    // column budget). Fahrenheit is the fallback to match the v1 default.
    var cut = RenderComponent<SleepForecastPane>(p => p
      .Add(x => x.Forecast, BuildForecast(3))
      .Add(x => x.TemperatureUnit, "both"));

    var temp = cut.Find(".sleep-forecast-primary-temp");
    temp.TextContent.Should().Contain("77"); // HighF
    temp.TextContent.Should().NotContain("25"); // HighC — not on the primary
  }

  [Fact]
  public void Pane_UnitIndicator_MarksFActive_WhenUnitIsF()
  {
    var cut = RenderComponent<SleepForecastPane>(p => p
      .Add(x => x.Forecast, BuildForecast(3))
      .Add(x => x.TemperatureUnit, "F"));

    var unit = cut.Find(".sleep-forecast-primary-unit");
    // F is the first <span> child; with F active the first letter gets
    // .is-active, the second .is-inactive.
    var letters = unit.QuerySelectorAll("span.is-active, span.is-inactive");
    letters.Should().HaveCount(2);
    letters[0].ClassList.Should().Contain("is-active");
    letters[0].TextContent.Trim().Should().Be("F");
    letters[1].ClassList.Should().Contain("is-inactive");
    letters[1].TextContent.Trim().Should().Be("C");
  }

  [Fact]
  public void Pane_UnitIndicator_MarksCActive_WhenUnitIsC()
  {
    var cut = RenderComponent<SleepForecastPane>(p => p
      .Add(x => x.Forecast, BuildForecast(3))
      .Add(x => x.TemperatureUnit, "C"));

    var unit = cut.Find(".sleep-forecast-primary-unit");
    var letters = unit.QuerySelectorAll("span.is-active, span.is-inactive");
    letters[0].ClassList.Should().Contain("is-inactive");
    letters[1].ClassList.Should().Contain("is-active");
  }

  [Fact]
  public void Pane_PrimaryCondition_RendersTodayShortConditionVerbatim()
  {
    var cut = RenderComponent<SleepForecastPane>(p => p
      .Add(x => x.Forecast, BuildForecast(3))
      .Add(x => x.TemperatureUnit, "F"));

    var cond = cut.Find(".sleep-forecast-primary-condition");
    // NWS labels are already capitalised — no text-transform applied; the
    // string round-trips verbatim per spec §4.
    cond.TextContent.Trim().Should().Be("Partly Sunny");
  }

  [Fact]
  public void Pane_PrimaryHL_RendersTodayHighAndLow_WithSeparator()
  {
    var cut = RenderComponent<SleepForecastPane>(p => p
      .Add(x => x.Forecast, BuildForecast(3))
      .Add(x => x.TemperatureUnit, "F"));

    var hl = cut.Find(".sleep-forecast-primary-hl");
    hl.QuerySelector(".sleep-forecast-primary-high")!.TextContent.Trim()
      .Should().Be("77");
    hl.QuerySelector(".sleep-forecast-primary-low")!.TextContent.Trim()
      .Should().Be("66");
    hl.QuerySelector(".sleep-forecast-primary-hl-sep")!.TextContent.Trim()
      .Should().Be("/");
  }

  // ── Region 1 — wall-clock typography parity (load-bearing) ──────────────

  [Fact]
  public void Pane_PrimaryTempRule_UsesSameLedFontAndDimAmber_AsSleepScreenClock()
  {
    // The spec's load-bearing aesthetic decision: the primary temperature
    // numeral must read as the SAME amber instrument as the wall clock.
    // bUnit can't fully resolve CSS variables on getComputedStyle, so we
    // pin BOTH the component contract (the temp span carries the
    // .sleep-forecast-primary-temp class) AND the design-system rule (the
    // class binds to the same recipe as .sleep-screen-clock).
    var cut = RenderComponent<SleepForecastPane>(p => p
      .Add(x => x.Forecast, BuildForecast(3))
      .Add(x => x.TemperatureUnit, "F"));

    var temp = cut.Find(".sleep-forecast-primary-temp");
    temp.ClassList.Should().Contain("sleep-forecast-primary-temp",
      "the temp span must carry the class the byte-identical rule targets");

    var cssPath = LocateDesignSystemCss();
    var css = File.ReadAllText(cssPath);

    // Match the .sleep-forecast-primary-temp body and assert every
    // load-bearing typography property mirrors .sleep-screen-clock per
    // spec §4 ("byte-identical to .sleep-screen-clock").
    var primaryBlockMatch = Regex.Match(
      css,
      @"\.sleep-forecast-primary-temp\s*\{([^}]*)\}",
      RegexOptions.Singleline);
    primaryBlockMatch.Success.Should().BeTrue(
      "the .sleep-forecast-primary-temp rule must exist");
    var body = primaryBlockMatch.Groups[1].Value;

    body.Should().Contain("font-family: var(--font-led)",
      "primary temp must use the LED font (matches .sleep-screen-clock)");
    body.Should().Contain("font-weight: 700",
      "primary temp must be 700 weight (matches .sleep-screen-clock)");
    body.Should().Contain("font-size: 96px",
      "primary temp must be 96 px (matches .sleep-screen-clock)");
    body.Should().Contain("color: color-mix(in srgb, var(--signal-amber) 35%, #050507)",
      "primary temp must use the dim-amber 35 % color-mix (matches .sleep-screen-clock)");
    body.Should().Contain("text-shadow: 0 0 12px color-mix(in srgb, var(--signal-amber) 15%, transparent)",
      "primary temp must use the same text-shadow recipe as .sleep-screen-clock");
    body.Should().Contain("font-variant-numeric: tabular-nums",
      "primary temp must use tabular-nums (matches .sleep-screen-clock)");
    body.Should().Contain("letter-spacing: 0.02em",
      "primary temp must use the same letter-spacing as .sleep-screen-clock");
  }

  // ── Sub-line contract ───────────────────────────────────────────────────

  [Fact]
  public void Pane_SubLine_RendersWithLocationDayAndTime_WhenFresh()
  {
    var cut = RenderComponent<SleepForecastPane>(p => p
      .Add(x => x.Forecast, BuildForecast(3))
      .Add(x => x.TemperatureUnit, "F"));

    var subline = cut.Find(".sleep-forecast-subline");
    var text = subline.TextContent.Trim();

    // Location with the state stripped (spec §3 ShortLocation rule).
    text.Should().Contain("Pittsboro");
    text.Should().NotContain("Pittsboro, NC",
      "the comma-and-state slice must be stripped for the sub-line");

    // The middle-dot separator is rendered as a literal "·" inside the
    // text node — verifies the format string is wired correctly.
    text.Should().Contain("·");
  }

  [Fact]
  public void Pane_SubLine_RendersFullWeekdayName_NotAbbreviation()
  {
    // Spec §3: the sub-line uses the full weekday name ("Sunday") rather
    // than the 3-letter abbreviation; abbreviations live only in the
    // forecast cards. Compute the expected weekday for "today" so the test
    // doesn't drift over time.
    var expectedDay = DateTime.Now.ToString("dddd",
      System.Globalization.CultureInfo.InvariantCulture);

    var cut = RenderComponent<SleepForecastPane>(p => p
      .Add(x => x.Forecast, BuildForecast(3))
      .Add(x => x.TemperatureUnit, "F"));

    cut.Find(".sleep-forecast-subline").TextContent.Should().Contain(expectedDay);
  }

  // ── Stale state contract ────────────────────────────────────────────────

  [Fact]
  public void Pane_IsStale_AppliesOpacityClassAndShowsSyncProblemIcon()
  {
    var cut = RenderComponent<SleepForecastPane>(p => p
      .Add(x => x.Forecast, BuildForecast(3, isStale: true))
      .Add(x => x.TemperatureUnit, "F"));

    // Single dimming knob — the .is-stale class on the root.
    cut.Find(".sleep-forecast-pane").ClassList.Should().Contain("is-stale");

    // sync_problem glyph lives on the sub-line in v2 (was the footer in v1).
    var staleIcon = cut.Find(".sleep-forecast-stale-icon");
    staleIcon.TextContent.Trim().Should().Be("sync_problem");
    staleIcon.GetAttribute("aria-hidden").Should().Be("true");
  }

  [Fact]
  public void Pane_IsStale_SubLineRendersRelativeQualifier()
  {
    var cut = RenderComponent<SleepForecastPane>(p => p
      .Add(x => x.Forecast, BuildForecast(3, isStale: true))
      .Add(x => x.TemperatureUnit, "F"));

    // 25h-old forecast (BuildForecast stale path) — sub-line prepends
    // "yesterday at HH:mm" per spec §3 State B.
    var subline = cut.Find(".sleep-forecast-subline");
    subline.TextContent.Should().Contain("yesterday at");
  }

  [Fact]
  public void Pane_Fresh_DoesNotRenderStaleIcon()
  {
    var cut = RenderComponent<SleepForecastPane>(p => p
      .Add(x => x.Forecast, BuildForecast(3, isStale: false))
      .Add(x => x.TemperatureUnit, "F"));

    cut.FindAll(".sleep-forecast-stale-icon").Should().BeEmpty();
    cut.Find(".sleep-forecast-pane").ClassList.Should().NotContain("is-stale");
  }

  // ── Region 2 — forecast cards: partial-day matrix ───────────────────────

  [Fact]
  public void Pane_3Days_RendersThreeCards()
  {
    var cut = RenderComponent<SleepForecastPane>(p => p
      .Add(x => x.Forecast, BuildForecast(3))
      .Add(x => x.TemperatureUnit, "F"));

    cut.FindAll(".sleep-forecast-card").Should().HaveCount(3);
  }

  [Fact]
  public void Pane_2Days_RendersTwoCards()
  {
    var cut = RenderComponent<SleepForecastPane>(p => p
      .Add(x => x.Forecast, BuildForecast(2))
      .Add(x => x.TemperatureUnit, "F"));

    // Spec §3 State C — 2-day case shows 2 cards centered (CSS handles the
    // centering via justify-content). The card count is what we assert at
    // the component level.
    cut.FindAll(".sleep-forecast-card").Should().HaveCount(2);
    cut.Find(".sleep-forecast-cards").Should().NotBeNull();
  }

  [Fact]
  public void Pane_1Day_OmitsForecastRowEntirely()
  {
    var cut = RenderComponent<SleepForecastPane>(p => p
      .Add(x => x.Forecast, BuildForecast(1))
      .Add(x => x.TemperatureUnit, "F"));

    // Spec §3 State D — Region 2 is omitted entirely when only today is
    // available; a single forecast card duplicating today's data has no
    // comparative value. The primary block + sub-line carry all the
    // information.
    cut.FindAll(".sleep-forecast-cards").Should().BeEmpty();
    cut.FindAll(".sleep-forecast-card").Should().BeEmpty();

    // Primary block + sub-line still render — they're the entire output.
    cut.Find(".sleep-forecast-primary").Should().NotBeNull();
    cut.Find(".sleep-forecast-subline").Should().NotBeNull();
  }

  // ── Region 2 — card structure ───────────────────────────────────────────

  [Fact]
  public void Pane_Card_RendersIconDayHighAndLow_InFahrenheit()
  {
    var cut = RenderComponent<SleepForecastPane>(p => p
      .Add(x => x.Forecast, BuildForecast(3))
      .Add(x => x.TemperatureUnit, "F"));

    var firstCard = cut.FindAll(".sleep-forecast-card")[0];
    firstCard.QuerySelector(".sleep-forecast-card-icon").Should().NotBeNull();
    firstCard.QuerySelector(".sleep-forecast-card-day")!.TextContent.Trim()
      .Should().Be("Today");
    firstCard.QuerySelector(".sleep-forecast-card-temp-high")!.TextContent
      .Should().Contain("77");
    firstCard.QuerySelector(".sleep-forecast-card-temp-low")!.TextContent
      .Should().Contain("66");
  }

  [Fact]
  public void Pane_Card_RendersCelsius_WhenUnitIsC()
  {
    var cut = RenderComponent<SleepForecastPane>(p => p
      .Add(x => x.Forecast, BuildForecast(3))
      .Add(x => x.TemperatureUnit, "C"));

    var firstCard = cut.FindAll(".sleep-forecast-card")[0];
    firstCard.QuerySelector(".sleep-forecast-card-temp-high")!.TextContent
      .Should().Contain("25");
    firstCard.QuerySelector(".sleep-forecast-card-temp-low")!.TextContent
      .Should().Contain("19");
  }

  [Fact]
  public void Pane_Card_RendersBothNumerics_WhenUnitIsBoth_AndPaneCarriesUnitBothClass()
  {
    var cut = RenderComponent<SleepForecastPane>(p => p
      .Add(x => x.Forecast, BuildForecast(3))
      .Add(x => x.TemperatureUnit, "both"));

    // .unit-both modifier triggers the wider card column (spec §3 State F).
    cut.Find(".sleep-forecast-pane").ClassList.Should().Contain("unit-both");

    var firstCard = cut.FindAll(".sleep-forecast-card")[0];
    // Both F and C numerics appear on the card's high row.
    var high = firstCard.QuerySelector(".sleep-forecast-card-temp-high")!;
    high.ClassList.Should().Contain("sleep-forecast-card-temp-both");
    high.TextContent.Should().Contain("77");
    high.TextContent.Should().Contain("25");
  }

  // ── Icon mapping (preserved verbatim from v1) ───────────────────────────

  [Fact]
  public void Pane_Card_RendersMaterialSymbolForIconKey()
  {
    var cut = RenderComponent<SleepForecastPane>(p => p
      .Add(x => x.Forecast, BuildForecast(3))
      .Add(x => x.TemperatureUnit, "F"));

    // First card = today = "partly-cloudy" iconKey → "partly_cloudy_day".
    // Third card = Tuesday = "sunny" → "sunny".
    var cards = cut.FindAll(".sleep-forecast-card");
    cards[0].QuerySelector(".sleep-forecast-card-icon .material-symbols-rounded")!
      .TextContent.Trim().Should().Be("partly_cloudy_day");
    cards[2].QuerySelector(".sleep-forecast-card-icon .material-symbols-rounded")!
      .TextContent.Trim().Should().Be("sunny");
  }

  [Fact]
  public void Pane_PrimaryIcon_MatchesTodayCardIconSymbol()
  {
    // Spec §5: the same Material Symbol name MUST render at 96 px in the
    // primary block and at 48 px in today's card. The IconKeyToSymbol
    // helper is the single source of truth for both renderings.
    var cut = RenderComponent<SleepForecastPane>(p => p
      .Add(x => x.Forecast, BuildForecast(3))
      .Add(x => x.TemperatureUnit, "F"));

    var primary = cut.Find(".sleep-forecast-primary-icon .material-symbols-rounded")
      .TextContent.Trim();
    var todayCard = cut.FindAll(".sleep-forecast-card")[0]
      .QuerySelector(".sleep-forecast-card-icon .material-symbols-rounded")!
      .TextContent.Trim();

    primary.Should().Be(todayCard);
  }

  // ── Aria-live label ─────────────────────────────────────────────────────

  [Fact]
  public void Pane_AriaLabel_LeadsWithCurrentConditionAndTemperature()
  {
    var cut = RenderComponent<SleepForecastPane>(p => p
      .Add(x => x.Forecast, BuildForecast(3))
      .Add(x => x.TemperatureUnit, "F"));

    var pane = cut.Find(".sleep-forecast-pane");
    var ariaLabel = pane.GetAttribute("aria-label")!;

    // v2: the SR string mirrors the visual layout — primary block first,
    // then the day-by-day breakdown (spec §9).
    ariaLabel.Should().StartWith("Currently 77 degrees Fahrenheit");
    ariaLabel.Should().Contain("Partly Sunny");
    ariaLabel.Should().Contain("Pittsboro");
  }

  [Fact]
  public void Pane_AriaLabel_FlaggsStaleData_WhenStale()
  {
    var cut = RenderComponent<SleepForecastPane>(p => p
      .Add(x => x.Forecast, BuildForecast(3, isStale: true))
      .Add(x => x.TemperatureUnit, "F"));

    var pane = cut.Find(".sleep-forecast-pane");
    pane.GetAttribute("aria-label").Should().Contain("stale");
  }

  // ── Helpers ─────────────────────────────────────────────────────────────

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

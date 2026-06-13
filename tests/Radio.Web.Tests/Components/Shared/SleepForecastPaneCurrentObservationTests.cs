using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Radio.Core.Models;
using Radio.Web.Components.Shared;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// bUnit tests for the current-observation behavior of
/// <see cref="SleepForecastPane"/> per HANDOFF-sleep-weather-current-conditions.md
/// §5. Kept in a dedicated file so the v2 baseline tests in
/// <see cref="SleepForecastPaneTests"/> stay tightly focused on the v2
/// structural contract; this file owns the State G / State H matrix and the
/// fallback qualifier wiring.
/// </summary>
public class SleepForecastPaneCurrentObservationTests : TestContext
{
  public SleepForecastPaneCurrentObservationTests()
  {
    Services.AddRadzenComponents();
    JSInterop.Mode = JSRuntimeMode.Loose;
  }

  // ── Helpers ─────────────────────────────────────────────────────────────

  /// <summary>
  /// Build a fresh test forecast with N days. Day 0 is "Today" with the
  /// reference-image data (77/66, "Partly Sunny"); when <paramref name="current"/>
  /// is supplied the pane runs in State G (current = headline), otherwise
  /// State H (today = headline, fallback).
  /// </summary>
  private static WeatherForecast BuildForecast(
    int dayCount,
    bool forecastIsStale = false,
    CurrentObservation? current = null)
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
      GeneratedAtUtc: DateTime.UtcNow.AddHours(forecastIsStale ? -25 : -1),
      FetchedAtUtc: DateTime.UtcNow.AddMinutes(-1),
      IsStale: forecastIsStale,
      Days: days.Take(dayCount).ToList(),
      Current: current);
  }

  private static CurrentObservation BuildCurrent(
    int tempF = 48,
    int tempC = 9,
    string conditionShort = "Partly Cloudy",
    string iconKey = "partly-cloudy-night",
    bool isStale = false,
    DateTimeOffset? observedAtUtc = null)
  {
    return new CurrentObservation(
      TempF: tempF,
      TempC: tempC,
      ConditionShort: conditionShort,
      IconKey: iconKey,
      ObservedAtUtc: observedAtUtc ?? DateTime.UtcNow.AddMinutes(-15),
      IsStale: isStale);
  }

  // ── State G — Current present (the new default) ─────────────────────────

  [Fact]
  public void Renders_State_G_PrimaryUsesCurrentTempIconAndCondition()
  {
    var cut = RenderComponent<SleepForecastPane>(p => p
      .Add(x => x.Forecast, BuildForecast(3, current: BuildCurrent(tempF: 48, conditionShort: "Partly Cloudy", iconKey: "partly-cloudy-night")))
      .Add(x => x.TemperatureUnit, "F"));

    // Big number = Current.TempF (48), not Today.HighF (77).
    var temp = cut.Find(".sleep-forecast-primary-temp");
    temp.TextContent.Should().Contain("48");
    temp.TextContent.Should().NotContain("77");

    // Condition = Current.ConditionShort.
    cut.Find(".sleep-forecast-primary-condition")
      .TextContent.Trim().Should().Be("Partly Cloudy");

    // Icon = Current.IconKey → partly_cloudy_night material symbol.
    cut.Find(".sleep-forecast-primary-icon .material-symbols-rounded")
      .TextContent.Trim().Should().Be("partly_cloudy_night");
  }

  [Fact]
  public void Renders_State_G_SupplementaryTodayLine_IsVisible()
  {
    var cut = RenderComponent<SleepForecastPane>(p => p
      .Add(x => x.Forecast, BuildForecast(3, current: BuildCurrent()))
      .Add(x => x.TemperatureUnit, "F"));

    // Supplementary slab is present and contains today's H/L (77 / 66).
    var slab = cut.Find(".sleep-forecast-primary-supplementary");
    slab.Should().NotBeNull();
    cut.Find(".sleep-forecast-supplementary-label").TextContent.Trim().Should().Be("Today");
    cut.Find(".sleep-forecast-supplementary-high").TextContent.Trim().Should().Be("77");
    cut.Find(".sleep-forecast-supplementary-low").TextContent.Trim().Should().Be("66");
  }

  [Fact]
  public void Renders_State_G_V2HLLineIsHidden()
  {
    // When Current is present, the v2 28 px H/L line MUST NOT render — the
    // supplementary slab carries today's H/L instead (avoids duplication).
    var cut = RenderComponent<SleepForecastPane>(p => p
      .Add(x => x.Forecast, BuildForecast(3, current: BuildCurrent()))
      .Add(x => x.TemperatureUnit, "F"));

    cut.FindAll(".sleep-forecast-primary-hl").Should().BeEmpty(
      "the v2 28 px H/L line must be replaced by the supplementary slab when Current is present");
  }

  [Fact]
  public void Renders_State_G_NoFallbackQualifierOnSubLine()
  {
    var cut = RenderComponent<SleepForecastPane>(p => p
      .Add(x => x.Forecast, BuildForecast(3, current: BuildCurrent()))
      .Add(x => x.TemperatureUnit, "F"));

    cut.FindAll(".sleep-forecast-subline-fallback").Should().BeEmpty(
      "the ' · forecast only' qualifier is for the fallback state only");
  }

  // ── State H — Current null (fallback to v2) ─────────────────────────────

  [Fact]
  public void Renders_State_H_PrimaryFallsBackToTodayForecast()
  {
    var cut = RenderComponent<SleepForecastPane>(p => p
      .Add(x => x.Forecast, BuildForecast(3, current: null))
      .Add(x => x.TemperatureUnit, "F"));

    // Big number = Today.HighF (77) — v2 fallback.
    var temp = cut.Find(".sleep-forecast-primary-temp");
    temp.TextContent.Should().Contain("77");

    cut.Find(".sleep-forecast-primary-condition")
      .TextContent.Trim().Should().Be("Partly Sunny");

    cut.Find(".sleep-forecast-primary-icon .material-symbols-rounded")
      .TextContent.Trim().Should().Be("partly_cloudy_day");
  }

  [Fact]
  public void Renders_State_H_SupplementarySlabIsHidden_V2HLLineReturns()
  {
    var cut = RenderComponent<SleepForecastPane>(p => p
      .Add(x => x.Forecast, BuildForecast(3, current: null))
      .Add(x => x.TemperatureUnit, "F"));

    cut.FindAll(".sleep-forecast-primary-supplementary").Should().BeEmpty(
      "the supplementary 'Today' slab must NOT render when the primary IS today's forecast — would be redundant");

    // v2 28 px H/L line returns in its original position.
    var hl = cut.Find(".sleep-forecast-primary-hl");
    hl.QuerySelector(".sleep-forecast-primary-high")!.TextContent.Trim().Should().Be("77");
    hl.QuerySelector(".sleep-forecast-primary-low")!.TextContent.Trim().Should().Be("66");
  }

  [Fact]
  public void Renders_State_H_SubLineGainsForecastOnlyQualifier()
  {
    var cut = RenderComponent<SleepForecastPane>(p => p
      .Add(x => x.Forecast, BuildForecast(3, current: null))
      .Add(x => x.TemperatureUnit, "F"));

    var fallbackSpan = cut.Find(".sleep-forecast-subline-fallback");
    fallbackSpan.TextContent.Should().Contain("forecast only");
    fallbackSpan.TextContent.Should().StartWith(" · ", "the qualifier must be preceded by the ' · ' separator");
  }

  // ── Stale state — IsStale on Current vs Forecast ────────────────────────

  [Fact]
  public void Pane_HasStaleClass_WhenCurrentIsStale()
  {
    var cut = RenderComponent<SleepForecastPane>(p => p
      .Add(x => x.Forecast, BuildForecast(3, forecastIsStale: false,
          current: BuildCurrent(isStale: true, observedAtUtc: DateTimeOffset.UtcNow.AddHours(-3))))
      .Add(x => x.TemperatureUnit, "F"));

    // Stale on Current alone triggers the existing .is-stale + sync_problem
    // treatment — single dimming knob (HANDOFF §5.1).
    cut.Find(".sleep-forecast-pane").ClassList.Should().Contain("is-stale");
    cut.Find(".sleep-forecast-stale-icon").TextContent.Trim().Should().Be("sync_problem");
  }

  [Fact]
  public void Pane_HasStaleClass_WhenForecastIsStaleAndCurrentNull()
  {
    var cut = RenderComponent<SleepForecastPane>(p => p
      .Add(x => x.Forecast, BuildForecast(3, forecastIsStale: true, current: null))
      .Add(x => x.TemperatureUnit, "F"));

    cut.Find(".sleep-forecast-pane").ClassList.Should().Contain("is-stale");

    // Both the relative-time stale qualifier AND the " · forecast only"
    // qualifier should appear in the sub-line — they cover orthogonal
    // failure modes (forecast freshness vs. observation availability).
    var subline = cut.Find(".sleep-forecast-subline");
    subline.TextContent.Should().Contain("as of");
    subline.TextContent.Should().Contain("yesterday");
    cut.Find(".sleep-forecast-subline-fallback").TextContent.Should().Contain("forecast only");
  }

  // ── Unit-mode handling ──────────────────────────────────────────────────

  [Fact]
  public void Switches_BigNumber_ToCelsius_WhenUnitC_AndCurrentPresent()
  {
    var cut = RenderComponent<SleepForecastPane>(p => p
      .Add(x => x.Forecast, BuildForecast(3, current: BuildCurrent(tempF: 48, tempC: 9)))
      .Add(x => x.TemperatureUnit, "C"));

    // Big number = Current.TempC (9), not TempF (48).
    var temp = cut.Find(".sleep-forecast-primary-temp");
    temp.TextContent.Should().Contain("9");
    temp.TextContent.Should().NotContain("48");

    // Supplementary H/L uses Today.HighC/LowC (25/19).
    cut.Find(".sleep-forecast-supplementary-high").TextContent.Trim().Should().Be("25");
    cut.Find(".sleep-forecast-supplementary-low").TextContent.Trim().Should().Be("19");
  }

  [Fact]
  public void BigNumber_FallsBackToFahrenheit_InBothMode_WithCurrentPresent()
  {
    // HANDOFF §5.2 + §8: "both" mode keeps the primary numeral Fahrenheit-
    // only to avoid blowing the column budget. The supplementary "Today"
    // line also stays F.
    var cut = RenderComponent<SleepForecastPane>(p => p
      .Add(x => x.Forecast, BuildForecast(3, current: BuildCurrent(tempF: 48, tempC: 9)))
      .Add(x => x.TemperatureUnit, "both"));

    var temp = cut.Find(".sleep-forecast-primary-temp");
    temp.TextContent.Should().Contain("48"); // TempF
    temp.TextContent.Should().NotContain("9°"); // TempC must NOT appear on the primary

    // Supplementary H/L also stays Fahrenheit-only.
    cut.Find(".sleep-forecast-supplementary-high").TextContent.Trim().Should().Be("77");
    cut.Find(".sleep-forecast-supplementary-low").TextContent.Trim().Should().Be("66");

    // Pane carries .unit-both so the card column rule still wakes up.
    cut.Find(".sleep-forecast-pane").ClassList.Should().Contain("unit-both");
  }

  // ── Aria label leads with Current when available ────────────────────────

  [Fact]
  public void AriaLabel_LeadsWithCurrent_WhenAvailable()
  {
    var cut = RenderComponent<SleepForecastPane>(p => p
      .Add(x => x.Forecast, BuildForecast(3, current: BuildCurrent(tempF: 48, conditionShort: "Partly Cloudy")))
      .Add(x => x.TemperatureUnit, "F"));

    var ariaLabel = cut.Find(".sleep-forecast-pane").GetAttribute("aria-label")!;

    ariaLabel.Should().StartWith("Currently 48 degrees Fahrenheit, Partly Cloudy in Pittsboro, NC.");
    ariaLabel.Should().Contain("Today's high 77, low 66.");
  }

  [Fact]
  public void AriaLabel_LeadsWithTodayForecast_WhenCurrentIsNull()
  {
    var cut = RenderComponent<SleepForecastPane>(p => p
      .Add(x => x.Forecast, BuildForecast(3, current: null))
      .Add(x => x.TemperatureUnit, "F"));

    var ariaLabel = cut.Find(".sleep-forecast-pane").GetAttribute("aria-label")!;

    // v2 fallback contract preserved: leads with "Currently 77 degrees …
    // Partly Sunny in Pittsboro" followed by the per-day breakdown.
    ariaLabel.Should().StartWith("Currently 77 degrees Fahrenheit, Partly Sunny in Pittsboro, NC.");
  }

  // ── 1-day fallback + Current present ────────────────────────────────────

  [Fact]
  public void Renders_WithoutRegion2_When_SingleDay_AndCurrentPresent()
  {
    var cut = RenderComponent<SleepForecastPane>(p => p
      .Add(x => x.Forecast, BuildForecast(1, current: BuildCurrent()))
      .Add(x => x.TemperatureUnit, "F"));

    // Region 2 omitted (HANDOFF §5.1 State I — preserves the v2 1-day fallback).
    cut.FindAll(".sleep-forecast-cards").Should().BeEmpty();
    cut.FindAll(".sleep-forecast-card").Should().BeEmpty();

    // Primary block still in State G: Current = headline, supplementary
    // "Today" slab visible.
    cut.Find(".sleep-forecast-primary").Should().NotBeNull();
    cut.Find(".sleep-forecast-primary-temp").TextContent.Should().Contain("48");
    cut.Find(".sleep-forecast-primary-supplementary").Should().NotBeNull();
  }
}

using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Radio.Web.Components.Shared;
using Radio.Web.Formatting;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// bUnit tests for the <see cref="MetricTile"/> card introduced by PR 4
/// of the design tightening arc (handoff §P0·4).
///
/// Covers:
///   * Category / Name / unit-aware Value rendering.
///   * Threshold-band color logic (warn / critical, normal + inverted).
///   * Sparkline rendered only when Series is provided.
///   * The fixed-bug cases from the screenshot audit (Memory Usage Mb
///     reads as MB, Signal Strength as %, Latency Ms as ms/s, etc.).
/// </summary>
public class MetricTileTests : TestContext
{
  public MetricTileTests()
  {
    Services.AddRadzenComponents();
    JSInterop.Mode = JSRuntimeMode.Loose;
  }

  [Fact]
  public void MetricTile_RendersCategory_Name_AndUnitFormattedValue()
  {
    var cut = RenderComponent<MetricTile>(p => p
      .Add(x => x.Category, "System · Memory")
      .Add(x => x.Name, "Heap in use")
      .Add(x => x.Value, 850)
      .Add(x => x.Unit, Units.Megabytes));

    cut.Find(".metric-tile-category").TextContent.Trim().Should().Be("System · Memory");
    cut.Find(".metric-tile-name").TextContent.Trim().Should().Be("Heap in use");
    cut.Find(".metric-tile-value").TextContent.Trim().Should().Be("850 MB");
  }

  [Fact]
  public void MetricTile_MemoryBugRegression_ShowsMBNotPercent()
  {
    // Regression for handoff §P0·4: Memory Usage Mb tile previously
    // rendered as "850.4%". Must read "850 MB".
    var cut = RenderComponent<MetricTile>(p => p
      .Add(x => x.Category, "System · Memory")
      .Add(x => x.Name, "Memory Usage Mb")
      .Add(x => x.Value, 850)
      .Add(x => x.Unit, Units.Megabytes));

    var value = cut.Find(".metric-tile-value").TextContent.Trim();
    value.Should().Be("850 MB");
    value.Should().NotContain("%");
  }

  [Fact]
  public void MetricTile_SignalStrength_RendersAsPercent()
  {
    var cut = RenderComponent<MetricTile>(p => p
      .Add(x => x.Category, "Radio · Signal")
      .Add(x => x.Name, "Signal Strength")
      .Add(x => x.Value, 65)
      .Add(x => x.Unit, Units.Percent));

    cut.Find(".metric-tile-value").TextContent.Trim().Should().Be("65%");
  }

  [Fact]
  public void MetricTile_LatencyMs_BelowOneSecond_RendersMs()
  {
    var cut = RenderComponent<MetricTile>(p => p
      .Add(x => x.Category, "Radio · Latency")
      .Add(x => x.Name, "Latency Ms")
      .Add(x => x.Value, 250)
      .Add(x => x.Unit, Units.Milliseconds));

    cut.Find(".metric-tile-value").TextContent.Trim().Should().Be("250 ms");
  }

  [Fact]
  public void MetricTile_LatencyMs_AboveOneSecond_AutoPromotesToSeconds()
  {
    var cut = RenderComponent<MetricTile>(p => p
      .Add(x => x.Category, "Radio · Latency")
      .Add(x => x.Name, "Latency Ms")
      .Add(x => x.Value, 1200)
      .Add(x => x.Unit, Units.Milliseconds));

    cut.Find(".metric-tile-value").TextContent.Trim().Should().Be("1.2 s");
  }

  [Fact]
  public void MetricTile_FrequencyChanges_AsCount_IsThousandsSeparated()
  {
    var cut = RenderComponent<MetricTile>(p => p
      .Add(x => x.Category, "Radio · Tuner")
      .Add(x => x.Name, "Frequency Changes")
      .Add(x => x.Value, 12345)
      .Add(x => x.Unit, Units.Count));

    var value = cut.Find(".metric-tile-value").TextContent.Trim();
    value.Should().BeOneOf("12,345", "12.345"); // tolerate culture difference
  }

  [Fact]
  public void MetricTile_NoThresholds_UsesTextHighColor()
  {
    var cut = RenderComponent<MetricTile>(p => p
      .Add(x => x.Category, "X")
      .Add(x => x.Name, "Y")
      .Add(x => x.Value, 50)
      .Add(x => x.Unit, Units.Bare));

    var style = cut.Find(".metric-tile-value").GetAttribute("style") ?? string.Empty;
    style.Should().Contain("--text-high");
  }

  [Fact]
  public void MetricTile_BelowWarn_UsesSignalGreenColor()
  {
    var cut = RenderComponent<MetricTile>(p => p
      .Add(x => x.Category, "X")
      .Add(x => x.Name, "Y")
      .Add(x => x.Value, 30)
      .Add(x => x.Unit, Units.Percent)
      .Add(x => x.Warn, 80.0)
      .Add(x => x.Critical, 95.0));

    var style = cut.Find(".metric-tile-value").GetAttribute("style") ?? string.Empty;
    style.Should().Contain("--signal-green");
  }

  [Fact]
  public void MetricTile_AtOrAboveWarn_BelowCritical_UsesSignalAmberColor()
  {
    var cut = RenderComponent<MetricTile>(p => p
      .Add(x => x.Category, "X")
      .Add(x => x.Name, "Y")
      .Add(x => x.Value, 85)
      .Add(x => x.Unit, Units.Percent)
      .Add(x => x.Warn, 80.0)
      .Add(x => x.Critical, 95.0));

    var style = cut.Find(".metric-tile-value").GetAttribute("style") ?? string.Empty;
    style.Should().Contain("--signal-amber");
  }

  [Fact]
  public void MetricTile_AtOrAboveCritical_UsesSignalRedColor()
  {
    var cut = RenderComponent<MetricTile>(p => p
      .Add(x => x.Category, "X")
      .Add(x => x.Name, "Y")
      .Add(x => x.Value, 96)
      .Add(x => x.Unit, Units.Percent)
      .Add(x => x.Warn, 80.0)
      .Add(x => x.Critical, 95.0));

    var style = cut.Find(".metric-tile-value").GetAttribute("style") ?? string.Empty;
    style.Should().Contain("--signal-red");
  }

  [Fact]
  public void MetricTile_InvertedThresholds_LowValuesRenderRed()
  {
    // Buffer-fill style: low values are bad. value=10, critical=20 → red.
    var cut = RenderComponent<MetricTile>(p => p
      .Add(x => x.Category, "Audio · Buffer")
      .Add(x => x.Name, "Buffer Fill")
      .Add(x => x.Value, 10)
      .Add(x => x.Unit, Units.Percent)
      .Add(x => x.Warn, 50.0)
      .Add(x => x.Critical, 20.0)
      .Add(x => x.InvertThresholds, true));

    var style = cut.Find(".metric-tile-value").GetAttribute("style") ?? string.Empty;
    style.Should().Contain("--signal-red");
  }

  [Fact]
  public void MetricTile_NoSeries_DoesNotRenderSparkline()
  {
    var cut = RenderComponent<MetricTile>(p => p
      .Add(x => x.Category, "X")
      .Add(x => x.Name, "Y")
      .Add(x => x.Value, 50)
      .Add(x => x.Unit, Units.Bare));

    cut.FindAll(".metric-tile-spark").Should().BeEmpty();
    cut.FindAll("svg").Should().BeEmpty();
  }

  [Fact]
  public void MetricTile_WithSeries_RendersSparkline()
  {
    var cut = RenderComponent<MetricTile>(p => p
      .Add(x => x.Category, "X")
      .Add(x => x.Name, "Y")
      .Add(x => x.Value, 50)
      .Add(x => x.Unit, Units.Bare)
      .Add(x => x.Series, new double[] { 10, 20, 30, 40, 50 }));

    cut.FindAll(".metric-tile-spark").Count.Should().Be(1);
    cut.FindAll("svg").Count.Should().Be(1);
  }

  [Fact]
  public void MetricTile_KeyAttribute_PropagatesAsDataAttribute()
  {
    var cut = RenderComponent<MetricTile>(p => p
      .Add(x => x.Category, "X")
      .Add(x => x.Name, "Y")
      .Add(x => x.Value, 1)
      .Add(x => x.Unit, Units.Bare)
      .Add(x => x.Key, "system.memory_usage_mb"));

    cut.Find(".metric-tile").GetAttribute("data-metric-key").Should().Be("system.memory_usage_mb");
  }
}

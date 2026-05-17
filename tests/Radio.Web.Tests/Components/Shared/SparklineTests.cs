using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Radio.Web.Components.Shared;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// bUnit tests for the <see cref="Sparkline"/> SVG mini-chart introduced
/// by PR 4 of the design tightening arc (handoff §P0·4).
///
/// Asserts the SVG shell renders, path generation rules for the empty
/// / single-value / multi-value cases, and that the Stroke parameter
/// is propagated to both the line and the filled-area paths.
/// </summary>
public class SparklineTests : TestContext
{
  public SparklineTests()
  {
    Services.AddRadzenComponents();
    JSInterop.Mode = JSRuntimeMode.Loose;
  }

  [Fact]
  public void Sparkline_RendersRootSvg_WithCorrectViewBox()
  {
    var cut = RenderComponent<Sparkline>(p => p
      .Add(x => x.Values, new double[] { 1, 2, 3 }));

    var svg = cut.Find("svg");
    svg.GetAttribute("viewBox").Should().Be("0 0 120 28");
    svg.GetAttribute("preserveAspectRatio").Should().Be("none");
  }

  [Fact]
  public void Sparkline_EmptyValues_RenderEmptySvg()
  {
    var cut = RenderComponent<Sparkline>(p => p
      .Add(x => x.Values, Array.Empty<double>()));

    cut.FindAll("path").Should().BeEmpty();
  }

  [Fact]
  public void Sparkline_NullValues_RenderEmptySvg()
  {
    var cut = RenderComponent<Sparkline>(p => p
      .Add(x => x.Values, null!));

    cut.FindAll("path").Should().BeEmpty();
  }

  [Fact]
  public void Sparkline_SingleValue_RendersFlatLineAtMidHeight()
  {
    var cut = RenderComponent<Sparkline>(p => p
      .Add(x => x.Values, new double[] { 42 }));

    var paths = cut.FindAll("path");
    paths.Count.Should().Be(2); // area + line

    // The line path should be a flat segment at y == ViewHeight/2 == 14.
    var line = paths[1].GetAttribute("d") ?? string.Empty;
    line.Should().Contain("14");
    line.Should().StartWith("M0,14");
  }

  [Fact]
  public void Sparkline_FivePointSeries_GeneratesFivePointPath()
  {
    var values = new double[] { 0, 25, 50, 75, 100 };
    var cut = RenderComponent<Sparkline>(p => p
      .Add(x => x.Values, values));

    var paths = cut.FindAll("path");
    paths.Count.Should().Be(2);

    var line = paths[1].GetAttribute("d") ?? string.Empty;
    // Five points → "M" + four " L" segments.
    line.Should().StartWith("M");
    var lCount = line.Split(" L", StringSplitOptions.None).Length - 1;
    lCount.Should().Be(4);
  }

  [Fact]
  public void Sparkline_FivePointSeries_FirstPointAtLeftEdge_LastAtRightEdge()
  {
    var values = new double[] { 0, 25, 50, 75, 100 };
    var cut = RenderComponent<Sparkline>(p => p
      .Add(x => x.Values, values));

    var line = cut.FindAll("path")[1].GetAttribute("d") ?? string.Empty;

    // First moveTo should be at x = 0; last point should be near x = 120 (ViewWidth).
    line.Should().StartWith("M0,");
    // The last L segment should include "120,"
    line.Should().Contain("120,");
  }

  [Fact]
  public void Sparkline_NormalizesValues_TopAndBottomOfRange()
  {
    // With a five-point ramp the minimum (0) should map to ViewHeight (28)
    // and the maximum (100) to 0 (SVG y is inverted relative to data).
    var values = new double[] { 0, 25, 50, 75, 100 };
    var cut = RenderComponent<Sparkline>(p => p
      .Add(x => x.Values, values));

    var line = cut.FindAll("path")[1].GetAttribute("d") ?? string.Empty;
    // First point (value 0) → y = 28; rendered as "M0,28".
    line.Should().StartWith("M0,28");
    // Last point (value 100) → y = 0; expect ",0" anywhere after the last L.
    var lastL = line.LastIndexOf(" L", StringComparison.Ordinal);
    var lastSegment = line[(lastL + 2)..];
    lastSegment.Should().EndWith(",0");
  }

  [Fact]
  public void Sparkline_AllEqualValues_RenderFlatLineAtMidHeight()
  {
    // Range == 0 case — the path generator falls back to mid-height.
    var values = new double[] { 5, 5, 5, 5 };
    var cut = RenderComponent<Sparkline>(p => p
      .Add(x => x.Values, values));

    var line = cut.FindAll("path")[1].GetAttribute("d") ?? string.Empty;
    line.Should().Contain(",14");
    // All four points should land at y = 14.
    line.Split(',').Where(s => s.StartsWith("14")).Count().Should().BeGreaterThanOrEqualTo(2);
  }

  [Fact]
  public void Sparkline_PropagatesStrokeToBothPaths()
  {
    var cut = RenderComponent<Sparkline>(p => p
      .Add(x => x.Values, new double[] { 1, 2, 3 })
      .Add(x => x.Stroke, "var(--signal-red)"));

    var paths = cut.FindAll("path");
    paths[0].GetAttribute("fill").Should().Be("var(--signal-red)"); // area
    paths[1].GetAttribute("stroke").Should().Be("var(--signal-red)"); // line
  }

  [Fact]
  public void Sparkline_DefaultStroke_IsCurrentColor()
  {
    var cut = RenderComponent<Sparkline>(p => p
      .Add(x => x.Values, new double[] { 1, 2 }));

    cut.FindAll("path")[1].GetAttribute("stroke").Should().Be("currentColor");
  }
}

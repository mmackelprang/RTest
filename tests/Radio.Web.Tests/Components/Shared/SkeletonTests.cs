using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Radio.Web.Components.Shared;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// bUnit tests for the <see cref="Skeleton"/> shape-aware placeholder
/// introduced by PR 4 of the design tightening arc (handoff §P1·5).
///
/// Each test asserts the structural fingerprint of one Shape value so a
/// future refactor that accidentally collapses two shapes into one would
/// trip the test. We do not assert exact CSS or pixel sizing here — the
/// design-system stylesheet owns the visual concerns; the component only
/// owns the markup arrangement.
/// </summary>
public class SkeletonTests : TestContext
{
  public SkeletonTests()
  {
    Services.AddRadzenComponents();
    JSInterop.Mode = JSRuntimeMode.Loose;
  }

  [Fact]
  public void Skeleton_NowPlaying_RendersArt_Heading_Text_Progress()
  {
    var cut = RenderComponent<Skeleton>(p => p
      .Add(x => x.Shape, Skeleton.SkeletonShape.NowPlaying));

    var root = cut.Find(".skeleton");
    root.GetAttribute("class").Should().Contain("skeleton-now-playing");
    cut.FindAll(".skeleton-art").Count.Should().Be(1);
    cut.FindAll(".skeleton-heading").Count.Should().Be(1);
    cut.FindAll(".skeleton-text").Count.Should().BeGreaterThanOrEqualTo(1);
    cut.FindAll(".skeleton-progress").Count.Should().Be(1);
  }

  [Fact]
  public void Skeleton_Radio_RendersBands_FreqWell_Meter()
  {
    var cut = RenderComponent<Skeleton>(p => p
      .Add(x => x.Shape, Skeleton.SkeletonShape.Radio));

    cut.Find(".skeleton").GetAttribute("class").Should().Contain("skeleton-radio");
    cut.FindAll(".skeleton-band").Count.Should().Be(4);
    cut.FindAll(".skeleton-freq-well").Count.Should().Be(1);
    cut.FindAll(".skeleton-meter").Count.Should().Be(1);
  }

  [Fact]
  public void Skeleton_ListRow_RendersThumb_TwoTextLines()
  {
    var cut = RenderComponent<Skeleton>(p => p
      .Add(x => x.Shape, Skeleton.SkeletonShape.ListRow));

    cut.Find(".skeleton").GetAttribute("class").Should().Contain("skeleton-list-row");
    cut.FindAll(".skeleton-thumb").Count.Should().Be(1);
    cut.FindAll(".skeleton-text").Count.Should().Be(2);
    cut.FindAll(".skeleton-circle").Should().BeEmpty(); // distinct from DeviceRow
  }

  [Fact]
  public void Skeleton_DeviceRow_RendersCircle_TextLines_ActionPill()
  {
    var cut = RenderComponent<Skeleton>(p => p
      .Add(x => x.Shape, Skeleton.SkeletonShape.DeviceRow));

    cut.Find(".skeleton").GetAttribute("class").Should().Contain("skeleton-device-row");
    cut.FindAll(".skeleton-circle").Count.Should().Be(1);
    cut.FindAll(".skeleton-text").Count.Should().Be(2);
    cut.FindAll(".skeleton-action-pill").Count.Should().Be(1);
    cut.FindAll(".skeleton-thumb").Should().BeEmpty(); // distinct from ListRow
  }

  [Fact]
  public void Skeleton_MetricTile_RendersCategoryNameValueAndSpark()
  {
    var cut = RenderComponent<Skeleton>(p => p
      .Add(x => x.Shape, Skeleton.SkeletonShape.MetricTile));

    cut.Find(".skeleton").GetAttribute("class").Should().Contain("skeleton-metric-tile");
    cut.FindAll(".skeleton-text").Count.Should().BeGreaterThanOrEqualTo(3);
    cut.FindAll(".skeleton-spark").Count.Should().Be(1);
  }

  [Fact]
  public void Skeleton_Visualizer_RendersCanvasAndAxisTicks()
  {
    var cut = RenderComponent<Skeleton>(p => p
      .Add(x => x.Shape, Skeleton.SkeletonShape.Visualizer));

    cut.Find(".skeleton").GetAttribute("class").Should().Contain("skeleton-visualizer");
    cut.FindAll(".skeleton-viz-canvas").Count.Should().Be(1);
    cut.FindAll(".skeleton-viz-axis").Count.Should().Be(1);
    cut.FindAll(".skeleton-axis-tick").Count.Should().Be(4);
  }

  [Fact]
  public void Skeleton_AllShapes_ProduceDistinctRootClasses()
  {
    // Cheap structural smoke test — render every shape and assert their
    // root class differs. Catches accidental enum→class collapses.
    var classes = new HashSet<string>();
    foreach (Skeleton.SkeletonShape shape in Enum.GetValues(typeof(Skeleton.SkeletonShape)))
    {
      var cut = RenderComponent<Skeleton>(p => p.Add(x => x.Shape, shape));
      var c = cut.Find(".skeleton").GetAttribute("class") ?? string.Empty;
      classes.Add(c).Should().BeTrue($"shape {shape} must have a distinct root class");
    }
  }

  [Fact]
  public void Skeleton_AriaBusyAndRole_IsSetForAccessibility()
  {
    var cut = RenderComponent<Skeleton>(p => p
      .Add(x => x.Shape, Skeleton.SkeletonShape.ListRow));

    var root = cut.Find(".skeleton");
    root.GetAttribute("role").Should().Be("status");
    root.GetAttribute("aria-busy").Should().Be("true");
  }
}

using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Radio.Web.Components.Shared;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// bUnit tests for the <see cref="RdsCard"/> widget introduced by PR 3 of the
/// Radio Controller Polish arc. The card mounts above the frequency well in
/// <c>RadioControlPanel</c>; it renders nothing at all when no station name is
/// present (so the layout collapses gracefully on AM/SW or before RDS confirms
/// the PS field), and renders the PTY chip only when supplied.
/// </summary>
public class RdsCardTests : TestContext
{
  public RdsCardTests()
  {
    Services.AddRadzenComponents();
    JSInterop.Mode = JSRuntimeMode.Loose;
  }

  [Fact]
  public void RdsCard_RendersNothing_WhenStationNameNull()
  {
    var cut = RenderComponent<RdsCard>(p => p
      .Add(x => x.StationName, null));

    // No .rds-card root in DOM — the card collapses entirely so the
    // surrounding layout doesn't have to skirt an empty box.
    Assert.Empty(cut.FindAll(".rds-card"));
  }

  [Fact]
  public void RdsCard_RendersNothing_WhenStationNameEmpty()
  {
    var cut = RenderComponent<RdsCard>(p => p
      .Add(x => x.StationName, string.Empty));

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
}

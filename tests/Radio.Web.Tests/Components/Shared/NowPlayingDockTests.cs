using Bunit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Radzen;
using Radio.Web.Components.Shared;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// bUnit tests for the <see cref="NowPlayingDock"/> persistent player strip
/// introduced by PR 5 of the design tightening arc (handoff §P1·1).
///
/// The dock renders even with no API server reachable — the initial refresh
/// just swallows the connection-refused exception and leaves the component
/// in its empty-state shape. We exercise:
///
/// <list type="bullet">
///   <item>Structural fingerprint: art block, metadata block, progress bar, controls.</item>
///   <item>The metadata column declares <c>min-width: 200px</c> via CSS class
///         so a track change doesn't cause horizontal layout shift.</item>
///   <item>Progress bar carries the ARIA role + min/max/now attributes so the
///         screen reader can announce playback position.</item>
///   <item>Controls expose individual <c>aria-label</c>s for the three actions.</item>
/// </list>
///
/// We do NOT exercise live SignalR pushes here — the hub never connects in the
/// test rig, so we can't simulate a server-initiated NowPlayingChanged event
/// without a custom fake. The empty-state asserts are sufficient to lock the
/// rendering contract; richer interaction tests would belong in an E2E run.
/// </summary>
public class NowPlayingDockTests : TestContext
{
  private readonly ILoggerFactory _loggerFactory;

  public NowPlayingDockTests()
  {
    _loggerFactory = new NullLoggerFactory();
    JSInterop.Mode = JSRuntimeMode.Loose;

    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        { "ApiBaseUrl", "http://localhost:5000" }
      })
      .Build();

    Services.AddSingleton<IConfiguration>(configuration);
    Services.AddSingleton(_loggerFactory);
    Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

    Services.AddRadzenComponents();

    Services.AddHttpClient<AudioApiService>();

    Services.AddSingleton(sp =>
      new AudioStateHubService(
        NullLogger<AudioStateHubService>.Instance,
        sp.GetRequiredService<IConfiguration>()
      )
    );
  }

  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      _loggerFactory?.Dispose();
    }
    base.Dispose(disposing);
  }

  [Fact]
  public void NowPlayingDock_RendersRootContainer()
  {
    var cut = RenderComponent<NowPlayingDock>();
    cut.FindAll(".now-playing-dock").Count.Should().Be(1);
  }

  [Fact]
  public void NowPlayingDock_RendersAllSevenColumns()
  {
    // The grid template defined by §N expects: art → text → bars/placeholder
    // → elapsed → progress → total → controls. We assert one of each is in DOM.
    var cut = RenderComponent<NowPlayingDock>();
    cut.FindAll(".now-playing-dock-art").Count.Should().Be(1);
    cut.FindAll(".now-playing-dock-text").Count.Should().Be(1);
    cut.FindAll(".now-playing-dock-elapsed").Count.Should().Be(1);
    cut.FindAll(".now-playing-dock-progress").Count.Should().Be(1);
    cut.FindAll(".now-playing-dock-total").Count.Should().Be(1);
    cut.FindAll(".now-playing-dock-controls").Count.Should().Be(1);
  }

  [Fact]
  public void NowPlayingDock_EmptyState_ShowsBarPlaceholder_NotEqBars()
  {
    // With no SignalR data, IsPlaying defaults to false. The dock renders the
    // bars-placeholder block (preserves grid width) instead of the EQ animation.
    var cut = RenderComponent<NowPlayingDock>();
    cut.FindAll(".now-playing-dock-bars-placeholder").Count.Should().Be(1);
    cut.FindAll(".now-playing-dock-bars").Count.Should().Be(0);
  }

  [Fact]
  public void NowPlayingDock_EmptyState_DisplaysNoTrackPlaying()
  {
    // The empty-state title surfaces the friendly placeholder from
    // DisplayNames.Track / NowPlayingPanel parity ("No Track Playing").
    var cut = RenderComponent<NowPlayingDock>();
    cut.Markup.Should().Contain("No Track Playing");
  }

  [Fact]
  public void NowPlayingDock_EmptyState_DisplaysElapsedZeroAndTotalDash()
  {
    // Without a position payload, elapsed shows "0:00" and total shows the
    // canonical em-dash so the layout doesn't flicker to "—:—" on load.
    var cut = RenderComponent<NowPlayingDock>();
    cut.Find(".now-playing-dock-elapsed").TextContent.Trim().Should().Be("0:00");
    cut.Find(".now-playing-dock-total").TextContent.Trim().Should().Be("—");
  }

  [Fact]
  public void NowPlayingDock_ProgressBar_HasZeroPercentWidth_ByDefault()
  {
    // No duration → no progress. The bar must still render so the column slot
    // is occupied, but its inner element should be 0% wide.
    var cut = RenderComponent<NowPlayingDock>();
    var bar = cut.Find(".now-playing-dock-progress-bar");
    bar.GetAttribute("style").Should().Contain("width: 0");
  }

  [Fact]
  public void NowPlayingDock_ProgressBar_DeclaresAriaRoleAndRange()
  {
    // Screen-reader announcement requires role=progressbar plus min/max/now.
    var cut = RenderComponent<NowPlayingDock>();
    var progress = cut.Find(".now-playing-dock-progress");
    progress.GetAttribute("role").Should().Be("progressbar");
    progress.GetAttribute("aria-valuemin").Should().Be("0");
    progress.HasAttribute("aria-valuemax").Should().BeTrue();
    progress.HasAttribute("aria-valuenow").Should().BeTrue();
  }

  [Fact]
  public void NowPlayingDock_Controls_HaveDistinctAriaLabels()
  {
    // Each transport button advertises its action so a kiosk user with
    // screen-reader assistance can choose between prev / play-pause / next.
    var cut = RenderComponent<NowPlayingDock>();
    var buttons = cut.FindAll(".now-playing-dock-btn");
    buttons.Count.Should().Be(3);

    var labels = buttons
      .Select(b => b.GetAttribute("aria-label") ?? string.Empty)
      .ToList();
    labels.Should().Contain("Previous track");
    // Empty state → IsPlaying=false → button label reads "Play".
    labels.Should().Contain("Play");
    labels.Should().Contain("Next track");
  }

  [Fact]
  public void NowPlayingDock_RegionRole_AnnouncesDockName()
  {
    // The whole strip is wrapped as a region so AT can address it as a unit.
    var cut = RenderComponent<NowPlayingDock>();
    var root = cut.Find(".now-playing-dock");
    root.GetAttribute("role").Should().Be("region");
    root.GetAttribute("aria-label").Should().Be("Now playing dock");
  }

  [Fact]
  public void NowPlayingDock_Markup_NeverContains_FractionalSecondsTimespan()
  {
    // Regression guard: any raw TimeSpan.ToString() reintroduced into the dock
    // would surface fractional-seconds. The Durations.FormatTrack helper rounds
    // to whole seconds, so this pattern must never appear in the rendered DOM.
    var cut = RenderComponent<NowPlayingDock>();
    cut.Markup.Should().NotMatchRegex(@"\d+:\d{2}:\d{2}\.\d{4,}");
  }

  [Fact]
  public void NowPlayingDock_SourceColorDot_RendersInsideArtistBlock()
  {
    // The 8×8 source-color dot is a visual handle for "what's playing this"
    // — it sits at the start of the artist line. Empty state shows it with
    // the fallback --text-low colour (no source known yet).
    var cut = RenderComponent<NowPlayingDock>();
    var artist = cut.Find(".now-playing-dock-artist");
    var dot = artist.QuerySelector(".source-color-dot");
    dot.Should().NotBeNull("the source-color dot lives inside the artist row");
    dot!.GetAttribute("style").Should().Contain("--text-low");
  }
}

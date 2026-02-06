using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using Radio.Web.Components.Pages;
using Radio.Web.Services;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;
using MudBlazor;
using MudBlazor.Services;
using Moq;

namespace Radio.Web.Tests.Components.Pages;

/// <summary>
/// bUnit tests for the Home page component (three-panel dashboard layout).
/// Tests basic UI rendering and component structure.
/// </summary>
public class HomePageTests : TestContext
{
  private readonly ILoggerFactory _loggerFactory;

  public HomePageTests()
  {
    _loggerFactory = new NullLoggerFactory();

    // Allow all JS interop calls to succeed (needed for MudTabs, canvas, etc.)
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

    // MudBlazor services (full registration needed for MudTabs in QueueHistoryPanel)
    Services.AddMudServices();
    var mockSnackbar = new Mock<ISnackbar>();
    Services.AddSingleton<ISnackbar>(mockSnackbar.Object);
    var mockDialogService = new Mock<IDialogService>();
    Services.AddSingleton<IDialogService>(mockDialogService.Object);

    // HTTP clients for API services
    Services.AddHttpClient<AudioApiService>();
    Services.AddHttpClient<SystemApiService>();
    Services.AddHttpClient<ConfigurationApiService>();
    Services.AddHttpClient<QueueApiService>();
    Services.AddHttpClient<PlayHistoryApiService>();
    Services.AddHttpClient<FileApiService>();

    // SignalR hub services
    Services.AddSingleton(sp =>
      new AudioStateHubService(
        NullLogger<AudioStateHubService>.Instance,
        sp.GetRequiredService<IConfiguration>()
      )
    );
    Services.AddSingleton(sp =>
      new AudioVisualizationHubService(
        NullLogger<AudioVisualizationHubService>.Instance,
        sp.GetRequiredService<IConfiguration>()
      )
    );

    // QueuePersistenceService
    Services.AddSingleton<QueuePersistenceService>();
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
  public void Home_Renders_Successfully()
  {
    var cut = RenderComponent<Home>();

    Assert.NotNull(cut);
    // Three-panel layout should render
    Assert.Contains("mud-paper", cut.Markup);
  }

  [Fact]
  public void Home_Contains_ThreePanel_Layout()
  {
    var cut = RenderComponent<Home>();

    // Should have a flex container with three MudPaper panels
    var papers = cut.FindAll(".mud-paper");
    Assert.True(papers.Count >= 3, $"Expected at least 3 MudPaper panels, found {papers.Count}");
  }

  [Fact]
  public void Home_Contains_NowPlaying_Panel()
  {
    var cut = RenderComponent<Home>();

    // NowPlayingPanel should render with track info default
    Assert.Contains("No Track Playing", cut.Markup);
  }

  [Fact]
  public void Home_Contains_Transport_Controls()
  {
    var cut = RenderComponent<Home>();

    // Transport controls from NowPlayingPanel
    var buttons = cut.FindAll("button");
    Assert.NotEmpty(buttons);
    Assert.True(buttons.Count >= 5, $"Expected at least 5 buttons, found {buttons.Count}");
  }

  [Fact]
  public void Home_Contains_Volume_Control()
  {
    var cut = RenderComponent<Home>();

    // Volume slider renders percentage text
    var markup = cut.Markup;
    Assert.True(markup.Contains("%"), "Volume percentage should be displayed");
  }

  [Fact]
  public void Home_Shows_DefaultState_WhenNoData()
  {
    var cut = RenderComponent<Home>();

    Assert.Contains("No Track Playing", cut.Markup);
  }

  [Fact]
  public void Home_Has_AlbumArt_Placeholder()
  {
    var cut = RenderComponent<Home>();

    Assert.Contains("mud-icon", cut.Markup);
  }

  [Fact]
  public void Home_Contains_Balance_Control()
  {
    var cut = RenderComponent<Home>();

    var markup = cut.Markup;
    // Balance slider has L and R labels
    Assert.Contains(">L<", markup);
    Assert.Contains(">R<", markup);
  }

  [Fact]
  public void Home_Contains_QueueHistory_Panel()
  {
    var cut = RenderComponent<Home>();

    // QueueHistoryPanel renders with tabs
    Assert.Contains("Queue", cut.Markup);
    Assert.Contains("History", cut.Markup);
  }

  [Fact]
  public void Home_Contains_Visualizer_Panel()
  {
    var cut = RenderComponent<Home>();

    // VisualizerPanel renders with mode buttons and canvas
    Assert.Contains("Visualizer", cut.Markup);
    Assert.Contains("visualizer-panel-canvas", cut.Markup);
  }

  [Fact]
  public void Home_Shows_MuteButton()
  {
    var cut = RenderComponent<Home>();

    var buttons = cut.FindAll("button");
    var muteButton = buttons.Any(b =>
      b.GetAttribute("title")?.Contains("Mute") == true ||
      b.GetAttribute("title")?.Contains("Unmute") == true);
    Assert.True(muteButton, "Mute button should be present");
  }

  [Fact]
  public void Home_EmptyState_Shows_GenericIcon()
  {
    var cut = RenderComponent<Home>();

    Assert.Contains("mud-icon", cut.Markup);
    Assert.Contains("No Track Playing", cut.Markup);
  }
}

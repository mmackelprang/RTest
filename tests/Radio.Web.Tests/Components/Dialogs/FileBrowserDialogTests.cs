using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using MudBlazor.Services;
using Radio.Web.Components.Dialogs;
using Radio.Web.Services.ApiClients;

namespace Radio.Web.Tests.Components.Dialogs;

/// <summary>
/// bUnit tests for the FileBrowserDialog component.
/// MudDialog components render empty without a CascadingParameter IMudDialogInstance,
/// so these tests verify the component instantiates and accepts parameters without errors.
/// </summary>
public class FileBrowserDialogTests : TestContext
{
  private readonly ILoggerFactory _loggerFactory;

  public FileBrowserDialogTests()
  {
    _loggerFactory = new NullLoggerFactory();

    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        { "ApiBaseUrl", "http://localhost:5000" }
      })
      .Build();

    Services.AddSingleton<IConfiguration>(configuration);
    Services.AddSingleton(_loggerFactory);
    Services.AddMudServices();
    Services.AddHttpClient<FileApiService>();
    Services.AddHttpClient<ConfigurationApiService>();
    Services.AddSingleton<ILogger<FileBrowserDialog>>(
      _loggerFactory.CreateLogger<FileBrowserDialog>());

    JSInterop.Mode = JSRuntimeMode.Loose;
  }

  protected override void Dispose(bool disposing)
  {
    if (disposing)
      _loggerFactory?.Dispose();
    base.Dispose(disposing);
  }

  [Fact]
  public void FileBrowserDialog_Renders_Successfully()
  {
    var cut = RenderComponent<FileBrowserDialog>();
    Assert.NotNull(cut);
  }

  [Fact]
  public void FileBrowserDialog_Accepts_MultiSelect_Parameter()
  {
    var cut = RenderComponent<FileBrowserDialog>(parameters => parameters
      .Add(p => p.AllowMultiSelect, true));
    Assert.NotNull(cut);
  }

  [Fact]
  public void FileBrowserDialog_Accepts_SingleSelect_Default()
  {
    var cut = RenderComponent<FileBrowserDialog>();
    Assert.NotNull(cut);
  }

  [Fact]
  public void FileBrowserDialog_Accepts_ShowPlayButton_Parameter()
  {
    var cut = RenderComponent<FileBrowserDialog>(parameters => parameters
      .Add(p => p.ShowPlayButton, true));
    Assert.NotNull(cut);
  }

  [Fact]
  public void FileBrowserDialog_Accepts_AllowAddToQueue_Parameter()
  {
    var cut = RenderComponent<FileBrowserDialog>(parameters => parameters
      .Add(p => p.AllowAddToQueue, true));
    Assert.NotNull(cut);
  }

  [Fact]
  public void FileBrowserDialog_Accepts_InitialSelection_Parameter()
  {
    var cut = RenderComponent<FileBrowserDialog>(parameters => parameters
      .Add(p => p.InitialSelection, "/path/to/sound.mp3"));
    Assert.NotNull(cut);
  }

  [Fact]
  public void FileBrowserDialog_Accepts_Subdirectory_Parameter()
  {
    var cut = RenderComponent<FileBrowserDialog>(parameters => parameters
      .Add(p => p.Subdirectory, "notifications"));
    Assert.NotNull(cut);
  }

  [Fact]
  public void FileBrowserDialog_Accepts_Combined_Parameters()
  {
    var cut = RenderComponent<FileBrowserDialog>(parameters => parameters
      .Add(p => p.AllowMultiSelect, true)
      .Add(p => p.AllowAddToQueue, true)
      .Add(p => p.ShowPlayButton, true)
      .Add(p => p.Subdirectory, "music"));
    Assert.NotNull(cut);
  }

  [Fact]
  public void FileBrowserDialog_WithAbsoluteMode_Renders_Successfully()
  {
    // Verify the component instantiates without error — it has internal
    // absolute mode state (drives, path bar) that must initialize cleanly.
    // MudDialog renders empty without IMudDialogInstance, so we just
    // verify no exceptions during construction.
    var cut = RenderComponent<FileBrowserDialog>();
    Assert.NotNull(cut);
    // Component should have the internal drives list initialized (empty until API call)
    Assert.NotNull(cut.Instance);
  }

  [Fact]
  public void FileBrowserDialog_Accepts_AllParameters_Combined_WithQueueAndPlay()
  {
    var cut = RenderComponent<FileBrowserDialog>(parameters => parameters
      .Add(p => p.AllowMultiSelect, true)
      .Add(p => p.AllowAddToQueue, true)
      .Add(p => p.ShowPlayButton, true)
      .Add(p => p.Subdirectory, "music")
      .Add(p => p.InitialSelection, "/path/to/song.mp3"));
    Assert.NotNull(cut);
  }
}

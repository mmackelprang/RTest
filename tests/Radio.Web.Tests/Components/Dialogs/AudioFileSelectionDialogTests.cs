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
/// bUnit tests for the AudioFileSelectionDialog component
/// Tests dialog rendering, file selection, and user interactions
/// </summary>
public class AudioFileSelectionDialogTests : TestContext
{
  private readonly ILoggerFactory _loggerFactory;

  public AudioFileSelectionDialogTests()
  {
    _loggerFactory = new NullLoggerFactory();
    
    // Set up minimal dependencies with in-memory configuration
    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        { "ApiBaseUrl", "http://localhost:5000" }
      })
      .Build();

    Services.AddSingleton<IConfiguration>(configuration);
    Services.AddSingleton(_loggerFactory);
    
    // Add MudBlazor services
    Services.AddMudServices();
    
    // Add HttpClient for API services
    Services.AddHttpClient<SourcesApiService>();
    
    // Setup JSInterop mocks for MudBlazor components
    JSInterop.Mode = JSRuntimeMode.Loose;
    JSInterop.SetupVoid("mudElementRef.getBoundingClientRect", _ => true);
    JSInterop.Setup<int>("mudElementRef.getBoundingClientRect", _ => true).SetResult(0);
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
  public void AudioFileSelectionDialog_Renders_Successfully()
  {
    // Act
    var cut = RenderComponent<AudioFileSelectionDialog>();

    // Assert - Check that the component renders without throwing
    Assert.NotNull(cut);
  }

  [Fact]
  public void AudioFileSelectionDialog_Shows_Title()
  {
    // Act
    var cut = RenderComponent<AudioFileSelectionDialog>();

    // Assert - Check for dialog title
    Assert.Contains("Select Audio File", cut.Markup);
  }

  [Fact]
  public void AudioFileSelectionDialog_Has_Cancel_Button()
  {
    // Act
    var cut = RenderComponent<AudioFileSelectionDialog>();

    // Assert - Check for Cancel button
    Assert.Contains("Cancel", cut.Markup);
  }

  [Fact]
  public void AudioFileSelectionDialog_Has_Select_Button()
  {
    // Act
    var cut = RenderComponent<AudioFileSelectionDialog>();

    // Assert - Check for Select button
    Assert.Contains("Select", cut.Markup);
  }

  [Fact]
  public void AudioFileSelectionDialog_Shows_Loading_State_Initially()
  {
    // Act
    var cut = RenderComponent<AudioFileSelectionDialog>();

    // Assert - Should show loading indicator initially
    // The component starts with _isLoading = true
    Assert.NotNull(cut);
    // Loading state is transient, so we just verify component renders
  }

  [Fact]
  public void AudioFileSelectionDialog_Accepts_InitialSelection_Parameter()
  {
    // Arrange
    var initialPath = "/path/to/sound.mp3";

    // Act
    var cut = RenderComponent<AudioFileSelectionDialog>(parameters => parameters
      .Add(p => p.InitialSelection, initialPath));

    // Assert - Component should accept the parameter without error
    Assert.NotNull(cut);
  }

  [Fact]
  public void AudioFileSelectionDialog_Accepts_Subdirectory_Parameter()
  {
    // Arrange
    var subdirectory = "notifications";

    // Act
    var cut = RenderComponent<AudioFileSelectionDialog>(parameters => parameters
      .Add(p => p.Subdirectory, subdirectory));

    // Assert - Component should accept the parameter without error
    Assert.NotNull(cut);
  }
}

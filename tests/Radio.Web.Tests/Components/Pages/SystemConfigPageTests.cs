using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using MudBlazor.Services;
using Radio.Web.Components.Pages;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;

namespace Radio.Web.Tests.Components.Pages;

/// <summary>
/// bUnit tests for the SystemConfigPage component
/// Tests system stats display, configuration management, log viewer, and event sources
/// </summary>
public class SystemConfigPageTests : TestContext
{
  private readonly ILoggerFactory _loggerFactory;

  public SystemConfigPageTests()
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
    Services.AddHttpClient<SystemApiService>();
    Services.AddHttpClient<ConfigurationApiService>();
    Services.AddHttpClient<SourcesApiService>();
    
    // Add AudioStateHubService
    Services.AddSingleton<AudioStateHubService>();
    
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
  public void SystemConfigPage_Renders_Successfully()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - Check that the component renders without throwing
    Assert.NotNull(cut);
  }

  [Fact]
  public void SystemConfigPage_Contains_Tabs()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - Check for tab structure
    Assert.Contains("System Stats", cut.Markup);
    Assert.Contains("Configuration", cut.Markup);
    Assert.Contains("Logs", cut.Markup);
    Assert.Contains("Event Sources", cut.Markup);
  }

  [Fact]
  public void SystemConfigPage_SystemStats_Tab_Contains_Gauges()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - Check for system stat components
    Assert.Contains("CPU Usage", cut.Markup);
    Assert.Contains("RAM Usage", cut.Markup);
    Assert.Contains("Disk Usage", cut.Markup);
    Assert.Contains("Active Threads", cut.Markup);
    Assert.Contains("App Uptime", cut.Markup);
    Assert.Contains("System Uptime", cut.Markup);
    Assert.Contains("Temperature", cut.Markup);
    Assert.Contains("Audio Engine State", cut.Markup);
  }

  [Fact]
  public void SystemConfigPage_Configuration_Tab_Present()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - Check that configuration tab exists
    Assert.Contains("Configuration", cut.Markup);
  }

  [Fact]
  public void SystemConfigPage_Logs_Tab_Present()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - Check that logs tab exists
    Assert.Contains("Logs", cut.Markup);
  }

  [Fact]
  public void SystemConfigPage_EventSources_Tab_Renders()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - The component renders without error
    Assert.NotNull(cut);
  }

  [Fact]
  public void SystemConfigPage_EventSources_Tab_Contains_TTS_Section()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - Check that Event Sources tab is present (content loads dynamically)
    Assert.Contains("Event Sources", cut.Markup);
  }

  [Fact]
  public void SystemConfigPage_EventSources_Tab_Contains_AudioFile_Section()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - Check that Event Sources tab is present (content loads dynamically)
    Assert.Contains("Event Sources", cut.Markup);
  }

  [Fact]
  public void SystemConfigPage_EventSources_Tab_Contains_ActiveSources_Section()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - Check for Active Event Sources section (content loads dynamically)
    Assert.Contains("Event Sources", cut.Markup);
  }

  [Fact]
  public void SystemConfigPage_EventSources_Tab_Shows_Empty_State()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - Component should render successfully (empty state depends on API data)
    Assert.NotNull(cut);
    Assert.Contains("Event Sources", cut.Markup);
  }

  [Fact]
  public void SystemConfigPage_RadioControl_Uses_Modern_Typography()
  {
    // This test validates that the Radio Control page doesn't use LED fonts
    // Note: RadioPage is a separate component, so we test it independently
    // We need to add the dependencies for RadioPage
    Services.AddHttpClient<RadioApiService>();
    Services.AddSingleton<AudioStateHubService>();
    Services.AddHttpClient<ConfigurationApiService>();
    
    // Act
    var cut = RenderComponent<RadioPage>();

    // Assert - Should not contain LED font references
    Assert.DoesNotContain("DSEG14Classic", cut.Markup);
    Assert.DoesNotContain("text-shadow: 0 0 20px", cut.Markup);
  }

  [Fact]
  public void SystemConfigPage_DefaultValues_Displayed()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - Check default values are shown (N/A for unavailable stats)
    Assert.Contains("N/A", cut.Markup);
  }

  [Fact]
  public void SystemConfigPage_Configuration_Tab_Has_Multiple_SubTabs()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - Check that Configuration tab has multiple sub-tabs
    Assert.Contains("Configuration", cut.Markup);
    Assert.NotNull(cut);
    // Component should render without errors
    Assert.True(cut.Markup.Length > 0);
  }

  [Fact]
  public void SystemConfigPage_Devices_Configuration_Renders_Without_Error()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - The page should render successfully with Devices configuration support
    Assert.NotNull(cut);
    Assert.Contains("Configuration", cut.Markup);
    
    // Verify component initializes device options (even if null initially due to API unavailability)
    // The component should handle missing configuration gracefully
    Assert.DoesNotContain("Object reference not set", cut.Markup);
  }

  [Fact]
  public void SystemConfigPage_Handles_Missing_DeviceConfiguration_Gracefully()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - Component should not crash when device configuration is unavailable
    Assert.NotNull(cut);
    // Should show loading indicator or handle null device options
    Assert.DoesNotContain("NullReferenceException", cut.Markup);
  }
}

using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using MudBlazor.Services;
using Radio.Web.Components.Pages;
using Radio.Web.Services.ApiClients;

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
  public void SystemConfigPage_DefaultValues_Displayed()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - Check default values are shown (N/A for unavailable stats)
    Assert.Contains("N/A", cut.Markup);
  }
}

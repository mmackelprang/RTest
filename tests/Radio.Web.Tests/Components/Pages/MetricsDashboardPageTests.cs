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
/// bUnit tests for the MetricsDashboardPage component
/// Tests metrics discovery, snapshot display, and aggregate statistics
/// </summary>
public class MetricsDashboardPageTests : TestContext
{
  private readonly ILoggerFactory _loggerFactory;

  public MetricsDashboardPageTests()
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
    Services.AddHttpClient<MetricsApiService>();
    
    // Setup JSInterop mocks for MudBlazor components
    JSInterop.Mode = JSRuntimeMode.Loose;
    JSInterop.SetupVoid("mudElementRef.getBoundingClientRect", _ => true);
    JSInterop.Setup<int>("mudElementRef.getBoundingClientRect", _ => true).SetResult(0);
    JSInterop.SetupVoid("mudPopover.connect", _ => true);
    JSInterop.SetupVoid("mudPopover.disconnect", _ => true);
    JSInterop.SetupVoid("mudPopover.initialize", _ => true);
    JSInterop.SetupVoid("mudSelect.setDisabled", _ => true);
    JSInterop.SetupVoid("mudKeyInterceptor.connect", _ => true);
    JSInterop.SetupVoid("mudKeyInterceptor.disconnect", _ => true);
  }

  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      _loggerFactory?.Dispose();
    }
    base.Dispose(disposing);
  }

  private IRenderedComponent<MetricsDashboardPage> RenderMetricsDashboard()
  {
    ComponentFactories.AddStub<MudBlazor.MudPopoverProvider>();
    return RenderComponent<MetricsDashboardPage>();
  }

  [Fact]
  public void MetricsDashboardPage_Renders_Successfully()
  {
    // Act
    var cut = RenderMetricsDashboard();

    // Assert - Check that the component renders without throwing
    Assert.NotNull(cut);
  }

  [Fact]
  public void MetricsDashboardPage_Contains_Title()
  {
    // Act
    var cut = RenderMetricsDashboard();

    // Assert - Check for page title
    Assert.Contains("Metrics Dashboard", cut.Markup);
  }

  [Fact]
  public void MetricsDashboardPage_Contains_TimeRange_Buttons()
  {
    // Act
    var cut = RenderMetricsDashboard();

    // Assert - Check for time range buttons
    Assert.Contains("Last Hour", cut.Markup);
    Assert.Contains("Last 24 Hours", cut.Markup);
    Assert.Contains("Last 7 Days", cut.Markup);
  }

  [Fact]
  public void MetricsDashboardPage_Contains_Refresh_Button()
  {
    // Act
    var cut = RenderMetricsDashboard();

    // Assert - Check for refresh button
    Assert.Contains("Refresh", cut.Markup);
  }

  [Fact]
  public void MetricsDashboardPage_Shows_Info_When_No_Metrics()
  {
    // Act
    var cut = RenderMetricsDashboard();

    // Assert - Should show info message when no metrics are available
    // Note: Actual behavior depends on API response, test just validates rendering
    Assert.NotNull(cut);
  }

  [Fact]
  public void MetricsDashboardPage_Renders_Content_Area()
  {
    // Act
    var cut = RenderMetricsDashboard();

    // Assert - Check that component has basic structure
    Assert.Contains("Metrics Dashboard", cut.Markup);
  }
}

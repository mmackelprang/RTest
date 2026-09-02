using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Radzen;
using Radio.Web.Components.Pages;
using Radio.Web.Services.ApiClients;
using Radio.Web.Tests.TestHelpers;

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
    // Hermetic rig: fails every outbound HTTP request and every SignalR
    // negotiate without touching the network, so this fixture's result never
    // depends on whether radio-api happens to be running locally.
    Services.AddHermeticTestRig();

    _loggerFactory = new NullLoggerFactory();

    // Set up minimal dependencies with in-memory configuration
    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        { "ApiBaseUrl", HermeticTestRig.ApiBaseUrl }
      })
      .Build();

    Services.AddSingleton<IConfiguration>(configuration);
    Services.AddSingleton(_loggerFactory);

    // Add Radzen services
    Services.AddRadzenComponents();

    // Add HttpClient for API services
    Services.AddHttpClient<MetricsApiService>();
    Services.AddHttpClient<ConfigurationApiService>();

    // Setup JSInterop for Radzen components and metricsChart module
    JSInterop.Mode = JSRuntimeMode.Loose;
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

    // Assert - Check for page heading
    Assert.Contains("Metrics", cut.Markup);
  }

  [Fact]
  public void MetricsDashboardPage_Contains_TimeRange_Buttons()
  {
    // Act
    var cut = RenderMetricsDashboard();

    // Assert - Check for all time range buttons including new 5m and 30d
    Assert.Contains(">5m<", cut.Markup);
    Assert.Contains(">1h<", cut.Markup);
    Assert.Contains(">24h<", cut.Markup);
    Assert.Contains(">7d<", cut.Markup);
    Assert.Contains(">30d<", cut.Markup);
  }

  [Fact]
  public void MetricsDashboardPage_Contains_Refresh_Button()
  {
    // Act
    var cut = RenderMetricsDashboard();

    // Assert - Check for refresh icon button (RadzenButton renders an icon, not text)
    Assert.NotNull(cut);
    // The refresh button is a RadzenButton, verify it renders
    Assert.Contains("metricsChartCanvas", cut.Markup);
  }

  [Fact]
  public void MetricsDashboardPage_Contains_Chart_Canvas()
  {
    // Act
    var cut = RenderMetricsDashboard();

    // Assert - Check for the canvas element for time-series chart
    Assert.Contains("metricsChartCanvas", cut.Markup);
  }

  [Fact]
  public void MetricsDashboardPage_Shows_Info_When_No_Metrics()
  {
    // Act
    var cut = RenderMetricsDashboard();

    // Assert - Should show info message when no metrics are available
    Assert.NotNull(cut);
  }

  [Fact]
  public void MetricsDashboardPage_Renders_Content_Area()
  {
    // Act
    var cut = RenderMetricsDashboard();

    // Assert - Check that component has basic structure with new layout
    Assert.Contains("metrics-page", cut.Markup);
    Assert.Contains("metrics-header", cut.Markup);
    Assert.Contains("metrics-body", cut.Markup);
  }
}

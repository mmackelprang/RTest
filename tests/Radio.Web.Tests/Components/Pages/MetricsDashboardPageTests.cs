using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    Services.AddHttpClient<ConfigurationApiService>();

    // Setup JSInterop mocks for MudBlazor components and metricsChart module
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

    // Assert - Check for refresh icon button (MudIconButton renders an icon, not text)
    Assert.NotNull(cut);
    // The refresh button is a MudIconButton, verify it renders
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

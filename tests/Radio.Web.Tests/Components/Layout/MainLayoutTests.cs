using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using Radio.Web.Components.Layout;
using Radio.Web.Services.ApiClients;
using Xunit;

namespace Radio.Web.Tests.Components.Layout;

public class MainLayoutTests : TestContext
{
  public MainLayoutTests()
  {
    // Set up minimal dependencies with in-memory configuration
    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        { "ApiBaseUrl", "http://localhost:5000" }
      })
      .Build();

    Services.AddSingleton<IConfiguration>(configuration);
    Services.AddSingleton(NullLoggerFactory.Instance);
    
    // Add real HttpClient services (they will fail gracefully if API is not available)
    Services.AddHttpClient<SystemApiService>();
    Services.AddHttpClient<SourcesApiService>();
    
    // bUnit provides a mock navigation manager automatically
  }

  [Fact]
  public void MainLayout_RendersWithoutErrors()
  {
    // Act
    var cut = RenderComponent<MainLayout>();

    // Assert
    Assert.NotNull(cut);
    cut.Find(".layout-container"); // Verify layout container exists
  }

  [Fact]
  public void MainLayout_HasSystemStatsDisplay()
  {
    // Act
    var cut = RenderComponent<MainLayout>();

    // Assert
    var statsDisplay = cut.Find(".led-display-cyan");
    Assert.NotNull(statsDisplay);
    Assert.Contains("CPU:", statsDisplay.TextContent);
    Assert.Contains("RAM:", statsDisplay.TextContent);
    Assert.Contains("Threads:", statsDisplay.TextContent);
  }

  [Fact]
  public void MainLayout_DisplaysDateTime()
  {
    // Act
    var cut = RenderComponent<MainLayout>();

    // Assert
    var timeDisplay = cut.Find(".led-display");
    Assert.NotNull(timeDisplay);
    // DateTime should be in HH:mm:ss format
    Assert.Matches(@"\d{2}:\d{2}:\d{2}", timeDisplay.TextContent);
  }

  [Fact]
  public void MainLayout_RendersNavigationIcons()
  {
    // Act
    var cut = RenderComponent<MainLayout>();

    // Assert
    var homeButton = cut.Find("a[href='/']");
    Assert.NotNull(homeButton);
    
    var visualizerButton = cut.Find("a[href='/visualizer']");
    Assert.NotNull(visualizerButton);
    
    var systemButton = cut.Find("a[href='/system']");
    Assert.NotNull(systemButton);
  }

  [Fact]
  public void MainLayout_HasSourceSelector()
  {
    // Act
    var cut = RenderComponent<MainLayout>();

    // Assert - Component should have source selector structure
    Assert.Contains("mud-select", cut.Markup, StringComparison.OrdinalIgnoreCase);
  }
}

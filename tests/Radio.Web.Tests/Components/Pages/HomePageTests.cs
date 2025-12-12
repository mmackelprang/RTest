using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using Radio.Web.Components.Pages;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;

namespace Radio.Web.Tests.Components.Pages;

/// <summary>
/// bUnit tests for the Home page component
/// Tests basic UI rendering and component structure
/// </summary>
public class HomePageTests : TestContext
{
  public HomePageTests()
  {
    // Set up minimal dependencies with in-memory configuration
    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        { "ApiBaseUrl", "http://localhost:5000" }
      })
      .Build();

    Services.AddSingleton<IConfiguration>(configuration);
    Services.AddSingleton(new NullLoggerFactory());
    
    // Add HttpClient for API services
    Services.AddHttpClient<AudioApiService>();
    Services.AddHttpClient<SystemApiService>();
    
    // Add SignalR hub service
    Services.AddSingleton(sp => 
      new AudioStateHubService(
        NullLogger<AudioStateHubService>.Instance,
        sp.GetRequiredService<IConfiguration>()
      )
    );
  }

  [Fact]
  public void Home_Renders_Successfully()
  {
    // Act
    var cut = RenderComponent<Home>();

    // Assert - Check that the component renders without throwing
    Assert.NotNull(cut);
    Assert.Contains("home-page", cut.Markup);
  }

  [Fact]
  public void Home_Contains_NowPlaying_Card()
  {
    // Act
    var cut = RenderComponent<Home>();

    // Assert - Check for the main card structure
    Assert.Contains("mud-card", cut.Markup);
    Assert.Contains("text-align: center", cut.Markup);
  }

  [Fact]
  public void Home_Contains_Transport_Controls()
  {
    // Act
    var cut = RenderComponent<Home>();

    // Assert - Check for transport control buttons
    var buttons = cut.FindAll("button");
    Assert.NotEmpty(buttons);
    
    // Should have play/pause, next, previous, shuffle, repeat buttons
    Assert.True(buttons.Count >= 5, $"Expected at least 5 buttons, found {buttons.Count}");
  }

  [Fact]
  public void Home_Contains_Volume_Control()
  {
    // Act
    var cut = RenderComponent<Home>();

    // Assert - Check for volume slider
    Assert.Contains("Volume:", cut.Markup);
  }

  [Fact]
  public void Home_Shows_DefaultState_WhenNoData()
  {
    // Act
    var cut = RenderComponent<Home>();

    // Assert - Should show "No Track Playing" when no data
    Assert.Contains("No Track Playing", cut.Markup);
  }

  [Fact]
  public void Home_Has_AlbumArt_Placeholder()
  {
    // Act
    var cut = RenderComponent<Home>();

    // Assert - Should have music icon (svg) as placeholder
    Assert.Contains("mud-icon-root", cut.Markup);
  }


}

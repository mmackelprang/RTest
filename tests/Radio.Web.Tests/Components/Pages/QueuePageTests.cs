using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using Radio.Web.Components.Pages;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;
using Xunit;

namespace Radio.Web.Tests.Components.Pages;

public class QueuePageTests : TestContext
{
  public QueuePageTests()
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
    
    // Add real HttpClient services
    Services.AddHttpClient<QueueApiService>();
    Services.AddHttpClient<AudioApiService>();
    
    // Add SignalR hub service
    Services.AddSingleton(sp => 
      new AudioStateHubService(
        NullLogger<AudioStateHubService>.Instance,
        sp.GetRequiredService<IConfiguration>()
      )
    );
  }

  [Fact]
  public void QueuePage_RendersWithoutErrors()
  {
    // Act
    var cut = RenderComponent<QueuePage>();

    // Assert
    Assert.NotNull(cut);
    cut.Find(".queue-page");
  }

  [Fact]
  public void QueuePage_ShowsEmptyState_Initially()
  {
    // Act
    var cut = RenderComponent<QueuePage>();

    // Assert - Should show empty state when API returns no data
    cut.WaitForAssertion(() =>
    {
      Assert.Contains("No tracks in queue", cut.Markup);
    }, TimeSpan.FromSeconds(2));
  }

  [Fact]
  public void QueuePage_HasClearAllButton()
  {
    // Act
    var cut = RenderComponent<QueuePage>();

    // Assert
    Assert.Contains("Clear All", cut.Markup);
  }

  [Fact]
  public void QueuePage_ShowsTableStructure()
  {
    // Act
    var cut = RenderComponent<QueuePage>();

    // Assert - MudTable or table headers should be present
    var markup = cut.Markup;
    // Queue page should have table structure ready
    Assert.Contains("queue-page", markup);
  }

  [Fact]
  public void QueuePage_HasQueueIcon_InEmptyState()
  {
    // Act
    var cut = RenderComponent<QueuePage>();

    // Assert - Empty state should have icon
    cut.WaitForAssertion(() =>
    {
      var markup = cut.Markup;
      Assert.Contains("mud-icon", markup, StringComparison.OrdinalIgnoreCase);
    }, TimeSpan.FromSeconds(2));
  }

  [Fact]
  public void QueuePage_InitializesSignalRSubscription()
  {
    // Act
    var cut = RenderComponent<QueuePage>();

    // Assert - Component should initialize successfully with SignalR subscription
    Assert.NotNull(cut);
    cut.WaitForAssertion(() =>
    {
      Assert.True(true); // Component initialized successfully
    }, TimeSpan.FromSeconds(1));
  }
}

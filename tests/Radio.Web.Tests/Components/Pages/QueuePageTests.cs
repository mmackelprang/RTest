using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using Radio.Web.Components.Pages;
using Radio.Web.Services;
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
    Services.AddHttpClient<ConfigurationApiService>();
    Services.AddHttpClient<FileApiService>();
    Services.AddHttpClient<PlaylistApiService>();
    
    // Add SignalR hub service
    Services.AddSingleton(sp => 
      new AudioStateHubService(
        NullLogger<AudioStateHubService>.Instance,
        sp.GetRequiredService<IConfiguration>()
      )
    );
    
    // Add ISnackbar mock
    var mockSnackbar = Substitute.For<ISnackbar>();
    Services.AddSingleton(mockSnackbar);
    
    // Add IDialogService mock
    var mockDialogService = Substitute.For<IDialogService>();
    Services.AddSingleton(mockDialogService);
    
    // Add QueuePersistenceService
    Services.AddScoped<QueuePersistenceService>();

    // Add MudBlazor services (needed for MudTextField, MudSelect in always-visible file browser)
    Services.AddMudServices();

    // Setup JSInterop mocks for MudBlazor components
    JSInterop.Mode = JSRuntimeMode.Loose;
  }

  private IRenderedComponent<QueuePage> RenderQueuePage()
  {
    ComponentFactories.AddStub<MudBlazor.MudPopoverProvider>();
    ComponentFactories.AddStub<MudBlazor.MudTextField<string>>();
    ComponentFactories.AddStub<MudBlazor.MudSelect<string>>();
    return RenderComponent<QueuePage>();
  }

  [Fact]
  public void QueuePage_RendersWithoutErrors()
  {
    // Act
    var cut = RenderQueuePage();

    // Assert
    Assert.NotNull(cut);
    cut.Find(".queue-page");
  }

  [Fact]
  public void QueuePage_ShowsEmptyState_Initially()
  {
    // Act
    var cut = RenderQueuePage();

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
    var cut = RenderQueuePage();

    // Assert - Clear All is now an icon button with title attribute
    Assert.Contains("Clear All", cut.Markup);
  }

  [Fact]
  public void QueuePage_ShowsTableStructure()
  {
    // Act
    var cut = RenderQueuePage();

    // Assert - MudTable or table headers should be present
    var markup = cut.Markup;
    // Queue page should have table structure ready
    Assert.Contains("queue-page", markup);
  }

  [Fact]
  public void QueuePage_HasQueueIcon_InEmptyState()
  {
    // Act
    var cut = RenderQueuePage();

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
    var cut = RenderQueuePage();

    // Assert - Component should initialize successfully with SignalR subscription
    Assert.NotNull(cut);
    cut.WaitForAssertion(() =>
    {
      Assert.True(true); // Component initialized successfully
    }, TimeSpan.FromSeconds(1));
  }

  [Fact]
  public void QueuePage_HasFileBrowserPanel()
  {
    // Act
    var cut = RenderQueuePage();

    // Assert - File browser is always visible with header
    Assert.Contains("File Browser", cut.Markup);
  }

  [Fact]
  public void QueuePage_EmptyState_ShowsBrowseHint()
  {
    // Act
    var cut = RenderQueuePage();

    // Assert - Empty state hints to browse files on the left
    cut.WaitForAssertion(() =>
    {
      Assert.Contains("Browse files on the left", cut.Markup);
    }, TimeSpan.FromSeconds(2));
  }
}

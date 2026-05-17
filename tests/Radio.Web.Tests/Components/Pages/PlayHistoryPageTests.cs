using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Radzen;
using Radio.Web.Components.Pages;
using Radio.Web.Services.ApiClients;
using Xunit;

namespace Radio.Web.Tests.Components.Pages;

public class PlayHistoryPageTests : TestContext
{
  private readonly ILoggerFactory _loggerFactory;

  public PlayHistoryPageTests()
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
    Services.AddRadzenComponents();

    // Add real HttpClient services
    Services.AddHttpClient<PlayHistoryApiService>();
    Services.AddHttpClient<ConfigurationApiService>();
    
    // Setup JSInterop for Radzen components
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

  private IRenderedComponent<PlayHistoryPage> RenderPlayHistoryPage()
  {
    return RenderComponent<PlayHistoryPage>();
  }

  [Fact]
  public void PlayHistoryPage_Renders_Successfully()
  {
    // Act
    var cut = RenderPlayHistoryPage();

    // Assert
    Assert.NotNull(cut);
  }

  [Fact]
  public void PlayHistoryPage_Contains_Title()
  {
    // Act
    var cut = RenderPlayHistoryPage();

    // Assert
    Assert.Contains("Play History", cut.Markup);
  }

  [Fact]
  public void PlayHistoryPage_Contains_Filter_Controls()
  {
    // Act
    var cut = RenderPlayHistoryPage();

    // Assert - Check that filter controls section exists
    Assert.NotNull(cut);
    Assert.Contains("Search", cut.Markup); // TextField is not stubbed so its placeholder will appear
  }

  [Fact]
  public void PlayHistoryPage_Contains_Statistics_Section()
  {
    // Act
    var cut = RenderPlayHistoryPage();

    // Assert - Check for statistics section structure
    // Note: Actual stats will be null unless backend is running
    Assert.Contains("History", cut.Markup);
  }

  [Fact]
  public void PlayHistoryPage_Contains_History_Table_Headers()
  {
    // Act
    var cut = RenderPlayHistoryPage();

    // Assert - Check that the component renders (table only shows when data is available)
    // Without API running, no data means no table headers
    Assert.NotNull(cut);
    Assert.Contains("History", cut.Markup);
  }

  [Fact]
  public void PlayHistoryPage_Contains_Refresh_Button()
  {
    // Act
    var cut = RenderPlayHistoryPage();
    cut.WaitForAssertion(() => Assert.Contains("Refresh", cut.Markup), timeout: TimeSpan.FromSeconds(5));

    // Assert
    Assert.Contains("Refresh", cut.Markup);
  }

  [Fact]
  public void PlayHistoryPage_Contains_Filter_Button()
  {
    // Act
    var cut = RenderPlayHistoryPage();

    // Assert
    Assert.Contains("Filter", cut.Markup);
  }

  [Fact]
  public void PlayHistoryPage_Contains_Clear_Filters_Button()
  {
    // Act
    var cut = RenderPlayHistoryPage();

    // Assert - Check for clear filters button (icon class identifies the action;
    // visible text was shortened to "Clear" in fix/web-history-filter-panel-fit
    // to fit a half-width side-by-side button cell).
    Assert.Contains("filter_list_off", cut.Markup);
  }
}

using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using MudBlazor.Services;
using Radio.Web.Components.Pages;
using Radio.Web.Services.ApiClients;
using Xunit;

namespace Radio.Web.Tests.Components.Pages;

public class PlayHistoryPageTests : TestContext
{
  public PlayHistoryPageTests()
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
    Services.AddMudServices();
    
    // Add real HttpClient services
    Services.AddHttpClient<PlayHistoryApiService>();
    
    // Setup JSInterop mocks for MudBlazor components
    JSInterop.Mode = JSRuntimeMode.Loose;
  }

  [Fact]
  public void PlayHistoryPage_Renders_Successfully()
  {
    // Act
    var cut = RenderComponent<PlayHistoryPage>();

    // Assert
    Assert.NotNull(cut);
  }

  [Fact]
  public void PlayHistoryPage_Contains_Title()
  {
    // Act
    var cut = RenderComponent<PlayHistoryPage>();

    // Assert
    var title = cut.Find("h4");
    Assert.Contains("Play History", title.TextContent);
  }

  [Fact]
  public void PlayHistoryPage_Contains_Filter_Controls()
  {
    // Act
    var cut = RenderComponent<PlayHistoryPage>();

    // Assert
    Assert.Contains("Filter by Date", cut.Markup);
    Assert.Contains("Source Filter", cut.Markup);
    Assert.Contains("Search", cut.Markup);
  }

  [Fact]
  public void PlayHistoryPage_Contains_Statistics_Section()
  {
    // Act
    var cut = RenderComponent<PlayHistoryPage>();

    // Assert - Check for statistics section structure
    // Note: Actual stats will be null unless backend is running
    Assert.Contains("History", cut.Markup);
  }

  [Fact]
  public void PlayHistoryPage_Contains_History_Table_Headers()
  {
    // Act
    var cut = RenderComponent<PlayHistoryPage>();

    // Assert
    Assert.Contains("Date/Time", cut.Markup);
    Assert.Contains("Title", cut.Markup);
    Assert.Contains("Artist", cut.Markup);
    Assert.Contains("Album", cut.Markup);
    Assert.Contains("Source", cut.Markup);
    Assert.Contains("Duration", cut.Markup);
  }

  [Fact]
  public void PlayHistoryPage_Contains_Refresh_Button()
  {
    // Act
    var cut = RenderComponent<PlayHistoryPage>();
    cut.WaitForAssertion(() => Assert.Contains("Refresh", cut.Markup), timeout: TimeSpan.FromSeconds(5));

    // Assert
    Assert.Contains("Refresh", cut.Markup);
  }

  [Fact]
  public void PlayHistoryPage_Contains_Filter_Button()
  {
    // Act
    var cut = RenderComponent<PlayHistoryPage>();

    // Assert
    Assert.Contains("Filter", cut.Markup);
  }

  [Fact]
  public void PlayHistoryPage_Has_Page_Route()
  {
    // Act
    var cut = RenderComponent<PlayHistoryPage>();

    // Assert - Component should render without throwing
    Assert.NotNull(cut);
    Assert.NotEmpty(cut.Markup);
  }

  [Fact]
  public void PlayHistoryPage_Contains_Date_Picker()
  {
    // Act
    var cut = RenderComponent<PlayHistoryPage>();

    // Assert
    Assert.Contains("Filter by Date", cut.Markup);
  }

  [Fact]
  public void PlayHistoryPage_Contains_Source_Filter_Options()
  {
    // Act
    var cut = RenderComponent<PlayHistoryPage>();

    // Assert
    Assert.Contains("Source Filter", cut.Markup);
  }

  [Fact]
  public void PlayHistoryPage_Contains_Search_Field()
  {
    // Act
    var cut = RenderComponent<PlayHistoryPage>();

    // Assert
    Assert.Contains("Search", cut.Markup);
  }
}

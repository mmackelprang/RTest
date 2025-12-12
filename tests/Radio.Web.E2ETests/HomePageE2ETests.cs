using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace Radio.Web.E2ETests;

/// <summary>
/// End-to-end tests for the Home page
/// Tests complete user workflows using Playwright
/// NOTE: These tests require the application to be running
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class HomePageE2ETests : PageTest
{
  private const string BaseUrl = "http://localhost:5000"; // Adjust as needed

  [SetUp]
  public async Task Setup()
  {
    // Navigate to the home page before each test
    await Page.GotoAsync(BaseUrl);
  }

  [Test]
  public async Task HomePage_LoadsSuccessfully()
  {
    // Assert - Page should load without errors
    await Expect(Page).ToHaveTitleAsync(new Regex(".*Radio Console.*", RegexOptions.IgnoreCase));
  }

  [Test]
  public async Task HomePage_DisplaysNowPlayingCard()
  {
    // Assert - Should have the main now playing card
    var card = Page.Locator(".mud-card").First;
    await Expect(card).ToBeVisibleAsync();
  }

  [Test]
  public async Task HomePage_HasTransportControls()
  {
    // Assert - Should have transport control buttons
    // Play/Pause button
    var playButton = Page.Locator("button").Filter(new() { HasText = new Regex("Play|Pause", RegexOptions.IgnoreCase) }).First;
    await Expect(playButton).ToBeVisibleAsync();

    // Next button
    var nextButton = Page.Locator("button[title*='Next' i]").First;
    await Expect(nextButton).ToBeVisibleAsync();

    // Previous button
    var previousButton = Page.Locator("button[title*='Previous' i]").First;
    await Expect(previousButton).ToBeVisibleAsync();
  }

  [Test]
  public async Task HomePage_HasVolumeControl()
  {
    // Assert - Should have volume slider
    var volumeText = Page.Locator("text=Volume:");
    await Expect(volumeText).ToBeVisibleAsync();
  }

  [Test]
  public async Task HomePage_DisplaysNavigationBar()
  {
    // Assert - Should have navigation bar with icons
    var appBar = Page.Locator(".mud-appbar");
    await Expect(appBar).ToBeVisibleAsync();
    
    // Should have navigation icons
    var navIcons = appBar.Locator("button").Count;
    Assert.That(await navIcons, Is.GreaterThanOrEqualTo(6), "Should have at least 6 navigation buttons");
  }

  [Test]
  public async Task HomePage_HasResponsiveLayout()
  {
    // Assert - Page should have fixed dimensions for the display
    var layoutContainer = Page.Locator(".layout-container").First;
    await Expect(layoutContainer).ToBeVisibleAsync();
    
    // Verify it has inline styles for dimensions
    var style = await layoutContainer.GetAttributeAsync("style");
    Assert.That(style, Does.Contain("1920px"), "Width should be set to 1920px");
    Assert.That(style, Does.Contain("576px"), "Height should be set to 576px");
  }
}

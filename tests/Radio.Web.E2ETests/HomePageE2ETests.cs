using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;
using Xunit;

namespace Radio.Web.E2ETests;

/// <summary>
/// E2E tests for the Home page — now playing, transport controls, volume, layout.
/// </summary>
[Collection("E2E")]
public class HomePageE2ETests
{
  private readonly PlaywrightFixture _fixture;

  public HomePageE2ETests(PlaywrightFixture fixture)
  {
    _fixture = fixture;
  }

  [Fact]
  public async Task HomePage_LoadsSuccessfully()
  {
    if (!_fixture.IsServerAvailable) return;
    await _fixture.Page.GotoAsync(_fixture.BaseUrl);
    await Expect(_fixture.Page).ToHaveTitleAsync(new Regex(".*Radio Console.*", RegexOptions.IgnoreCase));
  }

  [Fact]
  public async Task HomePage_DisplaysNowPlayingCard()
  {
    if (!_fixture.IsServerAvailable) return;
    await _fixture.Page.GotoAsync(_fixture.BaseUrl);
    var card = _fixture.Page.Locator(".mud-card").First;
    await Expect(card).ToBeVisibleAsync();
  }

  [Fact]
  public async Task HomePage_HasTransportControls()
  {
    if (!_fixture.IsServerAvailable) return;
    await _fixture.Page.GotoAsync(_fixture.BaseUrl);

    var playButton = _fixture.Page.Locator("button[title*='Play' i], button[title*='Pause' i]").First;
    await Expect(playButton).ToBeVisibleAsync();

    var nextButton = _fixture.Page.Locator("button[title*='Next' i]").First;
    await Expect(nextButton).ToBeVisibleAsync();

    var previousButton = _fixture.Page.Locator("button[title*='Previous' i]").First;
    await Expect(previousButton).ToBeVisibleAsync();
  }

  [Fact]
  public async Task HomePage_HasVolumeControl()
  {
    if (!_fixture.IsServerAvailable) return;
    await _fixture.Page.GotoAsync(_fixture.BaseUrl);
    var volumeText = _fixture.Page.Locator("text=Volume:");
    await Expect(volumeText).ToBeVisibleAsync();
  }

  [Fact]
  public async Task HomePage_DisplaysNavigationBar()
  {
    if (!_fixture.IsServerAvailable) return;
    await _fixture.Page.GotoAsync(_fixture.BaseUrl);

    var appBar = _fixture.Page.Locator(".mud-appbar");
    await Expect(appBar).ToBeVisibleAsync();

    var navIcons = appBar.Locator("button");
    var count = await navIcons.CountAsync();
    Assert.True(count >= 6, $"Should have at least 6 navigation buttons, got {count}");
  }

  [Fact]
  public async Task HomePage_HasResponsiveLayout()
  {
    if (!_fixture.IsServerAvailable) return;
    await _fixture.Page.GotoAsync(_fixture.BaseUrl);

    var layoutContainer = _fixture.Page.Locator(".layout-container").First;
    await Expect(layoutContainer).ToBeVisibleAsync();

    var style = await layoutContainer.GetAttributeAsync("style");
    Assert.Contains("1920px", style);
    Assert.Contains("720px", style);
  }

  [Fact]
  public async Task HomePage_HasAudioSourceSelector()
  {
    if (!_fixture.IsServerAvailable) return;
    await _fixture.Page.GotoAsync(_fixture.BaseUrl);

    // The Audio Source MudSelect renders as a div with the label text
    var sourceLabel = _fixture.Page.Locator("text=Audio Source");
    await Expect(sourceLabel).ToBeVisibleAsync();
  }

  [Fact]
  public async Task HomePage_HasAudioOutputSelector()
  {
    if (!_fixture.IsServerAvailable) return;
    await _fixture.Page.GotoAsync(_fixture.BaseUrl);

    var outputLabel = _fixture.Page.Locator("text=Audio Output");
    await Expect(outputLabel).ToBeVisibleAsync();
  }

  [Fact]
  public async Task HomePage_HasMuteButton()
  {
    if (!_fixture.IsServerAvailable) return;
    await _fixture.Page.GotoAsync(_fixture.BaseUrl);

    var muteButton = _fixture.Page.Locator("button[title='Mute'], button[title='Unmute']").First;
    await Expect(muteButton).ToBeVisibleAsync();
  }
}

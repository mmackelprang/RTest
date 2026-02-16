using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;
using Xunit;

namespace Radio.Web.E2ETests;

/// <summary>
/// E2E tests for the Queue page — queue display, file browser toggle, playlist controls.
/// </summary>
[Collection("E2E")]
public class QueuePageE2ETests
{
  private readonly PlaywrightFixture _fixture;

  public QueuePageE2ETests(PlaywrightFixture fixture)
  {
    _fixture = fixture;
  }

  [Fact]
  public async Task QueuePage_HasBrowseFilesButton()
  {
    if (!_fixture.IsServerAvailable) return;
    await _fixture.Page.GotoAsync($"{_fixture.BaseUrl}/queue");
    await _fixture.Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

    var browseButton = _fixture.Page.Locator("button:has-text('Browse Files')");
    await Expect(browseButton).ToBeVisibleAsync();
  }

  [Fact]
  public async Task QueuePage_HasPlaylistButtons()
  {
    if (!_fixture.IsServerAvailable) return;
    await _fixture.Page.GotoAsync($"{_fixture.BaseUrl}/queue");
    await _fixture.Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

    var savePlaylist = _fixture.Page.Locator("button:has-text('Save Playlist')");
    await Expect(savePlaylist).ToBeVisibleAsync();

    var loadPlaylist = _fixture.Page.Locator("button:has-text('Load Playlist')");
    await Expect(loadPlaylist).ToBeVisibleAsync();
  }

  [Fact]
  public async Task QueuePage_ShowsEmptyStateOrQueue()
  {
    if (!_fixture.IsServerAvailable) return;
    await _fixture.Page.GotoAsync($"{_fixture.BaseUrl}/queue");
    await _fixture.Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

    // Either shows "No tracks in queue" empty state or a queue table
    var emptyState = _fixture.Page.Locator("text=No tracks in queue");
    var queueTable = _fixture.Page.Locator(".mud-table");

    var hasEmpty = await emptyState.CountAsync() > 0;
    var hasTable = await queueTable.CountAsync() > 0;
    Assert.True(hasEmpty || hasTable, "Queue page should show either empty state or queue table");
  }

  [Fact]
  public async Task QueuePage_BrowseFilesToggle_ShowsFileBrowser()
  {
    if (!_fixture.IsServerAvailable) return;
    await _fixture.Page.GotoAsync($"{_fixture.BaseUrl}/queue");
    await _fixture.Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

    // Click "Browse Files" to toggle file browser
    var browseButton = _fixture.Page.Locator("button:has-text('Browse Files')");
    if (await browseButton.CountAsync() > 0)
    {
      await browseButton.ClickAsync();
      await _fixture.Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

      // After clicking, should see "Show Queue" button instead
      var showQueueButton = _fixture.Page.Locator("button:has-text('Show Queue')");
      await Expect(showQueueButton).ToBeVisibleAsync();
    }
  }
}

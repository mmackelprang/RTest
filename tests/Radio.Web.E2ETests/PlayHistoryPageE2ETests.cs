using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace Radio.Web.E2ETests;

/// <summary>
/// E2E tests for the Play History page — filters, search, history list.
/// </summary>
[Collection("E2E")]
public class PlayHistoryPageE2ETests
{
  private readonly PlaywrightFixture _fixture;

  public PlayHistoryPageE2ETests(PlaywrightFixture fixture)
  {
    _fixture = fixture;
  }

  [Fact]
  public async Task HistoryPage_HasFilterControls()
  {
    if (!_fixture.IsServerAvailable)
    {
      return;
    }

    await _fixture.Page.GotoAsync($"{_fixture.BaseUrl}/history");
    await _fixture.Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

    var filterButton = _fixture.Page.Locator("button:has-text('Filter')");
    await Expect(filterButton).ToBeVisibleAsync();

    // Button text shortened from "Clear Filters" -> "Clear" in
    // fix/web-history-filter-panel-fit to fit a half-width cell.
    var clearButton = _fixture.Page.Locator("button:has-text('Clear')");
    await Expect(clearButton).ToBeVisibleAsync();
  }

  [Fact]
  public async Task HistoryPage_HasSearchField()
  {
    if (!_fixture.IsServerAvailable)
    {
      return;
    }

    await _fixture.Page.GotoAsync($"{_fixture.BaseUrl}/history");
    await _fixture.Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

    // RadzenTextBox with placeholder text
    var searchField = _fixture.Page.Locator("input[placeholder*='Search']").First;
    await Expect(searchField).ToBeVisibleAsync();
  }

  [Fact]
  public async Task HistoryPage_ShowsHistoryListOrEmptyState()
  {
    if (!_fixture.IsServerAvailable)
    {
      return;
    }

    await _fixture.Page.GotoAsync($"{_fixture.BaseUrl}/history");
    await _fixture.Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

    // Either shows "No History Found" or a history table
    var emptyState = _fixture.Page.Locator("text=No History Found");
    var historyTable = _fixture.Page.Locator(".mud-table");

    var hasEmpty = await emptyState.CountAsync() > 0;
    var hasTable = await historyTable.CountAsync() > 0;
    Assert.True(hasEmpty || hasTable, "History page should show either empty state or history table");
  }
}

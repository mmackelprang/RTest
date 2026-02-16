using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;
using Xunit;

namespace Radio.Web.E2ETests;

/// <summary>
/// E2E tests for page navigation via the app bar.
/// </summary>
[Collection("E2E")]
public class NavigationE2ETests
{
  private readonly PlaywrightFixture _fixture;

  public NavigationE2ETests(PlaywrightFixture fixture)
  {
    _fixture = fixture;
  }

  [Fact]
  public async Task Navigation_QueuePage_LoadsSuccessfully()
  {
    if (!_fixture.IsServerAvailable) return;
    await _fixture.Page.GotoAsync($"{_fixture.BaseUrl}/queue");
    await _fixture.Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

    // Queue page should have its header and action buttons
    var heading = _fixture.Page.Locator("text=Queue").First;
    await Expect(heading).ToBeVisibleAsync();
  }

  [Fact]
  public async Task Navigation_RadioPage_LoadsSuccessfully()
  {
    if (!_fixture.IsServerAvailable) return;
    await _fixture.Page.GotoAsync($"{_fixture.BaseUrl}/radio");
    await _fixture.Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

    var heading = _fixture.Page.Locator("text=Radio Control").First;
    await Expect(heading).ToBeVisibleAsync();
  }

  [Fact]
  public async Task Navigation_DevicesPage_LoadsSuccessfully()
  {
    if (!_fixture.IsServerAvailable) return;
    await _fixture.Page.GotoAsync($"{_fixture.BaseUrl}/devices");
    await _fixture.Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

    var heading = _fixture.Page.Locator("text=Device Management").First;
    await Expect(heading).ToBeVisibleAsync();
  }

  [Fact]
  public async Task Navigation_HistoryPage_LoadsSuccessfully()
  {
    if (!_fixture.IsServerAvailable) return;
    await _fixture.Page.GotoAsync($"{_fixture.BaseUrl}/history");
    await _fixture.Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

    var heading = _fixture.Page.Locator("text=Play History").First;
    await Expect(heading).ToBeVisibleAsync();
  }

  [Fact]
  public async Task Navigation_MetricsPage_LoadsSuccessfully()
  {
    if (!_fixture.IsServerAvailable) return;
    await _fixture.Page.GotoAsync($"{_fixture.BaseUrl}/metrics");
    await _fixture.Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

    // Metrics page shows system metrics
    var heading = _fixture.Page.Locator("text=Metrics").First;
    await Expect(heading).ToBeVisibleAsync();
  }

  [Fact]
  public async Task Navigation_SystemPage_LoadsSuccessfully()
  {
    if (!_fixture.IsServerAvailable) return;
    await _fixture.Page.GotoAsync($"{_fixture.BaseUrl}/system");
    await _fixture.Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

    // System config page
    var content = _fixture.Page.Locator(".content-area");
    await Expect(content).ToBeVisibleAsync();
  }

  [Fact]
  public async Task Navigation_BluetoothPage_LoadsSuccessfully()
  {
    if (!_fixture.IsServerAvailable) return;
    await _fixture.Page.GotoAsync($"{_fixture.BaseUrl}/bluetooth");
    await _fixture.Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

    var content = _fixture.Page.Locator(".content-area");
    await Expect(content).ToBeVisibleAsync();
  }

  [Fact]
  public async Task Navigation_AppBarPersistsAcrossPages()
  {
    if (!_fixture.IsServerAvailable) return;

    // Navigate to several pages and verify the app bar is always present
    var pages = new[] { "/", "/queue", "/radio", "/devices", "/history" };
    foreach (var path in pages)
    {
      await _fixture.Page.GotoAsync($"{_fixture.BaseUrl}{path}");
      await _fixture.Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

      var appBar = _fixture.Page.Locator(".mud-appbar");
      await Expect(appBar).ToBeVisibleAsync();

      // Volume control should always be visible in the app bar
      var volumeText = _fixture.Page.Locator("text=Volume:");
      await Expect(volumeText).ToBeVisibleAsync();
    }
  }
}

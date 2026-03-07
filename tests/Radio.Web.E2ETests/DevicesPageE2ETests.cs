using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace Radio.Web.E2ETests;

/// <summary>
/// E2E tests for the Device Management page — output/input devices, Cast devices.
/// </summary>
[Collection("E2E")]
public class DevicesPageE2ETests
{
  private readonly PlaywrightFixture _fixture;

  public DevicesPageE2ETests(PlaywrightFixture fixture)
  {
    _fixture = fixture;
  }

  [Fact]
  public async Task DevicesPage_HasRefreshButton()
  {
    if (!_fixture.IsServerAvailable)
    {
      return;
    }

    await _fixture.Page.GotoAsync($"{_fixture.BaseUrl}/devices");
    await _fixture.Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

    var refreshButton = _fixture.Page.Locator("button:has-text('Refresh Devices')");
    await Expect(refreshButton).ToBeVisibleAsync();
  }

  [Fact]
  public async Task DevicesPage_HasOutputDevicesSection()
  {
    if (!_fixture.IsServerAvailable)
    {
      return;
    }

    await _fixture.Page.GotoAsync($"{_fixture.BaseUrl}/devices");
    await _fixture.Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

    var heading = _fixture.Page.Locator("text=Output Devices").First;
    await Expect(heading).ToBeVisibleAsync();
  }

  [Fact]
  public async Task DevicesPage_HasCastDevicesSection()
  {
    if (!_fixture.IsServerAvailable)
    {
      return;
    }

    await _fixture.Page.GotoAsync($"{_fixture.BaseUrl}/devices");
    await _fixture.Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

    var heading = _fixture.Page.Locator("text=Google Cast Devices").First;
    await Expect(heading).ToBeVisibleAsync();

    var scanButton = _fixture.Page.Locator("button:has-text('Scan for New Devices')");
    await Expect(scanButton).ToBeVisibleAsync();
  }

  [Fact]
  public async Task DevicesPage_HasInputDevicesSection()
  {
    if (!_fixture.IsServerAvailable)
    {
      return;
    }

    await _fixture.Page.GotoAsync($"{_fixture.BaseUrl}/devices");
    await _fixture.Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

    var heading = _fixture.Page.Locator("text=Input Devices").First;
    await Expect(heading).ToBeVisibleAsync();
  }
}

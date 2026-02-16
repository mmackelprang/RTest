using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;
using Xunit;

namespace Radio.Web.E2ETests;

/// <summary>
/// E2E tests for the Radio page — band selection, frequency controls, presets, scanning.
/// </summary>
[Collection("E2E")]
public class RadioPageE2ETests
{
  private readonly PlaywrightFixture _fixture;

  public RadioPageE2ETests(PlaywrightFixture fixture)
  {
    _fixture = fixture;
  }

  [Fact]
  public async Task RadioPage_HasFrequencyDisplay()
  {
    if (!_fixture.IsServerAvailable) return;
    await _fixture.Page.GotoAsync($"{_fixture.BaseUrl}/radio");
    await _fixture.Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

    // The radio page should show a frequency display (large text with MHz/kHz)
    var freqDisplay = _fixture.Page.Locator("text=/\\d+\\.?\\d*\\s*(MHz|kHz)/i").First;
    await Expect(freqDisplay).ToBeVisibleAsync();
  }

  [Fact]
  public async Task RadioPage_HasFrequencyButtons()
  {
    if (!_fixture.IsServerAvailable) return;
    await _fixture.Page.GotoAsync($"{_fixture.BaseUrl}/radio");
    await _fixture.Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

    var freqDown = _fixture.Page.Locator("button[title='Frequency Down']");
    await Expect(freqDown).ToBeVisibleAsync();

    var freqUp = _fixture.Page.Locator("button[title='Frequency Up']");
    await Expect(freqUp).ToBeVisibleAsync();
  }

  [Fact]
  public async Task RadioPage_HasBandSelector()
  {
    if (!_fixture.IsServerAvailable) return;
    await _fixture.Page.GotoAsync($"{_fixture.BaseUrl}/radio");
    await _fixture.Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

    // Band buttons are MudButton pills — at least FM should be present
    var fmButton = _fixture.Page.Locator("button:has-text('FM')").First;
    await Expect(fmButton).ToBeVisibleAsync();
  }

  [Fact]
  public async Task RadioPage_HasScanControls()
  {
    if (!_fixture.IsServerAvailable) return;
    await _fixture.Page.GotoAsync($"{_fixture.BaseUrl}/radio");
    await _fixture.Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

    var scanUp = _fixture.Page.Locator("button:has-text('Scan Up')");
    await Expect(scanUp).ToBeVisibleAsync();

    var scanDown = _fixture.Page.Locator("button:has-text('Scan Down')");
    await Expect(scanDown).ToBeVisibleAsync();
  }

  [Fact]
  public async Task RadioPage_HasPresetsSection()
  {
    if (!_fixture.IsServerAvailable) return;
    await _fixture.Page.GotoAsync($"{_fixture.BaseUrl}/radio");
    await _fixture.Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

    // Presets section with save button
    var savePreset = _fixture.Page.Locator("button:has-text('Save Current')");
    await Expect(savePreset).ToBeVisibleAsync();
  }
}

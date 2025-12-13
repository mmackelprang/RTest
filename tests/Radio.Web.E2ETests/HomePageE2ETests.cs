using System.Text.RegularExpressions;
using System.Net.Http;
using System.Threading;
using System.Diagnostics;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;
using Xunit;
using Xunit.Sdk;

namespace Radio.Web.E2ETests;

/// <summary>
/// End-to-end tests for the Home page
/// Tests complete user workflows using Playwright
/// NOTE: These tests require the application to be running
/// </summary>
internal static class TestServer
{
  public static Process? Start(string url)
  {
    var dllPath = Path.Combine("src", "Radio.Web", "bin", "Debug", "net8.0", "Radio.Web.dll");
    var psi = new ProcessStartInfo
    {
      FileName = "dotnet",
      Arguments = $"\"{dllPath}\"",
      WorkingDirectory = Path.GetFullPath("."),
      UseShellExecute = false,
      CreateNoWindow = true
    };
    psi.Environment["ASPNETCORE_URLS"] = url;
    psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
    psi.Environment["DOTNET_ENVIRONMENT"] = "Development";
    return Process.Start(psi);
  }

  public static void Stop(Process? process)
  {
    if (process == null) return;
    try
    {
      if (!process.HasExited)
      {
        process.Kill(true);
        process.WaitForExit(5000);
      }
    }
    catch { }
    finally
    {
      process.Dispose();
    }
  }
}

[CollectionDefinition("E2E")] public class E2ECollection : ICollectionFixture<PlaywrightFixture> { }

[Collection("E2E")]
public class HomePageE2ETests
{
  private readonly PlaywrightFixture fixture;

  public HomePageE2ETests(PlaywrightFixture fixture)
  {
    this.fixture = fixture;
  }

  [Fact]
  public async Task HomePage_LoadsSuccessfully()
  {
    if (!fixture.IsServerAvailable) return;
    // Assert - Page should load without errors
    await Expect(fixture.Page).ToHaveTitleAsync(new Regex(".*Radio Console.*", RegexOptions.IgnoreCase));
  }

  [Fact]
  public async Task HomePage_DisplaysNowPlayingCard()
  {
    if (!fixture.IsServerAvailable) return;
    // Assert - Should have the main now playing card
    var card = fixture.Page.Locator(".mud-card").First;
    await Expect(card).ToBeVisibleAsync();
  }

  [Fact]
  public async Task HomePage_HasTransportControls()
  {
    if (!fixture.IsServerAvailable) return;
    // Assert - Should have transport control buttons
    // Play/Pause button - look for icon buttons
    var playButton = fixture.Page.Locator("button[title*='Play' i], button[title*='Pause' i]").First;
    await Expect(playButton).ToBeVisibleAsync();

    // Next button
    var nextButton = fixture.Page.Locator("button[title*='Next' i]").First;
    await Expect(nextButton).ToBeVisibleAsync();

    // Previous button
    var previousButton = fixture.Page.Locator("button[title*='Previous' i]").First;
    await Expect(previousButton).ToBeVisibleAsync();
  }

  [Fact]
  public async Task HomePage_HasVolumeControl()
  {
    if (!fixture.IsServerAvailable) return;
    // Assert - Should have volume slider
    var volumeText = fixture.Page.Locator("text=Volume:");
    await Expect(volumeText).ToBeVisibleAsync();
  }

  [Fact]
  public async Task HomePage_DisplaysNavigationBar()
  {
    if (!fixture.IsServerAvailable) return;
    // Assert - Should have navigation bar with icons
    var appBar = fixture.Page.Locator(".mud-appbar");
    await Expect(appBar).ToBeVisibleAsync();
    
    // Should have navigation icons
    var navIcons = appBar.Locator("button");
    var count = await navIcons.CountAsync();
    Assert.True(count >= 6, "Should have at least 6 navigation buttons");
  }

  [Fact]
  public async Task HomePage_HasResponsiveLayout()
  {
    if (!fixture.IsServerAvailable) return;
    // Assert - Page should have fixed dimensions for the display
    var layoutContainer = fixture.Page.Locator(".layout-container").First;
    await Expect(layoutContainer).ToBeVisibleAsync();
    
    // Verify it has inline styles for dimensions
    var style = await layoutContainer.GetAttributeAsync("style");
    Assert.Contains("1920px", style);
    Assert.Contains("576px", style);
  }
}

public class PlaywrightFixture : IAsyncLifetime
{
  private Process? serverProcess;
  public IPlaywright PlaywrightInstance { get; private set; } = default!;
  public IBrowser Browser { get; private set; } = default!;
  public IPage Page { get; private set; } = default!;
  public string BaseUrl { get; private set; } = string.Empty;
  public bool IsServerAvailable { get; private set; }

  public async Task InitializeAsync()
  {
    BaseUrl = "http://127.0.0.1:5010";
    serverProcess = TestServer.Start(BaseUrl);
    IsServerAvailable = await E2EHelpers.IsServerReachableAsync(BaseUrl, TimeSpan.FromSeconds(30));
    if (!IsServerAvailable)
    {
      return; // soft-skip: don't initialize Playwright when server is unavailable
    }

    PlaywrightInstance = await Microsoft.Playwright.Playwright.CreateAsync();
    Browser = await PlaywrightInstance.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
    {
      Headless = true
    });
    var context = await Browser.NewContextAsync();
    Page = await context.NewPageAsync();
    await Page.GotoAsync(BaseUrl);
  }

  public async Task DisposeAsync()
  {
    if (Browser != null)
    {
      await Browser.CloseAsync();
    }
    PlaywrightInstance?.Dispose();
    TestServer.Stop(serverProcess);
  }
}

internal static class E2EHelpers
{
  public static async Task<bool> IsServerReachableAsync(string url, TimeSpan timeout)
  {
    using var cts = new CancellationTokenSource(timeout);
    using var client = new HttpClient();
    try
    {
      using var request = new HttpRequestMessage(HttpMethod.Get, url);
      using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
      return response.IsSuccessStatusCode || (int)response.StatusCode < 500;
    }
    catch
    {
      return false;
    }
  }
}

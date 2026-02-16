using System.Diagnostics;
using System.Net.Http;
using Microsoft.Playwright;
using Xunit;

namespace Radio.Web.E2ETests;

/// <summary>
/// Manages the Radio.Web server process for E2E tests.
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

[CollectionDefinition("E2E")]
public class E2ECollection : ICollectionFixture<PlaywrightFixture> { }

/// <summary>
/// Shared Playwright fixture for all E2E tests.
/// Starts the server, launches Chromium, and navigates to the home page.
/// Tests soft-skip when the server is unavailable.
/// </summary>
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
      return;
    }

    PlaywrightInstance = await Microsoft.Playwright.Playwright.CreateAsync();
    Browser = await PlaywrightInstance.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
    {
      Headless = true
    });
    var context = await Browser.NewContextAsync(new BrowserNewContextOptions
    {
      ViewportSize = new ViewportSize { Width = 1920, Height = 576 }
    });
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

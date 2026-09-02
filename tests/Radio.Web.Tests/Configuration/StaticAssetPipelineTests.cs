namespace Radio.Web.Tests.Configuration;

using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

/// <summary>
/// Drives a real static-file request through the real Radio.Web pipeline.
/// </summary>
/// <remarks>
/// <see cref="StaticAssetCachingTests"/> pins the policy object; this pins that Program.cs actually
/// installs it. That is the gap worth closing here: the whole of OPS-5 rests on one argument at one
/// call site, and a rebase that dropped it — restoring the bare <c>app.UseStaticFiles()</c> — would
/// reintroduce the exact stale-asset bug with every policy unit test still green.
/// </remarks>
public class StaticAssetPipelineTests
  : IClassFixture<StaticAssetPipelineTests.WebFactory>
{
  private readonly WebFactory _factory;

  public StaticAssetPipelineTests(WebFactory factory) => _factory = factory;

  [Theory]
  [InlineData("/css/design-system.css")]
  [InlineData("/js/idle-dimmer.js")]
  [InlineData("/fonts/DSEG14Classic-Regular.woff2")]
  public async Task StaticAssets_AreServedWithRevalidateAlwaysCacheControl(string path)
  {
    var client = _factory.CreateClient();

    var response = await client.GetAsync(path);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
  }

  /// <summary>
  /// The header is what makes revalidation happen; the <c>ETag</c> is what makes it cheap. A policy
  /// that forced revalidation without an entity tag would turn every asset into a full re-download,
  /// so this pins that both halves are present on the same response.
  /// </summary>
  [Fact]
  public async Task StaticAssets_StillCarryAnETagToRevalidateAgainst()
  {
    var client = _factory.CreateClient();

    var response = await client.GetAsync("/css/design-system.css");

    Assert.NotNull(response.Headers.ETag);
  }

  /// <summary>
  /// Minimal host for Radio.Web. Mirrors the lessons already paid for in
  /// <c>Radio.API.Tests.TestSupport.CustomWebApplicationFactory</c>: hosted services removed so no
  /// background poll outlives the test, and every relative storage path redirected onto a private
  /// root so concurrent hosts never share a SQLite file or a DataProtection key ring.
  /// </summary>
  public sealed class WebFactory : WebApplicationFactory<Program>
  {
    private readonly string _storageRoot =
      Path.Combine(Path.GetTempPath(), "radio-web-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
      // Program.cs configures the static Serilog logger before the factory applies any override,
      // so replace it here or the run fills with connection-refused noise from this host.
      Log.Logger = new LoggerConfiguration().MinimumLevel.Fatal().CreateLogger();

      builder.UseEnvironment("Testing");

      builder.ConfigureAppConfiguration((_, config) =>
      {
        var data = Path.Combine(_storageRoot, "data");
        config.AddInMemoryCollection(new Dictionary<string, string?>
        {
          ["Database:RootPath"] = data,
          ["DataProtection:KeysPath"] = Path.Combine(data, "keys-web"),
        });
      });

      builder.ConfigureServices(services =>
      {
        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
        {
          services.Remove(descriptor);
        }
      });
    }

    protected override void Dispose(bool disposing)
    {
      base.Dispose(disposing);

      if (!disposing)
      {
        return;
      }

      // Best-effort: a leftover temp directory is harmless, and throwing here would turn a passing
      // test into a failing one during teardown.
      try
      {
        if (Directory.Exists(_storageRoot))
        {
          Directory.Delete(_storageRoot, recursive: true);
        }
      }
      catch
      {
        // Ignored - SQLite can still hold a handle on Windows when the host tears down.
      }
    }
  }
}

namespace Radio.Web.Tests.TestHelpers;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

/// <summary>
/// Minimal in-process host for Radio.Web, shared by every test that needs to drive a real request
/// through the real Program.cs pipeline.
/// </summary>
/// <remarks>
/// <para>
/// Extracted verbatim from <c>StaticAssetPipelineTests.WebFactory</c> (OPS-5) when UI-11 became its
/// second consumer. Mirrors the lessons already paid for in
/// <c>Radio.API.Tests.TestSupport.CustomWebApplicationFactory</c>: hosted services removed so no
/// background poll outlives the test, and every relative storage path redirected onto a private root
/// so concurrent hosts never share a SQLite file or a DataProtection key ring.
/// </para>
/// <para>
/// xUnit constructs one instance per test class holding it as an <c>IClassFixture</c>, so two classes
/// mean two hosts. That is safe precisely because of the per-instance <see cref="Guid"/> root below —
/// do not "optimise" it into a shared static.
/// </para>
/// </remarks>
public sealed class RadioWebFactory : WebApplicationFactory<Program>
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

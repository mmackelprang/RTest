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
/// Extracted from <c>StaticAssetPipelineTests.WebFactory</c> (OPS-5) when UI-11 became its second
/// consumer. Every relative storage path is redirected onto a private root so concurrent hosts never
/// share a SQLite file or a DataProtection key ring.
/// </para>
/// <para>
/// ⚠ <b>The <c>IHostedService</c> removal below stops exactly two things, and it is worth naming them
/// rather than claiming "no background work outlives the test".</b> It removes
/// <c>GvBridgeStatusService</c> and <c>BellHealthService</c> — the only two <c>AddHostedService</c>
/// registrations. It does <b>not</b> stop <c>PhoneHubService</c> and <c>GvTrunkHubService</c>: those
/// are <c>AddSingleton</c> and started fire-and-forget from <c>Program.cs</c> outside the hosted-service
/// mechanism, so they start regardless and retry on a 10/30/60 s ladder until disposed. It also does
/// not remove <c>GenericWebHostService</c> — proved by the fact these tests get real responses at all.
/// </para>
/// <para>
/// That is why the two RotaryPhone endpoints are pointed at a dead port below. Left at their
/// <c>appsettings.json</c> defaults they resolve to <c>http://radio:5004</c> — <b>the live appliance</b>
/// — so every test run opened real SignalR connections to it and retried against it for the life of
/// the host. On a box where log volume correlates with audible audio distortion that is not a
/// theoretical cost, and a unit test has no business reaching the network at all.
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

        // Port 1 is a reserved, never-listening port, so these fail instantly and locally instead of
        // reaching http://radio:5004 (the appsettings.json default, i.e. the live appliance). See the
        // remarks above: the hosted-service removal cannot stop these two clients, so the connection
        // has to be made unreachable rather than un-started.
        ["RotaryPhone:HubUrl"] = "http://127.0.0.1:1/hub",
        ["RotaryPhone:ApiBaseUrl"] = "http://127.0.0.1:1",
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

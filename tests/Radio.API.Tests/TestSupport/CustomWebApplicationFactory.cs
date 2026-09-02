using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace Radio.API.Tests.TestSupport;

/// <summary>
/// Custom WebApplicationFactory used by tests to alter the test host environment
/// and prevent background hosted services from running during controller/integration tests.
/// This avoids background services interacting with SQLite during host disposal which
/// can lead to cleanup errors such as "cannot rollback - no transaction is active".
///
/// <para>
/// <b>Each instance gets its own storage root (TEST-3).</b> Every path in appsettings.json is
/// relative — <c>Database:RootPath</c> is <c>"./data"</c> — and nothing overrode them for tests,
/// so all 17 classes holding an <c>IClassFixture&lt;CustomWebApplicationFactory&lt;Program&gt;&gt;</c>
/// opened the <i>same</i> SQLite files concurrently. Under load that surfaced as lock/busy errors
/// escaping as unhandled 500s: <c>Load_WithNonExistentId_ReturnsNotFound</c> got
/// <c>InternalServerError</c> instead of <c>NotFound</c>, and it failed CI on PR #485.
/// </para>
///
/// <para>
/// The tell was always which tests failed — only the <i>first-executed</i> test of each class, and
/// only under full-suite load, because that is when the hosts collide while initialising storage.
/// Capping test parallelism would have made the collision rarer; giving each host its own
/// directory removes the shared state the collision needs.
/// </para>
/// </summary>
public class CustomWebApplicationFactory<TProgram> : WebApplicationFactory<TProgram> where TProgram : class
{
  private static readonly string StorageBase =
    Path.Combine(Path.GetTempPath(), "radio-api-tests");

  private readonly string _storageRoot = Path.Combine(StorageBase, Guid.NewGuid().ToString("N"));

  static CustomWebApplicationFactory()
  {
    // Sweep roots left behind by earlier runs. Dispose is best-effort — on Windows SQLite can
    // still hold a handle when the host tears down, so roughly half of each run's directories
    // survive. Without this they accumulate every run forever.
    //
    // The one-hour floor is what makes this safe to run from a static constructor: a test run
    // takes minutes, so anything older cannot belong to a live host, including one in a
    // concurrently-executing test assembly.
    try
    {
      if (!Directory.Exists(StorageBase))
      {
        return;
      }

      DateTime cutoff = DateTime.UtcNow.AddHours(-1);
      foreach (string dir in Directory.EnumerateDirectories(StorageBase))
      {
        try
        {
          if (Directory.GetCreationTimeUtc(dir) < cutoff)
          {
            Directory.Delete(dir, recursive: true);
          }
        }
        catch
        {
          // Ignored — another process may hold it, or it may already be gone.
        }
      }
    }
    catch
    {
      // Ignored — housekeeping must never fail a test run.
    }
  }

  /// <summary>The isolated storage root for this host. Exposed so a test can assert isolation.</summary>
  public string StorageRoot => _storageRoot;

  protected override void ConfigureWebHost(IWebHostBuilder builder)
  {
    // Suppress Serilog console output during tests. The static Log.Logger is configured
    // in Program.cs before WebApplicationFactory applies environment overrides, so we
    // must replace it here to prevent noisy ERR/WRN stack traces in test output.
    Log.Logger = new LoggerConfiguration()
      .MinimumLevel.Fatal()
      .CreateLogger();

    // Run the host in a test environment
    builder.UseEnvironment("Testing");

    // Redirect every relative storage path onto this instance's own root. Added after the
    // appsettings sources so these win. Each key here is a path that appsettings.json declares
    // relative to the process working directory, which is shared by every host in the run.
    builder.ConfigureAppConfiguration((_, config) =>
    {
      string data = Path.Combine(_storageRoot, "data");
      config.AddInMemoryCollection(new Dictionary<string, string?>
      {
        ["Database:RootPath"] = data,
        ["DataProtection:KeysPath"] = Path.Combine(data, "keys"),
        ["ManagedConfiguration:BasePath"] = Path.Combine(_storageRoot, "config"),
        ["ManagedConfiguration:BackupPath"] = Path.Combine(_storageRoot, "config", "backups"),
        ["Metrics:DatabasePath"] = Path.Combine(data, "metrics.db"),
        ["Fingerprinting:DatabasePath"] = Path.Combine(data, "fingerprints.db"),
        ["Diagnostics:CaptureBaseDirectory"] = Path.Combine(data, "diagnostics"),
      });
    });

    builder.ConfigureServices(services =>
    {
      // Remove all registered hosted services to prevent background tasks from running
      // during tests. Background tasks may access databases and outlive expected
      // transaction lifetimes which can cause Sqlite rollback errors on host dispose.
      var hostedDescriptors = services.Where(d => d.ServiceType == typeof(IHostedService)).ToList();
      foreach (var desc in hostedDescriptors)
      {
        services.Remove(desc);
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
      // Ignored — SQLite may still hold a handle briefly on Windows.
    }
  }
}

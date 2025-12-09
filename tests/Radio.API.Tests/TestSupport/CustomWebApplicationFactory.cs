using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Radio.API.Tests.TestSupport;

/// <summary>
/// Custom WebApplicationFactory used by tests to alter the test host environment
/// and prevent background hosted services from running during controller/integration tests.
/// This avoids background services interacting with SQLite during host disposal which
/// can lead to cleanup errors such as "cannot rollback - no transaction is active".
/// </summary>
public class CustomWebApplicationFactory<TProgram> : WebApplicationFactory<TProgram> where TProgram : class
{
  protected override void ConfigureWebHost(IWebHostBuilder builder)
  {
    // Run the host in a test environment
    builder.UseEnvironment("Testing");

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

      // Additionally remove specific long-running services by type name if present.
      // This is defensive: remove things like MetricsCollector, BufferedMetricsCollector,
      // BackgroundIdentificationService, etc., which may have IHostedService registrations
      // or other disposable resources that interact with SQLite during disposal.
      var suspicious = services
        .Where(d => d.ServiceType != null && d.ServiceType.FullName != null &&
          (d.ServiceType.FullName.Contains("Metrics", System.StringComparison.OrdinalIgnoreCase)
           || d.ServiceType.FullName.Contains("Identification", System.StringComparison.OrdinalIgnoreCase)
           || d.ServiceType.FullName.Contains("Fingerprint", System.StringComparison.OrdinalIgnoreCase)))
        .ToList();

      foreach (var desc in suspicious)
      {
        services.Remove(desc);
      }
    });
  }
}

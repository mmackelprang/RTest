using System.Linq;
using Microsoft.AspNetCore.Hosting;
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
/// </summary>
public class CustomWebApplicationFactory<TProgram> : WebApplicationFactory<TProgram> where TProgram : class
{
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
}

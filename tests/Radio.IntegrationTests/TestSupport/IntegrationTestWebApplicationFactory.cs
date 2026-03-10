using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Radio.Core.Interfaces.Audio;
using Serilog;
using Serilog.Events;

namespace Radio.IntegrationTests.TestSupport;

/// <summary>
/// Extended WebApplicationFactory for integration tests with configurable options.
/// </summary>
public class IntegrationTestWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
  private readonly string _testId = Guid.NewGuid().ToString("N");
  private string? _tempDirectory;

  /// <summary>
  /// Gets the isolated temp directory for this test instance.
  /// </summary>
  public string TempDirectory => _tempDirectory ?? throw new InvalidOperationException("Factory not initialized");

  /// <summary>
  /// Gets or sets whether to enable background services. Default is false.
  /// </summary>
  public bool EnableBackgroundServices { get; set; } = false;

  /// <summary>
  /// Gets or sets whether to enable the real audio engine. Default is false.
  /// </summary>
  public bool EnableRealAudioEngine { get; set; } = false;

  /// <summary>
  /// Gets or sets whether to use mock fingerprinting services. Default is true.
  /// </summary>
  public bool UseMockFingerprinting { get; set; } = true;

  /// <summary>
  /// Gets or sets a custom service configuration action.
  /// </summary>
  public Action<IServiceCollection>? ConfigureTestServices { get; set; }

  public Task InitializeAsync()
  {
    _tempDirectory = Path.Combine(Path.GetTempPath(), $"RadioIntegrationTests_{_testId}");
    Directory.CreateDirectory(_tempDirectory);
    Directory.CreateDirectory(Path.Combine(_tempDirectory, "config"));
    Directory.CreateDirectory(Path.Combine(_tempDirectory, "metrics"));
    Directory.CreateDirectory(Path.Combine(_tempDirectory, "fingerprints"));
    Directory.CreateDirectory(Path.Combine(_tempDirectory, "logs"));
    return Task.CompletedTask;
  }

  public new async Task DisposeAsync()
  {
    await base.DisposeAsync();

    // Clear SQLite connection pool to release file handles
    SqliteConnection.ClearAllPools();

    // Give time for resources to be released
    await Task.Delay(100);

    // Clean up temp directory
    if (_tempDirectory != null && Directory.Exists(_tempDirectory))
    {
      try
      {
        Directory.Delete(_tempDirectory, recursive: true);
      }
      catch (IOException)
      {
        // Ignore cleanup errors - Windows may still have file handles
      }
    }
  }

  protected override void ConfigureWebHost(IWebHostBuilder builder)
  {
    // Suppress Serilog console output during tests
    Log.Logger = new LoggerConfiguration()
      .MinimumLevel.Override("Microsoft", LogEventLevel.Fatal)
      .MinimumLevel.Fatal()
      .CreateLogger();

    builder.UseEnvironment("IntegrationTests");

    // Override configuration to use temp directory
    builder.ConfigureAppConfiguration((context, config) =>
    {
      config.AddInMemoryCollection(new Dictionary<string, string?>
      {
        ["Database:RootPath"] = TempDirectory,
        ["Database:ConfigurationSubdirectory"] = "config",
        ["Database:ConfigurationFileName"] = "configuration.db",
        ["Database:MetricsSubdirectory"] = "metrics",
        ["Database:MetricsFileName"] = "metrics.db",
        ["Database:FingerprintsSubdirectory"] = "fingerprints",
        ["Database:FingerprintsFileName"] = "fingerprints.db",
        ["Fingerprinting:Enabled"] = UseMockFingerprinting ? "true" : "false",
        ["Fingerprinting:IdentificationIntervalSeconds"] = "30",
        ["Fingerprinting:SampleDurationSeconds"] = "10",
        ["Logging:LogLevel:Default"] = "Warning"
      });
    });

    builder.ConfigureServices(services =>
    {
      // Remove background services if not enabled
      if (!EnableBackgroundServices)
      {
        var hostedServices = services.Where(d => d.ServiceType == typeof(IHostedService)).ToList();
        foreach (var service in hostedServices)
        {
          services.Remove(service);
        }

        // Also remove fingerprint and identification related services
        // Note: We do NOT remove Metrics services as SignalR needs HttpConnectionsMetrics
        var suspiciousServices = services
          .Where(d => d.ServiceType?.FullName != null &&
            (d.ServiceType.FullName.Contains("Identification", StringComparison.OrdinalIgnoreCase) ||
             d.ServiceType.FullName.Contains("BackgroundIdentification", StringComparison.OrdinalIgnoreCase)))
          .ToList();

        foreach (var service in suspiciousServices)
        {
          services.Remove(service);
        }
      }

      // Replace audio services with mocks if not using real audio
      if (!EnableRealAudioEngine)
      {
        // Remove real audio sample provider and replace with mock
        var audioProviderDescriptor = services.FirstOrDefault(d =>
          d.ServiceType == typeof(IAudioSampleProvider));
        if (audioProviderDescriptor != null)
        {
          services.Remove(audioProviderDescriptor);
        }
        services.AddSingleton<IAudioSampleProvider, MockAudioSampleProvider>();
      }

      // Replace metadata lookup with mock if using mock fingerprinting
      if (UseMockFingerprinting)
      {
        var lookupDescriptor = services.FirstOrDefault(d =>
          d.ServiceType == typeof(IMetadataLookupService));
        if (lookupDescriptor != null)
        {
          services.Remove(lookupDescriptor);
        }
        services.AddSingleton<IMetadataLookupService, MockMetadataLookupService>();
      }

      // Apply custom test services
      ConfigureTestServices?.Invoke(services);
    });
  }

  /// <summary>
  /// Creates a new scope for resolving scoped services.
  /// </summary>
  public IServiceScope CreateScope()
  {
    return Services.CreateScope();
  }

  /// <summary>
  /// Gets a service from the container.
  /// </summary>
  public T? GetService<T>() where T : class
  {
    return Services.GetService<T>();
  }

  /// <summary>
  /// Gets a required service from the container.
  /// </summary>
  public T GetRequiredService<T>() where T : notnull
  {
    return Services.GetRequiredService<T>();
  }
}

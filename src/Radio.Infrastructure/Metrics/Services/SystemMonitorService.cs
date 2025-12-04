namespace Radio.Infrastructure.Metrics.Services;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using System.Diagnostics;

/// <summary>
/// Background service that monitors system health metrics.
/// Periodically collects metrics for disk usage, memory, CPU temperature, etc.
/// </summary>
public sealed class SystemMonitorService : BackgroundService
{
  private readonly ILogger<SystemMonitorService> _logger;
  private readonly MetricsOptions _options;
  private readonly IMetricsCollector _metricsCollector;
  private readonly TimeSpan _collectInterval = TimeSpan.FromMinutes(5);

  public SystemMonitorService(
    ILogger<SystemMonitorService> logger,
    IOptions<MetricsOptions> options,
    IMetricsCollector metricsCollector)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    if (!_options.Enabled)
    {
      _logger.LogInformation("Metrics collection is disabled, skipping system monitor");
      return;
    }

    _logger.LogInformation("System monitor service started. Collecting metrics every {Interval}",
      _collectInterval);

    // Wait a bit before first collection
    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        await CollectSystemMetricsAsync(stoppingToken);

        // Wait for next collection
        await Task.Delay(_collectInterval, stoppingToken);
      }
      catch (OperationCanceledException)
      {
        // Expected when stopping
        break;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error collecting system metrics");
        
        // Wait a bit before retrying
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
      }
    }

    _logger.LogInformation("System monitor service stopped");
  }

  private async Task CollectSystemMetricsAsync(CancellationToken ct)
  {
    // Memory usage
    try
    {
      var process = Process.GetCurrentProcess();
      var memoryMb = process.WorkingSet64 / 1024.0 / 1024.0;
      _metricsCollector.Gauge("system.memory_usage_mb", memoryMb);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to collect memory usage metric");
    }

    // Disk usage
    try
    {
      var dbPath = Path.GetFullPath(_options.DatabasePath);
      var rootPath = Path.GetPathRoot(dbPath);
      
      // Handle cases where GetPathRoot returns null or empty
      if (string.IsNullOrEmpty(rootPath))
      {
        rootPath = OperatingSystem.IsWindows() ? "C:\\" : "/";
      }
      
      var drive = new DriveInfo(rootPath);
      if (drive.IsReady)
      {
        var usedPercent = ((drive.TotalSize - drive.AvailableFreeSpace) / (double)drive.TotalSize) * 100.0;
        _metricsCollector.Gauge("system.disk_usage_percent", usedPercent);
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to collect disk usage metric");
    }

    // Database file size
    try
    {
      var dbPath = Path.GetFullPath(_options.DatabasePath);
      if (File.Exists(dbPath))
      {
        var fileInfo = new FileInfo(dbPath);
        var sizeMb = fileInfo.Length / 1024.0 / 1024.0;
        _metricsCollector.Gauge("db.file_size_mb", sizeMb);
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to collect database file size metric");
    }

    // CPU Temperature (Raspberry Pi specific)
    try
    {
      if (OperatingSystem.IsLinux())
      {
        var tempPath = "/sys/class/thermal/thermal_zone0/temp";
        if (File.Exists(tempPath))
        {
          var tempStr = await File.ReadAllTextAsync(tempPath, ct);
          if (int.TryParse(tempStr.Trim(), out var tempMilliC))
          {
            var tempCelsius = tempMilliC / 1000.0;
            _metricsCollector.Gauge("system.cpu_temp_celsius", tempCelsius);
          }
        }
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to collect CPU temperature metric");
    }

    _logger.LogDebug("System metrics collected successfully");
  }
}

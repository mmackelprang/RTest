namespace Radio.Metrics.Services;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
  private readonly IMetricDescriptorRegistry? _descriptorRegistry;
  private readonly TimeSpan _collectInterval = TimeSpan.FromMinutes(5);

  public SystemMonitorService(
    ILogger<SystemMonitorService> logger,
    IOptions<MetricsOptions> options,
    IMetricsCollector metricsCollector,
    IMetricDescriptorRegistry? descriptorRegistry = null)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
    _descriptorRegistry = descriptorRegistry;

    // Register authoritative descriptors for the system metrics this service
    // produces. PR D #11 — replaces the client-side MapKeyToUnit heuristic
    // for these keys with server-side ground truth. Registration happens in
    // the constructor (not ExecuteAsync) so the descriptors are available
    // immediately at service startup, before any sample is collected.
    if (_descriptorRegistry != null)
    {
      _descriptorRegistry.Register(new MetricDescriptor
      {
        Key = "system.memory_usage_mb",
        Unit = MetricUnit.Megabytes,
        Category = "System",
        DisplayName = "Memory Usage",
      });
      _descriptorRegistry.Register(new MetricDescriptor
      {
        Key = "system.disk_usage_percent",
        Unit = MetricUnit.Percent,
        Category = "System",
        DisplayName = "Disk Usage",
        Warn = 80,
        Critical = 95,
      });
      _descriptorRegistry.Register(new MetricDescriptor
      {
        Key = "system.cpu_usage_percent",
        Unit = MetricUnit.Percent,
        Category = "System",
        DisplayName = "CPU Usage",
        Warn = 80,
        Critical = 95,
      });
      // CPU temperature has no matching MetricUnit (no Celsius/Fahrenheit
      // entry today). Register it as Bare so dashboards don't accidentally
      // tag it with the wrong suffix; the dashboard's residual heuristic
      // pulls "°C" from the key suffix when present.
      _descriptorRegistry.Register(new MetricDescriptor
      {
        Key = "system.cpu_temp_celsius",
        Unit = MetricUnit.Bare,
        Category = "System",
        DisplayName = "CPU Temperature",
        Warn = 70,
        Critical = 85,
      });
      _descriptorRegistry.Register(new MetricDescriptor
      {
        Key = "db.file_size_mb",
        Unit = MetricUnit.Megabytes,
        Category = "Database",
        DisplayName = "DB File Size",
      });
    }
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

    // CPU usage (process CPU % across all cores)
    try
    {
      var process = Process.GetCurrentProcess();
      var startTime = DateTime.UtcNow;
      var startCpu = process.TotalProcessorTime;

      await Task.Delay(100, ct); // 100ms sampling window

      process.Refresh();
      var endTime = DateTime.UtcNow;
      var endCpu = process.TotalProcessorTime;

      var cpuUsedMs = (endCpu - startCpu).TotalMilliseconds;
      var elapsedMs = (endTime - startTime).TotalMilliseconds;
      var cpuPercent = (cpuUsedMs / (Environment.ProcessorCount * elapsedMs)) * 100.0;

      _metricsCollector.Gauge("system.cpu_usage_percent", Math.Round(cpuPercent, 1));
    }
    catch (OperationCanceledException) { throw; }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to collect CPU usage metric");
    }

    // CPU Temperature (works on Raspberry Pi, Ubuntu, and other Linux systems)
    try
    {
      if (OperatingSystem.IsLinux())
      {
        for (var i = 0; i < 10; i++)
        {
          var tempPath = $"/sys/class/thermal/thermal_zone{i}/temp";
          if (!File.Exists(tempPath))
          {
            continue;
          }

          try
          {
            var tempStr = await File.ReadAllTextAsync(tempPath, ct);
            if (int.TryParse(tempStr.Trim(), out var tempMilliC))
            {
              var tempCelsius = tempMilliC / 1000.0;

              // Skip sentinel values (absolute zero = sensor unavailable)
              if (tempCelsius < -100 || tempCelsius > 150)
              {
                continue;
              }

              _metricsCollector.Gauge("system.cpu_temp_celsius", tempCelsius);
              break; // Use the first valid reading
            }
          }
          catch (UnauthorizedAccessException) { }
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

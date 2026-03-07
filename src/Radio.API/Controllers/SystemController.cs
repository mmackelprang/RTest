using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Radio.API.Models;
using Radio.Core.Interfaces.Audio;

namespace Radio.API.Controllers;

/// <summary>
/// API controller for system management and monitoring.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class SystemController : ControllerBase
{
  private readonly ILogger<SystemController> _logger;
  private readonly IAudioEngine _audioEngine;
  private readonly IHostApplicationLifetime _applicationLifetime;
  private readonly Services.SleepService? _sleepService;
  private static readonly DateTime _startTime = DateTime.UtcNow;
  
  // Cache CPU usage to avoid delays on every request
  private static double _cachedCpuUsage = 0;
  private static DateTime _lastCpuCheck = DateTime.MinValue;
  private static readonly TimeSpan CpuCacheInterval = TimeSpan.FromSeconds(5);
  
  // Constants for log parsing
  private const int MaxExceptionLines = 50;
  private const int MaxLogFileSizeBytes = 50 * 1024 * 1024; // 50 MB safety limit

  /// <summary>
  /// Initializes a new instance of the SystemController.
  /// </summary>
  public SystemController(
    ILogger<SystemController> logger,
    IAudioEngine audioEngine,
    IHostApplicationLifetime applicationLifetime,
    Services.SleepService? sleepService = null)
  {
    _logger = logger;
    _audioEngine = audioEngine;
    _applicationLifetime = applicationLifetime;
    _sleepService = sleepService;
  }

  /// <summary>
  /// Gets system statistics including CPU, RAM, disk usage, and more.
  /// </summary>
  /// <returns>System statistics.</returns>
  [HttpGet("stats")]
  [ProducesResponseType(typeof(SystemStatsDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status500InternalServerError)]
  public async Task<ActionResult<SystemStatsDto>> GetSystemStats()
  {
    try
    {
      var process = Process.GetCurrentProcess();

      // CPU Usage (cached to avoid delay on every request)
      var cpuUsage = await GetCachedCpuUsageAsync(process);

      // RAM Usage
      var ramUsageMb = process.WorkingSet64 / 1024.0 / 1024.0;

      // Disk Usage
      var diskUsagePercent = GetDiskUsagePercent();

      // Thread Count
      var threadCount = process.Threads.Count;

      // App Uptime
      var appUptime = DateTime.UtcNow - _startTime;
      var appUptimeStr = FormatTimeSpan(appUptime);

      // System Uptime
      var systemUptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
      var systemUptimeStr = FormatTimeSpan(systemUptime);

      // Audio Engine State
      var audioEngineState = GetAudioEngineState();

      // System Temperature
      var temperature = await GetSystemTemperatureAsync();

      var stats = new SystemStatsDto
      {
        CpuUsagePercent = Math.Round(cpuUsage, 2),
        RamUsageMb = Math.Round(ramUsageMb, 2),
        DiskUsagePercent = Math.Round(diskUsagePercent, 2),
        ThreadCount = threadCount,
        AppUptime = appUptimeStr,
        SystemUptime = systemUptimeStr,
        AudioEngineState = audioEngineState,
        SystemTemperature = temperature
      };

      return Ok(stats);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting system stats");
      return StatusCode(500, new { error = "Failed to get system stats" });
    }
  }

  /// <summary>
  /// Gets system logs with filtering options.
  /// </summary>
  /// <param name="level">Log level filter (info, warning, error). Default is warning.</param>
  /// <param name="limit">Maximum number of log lines to return. Default is 100.</param>
  /// <param name="maxAgeMinutes">Maximum age of logs in minutes.</param>
  /// <returns>Filtered system logs.</returns>
  [HttpGet("logs")]
  [ProducesResponseType(typeof(SystemLogsDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status500InternalServerError)]
  public ActionResult<SystemLogsDto> GetSystemLogs(
    [FromQuery] string level = "warning",
    [FromQuery] int limit = 100,
    [FromQuery] int? maxAgeMinutes = null)
  {
    try
    {
      // Validate level
      var validLevels = new[] { "info", "warning", "error" };
      if (!validLevels.Contains(level.ToLowerInvariant()))
      {
        return BadRequest(new
        {
          error = $"Invalid log level: {level}",
          validLevels = validLevels
        });
      }

      // Validate limit
      if (limit < 1 || limit > 10000)
      {
        return BadRequest(new { error = "Limit must be between 1 and 10000" });
      }

      // Read and parse log files
      var logs = ReadLogFiles(level, limit, maxAgeMinutes);

      var response = new SystemLogsDto
      {
        Logs = logs,
        TotalCount = logs.Count,
        Filters = new LogFilterDto
        {
          Level = level,
          Limit = limit,
          MaxAgeMinutes = maxAgeMinutes
        }
      };

      _logger.LogInformation(
        "Log retrieval requested with level={Level}, limit={Limit}, maxAge={MaxAge}. Returned {Count} logs.",
        level, limit, maxAgeMinutes, logs.Count);

      return Ok(response);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting system logs");
      return StatusCode(500, new { error = "Failed to get system logs" });
    }
  }

  private List<LogEntryDto> ReadLogFiles(string level, int limit, int? maxAgeMinutes)
  {
    var logEntries = new List<LogEntryDto>();
    var logsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "logs");

    // Check if logs directory exists
    if (!Directory.Exists(logsDirectory))
    {
      _logger.LogWarning("Logs directory does not exist: {LogsDirectory}", logsDirectory);
      return logEntries;
    }

    // Get all log files matching the pattern, sorted by date (newest first)
    var logFiles = Directory.GetFiles(logsDirectory, "radio-*.txt")
      .OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc)
      .ToList();

    if (logFiles.Count == 0)
    {
      _logger.LogWarning("No log files found in directory: {LogsDirectory}", logsDirectory);
      return logEntries;
    }

    // Regex pattern matching the output template in appsettings.json
    // Format: {Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}
    var logPattern = @"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2}) \[(\w+)\] \[(.*?)\] (.*)$";
    var regex = new Regex(logPattern, RegexOptions.Compiled);

    // Determine minimum timestamp if maxAgeMinutes is specified
    DateTime? minTimestamp = maxAgeMinutes.HasValue
      ? DateTime.UtcNow.AddMinutes(-maxAgeMinutes.Value)
      : null;

    // Normalize level for comparison
    var normalizedLevel = level.ToUpperInvariant();
    var levelPriority = GetLevelPriority(normalizedLevel);

    // Read log files until we have enough entries
    foreach (var logFile in logFiles)
    {
      try
      {
        // Safety check: skip files that are too large to prevent memory issues
        var fileInfo = new FileInfo(logFile);
        if (fileInfo.Length > MaxLogFileSizeBytes)
        {
          _logger.LogWarning(
            "Skipping large log file {LogFile} ({Size} bytes exceeds {MaxSize} bytes limit)",
            logFile, fileInfo.Length, MaxLogFileSizeBytes);
          continue;
        }

        // Read file with sharing to allow concurrent access (Serilog has the file open)
        string[] lines;
        using (var fs = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var sr = new StreamReader(fs))
        {
          lines = sr.ReadToEnd().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }
        
        // Process lines in reverse order (newest first within each file)
        for (int i = lines.Length - 1; i >= 0 && logEntries.Count < limit; i--)
        {
          var line = lines[i];
          if (string.IsNullOrWhiteSpace(line))
          {
            continue;
          }

          var match = regex.Match(line);
          if (!match.Success)
          {
            continue;
          }

          var timestampStr = match.Groups[1].Value;
          var logLevel = match.Groups[2].Value;
          var sourceContext = match.Groups[3].Value;
          var message = match.Groups[4].Value;

          // Parse timestamp
          if (!DateTime.TryParseExact(
            timestampStr,
            "yyyy-MM-dd HH:mm:ss.fff zzz",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var timestamp))
          {
            continue;
          }

          // Convert to UTC for comparison
          var timestampUtc = timestamp.ToUniversalTime();

          // Filter by age
          if (minTimestamp.HasValue && timestampUtc < minTimestamp.Value)
          {
            continue;
          }

          // Filter by level
          var entryLevelPriority = GetLevelPriority(logLevel);
          if (entryLevelPriority < levelPriority)
          {
            continue;
          }

          // Check for exception on next lines
          string? exception = null;
          if (i + 1 < lines.Length && !string.IsNullOrWhiteSpace(lines[i + 1]))
          {
            // Check if next line is part of exception stack trace
            var nextLine = lines[i + 1];
            if (!regex.IsMatch(nextLine))
            {
              // It's likely an exception or continuation
              var exceptionLines = new List<string>();
              for (int j = i + 1; j < lines.Length && j < i + MaxExceptionLines; j++)
              {
                if (string.IsNullOrWhiteSpace(lines[j]) || regex.IsMatch(lines[j]))
                {
                  break;
                }

                exceptionLines.Add(lines[j]);
              }
              if (exceptionLines.Count > 0)
              {
                exception = string.Join(Environment.NewLine, exceptionLines);
              }
            }
          }

          logEntries.Add(new LogEntryDto
          {
            Timestamp = timestamp,
            Level = logLevel,
            Message = message,
            Exception = exception,
            SourceContext = sourceContext
          });
        }

        // Stop if we've collected enough entries
        if (logEntries.Count >= limit)
        {
          break;
        }
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Failed to read log file: {LogFile}", logFile);
        continue;
      }
    }

    return logEntries;
  }

  private static int GetLevelPriority(string level)
  {
    return level.ToUpperInvariant() switch
    {
      "VRB" or "VERBOSE" => 0,
      "DBG" or "DEBUG" => 1,
      "INF" or "INFO" or "INFORMATION" => 2,
      "WRN" or "WARNING" => 3,
      "ERR" or "ERROR" => 4,
      "FTL" or "FATAL" => 5,
      _ => 2 // Default to INFO level
    };
  }

  private async Task<double> GetCachedCpuUsageAsync(Process process)
  {
    var now = DateTime.UtcNow;
    
    // Return cached value if still valid
    if (now - _lastCpuCheck < CpuCacheInterval)
    {
      return _cachedCpuUsage;
    }

    try
    {
      var startTime = DateTime.UtcNow;
      var startCpuUsage = process.TotalProcessorTime;

      // Wait a short period to measure CPU usage
      await Task.Delay(100);

      var endTime = DateTime.UtcNow;
      var endCpuUsage = process.TotalProcessorTime;

      var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
      var totalMsPassed = (endTime - startTime).TotalMilliseconds;
      var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);

      _cachedCpuUsage = cpuUsageTotal * 100;
      _lastCpuCheck = now;
      
      return _cachedCpuUsage;
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to calculate CPU usage");
      return _cachedCpuUsage; // Return last known value
    }
  }

  private double GetDiskUsagePercent()
  {
    try
    {
      var currentDir = Directory.GetCurrentDirectory();
      var rootPath = Path.GetPathRoot(currentDir);

      if (string.IsNullOrEmpty(rootPath))
      {
        rootPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "C:\\" : "/";
      }

      var drive = new DriveInfo(rootPath);
      if (drive.IsReady)
      {
        var usedBytes = drive.TotalSize - drive.AvailableFreeSpace;
        return (usedBytes / (double)drive.TotalSize) * 100.0;
      }

      return 0;
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to get disk usage");
      return 0;
    }
  }

  private string GetAudioEngineState()
  {
    try
    {
      var mixer = _audioEngine.GetMasterMixer();
      var activeSources = mixer.GetActiveSources();
      var primarySource = activeSources.FirstOrDefault(s => s.Category == AudioSourceCategory.Primary);

      if (primarySource != null)
      {
        return $"Active - {primarySource.Type} ({primarySource.State})";
      }

      return "Idle - No active source";
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to get audio engine state");
      return "Unknown";
    }
  }

  private async Task<string> GetSystemTemperatureAsync()
  {
    try
    {
      if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
      {
        // Try thermal zones 0-9. Some systems (Ubuntu on Intel N100) expose the
        // CPU temperature at zone1+ or return a -273300 sentinel at zone0.
        for (var i = 0; i < 10; i++)
        {
          var tempPath = $"/sys/class/thermal/thermal_zone{i}/temp";
          if (!System.IO.File.Exists(tempPath))
          {
            continue;
          }

          try
          {
            var tempStr = await System.IO.File.ReadAllTextAsync(tempPath);
            if (int.TryParse(tempStr.Trim(), out var tempMilliC))
            {
              var tempCelsius = tempMilliC / 1000.0;

              // Skip sentinel values (absolute zero = sensor unavailable)
              if (tempCelsius < -100 || tempCelsius > 150)
              {
                continue;
              }

              return $"{tempCelsius:F1}°C";
            }
          }
          catch (UnauthorizedAccessException)
          {
            // Skip zones we can't read
          }
        }
      }

      return "N/A";
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to read system temperature");
      return "N/A";
    }
  }

  private static string FormatTimeSpan(TimeSpan ts)
  {
    if (ts.TotalDays >= 1)
    {
      return $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m";
    }
    if (ts.TotalHours >= 1)
    {
      return $"{(int)ts.TotalHours}h {ts.Minutes}m {ts.Seconds}s";
    }
    if (ts.TotalMinutes >= 1)
    {
      return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
    }
    return $"{ts.Seconds}s";
  }

  /// <summary>
  /// Gets the current sleep/standby state.
  /// </summary>
  [HttpGet("sleep")]
  [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
  public IActionResult GetSleepState()
  {
    return Ok(new { isSleeping = _sleepService?.IsSleeping ?? false });
  }

  /// <summary>
  /// Sets the sleep/standby state. When sleeping, audio is muted and the UI shows a black overlay.
  /// </summary>
  [HttpPost("sleep")]
  [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
  public async Task<IActionResult> SetSleepState([FromBody] SetSleepRequest request)
  {
    if (_sleepService == null)
    {
      return StatusCode(501, new { error = "Sleep service not available" });
    }

    if (request.Sleep)
    {
      await _sleepService.EnterSleepAsync();
    }
    else
    {
      await _sleepService.WakeAsync("api");
    }

    return Ok(new { isSleeping = _sleepService.IsSleeping });
  }

  /// <summary>
  /// Initiates a graceful shutdown of the application.
  /// This endpoint is useful for E2E UAT testing to cleanly shutdown the application after tests complete.
  /// </summary>
  /// <returns>Confirmation message.</returns>
  [HttpPost("shutdown")]
  [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
  public IActionResult Shutdown()
  {
    _logger.LogWarning("System shutdown requested via API");
    
    // Initiate shutdown after a short delay to allow the response to be sent
    Task.Run(async () =>
    {
      try
      {
        await Task.Delay(1000);
        _logger.LogInformation("Stopping application...");
        _applicationLifetime.StopApplication();
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Failed to execute delayed shutdown");
      }
    });
    
    return Ok(new { message = "Shutdown initiated" });
  }

  /// <summary>
  /// Powers off the entire system (Linux only).
  /// Requires the service user to have passwordless sudo for systemctl poweroff,
  /// or polkit authorization for org.freedesktop.login1.power-off.
  /// </summary>
  [HttpPost("poweroff")]
  [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
  [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
  public IActionResult PowerOff()
  {
    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    {
      return BadRequest(new { error = "System power off is only supported on Linux" });
    }

    _logger.LogWarning("System power off requested via API");

    Task.Run(async () =>
    {
      try
      {
        await Task.Delay(1500); // Allow response to be sent
        _logger.LogInformation("Executing system power off...");

        var psi = new ProcessStartInfo("systemctl", "poweroff")
        {
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          UseShellExecute = false,
        };

        using var proc = Process.Start(psi);
        if (proc != null)
        {
          await proc.WaitForExitAsync();
          if (proc.ExitCode != 0)
          {
            var stderr = await proc.StandardError.ReadToEndAsync();
            _logger.LogError("systemctl poweroff failed (exit {ExitCode}): {Error}", proc.ExitCode, stderr);
          }
        }
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Failed to execute system power off");
      }
    });

    return Ok(new { message = "System power off initiated" });
  }

  /// <summary>
  /// Restarts the radio-api and radio-web systemd services (Linux only).
  /// </summary>
  [HttpPost("restart-services")]
  [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
  [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
  public IActionResult RestartServices()
  {
    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    {
      return BadRequest(new { error = "Service restart is only supported on Linux" });
    }

    _logger.LogWarning("Service restart requested via API");

    Task.Run(async () =>
    {
      try
      {
        await Task.Delay(1500); // Allow response to be sent
        _logger.LogInformation("Restarting radio services...");

        var psi = new ProcessStartInfo("sudo", "systemctl restart radio-api radio-web")
        {
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          UseShellExecute = false,
        };

        using var proc = Process.Start(psi);
        if (proc != null)
        {
          await proc.WaitForExitAsync();
          if (proc.ExitCode != 0)
          {
            var stderr = await proc.StandardError.ReadToEndAsync();
            _logger.LogError("Service restart failed (exit {ExitCode}): {Error}", proc.ExitCode, stderr);
          }
        }
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Failed to restart services");
      }
    });

    return Ok(new { message = "Service restart initiated" });
  }
}

using Microsoft.AspNetCore.Mvc;
using Radio.API.Models;
using Radio.Core.Interfaces.Audio;
using System.Diagnostics;
using System.Runtime.InteropServices;

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
  private static readonly DateTime _startTime = DateTime.UtcNow;

  /// <summary>
  /// Initializes a new instance of the SystemController.
  /// </summary>
  public SystemController(
    ILogger<SystemController> logger,
    IAudioEngine audioEngine)
  {
    _logger = logger;
    _audioEngine = audioEngine;
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

      // CPU Usage (approximate using TotalProcessorTime)
      var cpuUsage = await GetCpuUsageAsync(process);

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

      // TODO: Implement log reading from Serilog file sink
      // For now, return a placeholder response indicating the feature needs log sink configuration
      var response = new SystemLogsDto
      {
        Logs = new List<LogEntryDto>(),
        TotalCount = 0,
        Filters = new LogFilterDto
        {
          Level = level,
          Limit = limit,
          MaxAgeMinutes = maxAgeMinutes
        }
      };

      _logger.LogInformation(
        "Log retrieval requested with level={Level}, limit={Limit}, maxAge={MaxAge}",
        level, limit, maxAgeMinutes);

      return Ok(response);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting system logs");
      return StatusCode(500, new { error = "Failed to get system logs" });
    }
  }

  private async Task<double> GetCpuUsageAsync(Process process)
  {
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

      return cpuUsageTotal * 100;
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to calculate CPU usage");
      return 0;
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
        var tempPath = "/sys/class/thermal/thermal_zone0/temp";
        if (System.IO.File.Exists(tempPath))
        {
          var tempStr = await System.IO.File.ReadAllTextAsync(tempPath);
          if (int.TryParse(tempStr.Trim(), out var tempMilliC))
          {
            var tempCelsius = tempMilliC / 1000.0;
            return $"{tempCelsius:F1}°C";
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
}

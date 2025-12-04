namespace Radio.API.Models;

/// <summary>
/// System statistics response.
/// </summary>
public class SystemStatsDto
{
  /// <summary>
  /// Gets or sets the CPU usage percentage (0-100).
  /// </summary>
  public double CpuUsagePercent { get; set; }

  /// <summary>
  /// Gets or sets the RAM usage in megabytes.
  /// </summary>
  public double RamUsageMb { get; set; }

  /// <summary>
  /// Gets or sets the disk usage percentage (0-100).
  /// </summary>
  public double DiskUsagePercent { get; set; }

  /// <summary>
  /// Gets or sets the total number of threads.
  /// </summary>
  public int ThreadCount { get; set; }

  /// <summary>
  /// Gets or sets the application uptime.
  /// </summary>
  public string AppUptime { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the system uptime.
  /// </summary>
  public string SystemUptime { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the audio engine state.
  /// </summary>
  public string AudioEngineState { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the system temperature in Celsius.
  /// Returns "N/A" if temperature is not available.
  /// </summary>
  public string SystemTemperature { get; set; } = "N/A";
}

/// <summary>
/// Request for retrieving system logs.
/// </summary>
public class SystemLogsRequest
{
  /// <summary>
  /// Gets or sets the log level filter.
  /// Valid values: "info", "warning", "error".
  /// Default is "warning".
  /// </summary>
  public string Level { get; set; } = "warning";

  /// <summary>
  /// Gets or sets the maximum number of log lines to return.
  /// Default is 100.
  /// </summary>
  public int Limit { get; set; } = 100;

  /// <summary>
  /// Gets or sets the maximum age of logs to return in minutes.
  /// If null, no age filtering is applied.
  /// </summary>
  public int? MaxAgeMinutes { get; set; }
}

/// <summary>
/// Response for system logs.
/// </summary>
public class SystemLogsDto
{
  /// <summary>
  /// Gets or sets the collection of log entries.
  /// </summary>
  public List<LogEntryDto> Logs { get; set; } = new();

  /// <summary>
  /// Gets or sets the total number of logs matching the filter.
  /// </summary>
  public int TotalCount { get; set; }

  /// <summary>
  /// Gets or sets the applied filters.
  /// </summary>
  public LogFilterDto Filters { get; set; } = new();
}

/// <summary>
/// Individual log entry.
/// </summary>
public class LogEntryDto
{
  /// <summary>
  /// Gets or sets the timestamp of the log entry.
  /// </summary>
  public DateTime Timestamp { get; set; }

  /// <summary>
  /// Gets or sets the log level.
  /// </summary>
  public string Level { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the log message.
  /// </summary>
  public string Message { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the exception details, if any.
  /// </summary>
  public string? Exception { get; set; }

  /// <summary>
  /// Gets or sets the source context (logger name).
  /// </summary>
  public string? SourceContext { get; set; }
}

/// <summary>
/// Applied log filters.
/// </summary>
public class LogFilterDto
{
  /// <summary>
  /// Gets or sets the level filter applied.
  /// </summary>
  public string Level { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the limit applied.
  /// </summary>
  public int Limit { get; set; }

  /// <summary>
  /// Gets or sets the max age in minutes applied.
  /// </summary>
  public int? MaxAgeMinutes { get; set; }
}

using System.IO.Compression;
using Microsoft.AspNetCore.Mvc;

namespace Radio.API.Controllers;

/// <summary>
/// System-level log-download endpoint surfaced to the DevTray
/// (PR D #24 of the Arc follow-up backlog). Reads Serilog log files from
/// the local logs directory (same path the existing
/// <see cref="SystemController.GetSystemLogs"/> endpoint scans), bundles
/// the ones whose last-write timestamp falls within the requested window
/// into a zip, and streams it as a download.
/// <para>
/// <b>Authorization:</b> no auth policy exists in this project today.
/// Log archives can leak sensitive runtime state (file paths, IP
/// addresses, partial stack traces). Until a kiosk-auth policy is wired
/// this endpoint is reachable from the local network — the DevTray is
/// itself the access gate at the UI layer. A follow-up PR must add
/// server-side authorization.
/// </para>
/// </summary>
[ApiController]
[Route("api/system/logs")]
public class SystemLogsController : ControllerBase
{
  private readonly ILogger<SystemLogsController> _logger;

  // Mirrors SystemController.MaxLogFileSizeBytes — skip any file larger than
  // this to avoid pinning the process working set on a runaway log.
  private const long MaxLogFileSizeBytes = 50L * 1024L * 1024L;

  // The DevTray accepts these strings; any other period falls back to "1h".
  // Keep the set small so the UI and server never disagree on what's legal.
  private static readonly Dictionary<string, TimeSpan> _periodWindows = new(StringComparer.OrdinalIgnoreCase)
  {
    ["5m"] = TimeSpan.FromMinutes(5),
    ["1h"] = TimeSpan.FromHours(1),
    ["24h"] = TimeSpan.FromHours(24),
    ["7d"] = TimeSpan.FromDays(7),
  };

  // Tests inject an explicit logs directory so the file-system dependency
  // is hermetic. Production wiring leaves this null; the endpoint then
  // falls back to <c>{cwd}/logs</c>, matching SystemController's existing
  // log scanner.
  internal string? OverrideLogsDirectory { get; set; }

  public SystemLogsController(ILogger<SystemLogsController> logger)
  {
    _logger = logger;
  }

  /// <summary>
  /// Returns a zip archive containing all Serilog log files
  /// (<c>radio-*.txt</c>) modified within the requested window. Default
  /// window is <c>1h</c>. Accepts <c>5m</c> / <c>1h</c> / <c>24h</c> /
  /// <c>7d</c>; anything else is treated as <c>1h</c>.
  /// </summary>
  [HttpGet("download")]
  [Produces("application/zip")]
  [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public IActionResult DownloadLogs([FromQuery] string period = "1h")
  {
    var window = _periodWindows.TryGetValue(period, out var w) ? w : TimeSpan.FromHours(1);
    var resolvedPeriod = _periodWindows.ContainsKey(period) ? period.ToLowerInvariant() : "1h";

    var logsDir = OverrideLogsDirectory ?? Path.Combine(Directory.GetCurrentDirectory(), "logs");
    if (!Directory.Exists(logsDir))
    {
      _logger.LogWarning("Logs directory does not exist: {LogsDir}", logsDir);
      return NotFound(new { error = "Logs directory not found", path = logsDir });
    }

    var since = DateTime.UtcNow - window;
    var candidates = Directory.GetFiles(logsDir, "radio-*.txt")
      .Select(p => new FileInfo(p))
      .Where(fi => fi.LastWriteTimeUtc >= since && fi.Length <= MaxLogFileSizeBytes)
      .OrderByDescending(fi => fi.LastWriteTimeUtc)
      .ToList();

    if (candidates.Count == 0)
    {
      _logger.LogInformation(
        "Log download: no files within {Period} window (since {Since:o}); returning 404",
        resolvedPeriod, since);
      return NotFound(new
      {
        error = "No log files found within the requested window.",
        period = resolvedPeriod,
        since = since.ToString("o")
      });
    }

    // Build the zip in memory. Log files are bounded by MaxLogFileSizeBytes
    // and we cap to candidates whose last-write is recent; the total fits
    // comfortably in a single response on any reasonable kiosk.
    var ms = new MemoryStream();
    using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
    {
      foreach (var fi in candidates)
      {
        try
        {
          var entry = zip.CreateEntry(fi.Name, CompressionLevel.Optimal);
          using var entryStream = entry.Open();
          // Open with shared read so Serilog can keep writing.
          using var fs = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
          fs.CopyTo(entryStream);
        }
        catch (Exception ex)
        {
          // Don't abort the whole zip for one bad file — log and continue.
          _logger.LogWarning(ex, "Failed to include log file {LogFile} in download zip", fi.FullName);
        }
      }
    }
    ms.Position = 0;

    var ts = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
    var filename = $"radio-logs-{resolvedPeriod}-{ts}.zip";

    _logger.LogInformation(
      "Log download served: period={Period}, files={Count}, bytes={Bytes}",
      resolvedPeriod, candidates.Count, ms.Length);

    return File(ms.ToArray(), "application/zip", filename);
  }
}

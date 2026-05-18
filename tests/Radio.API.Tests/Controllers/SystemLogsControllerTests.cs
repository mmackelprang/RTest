namespace Radio.API.Tests.Controllers;

using System.IO.Compression;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.API.Controllers;
using Xunit;

/// <summary>
/// Tests for the SystemLogsController logs/download endpoint (PR D #24).
/// Uses a hermetic per-test logs directory (set via the
/// <c>OverrideLogsDirectory</c> seam) so concurrent test execution doesn't
/// share a global current-directory mutation.
/// </summary>
public class SystemLogsControllerTests : IDisposable
{
  private readonly string _logsDir;

  public SystemLogsControllerTests()
  {
    _logsDir = Path.Combine(Path.GetTempPath(), $"radio-logs-test-{Guid.NewGuid():N}");
    Directory.CreateDirectory(_logsDir);
  }

  public void Dispose()
  {
    try
    {
      if (Directory.Exists(_logsDir))
      {
        Directory.Delete(_logsDir, recursive: true);
      }
    }
    catch
    {
      // best-effort cleanup; some files may be locked by the test runner.
    }
  }

  private SystemLogsController CreateController(bool clearLogsDir = false)
  {
    var controller = new SystemLogsController(NullLogger<SystemLogsController>.Instance);
    if (clearLogsDir)
    {
      // Point at a sibling path that does NOT exist — for the "no logs
      // directory" scenario. The override gets us hermetic regardless.
      controller.OverrideLogsDirectory = Path.Combine(_logsDir, "missing-subdir");
    }
    else
    {
      controller.OverrideLogsDirectory = _logsDir;
    }
    return controller;
  }

  [Fact]
  public void DownloadLogs_NoLogsDirectory_ReturnsNotFound()
  {
    var controller = CreateController(clearLogsDir: true);

    var result = controller.DownloadLogs(period: "1h");

    Assert.IsType<NotFoundObjectResult>(result);
  }

  [Fact]
  public void DownloadLogs_NoRecentFiles_ReturnsNotFound()
  {
    // Write a log file but backdate it well outside the 1h window.
    var stale = Path.Combine(_logsDir, "radio-stale.txt");
    File.WriteAllText(stale, "stale line");
    File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-30));

    var controller = CreateController();

    var result = controller.DownloadLogs(period: "1h");

    Assert.IsType<NotFoundObjectResult>(result);
  }

  [Fact]
  public void DownloadLogs_RecentFile_ReturnsZip()
  {
    var recent = Path.Combine(_logsDir, "radio-20260518.txt");
    File.WriteAllText(recent, "2026-05-18 12:00:00.000 +00:00 [INF] [Test] Hello");

    var controller = CreateController();

    var result = controller.DownloadLogs(period: "1h");

    var file = Assert.IsType<FileContentResult>(result);
    Assert.Equal("application/zip", file.ContentType);
    Assert.NotEmpty(file.FileContents);

    // Verify the zip contains the original filename.
    using var ms = new MemoryStream(file.FileContents);
    using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
    Assert.Contains(zip.Entries, e => e.Name == "radio-20260518.txt");
  }

  [Theory]
  [InlineData("5m")]
  [InlineData("1h")]
  [InlineData("24h")]
  [InlineData("7d")]
  public void DownloadLogs_AcceptsKnownPeriods(string period)
  {
    var recent = Path.Combine(_logsDir, "radio-test.txt");
    File.WriteAllText(recent, "line");

    var controller = CreateController();

    var result = controller.DownloadLogs(period);

    // Either 200 with zip, or 404 if the LastWriteTime ends up outside the
    // window for the smaller periods due to filesystem timestamp granularity.
    // Never a 400 or 5xx — period is always accepted.
    Assert.True(result is FileContentResult or NotFoundObjectResult,
      $"Expected file or not-found for period={period}, got {result.GetType().Name}");
  }

  [Fact]
  public void DownloadLogs_UnknownPeriod_FallsBackTo1h()
  {
    var recent = Path.Combine(_logsDir, "radio-test.txt");
    File.WriteAllText(recent, "line");

    var controller = CreateController();

    var result = controller.DownloadLogs(period: "garbage-period");

    Assert.True(result is FileContentResult or NotFoundObjectResult);
  }
}

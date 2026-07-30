using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Infrastructure.Audio.Diagnostics;

namespace Radio.Infrastructure.Tests.Audio.Diagnostics;

/// <summary>
/// Unit tests for DiagnosticCaptureService.PruneCaptureDirectory — the retention logic
/// that keeps the diagnostics capture directory bounded (age cap + count cap).
/// </summary>
public sealed class DiagnosticCaptureServicePruneTests : IDisposable
{
  private readonly string _baseDir;

  public DiagnosticCaptureServicePruneTests()
  {
    _baseDir = Path.Combine(Path.GetTempPath(), $"diag-prune-{Guid.NewGuid():N}");
    Directory.CreateDirectory(_baseDir);
  }

  public void Dispose()
  {
    try
    {
      if (Directory.Exists(_baseDir))
      {
        Directory.Delete(_baseDir, recursive: true);
      }
    }
    catch (IOException)
    {
      // Best-effort cleanup.
    }
  }

  /// <summary>Creates a run subdirectory named for the given UTC timestamp.</summary>
  private string CreateRun(DateTime timestampUtc)
  {
    var name = timestampUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
    var path = Path.Combine(_baseDir, name);
    Directory.CreateDirectory(path);
    // Drop a file inside to prove recursive deletion works.
    File.WriteAllText(Path.Combine(path, "capture.wav"), "x");
    return path;
  }

  [Fact]
  public void PruneCaptureDirectory_AgeCap_DeletesRunsOlderThanRetention()
  {
    var now = DateTime.UtcNow;
    var oldRun = CreateRun(now.AddDays(-10));
    var recentRun = CreateRun(now.AddHours(-1));

    // retentionDays=7, count cap disabled (0)
    DiagnosticCaptureService.PruneCaptureDirectory(_baseDir, maxRetainedRuns: 0, retentionDays: 7, NullLogger.Instance);

    Assert.False(Directory.Exists(oldRun), "run older than retention should be deleted");
    Assert.True(Directory.Exists(recentRun), "recent run should be kept");
  }

  [Fact]
  public void PruneCaptureDirectory_CountCap_KeepsOnlyNewestRuns()
  {
    var now = DateTime.UtcNow;
    // 5 runs, each a minute apart, all recent (within any age window).
    var runs = Enumerable.Range(0, 5)
      .Select(i => CreateRun(now.AddMinutes(-i)))
      .ToList();

    // Keep newest 2, age cap disabled (0).
    DiagnosticCaptureService.PruneCaptureDirectory(_baseDir, maxRetainedRuns: 2, retentionDays: 0, NullLogger.Instance);

    // runs[0] and runs[1] are the two most recent.
    Assert.True(Directory.Exists(runs[0]));
    Assert.True(Directory.Exists(runs[1]));
    Assert.False(Directory.Exists(runs[2]));
    Assert.False(Directory.Exists(runs[3]));
    Assert.False(Directory.Exists(runs[4]));
    Assert.Equal(2, Directory.GetDirectories(_baseDir).Length);
  }

  [Fact]
  public void PruneCaptureDirectory_CombinedCaps_ApplyBoth()
  {
    var now = DateTime.UtcNow;
    var veryOld = CreateRun(now.AddDays(-30));      // deleted by age
    var old = CreateRun(now.AddDays(-9));           // deleted by age
    var recent1 = CreateRun(now.AddMinutes(-1));    // survives age; newest
    var recent2 = CreateRun(now.AddMinutes(-2));    // survives age; 2nd newest
    var recent3 = CreateRun(now.AddMinutes(-3));    // survives age; trimmed by count cap

    // Age cap 7 days removes veryOld + old; count cap 2 then trims recent3.
    DiagnosticCaptureService.PruneCaptureDirectory(_baseDir, maxRetainedRuns: 2, retentionDays: 7, NullLogger.Instance);

    Assert.False(Directory.Exists(veryOld));
    Assert.False(Directory.Exists(old));
    Assert.True(Directory.Exists(recent1));
    Assert.True(Directory.Exists(recent2));
    Assert.False(Directory.Exists(recent3));
  }

  [Fact]
  public void PruneCaptureDirectory_NonTimestampName_UsesLastWriteTimeForAge()
  {
    var now = DateTime.UtcNow;
    var weird = Path.Combine(_baseDir, "not-a-timestamp");
    Directory.CreateDirectory(weird);
    Directory.SetLastWriteTimeUtc(weird, now.AddDays(-30));
    var recent = CreateRun(now.AddHours(-1));

    DiagnosticCaptureService.PruneCaptureDirectory(_baseDir, maxRetainedRuns: 0, retentionDays: 7, NullLogger.Instance);

    Assert.False(Directory.Exists(weird), "old non-timestamp dir should be aged out via mtime");
    Assert.True(Directory.Exists(recent));
  }

  [Fact]
  public void PruneCaptureDirectory_DisabledCaps_DeleteNothing()
  {
    var now = DateTime.UtcNow;
    var a = CreateRun(now.AddDays(-100));
    var b = CreateRun(now.AddDays(-200));

    // Both caps disabled (0) → no deletions.
    DiagnosticCaptureService.PruneCaptureDirectory(_baseDir, maxRetainedRuns: 0, retentionDays: 0, NullLogger.Instance);

    Assert.True(Directory.Exists(a));
    Assert.True(Directory.Exists(b));
  }

  [Fact]
  public void PruneCaptureDirectory_MissingBaseDirectory_IsNoOp()
  {
    var missing = Path.Combine(_baseDir, "does-not-exist");

    // Should not throw.
    DiagnosticCaptureService.PruneCaptureDirectory(missing, maxRetainedRuns: 5, retentionDays: 7, NullLogger.Instance);
  }
}

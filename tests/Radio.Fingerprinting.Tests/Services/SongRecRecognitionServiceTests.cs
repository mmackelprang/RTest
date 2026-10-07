using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Fingerprinting;
using Xunit;
using Radio.Fingerprinting.Services;
using Radio.Fingerprinting;

namespace Radio.Fingerprinting.Tests.Services;

public class SongRecRecognitionServiceTests
{
  private readonly Mock<ILogger<SongRecRecognitionService>> _loggerMock;

  public SongRecRecognitionServiceTests()
  {
    _loggerMock = new Mock<ILogger<SongRecRecognitionService>>();
  }

  [Fact]
  public void Constructor_WhenDisabled_SetsIsAvailableFalse()
  {
    var options = Options.Create(new FingerprintingOptions
    {
      SongRec = new SongRecOptions { Enabled = false }
    });

    var service = new SongRecRecognitionService(_loggerMock.Object, options);

    Assert.False(service.IsAvailable);
  }

  [Fact]
  public async Task RecognizeAsync_WhenNotAvailable_ReturnsError()
  {
    var options = Options.Create(new FingerprintingOptions
    {
      SongRec = new SongRecOptions { Enabled = false }
    });
    var service = new SongRecRecognitionService(_loggerMock.Object, options);

    var samples = CreateTestSamples(5.0);
    var result = await service.RecognizeAsync(samples);

    Assert.Equal(SongRecOutcome.Error, result.Outcome);
    Assert.Null(result.Track);
  }

  [Fact]
  public async Task RecognizeAsync_WithEmptySamples_ReturnsError()
  {
    var options = Options.Create(new FingerprintingOptions
    {
      SongRec = new SongRecOptions { Enabled = true }
    });
    var service = new SongRecRecognitionService(_loggerMock.Object, options);

    var samples = new AudioSampleBuffer
    {
      Samples = [],
      SampleRate = 44100,
      Channels = 2,
      Duration = TimeSpan.Zero
    };

    var result = await service.RecognizeAsync(samples);

    Assert.Equal(SongRecOutcome.Error, result.Outcome);
    Assert.Null(result.Track);
  }

  [Fact]
  public void ParseResult_WithValidTrack_ReturnsMetadata()
  {
    var result = new SongRecRecognitionService.SongRecResult
    {
      Track = new SongRecRecognitionService.SongRecTrack
      {
        Title = "Just What I Needed",
        Subtitle = "The Cars",
        Images = new SongRecRecognitionService.SongRecImages
        {
          CoverArt = "https://example.com/cover.jpg",
          CoverArtHq = "https://example.com/cover-hq.jpg"
        },
        Sections =
        [
          new SongRecRecognitionService.SongRecSection
          {
            Type = "SONG",
            Metadata =
            [
              new SongRecRecognitionService.SongRecMetadataItem
                { Title = "Album", Text = "The Cars" },
              new SongRecRecognitionService.SongRecMetadataItem
                { Title = "Released", Text = "1978" },
              new SongRecRecognitionService.SongRecMetadataItem
                { Title = "Genre", Text = "Rock" }
            ]
          }
        ]
      }
    };

    var metadata = SongRecRecognitionService.ParseResult(result);

    Assert.NotNull(metadata);
    Assert.Equal("Just What I Needed", metadata.Title);
    Assert.Equal("The Cars", metadata.Artist);
    Assert.Equal("The Cars", metadata.Album);
    Assert.Equal(1978, metadata.ReleaseYear);
    Assert.Equal("Rock", metadata.Genre);
    Assert.Equal("https://example.com/cover-hq.jpg", metadata.CoverArtUrl);
    Assert.Equal(MetadataSource.Shazam, metadata.Source);
  }

  [Fact]
  public void ParseResult_PrefersCoverArtHq_OverCoverArt()
  {
    var result = new SongRecRecognitionService.SongRecResult
    {
      Track = new SongRecRecognitionService.SongRecTrack
      {
        Title = "Test Song",
        Subtitle = "Test Artist",
        Images = new SongRecRecognitionService.SongRecImages
        {
          CoverArt = "https://example.com/cover.jpg",
          CoverArtHq = "https://example.com/cover-hq.jpg"
        }
      }
    };

    var metadata = SongRecRecognitionService.ParseResult(result);

    Assert.NotNull(metadata);
    Assert.Equal("https://example.com/cover-hq.jpg", metadata.CoverArtUrl);
  }

  [Fact]
  public void ParseResult_FallsBackToCoverArt_WhenNoHq()
  {
    var result = new SongRecRecognitionService.SongRecResult
    {
      Track = new SongRecRecognitionService.SongRecTrack
      {
        Title = "Test Song",
        Subtitle = "Test Artist",
        Images = new SongRecRecognitionService.SongRecImages
        {
          CoverArt = "https://example.com/cover.jpg",
          CoverArtHq = null
        }
      }
    };

    var metadata = SongRecRecognitionService.ParseResult(result);

    Assert.NotNull(metadata);
    Assert.Equal("https://example.com/cover.jpg", metadata.CoverArtUrl);
  }

  [Fact]
  public void ParseResult_WithNoTrack_ReturnsNull()
  {
    var result = new SongRecRecognitionService.SongRecResult
    {
      Track = null
    };

    var metadata = SongRecRecognitionService.ParseResult(result);

    Assert.Null(metadata);
  }

  [Fact]
  public void ParseResult_WithNoMetadataSections_StillReturnsMetadata()
  {
    var result = new SongRecRecognitionService.SongRecResult
    {
      Track = new SongRecRecognitionService.SongRecTrack
      {
        Title = "Minimal Track",
        Subtitle = "Unknown Artist"
      }
    };

    var metadata = SongRecRecognitionService.ParseResult(result);

    Assert.NotNull(metadata);
    Assert.Equal("Minimal Track", metadata.Title);
    Assert.Equal("Unknown Artist", metadata.Artist);
    Assert.Null(metadata.Album);
    Assert.Null(metadata.ReleaseYear);
    Assert.Null(metadata.Genre);
    Assert.Null(metadata.CoverArtUrl);
  }

  [Fact]
  public void ParseResult_FallsBackToGenresPrimary_WhenNoSectionGenre()
  {
    var result = new SongRecRecognitionService.SongRecResult
    {
      Track = new SongRecRecognitionService.SongRecTrack
      {
        Title = "Test Song",
        Subtitle = "Test Artist",
        Genres = new SongRecRecognitionService.SongRecGenres
        {
          Primary = "Pop"
        },
        Sections =
        [
          new SongRecRecognitionService.SongRecSection
          {
            Type = "SONG",
            Metadata =
            [
              new SongRecRecognitionService.SongRecMetadataItem
                { Title = "Album", Text = "Test Album" }
            ]
          }
        ]
      }
    };

    var metadata = SongRecRecognitionService.ParseResult(result);

    Assert.NotNull(metadata);
    Assert.Equal("Pop", metadata.Genre);
  }

  [Fact]
  public void ParseResult_PrefersSectionGenre_OverGenresPrimary()
  {
    var result = new SongRecRecognitionService.SongRecResult
    {
      Track = new SongRecRecognitionService.SongRecTrack
      {
        Title = "Test Song",
        Subtitle = "Test Artist",
        Genres = new SongRecRecognitionService.SongRecGenres
        {
          Primary = "Pop"
        },
        Sections =
        [
          new SongRecRecognitionService.SongRecSection
          {
            Type = "SONG",
            Metadata =
            [
              new SongRecRecognitionService.SongRecMetadataItem
                { Title = "Genre", Text = "Rock" }
            ]
          }
        ]
      }
    };

    var metadata = SongRecRecognitionService.ParseResult(result);

    Assert.NotNull(metadata);
    Assert.Equal("Rock", metadata.Genre);
  }

  [Fact]
  public void ParseResult_WithNullTitle_UsesDefault()
  {
    var result = new SongRecRecognitionService.SongRecResult
    {
      Track = new SongRecRecognitionService.SongRecTrack
      {
        Title = null,
        Subtitle = null
      }
    };

    var metadata = SongRecRecognitionService.ParseResult(result);

    Assert.NotNull(metadata);
    Assert.Equal("Unknown Title", metadata.Title);
    Assert.Equal("Unknown Artist", metadata.Artist);
  }

  [Fact]
  public async Task RunSongRecAsync_WhenProcessHangs_KillsProcessInsteadOfLeavingItOrphaned()
  {
    // Regression test for the production memory leak: when songrec hangs (e.g. its
    // network call to Shazam blocks), the timeout fired but the OS process was
    // never killed — only the managed handle was disposed. Orphaned songrec
    // processes accumulated (~34 observed live, ~650 MB). This proves the timed-out
    // subprocess is actually terminated, not orphaned.
    var options = Options.Create(new FingerprintingOptions
    {
      SongRec = new SongRecOptions { Enabled = true, TimeoutSeconds = 1 }
    });
    var service = new HangingSongRecService(_loggerMock.Object, options);

    var result = await service.RunSongRecAsync("nonexistent.wav", CancellationToken.None);

    // Timed out => a failure, not a no-match (the call policy backs off on failures only).
    Assert.Equal(SongRecOutcome.Error, result.Outcome);
    Assert.Null(result.Result);
    Assert.Contains("timed out", result.Error);

    // The stand-in process must actually have been launched.
    Assert.NotNull(service.StartedProcessId);
    var pid = service.StartedProcessId!.Value;

    try
    {
      // Critical assertion: the process must NOT still be running after the timeout.
      var terminated = await WaitForProcessExitAsync(pid, TimeSpan.FromSeconds(5));
      Assert.True(
        terminated,
        $"songrec stand-in process {pid} was orphaned instead of being killed after the timeout");
    }
    finally
    {
      TryKill(pid);
    }
  }

  [Fact]
  public async Task RunSongRecAsync_WhenCallerCancels_KillsProcessAndPropagatesCancellation()
  {
    // The kill guarantee must hold on the caller-cancellation path too, and — unlike
    // the timeout path (which returns an Error outcome) — caller cancellation must still surface
    // as an OperationCanceledException to the caller. TimeoutSeconds is set high so
    // the caller's token, not the internal timeout, is what fires.
    var options = Options.Create(new FingerprintingOptions
    {
      SongRec = new SongRecOptions { Enabled = true, TimeoutSeconds = 30 }
    });
    var service = new HangingSongRecService(_loggerMock.Object, options);

    using var cts = new CancellationTokenSource();
    cts.CancelAfter(TimeSpan.FromMilliseconds(500));

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => service.RunSongRecAsync("nonexistent.wav", cts.Token));

    Assert.NotNull(service.StartedProcessId);
    var pid = service.StartedProcessId!.Value;

    try
    {
      var terminated = await WaitForProcessExitAsync(pid, TimeSpan.FromSeconds(5));
      Assert.True(
        terminated,
        $"songrec stand-in process {pid} was orphaned instead of being killed on caller cancellation");
    }
    finally
    {
      TryKill(pid);
    }
  }

  [Fact]
  public async Task Recognized_IsInformationOnlyWhenTheResultChanges_AndNoMatchIsTrace()
  {
    // LOG-12: the same song is recognized every cycle while it plays.
    if (OperatingSystem.IsWindows())
    {
      return; // the stand-in is /bin/sh
    }

    var log = new LevelLog();
    var service = new ScriptedSongRecService(log, Options.Create(new FingerprintingOptions()));

    foreach (var output in new[]
    {
      "{\"track\":{\"title\":\"Song A\",\"subtitle\":\"Band\"}}",
      "{\"track\":{\"title\":\"Song A\",\"subtitle\":\"Band\"}}",
      "",
      "{\"track\":{\"title\":\"Song B\",\"subtitle\":\"Band\"}}",
    })
    {
      service.NextOutput = output;
      await service.RunSongRecAsync("unused.wav", CancellationToken.None);
    }

    Assert.Equal(
      new[] { LogLevel.Information, LogLevel.Debug, LogLevel.Information },
      log.Entries.Where(e => e.Message.StartsWith("SongRec recognized", StringComparison.Ordinal)).Select(e => e.Level));
    Assert.Equal(LogLevel.Trace, Assert.Single(log.Entries, e => e.Message.Contains("no match")).Level);
  }

  [Fact]
  public async Task SameSong_AfterTenMinutes_IsInformationAgain()
  {
    // A pause/resume, a different source, or the song coming round hours later must still produce an
    // Information line (AUD-12's UAT looks for it).
    if (OperatingSystem.IsWindows())
    {
      return;
    }

    var log = new LevelLog();
    var clock = new ManualClock();
    var service = new ScriptedSongRecService(log, Options.Create(new FingerprintingOptions())) { TimeProvider = clock };
    service.NextOutput = "{\"track\":{\"title\":\"Song A\",\"subtitle\":\"Band\"}}";

    await service.RunSongRecAsync("unused.wav", CancellationToken.None);  // Information
    clock.Advance(TimeSpan.FromMinutes(9));
    await service.RunSongRecAsync("unused.wav", CancellationToken.None);  // Debug
    clock.Advance(TimeSpan.FromMinutes(1));
    await service.RunSongRecAsync("unused.wav", CancellationToken.None);  // 10 min since Information

    Assert.Equal(
      new[] { LogLevel.Information, LogLevel.Debug, LogLevel.Information },
      log.Entries.Where(e => e.Message.StartsWith("SongRec recognized", StringComparison.Ordinal)).Select(e => e.Level));
  }

  // --- Call policy: a failure must be distinguishable from a clean no-match. Before it, every one of
  // these returned null, so a Shazam outage looked exactly like "no match" and nothing backed off.

  [Fact]
  public async Task RunSongRecAsync_NonZeroExit_IsAnError_NotANoMatch()
  {
    var service = new StandInSongRecService(
      _loggerMock.Object, Options.Create(new FingerprintingOptions()),
      windows: ("cmd.exe", "/c exit 3"), unix: ("/bin/sh", "-c \"exit 3\""));

    var run = await service.RunSongRecAsync("unused.wav", CancellationToken.None);

    Assert.Equal(SongRecOutcome.Error, run.Outcome);
    Assert.Contains("code 3", run.Error);
  }

  [Fact]
  public async Task RunSongRecAsync_UnparsableOutput_IsAnError_NotANoMatch()
  {
    var service = new StandInSongRecService(
      _loggerMock.Object, Options.Create(new FingerprintingOptions()),
      windows: ("cmd.exe", "/c echo notjson"), unix: ("/bin/sh", "-c \"echo notjson\""));

    var run = await service.RunSongRecAsync("unused.wav", CancellationToken.None);

    Assert.Equal(SongRecOutcome.Error, run.Outcome);
  }

  [Fact]
  public async Task RunSongRecAsync_CleanExitWithNoOutput_IsANoMatch()
  {
    var service = new StandInSongRecService(
      _loggerMock.Object, Options.Create(new FingerprintingOptions()),
      windows: ("cmd.exe", "/c exit 0"), unix: ("/bin/sh", "-c \"exit 0\""));

    var run = await service.RunSongRecAsync("unused.wav", CancellationToken.None);

    Assert.Equal(SongRecOutcome.NoMatch, run.Outcome);
    Assert.Null(run.Error);
  }

  [Fact]
  public async Task RunSongRecAsync_JsonWithoutATrack_IsANoMatch_AndWithATrack_IsAMatch()
  {
    if (OperatingSystem.IsWindows())
    {
      return; // the stand-in is /bin/sh: cmd.exe cannot echo JSON braces and quotes faithfully
    }

    var service = new ScriptedSongRecService(new LevelLog(), Options.Create(new FingerprintingOptions()));

    service.NextOutput = "{\"matches\":[]}";
    Assert.Equal(SongRecOutcome.NoMatch, (await service.RunSongRecAsync("unused.wav", CancellationToken.None)).Outcome);

    service.NextOutput = "{\"track\":{\"title\":\"Song A\",\"subtitle\":\"Band\"}}";
    var match = await service.RunSongRecAsync("unused.wav", CancellationToken.None);
    Assert.Equal(SongRecOutcome.Match, match.Outcome);
    Assert.Equal("Song A", match.Result?.Track?.Title);
  }

  /// <summary>Runs a fixed shell command in place of songrec.</summary>
  private sealed class StandInSongRecService(
    ILogger<SongRecRecognitionService> logger,
    IOptions<FingerprintingOptions> options,
    (string File, string Args) windows,
    (string File, string Args) unix) : SongRecRecognitionService(logger, options)
  {
    internal override Process? StartProcess(ProcessStartInfo startInfo)
    {
      var (file, args) = OperatingSystem.IsWindows() ? windows : unix;
      return Process.Start(new ProcessStartInfo
      {
        FileName = file,
        Arguments = args,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      });
    }
  }

  private sealed class ManualClock : TimeProvider
  {
    private long _ticks = 1_000_000;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _ticks;

    public void Advance(TimeSpan by) => _ticks += by.Ticks;
  }

  private sealed class ScriptedSongRecService : SongRecRecognitionService
  {
    public ScriptedSongRecService(ILogger<SongRecRecognitionService> logger, IOptions<FingerprintingOptions> options)
      : base(logger, options)
    {
    }

    public string NextOutput { get; set; } = string.Empty;

    internal override Process? StartProcess(ProcessStartInfo startInfo)
    {
      var info = new ProcessStartInfo
      {
        FileName = "/bin/sh",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
      };
      info.ArgumentList.Add("-c");
      info.ArgumentList.Add("printf '%s' \"$0\"");
      info.ArgumentList.Add(NextOutput);
      return Process.Start(info);
    }
  }

  private sealed class LevelLog : ILogger<SongRecRecognitionService>
  {
    public List<(LogLevel Level, string Message)> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
      Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
  }

  /// <summary>
  /// Test double: launches a long-running, cross-platform stand-in process in place
  /// of the real songrec binary so the timeout path can be exercised deterministically.
  /// </summary>
  private sealed class HangingSongRecService : SongRecRecognitionService
  {
    public HangingSongRecService(
      ILogger<SongRecRecognitionService> logger,
      IOptions<FingerprintingOptions> options)
      : base(logger, options)
    {
    }

    public int? StartedProcessId { get; private set; }

    internal override Process? StartProcess(ProcessStartInfo startInfo)
    {
      // Ignore the real songrec command; launch a process that runs far longer than
      // the timeout and produces no output on its redirected streams, forcing
      // RunSongRecAsync's read/wait to hit the timeout. Streams are redirected
      // because RunSongRecAsync reads stdout/stderr.
      var hangInfo = OperatingSystem.IsWindows()
        ? new ProcessStartInfo
        {
          FileName = "cmd.exe",
          Arguments = "/c ping -n 60 127.0.0.1",
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          UseShellExecute = false,
          CreateNoWindow = true
        }
        : new ProcessStartInfo
        {
          FileName = "/bin/sleep",
          Arguments = "60",
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          UseShellExecute = false,
          CreateNoWindow = true
        };

      var process = Process.Start(hangInfo);
      StartedProcessId = process?.Id;
      return process;
    }
  }

  private static async Task<bool> WaitForProcessExitAsync(int pid, TimeSpan timeout)
  {
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
      if (!IsProcessRunning(pid))
      {
        return true;
      }
      await Task.Delay(50);
    }
    return !IsProcessRunning(pid);
  }

  private static bool IsProcessRunning(int pid)
  {
    try
    {
      using var p = Process.GetProcessById(pid);
      return !p.HasExited;
    }
    catch (ArgumentException)
    {
      // No process with that id exists (already exited and reaped).
      return false;
    }
  }

  private static void TryKill(int pid)
  {
    try
    {
      using var p = Process.GetProcessById(pid);
      if (!p.HasExited)
      {
        p.Kill(entireProcessTree: true);
      }
    }
    catch
    {
      // Best-effort cleanup — process may already be gone.
    }
  }

  private static AudioSampleBuffer CreateTestSamples(double durationSeconds)
  {
    const int sampleRate = 44100;
    const int channels = 2;
    var totalSamples = (int)(sampleRate * channels * durationSeconds);
    var samples = new float[totalSamples];

    // Generate a simple sine wave for test purposes
    for (int i = 0; i < totalSamples; i++)
    {
      var t = (double)i / (sampleRate * channels);
      samples[i] = (float)(Math.Sin(2 * Math.PI * 440 * t) * 0.5);
    }

    return new AudioSampleBuffer
    {
      Samples = samples,
      SampleRate = sampleRate,
      Channels = channels,
      Duration = TimeSpan.FromSeconds(durationSeconds)
    };
  }
}

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
  public async Task RecognizeAsync_WhenNotAvailable_ReturnsNull()
  {
    var options = Options.Create(new FingerprintingOptions
    {
      SongRec = new SongRecOptions { Enabled = false }
    });
    var service = new SongRecRecognitionService(_loggerMock.Object, options);

    var samples = CreateTestSamples(5.0);
    var result = await service.RecognizeAsync(samples);

    Assert.Null(result);
  }

  [Fact]
  public async Task RecognizeAsync_WithEmptySamples_ReturnsNull()
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

    Assert.Null(result);
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

    // Timed out => no recognition result.
    Assert.Null(result);

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
    // the timeout path (which returns null) — caller cancellation must still surface
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

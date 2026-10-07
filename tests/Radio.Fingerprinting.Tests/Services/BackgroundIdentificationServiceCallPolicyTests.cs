using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Radio.Core.Events;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Fingerprinting.Services;

namespace Radio.Fingerprinting.Tests.Services;

/// <summary>
/// The call policy wired into <see cref="BackgroundIdentificationService"/>: schedules, failure handling,
/// and the two bugs it fixes, through the service's real cycle.
/// </summary>
/// <remarks>
/// Deterministic by construction: each test runs single cycles through
/// <c>RunOneCycleWithResultForTestingAsync</c> (no background loop, no start-up delay) against a
/// <see cref="FakeTimeProvider"/> the test advances by hand, and a tap and SongRec that answer at once. No
/// assertion depends on a timer firing; the cycle is the rendezvous.
/// </remarks>
public class BackgroundIdentificationServiceCallPolicyTests
{
  private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero));
  private readonly List<(LogLevel Level, string Message)> _logs = new();
  private readonly ScriptedTap _tap = new();
  private readonly ScriptedSongRec _songRec = new();

  private static TrackMetadata Track(string title, string artist = "Artist") => new()
  {
    Id = Guid.NewGuid().ToString(),
    Title = title,
    Artist = artist,
    Source = MetadataSource.Shazam,
    CreatedAt = DateTime.UtcNow,
    UpdatedAt = DateTime.UtcNow
  };

  private BackgroundIdentificationService CreateService(FingerprintingOptions? options = null)
  {
    var services = new ServiceCollection();
    services.AddSingleton<IAudioSampleProvider>(_tap);
    services.AddSingleton<ISongRecRecognitionService>(_songRec);
    var monitor = new Mock<IOptionsMonitor<FingerprintingOptions>>();
    monitor.Setup(o => o.CurrentValue).Returns(options ?? new FingerprintingOptions { SampleDurationSeconds = 1 });
    return new BackgroundIdentificationService(new ListLogger(_logs), services.BuildServiceProvider(), monitor.Object)
    {
      TimeProvider = _clock
    };
  }

  [Fact]
  public async Task Radio_CallsAt0And15_NotAt14()
  {
    _tap.SourceType = PlaySource.Radio;
    _songRec.Next = _ => SongRecRecognitionResult.Matched(Track("Boys of Summer"));
    using var service = CreateService();
    var start = _clock.GetUtcNow();

    var first = await service.RunOneCycleWithResultForTestingAsync();
    _clock.Advance(TimeSpan.FromSeconds(14));
    var early = await service.RunOneCycleWithResultForTestingAsync();
    _clock.Advance(TimeSpan.FromSeconds(1));
    var due = await service.RunOneCycleWithResultForTestingAsync();

    Assert.True(first.CalledRecognizer);
    Assert.False(early.CalledRecognizer);
    Assert.Equal(start + TimeSpan.FromSeconds(15), early.NextAttemptAt);
    Assert.True(due.CalledRecognizer);
    Assert.Equal(2, _songRec.Calls);
  }

  [Fact]
  public async Task KnownStart_CompleteMetadata_MakesNoCall()
  {
    _tap.SourceType = PlaySource.File;
    _tap.NeedsLookup = false;
    _tap.TrackStartedUtc = _clock.GetUtcNow().UtcDateTime;
    using var service = CreateService();

    for (var i = 0; i < 10; i++)
    {
      var cycle = await service.RunOneCycleWithResultForTestingAsync();
      Assert.False(cycle.Captured);
      _clock.Advance(TimeSpan.FromMinutes(1));
    }

    Assert.Equal(0, _tap.Captures);
    Assert.Equal(0, _songRec.Calls);
  }

  [Fact]
  public async Task Silence_IsNotACall_AndTheNextPassTriesAgainAtOnce()
  {
    _tap.SourceType = PlaySource.Radio;
    _tap.Silent = true;
    _songRec.Next = _ => SongRecRecognitionResult.NoMatch;
    using var service = CreateService();

    var silent = await service.RunOneCycleWithResultForTestingAsync();
    _tap.Silent = false;
    _clock.Advance(TimeSpan.FromSeconds(1));
    var back = await service.RunOneCycleWithResultForTestingAsync();

    Assert.False(silent.CalledRecognizer);
    Assert.True(back.CalledRecognizer);
    Assert.Equal(1, _songRec.Calls);
  }

  /// <summary>
  /// Failures are failures: before the call policy a timeout or non-zero exit returned null and was counted
  /// as an ordinary no-match, so the loop called again at once. Now each failure backs off 30 s, 60 s, 120 s…
  /// and one Warning marks the fifth in a row.
  /// </summary>
  [Fact]
  public async Task SongRecFailures_BackOffExponentially_AndWarnOnceAfterFive()
  {
    _tap.SourceType = PlaySource.Radio;
    _songRec.Next = _ => SongRecRecognitionResult.Failed("songrec timed out after 15s");
    using var service = CreateService(new FingerprintingOptions { SampleDurationSeconds = 1, UnknownStartIntervalSeconds = 1 });

    var callTimes = new List<DateTimeOffset>();
    while (callTimes.Count < 6)
    {
      var cycle = await service.RunOneCycleWithResultForTestingAsync();
      if (cycle.CalledRecognizer)
      {
        callTimes.Add(_clock.GetUtcNow());
        Assert.Equal(FingerprintPhase.Error, service.GetStatus().Phase);
        Assert.Contains("timed out", service.GetStatus().LastError);
        continue;
      }

      Assert.NotNull(cycle.NextAttemptAt);
      _clock.Advance(cycle.NextAttemptAt!.Value - _clock.GetUtcNow());
    }

    var gaps = callTimes.Zip(callTimes.Skip(1), (a, b) => (b - a).TotalSeconds);
    Assert.Equal(new double[] { 30, 60, 120, 240, 480 }, gaps);
    Assert.Single(_logs, l => l.Level == LogLevel.Warning && l.Message.Contains("failed 5 times in a row"));
  }

  [Fact]
  public async Task SourceSwitch_DoesNotResetTheFailureBackOff()
  {
    _tap.SourceType = PlaySource.Radio;
    _songRec.Next = _ => SongRecRecognitionResult.Failed("songrec exited with code 1");
    using var service = CreateService();

    Assert.True((await service.RunOneCycleWithResultForTestingAsync()).CalledRecognizer);
    service.ResetSongChangeState();
    _tap.SourceType = PlaySource.Vinyl;
    _tap.SourceName = "Vinyl";
    var afterSwitch = await service.RunOneCycleWithResultForTestingAsync();

    Assert.False(afterSwitch.CalledRecognizer);
    Assert.Equal(_clock.GetUtcNow() + TimeSpan.FromSeconds(30), afterSwitch.NextAttemptAt);
  }

  /// <summary>
  /// The duplicate-suppression loop (regression). A file whose song had been identified less than five
  /// minutes earlier — a replay — had its match suppressed by the time window, so the file player never saw
  /// it, never cleared its lookup flag, and the loop called SongRec again every cycle. A known-start source
  /// now always sees the first match of a track; after that, a validation naming the same song is not
  /// re-raised, and one naming a different song is.
  /// </summary>
  [Fact]
  public async Task KnownStart_ReplayWithinTheSuppressionWindow_IsRaised_ThenValidationsOfTheSameSongAreNot()
  {
    var song = Track("Spirit In The Sky", "Norman Greenbaum");
    _tap.SourceType = PlaySource.File;
    _tap.TrackStartedUtc = _clock.GetUtcNow().UtcDateTime - TimeSpan.FromSeconds(10);
    _songRec.Next = _ => SongRecRecognitionResult.Matched(song);
    using var service = CreateService();
    var raised = new List<TrackIdentifiedEventArgs>();
    service.TrackIdentified += (_, e) =>
    {
      raised.Add(e);
      _tap.NeedsLookup = false; // what FilePlayerAudioSource.OnTrackIdentified does with the flag
    };

    // Identified a minute ago on the previous play: inside the 5-minute window.
    service.MarkAsRecentlyIdentifiedForTesting(song, 0.8);
    Assert.True(service.IsSuppressedAsDuplicateForTesting(song));

    Assert.True((await service.RunOneCycleWithResultForTestingAsync()).CalledRecognizer);
    Assert.Single(raised);
    Assert.False(_tap.NeedsLookup);

    // A validation 60 s later naming the same song: called (the policy validates), not re-raised.
    _clock.Advance(TimeSpan.FromSeconds(60));
    Assert.True((await service.RunOneCycleWithResultForTestingAsync()).CalledRecognizer);
    Assert.Single(raised);

    // A validation naming a different song is raised.
    _songRec.Next = _ => SongRecRecognitionResult.Matched(Track("American Girl", "Tom Petty"));
    _clock.Advance(TimeSpan.FromSeconds(60));
    Assert.True((await service.RunOneCycleWithResultForTestingAsync()).CalledRecognizer);
    Assert.Equal(2, raised.Count);
    Assert.Equal("American Girl", raised[1].Track.Title);
    Assert.Equal(3, _songRec.Calls);
  }

  [Fact]
  public async Task UnknownStart_KeepsTheTimeBasedDuplicateWindow()
  {
    var song = Track("Boys of Summer", "Don Henley");
    _tap.SourceType = PlaySource.Radio;
    _songRec.Next = _ => SongRecRecognitionResult.Matched(song);
    using var service = CreateService();
    var raised = 0;
    service.TrackIdentified += (_, _) => raised++;

    Assert.True((await service.RunOneCycleWithResultForTestingAsync()).CalledRecognizer);
    _clock.Advance(TimeSpan.FromSeconds(15));
    Assert.True((await service.RunOneCycleWithResultForTestingAsync()).CalledRecognizer);

    Assert.Equal(1, raised);
  }

  /// <summary>A tap whose every answer the test sets. Capture returns at once.</summary>
  private sealed class ScriptedTap : IAudioSampleProvider
  {
    public PlaySource SourceType { get; set; } = PlaySource.Radio;
    public string SourceName { get; set; } = "Scripted";
    public DateTime? CurrentTrackStartedUtc => TrackStartedUtc;
    public DateTime? TrackStartedUtc { get; set; }
    public bool NeedsLookup { get; set; } = true;
    public bool Silent { get; set; }
    public int Captures { get; private set; }

    public bool IsActive => true;
    public string? SourceFilePath => null;
    public bool NeedsFingerprintingLookup => NeedsLookup;

    public Task<AudioSampleBuffer?> CaptureAsync(TimeSpan duration, CancellationToken ct = default)
    {
      Captures++;
      if (Silent)
      {
        return Task.FromResult<AudioSampleBuffer?>(null);
      }

      return Task.FromResult<AudioSampleBuffer?>(new AudioSampleBuffer
      {
        Samples = new float[96],
        SampleRate = 48000,
        Channels = 2,
        Duration = TimeSpan.FromMilliseconds(1),
        SourceName = SourceName
      });
    }
  }

  private sealed class ScriptedSongRec : ISongRecRecognitionService
  {
    public Func<int, SongRecRecognitionResult> Next { get; set; } = _ => SongRecRecognitionResult.NoMatch;
    public int Calls { get; private set; }
    public bool IsAvailable => true;

    public Task<SongRecRecognitionResult> RecognizeAsync(AudioSampleBuffer samples, CancellationToken ct = default) =>
      Task.FromResult(Next(Calls++));
  }

  private sealed class ListLogger(List<(LogLevel Level, string Message)> sink) : ILogger<BackgroundIdentificationService>
  {
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
      Func<TState, Exception?, string> formatter) => sink.Add((logLevel, formatter(state, exception)));
  }
}

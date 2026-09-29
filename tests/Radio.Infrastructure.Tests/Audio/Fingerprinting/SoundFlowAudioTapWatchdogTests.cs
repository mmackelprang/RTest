using Microsoft.Extensions.Logging;
using Moq;
using Radio.Core.Interfaces.Audio;
using Radio.Fingerprinting.Services;
using Radio.Infrastructure.Audio.Fingerprinting;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Fingerprinting;

/// <summary>
/// AUD-18: how <see cref="SoundFlowAudioTap"/> classifies each capture window for
/// <see cref="FingerprintCaptureWatchdog"/>, and the demotion of its own per-window Warning while the
/// watchdog is latched.
/// </summary>
/// <remarks>
/// Each capture reads a <see cref="MemoryStream"/>; an empty one yields a zero-byte window. The capture
/// duration is a few milliseconds of wall clock, but nothing is timed: the outcome of a window depends only
/// on the bytes the stream holds and the source state at the window's two ends, so machine speed cannot
/// change a result.
/// </remarks>
public class SoundFlowAudioTapWatchdogTests
{
  private const int N = FingerprintCaptureWatchdog.TripAfterConsecutiveEmptyWindows;
  private static readonly TimeSpan Capture = TimeSpan.FromMilliseconds(5);

  private readonly List<(LogLevel Level, string Message)> _tapLogs = new();
  private readonly List<(LogLevel Level, string Message)> _watchdogLogs = new();
  private readonly Mock<IAudioEngine> _engine = new();
  private readonly Mock<IAudioManager> _manager = new();
  private readonly Mock<IAudioSource> _source = new();
  private readonly Mock<IBluetoothService> _bluetooth = new();
  private readonly FingerprintCaptureWatchdog _watchdog;
  private readonly SoundFlowAudioTap _tap;

  private AudioSourceState _state = AudioSourceState.Playing;
  private Func<Stream> _streamFactory = () => new MemoryStream();

  public SoundFlowAudioTapWatchdogTests()
  {
    _engine.SetupGet(e => e.State).Returns(AudioEngineState.Running);
    _engine
      .Setup(e => e.CreateStreamReader(It.IsAny<string>(), It.IsAny<double?>()))
      .Returns(() => _streamFactory());

    _source.SetupGet(s => s.State).Returns(() => _state);
    _source.SetupGet(s => s.Type).Returns(AudioSourceType.Radio);
    _source.SetupGet(s => s.Name).Returns("SDR Radio");
    _manager.SetupGet(m => m.ActiveSource).Returns(_source.Object);
    _bluetooth.SetupGet(b => b.PipelineStatus).Returns(BluetoothPipelineStatus.Healthy);

    _watchdog = new FingerprintCaptureWatchdog(new CapturingLogger<FingerprintCaptureWatchdog>(_watchdogLogs));
    _tap = new SoundFlowAudioTap(
      new CapturingLogger<SoundFlowAudioTap>(_tapLogs), _engine.Object, _manager.Object, _watchdog,
      _bluetooth.Object);
  }

  private static MemoryStream Audio() => new(Enumerable.Repeat((byte)0x40, 4096).ToArray());

  private async Task CaptureEmpty(int times)
  {
    _streamFactory = () => new MemoryStream();
    for (var i = 0; i < times; i++)
    {
      Assert.Null(await _tap.CaptureAsync(Capture));
    }
  }

  private List<LogLevel> NoAudioLevels() =>
    _tapLogs.Where(l => l.Message.StartsWith("No audio data captured")).Select(l => l.Level).ToList();

  [Fact]
  public async Task PlayingSource_NConsecutiveEmptyWindows_TripTheWatchdog()
  {
    await CaptureEmpty(N - 1);
    Assert.False(_watchdog.IsLatched);

    await CaptureEmpty(1);
    Assert.True(_watchdog.IsLatched);
    Assert.Single(_watchdogLogs, l => l.Level == LogLevel.Warning && l.Message.Contains("SDR Radio"));
  }

  [Fact]
  public async Task PerWindowWarning_IsKeptBeforeTheTrip_AndDemotedFromTheTrippingWindowOn()
  {
    await CaptureEmpty(N + 10);

    var levels = NoAudioLevels();
    Assert.Equal(N + 10, levels.Count);
    // The first occurrences are not hidden...
    Assert.All(levels.Take(N - 1), l => Assert.Equal(LogLevel.Warning, l));
    // ...the tripping window is reported by the watchdog's Warning instead, and every window after it is Debug.
    Assert.All(levels.Skip(N - 1), l => Assert.Equal(LogLevel.Debug, l));
  }

  [Fact]
  public async Task AfterRecovery_ThePerWindowWarningComesBack()
  {
    await CaptureEmpty(N);
    _streamFactory = Audio;
    Assert.NotNull(await _tap.CaptureAsync(Capture));
    Assert.False(_watchdog.IsLatched);
    Assert.Single(_watchdogLogs, l => l.Level == LogLevel.Information);

    await CaptureEmpty(1);
    Assert.Equal(LogLevel.Warning, NoAudioLevels().Last());
  }

  [Fact]
  public async Task IntermittentEmptyWindows_NeverTrip()
  {
    for (var i = 0; i < 3; i++)
    {
      await CaptureEmpty(N - 1);
      _streamFactory = Audio;
      Assert.NotNull(await _tap.CaptureAsync(Capture));
    }

    Assert.False(_watchdog.IsLatched);
    Assert.Empty(_watchdogLogs);
  }

  [Fact]
  public async Task SourcePausedDuringEachWindow_NeverTrips()
  {
    // Every window starts Playing (so the capture runs) and the source pauses before it ends.
    for (var i = 0; i < N + 5; i++)
    {
      _state = AudioSourceState.Playing;
      _streamFactory = () =>
      {
        _state = AudioSourceState.Paused;
        return new MemoryStream();
      };
      Assert.Null(await _tap.CaptureAsync(Capture));
    }

    Assert.False(_watchdog.IsLatched);
    Assert.Empty(_watchdogLogs);
  }

  [Fact]
  public async Task PausedSource_IsNotCapturedAtAll()
  {
    _state = AudioSourceState.Paused;
    for (var i = 0; i < N + 5; i++)
    {
      Assert.Null(await _tap.CaptureAsync(Capture));
    }

    _engine.Verify(e => e.CreateStreamReader(It.IsAny<string>(), It.IsAny<double?>()), Times.Never);
    Assert.False(_watchdog.IsLatched);
  }

  [Theory]
  [InlineData(BluetoothPipelineStatus.WaitingForCaptureNode)] // handset paused: the capture node is gone
  [InlineData(BluetoothPipelineStatus.Degraded)]              // no phone connected
  [InlineData(BluetoothPipelineStatus.Broken)]
  public async Task BluetoothPlaying_WithItsPipelineNotHealthy_NeverTrips(BluetoothPipelineStatus status)
  {
    _source.SetupGet(s => s.Type).Returns(AudioSourceType.Bluetooth);
    _bluetooth.SetupGet(b => b.PipelineStatus).Returns(status);

    await CaptureEmpty(N + 5);

    Assert.False(_watchdog.IsLatched);
    Assert.Empty(_watchdogLogs);
  }

  [Fact]
  public async Task BluetoothPlaying_WithAHealthyPipeline_Trips()
  {
    // The 2026-09-25 23:48 shape: pipeline up, AVRCP says Playing, nothing reaching the tap.
    _source.SetupGet(s => s.Type).Returns(AudioSourceType.Bluetooth);

    await CaptureEmpty(N);

    Assert.True(_watchdog.IsLatched);
  }

  [Fact]
  public async Task ActiveSourceChangedDuringTheWindow_DoesNotCount()
  {
    var other = new Mock<IAudioSource>();
    other.SetupGet(s => s.State).Returns(AudioSourceState.Playing);
    other.SetupGet(s => s.Type).Returns(AudioSourceType.Radio);
    other.SetupGet(s => s.Name).Returns("Other");

    for (var i = 0; i < N + 5; i++)
    {
      var (from, to) = i % 2 == 0 ? (_source.Object, other.Object) : (other.Object, _source.Object);
      _manager.SetupGet(m => m.ActiveSource).Returns(from);
      _streamFactory = () =>
      {
        _manager.SetupGet(m => m.ActiveSource).Returns(to);
        return new MemoryStream();
      };
      Assert.Null(await _tap.CaptureAsync(Capture));
    }

    Assert.False(_watchdog.IsLatched);
  }

  private sealed class CapturingLogger<T>(List<(LogLevel Level, string Message)> sink) : ILogger<T>
  {
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
      LogLevel logLevel, EventId eventId, TState state, Exception? exception,
      Func<TState, Exception?, string> formatter)
    {
      lock (sink)
      {
        sink.Add((logLevel, formatter(state, exception)));
      }
    }
  }
}

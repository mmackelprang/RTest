using Microsoft.Extensions.Logging;
using Moq;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Sources.Events;

namespace Radio.Infrastructure.Tests.Audio.Events;

public class TTSEventSourceTests
{
  private readonly Mock<ILogger<TTSEventSource>> _loggerMock;

  public TTSEventSourceTests()
  {
    _loggerMock = new Mock<ILogger<TTSEventSource>>();
  }

  private TTSEventSource CreateSource(
    string text = "Test speech",
    TTSParameters? parameters = null,
    Stream? audioStream = null,
    TimeSpan? duration = null)
  {
    var stream = audioStream ?? new MemoryStream(new byte[1000]);
    var dur = duration ?? TimeSpan.FromSeconds(1);
    var parms = parameters ?? new TTSParameters();

    return new TTSEventSource(text, parms, stream, dur, _loggerMock.Object);
  }

  [Fact]
  public void Constructor_SetsCorrectType()
  {
    var source = CreateSource();

    Assert.Equal(AudioSourceType.TTS, source.Type);
  }

  [Fact]
  public void Constructor_SetsEventCategory()
  {
    var source = CreateSource();

    Assert.Equal(AudioSourceCategory.Event, source.Category);
  }

  [Fact]
  public void Constructor_SetsInitialState()
  {
    var source = CreateSource();

    Assert.Equal(AudioSourceState.Created, source.State);
  }

  [Fact]
  public void Constructor_TruncatesLongTextInName()
  {
    var longText = new string('a', 100);
    var source = CreateSource(longText);

    Assert.Contains("...", source.Name);
    Assert.StartsWith("TTS:", source.Name);
  }

  [Fact]
  public void Constructor_PreservesShortTextInName()
  {
    var shortText = "Hello";
    var source = CreateSource(shortText);

    Assert.Contains(shortText, source.Name);
    Assert.DoesNotContain("...", source.Name);
  }

  [Fact]
  public void Text_ReturnsOriginalText()
  {
    var text = "Original text";
    var source = CreateSource(text);

    Assert.Equal(text, source.Text);
  }

  [Fact]
  public void Parameters_ReturnsPassedParameters()
  {
    var parameters = new TTSParameters
    {
      Engine = TTSEngine.Azure,
      Voice = "test-voice",
      Speed = 1.5f,
      Pitch = 0.8f
    };
    var source = CreateSource(parameters: parameters);

    Assert.Equal(parameters.Engine, source.Parameters.Engine);
    Assert.Equal(parameters.Voice, source.Parameters.Voice);
    Assert.Equal(parameters.Speed, source.Parameters.Speed);
    Assert.Equal(parameters.Pitch, source.Parameters.Pitch);
  }

  [Fact]
  public void Duration_ReturnsCorrectValue()
  {
    var duration = TimeSpan.FromSeconds(5);
    var source = CreateSource(duration: duration);

    Assert.Equal(duration, source.Duration);
  }

  [Fact]
  public void Volume_DefaultsToOne()
  {
    var source = CreateSource();

    Assert.Equal(1.0f, source.Volume);
  }

  [Fact]
  public void Volume_CanBeSet()
  {
    var source = CreateSource();

    source.Volume = 0.5f;

    Assert.Equal(0.5f, source.Volume);
  }

  [Fact]
  public void Volume_ClampsToValidRange()
  {
    var source = CreateSource();

    source.Volume = 1.5f;
    Assert.Equal(1.0f, source.Volume);

    source.Volume = -0.5f;
    Assert.Equal(0.0f, source.Volume);
  }

  [Fact]
  public void Id_IsGenerated()
  {
    var source = CreateSource();

    Assert.NotNull(source.Id);
    Assert.StartsWith("TTS-", source.Id);
  }

  [Fact]
  public void GetSoundComponent_ReturnsStream()
  {
    var stream = new MemoryStream(new byte[100]);
    var source = CreateSource(audioStream: stream);

    var component = source.GetSoundComponent();

    Assert.Same(stream, component);
  }

  [Fact]
  public async Task PlayAsync_ChangesStateToPlaying()
  {
    var source = CreateSource();

    await source.PlayAsync();

    Assert.Equal(AudioSourceState.Playing, source.State);
  }

  [Fact]
  public async Task StopAsync_ChangesStateToStopped()
  {
    var source = CreateSource();
    await source.PlayAsync();

    await source.StopAsync();

    Assert.Equal(AudioSourceState.Stopped, source.State);
  }

  [Fact]
  public async Task PlaybackCompleted_IsRaisedOnCompletion()
  {
    var duration = TimeSpan.FromMilliseconds(50);
    var source = CreateSource(duration: duration);
    var completedEvent = new TaskCompletionSource<AudioSourceCompletedEventArgs>();

    source.PlaybackCompleted += (_, args) => completedEvent.TrySetResult(args);

    await source.PlayAsync();

    var args = await completedEvent.Task.WaitAsync(TimeSpan.FromSeconds(5));

    Assert.Equal(source.Id, args.SourceId);
    Assert.Equal(PlaybackCompletionReason.EndOfContent, args.Reason);
  }

  [Fact]
  public async Task StopAsync_RaisesPlaybackCompleted_WithUserStopped()
  {
    var source = CreateSource(duration: TimeSpan.FromMinutes(1)); // Long duration
    var completedEvent = new TaskCompletionSource<AudioSourceCompletedEventArgs>();

    source.PlaybackCompleted += (_, args) => completedEvent.TrySetResult(args);

    await source.PlayAsync();
    await source.StopAsync();

    var args = await completedEvent.Task.WaitAsync(TimeSpan.FromSeconds(1));

    Assert.Equal(PlaybackCompletionReason.UserStopped, args.Reason);
  }

  [Fact]
  public async Task DisposeAsync_DisposesResources()
  {
    var source = CreateSource();
    await source.PlayAsync();

    await source.DisposeAsync();

    Assert.Equal(AudioSourceState.Disposed, source.State);
  }

  [Fact]
  public async Task PlayAsync_ThrowsWhenDisposed()
  {
    var source = CreateSource();
    await source.DisposeAsync();

    await Assert.ThrowsAsync<ObjectDisposedException>(() => source.PlayAsync());
  }

  [Fact]
  public void StateChanged_IsRaisedOnStateChange()
  {
    var source = CreateSource();
    var stateChanges = new List<AudioSourceStateChangedEventArgs>();

    source.StateChanged += (_, args) => stateChanges.Add(args);

    source.Volume = 0.5f; // Should not trigger state change

    Assert.Empty(stateChanges);
  }
}

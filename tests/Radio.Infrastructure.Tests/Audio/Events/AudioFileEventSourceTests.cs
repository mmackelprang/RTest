using Microsoft.Extensions.Logging;
using Moq;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Sources.Events;

namespace Radio.Infrastructure.Tests.Audio.Events;

public class AudioFileEventSourceTests
{
  private readonly Mock<ILogger<AudioFileEventSource>> _loggerMock;
  private readonly string _testFilePath;

  public AudioFileEventSourceTests()
  {
    _loggerMock = new Mock<ILogger<AudioFileEventSource>>();
    _testFilePath = "/test/audio.wav";
  }

  private AudioFileEventSource CreateSource(
    string? filePath = null,
    TimeSpan? duration = null)
  {
    return new AudioFileEventSource(
      filePath ?? _testFilePath,
      duration ?? TimeSpan.FromSeconds(1),
      _loggerMock.Object);
  }

  private AudioFileEventSource CreateSourceFromStream(
    string name = "Test Event",
    Stream? audioStream = null,
    TimeSpan? duration = null)
  {
    var stream = audioStream ?? new MemoryStream(new byte[1000]);
    return new AudioFileEventSource(
      name,
      stream,
      duration ?? TimeSpan.FromSeconds(1),
      _loggerMock.Object);
  }

  [Fact]
  public void Constructor_WithFilePath_SetsCorrectType()
  {
    var source = CreateSource();

    Assert.Equal(AudioSourceType.AudioFileEvent, source.Type);
  }

  [Fact]
  public void Constructor_WithFilePath_SetsEventCategory()
  {
    var source = CreateSource();

    Assert.Equal(AudioSourceCategory.Event, source.Category);
  }

  [Fact]
  public void Constructor_WithFilePath_SetsInitialState()
  {
    var source = CreateSource();

    Assert.Equal(AudioSourceState.Created, source.State);
  }

  [Fact]
  public void Constructor_WithFilePath_SetsNameFromFile()
  {
    var source = CreateSource("/path/to/notification.wav");

    Assert.Contains("notification.wav", source.Name);
    Assert.StartsWith("Event:", source.Name);
  }

  [Fact]
  public void Constructor_WithStream_SetsName()
  {
    var source = CreateSourceFromStream("Doorbell");

    Assert.Contains("Doorbell", source.Name);
    Assert.StartsWith("Event:", source.Name);
  }

  [Fact]
  public void FilePath_ReturnsCorrectValue()
  {
    var filePath = "/test/sound.wav";
    var source = CreateSource(filePath);

    Assert.Equal(filePath, source.FilePath);
  }

  [Fact]
  public void FilePath_IsEmptyForStreamSource()
  {
    var source = CreateSourceFromStream();

    Assert.Equal(string.Empty, source.FilePath);
  }

  [Fact]
  public void Duration_ReturnsCorrectValue()
  {
    var duration = TimeSpan.FromSeconds(3);
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

    source.Volume = 0.75f;

    Assert.Equal(0.75f, source.Volume);
  }

  [Fact]
  public void Volume_ClampsToValidRange()
  {
    var source = CreateSource();

    source.Volume = 2.0f;
    Assert.Equal(1.0f, source.Volume);

    source.Volume = -1.0f;
    Assert.Equal(0.0f, source.Volume);
  }

  [Fact]
  public void Id_IsGenerated()
  {
    var source = CreateSource();

    Assert.NotNull(source.Id);
    Assert.StartsWith("AudioFileEvent-", source.Id);
  }

  [Fact]
  public void GetSoundComponent_ReturnsStreamForStreamSource()
  {
    var stream = new MemoryStream(new byte[100]);
    var source = CreateSourceFromStream(audioStream: stream);

    var component = source.GetSoundComponent();

    Assert.Same(stream, component);
  }

  [Fact]
  public void GetSoundComponent_ReturnsFilePathForFileSource()
  {
    var filePath = "/test/notification.wav";
    var source = CreateSource(filePath);

    var component = source.GetSoundComponent();

    // Before initialization, returns the file path
    Assert.Equal(filePath, component);
  }

  [Fact]
  public async Task PlayAsync_WithStream_ChangesStateToPlaying()
  {
    var source = CreateSourceFromStream();

    await source.PlayAsync();

    Assert.Equal(AudioSourceState.Playing, source.State);
  }

  [Fact]
  public async Task StopAsync_ChangesStateToStopped()
  {
    var source = CreateSourceFromStream();
    await source.PlayAsync();

    await source.StopAsync();

    Assert.Equal(AudioSourceState.Stopped, source.State);
  }

  [Fact]
  public async Task PlaybackCompleted_IsRaisedOnCompletion()
  {
    var duration = TimeSpan.FromMilliseconds(50);
    var source = CreateSourceFromStream(duration: duration);
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
    var source = CreateSourceFromStream(duration: TimeSpan.FromMinutes(1));
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
    var source = CreateSourceFromStream();
    await source.PlayAsync();

    await source.DisposeAsync();

    Assert.Equal(AudioSourceState.Disposed, source.State);
  }

  [Fact]
  public async Task PlayAsync_ThrowsWhenDisposed()
  {
    var source = CreateSourceFromStream();
    await source.DisposeAsync();

    await Assert.ThrowsAsync<ObjectDisposedException>(() => source.PlayAsync());
  }

  [Fact]
  public void StateChanged_IsRaisedOnStateChange()
  {
    var source = CreateSourceFromStream();
    var stateChanges = new List<AudioSourceStateChangedEventArgs>();

    source.StateChanged += (_, args) => stateChanges.Add(args);

    // Volume change should not trigger state change
    source.Volume = 0.5f;

    Assert.Empty(stateChanges);
  }
}

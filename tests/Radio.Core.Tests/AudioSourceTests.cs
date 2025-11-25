using Radio.Core.Interfaces.Audio;

namespace Radio.Core.Tests;

/// <summary>
/// Tests for the IAudioSource interface and related types.
/// </summary>
public class AudioSourceTests
{
  [Fact]
  public void AudioSourceType_HasExpectedValues()
  {
    // Verify all expected audio source types exist
    var types = Enum.GetValues<AudioSourceType>();

    Assert.Contains(AudioSourceType.Spotify, types);
    Assert.Contains(AudioSourceType.Radio, types);
    Assert.Contains(AudioSourceType.Vinyl, types);
    Assert.Contains(AudioSourceType.FilePlayer, types);
    Assert.Contains(AudioSourceType.GenericUSB, types);
    Assert.Contains(AudioSourceType.TTS, types);
    Assert.Contains(AudioSourceType.AudioFileEvent, types);
  }

  [Fact]
  public void AudioSourceCategory_HasExpectedValues()
  {
    var categories = Enum.GetValues<AudioSourceCategory>();

    Assert.Contains(AudioSourceCategory.Primary, categories);
    Assert.Contains(AudioSourceCategory.Event, categories);
  }

  [Fact]
  public void AudioSourceState_HasExpectedValues()
  {
    var states = Enum.GetValues<AudioSourceState>();

    Assert.Contains(AudioSourceState.Created, states);
    Assert.Contains(AudioSourceState.Initializing, states);
    Assert.Contains(AudioSourceState.Ready, states);
    Assert.Contains(AudioSourceState.Playing, states);
    Assert.Contains(AudioSourceState.Paused, states);
    Assert.Contains(AudioSourceState.Stopped, states);
    Assert.Contains(AudioSourceState.Error, states);
    Assert.Contains(AudioSourceState.Disposed, states);
  }

  [Fact]
  public void AudioSourceStateChangedEventArgs_CanBeCreated()
  {
    var args = new AudioSourceStateChangedEventArgs
    {
      SourceId = "test-source",
      PreviousState = AudioSourceState.Ready,
      NewState = AudioSourceState.Playing
    };

    Assert.Equal("test-source", args.SourceId);
    Assert.Equal(AudioSourceState.Ready, args.PreviousState);
    Assert.Equal(AudioSourceState.Playing, args.NewState);
  }
}

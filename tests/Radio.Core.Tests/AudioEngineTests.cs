using Radio.Core.Interfaces.Audio;

namespace Radio.Core.Tests;

/// <summary>
/// Tests for the IAudioEngine interface and related types.
/// </summary>
public class AudioEngineTests
{
  [Fact]
  public void AudioEngineState_HasExpectedValues()
  {
    var states = Enum.GetValues<AudioEngineState>();

    Assert.Contains(AudioEngineState.Uninitialized, states);
    Assert.Contains(AudioEngineState.Initializing, states);
    Assert.Contains(AudioEngineState.Ready, states);
    Assert.Contains(AudioEngineState.Running, states);
    Assert.Contains(AudioEngineState.Stopping, states);
    Assert.Contains(AudioEngineState.Error, states);
    Assert.Contains(AudioEngineState.Disposed, states);
  }

  [Fact]
  public void DeviceChangeType_HasExpectedValues()
  {
    var types = Enum.GetValues<DeviceChangeType>();

    Assert.Contains(DeviceChangeType.Added, types);
    Assert.Contains(DeviceChangeType.Removed, types);
    Assert.Contains(DeviceChangeType.DefaultChanged, types);
  }

  [Fact]
  public void AudioEngineStateChangedEventArgs_CanBeCreated()
  {
    var args = new AudioEngineStateChangedEventArgs
    {
      PreviousState = AudioEngineState.Uninitialized,
      NewState = AudioEngineState.Ready
    };

    Assert.Equal(AudioEngineState.Uninitialized, args.PreviousState);
    Assert.Equal(AudioEngineState.Ready, args.NewState);
  }
}

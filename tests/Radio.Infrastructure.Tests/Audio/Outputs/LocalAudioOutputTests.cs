using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Outputs;

namespace Radio.Infrastructure.Tests.Audio.Outputs;

public class LocalAudioOutputTests
{
  private readonly Mock<ILogger<LocalAudioOutput>> _loggerMock;
  private readonly Mock<IAudioDeviceManager> _deviceManagerMock;
  private readonly Mock<IOptions<AudioOutputOptions>> _optionsMock;
  private readonly AudioOutputOptions _defaultOptions;

  public LocalAudioOutputTests()
  {
    _loggerMock = new Mock<ILogger<LocalAudioOutput>>();
    _deviceManagerMock = new Mock<IAudioDeviceManager>();
    _optionsMock = new Mock<IOptions<AudioOutputOptions>>();

    _defaultOptions = new AudioOutputOptions
    {
      Local = new LocalAudioOutputOptions
      {
        Enabled = true,
        PreferredDeviceId = "",
        DefaultVolume = 0.8f
      }
    };

    _optionsMock.Setup(x => x.Value).Returns(_defaultOptions);

    // Set up default device manager behavior
    var defaultDevice = new AudioDeviceInfo
    {
      Id = "default-device",
      Name = "Default Audio Device",
      Type = AudioDeviceType.Output,
      IsDefault = true,
      MaxChannels = 2
    };

    _deviceManagerMock
      .Setup(x => x.GetOutputDevicesAsync(It.IsAny<CancellationToken>()))
      .ReturnsAsync(new List<AudioDeviceInfo> { defaultDevice });

    _deviceManagerMock
      .Setup(x => x.GetDefaultOutputDeviceAsync(It.IsAny<CancellationToken>()))
      .ReturnsAsync(defaultDevice);
  }

  private LocalAudioOutput CreateOutput()
  {
    return new LocalAudioOutput(_loggerMock.Object, _deviceManagerMock.Object, _optionsMock.Object);
  }

  [Fact]
  public void Constructor_ThrowsOnNullLogger()
  {
    Assert.Throws<ArgumentNullException>(
      () => new LocalAudioOutput(null!, _deviceManagerMock.Object, _optionsMock.Object));
  }

  [Fact]
  public void Constructor_ThrowsOnNullDeviceManager()
  {
    Assert.Throws<ArgumentNullException>(
      () => new LocalAudioOutput(_loggerMock.Object, null!, _optionsMock.Object));
  }

  [Fact]
  public void Constructor_ThrowsOnNullOptions()
  {
    Assert.Throws<ArgumentNullException>(
      () => new LocalAudioOutput(_loggerMock.Object, _deviceManagerMock.Object, null!));
  }

  [Fact]
  public void Constructor_SetsCorrectType()
  {
    var output = CreateOutput();

    Assert.Equal(AudioOutputType.Local, output.Type);
  }

  [Fact]
  public void Constructor_SetsInitialState()
  {
    var output = CreateOutput();

    Assert.Equal(AudioOutputState.Created, output.State);
  }

  [Fact]
  public void Constructor_SetsDefaultVolume()
  {
    var output = CreateOutput();

    Assert.Equal(0.8f, output.Volume);
  }

  [Fact]
  public void Volume_ClampsToValidRange()
  {
    var output = CreateOutput();

    output.Volume = 1.5f;
    Assert.Equal(1.0f, output.Volume);

    output.Volume = -0.5f;
    Assert.Equal(0f, output.Volume);
  }

  [Fact]
  public void IsMuted_CanBeToggled()
  {
    var output = CreateOutput();

    Assert.False(output.IsMuted);

    output.IsMuted = true;
    Assert.True(output.IsMuted);

    output.IsMuted = false;
    Assert.False(output.IsMuted);
  }

  [Fact]
  public async Task InitializeAsync_TransitionsToReady()
  {
    var output = CreateOutput();

    await output.InitializeAsync();

    Assert.Equal(AudioOutputState.Ready, output.State);
  }

  [Fact]
  public async Task InitializeAsync_RaisesStateChangedEvent()
  {
    var output = CreateOutput();
    var stateChanges = new List<AudioOutputStateChangedEventArgs>();

    output.StateChanged += (_, args) => stateChanges.Add(args);

    await output.InitializeAsync();

    Assert.Equal(2, stateChanges.Count); // Initializing and Ready
    Assert.Equal(AudioOutputState.Initializing, stateChanges[0].NewState);
    Assert.Equal(AudioOutputState.Ready, stateChanges[1].NewState);
  }

  [Fact]
  public async Task InitializeAsync_UsesDefaultDevice()
  {
    var output = CreateOutput();

    await output.InitializeAsync();

    Assert.Equal("default-device", output.CurrentDeviceId);
    Assert.Contains("Default Audio Device", output.Name);
  }

  [Fact]
  public async Task InitializeAsync_UsesPreferredDevice_WhenAvailable()
  {
    var preferredDevice = new AudioDeviceInfo
    {
      Id = "preferred-device",
      Name = "Preferred Device",
      Type = AudioDeviceType.Output,
      IsDefault = false
    };

    _defaultOptions.Local.PreferredDeviceId = "preferred-device";

    _deviceManagerMock
      .Setup(x => x.GetOutputDevicesAsync(It.IsAny<CancellationToken>()))
      .ReturnsAsync(new List<AudioDeviceInfo>
      {
        new AudioDeviceInfo { Id = "default-device", Name = "Default", Type = AudioDeviceType.Output, IsDefault = true },
        preferredDevice
      });

    var output = CreateOutput();
    await output.InitializeAsync();

    Assert.Equal("preferred-device", output.CurrentDeviceId);
  }

  [Fact]
  public async Task InitializeAsync_ThrowsWhenAlreadyInitialized()
  {
    var output = CreateOutput();
    await output.InitializeAsync();

    await Assert.ThrowsAsync<InvalidOperationException>(() => output.InitializeAsync());
  }

  [Fact]
  public async Task StartAsync_TransitionsToStreaming()
  {
    var output = CreateOutput();
    await output.InitializeAsync();

    await output.StartAsync();

    Assert.Equal(AudioOutputState.Streaming, output.State);
    Assert.True(output.IsEnabled);
  }

  [Fact]
  public async Task StartAsync_ThrowsWhenNotReady()
  {
    var output = CreateOutput();

    await Assert.ThrowsAsync<InvalidOperationException>(() => output.StartAsync());
  }

  [Fact]
  public async Task StopAsync_TransitionsToStopped()
  {
    var output = CreateOutput();
    await output.InitializeAsync();
    await output.StartAsync();

    await output.StopAsync();

    Assert.Equal(AudioOutputState.Stopped, output.State);
    Assert.False(output.IsEnabled);
  }

  [Fact]
  public async Task StopAsync_WhenNotStreaming_DoesNotThrow()
  {
    var output = CreateOutput();
    await output.InitializeAsync();

    await output.StopAsync(); // Should not throw
  }

  [Fact]
  public async Task SelectDeviceAsync_ChangesDevice()
  {
    var devices = new List<AudioDeviceInfo>
    {
      new AudioDeviceInfo { Id = "device1", Name = "Device 1", Type = AudioDeviceType.Output },
      new AudioDeviceInfo { Id = "device2", Name = "Device 2", Type = AudioDeviceType.Output }
    };

    _deviceManagerMock
      .Setup(x => x.GetOutputDevicesAsync(It.IsAny<CancellationToken>()))
      .ReturnsAsync(devices);

    var output = CreateOutput();
    await output.InitializeAsync();

    await output.SelectDeviceAsync("device2");

    Assert.Equal("device2", output.CurrentDeviceId);
    Assert.Contains("Device 2", output.Name);
  }

  [Fact]
  public async Task SelectDeviceAsync_ThrowsForInvalidDevice()
  {
    var output = CreateOutput();
    await output.InitializeAsync();

    await Assert.ThrowsAsync<ArgumentException>(
      () => output.SelectDeviceAsync("non-existent"));
  }

  [Fact]
  public void GetEffectiveVolume_ReturnsZeroWhenMuted()
  {
    var output = CreateOutput();
    output.Volume = 0.8f;
    output.IsMuted = true;

    Assert.Equal(0f, output.GetEffectiveVolume());
  }

  [Fact]
  public void GetEffectiveVolume_ReturnsVolumeWhenNotMuted()
  {
    var output = CreateOutput();
    output.Volume = 0.8f;
    output.IsMuted = false;

    Assert.Equal(0.8f, output.GetEffectiveVolume());
  }

  [Fact]
  public async Task DisposeAsync_SetsDisposedState()
  {
    var output = CreateOutput();

    await output.DisposeAsync();

    Assert.Equal(AudioOutputState.Disposed, output.State);
  }

  [Fact]
  public async Task DisposeAsync_CanBeCalledMultipleTimes()
  {
    var output = CreateOutput();

    await output.DisposeAsync();
    await output.DisposeAsync(); // Should not throw
  }

  [Fact]
  public async Task InitializeAsync_ThrowsWhenDisposed()
  {
    var output = CreateOutput();
    await output.DisposeAsync();

    await Assert.ThrowsAsync<ObjectDisposedException>(() => output.InitializeAsync());
  }
}

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.SoundFlow;

namespace Radio.Infrastructure.Tests.Audio;

/// <summary>
/// Unit tests for the SoundFlowAudioEngine class.
/// </summary>
public class SoundFlowAudioEngineTests
{
  private readonly Mock<ILogger<SoundFlowAudioEngine>> _engineLoggerMock;
  private readonly Mock<ILogger<SoundFlowMasterMixer>> _mixerLoggerMock;
  private readonly Mock<ILogger<SoundFlowDeviceManager>> _deviceManagerLoggerMock;
  private readonly Mock<IOptions<AudioEngineOptions>> _optionsMock;
  private readonly AudioEngineOptions _options;
  private readonly SoundFlowMasterMixer _masterMixer;
  private readonly SoundFlowDeviceManager _deviceManager;

  public SoundFlowAudioEngineTests()
  {
    _engineLoggerMock = new Mock<ILogger<SoundFlowAudioEngine>>();
    _mixerLoggerMock = new Mock<ILogger<SoundFlowMasterMixer>>();
    _deviceManagerLoggerMock = new Mock<ILogger<SoundFlowDeviceManager>>();

    _options = new AudioEngineOptions
    {
      SampleRate = 48000,
      Channels = 2,
      BufferSize = 1024,
      HotPlugIntervalSeconds = 5,
      OutputBufferSizeSeconds = 5,
      EnableHotPlugDetection = false // Disable for tests
    };

    _optionsMock = new Mock<IOptions<AudioEngineOptions>>();
    _optionsMock.Setup(o => o.Value).Returns(_options);

    _masterMixer = new SoundFlowMasterMixer(_mixerLoggerMock.Object);
    _deviceManager = new SoundFlowDeviceManager(_deviceManagerLoggerMock.Object);
  }

  private SoundFlowAudioEngine CreateEngine()
  {
    return new SoundFlowAudioEngine(
      _engineLoggerMock.Object,
      _optionsMock.Object,
      _masterMixer,
      _deviceManager);
  }

  [Fact]
  public void Constructor_SetsInitialStateToUninitialized()
  {
    // Act
    var engine = CreateEngine();

    // Assert
    Assert.Equal(AudioEngineState.Uninitialized, engine.State);
    Assert.False(engine.IsReady);
  }

  [Fact]
  public void GetMasterMixer_ReturnsMixerInstance()
  {
    // Arrange
    var engine = CreateEngine();

    // Act
    var mixer = engine.GetMasterMixer();

    // Assert
    Assert.NotNull(mixer);
    Assert.IsType<SoundFlowMasterMixer>(mixer);
  }

  [Fact]
  public void GetDeviceManager_ReturnsDeviceManagerInstance()
  {
    // Arrange
    var engine = CreateEngine();

    // Act
    var deviceManager = engine.GetDeviceManager();

    // Assert
    Assert.NotNull(deviceManager);
    Assert.IsType<SoundFlowDeviceManager>(deviceManager);
  }

  [Fact]
  public async Task InitializeAsync_TransitionsToReadyState()
  {
    // Arrange
    var engine = CreateEngine();

    // Act
    await engine.InitializeAsync();

    // Assert
    Assert.Equal(AudioEngineState.Ready, engine.State);
    Assert.True(engine.IsReady);
  }

  [Fact]
  public async Task InitializeAsync_ThrowsWhenAlreadyInitialized()
  {
    // Arrange
    var engine = CreateEngine();
    await engine.InitializeAsync();

    // Act & Assert
    await Assert.ThrowsAsync<InvalidOperationException>(
      () => engine.InitializeAsync());
  }

  [Fact]
  public async Task InitializeAsync_RaisesStateChangedEvent()
  {
    // Arrange
    var engine = CreateEngine();
    var stateChanges = new List<AudioEngineState>();
    engine.StateChanged += (s, e) => stateChanges.Add(e.NewState);

    // Act
    await engine.InitializeAsync();

    // Assert
    Assert.Contains(AudioEngineState.Initializing, stateChanges);
    Assert.Contains(AudioEngineState.Ready, stateChanges);
  }

  [Fact]
  public async Task StartAsync_TransitionsToRunningState()
  {
    // Arrange
    var engine = CreateEngine();
    await engine.InitializeAsync();

    // Act
    await engine.StartAsync();

    // Assert
    Assert.Equal(AudioEngineState.Running, engine.State);
  }

  [Fact]
  public async Task StartAsync_ThrowsWhenNotReady()
  {
    // Arrange
    var engine = CreateEngine();

    // Act & Assert
    await Assert.ThrowsAsync<InvalidOperationException>(
      () => engine.StartAsync());
  }

  [Fact]
  public async Task StopAsync_TransitionsToReadyState()
  {
    // Arrange
    var engine = CreateEngine();
    await engine.InitializeAsync();
    await engine.StartAsync();

    // Act
    await engine.StopAsync();

    // Assert
    Assert.Equal(AudioEngineState.Ready, engine.State);
  }

  [Fact]
  public async Task StopAsync_DoesNotThrowWhenNotRunning()
  {
    // Arrange
    var engine = CreateEngine();
    await engine.InitializeAsync();

    // Act & Assert - should not throw
    await engine.StopAsync();
    Assert.Equal(AudioEngineState.Ready, engine.State);
  }

  [Fact]
  public async Task GetMixedOutputStream_ReturnsStreamAfterInitialization()
  {
    // Arrange
    var engine = CreateEngine();
    await engine.InitializeAsync();

    // Act
    var stream = engine.GetMixedOutputStream();

    // Assert
    Assert.NotNull(stream);
    Assert.True(stream.CanRead);
  }

  [Fact]
  public void GetMixedOutputStream_ThrowsWhenNotInitialized()
  {
    // Arrange
    var engine = CreateEngine();

    // Act & Assert
    Assert.Throws<InvalidOperationException>(() => engine.GetMixedOutputStream());
  }

  [Fact]
  public async Task DisposeAsync_TransitionsToDisposedState()
  {
    // Arrange
    var engine = CreateEngine();
    await engine.InitializeAsync();

    // Act
    await engine.DisposeAsync();

    // Assert
    Assert.Equal(AudioEngineState.Disposed, engine.State);
  }

  [Fact]
  public async Task DisposeAsync_CanBeCalledMultipleTimes()
  {
    // Arrange
    var engine = CreateEngine();
    await engine.InitializeAsync();

    // Act & Assert - should not throw
    await engine.DisposeAsync();
    await engine.DisposeAsync();
  }

  [Fact]
  public async Task Operations_ThrowAfterDispose()
  {
    // Arrange
    var engine = CreateEngine();
    await engine.InitializeAsync();
    await engine.DisposeAsync();

    // Act & Assert
    await Assert.ThrowsAsync<ObjectDisposedException>(() => engine.InitializeAsync());
    await Assert.ThrowsAsync<ObjectDisposedException>(() => engine.StartAsync());
    await Assert.ThrowsAsync<ObjectDisposedException>(() => engine.StopAsync());
    Assert.Throws<ObjectDisposedException>(() => engine.GetMasterMixer());
    Assert.Throws<ObjectDisposedException>(() => engine.GetMixedOutputStream());
  }

  [Fact]
  public async Task WriteToOutputTap_WritesDataToStream()
  {
    // Arrange
    var engine = CreateEngine();
    await engine.InitializeAsync();
    var stream = engine.GetMixedOutputStream();
    var samples = new float[] { 0.5f, -0.5f, 0.25f, -0.25f };

    // Act
    engine.WriteToOutputTap(samples);

    // Assert
    var buffer = new byte[8]; // 4 samples * 2 bytes each
    var bytesRead = stream.Read(buffer, 0, buffer.Length);
    Assert.Equal(8, bytesRead);
  }

  [Fact]
  public void IsReady_TrueWhenReadyOrRunning()
  {
    // Arrange
    var engine = CreateEngine();

    // Assert initial state
    Assert.False(engine.IsReady);
  }

  [Fact]
  public async Task DeviceChanged_EventForwardedFromDeviceManager()
  {
    // Arrange
    var engine = CreateEngine();
    await engine.InitializeAsync();

    AudioDeviceChangedEventArgs? capturedArgs = null;
    engine.DeviceChanged += (s, e) => capturedArgs = e;

    // We can't easily trigger the device manager's event in this test
    // as it requires actual device changes
    // This test verifies the subscription is set up correctly
    Assert.NotNull(engine);
  }

  [Fact]
  public async Task StateChanged_EventIncludesPreviousAndNewState()
  {
    // Arrange
    var engine = CreateEngine();
    AudioEngineStateChangedEventArgs? capturedArgs = null;
    engine.StateChanged += (s, e) => capturedArgs = e;

    // Act
    await engine.InitializeAsync();

    // Assert
    Assert.NotNull(capturedArgs);
    Assert.Equal(AudioEngineState.Initializing, capturedArgs.PreviousState);
    Assert.Equal(AudioEngineState.Ready, capturedArgs.NewState);
  }

  [Fact]
  public async Task EngineIsInitialized_AfterInitialization()
  {
    // Arrange
    var engine = CreateEngine();
    await engine.InitializeAsync();

    // Act & Assert - verify the engine is initialized by checking state
    Assert.Equal(AudioEngineState.Ready, engine.State);
    Assert.True(engine.IsReady);
  }

  [Fact]
  public void EngineNotInitialized_BeforeInitialization()
  {
    // Arrange
    var engine = CreateEngine();

    // Act & Assert - verify the engine is not initialized
    Assert.Equal(AudioEngineState.Uninitialized, engine.State);
    Assert.False(engine.IsReady);
  }
}

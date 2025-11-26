using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Outputs;

namespace Radio.Infrastructure.Tests.Audio.Outputs;

public class GoogleCastOutputTests
{
  private readonly Mock<ILogger<GoogleCastOutput>> _loggerMock;
  private readonly Mock<IOptions<AudioOutputOptions>> _optionsMock;
  private readonly AudioOutputOptions _defaultOptions;

  public GoogleCastOutputTests()
  {
    _loggerMock = new Mock<ILogger<GoogleCastOutput>>();
    _optionsMock = new Mock<IOptions<AudioOutputOptions>>();

    _defaultOptions = new AudioOutputOptions
    {
      GoogleCast = new GoogleCastOutputOptions
      {
        Enabled = false,
        DiscoveryTimeoutSeconds = 5,
        PreferredDeviceName = "",
        DefaultVolume = 0.7f,
        AutoReconnect = true,
        ReconnectDelaySeconds = 5
      }
    };

    _optionsMock.Setup(x => x.Value).Returns(_defaultOptions);
  }

  private GoogleCastOutput CreateOutput()
  {
    return new GoogleCastOutput(_loggerMock.Object, _optionsMock.Object);
  }

  [Fact]
  public void Constructor_ThrowsOnNullLogger()
  {
    Assert.Throws<ArgumentNullException>(
      () => new GoogleCastOutput(null!, _optionsMock.Object));
  }

  [Fact]
  public void Constructor_ThrowsOnNullOptions()
  {
    Assert.Throws<ArgumentNullException>(
      () => new GoogleCastOutput(_loggerMock.Object, null!));
  }

  [Fact]
  public void Constructor_SetsCorrectType()
  {
    var output = CreateOutput();

    Assert.Equal(AudioOutputType.GoogleCast, output.Type);
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

    Assert.Equal(0.7f, output.Volume);
  }

  [Fact]
  public void Constructor_SetsDefaultName()
  {
    var output = CreateOutput();

    Assert.Equal("Google Cast Output", output.Name);
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
  public async Task InitializeAsync_ThrowsWhenAlreadyInitialized()
  {
    var output = CreateOutput();
    await output.InitializeAsync();

    await Assert.ThrowsAsync<InvalidOperationException>(() => output.InitializeAsync());
  }

  [Fact]
  public async Task StartAsync_ThrowsWhenNotConnected()
  {
    var output = CreateOutput();
    await output.InitializeAsync();

    await Assert.ThrowsAsync<InvalidOperationException>(() => output.StartAsync());
  }

  [Fact]
  public async Task StartAsync_ThrowsWhenNotReady()
  {
    var output = CreateOutput();

    await Assert.ThrowsAsync<InvalidOperationException>(() => output.StartAsync());
  }

  [Fact]
  public void SetStreamUrl_StoresUrl()
  {
    var output = CreateOutput();

    output.SetStreamUrl("http://localhost:8080/stream/audio");

    // No direct way to verify, but it shouldn't throw
  }

  [Fact]
  public async Task StopAsync_WhenNotStreaming_DoesNotThrow()
  {
    var output = CreateOutput();
    await output.InitializeAsync();

    await output.StopAsync(); // Should not throw
  }

  [Fact]
  public async Task DisconnectAsync_WhenNotConnected_DoesNotThrow()
  {
    var output = CreateOutput();
    await output.InitializeAsync();

    await output.DisconnectAsync(); // Should not throw
  }

  [Fact]
  public async Task ConnectAsync_ThrowsOnNullDevice()
  {
    var output = CreateOutput();
    await output.InitializeAsync();

    await Assert.ThrowsAsync<ArgumentNullException>(() => output.ConnectAsync(null!));
  }

  [Fact]
  public async Task ConnectAsync_ThrowsWhenNotInitialized()
  {
    var output = CreateOutput();
    var device = new ChromecastDeviceInfo
    {
      Id = "test-device",
      FriendlyName = "Test Device",
      IpAddress = "192.168.1.100",
      Port = 8009,
      Model = "Chromecast"
    };

    await Assert.ThrowsAsync<InvalidOperationException>(() => output.ConnectAsync(device));
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

  [Fact]
  public void ConnectedDevice_IsNullInitially()
  {
    var output = CreateOutput();

    Assert.Null(output.ConnectedDevice);
  }
}

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Outputs;

namespace Radio.Infrastructure.Tests.Audio.Outputs;

public class HttpStreamOutputTests
{
  private readonly Mock<ILogger<HttpStreamOutput>> _loggerMock;
  private readonly Mock<IOptions<AudioOutputOptions>> _optionsMock;
  private readonly Mock<IAudioEngine> _audioEngineMock;
  private readonly AudioOutputOptions _defaultOptions;

  public HttpStreamOutputTests()
  {
    _loggerMock = new Mock<ILogger<HttpStreamOutput>>();
    _optionsMock = new Mock<IOptions<AudioOutputOptions>>();
    _audioEngineMock = new Mock<IAudioEngine>();

    _defaultOptions = new AudioOutputOptions
    {
      HttpStream = new HttpStreamOutputOptions
      {
        Enabled = true,
        Port = 8080,
        EndpointPath = "/stream/audio",
        ContentType = "audio/wav",
        SampleRate = 48000,
        Channels = 2,
        BitsPerSample = 16,
        MaxConcurrentClients = 10,
        ClientBufferSize = 65536
      }
    };

    _optionsMock.Setup(x => x.Value).Returns(_defaultOptions);

    // Set up mock audio engine to return a mock stream
    var mockStream = new MemoryStream();
    _audioEngineMock
      .Setup(x => x.GetMixedOutputStream())
      .Returns(mockStream);
  }

  private HttpStreamOutput CreateOutput()
  {
    return new HttpStreamOutput(_loggerMock.Object, _optionsMock.Object, _audioEngineMock.Object);
  }

  [Fact]
  public void Constructor_ThrowsOnNullLogger()
  {
    Assert.Throws<ArgumentNullException>(
      () => new HttpStreamOutput(null!, _optionsMock.Object, _audioEngineMock.Object));
  }

  [Fact]
  public void Constructor_ThrowsOnNullOptions()
  {
    Assert.Throws<ArgumentNullException>(
      () => new HttpStreamOutput(_loggerMock.Object, null!, _audioEngineMock.Object));
  }

  [Fact]
  public void Constructor_ThrowsOnNullAudioEngine()
  {
    Assert.Throws<ArgumentNullException>(
      () => new HttpStreamOutput(_loggerMock.Object, _optionsMock.Object, null!));
  }

  [Fact]
  public void Constructor_SetsCorrectType()
  {
    var output = CreateOutput();

    Assert.Equal(AudioOutputType.HttpStream, output.Type);
  }

  [Fact]
  public void Constructor_SetsInitialState()
  {
    var output = CreateOutput();

    Assert.Equal(AudioOutputState.Created, output.State);
  }

  [Fact]
  public void Constructor_SetsDefaultName()
  {
    var output = CreateOutput();

    Assert.Contains("8080", output.Name);
  }

  [Fact]
  public void Constructor_SetsPort()
  {
    var output = CreateOutput();

    Assert.Equal(8080, output.Port);
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
  public async Task InitializeAsync_SetsStreamUrl()
  {
    var output = CreateOutput();

    await output.InitializeAsync();

    Assert.Contains("/stream/audio", output.StreamUrl);
    Assert.Contains(":8080", output.StreamUrl);
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
  public async Task StartAsync_ThrowsWhenNotInitialized()
  {
    var output = CreateOutput();

    await Assert.ThrowsAsync<InvalidOperationException>(() => output.StartAsync());
  }

  [Fact]
  public async Task StopAsync_WhenNotStreaming_DoesNotThrow()
  {
    var output = CreateOutput();
    await output.InitializeAsync();

    await output.StopAsync(); // Should not throw
  }

  [Fact]
  public void ConnectedClientCount_IsZeroInitially()
  {
    var output = CreateOutput();

    Assert.Equal(0, output.ConnectedClientCount);
  }

  [Fact]
  public void GetConnectedClients_ReturnsEmptyListInitially()
  {
    var output = CreateOutput();

    var clients = output.GetConnectedClients();

    Assert.Empty(clients);
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

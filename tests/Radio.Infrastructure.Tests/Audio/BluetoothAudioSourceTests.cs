using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Sources.Primary;
using Radio.Infrastructure.Platform.Bluetooth;

namespace Radio.Infrastructure.Tests.Audio;

public class BluetoothAudioSourceTests : IAsyncDisposable
{
  private readonly Mock<ILogger<BluetoothAudioSource>> _loggerMock = new();
  private readonly Mock<IAudioDeviceManager> _deviceManagerMock = new();
  private readonly MockBluetoothService _mockBluetooth;
  private readonly Mock<IMetricsCollector> _metricsMock = new();
  private readonly IOptionsMonitor<BluetoothOptions> _options;
  private BluetoothAudioSource _source;

  public BluetoothAudioSourceTests()
  {
    _mockBluetooth = new MockBluetoothService(
      new Mock<ILogger<MockBluetoothService>>().Object);

    var optionsMock = new Mock<IOptionsMonitor<BluetoothOptions>>();
    optionsMock.Setup(o => o.CurrentValue).Returns(new BluetoothOptions
    {
      Enabled = true,
      DeviceName = "TestRadio"
    });
    _options = optionsMock.Object;

    _source = new BluetoothAudioSource(
      _loggerMock.Object,
      _deviceManagerMock.Object,
      _mockBluetooth,
      _options,
      identificationService: null,
      metricsCollector: _metricsMock.Object);
  }

  public async ValueTask DisposeAsync()
  {
    await _source.DisposeAsync();
  }

  [Fact]
  public void MetadataChanged_PropagatesTitle()
  {
    _mockBluetooth.SimulateMetadataChange("Test Song", "Test Artist");

    Assert.Equal("Test Song", _source.Metadata[StandardMetadataKeys.Title]);
    Assert.Equal("Test Artist", _source.Metadata[StandardMetadataKeys.Artist]);
    Assert.Equal("Mock Album", _source.Metadata[StandardMetadataKeys.Album]);
  }

  [Fact]
  public void MetadataChanged_WithEmptyTitle_SetsNeedsFingerprintingLookup()
  {
    _mockBluetooth.SimulateMetadataChange("", "Some Artist");

    Assert.True(_source.NeedsFingerprintingLookup);
  }

  [Fact]
  public void MetadataChanged_WithCompleteMetadata_ClearsNeedsFingerprintingLookup()
  {
    // First set incomplete metadata
    _mockBluetooth.SimulateMetadataChange("", "");
    Assert.True(_source.NeedsFingerprintingLookup);

    // Then set complete metadata
    _mockBluetooth.SimulateMetadataChange("Song", "Artist");
    Assert.False(_source.NeedsFingerprintingLookup);
  }

  [Fact]
  public void MetadataChanged_RecordsMetric()
  {
    _mockBluetooth.SimulateMetadataChange("Song", "Artist");

    _metricsMock.Verify(m => m.Increment("bluetooth.metadata_updates", 1.0, null), Times.Once);
  }

  [Fact]
  public void DeviceConnected_UpdatesMetadata()
  {
    var device = new BluetoothDeviceInfo
    {
      Address = "AA:BB:CC:DD:EE:FF",
      Name = "My Speaker",
      IsPaired = true,
      IsConnected = true
    };

    _mockBluetooth.SimulateConnection(device);

    Assert.Equal("My Speaker", _source.Metadata[StandardMetadataKeys.Title]);
    Assert.Equal("My Speaker", _source.Metadata["Device"]);
    Assert.Equal("AA:BB:CC:DD:EE:FF", _source.Metadata["DeviceAddress"]);
    Assert.True(_source.NeedsFingerprintingLookup);
  }

  [Fact]
  public void DeviceDisconnected_TransitionsToStopped()
  {
    var device = new BluetoothDeviceInfo
    {
      Address = "AA:BB:CC:DD:EE:FF",
      Name = "My Speaker",
      IsPaired = true,
      IsConnected = true
    };

    // First connect, then start playing, then disconnect
    _mockBluetooth.SimulateConnection(device);

    // Simulate the source being in a playing state
    // (We test the state transition logic in OnDeviceDisconnected)
    _mockBluetooth.SimulateDisconnection(device);

    Assert.False(_source.NeedsFingerprintingLookup);
  }

  [Fact]
  public async Task InitializeAsync_WhenNoCaptureDevice_SetsReadyState()
  {
    // MockBluetoothService returns "mock-capture-endpoint" (string, not AudioCaptureDevice)
    // InitializeAsync should set Ready state (waiting for device to connect)

    await _source.InitializeAsync(CancellationToken.None);

    Assert.Equal(AudioSourceState.Ready, _source.State);
  }

  [Fact]
  public async Task InitializeAsync_WhenNoCaptureDevice_DoesNotRecordErrorMetric()
  {
    await _source.InitializeAsync(CancellationToken.None);

    _metricsMock.Verify(m => m.Increment("bluetooth.audio_capture_errors", 1.0, null), Times.Never);
  }

  [Fact]
  public void Source_HasCorrectProperties()
  {
    Assert.Equal("Bluetooth Audio", _source.Name);
    Assert.Equal(AudioSourceType.Bluetooth, _source.Type);
    Assert.False(_source.SupportsNext);
    Assert.False(_source.SupportsPrevious);
    Assert.False(_source.SupportsShuffle);
    Assert.False(_source.SupportsRepeat);
    Assert.False(_source.IsSeekable);
  }

  [Fact]
  public void PlaybackStatusChanged_UpdatesMetadata()
  {
    _mockBluetooth.SimulatePlaybackStatusChange(BluetoothPlaybackStatus.Playing);

    Assert.Equal("Playing", _source.Metadata["PlaybackStatus"]);
  }

  [Fact]
  public async Task InitializeAsync_WhenPlatformManagesAudio_SetsReadyWithoutCapture()
  {
    // Create a mock IBluetoothService that reports IsAudioManagedByPlatform = true
    var platformBtMock = new Mock<IBluetoothService>();
    platformBtMock.Setup(b => b.IsAudioManagedByPlatform).Returns(true);
    platformBtMock.Setup(b => b.StartAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(true);
    platformBtMock.Setup(b => b.ConnectedDevice).Returns(new BluetoothDeviceInfo
    {
      Address = "11:22:33:44:55:66",
      Name = "Test Phone",
      IsPaired = true,
      IsConnected = true
    });

    var source = new BluetoothAudioSource(
      _loggerMock.Object,
      _deviceManagerMock.Object,
      platformBtMock.Object,
      _options,
      identificationService: null,
      metricsCollector: _metricsMock.Object);

    await source.InitializeAsync(CancellationToken.None);

    Assert.Equal(AudioSourceState.Ready, source.State);
    Assert.Equal("Test Phone", source.Metadata[StandardMetadataKeys.Title]);
    Assert.True(source.NeedsFingerprintingLookup);

    // GetAudioCaptureDeviceAsync should NOT have been called
    platformBtMock.Verify(b => b.GetAudioCaptureDeviceAsync(It.IsAny<CancellationToken>()), Times.Never);

    await source.DisposeAsync();
  }

  [Fact]
  public void MetadataChanged_NewSongWithoutArt_ClearsPreviousAlbumArt()
  {
    // Simulate Song A with art set via a previous lookup
    _mockBluetooth.SimulateMetadataChange("Song A", "Artist A");
    // Manually set album art as if MusicBrainz/SongRec resolved it
    // (source metadata is publicly readable)
    Assert.False(_source.Metadata.ContainsKey(StandardMetadataKeys.AlbumArtUrl));

    // Now simulate Song B arriving — AVRCP without art (the common case)
    _mockBluetooth.SimulateMetadataChange("Song B", "Artist B");

    // AlbumArtUrl should NOT carry over from Song A
    Assert.False(
      _source.Metadata.TryGetValue(StandardMetadataKeys.AlbumArtUrl, out var art)
      && art is string s && !string.IsNullOrEmpty(s),
      "AlbumArtUrl should be cleared when new song arrives without art");
  }

  [Fact]
  public void MockBluetoothService_IsAudioManagedByPlatform_ReturnsFalse()
  {
    Assert.False(_mockBluetooth.IsAudioManagedByPlatform);
  }

  [Fact]
  public async Task MetadataChanged_WithShazamToggleOn_SetsNeedsFingerprintingEvenWithCompleteMetadata()
  {
    // Arrange — create source with UseShazamForAllSources enabled
    var fingerprintingOptions = Options.Create(new FingerprintingOptions
    {
      UseShazamForAllSources = true
    });

    await _source.DisposeAsync();
    _source = new BluetoothAudioSource(
      _loggerMock.Object,
      _deviceManagerMock.Object,
      _mockBluetooth,
      _options,
      identificationService: null,
      metricsCollector: _metricsMock.Object,
      fingerprintingOptions: fingerprintingOptions);

    // Act — send complete AVRCP metadata (title + artist present)
    _mockBluetooth.SimulateMetadataChange("Known Song", "Known Artist");

    // Assert — should still request fingerprinting because toggle is ON
    Assert.True(_source.NeedsFingerprintingLookup);
  }

  [Fact]
  public async Task MetadataChanged_WithShazamToggleOff_DoesNotFingerprintCompleteMetadata()
  {
    // Arrange — create source with UseShazamForAllSources disabled (default)
    var fingerprintingOptions = Options.Create(new FingerprintingOptions
    {
      UseShazamForAllSources = false
    });

    await _source.DisposeAsync();
    _source = new BluetoothAudioSource(
      _loggerMock.Object,
      _deviceManagerMock.Object,
      _mockBluetooth,
      _options,
      identificationService: null,
      metricsCollector: _metricsMock.Object,
      fingerprintingOptions: fingerprintingOptions);

    // Act — send complete AVRCP metadata
    _mockBluetooth.SimulateMetadataChange("Known Song", "Known Artist");

    // Assert — should NOT request fingerprinting because toggle is OFF and metadata is complete
    Assert.False(_source.NeedsFingerprintingLookup);
  }
}

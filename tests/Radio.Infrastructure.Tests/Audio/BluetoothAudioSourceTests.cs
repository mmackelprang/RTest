using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Events;
using Radio.Core.Interfaces;
using Radio.Fingerprinting;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Fingerprinting.Services;
using Radio.Infrastructure.Audio.Sources.Primary;
using Radio.Infrastructure.Platform.Bluetooth;
using Radio.Metrics;

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
    var fpMonitor = new Mock<IOptionsMonitor<FingerprintingOptions>>();
    fpMonitor.Setup(o => o.CurrentValue).Returns(new FingerprintingOptions
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
      fingerprintingOptions: fpMonitor.Object);

    // Act — send complete AVRCP metadata (title + artist present)
    _mockBluetooth.SimulateMetadataChange("Known Song", "Known Artist");

    // Assert — should still request fingerprinting because toggle is ON
    Assert.True(_source.NeedsFingerprintingLookup);
  }

  [Fact]
  public async Task MetadataChanged_WithShazamToggleOff_DoesNotFingerprintCompleteMetadata()
  {
    // Arrange — create source with UseShazamForAllSources disabled (default)
    var fpMonitor = new Mock<IOptionsMonitor<FingerprintingOptions>>();
    fpMonitor.Setup(o => o.CurrentValue).Returns(new FingerprintingOptions
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
      fingerprintingOptions: fpMonitor.Object);

    // Act — send complete AVRCP metadata
    _mockBluetooth.SimulateMetadataChange("Known Song", "Known Artist");

    // Assert — should NOT request fingerprinting because toggle is OFF and metadata is complete
    Assert.False(_source.NeedsFingerprintingLookup);
  }

  // -----------------------------------------------------------------------
  // BT album-art tests — verify AVRCP fast path + SongRec fallback routing.
  //
  // Bug A regression: file:// AVRCP URLs (the common Spotify/YouTube Music
  // case on Android) must not be propagated raw to the browser. The fix
  // routes every AVRCP ArtUrl through AlbumArtCacheService.SaveFromUrlAsync,
  // which returns null for file:// (HttpClient throws NotSupportedException,
  // caught internally) and a /api/albumart/{hash}.{ext} URL for http(s)://.
  // -----------------------------------------------------------------------

  /// <summary>
  /// Minimal IServiceScopeFactory for tests: the scope it produces has no
  /// IPlayHistoryRepository / ITrackMetadataRepository registered, so
  /// UpdateRecentPlayHistoryCoverArtAsync no-ops cleanly via its null guards.
  /// </summary>
  private static IServiceScopeFactory BuildScopeFactory()
  {
    var services = new ServiceCollection();
    return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
  }

  /// <summary>
  /// Builds a real BackgroundIdentificationService (no SongRec, no audio
  /// capture) suitable for raising TrackIdentified via the internal
  /// RaiseTrackIdentifiedForTesting hook.
  /// </summary>
  private static BackgroundIdentificationService BuildIdentificationServiceForTests()
  {
    var services = new ServiceCollection();
    var sp = services.BuildServiceProvider();
    var optionsMonitor = new Mock<IOptionsMonitor<FingerprintingOptions>>();
    optionsMonitor.Setup(o => o.CurrentValue).Returns(new FingerprintingOptions());
    var logger = new Mock<ILogger<BackgroundIdentificationService>>().Object;
    return new BackgroundIdentificationService(logger, sp, optionsMonitor.Object);
  }

  [Fact]
  public async Task MetadataChanged_WithFileSchemeArtUrl_DoesNotStoreRawUrlInMetadata()
  {
    // Arrange — cache mock returns null for file:// (simulating HttpClient
    // NotSupportedException caught inside SaveFromUrlAsync).
    var cacheMock = new Mock<IAlbumArtCacheService>();
    cacheMock
      .Setup(c => c.SaveFromUrlAsync(It.Is<string>(u => u.StartsWith("file://"))))
      .ReturnsAsync((string?)null);

    await _source.DisposeAsync();
    _source = new BluetoothAudioSource(
      _loggerMock.Object,
      _deviceManagerMock.Object,
      _mockBluetooth,
      _options,
      identificationService: null,
      metricsCollector: _metricsMock.Object,
      serviceScopeFactory: BuildScopeFactory(),
      albumArtCache: cacheMock.Object);

    // Act — simulate AVRCP metadata with a phone-local file:// URI (the
    // common case from Spotify/YouTube Music on Android — Track.ArtUrl
    // points to the phone's app cache directory, unreachable from the browser).
    _mockBluetooth.SimulateMetadataChange(
      "Song", "Artist",
      albumArtUrl: "file:///data/data/com.android.spotify/cache/art.jpg");

    // Allow the fire-and-forget CacheAvrcpArtAsync task to complete.
    await Task.Delay(200);

    // Assert — AlbumArtUrl must NOT be set to the raw file:// URL. It must
    // be either absent or empty (UI then falls back to the default-art icon).
    var hasArt = _source.Metadata.TryGetValue(StandardMetadataKeys.AlbumArtUrl, out var art);
    Assert.False(
      hasArt && art is string s && s.StartsWith("file://"),
      "Raw file:// AVRCP URL must not be propagated to metadata");

    // And the cache was invoked exactly once for the file:// URL — we route
    // every URL through the cache (rather than scheme-filtering up front) so
    // future schemes (data:, embedded http://localhost servers) work
    // automatically once the cache learns to handle them.
    cacheMock.Verify(c => c.SaveFromUrlAsync(It.Is<string>(u => u.StartsWith("file://"))), Times.Once);
  }

  [Fact]
  public async Task MetadataChanged_WithHttpsArtUrl_StoresCachedRelativeUrl()
  {
    // Arrange — cache mock returns a cached /api/albumart URL.
    var cacheMock = new Mock<IAlbumArtCacheService>();
    cacheMock
      .Setup(c => c.SaveFromUrlAsync("https://example.com/art.jpg"))
      .ReturnsAsync("/api/albumart/abc123.jpg");

    await _source.DisposeAsync();
    _source = new BluetoothAudioSource(
      _loggerMock.Object,
      _deviceManagerMock.Object,
      _mockBluetooth,
      _options,
      identificationService: null,
      metricsCollector: _metricsMock.Object,
      serviceScopeFactory: BuildScopeFactory(),
      albumArtCache: cacheMock.Object);

    // Act — AVRCP metadata with an https:// art URL (rare from phones, common
    // from local-music players that expose art via MPRIS).
    _mockBluetooth.SimulateMetadataChange(
      "Song", "Artist",
      albumArtUrl: "https://example.com/art.jpg");
    await Task.Delay(200);  // let the fire-and-forget CacheAvrcpArtAsync complete

    // Assert — metadata holds the cache's relative URL (browser-fetchable).
    Assert.True(_source.Metadata.TryGetValue(StandardMetadataKeys.AlbumArtUrl, out var art));
    Assert.Equal("/api/albumart/abc123.jpg", art);
    cacheMock.Verify(c => c.SaveFromUrlAsync("https://example.com/art.jpg"), Times.Once);
  }

  [Fact]
  public async Task TrackIdentified_AfterEmptyAvrcp_CachesSongRecCoverArtUrl()
  {
    // Arrange — cache mock returns a /api/albumart URL when called with the
    // SongRec CDN URL. SongRec provides Apple Music CDN URLs which are HTTPS.
    var cacheMock = new Mock<IAlbumArtCacheService>();
    cacheMock
      .Setup(c => c.SaveFromUrlAsync("https://itunes.apple.com/some-art.jpg"))
      .ReturnsAsync("/api/albumart/songrec-abc.jpg");

    var identificationService = BuildIdentificationServiceForTests();

    await _source.DisposeAsync();
    _source = new BluetoothAudioSource(
      _loggerMock.Object,
      _deviceManagerMock.Object,
      _mockBluetooth,
      _options,
      identificationService: identificationService,
      metricsCollector: _metricsMock.Object,
      serviceScopeFactory: BuildScopeFactory(),
      albumArtCache: cacheMock.Object);

    // AVRCP delivers only Title (Spotify/YouTube-style: Artist empty) — this
    // sets NeedsFingerprintingLookup = true and the SongRec path engages.
    _mockBluetooth.SimulateMetadataChange("Some Song", "", albumArtUrl: null);
    Assert.True(_source.NeedsFingerprintingLookup);

    // Act — SongRec identifies the track later.
    identificationService.RaiseTrackIdentifiedForTesting(new TrackIdentifiedEventArgs(
      new TrackMetadata
      {
        Id = Guid.NewGuid().ToString(),
        Title = "Some Song",
        Artist = "Real Artist",
        CoverArtUrl = "https://itunes.apple.com/some-art.jpg",
        Source = MetadataSource.Shazam,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
      },
      confidence: 0.95));
    await Task.Delay(200);

    // Assert — metadata holds the cached SongRec art URL.
    Assert.True(_source.Metadata.TryGetValue(StandardMetadataKeys.AlbumArtUrl, out var art));
    Assert.Equal("/api/albumart/songrec-abc.jpg", art);
  }

  [Fact]
  public async Task TrackIdentified_WithNoCoverArtUrl_LeavesAlbumArtUrlAbsent()
  {
    // Arrange — cache mock should NEVER be called (no URL to download).
    var cacheMock = new Mock<IAlbumArtCacheService>(MockBehavior.Strict);

    var identificationService = BuildIdentificationServiceForTests();

    await _source.DisposeAsync();
    _source = new BluetoothAudioSource(
      _loggerMock.Object,
      _deviceManagerMock.Object,
      _mockBluetooth,
      _options,
      identificationService: identificationService,
      metricsCollector: _metricsMock.Object,
      serviceScopeFactory: BuildScopeFactory(),
      albumArtCache: cacheMock.Object);

    _mockBluetooth.SimulateMetadataChange("Mystery Song", "", albumArtUrl: null);

    // Act — SongRec identifies but has no cover art (track not on Apple Music
    // CDN, or SongRec returned partial metadata).
    identificationService.RaiseTrackIdentifiedForTesting(new TrackIdentifiedEventArgs(
      new TrackMetadata
      {
        Id = Guid.NewGuid().ToString(),
        Title = "Mystery Song",
        Artist = "Mystery Artist",
        CoverArtUrl = null,
        Source = MetadataSource.Shazam,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
      },
      confidence: 0.7));
    await Task.Delay(200);

    // Assert — AlbumArtUrl must be absent (UI shows fallback icon — accepted UX).
    var hasArt = _source.Metadata.TryGetValue(StandardMetadataKeys.AlbumArtUrl, out var art);
    Assert.False(
      hasArt && art is string s && !string.IsNullOrEmpty(s),
      "AlbumArtUrl must remain absent when neither AVRCP nor SongRec provides art");

    // And the cache was never touched (MockBehavior.Strict throws on any unset call).
    cacheMock.VerifyNoOtherCalls();
  }
}

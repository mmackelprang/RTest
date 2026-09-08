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
using Radio.Infrastructure.Audio.Fingerprinting;
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
  public void CaptureStreamRecovered_AfterDisconnect_ReenablesFingerprintingLookup()
  {
    // Regression for: after a BT capture pipeline recovery event, SongRec stops being
    // triggered for new tracks indefinitely (no album art). Root cause: OnDeviceDisconnected
    // clears NeedsFingerprintingLookup, and OnCaptureStreamRecovered did not re-set it —
    // so BackgroundIdentificationService's gate stayed false until service restart.
    var device = new BluetoothDeviceInfo
    {
      Address = "AA:BB:CC:DD:EE:FF",
      Name = "My Speaker",
      IsPaired = true,
      IsConnected = true
    };

    _mockBluetooth.SimulateConnection(device);
    Assert.True(_source.NeedsFingerprintingLookup);

    _mockBluetooth.SimulateDisconnection(device);
    Assert.False(_source.NeedsFingerprintingLookup);

    // Pipeline monitor reports recovery — capture stream is re-established.
    _mockBluetooth.SimulateCaptureStreamRecovered();

    Assert.True(_source.NeedsFingerprintingLookup);
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

  // -----------------------------------------------------------------------
  // Resolved-art persistence tests — verify that BT album art survives AVRCP
  // metadata refreshes, track repeats, and the SongRec re-identification
  // window instead of being cleared on every metadata event.
  //
  // BackgroundIdentificationService pre-caches SongRec art to a local
  // /api/albumart/... path before raising TrackIdentified, so the value BT
  // receives is ALREADY browser-fetchable. Re-running SaveFromUrlAsync on it
  // is a no-op that returns null (cache HttpClient has no BaseAddress), so the
  // fix skips the download for non-remote URLs (parity with FilePlayer/SDR).
  // -----------------------------------------------------------------------

  [Fact]
  public async Task TrackIdentified_WithLocalArtPath_DoesNotReDownload()
  {
    // Strict mock — any SaveFromUrlAsync call would throw. The SongRec art is
    // already a local /api/albumart path, so no re-download must occur.
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

    _mockBluetooth.SimulateMetadataChange("Song", "Artist", albumArtUrl: null);

    // SongRec identifies the track and hands BT an already-local art path
    // (BackgroundIdentificationService pre-cached it before raising the event).
    identificationService.RaiseTrackIdentifiedForTesting(new TrackIdentifiedEventArgs(
      new TrackMetadata
      {
        Id = Guid.NewGuid().ToString(),
        Title = "Song",
        Artist = "Artist",
        CoverArtUrl = "/api/albumart/local-abc.jpg",
        Source = MetadataSource.Shazam,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
      },
      confidence: 0.95));
    await Task.Delay(200);

    // Metadata holds the local path verbatim — no re-download rewrote it.
    Assert.True(_source.Metadata.TryGetValue(StandardMetadataKeys.AlbumArtUrl, out var art));
    Assert.Equal("/api/albumart/local-abc.jpg", art);

    // Proves the redundant SaveFromUrlAsync download was skipped (strict mock
    // throws on any unset call).
    cacheMock.VerifyNoOtherCalls();
  }

  [Fact]
  public async Task MetadataRefresh_SameTrack_RestoresResolvedArt()
  {
    // Core regression: a plain AVRCP metadata refresh (no art) for the SAME
    // track that SongRec already resolved must NOT blank out the display.
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

    // Song A arrives (no AVRCP art), then SongRec resolves its art.
    _mockBluetooth.SimulateMetadataChange("Song A", "Artist A", albumArtUrl: null);
    identificationService.RaiseTrackIdentifiedForTesting(new TrackIdentifiedEventArgs(
      new TrackMetadata
      {
        Id = Guid.NewGuid().ToString(),
        Title = "Song A",
        Artist = "Artist A",
        CoverArtUrl = "/api/albumart/a.jpg",
        Source = MetadataSource.Shazam,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
      },
      confidence: 0.95));
    await Task.Delay(200);
    Assert.True(_source.Metadata.TryGetValue(StandardMetadataKeys.AlbumArtUrl, out var art1));
    Assert.Equal("/api/albumart/a.jpg", art1);

    // AVRCP fires another metadata update for the SAME track with no art
    // (position tick, player re-announce, etc.). The art must be restored
    // from the per-track cache rather than blanked.
    _mockBluetooth.SimulateMetadataChange("Song A", "Artist A", albumArtUrl: null);

    Assert.True(_source.Metadata.TryGetValue(StandardMetadataKeys.AlbumArtUrl, out var art2));
    Assert.Equal("/api/albumart/a.jpg", art2);
  }

  [Fact]
  public async Task RepeatedTrack_RestoresResolvedArtFromCache()
  {
    // A re-selected/repeated track (duplicate-suppressed by
    // BackgroundIdentificationService, so no new TrackIdentified fires) must
    // still show art — restored from the per-track cache.
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

    // Song A resolves its art.
    _mockBluetooth.SimulateMetadataChange("Song A", "Artist A", albumArtUrl: null);
    identificationService.RaiseTrackIdentifiedForTesting(new TrackIdentifiedEventArgs(
      new TrackMetadata
      {
        Id = Guid.NewGuid().ToString(),
        Title = "Song A",
        Artist = "Artist A",
        CoverArtUrl = "/api/albumart/a.jpg",
        Source = MetadataSource.Shazam,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
      },
      confidence: 0.95));
    await Task.Delay(200);
    Assert.Equal("/api/albumart/a.jpg", _source.Metadata[StandardMetadataKeys.AlbumArtUrl]);

    // Switch to Song B — never resolved, so art is absent.
    _mockBluetooth.SimulateMetadataChange("Song B", "Artist B", albumArtUrl: null);
    var hasBArt = _source.Metadata.TryGetValue(StandardMetadataKeys.AlbumArtUrl, out var bArt);
    Assert.False(
      hasBArt && bArt is string bs && !string.IsNullOrEmpty(bs),
      "Song B never resolved art, so none should be present");

    // Return to Song A (repeat) — no new TrackIdentified fires, but the cache
    // restores the previously-resolved art.
    _mockBluetooth.SimulateMetadataChange("Song A", "Artist A", albumArtUrl: null);
    Assert.True(_source.Metadata.TryGetValue(StandardMetadataKeys.AlbumArtUrl, out var artBack));
    Assert.Equal("/api/albumart/a.jpg", artBack);
  }

  [Fact]
  public async Task TrackChange_DoesNotLeakPreviousArtOntoNewTrack()
  {
    // Generation guard: switching to a new, unresolved track must clear art —
    // the previous song's art must not leak onto it.
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

    // Song A resolves its art.
    _mockBluetooth.SimulateMetadataChange("Song A", "Artist A", albumArtUrl: null);
    identificationService.RaiseTrackIdentifiedForTesting(new TrackIdentifiedEventArgs(
      new TrackMetadata
      {
        Id = Guid.NewGuid().ToString(),
        Title = "Song A",
        Artist = "Artist A",
        CoverArtUrl = "/api/albumart/a.jpg",
        Source = MetadataSource.Shazam,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
      },
      confidence: 0.95));
    await Task.Delay(200);
    Assert.Equal("/api/albumart/a.jpg", _source.Metadata[StandardMetadataKeys.AlbumArtUrl]);

    // Switch to Song B (no art, never cached). The display for Song B must
    // show no art — Song A's art must not carry over.
    _mockBluetooth.SimulateMetadataChange("Song B", "Artist B", albumArtUrl: null);
    await Task.Delay(50);

    var hasArt = _source.Metadata.TryGetValue(StandardMetadataKeys.AlbumArtUrl, out var art);
    Assert.False(
      hasArt && art is string s && !string.IsNullOrEmpty(s),
      "Song A's art must not leak onto the newly-selected Song B");
  }

  [Fact]
  public async Task PendingArtDownload_CompletingAfterTrackChange_DoesNotLeakButIsCached()
  {
    // The generation guard must stop an art download that was kicked off for
    // Song A but only COMPLETES after the user has already switched to Song B
    // from writing Song A's art onto Song B's live metadata — while still
    // caching it so a later return to Song A restores it. Unlike the other
    // tests, this drives a genuine in-flight race (the download is still
    // pending when the track changes), so it fails if the _trackGeneration
    // guard is removed rather than just exercising the synchronous clear.
    var tcs = new TaskCompletionSource<string?>();
    var cacheMock = new Mock<IAlbumArtCacheService>();
    cacheMock
      .Setup(c => c.SaveFromUrlAsync("https://cdn.example.com/a.jpg"))
      .Returns(tcs.Task);

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

    // Song A arrives, SongRec hands back a REMOTE art URL — the download
    // (SaveFromUrlAsync) is now pending on the TCS and has NOT completed.
    _mockBluetooth.SimulateMetadataChange("Song A", "Artist A", albumArtUrl: null);
    identificationService.RaiseTrackIdentifiedForTesting(new TrackIdentifiedEventArgs(
      new TrackMetadata
      {
        Id = Guid.NewGuid().ToString(),
        Title = "Song A",
        Artist = "Artist A",
        CoverArtUrl = "https://cdn.example.com/a.jpg",
        Source = MetadataSource.Shazam,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
      },
      confidence: 0.95));

    // While the download is still in flight, the user switches to Song B.
    _mockBluetooth.SimulateMetadataChange("Song B", "Artist B", albumArtUrl: null);

    // NOW the Song A download completes.
    tcs.SetResult("/api/albumart/a.jpg");
    await Task.Delay(200); // let the awaiting continuation run

    // Guard held: Song A's late art must NOT have leaked onto Song B.
    var hasBArt = _source.Metadata.TryGetValue(StandardMetadataKeys.AlbumArtUrl, out var bArt);
    Assert.False(
      hasBArt && bArt is string bs && !string.IsNullOrEmpty(bs),
      "A late-completing download for the previous track must not write onto the new track");

    // But the art WAS cached — returning to Song A restores it without a new identification.
    _mockBluetooth.SimulateMetadataChange("Song A", "Artist A", albumArtUrl: null);
    Assert.True(_source.Metadata.TryGetValue(StandardMetadataKeys.AlbumArtUrl, out var artBack));
    Assert.Equal("/api/albumart/a.jpg", artBack);
  }

  // -----------------------------------------------------------------------
  // Cross-source contamination tests — BackgroundIdentificationService
  // broadcasts TrackIdentified to EVERY subscriber, not just the source the
  // fingerprinted audio came from. Production regression: while SDR Radio was
  // active, BT overwrote its own AVRCP Title/Artist/Album with the radio's
  // track, so switching to BT showed a song that had played on the radio.
  // -----------------------------------------------------------------------

  [Fact]
  public async Task TrackIdentified_WhileDifferentSourceIsActive_DoesNotOverwriteAvrcpMetadata()
  {
    // Arrange — UseShazamForAllSources ON is the exact production configuration:
    // SongRec metadata unconditionally replaces AVRCP metadata in the handler.
    var fpMonitor = new Mock<IOptionsMonitor<FingerprintingOptions>>();
    fpMonitor.Setup(o => o.CurrentValue).Returns(new FingerprintingOptions
    {
      UseShazamForAllSources = true
    });

    var identificationService = BuildIdentificationServiceForTests();

    // A DIFFERENT source is the audio manager's active source — the radio.
    var activeSource = new Mock<IAudioSource>().Object;

    await _source.DisposeAsync();
    _source = new BluetoothAudioSource(
      _loggerMock.Object,
      _deviceManagerMock.Object,
      _mockBluetooth,
      _options,
      identificationService: identificationService,
      metricsCollector: _metricsMock.Object,
      fingerprintingOptions: fpMonitor.Object,
      getActiveSource: () => activeSource);

    // The phone's AVRCP metadata is what BT must keep showing.
    _mockBluetooth.SimulateMetadataChange("Enter Sandman", "Metallica");
    Assert.Equal("Enter Sandman", _source.Metadata[StandardMetadataKeys.Title]);
    Assert.Equal("Metallica", _source.Metadata[StandardMetadataKeys.Artist]);

    // Act — the radio's audio gets fingerprinted and the event is broadcast to
    // every subscriber, BT included.
    identificationService.RaiseTrackIdentifiedForTesting(new TrackIdentifiedEventArgs(
      new TrackMetadata
      {
        Id = Guid.NewGuid().ToString(),
        Title = "Radio Song",
        Artist = "Radio Artist",
        Album = "Radio Album",
        Source = MetadataSource.Shazam,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
      },
      confidence: 0.95));
    await Task.Delay(100);

    // Assert — BT kept its own AVRCP metadata; the radio's track did not leak in.
    Assert.Equal("Enter Sandman", _source.Metadata[StandardMetadataKeys.Title]);
    Assert.Equal("Metallica", _source.Metadata[StandardMetadataKeys.Artist]);
  }

  [Fact]
  public async Task TrackIdentified_WhileThisSourceIsActive_StillUpdatesMetadata()
  {
    // The guard must not break the normal path: when BT *is* the active source,
    // SongRec metadata still replaces AVRCP metadata as before.
    var fpMonitor = new Mock<IOptionsMonitor<FingerprintingOptions>>();
    fpMonitor.Setup(o => o.CurrentValue).Returns(new FingerprintingOptions
    {
      UseShazamForAllSources = true
    });

    var identificationService = BuildIdentificationServiceForTests();

    await _source.DisposeAsync();
    BluetoothAudioSource? active = null;
    active = new BluetoothAudioSource(
      _loggerMock.Object,
      _deviceManagerMock.Object,
      _mockBluetooth,
      _options,
      identificationService: identificationService,
      metricsCollector: _metricsMock.Object,
      fingerprintingOptions: fpMonitor.Object,
      getActiveSource: () => active);
    _source = active;

    _mockBluetooth.SimulateMetadataChange("Enter Sandman", "Metallica");

    // Act — BT's own audio is fingerprinted.
    identificationService.RaiseTrackIdentifiedForTesting(new TrackIdentifiedEventArgs(
      new TrackMetadata
      {
        Id = Guid.NewGuid().ToString(),
        Title = "Shazam Title",
        Artist = "Shazam Artist",
        Album = "Shazam Album",
        Source = MetadataSource.Shazam,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
      },
      confidence: 0.95));
    await Task.Delay(100);

    // Assert — the more authoritative SongRec metadata was adopted.
    Assert.Equal("Shazam Title", _source.Metadata[StandardMetadataKeys.Title]);
    Assert.Equal("Shazam Artist", _source.Metadata[StandardMetadataKeys.Artist]);
    Assert.Equal("Shazam Album", _source.Metadata[StandardMetadataKeys.Album]);
  }

  // -----------------------------------------------------------------------
  // Branch-dispatch coverage (TEST-2). These enter through the real event path
  // — DeviceConnected -> OnDeviceConnected (:361) -> TryAcquireAudioCaptureAsync
  // (:370) -> the real `capture is ...` arms at :486 / :497 — rather than by
  // calling ApplyDeferredCaptureState directly. That distinction is the point of
  // the row: entering at the method executes the state decision without executing
  // the dispatch, which reads as coverage and is not. design/TESTING.md § Test Seams.
  //
  // RouteCaptureThroughMixerAsync (:524) is reached and no-ops because
  // _playbackService is null (an optional ctor parameter this fixture does not
  // pass), so both of its arms fall through (:538, :579). That is load-bearing: if
  // a future change makes routing unconditional, these break loudly rather than
  // silently stopping short of the branch. The DISPATCH therefore calls nothing on
  // the capture mock — but the mock is not untouched: `await using` runs
  // DisposeAsyncCore (:291-296), which unsubscribes OnAudioProcessed and Disposes an
  // AudioCaptureDevice, and _WhilePlaying_ calls Stop() on one deliberately (below).
  //
  // ⚠ The arms are told apart by GetSoundComponent(), not by state alone. Only
  // the :497 arm assigns SoundComponent, so :486 leaves GetSoundComponent()
  // throwing and :497 leaves it returning the exact object the mock handed over.
  // State alone would not distinguish the two arms from each other, and would not
  // distinguish either from the `else` at :508 landing the source in Ready by a
  // different route — which is precisely the vacuous shape this row exists to
  // prevent.
  //
  // ⚠⚠ GetSoundComponent() is a COMPLETE discriminator for :497 and only HALF a
  // discriminator for :486, and the difference is what this row is about. For :497,
  // the exact object coming back is conclusive — no other path could produce it. For
  // :486, a throwing GetSoundComponent() rules out :497 but is equally true of the
  // `else` at :508, which assigns nothing at all. So ruling out the `else` for the
  // AudioCaptureDevice case is each test's own job, and each does it differently:
  // _AndLandsReady by the Ready state (the `else` never calls
  // ApplyDeferredCaptureState, so a source that entered Created would still be
  // Created), and _WhilePlaying_ — where the state is Playing before and after
  // either way — by pausing and verifying Stop() reached the assigned _captureDevice.
  // Only with both halves does every assertion below pin WHICH arm ran.
  //
  // These sources are built with a Mock<IBluetoothService> rather than the
  // fixture's MockBluetoothService, whose GetAudioCaptureDeviceAsync returns a
  // bare string and so can only ever reach the `else`. They are NOT the fixture's
  // _source and DisposeAsync does not cover them — each test disposes its own.
  // -----------------------------------------------------------------------

  /// <summary>
  /// Counts calls into <c>GetAudioCaptureDeviceAsync</c> so a test can synchronize on
  /// the acquisition having been observed rather than on elapsed time, and so the
  /// first (pre-connect) acquisition can be told from the deferred one.
  /// </summary>
  private sealed class CaptureAcquisitionProbe
  {
    private int _calls;
    public int Calls => Volatile.Read(ref _calls);
    /// <summary>Records a call and returns its 1-based ordinal.</summary>
    public int Record() => Interlocked.Increment(ref _calls);
  }

  /// <summary>
  /// Builds a Bluetooth service mock whose capture acquisition returns
  /// <paramref name="capture"/>.
  /// <para>
  /// The first <paramref name="nullAcquisitions"/> calls answer null instead. That is not
  /// a convenience — it is what makes the "already Playing" test mean anything.
  /// <c>PlayAsync</c> on a Created source runs <c>InitializeAsync</c>, which consumes the
  /// capture through the OTHER dispatch at :159/:166 and assigns
  /// <c>_captureDevice</c>/<c>SoundComponent</c>; a <c>DeviceConnected</c> raised after
  /// that returns at the "already acquired" guard (:479) and never reaches :486/:497.
  /// Answering null first leaves the source Playing with no capture — the real
  /// scenario #469 is about, a source activated before the phone's A2DP stream existed.
  /// </para>
  /// <para>
  /// ⚠ Two, not one. <c>PlayAsync</c> acquires TWICE on a Created source:
  /// <c>AudioSourceBase.PlayAsync</c> calls <c>InitializeAsync</c> because the state is
  /// Created (<c>:84-87</c>), and <c>PlayCoreAsync</c> then calls it AGAIN because no
  /// capture was established (<c>BluetoothAudioSource.cs:206-209</c>). Deferring only the
  /// first leaves the second one acquiring, and the test silently degrades back into the
  /// vacuous shape described above. Measured, not assumed — the count is asserted below.
  /// </para>
  /// </summary>
  private static Mock<IBluetoothService> BuildBtMock(
    object? capture,
    CaptureAcquisitionProbe probe,
    int nullAcquisitions = 0,
    bool platformManaged = false)
  {
    var btMock = new Mock<IBluetoothService>();
    btMock.Setup(b => b.IsAudioManagedByPlatform).Returns(platformManaged);
    btMock.Setup(b => b.StartAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(true);
    btMock.Setup(b => b.GetAudioCaptureDeviceAsync(It.IsAny<CancellationToken>()))
      .Returns(() =>
      {
        var ordinal = probe.Record();
        return Task.FromResult(ordinal <= nullAcquisitions ? null : capture);
      });
    btMock.Setup(b => b.ConnectedDevice).Returns(new BluetoothDeviceInfo
    {
      Address = "AA:BB:CC:DD:EE:FF",
      Name = "Test Phone",
      IsPaired = true,
      IsConnected = true
    });
    return btMock;
  }

  private BluetoothAudioSource BuildSource(Mock<IBluetoothService> btMock) =>
    new(_loggerMock.Object,
        _deviceManagerMock.Object,
        btMock.Object,
        _options,
        identificationService: null,
        metricsCollector: _metricsMock.Object);

  private static void RaiseDeviceConnected(Mock<IBluetoothService> btMock) =>
    btMock.Raise(
      b => b.DeviceConnected += null,
      new BluetoothDeviceConnectedEventArgs { Device = btMock.Object.ConnectedDevice! });

  private static object NewCaptureDeviceMock() =>
    new Mock<global::SoundFlow.Abstracts.Devices.AudioCaptureDevice>(
      MockBehavior.Loose, null!, default(global::SoundFlow.Structs.AudioFormat), null!).Object;

  private static object NewSoundComponentMock() =>
    new Mock<global::SoundFlow.Abstracts.SoundComponent>(
      MockBehavior.Loose, null!, default(global::SoundFlow.Structs.AudioFormat)).Object;

  public static TheoryData<string> CaptureKinds => new() { "AudioCaptureDevice", "SoundComponent" };

  private static object NewCaptureMock(string kind) =>
    kind == "AudioCaptureDevice" ? NewCaptureDeviceMock() : NewSoundComponentMock();

  /// <summary>
  /// Asserts that the arm matching <paramref name="kind"/> ran and the OTHER arm did not.
  /// Only :497 assigns SoundComponent, so GetSoundComponent() is the discriminator between
  /// the two arms.
  /// </summary>
  /// <remarks>
  /// ⚠ <b>It does not, by itself, rule out the <c>else</c> at :508 for the
  /// <c>AudioCaptureDevice</c> case</b> — the <c>else</c> assigns nothing, so
  /// <c>GetSoundComponent()</c> throws there too, and this helper cannot tell the two apart.
  /// Callers must exclude the <c>else</c> themselves; see the block comment above these tests
  /// for how each one does it. For the <c>SoundComponent</c> case the helper IS conclusive,
  /// because no other path hands back that exact object.
  /// </remarks>
  private static void AssertArmTaken(string kind, BluetoothAudioSource source, object capture)
  {
    if (kind == "AudioCaptureDevice")
    {
      var ex = Assert.Throws<InvalidOperationException>(() => source.GetSoundComponent());
      Assert.Contains("not initialized", ex.Message);
    }
    else
    {
      Assert.Same(capture, source.GetSoundComponent());
    }
  }

  [Theory]
  [MemberData(nameof(CaptureKinds))]
  public async Task DeviceConnectedEvent_TakesTheMatchingBranch_AndLandsReady(string kind)
  {
    var capture = NewCaptureMock(kind);
    var probe = new CaptureAcquisitionProbe();
    var btMock = BuildBtMock(capture, probe);
    await using var source = BuildSource(btMock);

    // Not played, so ApplyDeferredCaptureState — reached at :489 or :500 from its REAL
    // call site inside the arm under test — must land the source in Ready. Created is
    // the pre-state, so a Ready here cannot have come from InitializeAsync.
    Assert.Equal(AudioSourceState.Created, source.State);

    RaiseDeviceConnected(btMock);

    // TryAcquireAudioCaptureAsync is fire-and-forget from :370, so synchronize on the
    // observation rather than on elapsed time (CLAUDE.md § Test Timing).
    await WaitForAsync(() => source.State == AudioSourceState.Ready);

    Assert.Equal(AudioSourceState.Ready, source.State);
    Assert.Equal(1, probe.Calls);
    AssertArmTaken(kind, source, capture);
  }

  [Theory]
  [MemberData(nameof(CaptureKinds))]
  public async Task DeviceConnectedEvent_WhilePlaying_TakesTheBranchAndStaysPlaying(string kind)
  {
    // The #469 invariant, driven through the real dispatch instead of the seam: a source
    // already Playing must survive deferred acquisition, because SoundFlowAudioTap.IsActive
    // gates fingerprinting on Playing and a demotion silently kills song recognition.
    var capture = NewCaptureMock(kind);
    var probe = new CaptureAcquisitionProbe();
    var btMock = BuildBtMock(capture, probe, nullAcquisitions: 2);
    await using var source = BuildSource(btMock);

    // Both of PlayAsync's acquisitions answer null, so PlayCoreAsync takes the production
    // "no capture device yet, starting background retry" path (:220) and the source
    // reaches Playing with nothing acquired — the state the deferred arm exists for.
    await source.PlayAsync(CancellationToken.None);
    Assert.Equal(AudioSourceState.Playing, source.State);
    Assert.Equal(2, probe.Calls);
    Assert.Throws<InvalidOperationException>(() => source.GetSoundComponent());

    RaiseDeviceConnected(btMock);

    await WaitForAsync(() => probe.Calls >= 3);

    Assert.Equal(AudioSourceState.Playing, source.State);
    AssertArmTaken(kind, source, capture);

    // ⚠ For "AudioCaptureDevice" this step is the ONLY thing that makes the case mean
    // anything, and without it the test's name is a false claim. Every other assertion here
    // survives the :486 arm being dead: the state is Playing before and after, and
    // AssertArmTaken's ACD branch only asserts GetSoundComponent() throws — equally true of
    // the `else` at :508. Measured, not assumed: with :486 disabled this case still passed.
    //
    // Only :486 assigns _captureDevice, and PauseCoreAsync (:228) is the one place that
    // observably touches it. PauseAsync (PrimaryAudioSourceBase.cs:109) requires Playing —
    // asserted immediately above — and then calls _captureDevice?.Stop(). Times.Once, not
    // AtLeastOnce, because nothing else in this test can call Stop(): routing no-ops with a
    // null _playbackService, so neither :560's Start() nor any paired Stop() happens.
    if (kind == "AudioCaptureDevice")
    {
      await source.PauseAsync(CancellationToken.None);
      Mock.Get((global::SoundFlow.Abstracts.Devices.AudioCaptureDevice)capture)
        .Verify(d => d.Stop(), Times.Once);
    }
  }

  [Fact]
  public async Task DeviceConnectedEvent_WhenPlatformManaged_LandsReadyWithoutAcquiring()
  {
    // The third ApplyDeferredCaptureState call site (:472), which no dispatch test
    // reaches: when the platform owns audio routing the method short-circuits before
    // the `capture is ...` arms. Retiring the seam without this would trade one
    // uncovered branch for another.
    var probe = new CaptureAcquisitionProbe();
    var btMock = BuildBtMock(capture: null, probe, platformManaged: true);
    await using var source = BuildSource(btMock);

    Assert.Equal(AudioSourceState.Created, source.State);

    RaiseDeviceConnected(btMock);

    await WaitForAsync(() => source.State == AudioSourceState.Ready);

    Assert.Equal(AudioSourceState.Ready, source.State);
    Assert.Equal(0, probe.Calls);
    btMock.Verify(
      b => b.GetAudioCaptureDeviceAsync(It.IsAny<CancellationToken>()), Times.Never);
  }

  /// <summary>
  /// The downstream invariant that actually matters: fingerprinting keeps running.
  /// <c>SoundFlowAudioTap.IsActive</c> requires the active source to be in <c>Playing</c>,
  /// so a demotion to <c>Ready</c> when the deferred capture lands kills song recognition.
  /// <para>
  /// ⚠ <c>TEST-2</c> rewrote how this test gets there, and kept the assertion untouched.
  /// It used to reach the state by calling <c>ApplyDeferredCaptureState()</c> directly
  /// through an <c>internal</c> seam — which executed the state decision without executing
  /// the dispatch that selects it, and so read as coverage of a branch it never entered.
  /// It now plays, then lands the deferred acquisition through the real
  /// <c>DeviceConnected</c> path. This is the one test of the three whose assertion was
  /// irreplaceable; the other two are superseded by <c>DeviceConnectedEvent_*</c>.
  /// </para>
  /// </summary>
  [Fact]
  public async Task DeferredCaptureAcquisition_ThroughDispatch_KeepsAudioTapActive()
  {
    var capture = NewCaptureDeviceMock();
    var probe = new CaptureAcquisitionProbe();
    var btMock = BuildBtMock(capture, probe, nullAcquisitions: 2);
    await using var source = BuildSource(btMock);

    await source.PlayAsync(CancellationToken.None);
    Assert.Equal(AudioSourceState.Playing, source.State);

    RaiseDeviceConnected(btMock);
    await WaitForAsync(() => probe.Calls >= 3);
    Assert.Equal(AudioSourceState.Playing, source.State);

    var engineMock = new Mock<IAudioEngine>();
    engineMock.Setup(e => e.State).Returns(AudioEngineState.Running);

    var managerMock = new Mock<IAudioManager>();
    managerMock.Setup(m => m.ActiveSource).Returns(source);

    var tap = new SoundFlowAudioTap(
      new Mock<ILogger<SoundFlowAudioTap>>().Object,
      engineMock.Object,
      managerMock.Object);

    Assert.True(tap.IsActive, "Fingerprinting must stay active after deferred capture lands");
  }

  /// <summary>
  /// Polls a condition to a deadline. Used instead of a fixed Task.Delay because
  /// TryAcquireAudioCaptureAsync is fire-and-forget (:370) — there is no handle to await, so
  /// the test must synchronize on the observation rather than on elapsed time
  /// (CLAUDE.md § Test Timing).
  /// </summary>
  /// <remarks>
  /// ⚠ <b>A deadline is a wall clock, so this helper is NOT starvation-proof on its own.</b>
  /// It returns once <paramref name="timeoutMs"/> elapses whether the condition came true or
  /// not, and the caller's next assertion then fails. An earlier revision of this comment
  /// claimed starvation could only slow these tests and never flip a pass to a fail; that was
  /// wrong, and it was measured wrong — disabling a dispatch arm made a caller sit here for
  /// the full 5 s and then fail.
  ///
  /// <para>
  /// What actually makes these tests deterministic is upstream, and it is not patience: the
  /// handler runs to completion SYNCHRONOUSLY inside <c>btMock.Raise</c>.
  /// <c>OnDeviceConnected</c> starts <c>TryAcquireAudioCaptureAsync</c> by direct invocation
  /// rather than <c>Task.Run</c>, <c>GetAudioCaptureDeviceAsync</c> is set up to return
  /// <c>Task.FromResult</c>, and <c>_routeLock.WaitAsync()</c> is uncontended (the background
  /// retry loop sleeps 10 s before its first attempt), so there is no incomplete await
  /// anywhere on that path. The condition is therefore already true on its first evaluation
  /// and this helper never actually awaits. <b>Add a real await point to that path and these
  /// tests become timing races — the deadline is not the safety net.</b>
  /// </para>
  ///
  /// <para>
  /// The closing assertion exists so a starved or genuinely broken run fails HERE, naming the
  /// timeout, instead of downstream on a confusing state mismatch.
  /// </para>
  /// </remarks>
  private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 5000)
  {
    var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
    while (!condition() && DateTime.UtcNow < deadline)
    {
      await Task.Delay(10);
    }

    Assert.True(condition(), $"WaitForAsync timed out after {timeoutMs} ms — condition never became true.");
  }
}

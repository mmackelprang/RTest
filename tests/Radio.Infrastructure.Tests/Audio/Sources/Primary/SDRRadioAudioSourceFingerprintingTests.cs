using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Events;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Fingerprinting;
using Radio.Infrastructure.Audio.Sources.Primary;
using RTLSDRCore;
using RTLSDRCore.Hardware;

namespace Radio.Infrastructure.Tests.Audio.Sources.Primary;

/// <summary>
/// Unit tests for SDRRadioAudioSource fingerprinting functionality.
/// Tests the UpdateMetadataFromFingerprint method and TrackIdentified event handling.
/// </summary>
public class SDRRadioAudioSourceFingerprintingTests
{
  private readonly Mock<ILogger<SDRRadioAudioSource>> _loggerMock;
  private readonly Mock<ISdrDevice> _sdrDeviceMock;
  private readonly RadioReceiver _radioReceiver;
  private readonly Mock<IOptionsMonitor<RadioOptions>> _radioOptionsMock;
  private readonly Mock<IMetricsCollector> _metricsCollectorMock;
  private readonly RadioOptions _radioOptions;

  public SDRRadioAudioSourceFingerprintingTests()
  {
    _loggerMock = new Mock<ILogger<SDRRadioAudioSource>>();
    _sdrDeviceMock = new Mock<ISdrDevice>();
    _metricsCollectorMock = new Mock<IMetricsCollector>();

    // Set up the mock SDR device with minimal required setup
    var deviceInfo = new RTLSDRCore.Models.DeviceInfo
    {
      Index = 0,
      Name = "Mock RTL-SDR Device",
      Type = RTLSDRCore.Enums.DeviceType.Mock,
      Serial = "TEST123",
      Manufacturer = "Test",
      TunerType = "Test Tuner",
      IsAvailable = true,
      MinFrequencyHz = 24_000_000,
      MaxFrequencyHz = 1_766_000_000
    };

    _sdrDeviceMock.Setup(d => d.DeviceInfo).Returns(deviceInfo);
    _sdrDeviceMock.Setup(d => d.IsOpen).Returns(false);
    _sdrDeviceMock.Setup(d => d.GetSampleRate()).Returns(2048000);
    _sdrDeviceMock.Setup(d => d.GetFrequency()).Returns(100_000_000);

    // Create a real RadioReceiver with the mocked device
    _radioReceiver = new RadioReceiver(_sdrDeviceMock.Object);

    _radioOptions = new RadioOptions
    {
      DefaultDevice = "RTLSDRCore",
      DefaultDeviceVolume = 50,
      DefaultFMStepMHz = 0.1
    };

    _radioOptionsMock = new Mock<IOptionsMonitor<RadioOptions>>();
    _radioOptionsMock.Setup(o => o.CurrentValue).Returns(_radioOptions);
  }

  [Fact]
  public void Constructor_WithoutIdentificationService_CreatesSuccessfully()
  {
    // Act
    var source = CreateSource(identificationService: null);

    // Assert
    Assert.NotNull(source);
    Assert.Equal("SDR Radio (RTL-SDR)", source.Name);
    Assert.Equal(AudioSourceType.Radio, source.Type);
  }

  [Fact]
  public void Constructor_WithIdentificationService_CreatesSuccessfully()
  {
    // Arrange
    var identificationService = CreateIdentificationService();

    // Act
    var source = CreateSource(identificationService: identificationService);

    // Assert
    Assert.NotNull(source);
    Assert.Equal("SDR Radio (RTL-SDR)", source.Name);
    Assert.Equal(AudioSourceType.Radio, source.Type);
  }

  [Fact]
  public void UpdateMetadataFromFingerprint_WithBasicTrackData_UpdatesMetadata()
  {
    // Arrange
    var source = CreateSource(identificationService: null);
    var track = CreateTrackMetadata(
      title: "Test Song",
      artist: "Test Artist",
      album: "Test Album"
    );
    var confidence = 0.95;
    var identifiedAt = DateTime.UtcNow;

    // Act
    InvokeUpdateMetadataFromFingerprint(source, track, confidence, identifiedAt);

    // Assert
    Assert.Contains(StandardMetadataKeys.Title, source.Metadata.Keys);
    Assert.Contains(StandardMetadataKeys.Artist, source.Metadata.Keys);
    Assert.Contains(StandardMetadataKeys.Album, source.Metadata.Keys);
    Assert.Equal("Test Song", source.Metadata[StandardMetadataKeys.Title]);
    Assert.Equal("Test Artist", source.Metadata[StandardMetadataKeys.Artist]);
    Assert.Equal("Test Album", source.Metadata[StandardMetadataKeys.Album]);
  }

  [Fact]
  public void UpdateMetadataFromFingerprint_WithAllOptionalFields_UpdatesAllFields()
  {
    // Arrange
    var source = CreateSource(identificationService: null);
    var track = CreateTrackMetadata(
      title: "Complete Song",
      artist: "Complete Artist",
      album: "Complete Album",
      genre: "Rock",
      releaseYear: 2023,
      trackNumber: 5,
      coverArtUrl: "https://example.com/art.jpg"
    );
    var confidence = 0.98;
    var identifiedAt = DateTime.UtcNow;

    // Act
    InvokeUpdateMetadataFromFingerprint(source, track, confidence, identifiedAt);

    // Assert
    Assert.Equal("Complete Song", source.Metadata[StandardMetadataKeys.Title]);
    Assert.Equal("Complete Artist", source.Metadata[StandardMetadataKeys.Artist]);
    Assert.Equal("Complete Album", source.Metadata[StandardMetadataKeys.Album]);
    Assert.Equal("Rock", source.Metadata[StandardMetadataKeys.Genre]);
    Assert.Equal(2023, source.Metadata[StandardMetadataKeys.Year]);
    Assert.Equal(5, source.Metadata[StandardMetadataKeys.TrackNumber]);
    Assert.Equal("https://example.com/art.jpg", source.Metadata[StandardMetadataKeys.AlbumArtUrl]);
  }

  [Fact]
  public void UpdateMetadataFromFingerprint_WithNullCoverArt_UsesDefaultAlbumArtUrl()
  {
    // Arrange
    var source = CreateSource(identificationService: null);
    var track = CreateTrackMetadata(
      title: "Song Without Art",
      artist: "Artist",
      album: "Album",
      coverArtUrl: null
    );
    var confidence = 0.90;
    var identifiedAt = DateTime.UtcNow;

    // Act
    InvokeUpdateMetadataFromFingerprint(source, track, confidence, identifiedAt);

    // Assert
    Assert.Equal(StandardMetadataKeys.DefaultAlbumArtUrl, source.Metadata[StandardMetadataKeys.AlbumArtUrl]);
  }

  [Fact]
  public void UpdateMetadataFromFingerprint_WithEmptyCoverArt_UsesDefaultAlbumArtUrl()
  {
    // Arrange
    var source = CreateSource(identificationService: null);
    var track = CreateTrackMetadata(
      title: "Song With Empty Art",
      artist: "Artist",
      album: "Album",
      coverArtUrl: ""
    );
    var confidence = 0.88;
    var identifiedAt = DateTime.UtcNow;

    // Act
    InvokeUpdateMetadataFromFingerprint(source, track, confidence, identifiedAt);

    // Assert
    Assert.Equal(StandardMetadataKeys.DefaultAlbumArtUrl, source.Metadata[StandardMetadataKeys.AlbumArtUrl]);
  }

  [Fact]
  public void UpdateMetadataFromFingerprint_PreservesSourceField()
  {
    // Arrange
    var source = CreateSource(identificationService: null);
    
    // Add Source metadata using reflection to access private field
    var metadata = GetMetadataDictionary(source);
    metadata["Source"] = "Test Source";

    var track = CreateTrackMetadata(
      title: "Test Song",
      artist: "Test Artist",
      album: "Test Album"
    );
    var confidence = 0.95;
    var identifiedAt = DateTime.UtcNow;

    // Act
    InvokeUpdateMetadataFromFingerprint(source, track, confidence, identifiedAt);

    // Assert
    Assert.Contains("Source", source.Metadata.Keys);
    Assert.Equal("Test Source", source.Metadata["Source"]);
  }

  [Fact]
  public void UpdateMetadataFromFingerprint_PreservesDeviceField()
  {
    // Arrange
    var source = CreateSource(identificationService: null);
    
    // Add Device metadata using reflection to access private field
    var metadata = GetMetadataDictionary(source);
    metadata["Device"] = "Test Device";

    var track = CreateTrackMetadata(
      title: "Test Song",
      artist: "Test Artist",
      album: "Test Album"
    );
    var confidence = 0.95;
    var identifiedAt = DateTime.UtcNow;

    // Act
    InvokeUpdateMetadataFromFingerprint(source, track, confidence, identifiedAt);

    // Assert
    Assert.Contains("Device", source.Metadata.Keys);
    Assert.Equal("Test Device", source.Metadata["Device"]);
  }

  [Fact]
  public void UpdateMetadataFromFingerprint_AddsIdentificationConfidence()
  {
    // Arrange
    var source = CreateSource(identificationService: null);
    var track = CreateTrackMetadata(
      title: "Test Song",
      artist: "Test Artist",
      album: "Test Album"
    );
    var confidence = 0.92;
    var identifiedAt = DateTime.UtcNow;

    // Act
    InvokeUpdateMetadataFromFingerprint(source, track, confidence, identifiedAt);

    // Assert
    Assert.Contains("IdentificationConfidence", source.Metadata.Keys);
    Assert.Equal(confidence, source.Metadata["IdentificationConfidence"]);
  }

  [Fact]
  public void UpdateMetadataFromFingerprint_AddsIdentifiedAtTimestamp()
  {
    // Arrange
    var source = CreateSource(identificationService: null);
    var track = CreateTrackMetadata(
      title: "Test Song",
      artist: "Test Artist",
      album: "Test Album"
    );
    var confidence = 0.92;
    var identifiedAt = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);

    // Act
    InvokeUpdateMetadataFromFingerprint(source, track, confidence, identifiedAt);

    // Assert
    Assert.Contains("IdentifiedAt", source.Metadata.Keys);
    Assert.Equal(identifiedAt, source.Metadata["IdentifiedAt"]);
  }

  [Fact]
  public void UpdateMetadataFromFingerprint_AddsMetadataSource()
  {
    // Arrange
    var source = CreateSource(identificationService: null);
    var track = CreateTrackMetadata(
      title: "Test Song",
      artist: "Test Artist",
      album: "Test Album"
    );
    var confidence = 0.92;
    var identifiedAt = DateTime.UtcNow;

    // Act
    InvokeUpdateMetadataFromFingerprint(source, track, confidence, identifiedAt);

    // Assert
    Assert.Contains("MetadataSource", source.Metadata.Keys);
    Assert.Equal("Fingerprinting", source.Metadata["MetadataSource"]);
  }

  [Fact]
  public void OnTrackIdentified_WhenPlaying_UpdatesMetadata()
  {
    // Arrange
    var identificationService = CreateIdentificationService();
    var source = CreateSource(identificationService: identificationService);
    
    // Manually set the state to Playing using reflection to bypass hardware requirement
    var stateField = typeof(PrimaryAudioSourceBase).GetField(
      "_state",
      System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    stateField?.SetValue(source, AudioSourceState.Playing);

    var track = CreateTrackMetadata(
      title: "Identified Song",
      artist: "Identified Artist",
      album: "Identified Album"
    );
    var eventArgs = new TrackIdentifiedEventArgs(track, 0.96);

    // Act - Raise the TrackIdentified event using reflection
    InvokeOnTrackIdentified(source, identificationService, eventArgs);

    // Assert - Check that metadata was updated
    Assert.Equal("Identified Song", source.Metadata[StandardMetadataKeys.Title]);
    Assert.Equal("Identified Artist", source.Metadata[StandardMetadataKeys.Artist]);
    Assert.Equal("Identified Album", source.Metadata[StandardMetadataKeys.Album]);
    Assert.Contains("IdentificationConfidence", source.Metadata.Keys);
    Assert.Equal(0.96, source.Metadata["IdentificationConfidence"]);
  }

  [Fact]
  public void OnTrackIdentified_WhenPaused_UpdatesMetadata()
  {
    // Arrange
    var identificationService = CreateIdentificationService();
    var source = CreateSource(identificationService: identificationService);
    
    // Manually set the state to Paused using reflection to bypass hardware requirement
    var stateField = typeof(PrimaryAudioSourceBase).GetField(
      "_state",
      System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    stateField?.SetValue(source, AudioSourceState.Paused);

    var track = CreateTrackMetadata(
      title: "Paused Song",
      artist: "Paused Artist",
      album: "Paused Album"
    );
    var eventArgs = new TrackIdentifiedEventArgs(track, 0.93);

    // Act - Raise the TrackIdentified event using reflection
    InvokeOnTrackIdentified(source, identificationService, eventArgs);

    // Assert - Check that metadata was updated
    Assert.Equal("Paused Song", source.Metadata[StandardMetadataKeys.Title]);
    Assert.Equal("Paused Artist", source.Metadata[StandardMetadataKeys.Artist]);
    Assert.Equal("Paused Album", source.Metadata[StandardMetadataKeys.Album]);
    Assert.Contains("IdentificationConfidence", source.Metadata.Keys);
    Assert.Equal(0.93, source.Metadata["IdentificationConfidence"]);
  }

  [Fact]
  public void OnTrackIdentified_WhenNotPlaying_DoesNotUpdateMetadata()
  {
    // Arrange
    var identificationService = CreateIdentificationService();
    var source = CreateSource(identificationService: identificationService);
    
    // Ensure source is in Created state (default state)
    Assert.Equal(AudioSourceState.Created, source.State);

    var track = CreateTrackMetadata(
      title: "Should Not Update",
      artist: "Should Not Update Artist",
      album: "Should Not Update Album"
    );
    var eventArgs = new TrackIdentifiedEventArgs(track, 0.96);

    // Act - Raise the TrackIdentified event while source is in Created state
    InvokeOnTrackIdentified(source, identificationService, eventArgs);

    // Assert - Metadata should not have been updated to the new track
    // The metadata should still have the default SDR Radio metadata
    Assert.Contains(StandardMetadataKeys.Title, source.Metadata.Keys);
    Assert.Equal("SDR Radio", source.Metadata[StandardMetadataKeys.Title]);
    Assert.NotEqual("Should Not Update", source.Metadata[StandardMetadataKeys.Title]);
  }

  [Fact]
  public void UpdateMetadataFromFingerprint_WithNullAlbum_UsesDefaultAlbum()
  {
    // Arrange
    var source = CreateSource(identificationService: null);
    var track = CreateTrackMetadata(
      title: "Song Without Album",
      artist: "Artist",
      album: null
    );
    var confidence = 0.85;
    var identifiedAt = DateTime.UtcNow;

    // Act
    InvokeUpdateMetadataFromFingerprint(source, track, confidence, identifiedAt);

    // Assert
    Assert.Equal(StandardMetadataKeys.DefaultAlbum, source.Metadata[StandardMetadataKeys.Album]);
  }

  [Fact]
  public void UpdateMetadataFromFingerprint_DoesNotAddOptionalFieldsWhenNull()
  {
    // Arrange
    var source = CreateSource(identificationService: null);
    var track = CreateTrackMetadata(
      title: "Minimal Song",
      artist: "Minimal Artist",
      album: "Minimal Album",
      genre: null,
      releaseYear: null,
      trackNumber: null
    );
    var confidence = 0.80;
    var identifiedAt = DateTime.UtcNow;

    // Act
    InvokeUpdateMetadataFromFingerprint(source, track, confidence, identifiedAt);

    // Assert - Optional fields should not be present in metadata when null
    Assert.False(source.Metadata.ContainsKey(StandardMetadataKeys.Genre));
    Assert.False(source.Metadata.ContainsKey(StandardMetadataKeys.Year));
    Assert.False(source.Metadata.ContainsKey(StandardMetadataKeys.TrackNumber));
  }

  #region Helper Methods

  private SDRRadioAudioSource CreateSource(BackgroundIdentificationService? identificationService)
  {
    return new SDRRadioAudioSource(
      _loggerMock.Object,
      _radioReceiver,
      _radioOptionsMock.Object,
      _metricsCollectorMock.Object,
      identificationService);
  }

  private BackgroundIdentificationService CreateIdentificationService()
  {
    var mockLogger = new Mock<ILogger<BackgroundIdentificationService>>();
    var mockServiceProvider = new Mock<IServiceProvider>();
    var options = Options.Create(new FingerprintingOptions { Enabled = false });
    
    return new BackgroundIdentificationService(
      mockLogger.Object,
      mockServiceProvider.Object,
      options);
  }

  private TrackMetadata CreateTrackMetadata(
    string title,
    string artist,
    string? album,
    string? genre = null,
    int? releaseYear = null,
    int? trackNumber = null,
    string? coverArtUrl = null)
  {
    return new TrackMetadata
    {
      Id = Guid.NewGuid().ToString(),
      Title = title,
      Artist = artist,
      Album = album,
      Genre = genre,
      ReleaseYear = releaseYear,
      TrackNumber = trackNumber,
      CoverArtUrl = coverArtUrl,
      Source = MetadataSource.Fingerprinting,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
  }

  /// <summary>
  /// Helper method to invoke the protected UpdateMetadataFromFingerprint method via reflection.
  /// </summary>
  private void InvokeUpdateMetadataFromFingerprint(
    SDRRadioAudioSource source,
    TrackMetadata track,
    double confidence,
    DateTime identifiedAt)
  {
    var method = typeof(SDRRadioAudioSource).GetMethod(
      "UpdateMetadataFromFingerprint",
      System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    method?.Invoke(source, new object[] { track, confidence, identifiedAt });
  }

  /// <summary>
  /// Helper method to invoke the private OnTrackIdentified event handler via reflection.
  /// </summary>
  private void InvokeOnTrackIdentified(
    SDRRadioAudioSource source,
    BackgroundIdentificationService identificationService,
    TrackIdentifiedEventArgs eventArgs)
  {
    var method = typeof(SDRRadioAudioSource).GetMethod(
      "OnTrackIdentified",
      System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    method?.Invoke(source, new object[] { identificationService, eventArgs });
  }

  /// <summary>
  /// Helper method to access the private _metadata field via reflection.
  /// </summary>
  private Dictionary<string, object> GetMetadataDictionary(SDRRadioAudioSource source)
  {
    var metadataField = typeof(SDRRadioAudioSource).GetField(
      "_metadata",
      System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    return (Dictionary<string, object>)metadataField!.GetValue(source)!;
  }

  #endregion
}

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Events;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Fingerprinting;
using Radio.Infrastructure.Audio.Sources.Primary;

namespace Radio.Infrastructure.Tests.Audio.Sources.Primary;

/// <summary>
/// Tests for standardized metadata format across all audio sources.
/// </summary>
public class StandardMetadataTests : IDisposable
{
  private readonly Mock<ILogger<FilePlayerAudioSource>> _filePlayerLoggerMock;
  private readonly Mock<ILogger<RadioAudioSource>> _radioLoggerMock;
  private readonly Mock<ILogger<VinylAudioSource>> _vinylLoggerMock;
  private readonly Mock<IOptionsMonitor<FilePlayerOptions>> _filePlayerOptionsMock;
  private readonly Mock<IOptionsMonitor<FilePlayerPreferences>> _filePlayerPreferencesMock;
  private readonly Mock<IOptionsMonitor<DeviceOptions>> _deviceOptionsMock;
  private readonly Mock<IOptionsMonitor<RadioOptions>> _radioOptionsMock;
  private readonly Mock<IAudioDeviceManager> _deviceManagerMock;
  private readonly string _testDir;

  public StandardMetadataTests()
  {
    _filePlayerLoggerMock = new Mock<ILogger<FilePlayerAudioSource>>();
    _radioLoggerMock = new Mock<ILogger<RadioAudioSource>>();
    _vinylLoggerMock = new Mock<ILogger<VinylAudioSource>>();
    _deviceManagerMock = new Mock<IAudioDeviceManager>();

    var filePlayerOptions = new FilePlayerOptions
    {
      RootDirectory = "",
      SupportedExtensions = [".mp3", ".flac", ".wav", ".ogg"]
    };

    var filePlayerPreferences = new FilePlayerPreferences
    {
      LastSongPlayed = "",
      SongPositionMs = 0,
      Shuffle = false,
      Repeat = RepeatMode.Off
    };

    _filePlayerOptionsMock = new Mock<IOptionsMonitor<FilePlayerOptions>>();
    _filePlayerOptionsMock.Setup(o => o.CurrentValue).Returns(filePlayerOptions);

    _filePlayerPreferencesMock = new Mock<IOptionsMonitor<FilePlayerPreferences>>();
    _filePlayerPreferencesMock.Setup(o => o.CurrentValue).Returns(filePlayerPreferences);

    var deviceOptions = new DeviceOptions
    {
      Radio = new RadioDeviceOptions { USBPort = "test-radio-port" },
      Vinyl = new VinylDeviceOptions { USBPort = "test-vinyl-port" }
    };

    _deviceOptionsMock = new Mock<IOptionsMonitor<DeviceOptions>>();
    _deviceOptionsMock.Setup(o => o.CurrentValue).Returns(deviceOptions);

    var radioOptions = new RadioOptions
    {
      DefaultDevice = "RTLSDRCore",
      DefaultDeviceVolume = 50
    };

    _radioOptionsMock = new Mock<IOptionsMonitor<RadioOptions>>();
    _radioOptionsMock.Setup(o => o.CurrentValue).Returns(radioOptions);

    // Setup device manager to indicate ports are not in use
    _deviceManagerMock.Setup(m => m.IsUSBPortInUse(It.IsAny<string>())).Returns(false);

    _testDir = Path.Combine(Path.GetTempPath(), $"MetadataTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(_testDir);
  }

  public void Dispose()
  {
    if (Directory.Exists(_testDir))
    {
      Directory.Delete(_testDir, recursive: true);
    }
  }

  [Fact]
  public void StandardMetadataKeys_AreDefinedCorrectly()
  {
    // Assert - Verify all standard keys are defined
    Assert.Equal("Title", StandardMetadataKeys.Title);
    Assert.Equal("Artist", StandardMetadataKeys.Artist);
    Assert.Equal("Album", StandardMetadataKeys.Album);
    Assert.Equal("AlbumArtUrl", StandardMetadataKeys.AlbumArtUrl);
    Assert.Equal("Duration", StandardMetadataKeys.Duration);
    Assert.Equal("TrackNumber", StandardMetadataKeys.TrackNumber);
    Assert.Equal("Genre", StandardMetadataKeys.Genre);
    Assert.Equal("Year", StandardMetadataKeys.Year);
  }

  [Fact]
  public void StandardMetadataKeys_DefaultValues_AreDefinedCorrectly()
  {
    // Assert - Verify default values
    Assert.Equal("No Track", StandardMetadataKeys.DefaultTitle);
    Assert.Equal("--", StandardMetadataKeys.DefaultArtist);
    Assert.Equal("--", StandardMetadataKeys.DefaultAlbum);
    Assert.Equal("/images/default-album-art.png", StandardMetadataKeys.DefaultAlbumArtUrl);
  }

  [Fact]
  public void FilePlayerAudioSource_Metadata_IsObjectType()
  {
    // Arrange
    var source = new FilePlayerAudioSource(
      _filePlayerLoggerMock.Object,
      _filePlayerOptionsMock.Object,
      _filePlayerPreferencesMock.Object,
      _testDir);

    // Assert - Verify metadata is IReadOnlyDictionary<string, object>
    Assert.IsAssignableFrom<IReadOnlyDictionary<string, object>>(source.Metadata);
  }

  [Fact]
  public async Task FilePlayerAudioSource_WithFile_UsesStandardMetadataKeys()
  {
    // Arrange
    var source = new FilePlayerAudioSource(
      _filePlayerLoggerMock.Object,
      _filePlayerOptionsMock.Object,
      _filePlayerPreferencesMock.Object,
      _testDir);

    var testFile = Path.Combine(_testDir, "test.mp3");
    File.WriteAllText(testFile, "test content");

    // Act
    await source.LoadFileAsync("test.mp3");

    // Assert - Standard keys should exist
    Assert.Contains(StandardMetadataKeys.Title, source.Metadata.Keys);
    Assert.Contains(StandardMetadataKeys.Artist, source.Metadata.Keys);
    Assert.Contains(StandardMetadataKeys.Album, source.Metadata.Keys);
    Assert.Contains(StandardMetadataKeys.AlbumArtUrl, source.Metadata.Keys);
    Assert.Contains(StandardMetadataKeys.Duration, source.Metadata.Keys);
  }

  [Fact]
  public async Task FilePlayerAudioSource_WithFile_ProvidesDefaultValues()
  {
    // Arrange
    var source = new FilePlayerAudioSource(
      _filePlayerLoggerMock.Object,
      _filePlayerOptionsMock.Object,
      _filePlayerPreferencesMock.Object,
      _testDir);

    var testFile = Path.Combine(_testDir, "test.mp3");
    File.WriteAllText(testFile, "test content");

    // Act
    await source.LoadFileAsync("test.mp3");

    // Assert - Should have default values for missing metadata
    Assert.Equal(StandardMetadataKeys.DefaultArtist, source.Metadata[StandardMetadataKeys.Artist]);
    Assert.Equal(StandardMetadataKeys.DefaultAlbum, source.Metadata[StandardMetadataKeys.Album]);
    Assert.Equal(StandardMetadataKeys.DefaultAlbumArtUrl, source.Metadata[StandardMetadataKeys.AlbumArtUrl]);
  }

  [Fact]
  public async Task FilePlayerAudioSource_Duration_IsTimeSpanType()
  {
    // Arrange
    var source = new FilePlayerAudioSource(
      _filePlayerLoggerMock.Object,
      _filePlayerOptionsMock.Object,
      _filePlayerPreferencesMock.Object,
      _testDir);

    var testFile = Path.Combine(_testDir, "test.mp3");
    File.WriteAllText(testFile, "test content");

    // Act
    await source.LoadFileAsync("test.mp3");

    // Assert - Duration should be TimeSpan type, not string
    Assert.Contains(StandardMetadataKeys.Duration, source.Metadata.Keys);
    Assert.IsType<TimeSpan>(source.Metadata[StandardMetadataKeys.Duration]);
  }

  [Fact]
  public async Task RadioAudioSource_UsesStandardMetadataKeys()
  {
    // Arrange
    var source = new RadioAudioSource(
      _radioLoggerMock.Object,
      _deviceOptionsMock.Object,
      _radioOptionsMock.Object,
      _deviceManagerMock.Object);

    // Act
    try
    {
      await source.PlayAsync();
    }
    catch
    {
      // Expected to fail due to missing actual device, but metadata should still be set
    }

    // Assert - Standard keys should exist with defaults
    Assert.Contains(StandardMetadataKeys.Title, source.Metadata.Keys);
    Assert.Equal("Radio", source.Metadata[StandardMetadataKeys.Title]);
    Assert.Equal(StandardMetadataKeys.DefaultArtist, source.Metadata[StandardMetadataKeys.Artist]);
    Assert.Equal(StandardMetadataKeys.DefaultAlbum, source.Metadata[StandardMetadataKeys.Album]);
    Assert.Equal(StandardMetadataKeys.DefaultAlbumArtUrl, source.Metadata[StandardMetadataKeys.AlbumArtUrl]);
  }

  [Fact]
  public async Task VinylAudioSource_UsesStandardMetadataKeys()
  {
    // Arrange
    var source = new VinylAudioSource(
      _vinylLoggerMock.Object,
      _deviceOptionsMock.Object,
      _deviceManagerMock.Object);

    // Act
    try
    {
      await source.PlayAsync();
    }
    catch
    {
      // Expected to fail due to missing actual device, but metadata should still be set
    }

    // Assert - Standard keys should exist with defaults
    Assert.Contains(StandardMetadataKeys.Title, source.Metadata.Keys);
    Assert.Equal("Vinyl", source.Metadata[StandardMetadataKeys.Title]);
    Assert.Equal(StandardMetadataKeys.DefaultArtist, source.Metadata[StandardMetadataKeys.Artist]);
    Assert.Equal(StandardMetadataKeys.DefaultAlbum, source.Metadata[StandardMetadataKeys.Album]);
    Assert.Equal(StandardMetadataKeys.DefaultAlbumArtUrl, source.Metadata[StandardMetadataKeys.AlbumArtUrl]);
  }

  [Fact]
  public void RadioAudioSource_Metadata_IsObjectType()
  {
    // Arrange
    var source = new RadioAudioSource(
      _radioLoggerMock.Object,
      _deviceOptionsMock.Object,
      _radioOptionsMock.Object,
      _deviceManagerMock.Object);

    // Assert - Verify metadata is IReadOnlyDictionary<string, object>
    Assert.IsAssignableFrom<IReadOnlyDictionary<string, object>>(source.Metadata);
  }

  [Fact]
  public void VinylAudioSource_Metadata_IsObjectType()
  {
    // Arrange
    var source = new VinylAudioSource(
      _vinylLoggerMock.Object,
      _deviceOptionsMock.Object,
      _deviceManagerMock.Object);

    // Assert - Verify metadata is IReadOnlyDictionary<string, object>
    Assert.IsAssignableFrom<IReadOnlyDictionary<string, object>>(source.Metadata);
  }

  // Note: Tests for fingerprinting integration with RadioAudioSource, VinylAudioSource, and FilePlayerAudioSource
  // require an actual BackgroundIdentificationService instance since it's a sealed class.
  // The integration is tested through integration tests with the full service stack.
  // Unit tests verify that:
  // 1. Audio sources can be constructed with an optional BackgroundIdentificationService
  // 2. Audio sources without the service continue to work normally
  // 3. Metadata format remains consistent using StandardMetadataKeys
}

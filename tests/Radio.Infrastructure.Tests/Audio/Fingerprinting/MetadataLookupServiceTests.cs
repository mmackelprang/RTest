using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Fingerprinting;

namespace Radio.Infrastructure.Tests.Audio.Fingerprinting;

/// <summary>
/// Unit tests for the MetadataLookupService class.
/// </summary>
public class MetadataLookupServiceTests
{
  private readonly Mock<ILogger<MetadataLookupService>> _loggerMock;
  private readonly Mock<IFingerprintCacheRepository> _cacheMock;
  private readonly Mock<ITrackMetadataRepository> _metadataRepoMock;
  private readonly Mock<IOptions<FingerprintingOptions>> _optionsMock;
  private readonly FingerprintingOptions _options;
  private readonly MetadataLookupService _service;

  public MetadataLookupServiceTests()
  {
    _loggerMock = new Mock<ILogger<MetadataLookupService>>();
    _cacheMock = new Mock<IFingerprintCacheRepository>();
    _metadataRepoMock = new Mock<ITrackMetadataRepository>();

    _options = new FingerprintingOptions
    {
      MinimumConfidenceThreshold = 0.5,
      AcoustId = new AcoustIdOptions { ApiKey = "" }
    };

    _optionsMock = new Mock<IOptions<FingerprintingOptions>>();
    _optionsMock.Setup(o => o.Value).Returns(_options);

    _service = new MetadataLookupService(
      _loggerMock.Object,
      _cacheMock.Object,
      _metadataRepoMock.Object,
      _optionsMock.Object);
  }

  [Fact]
  public async Task LookupAsync_WithCachedMetadata_ReturnsCachedResult()
  {
    // Arrange
    var fingerprint = CreateTestFingerprint();
    var cachedMetadata = CreateTestMetadata();
    var cached = new CachedFingerprint
    {
      Id = "cached-id",
      ChromaprintHash = fingerprint.ChromaprintHash,
      DurationSeconds = 15,
      CreatedAt = DateTime.UtcNow,
      Metadata = cachedMetadata
    };

    _cacheMock.Setup(c => c.FindByHashAsync(fingerprint.ChromaprintHash, It.IsAny<CancellationToken>()))
      .ReturnsAsync(cached);

    // Act
    var result = await _service.LookupAsync(fingerprint);

    // Assert
    Assert.NotNull(result);
    Assert.True(result.IsMatch);
    Assert.Equal(1.0, result.Confidence);
    Assert.Equal(LookupSource.Cache, result.Source);
    Assert.Equal(cachedMetadata, result.Metadata);

    _cacheMock.Verify(c => c.UpdateLastMatchedAsync(cached.Id, It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task LookupAsync_WithNoCache_NoApiKey_StoresAndReturnsNull()
  {
    // Arrange
    var fingerprint = CreateTestFingerprint();

    _cacheMock.Setup(c => c.FindByHashAsync(fingerprint.ChromaprintHash, It.IsAny<CancellationToken>()))
      .ReturnsAsync((CachedFingerprint?)null);

    // Act
    var result = await _service.LookupAsync(fingerprint);

    // Assert
    Assert.Null(result);
    _cacheMock.Verify(c => c.StoreAsync(fingerprint, null, It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task LookupAsync_WithNullFingerprint_ThrowsArgumentNullException()
  {
    // Act & Assert
    await Assert.ThrowsAsync<ArgumentNullException>(
      () => _service.LookupAsync(null!));
  }

  [Fact]
  public async Task LookupAsync_CacheHasNoMetadata_StoresFingerprint()
  {
    // Arrange
    var fingerprint = CreateTestFingerprint();
    var cached = new CachedFingerprint
    {
      Id = "cached-id",
      ChromaprintHash = fingerprint.ChromaprintHash,
      DurationSeconds = 15,
      CreatedAt = DateTime.UtcNow,
      Metadata = null // No metadata
    };

    _cacheMock.Setup(c => c.FindByHashAsync(fingerprint.ChromaprintHash, It.IsAny<CancellationToken>()))
      .ReturnsAsync(cached);

    // Act
    var result = await _service.LookupAsync(fingerprint);

    // Assert
    Assert.Null(result);
    _cacheMock.Verify(c => c.StoreAsync(fingerprint, null, It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task GetMusicBrainzMetadataAsync_ReturnsNull_NotImplemented()
  {
    // Act
    var result = await _service.GetMusicBrainzMetadataAsync("some-recording-id");

    // Assert
    Assert.Null(result);
  }

  [Fact]
  public async Task GetMusicBrainzMetadataAsync_WithNullId_ThrowsArgumentException()
  {
    // Act & Assert
    await Assert.ThrowsAsync<ArgumentNullException>(
      () => _service.GetMusicBrainzMetadataAsync(null!));
  }

  [Fact]
  public async Task GetMusicBrainzMetadataAsync_WithEmptyId_ThrowsArgumentException()
  {
    // Act & Assert
    await Assert.ThrowsAsync<ArgumentException>(
      () => _service.GetMusicBrainzMetadataAsync(""));
  }

  private static FingerprintData CreateTestFingerprint()
  {
    return new FingerprintData
    {
      Id = Guid.NewGuid().ToString(),
      ChromaprintHash = Convert.ToBase64String(new byte[32]),
      DurationSeconds = 15,
      GeneratedAt = DateTime.UtcNow
    };
  }

  private static TrackMetadata CreateTestMetadata()
  {
    return new TrackMetadata
    {
      Id = Guid.NewGuid().ToString(),
      Title = "Test Song",
      Artist = "Test Artist",
      Album = "Test Album",
      Source = MetadataSource.AcoustID,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
  }
}

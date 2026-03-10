using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Infrastructure.Audio.Fingerprinting;
using Radio.Fingerprinting.Services;
using Radio.Fingerprinting;

namespace Radio.Fingerprinting.Tests.Services;

/// <summary>
/// Unit tests for the MetadataLookupService class (cover art search functionality).
/// </summary>
public class MetadataLookupServiceTests
{
  private readonly Mock<ILogger<MetadataLookupService>> _loggerMock;
  private readonly Mock<IOptions<FingerprintingOptions>> _optionsMock;
  private readonly FingerprintingOptions _options;
  private readonly MetadataLookupService _service;

  public MetadataLookupServiceTests()
  {
    _loggerMock = new Mock<ILogger<MetadataLookupService>>();

    _options = new FingerprintingOptions
    {
      MinimumConfidenceThreshold = 0.5,
    };

    _optionsMock = new Mock<IOptions<FingerprintingOptions>>();
    _optionsMock.Setup(o => o.Value).Returns(_options);

    _service = new MetadataLookupService(
      _loggerMock.Object,
      _optionsMock.Object,
      new HttpClient());
  }

  [Fact]
  public async Task SearchCoverArtByTextAsync_WithNullTitle_ReturnsNull()
  {
    var result = await _service.SearchCoverArtByTextAsync(null!, "Artist");
    Assert.Null(result);
  }

  [Fact]
  public async Task SearchCoverArtByTextAsync_WithEmptyArtist_ReturnsNull()
  {
    var result = await _service.SearchCoverArtByTextAsync("Title", "");
    Assert.Null(result);
  }

  [Fact]
  public async Task SearchCoverArtByTextAsync_WithWhitespaceTitle_ReturnsNull()
  {
    var result = await _service.SearchCoverArtByTextAsync("   ", "Artist");
    Assert.Null(result);
  }

  [Fact]
  public async Task GetCoverArtByReleaseIdAsync_WithNullId_ReturnsNull()
  {
    var result = await _service.GetCoverArtByReleaseIdAsync(null!);
    Assert.Null(result);
  }

  [Fact]
  public async Task GetCoverArtByReleaseIdAsync_WithEmptyId_ReturnsNull()
  {
    var result = await _service.GetCoverArtByReleaseIdAsync("");
    Assert.Null(result);
  }

  [Fact]
  public async Task GetCoverArtByReleaseIdAsync_WithWhitespaceId_ReturnsNull()
  {
    var result = await _service.GetCoverArtByReleaseIdAsync("   ");
    Assert.Null(result);
  }
}

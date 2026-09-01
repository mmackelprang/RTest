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

    // A bare `new HttpClient()` here would be hermetic only by accident: every test below
    // exercises a guard clause that returns before the service issues a request, so nothing
    // reaches MusicBrainz *today*. The next test that passes a valid title and artist would
    // silently make a live third-party call from CI. Stub the transport so that stays
    // impossible by construction rather than by coincidence. (TEST-1(c))
    _service = new MetadataLookupService(
      _loggerMock.Object,
      _optionsMock.Object,
      new HttpClient(new NoNetworkHandler()));
  }

  /// <summary>Fails any outbound request without touching the network, naming the URI so an
  /// accidental live call is identifiable in test output.</summary>
  private sealed class NoNetworkHandler : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
    {
      throw new HttpRequestException(
        $"Blocked by the test rig: this unit test tried to reach '{request.RequestUri}'.");
    }
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

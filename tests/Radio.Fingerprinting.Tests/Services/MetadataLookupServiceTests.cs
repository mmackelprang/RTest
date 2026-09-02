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
  private readonly NoNetworkHandler _coverArtHandler = new();

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
    // silently make a live third-party call from CI. (TEST-1(c))
    //
    // BOTH transports have to be stubbed, and that is not obvious: GetCoverArtUrlAsync
    // deliberately does not use the injected client — it builds its own, so that archive.org
    // does not receive the MusicBrainz User-Agent. Stubbing only the injected client would
    // leave `GetCoverArtByReleaseIdAsync` with a valid id free to call coverartarchive.org
    // for real, which is exactly the trap the three GetCoverArtByReleaseIdAsync_With*Id tests
    // below invite someone to walk into.
    _service = new MetadataLookupService(
      _loggerMock.Object,
      _optionsMock.Object,
      new HttpClient(new NoNetworkHandler()),
      metricsCollector: null,
      coverArtHandler: _coverArtHandler);
  }

  /// <summary>Fails any outbound request without touching the network, naming the URI so an
  /// accidental live call is identifiable in test output. Counts attempts so a test can prove
  /// a request was routed through this stub rather than through a real client.</summary>
  private sealed class NoNetworkHandler : HttpMessageHandler
  {
    public int Attempts { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
    {
      Attempts++;
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
  public async Task GetCoverArtByReleaseIdAsync_WithValidId_DoesNotReachTheNetwork()
  {
    // Regression guard for TEST-1(c). The other GetCoverArtByReleaseIdAsync tests all return at
    // the guard clause, so they would still pass if the Cover Art Archive transport went back to
    // building its own HttpClient. This one passes a valid id, so it reaches the request — and
    // asserts the request went through the injected stub. Remove the seam and Attempts stays 0.
    var result = await _service.GetCoverArtByReleaseIdAsync("d2f5f3a1-0000-4000-8000-000000000000");

    Assert.Null(result);
    Assert.True(
      _coverArtHandler.Attempts > 0,
      "The Cover Art Archive request did not go through the injected handler — the transport " +
      "seam is bypassed and this test could make a live call to coverartarchive.org.");
  }

  [Fact]
  public async Task GetCoverArtByReleaseIdAsync_WithWhitespaceId_ReturnsNull()
  {
    var result = await _service.GetCoverArtByReleaseIdAsync("   ");
    Assert.Null(result);
  }
}

using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;

namespace Radio.IntegrationTests.TestSupport;

/// <summary>
/// Mock metadata lookup service for integration tests.
/// Returns configurable cover art results without requiring external API calls.
/// </summary>
public class MockMetadataLookupService : IMetadataLookupService
{
  /// <summary>
  /// Gets or sets the cover art URL to return from SearchCoverArtByTextAsync.
  /// </summary>
  public string? CoverArtUrl { get; set; }

  public Task<string?> SearchCoverArtByTextAsync(
    string title, string artist, string? album = null, CancellationToken ct = default)
  {
    return Task.FromResult(CoverArtUrl);
  }

  public Task<string?> GetCoverArtByReleaseIdAsync(string releaseId, CancellationToken ct = default)
  {
    return Task.FromResult(CoverArtUrl);
  }
}

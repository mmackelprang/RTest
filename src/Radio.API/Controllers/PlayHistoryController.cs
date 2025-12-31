using Microsoft.AspNetCore.Mvc;
using Radio.API.Models;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;

namespace Radio.API.Controllers;

/// <summary>
/// API controller for play history operations.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class PlayHistoryController : ControllerBase
{
  private readonly ILogger<PlayHistoryController> _logger;
  private readonly IPlayHistoryRepository _playHistoryRepository;
  private readonly ITrackMetadataRepository _metadataRepository;

  /// <summary>
  /// Initializes a new instance of the PlayHistoryController.
  /// </summary>
  public PlayHistoryController(
    ILogger<PlayHistoryController> logger,
    IPlayHistoryRepository playHistoryRepository,
    ITrackMetadataRepository metadataRepository)
  {
    _logger = logger;
    _playHistoryRepository = playHistoryRepository;
    _metadataRepository = metadataRepository;
  }

  /// <summary>
  /// Gets recent play history entries.
  /// </summary>
  /// <param name="limit">Number of entries to retrieve (default 20, max 100).</param>
  /// <param name="offset">Number of entries to skip.</param>
  /// <returns>A list of recent play history entries.</returns>
  [HttpGet]
  [ProducesResponseType(typeof(PlayHistoryListDto), StatusCodes.Status200OK)]
  public async Task<ActionResult<PlayHistoryListDto>> GetRecent([FromQuery] int? limit = null, [FromQuery] int? offset = null)
  {
    try
    {
      int take = Math.Clamp(limit ?? 20, 1, 100);
      int skip = offset ?? 0;

      // Fetch enough entries to handle offset
      int fetchCount = skip + take;
      var entries = await _playHistoryRepository.GetRecentAsync(fetchCount);

      int totalCount = entries.Count;
      if (entries.Count >= fetchCount)
      {
        // There may be more entries
        totalCount = fetchCount + 1;
      }

      var pagedEntries = entries.Skip(skip).Take(take);

      return Ok(new PlayHistoryListDto
      {
        Items = pagedEntries.Select(MapToDto).ToList(),
        TotalCount = totalCount
      });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting recent play history");
      return StatusCode(500, new { error = "Failed to get play history" });
    }
  }

  /// <summary>
  /// Gets play history entries by date range.
  /// </summary>
  /// <param name="start">Start date (ISO 8601 format).</param>
  /// <param name="end">End date (ISO 8601 format).</param>
  /// <returns>A list of play history entries in the date range.</returns>
  [HttpGet("range")]
  [ProducesResponseType(typeof(List<PlayHistoryEntryDto>), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<ActionResult<List<PlayHistoryEntryDto>>> GetByDateRange(
    [FromQuery] DateTime start,
    [FromQuery] DateTime end)
  {
    try
    {
      if (end < start)
      {
        return BadRequest(new { error = "End date must be after start date" });
      }

      var entries = await _playHistoryRepository.GetByDateRangeAsync(start, end);
      return Ok(entries.Select(MapToDto).ToList());
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting play history by date range");
      return StatusCode(500, new { error = "Failed to get play history" });
    }
  }

  /// <summary>
  /// Gets play history entries for a specific date.
  /// </summary>
  /// <param name="date">The date (yyyy-MM-dd).</param>
  /// <param name="limit">Number of entries to retrieve.</param>
  /// <param name="offset">Number of entries to skip.</param>
  /// <returns>A list of play history entries for the specified date.</returns>
  [HttpGet("date/{date}")]
  [ProducesResponseType(typeof(PlayHistoryListDto), StatusCodes.Status200OK)]
  public async Task<ActionResult<PlayHistoryListDto>> GetByDate(
    DateTime date,
    [FromQuery] int? limit = null,
    [FromQuery] int? offset = null)
  {
    try
    {
      // Get full day range
      var start = date.Date;
      var end = start.AddDays(1);
      
      var entries = await _playHistoryRepository.GetByDateRangeAsync(start, end);
      
      // Apply pagination in memory since repository doesn't support it for date range yet
      var query = entries.AsEnumerable();
      var totalCount = query.Count();
      
      if (offset.HasValue)
      {
        query = query.Skip(offset.Value);
      }
      
      if (limit.HasValue)
      {
        query = query.Take(limit.Value);
      }
      
      return Ok(new PlayHistoryListDto
      {
        Items = query.Select(MapToDto).ToList(),
        TotalCount = totalCount
      });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting play history by date");
      return StatusCode(500, new { error = "Failed to get play history" });
    }
  }

  /// <summary>
  /// Gets play history entries for today.
  /// </summary>
  /// <returns>A list of play history entries from today.</returns>
  [HttpGet("today")]
  [ProducesResponseType(typeof(List<PlayHistoryEntryDto>), StatusCodes.Status200OK)]
  public async Task<ActionResult<List<PlayHistoryEntryDto>>> GetToday()
  {
    try
    {
      var today = DateTime.UtcNow.Date;
      var tomorrow = today.AddDays(1);
      var entries = await _playHistoryRepository.GetByDateRangeAsync(today, tomorrow);
      return Ok(entries.Select(MapToDto).ToList());
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting today's play history");
      return StatusCode(500, new { error = "Failed to get play history" });
    }
  }

  /// <summary>
  /// Gets play history entries by source type.
  /// </summary>
  /// <param name="source">The source type (Vinyl, Radio, File, Spotify).</param>
  /// <param name="limit">Number of entries to retrieve (default 20, max 100).</param>
  /// <param name="offset">Number of entries to skip.</param>
  /// <returns>A list of play history entries for the specified source.</returns>
  [HttpGet("source/{source}")]
  [ProducesResponseType(typeof(PlayHistoryListDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<ActionResult<PlayHistoryListDto>> GetBySource(
    string source,
    [FromQuery] int? limit = null,
    [FromQuery] int? offset = null)
  {
    try
    {
      if (!Enum.TryParse<PlaySource>(source, true, out var playSource))
      {
        return BadRequest(new { error = $"Invalid source type: {source}" });
      }

      // Default limit if not specified
      int take = limit ?? 20;
      take = Math.Clamp(take, 1, 100);
      
      // Since repository only supports count, we need to fetch enough to handle offset
      // This is inefficient but works without changing repository interface
      // Ideally repository should support skip/take
      int fetchCount = (offset ?? 0) + take;
      
      var entries = await _playHistoryRepository.GetBySourceAsync(playSource, fetchCount);
      var totalCount = entries.Count; // This is approximate since we limited the fetch
      
      // If we got fewer items than requested, we know the exact total
      if (entries.Count < fetchCount)
      {
        totalCount = entries.Count;
      }
      else
      {
        // Otherwise assume there might be more
        // For accurate pagination we'd need a count query in repository
        // For now, we'll return a larger number if we hit the limit to enable "Next" button
        totalCount = fetchCount + 1;
      }

      var pagedEntries = entries.Skip(offset ?? 0).Take(take);
      
      return Ok(new PlayHistoryListDto
      {
        Items = pagedEntries.Select(MapToDto).ToList(),
        TotalCount = totalCount
      });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting play history by source");
      return StatusCode(500, new { error = "Failed to get play history" });
    }
  }

  /// <summary>
  /// Search play history.
  /// </summary>
  /// <param name="q">Search query.</param>
  /// <param name="limit">Number of entries to retrieve.</param>
  /// <param name="offset">Number of entries to skip.</param>
  /// <returns>A list of matching play history entries.</returns>
  [HttpGet("search")]
  [ProducesResponseType(typeof(PlayHistoryListDto), StatusCodes.Status200OK)]
  public async Task<ActionResult<PlayHistoryListDto>> Search(
    [FromQuery] string q,
    [FromQuery] int? limit = null,
    [FromQuery] int? offset = null)
  {
    try
    {
      if (string.IsNullOrWhiteSpace(q))
      {
        return BadRequest(new { error = "Search query is required" });
      }

      // Use repository search method for efficient SQL-based search
      var (items, totalCount) = await _playHistoryRepository.SearchAsync(
        q,
        limit,
        offset);

      return Ok(new PlayHistoryListDto
      {
        Items = items.Select(MapToDto).ToList(),
        TotalCount = totalCount
      });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error searching play history");
      return StatusCode(500, new { error = "Failed to search history" });
    }
  }

  /// <summary>
  /// Gets a specific play history entry by ID.
  /// </summary>
  /// <param name="id">The entry ID.</param>
  /// <returns>The play history entry.</returns>
  [HttpGet("{id}")]
  [ProducesResponseType(typeof(PlayHistoryEntryDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<ActionResult<PlayHistoryEntryDto>> GetById(string id)
  {
    try
    {
      var entry = await _playHistoryRepository.GetByIdAsync(id);
      if (entry == null)
      {
        return NotFound(new { error = $"Play history entry not found: {id}" });
      }

      return Ok(MapToDto(entry));
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting play history entry {Id}", id);
      return StatusCode(500, new { error = "Failed to get play history entry" });
    }
  }

  /// <summary>
  /// Gets play statistics.
  /// </summary>
  /// <returns>Play statistics including totals, top artists, and top tracks.</returns>
  [HttpGet("statistics")]
  [ProducesResponseType(typeof(PlayStatisticsDto), StatusCodes.Status200OK)]
  public async Task<ActionResult<PlayStatisticsDto>> GetStatistics()
  {
    try
    {
      var stats = await _playHistoryRepository.GetStatisticsAsync();
      return Ok(new PlayStatisticsDto
      {
        TotalPlays = stats.TotalPlays,
        IdentifiedPlays = stats.IdentifiedPlays,
        UnidentifiedPlays = stats.UnidentifiedPlays,
        PlaysBySource = stats.PlaysBySource.ToDictionary(
          kvp => kvp.Key.ToString(),
          kvp => kvp.Value),
        TopArtists = stats.TopArtists.Select(a => new ArtistPlayCountDto
        {
          Artist = a.Artist,
          PlayCount = a.PlayCount
        }).ToList(),
        TopTracks = stats.TopTracks.Select(t => new TrackPlayCountDto
        {
          Title = t.Title,
          Artist = t.Artist,
          PlayCount = t.PlayCount
        }).ToList()
      });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting play statistics");
      return StatusCode(500, new { error = "Failed to get statistics" });
    }
  }

  /// <summary>
  /// Records a play history entry.
  /// Priority: Spotify and File metadata are prioritized over Fingerprinting.
  /// Duplicate entries within 5 minutes for the same track are prevented.
  /// </summary>
  /// <param name="request">The play recording request.</param>
  /// <returns>The created play history entry.</returns>
  [HttpPost]
  [ProducesResponseType(typeof(PlayHistoryEntryDto), StatusCodes.Status201Created)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  public async Task<ActionResult<PlayHistoryEntryDto>> RecordPlay([FromBody] RecordPlayRequest request)
  {
    try
    {
      if (!Enum.TryParse<PlaySource>(request.Source, true, out var playSource))
      {
        return BadRequest(new { error = $"Invalid source type: {request.Source}" });
      }

      MetadataSource? metadataSource = null;
      if (!string.IsNullOrEmpty(request.MetadataSource) &&
          Enum.TryParse<MetadataSource>(request.MetadataSource, true, out var ms))
      {
        metadataSource = ms;
      }

      // Check for duplicate entries if we have track info
      if (!string.IsNullOrEmpty(request.Title) && !string.IsNullOrEmpty(request.Artist))
      {
        var isDuplicate = await _playHistoryRepository.ExistsRecentlyPlayedAsync(
          request.Title,
          request.Artist,
          withinMinutes: 5);

        if (isDuplicate)
        {
          _logger.LogInformation(
            "Duplicate play suppressed: {Title} by {Artist}",
            request.Title,
            request.Artist);
          return Conflict(new { error = "Track was recently played, duplicate suppressed" });
        }
      }

      string? trackMetadataId = null;
      TrackMetadata? trackMetadata = null;

      // Create track metadata if we have title and artist
      if (!string.IsNullOrEmpty(request.Title) && !string.IsNullOrEmpty(request.Artist))
      {
        trackMetadata = new TrackMetadata
        {
          Id = Guid.NewGuid().ToString(),
          Title = request.Title,
          Artist = request.Artist,
          Album = request.Album,
          Source = metadataSource ?? MetadataSource.Manual,
          CreatedAt = DateTime.UtcNow,
          UpdatedAt = DateTime.UtcNow
        };

        await _metadataRepository.StoreAsync(trackMetadata);
        trackMetadataId = trackMetadata.Id;
      }

      var entry = new PlayHistoryEntry
      {
        Id = Guid.NewGuid().ToString(),
        TrackMetadataId = trackMetadataId,
        PlayedAt = DateTime.UtcNow,
        Source = playSource,
        MetadataSource = metadataSource,
        SourceDetails = request.SourceDetails,
        DurationSeconds = request.DurationSeconds,
        WasIdentified = trackMetadata != null
      };

      await _playHistoryRepository.RecordPlayAsync(entry);

      _logger.LogInformation(
        "Recorded play: {Source} - {Title} by {Artist} (MetadataSource: {MetadataSource})",
        entry.Source,
        request.Title ?? "Unknown",
        request.Artist ?? "Unknown",
        metadataSource?.ToString() ?? "None");

      var dto = MapToDto(entry with { Track = trackMetadata });
      return CreatedAtAction(nameof(GetById), new { id = entry.Id }, dto);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error recording play");
      return StatusCode(500, new { error = "Failed to record play" });
    }
  }

  /// <summary>
  /// Deletes a play history entry.
  /// </summary>
  /// <param name="id">The entry ID to delete.</param>
  /// <returns>No content if successful.</returns>
  [HttpDelete("{id}")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<IActionResult> Delete(string id)
  {
    try
    {
      var deleted = await _playHistoryRepository.DeleteAsync(id);
      if (!deleted)
      {
        return NotFound(new { error = $"Play history entry not found: {id}" });
      }

      return NoContent();
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error deleting play history entry {Id}", id);
      return StatusCode(500, new { error = "Failed to delete play history entry" });
    }
  }

  private static PlayHistoryEntryDto MapToDto(PlayHistoryEntry entry)
  {
    return new PlayHistoryEntryDto
    {
      Id = entry.Id,
      PlayedAt = entry.PlayedAt,
      Source = entry.Source.ToString(),
      MetadataSource = entry.MetadataSource?.ToString(),
      SourceDetails = entry.SourceDetails,
      DurationSeconds = entry.DurationSeconds,
      IdentificationConfidence = entry.IdentificationConfidence,
      WasIdentified = entry.WasIdentified,
      Track = entry.Track != null ? new TrackMetadataDto
      {
        Title = entry.Track.Title,
        Artist = entry.Track.Artist,
        Album = entry.Track.Album,
        AlbumArtist = entry.Track.AlbumArtist,
        CoverArtUrl = entry.Track.CoverArtUrl
      } : null
    };
  }
}

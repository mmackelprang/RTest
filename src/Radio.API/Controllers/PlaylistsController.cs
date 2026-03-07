using Microsoft.AspNetCore.Mvc;
using Radio.API.Extensions;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;

namespace Radio.API.Controllers;

/// <summary>
/// REST API for managing saved playlists.
/// </summary>
[ApiController]
[Route("api/playlists")]
public class PlaylistsController : ControllerBase
{
  private readonly IPlaylistRepository _playlistRepository;
  private readonly IAudioEngine _audioEngine;
  private readonly IAudioManager? _audioManager;
  private readonly ILogger<PlaylistsController> _logger;

  public PlaylistsController(
    IPlaylistRepository playlistRepository,
    IAudioEngine audioEngine,
    ILogger<PlaylistsController> logger,
    IAudioManager? audioManager = null)
  {
    _playlistRepository = playlistRepository;
    _audioEngine = audioEngine;
    _audioManager = audioManager;
    _logger = logger;
  }

  /// <summary>
  /// Lists all saved playlists.
  /// </summary>
  [HttpGet]
  [ProducesResponseType(typeof(List<PlaylistSummaryDto>), StatusCodes.Status200OK)]
  public async Task<ActionResult<List<PlaylistSummaryDto>>> GetAll(CancellationToken ct)
  {
    try
    {
      var playlists = await _playlistRepository.GetAllAsync(ct);
      return Ok(playlists.Select(p => new PlaylistSummaryDto
      {
        Id = p.Id,
        Name = p.Name,
        Description = p.Description,
        ItemCount = p.ItemCount,
        CreatedAt = p.CreatedAt.ToString("O"),
        ModifiedAt = p.ModifiedAt.ToString("O")
      }).ToList());
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to list playlists");
      return StatusCode(500, new { error = "Failed to list playlists" });
    }
  }

  /// <summary>
  /// Gets a playlist by ID with its items.
  /// </summary>
  [HttpGet("{id}")]
  [ProducesResponseType(typeof(PlaylistDetailDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<ActionResult<PlaylistDetailDto>> GetById(string id, CancellationToken ct)
  {
    try
    {
      var (playlist, items) = await _playlistRepository.GetByIdAsync(id, ct);
      if (playlist == null)
      {
        return NotFound();
      }

      return Ok(new PlaylistDetailDto
      {
        Id = playlist.Id,
        Name = playlist.Name,
        Description = playlist.Description,
        ItemCount = playlist.ItemCount,
        CreatedAt = playlist.CreatedAt.ToString("O"),
        ModifiedAt = playlist.ModifiedAt.ToString("O"),
        Items = items.Select(i => new PlaylistItemDto
        {
          FilePath = i.FilePath,
          Title = i.Title,
          Artist = i.Artist,
          Album = i.Album,
          DurationMs = i.DurationMs
        }).ToList()
      });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get playlist {Id}", id);
      return StatusCode(500, new { error = "Failed to get playlist" });
    }
  }

  /// <summary>
  /// Creates a new playlist from the current queue.
  /// </summary>
  [HttpPost]
  [ProducesResponseType(typeof(PlaylistSummaryDto), StatusCodes.Status201Created)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<ActionResult<PlaylistSummaryDto>> Create(
    [FromBody] CreatePlaylistRequest request, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(request.Name))
    {
      return BadRequest(new { error = "Playlist name is required" });
    }

    try
    {
      // Get current queue items from the active source (or file player)
      var primarySource = _audioManager?.ActiveSource ?? _audioEngine.GetActivePrimaryAudioSource();
      if (primarySource is not IPlayQueue playQueue)
      {
        if (_audioManager != null)
        {
          primarySource = await _audioManager.GetOrCreateSourceAsync(
            AudioSourceType.FilePlayer, switchToSource: false, ct);
        }
        if (primarySource is not IPlayQueue fileQueue)
        {
          return BadRequest(new { error = "No queue-capable audio source available" });
        }

        playQueue = fileQueue;
      }

      var queueItems = await playQueue.GetQueueAsync(ct);
      if (queueItems == null || queueItems.Count == 0)
      {
        return BadRequest(new { error = "Queue is empty" });
      }

      var playlistItems = queueItems.Select((q, i) => new PlaylistItem
      {
        Id = Guid.NewGuid().ToString(),
        PlaylistId = string.Empty,
        Position = i,
        FilePath = q.Id, // QueueItem.Id is the file path for file-based sources
        Title = q.Title,
        Artist = q.Artist,
        Album = q.Album,
        DurationMs = q.Duration.HasValue ? (int)q.Duration.Value.TotalMilliseconds : null
      }).ToList();

      var playlist = await _playlistRepository.CreateAsync(
        request.Name, request.Description, playlistItems, ct);

      var dto = new PlaylistSummaryDto
      {
        Id = playlist.Id,
        Name = playlist.Name,
        Description = playlist.Description,
        ItemCount = playlist.ItemCount,
        CreatedAt = playlist.CreatedAt.ToString("O"),
        ModifiedAt = playlist.ModifiedAt.ToString("O")
      };

      return CreatedAtAction(nameof(GetById), new { id = playlist.Id }, dto);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to create playlist");
      return StatusCode(500, new { error = "Failed to create playlist" });
    }
  }

  /// <summary>
  /// Deletes a playlist.
  /// </summary>
  [HttpDelete("{id}")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<IActionResult> Delete(string id, CancellationToken ct)
  {
    try
    {
      var deleted = await _playlistRepository.DeleteAsync(id, ct);
      return deleted ? NoContent() : NotFound();
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to delete playlist {Id}", id);
      return StatusCode(500, new { error = "Failed to delete playlist" });
    }
  }

  /// <summary>
  /// Loads a playlist into the current queue (replaces current queue).
  /// </summary>
  [HttpPost("{id}/load")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<IActionResult> Load(string id, CancellationToken ct)
  {
    try
    {
      var (playlist, items) = await _playlistRepository.GetByIdAsync(id, ct);
      if (playlist == null)
      {
        return NotFound();
      }

      var primarySource = _audioManager?.ActiveSource ?? _audioEngine.GetActivePrimaryAudioSource();
      if (primarySource is not IPlayQueue playQueue)
      {
        // Active source doesn't support a queue — switch to file player
        if (_audioManager != null)
        {
          _logger.LogInformation("Active source is not a queue, switching to file player for playlist load");
          primarySource = await _audioManager.GetOrCreateSourceAsync(
            AudioSourceType.FilePlayer, switchToSource: true, ct);
        }

        if (primarySource is not IPlayQueue fileQueue)
        {
          return BadRequest(new { error = "No queue-capable audio source available" });
        }

        playQueue = fileQueue;
      }

      // Clear current queue
      await playQueue.ClearQueueAsync(ct);

      // Add each item from the playlist, skipping files that no longer exist
      var loaded = 0;
      var skipped = 0;
      foreach (var item in items.OrderBy(i => i.Position))
      {
        if (!System.IO.File.Exists(item.FilePath))
        {
          _logger.LogWarning("Playlist file not found, skipping: {FilePath}", item.FilePath);
          skipped++;
          continue;
        }

        await playQueue.AddToQueueAsync(item.FilePath, cancellationToken: ct);
        loaded++;
      }

      _logger.LogInformation("Loaded playlist '{Name}': {Loaded} tracks loaded, {Skipped} skipped (missing)",
        playlist.Name, loaded, skipped);

      return Ok(new
      {
        message = skipped == 0
          ? $"Loaded {loaded} tracks from '{playlist.Name}'"
          : $"Loaded {loaded} tracks from '{playlist.Name}' ({skipped} files not found)",
        loaded,
        skipped
      });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to load playlist {Id}", id);
      return StatusCode(500, new { error = "Failed to load playlist" });
    }
  }
}

// DTOs
public class PlaylistSummaryDto
{
  public string Id { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public string? Description { get; set; }
  public int ItemCount { get; set; }
  public string CreatedAt { get; set; } = string.Empty;
  public string ModifiedAt { get; set; } = string.Empty;
}

public class PlaylistDetailDto : PlaylistSummaryDto
{
  public List<PlaylistItemDto> Items { get; set; } = new();
}

public class PlaylistItemDto
{
  public string FilePath { get; set; } = string.Empty;
  public string? Title { get; set; }
  public string? Artist { get; set; }
  public string? Album { get; set; }
  public int? DurationMs { get; set; }
}

public class CreatePlaylistRequest
{
  public string Name { get; set; } = string.Empty;
  public string? Description { get; set; }
}

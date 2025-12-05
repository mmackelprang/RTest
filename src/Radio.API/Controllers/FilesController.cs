using Microsoft.AspNetCore.Mvc;
using Radio.API.Extensions;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.API.Models;
using Radio.Infrastructure.Audio.Sources.Primary;

namespace Radio.API.Controllers;

/// <summary>
/// Controller for managing audio files and file playback.
/// Provides endpoints for browsing, listing, and playing audio files.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class FilesController : ControllerBase
{
  private readonly ILogger<FilesController> _logger;
  private readonly IFileBrowser _fileBrowser;
  private readonly IAudioEngine _audioEngine;

  /// <summary>
  /// Initializes a new instance of the <see cref="FilesController"/> class.
  /// </summary>
  public FilesController(
    ILogger<FilesController> logger,
    IFileBrowser fileBrowser,
    IAudioEngine audioEngine)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _fileBrowser = fileBrowser ?? throw new ArgumentNullException(nameof(fileBrowser));
    _audioEngine = audioEngine ?? throw new ArgumentNullException(nameof(audioEngine));
  }

  /// <summary>
  /// Lists audio files in the specified directory.
  /// </summary>
  /// <param name="path">Optional path relative to the configured root directory. Empty for root.</param>
  /// <param name="recursive">If true, searches subdirectories recursively. Default is false.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A list of audio file information.</returns>
  /// <response code="200">Returns the list of audio files.</response>
  /// <response code="500">If an error occurs while listing files.</response>
  [HttpGet]
  [ProducesResponseType(typeof(IEnumerable<AudioFileInfoDto>), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status500InternalServerError)]
  public async Task<IActionResult> ListFiles(
    [FromQuery] string? path = null,
    [FromQuery] bool recursive = false,
    CancellationToken cancellationToken = default)
  {
    try
    {
      _logger.LogInformation("Listing audio files from path: {Path}, recursive: {Recursive}", 
        path ?? "(root)", recursive);

      var files = await _fileBrowser.ListFilesAsync(path, recursive, cancellationToken);
      var dtos = files.Select(MapToDto).ToList();

      _logger.LogInformation("Found {Count} audio files", dtos.Count);
      return Ok(dtos);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error listing audio files from path: {Path}", path);
      return StatusCode(500, new { error = "Failed to list audio files", details = ex.Message });
    }
  }

  /// <summary>
  /// Plays a specific audio file.
  /// </summary>
  /// <param name="request">The play file request containing the file path.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>Success status and playback information.</returns>
  /// <response code="200">File is now playing.</response>
  /// <response code="400">If the file path is invalid or file not found.</response>
  /// <response code="500">If an error occurs while starting playback.</response>
  [HttpPost("play")]
  [ProducesResponseType(typeof(PlayFileResponseDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status500InternalServerError)]
  public async Task<IActionResult> PlayFile(
    [FromBody] PlayFileRequestDto request,
    CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(request.Path))
    {
      return BadRequest(new { error = "File path is required" });
    }

    try
    {
      _logger.LogInformation("Playing audio file: {Path}", request.Path);

      // Verify file exists
      var fileInfo = await _fileBrowser.GetFileInfoAsync(request.Path, cancellationToken);
      if (fileInfo == null)
      {
        _logger.LogWarning("File not found or not supported: {Path}", request.Path);
        return BadRequest(new { error = "File not found or not a supported audio format" });
      }

      // Get or activate File Player source
      var filePlayerSource = await GetOrActivateFilePlayerSourceAsync(cancellationToken);
      if (filePlayerSource == null)
      {
        return StatusCode(500, new { error = "Failed to activate File Player source" });
      }

      // Cast to FilePlayerAudioSource to access LoadFileAsync
      if (filePlayerSource is not FilePlayerAudioSource filePlayer)
      {
        return StatusCode(500, new { error = "File Player source is not of the expected type" });
      }

      // Load and play the file
      await filePlayer.LoadFileAsync(request.Path, cancellationToken);
      await filePlayer.PlayAsync(cancellationToken);

      _logger.LogInformation("Now playing: {Path}", request.Path);

      return Ok(new PlayFileResponseDto
      {
        Success = true,
        Message = "File is now playing",
        FilePath = request.Path,
        FileName = fileInfo.FileName,
        Title = fileInfo.Title,
        Artist = fileInfo.Artist,
        Album = fileInfo.Album,
        Duration = fileInfo.Duration
      });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error playing audio file: {Path}", request.Path);
      return StatusCode(500, new { error = "Failed to play audio file", details = ex.Message });
    }
  }

  /// <summary>
  /// Adds audio files to the playback queue.
  /// </summary>
  /// <param name="request">The queue request containing file paths to add.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>Success status and queue information.</returns>
  /// <response code="200">Files added to queue successfully.</response>
  /// <response code="400">If the request is invalid or no files specified.</response>
  /// <response code="500">If an error occurs while adding files to queue.</response>
  [HttpPost("queue")]
  [ProducesResponseType(typeof(QueueFilesResponseDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status500InternalServerError)]
  public async Task<IActionResult> QueueFiles(
    [FromBody] QueueFilesRequestDto request,
    CancellationToken cancellationToken = default)
  {
    if (request.Paths == null || !request.Paths.Any())
    {
      return BadRequest(new { error = "At least one file path is required" });
    }

    try
    {
      _logger.LogInformation("Adding {Count} files to queue", request.Paths.Count);

      // Get or activate File Player source
      var filePlayerSource = await GetOrActivateFilePlayerSourceAsync(cancellationToken);
      if (filePlayerSource == null)
      {
        return StatusCode(500, new { error = "Failed to activate File Player source" });
      }

      // Check if source supports queue
      if (!filePlayerSource.SupportsQueue || filePlayerSource is not IPlayQueue playQueue)
      {
        return BadRequest(new { error = "File Player source does not support queue operations" });
      }

      var addedCount = 0;
      var failedPaths = new List<string>();

      foreach (var path in request.Paths)
      {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
          // Verify file exists
          var fileInfo = await _fileBrowser.GetFileInfoAsync(path, cancellationToken);
          if (fileInfo == null)
          {
            _logger.LogWarning("Skipping file (not found or not supported): {Path}", path);
            failedPaths.Add(path);
            continue;
          }

          // Add to queue
          await playQueue.AddToQueueAsync(path, null, cancellationToken);
          addedCount++;
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "Error adding file to queue: {Path}", path);
          failedPaths.Add(path);
        }
      }

      _logger.LogInformation("Added {AddedCount} files to queue, {FailedCount} failed",
        addedCount, failedPaths.Count);

      return Ok(new QueueFilesResponseDto
      {
        Success = true,
        Message = $"Added {addedCount} file(s) to queue",
        AddedCount = addedCount,
        FailedCount = failedPaths.Count,
        FailedPaths = failedPaths
      });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error adding files to queue");
      return StatusCode(500, new { error = "Failed to add files to queue", details = ex.Message });
    }
  }

  /// <summary>
  /// Gets or activates the File Player audio source.
  /// </summary>
  /// <remarks>
  /// TODO: Implement automatic source switching via IAudioManager.SwitchSourceAsync
  /// when the full AudioManager implementation is available (Phase 3).
  /// Currently returns null if File Player is not the active source.
  /// </remarks>
  private async Task<IPrimaryAudioSource?> GetOrActivateFilePlayerSourceAsync(
    CancellationToken cancellationToken)
  {
    // Get the mixer to check active sources
    var primarySource = _audioEngine.GetActivePrimarySource();

    // If File Player is already active, return it
    if (primarySource?.Type == AudioSourceType.FilePlayer)
    {
      return primarySource;
    }

    // TODO: When IAudioManager.SwitchSourceAsync is available, activate File Player here
    _logger.LogWarning("File Player source is not currently active. Automatic source switching requires Phase 3 completion.");
    return null;
  }

  /// <summary>
  /// Maps an AudioFileInfo to AudioFileInfoDto.
  /// </summary>
  private static AudioFileInfoDto MapToDto(AudioFileInfo fileInfo)
  {
    return new AudioFileInfoDto
    {
      Path = fileInfo.Path,
      FileName = fileInfo.FileName,
      Extension = fileInfo.Extension,
      SizeBytes = fileInfo.SizeBytes,
      CreatedAt = fileInfo.CreatedAt,
      LastModifiedAt = fileInfo.LastModifiedAt,
      Title = fileInfo.Title,
      Artist = fileInfo.Artist,
      Album = fileInfo.Album,
      Duration = fileInfo.Duration,
      TrackNumber = fileInfo.TrackNumber,
      Genre = fileInfo.Genre,
      Year = fileInfo.Year
    };
  }
}

using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Radio.API.Extensions;
using Radio.API.Models;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
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
  private readonly IAudioManager? _audioManager;
  private readonly IOptionsMonitor<FilePlayerOptions> _filePlayerOptions;

  /// <summary>
  /// Initializes a new instance of the <see cref="FilesController"/> class.
  /// </summary>
  public FilesController(
    ILogger<FilesController> logger,
    IFileBrowser fileBrowser,
    IAudioEngine audioEngine,
    IOptionsMonitor<FilePlayerOptions> filePlayerOptions,
    IAudioManager? audioManager = null)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _fileBrowser = fileBrowser ?? throw new ArgumentNullException(nameof(fileBrowser));
    _audioEngine = audioEngine ?? throw new ArgumentNullException(nameof(audioEngine));
    _filePlayerOptions = filePlayerOptions ?? throw new ArgumentNullException(nameof(filePlayerOptions));
    _audioManager = audioManager;
  }

  /// <summary>
  /// Lists audio files and directories in the specified directory.
  /// </summary>
  /// <param name="path">Optional path relative to the configured root directory. Empty for root.</param>
  /// <param name="absolutePath">Optional absolute filesystem path. When provided, bypasses the configured root and enumerates directly.</param>
  /// <param name="recursive">If true, searches subdirectories recursively. Default is false.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A list of files and directories.</returns>
  /// <response code="200">Returns the list of files and directories.</response>
  /// <response code="404">If the specified absolute path does not exist.</response>
  /// <response code="500">If an error occurs while listing files.</response>
  [HttpGet]
  [ProducesResponseType(typeof(FileListDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  [ProducesResponseType(StatusCodes.Status500InternalServerError)]
  public async Task<IActionResult> ListFiles(
    [FromQuery] string? path = null,
    [FromQuery] string? absolutePath = null,
    [FromQuery] bool recursive = false,
    CancellationToken cancellationToken = default)
  {
    // Absolute path mode: bypass _fileBrowser and enumerate directly
    if (!string.IsNullOrWhiteSpace(absolutePath))
    {
      if (!IsPathAllowed(absolutePath))
      {
        _logger.LogWarning("Rejected absolute path access: {Path}", absolutePath);
        return BadRequest(new { error = "Path is not within an allowed directory" });
      }
      return await ListFilesAbsolute(absolutePath, cancellationToken);
    }

    try
    {
      var currentPath = path ?? "/";
      _logger.LogDebug("Listing files from path: {Path}, recursive: {Recursive}",
        currentPath, recursive);

      var items = new List<FileItemDto>();

      // Get directories (unless recursive mode, which returns flat file list)
      if (!recursive)
      {
        var directories = _fileBrowser.ListDirectories(path);
        foreach (var dir in directories)
        {
          items.Add(new FileItemDto
          {
            Name = System.IO.Path.GetFileName(dir),
            Path = dir,
            IsDirectory = true,
            Size = null,
            Duration = null,
            Artist = null,
            Album = null
          });
        }
      }

      // Get audio files
      var files = await _fileBrowser.ListFilesAsync(path, recursive, cancellationToken);
      foreach (var file in files)
      {
        items.Add(new FileItemDto
        {
          Name = file.FileName,
          Path = file.Path,
          IsDirectory = false,
          Size = file.SizeBytes,
          Duration = FormatDuration(file.Duration),
          Artist = file.Artist,
          Album = file.Album
        });
      }

      _logger.LogDebug("Found {Count} items ({DirCount} directories, {FileCount} files)",
        items.Count,
        items.Count(i => i.IsDirectory),
        items.Count(i => !i.IsDirectory));

      return Ok(new FileListDto
      {
        CurrentPath = currentPath,
        Items = items
      });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error listing files from path: {Path}", path);
      return StatusCode(500, new { error = "Failed to list files", details = ex.Message });
    }
  }

  /// <summary>
  /// Lists files and directories at an absolute filesystem path.
  /// Used for browsing outside the configured media root (e.g., NAS mounts, USB drives).
  /// </summary>
  private async Task<IActionResult> ListFilesAbsolute(
    string absolutePath,
    CancellationToken cancellationToken)
  {
    try
    {
      _logger.LogDebug("Listing files from absolute path: {Path}", absolutePath);

      if (!Directory.Exists(absolutePath))
      {
        return NotFound(new { error = "Directory not found", path = absolutePath });
      }

      var items = new List<FileItemDto>();
      var supportedExtensions = _fileBrowser.GetSupportedExtensions();

      // Enumerate directories
      try
      {
        foreach (var dir in Directory.GetDirectories(absolutePath))
        {
          cancellationToken.ThrowIfCancellationRequested();
          items.Add(new FileItemDto
          {
            Name = System.IO.Path.GetFileName(dir),
            Path = dir,
            IsDirectory = true,
            Size = null,
            Duration = null,
            Artist = null,
            Album = null
          });
        }
      }
      catch (UnauthorizedAccessException)
      {
        _logger.LogDebug("Cannot enumerate subdirectories of {Path} (access denied)", absolutePath);
      }

      // Enumerate audio files
      try
      {
        foreach (var file in Directory.GetFiles(absolutePath))
        {
          cancellationToken.ThrowIfCancellationRequested();
          var ext = System.IO.Path.GetExtension(file);
          if (!supportedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
          {
            continue;
          }

          // Try to get metadata via _fileBrowser, fall back to basic FileInfo
          try
          {
            var fileInfo = await _fileBrowser.GetFileInfoAsync(file, cancellationToken);
            if (fileInfo != null)
            {
              items.Add(new FileItemDto
              {
                Name = fileInfo.FileName,
                Path = file,
                IsDirectory = false,
                Size = fileInfo.SizeBytes,
                Duration = FormatDuration(fileInfo.Duration),
                Artist = fileInfo.Artist,
                Album = fileInfo.Album
              });
              continue;
            }
          }
          catch
          {
            // Fall through to basic FileInfo
          }

          var basicInfo = new FileInfo(file);
          items.Add(new FileItemDto
          {
            Name = basicInfo.Name,
            Path = file,
            IsDirectory = false,
            Size = basicInfo.Length,
            Duration = null,
            Artist = null,
            Album = null
          });
        }
      }
      catch (UnauthorizedAccessException)
      {
        _logger.LogDebug("Cannot enumerate files in {Path} (access denied)", absolutePath);
      }

      _logger.LogDebug("Found {Count} items at absolute path {Path} ({DirCount} directories, {FileCount} files)",
        items.Count, absolutePath,
        items.Count(i => i.IsDirectory),
        items.Count(i => !i.IsDirectory));

      return Ok(new FileListDto
      {
        CurrentPath = absolutePath,
        Items = items
      });
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      _logger.LogError(ex, "Error listing files from absolute path: {Path}", absolutePath);
      return StatusCode(500, new { error = "Failed to list files", details = ex.Message });
    }
  }

  /// <summary>
  /// Formats a TimeSpan duration as a human-readable string (e.g., "3:45").
  /// </summary>
  private static string? FormatDuration(TimeSpan? duration)
  {
    if (duration == null)
    {
      return null;
    }

    var ts = duration.Value;
    if (ts.Hours > 0)
    {
      return $"{ts.Hours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
    }
    return $"{ts.Minutes}:{ts.Seconds:D2}";
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

    // Validate absolute paths against allowed directories
    if (Path.IsPathRooted(request.Path) && !IsPathAllowed(request.Path))
    {
      _logger.LogWarning("Rejected play file path: {Path}", request.Path);
      return BadRequest(new { error = "Path is not within an allowed directory" });
    }

    try
    {
      _logger.LogInformation("Playing audio file: {Path}", request.Path);

      // Verify file exists — absolute paths bypass FileBrowser's relative-only GetFullPath
      AudioFileInfo? fileInfo = null;
      if (Path.IsPathRooted(request.Path))
      {
        if (!System.IO.File.Exists(request.Path) || !_fileBrowser.IsSupportedAudioFile(request.Path))
        {
          _logger.LogWarning("File not found or not supported: {Path}", request.Path);
          return BadRequest(new { error = "File not found or not a supported audio format" });
        }
      }
      else
      {
        fileInfo = await _fileBrowser.GetFileInfoAsync(request.Path, cancellationToken);
        if (fileInfo == null)
        {
          _logger.LogWarning("File not found or not supported: {Path}", request.Path);
          return BadRequest(new { error = "File not found or not a supported audio format" });
        }
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
        FileName = fileInfo?.FileName ?? Path.GetFileName(request.Path),
        Title = fileInfo?.Title,
        Artist = fileInfo?.Artist,
        Album = fileInfo?.Album,
        Duration = fileInfo?.Duration
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
          // Absolute paths (from bookmark/drive browsing) bypass FileBrowser's
          // relative-path-only GetFullPath. Validate with IsPathAllowed + direct checks.
          if (Path.IsPathRooted(path))
          {
            if (!IsPathAllowed(path))
            {
              _logger.LogWarning("Rejected absolute queue path: {Path}", path);
              failedPaths.Add(path);
              continue;
            }
            if (!System.IO.File.Exists(path) || !_fileBrowser.IsSupportedAudioFile(path))
            {
              _logger.LogWarning("Skipping file (not found or not supported): {Path}", path);
              failedPaths.Add(path);
              continue;
            }
          }
          else
          {
            // Relative paths go through FileBrowser for resolution + validation
            var fileInfo = await _fileBrowser.GetFileInfoAsync(path, cancellationToken);
            if (fileInfo == null)
            {
              _logger.LogWarning("Skipping file (not found or not supported): {Path}", path);
              failedPaths.Add(path);
              continue;
            }
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
        Success = addedCount > 0,
        Message = addedCount > 0
          ? $"Added {addedCount} file(s) to queue"
          : $"Failed to add files to queue ({failedPaths.Count} not found or not supported)",
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
  /// Gets the configured bookmarked directories with accessibility status.
  /// </summary>
  /// <returns>A list of bookmarked directories.</returns>
  [HttpGet("bookmarks")]
  [ProducesResponseType(typeof(List<BookmarkDto>), StatusCodes.Status200OK)]
  public IActionResult GetBookmarks()
  {
    var options = _filePlayerOptions.CurrentValue;
    var bookmarks = options.BookmarkedPaths.Select(b => new BookmarkDto
    {
      Path = b.Path,
      Label = b.Label,
      Tag = b.Tag,
      IsAccessible = Directory.Exists(b.Path)
    }).ToList();

    return Ok(bookmarks);
  }

  /// <summary>
  /// Gets a list of available drives and their information.
  /// </summary>
  /// <returns>A list of available drives with their properties.</returns>
  /// <response code="200">Returns the list of available drives.</response>
  /// <response code="500">If an error occurs while enumerating drives.</response>
  [HttpGet("drives")]
  [ProducesResponseType(typeof(List<DriveInfoDto>), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status500InternalServerError)]
  public IActionResult GetDrives()
  {
    try
    {
      var drives = new List<DriveInfoDto>();

      // Mount point prefixes that represent system internals (not useful for audio browsing)
      var systemPrefixes = new[] { "/dev/", "/run/", "/sys/", "/proc/", "/snap/", "/boot/" };

      // Get all drives using System.IO.DriveInfo
      foreach (var drive in System.IO.DriveInfo.GetDrives())
      {
        try
        {
          // Skip system pseudo-mounts on Linux (e.g., /dev/shm, /run, /dev/hugepages)
          if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
              systemPrefixes.Any(p => drive.Name.StartsWith(p, StringComparison.Ordinal)))
          {
            continue;
          }

          // Only include ready drives to avoid exceptions
          if (!drive.IsReady)
          {
            drives.Add(new DriveInfoDto
            {
              Name = drive.Name,
              Label = drive.Name,
              DriveType = drive.DriveType.ToString(),
              IsReady = false,
              TotalSize = 0,
              AvailableSpace = 0,
              DriveFormat = null
            });
            continue;
          }

          drives.Add(new DriveInfoDto
          {
            Name = drive.Name,
            Label = string.IsNullOrEmpty(drive.VolumeLabel) ? drive.Name : $"{drive.VolumeLabel} ({drive.Name})",
            DriveType = drive.DriveType.ToString(),
            IsReady = true,
            TotalSize = drive.TotalSize,
            AvailableSpace = drive.AvailableFreeSpace,
            DriveFormat = drive.DriveFormat
          });
        }
        catch (UnauthorizedAccessException)
        {
          // Linux FUSE mounts (e.g. /run/user/*/doc) throw when reading stats — skip silently
          _logger.LogDebug("Skipping inaccessible drive {DriveName}", drive.Name);
        }
        catch (IOException)
        {
          _logger.LogDebug("Skipping unavailable drive {DriveName}", drive.Name);
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "Error reading drive info for {DriveName}", drive.Name);
        }
      }

      // On Linux, DriveInfo.GetDrives() misses network/NAS mounts (cifs, nfs, sshfs).
      // Parse /proc/mounts to discover them.
      if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
      {
        var mountDrives = DiscoverLinuxMountPoints();
        // Normalize paths by trimming trailing separator for consistent comparison
        var existingPaths = new HashSet<string>(
          drives.Select(d => d.Name.TrimEnd(Path.DirectorySeparatorChar)),
          StringComparer.Ordinal);

        foreach (var mountDrive in mountDrives)
        {
          // Normalize mount drive name for comparison
          var normalizedName = mountDrive.Name.TrimEnd(Path.DirectorySeparatorChar);
          // Avoid duplicates — DriveInfo may already include some mounts,
          // and mount drives themselves may overlap
          if (!existingPaths.Contains(normalizedName))
          {
            drives.Add(mountDrive);
            existingPaths.Add(normalizedName);
          }
        }
      }

      _logger.LogDebug("Found {Count} drives", drives.Count);
      return Ok(drives);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error enumerating drives");
      return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to enumerate drives" });
    }
  }

  /// <summary>
  /// Parses /proc/mounts to discover user-relevant Linux mount points that
  /// DriveInfo.GetDrives() misses (NAS/CIFS, NFS, SSHFS, USB drives under /mnt or /media).
  /// </summary>
  private List<DriveInfoDto> DiscoverLinuxMountPoints()
  {
    var results = new List<DriveInfoDto>();

    // Filesystem types that represent user-relevant storage
    var relevantFsTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
      "cifs", "nfs", "nfs4", "fuse.sshfs", "ext4", "ext3", "ext2",
      "xfs", "btrfs", "vfat", "exfat", "ntfs", "fuseblk"
    };

    // Mount point prefixes that indicate system internals (exclude these)
    var excludedPrefixes = new[]
    {
      "/sys", "/proc", "/dev", "/run", "/snap", "/boot"
    };

    try
    {
      if (!System.IO.File.Exists("/proc/mounts"))
      {
        _logger.LogDebug("/proc/mounts not found, skipping Linux mount discovery");
        return results;
      }

      var lines = System.IO.File.ReadAllLines("/proc/mounts");
      foreach (var line in lines)
      {
        // Format: device mountpoint fstype options dump pass
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
        {
          continue;
        }

        var device = parts[0];
        var mountPoint = UnescapeOctalPath(parts[1]);
        var fsType = parts[2];

        // Only include relevant filesystem types
        if (!relevantFsTypes.Contains(fsType))
        {
          continue;
        }

        // Only include mounts under /mnt/ or /media/ (user-space mounts)
        if (!mountPoint.StartsWith("/mnt/", StringComparison.Ordinal) &&
            !mountPoint.StartsWith("/media/", StringComparison.Ordinal))
        {
          continue;
        }

        // Exclude system-internal paths that might appear under /mnt
        if (excludedPrefixes.Any(prefix =>
          mountPoint.StartsWith(prefix, StringComparison.Ordinal)))
        {
          continue;
        }

        try
        {
          // Build a friendly label from the mount point directory name
          var dirName = Path.GetFileName(mountPoint.TrimEnd('/'));
          var label = string.IsNullOrEmpty(dirName)
            ? mountPoint
            : $"{dirName} ({mountPoint})";

          // Map filesystem type to a logical DriveType string
          var driveType = fsType switch
          {
            "cifs" or "nfs" or "nfs4" or "fuse.sshfs" => "Network",
            "vfat" or "exfat" => "Removable",
            _ => "Fixed"
          };

          // Try to read space info; network mounts may be slow or throw
          long totalSize = 0;
          long availableSpace = 0;
          bool isReady = false;

          if (Directory.Exists(mountPoint))
          {
            isReady = true;
            try
            {
              var driveInfo = new System.IO.DriveInfo(mountPoint);
              if (driveInfo.IsReady)
              {
                totalSize = driveInfo.TotalSize;
                availableSpace = driveInfo.AvailableFreeSpace;
              }
            }
            catch
            {
              // Space info unavailable — mount is still browsable
            }
          }

          results.Add(new DriveInfoDto
          {
            Name = mountPoint,
            Label = label,
            DriveType = driveType,
            IsReady = isReady,
            TotalSize = totalSize,
            AvailableSpace = availableSpace,
            DriveFormat = fsType
          });

          _logger.LogDebug(
            "Discovered Linux mount: {MountPoint} (type={FsType}, device={Device})",
            mountPoint, fsType, device);
        }
        catch (Exception ex)
        {
          _logger.LogDebug(ex, "Error reading mount point info for {MountPoint}", mountPoint);
        }
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Error parsing /proc/mounts for Linux mount discovery");
    }

    return results;
  }

  /// <summary>
  /// Unescapes octal escape sequences in /proc/mounts paths (e.g., \040 for space,
  /// \011 for tab). Mount points with special characters use this encoding.
  /// </summary>
  private static string UnescapeOctalPath(string path)
  {
    if (!path.Contains('\\'))
    {
      return path;
    }

    var result = new System.Text.StringBuilder(path.Length);
    for (int i = 0; i < path.Length; i++)
    {
      if (path[i] == '\\' && i + 3 < path.Length &&
          path[i + 1] >= '0' && path[i + 1] <= '7' &&
          path[i + 2] >= '0' && path[i + 2] <= '7' &&
          path[i + 3] >= '0' && path[i + 3] <= '7')
      {
        var octalValue = (path[i + 1] - '0') * 64 +
                         (path[i + 2] - '0') * 8 +
                         (path[i + 3] - '0');
        result.Append((char)octalValue);
        i += 3;
      }
      else
      {
        result.Append(path[i]);
      }
    }

    return result.ToString();
  }

  /// <summary>
  /// Gets or activates the File Player audio source.
  /// Uses IAudioManager to create and switch to the FilePlayer source.
  /// </summary>
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

    // Use AudioManager to get or create FilePlayer source
    if (_audioManager != null)
    {
      var source = await _audioManager.GetOrCreateSourceAsync(
        AudioSourceType.FilePlayer,
        switchToSource: true,
        cancellationToken);

      if (source is IPrimaryAudioSource filePlayerSource)
      {
        _logger.LogInformation("Activated File Player source via AudioManager");
        return filePlayerSource;
      }

      _logger.LogWarning("AudioManager returned a non-primary source for FilePlayer");
      return null;
    }

    // Fallback: AudioManager not available
    _logger.LogWarning("IAudioManager not available, cannot activate File Player source");
    return null;
  }

  /// <summary>
  /// Validates that a path falls within an allowed directory (media root or configured browse directories).
  /// Prevents path traversal attacks via ".." segments or symlinks.
  /// </summary>
  private bool IsPathAllowed(string path)
  {
    try
    {
      // Resolve to absolute path, eliminating ".." segments and symlinks
      var resolvedPath = Path.GetFullPath(path);

      // Check against configured media root
      var options = _filePlayerOptions.CurrentValue;
      var mediaRoot = Path.GetFullPath(
        string.IsNullOrEmpty(options.RootDirectory) ? "." : options.RootDirectory);
      // Ensure trailing separator for prefix matching to prevent /mnt/nasty matching /mnt/nas
      var mediaRootPrefix = mediaRoot.EndsWith(Path.DirectorySeparatorChar)
        ? mediaRoot
        : mediaRoot + Path.DirectorySeparatorChar;
      if (resolvedPath.StartsWith(mediaRootPrefix, StringComparison.OrdinalIgnoreCase)
          || resolvedPath.Equals(mediaRoot, StringComparison.OrdinalIgnoreCase))
      {
        return true;
      }

      // Check against additional allowed browse directories and bookmarked paths
      var allowedPaths = options.AllowedBrowseDirectories
        .Concat(options.BookmarkedPaths.Select(b => b.Path));

      foreach (var allowedDir in allowedPaths)
      {
        if (string.IsNullOrWhiteSpace(allowedDir))
        {
          continue;
        }

        var resolvedAllowed = Path.GetFullPath(allowedDir);
        // Ensure trailing separator for prefix matching
        var prefix = resolvedAllowed.EndsWith(Path.DirectorySeparatorChar)
          ? resolvedAllowed
          : resolvedAllowed + Path.DirectorySeparatorChar;
        if (resolvedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || resolvedPath.Equals(resolvedAllowed, StringComparison.OrdinalIgnoreCase))
        {
          return true;
        }
      }

      return false;
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Error validating path: {Path}", path);
      return false;
    }
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

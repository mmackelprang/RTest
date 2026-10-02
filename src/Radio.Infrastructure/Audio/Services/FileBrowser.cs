using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Core.Utilities;
using Radio.Metrics;
using System.Diagnostics;

namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// File browser service for discovering and listing audio files.
/// Integrates with IAudioFileRepository to track new, modified, and removed files.
/// </summary>
public class FileBrowser : IFileBrowser
{
  private readonly ILogger<FileBrowser> _logger;
  private readonly IOptionsMonitor<FilePlayerOptions> _options;
  private readonly IMetricsCollector? _metricsCollector;
  private readonly IAudioFileRepository? _audioFileRepository;
  private readonly string _rootDir;

  // Supported audio file extensions
  private static readonly string[] SupportedExtensions = new[]
  {
    ".mp3", ".flac", ".wav", ".ogg", ".aac", ".m4a", ".wma"
  };

  /// <summary>
  /// Initializes a new instance of the <see cref="FileBrowser"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="options">The file player options.</param>
  /// <param name="rootDir">The root directory for resolving relative paths.</param>
  /// <param name="metricsCollector">Optional metrics collector for tracking scan operations.</param>
  /// <param name="audioFileRepository">Optional repository for persisting audio file state.</param>
  public FileBrowser(
    ILogger<FileBrowser> logger,
    IOptionsMonitor<FilePlayerOptions> options,
    string rootDir,
    IMetricsCollector? metricsCollector = null,
    IAudioFileRepository? audioFileRepository = null)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _options = options ?? throw new ArgumentNullException(nameof(options));
    _rootDir = rootDir ?? throw new ArgumentNullException(nameof(rootDir));
    _metricsCollector = metricsCollector;
    _audioFileRepository = audioFileRepository;
  }

  /// <inheritdoc/>
  public async Task<IReadOnlyList<AudioFileInfo>> ListFilesAsync(
    string? path = null,
    bool recursive = false,
    CancellationToken cancellationToken = default)
  {
    var stopwatch = Stopwatch.StartNew();
    var basePath = GetFullPath(path);

    if (!Directory.Exists(basePath))
    {
      // Auto-create the root media directory if it doesn't exist yet
      if (path == null)
      {
        _logger.LogInformation("Creating media directory: {Path}", basePath);
        Directory.CreateDirectory(basePath);
      }
      else
      {
        _logger.LogWarning("Directory not found: {Path}", basePath);
        return Array.Empty<AudioFileInfo>();
      }
    }

    var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
    var files = Directory.GetFiles(basePath, "*.*", searchOption)
      .Where(IsSupportedAudioFile)
      .ToList();

    _logger.LogInformation("Found {Count} audio files in {Path} (recursive: {Recursive})", 
      files.Count, basePath, recursive);

    var audioFiles = new List<AudioFileInfo>();
    
    // Get previous count from repository if available
    var previousCount = 0;
    if (_audioFileRepository != null)
    {
      previousCount = await _audioFileRepository.GetCountAsync(cancellationToken);
    }

    foreach (var file in files)
    {
      cancellationToken.ThrowIfCancellationRequested();
      
      var audioFile = await CreateAudioFileInfoAsync(file, cancellationToken);
      if (audioFile != null)
      {
        audioFiles.Add(audioFile);
      }
    }

    stopwatch.Stop();

    // Track metrics
    _metricsCollector?.Gauge("library.tracks_total", audioFiles.Count);
    _metricsCollector?.Gauge("library.scan_duration_ms", stopwatch.ElapsedMilliseconds);
    
    // Update repository with current state and track changes
    if (_audioFileRepository != null)
    {
      await _audioFileRepository.UpsertBatchAsync(audioFiles, cancellationToken);
      
      // Remove stale files from database that no longer exist on disk
      var currentPaths = audioFiles.Select(f => f.Path).ToList();
      var removedCount = await _audioFileRepository.RemoveStaleAsync(
        path ?? string.Empty,
        currentPaths,
        cancellationToken);
      
      if (removedCount > 0)
      {
        _metricsCollector?.Increment("library.tracks_removed", removedCount);
        _logger.LogInformation("Removed {Count} stale audio files from database", removedCount);
      }
    }

    // Track new files
    var newFilesCount = audioFiles.Count - previousCount;
    if (newFilesCount > 0)
    {
      _metricsCollector?.Increment("library.new_tracks_added", newFilesCount);
    }

    return audioFiles;
  }

  /// <inheritdoc/>
  public async Task<AudioFileInfo?> GetFileInfoAsync(
    string path,
    CancellationToken cancellationToken = default)
  {
    var fullPath = GetFullPath(path);

    if (!File.Exists(fullPath))
    {
      _logger.LogWarning("File not found: {Path}", fullPath);
      return null;
    }

    if (!IsSupportedAudioFile(fullPath))
    {
      _logger.LogWarning("File is not a supported audio format: {Path}", fullPath);
      return null;
    }

    return await CreateAudioFileInfoAsync(fullPath, cancellationToken);
  }

  /// <inheritdoc/>
  public bool IsSupportedAudioFile(string filePath)
  {
    var extension = Path.GetExtension(filePath).ToLowerInvariant();
    return SupportedExtensions.Contains(extension);
  }

  /// <inheritdoc/>
  public string[] GetSupportedExtensions()
  {
    return SupportedExtensions.ToArray();
  }

  /// <inheritdoc/>
  public IReadOnlyList<string> ListDirectories(string? path = null)
  {
    var basePath = GetFullPath(path);

    if (!Directory.Exists(basePath))
    {
      _logger.LogWarning("Directory not found: {Path}", basePath);
      return Array.Empty<string>();
    }

    try
    {
      var directories = Directory.GetDirectories(basePath)
        .Select(d => GetRelativePath(d))
        .OrderBy(d => d)
        .ToList();

      _logger.LogDebug("Found {Count} directories in {Path}", directories.Count, basePath);
      return directories;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error listing directories in {Path}", basePath);
      return Array.Empty<string>();
    }
  }

  /// <inheritdoc/>
  /// <remarks>
  /// <para>Two checks gate the folder. First the same lexical check every other entry point here uses
  /// (<see cref="GetFullPath"/>: <c>..</c> segments are normalised away and the result must sit inside the allowed
  /// set). <see cref="Path.GetFullPath(string)"/> does not resolve symbolic links, so second, the folder's real path
  /// — every link along it resolved — must also sit inside the real path of an allowed directory. Without that a link
  /// inside an allowed tree pointing at, say, <c>/etc</c> would pass the first check and be walked.</para>
  /// <para>The walk itself never follows a link (see <see cref="WalkFolder"/>), so nothing below the checked folder
  /// can lead out of it, and a link cycle cannot loop it.</para>
  /// <para>The walk is synchronous filesystem I/O — on the box a NAS mount, where the whole music tree takes ~15 s
  /// to walk — so it runs on the thread pool via <see cref="Task.Run(Action, CancellationToken)"/>, observing
  /// <paramref name="cancellationToken"/> between entries.</para>
  /// </remarks>
  public async Task<FolderTrackListing> ListFolderTracksAsync(
    string? path,
    bool includeSubfolders,
    int maxTracks,
    CancellationToken cancellationToken = default)
  {
    if (maxTracks < 1)
    {
      throw new ArgumentOutOfRangeException(nameof(maxTracks), maxTracks, "The cap must be at least 1.");
    }

    // Throws UnauthorizedAccessException for a path outside the allowed set. Null/empty means the media root.
    var folder = Path.TrimEndingDirectorySeparator(
      Path.GetFullPath(GetFullPath(string.IsNullOrEmpty(path) ? null : path)));

    var listing = await Task.Run(() =>
    {
      if (!Directory.Exists(folder))
      {
        throw new DirectoryNotFoundException($"Folder not found: {folder}");
      }

      if (!IsRealPathAllowed(folder))
      {
        _logger.LogWarning("Folder rejected: {Folder} resolves through a link to outside the allowed directories", folder);
        throw new UnauthorizedAccessException($"Folder '{folder}' resolves to outside the allowed media directories");
      }

      return WalkFolder(folder, includeSubfolders, maxTracks, IsSupportedAudioFile, CanOpenForRead, cancellationToken);
    }, cancellationToken);

    _logger.LogInformation(
      "Folder listing for {Folder}: {Count} tracks ({TopLevel} top level), subfolders {IncludeSubfolders}, truncated {Truncated}, skipped {Unreadable} unreadable / {Links} links, {UnreadableFolders} unreadable folders",
      listing.FolderPath, listing.Paths.Count, listing.TopLevelCount, listing.IncludeSubfolders, listing.Truncated,
      listing.SkippedUnreadable, listing.SkippedLinks, listing.UnreadableFolders);

    return listing;
  }

  /// <summary>
  /// Walks <paramref name="folder"/> and returns its supported files in queue order (see
  /// <see cref="FolderTrackListing"/>). Pure apart from the filesystem reads, so tests drive it directly.
  /// </summary>
  /// <remarks>
  /// <list type="bullet">
  /// <item>Hidden and system entries are ignored outright (on Linux, names starting with a dot — which covers the
  /// <c>._Song.mp3</c> AppleDouble files a Mac leaves on a NAS, and that are not audio).</item>
  /// <item>Symbolic links and other reparse points are never entered or queued, and are counted in
  /// <see cref="FolderTrackListing.SkippedLinks"/> when they are a folder or carry a supported extension.</item>
  /// <item>A supported file that <paramref name="canRead"/> rejects is counted in
  /// <see cref="FolderTrackListing.SkippedUnreadable"/> and left out.</item>
  /// <item>A subfolder that cannot be listed is counted in <see cref="FolderTrackListing.UnreadableFolders"/> and
  /// skipped; the chosen folder itself failing to list is an error and propagates.</item>
  /// <item>The walk stops at the first supported file past <paramref name="maxTracks"/> and sets
  /// <see cref="FolderTrackListing.Truncated"/>; nothing past that point is read.</item>
  /// </list>
  /// </remarks>
  internal static FolderTrackListing WalkFolder(
    string folder,
    bool includeSubfolders,
    int maxTracks,
    Func<string, bool> isSupported,
    Func<string, bool> canRead,
    CancellationToken cancellationToken)
  {
    // Attributes are filtered below rather than through AttributesToSkip so links can be counted, not just dropped.
    var options = new EnumerationOptions
    {
      RecurseSubdirectories = false,
      IgnoreInaccessible = false,
      AttributesToSkip = 0,
      ReturnSpecialDirectories = false
    };

    var paths = new List<string>();
    var topLevel = 0;
    var subfolderCount = 0;
    var skippedUnreadable = 0;
    var skippedLinks = 0;
    var unreadableFolders = 0;
    var truncated = false;

    // Depth-first, pre-order: a folder's files, then its subfolders in natural order. Children are pushed in
    // reverse so the first in natural order is popped first.
    var pending = new Stack<string>();
    pending.Push(folder);
    var isRoot = true;

    while (pending.Count > 0 && !truncated)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var dir = pending.Pop();

      List<FileSystemInfo> entries;
      try
      {
        entries = new DirectoryInfo(dir).EnumerateFileSystemInfos("*", options).ToList();
      }
      catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
      {
        if (isRoot)
        {
          // Not an UnauthorizedAccessException to the caller: that means "outside the allowed directories" there,
          // and this folder passed that check — it just cannot be listed.
          throw new IOException($"Cannot list folder '{folder}'", ex);
        }

        // DirectoryNotFoundException is an IOException: a subfolder removed mid-walk lands here too.
        unreadableFolders++;
        continue;
      }

      var files = new List<FileSystemInfo>();
      var dirs = new List<FileSystemInfo>();
      foreach (var entry in entries)
      {
        cancellationToken.ThrowIfCancellationRequested();
        var attributes = entry.Attributes;
        if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
        {
          continue;
        }

        var isDirectory = entry is DirectoryInfo;
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
          if (isDirectory || isSupported(entry.Name))
          {
            skippedLinks++;
          }
          continue;
        }

        if (isDirectory)
        {
          dirs.Add(entry);
        }
        else if (isSupported(entry.Name))
        {
          files.Add(entry);
        }
      }

      files.Sort((a, b) => NaturalStringComparer.Instance.Compare(a.Name, b.Name));
      foreach (var file in files)
      {
        cancellationToken.ThrowIfCancellationRequested();
        if (paths.Count == maxTracks)
        {
          truncated = true;
          break;
        }

        if (!canRead(file.FullName))
        {
          skippedUnreadable++;
          continue;
        }

        paths.Add(file.FullName);
        if (isRoot)
        {
          topLevel++;
        }
      }

      if (isRoot)
      {
        subfolderCount = dirs.Count;
      }

      if (includeSubfolders && !truncated)
      {
        dirs.Sort((a, b) => NaturalStringComparer.Instance.Compare(b.Name, a.Name));
        foreach (var sub in dirs)
        {
          pending.Push(sub.FullName);
        }
      }

      isRoot = false;
    }

    return new FolderTrackListing
    {
      FolderPath = folder,
      Paths = paths,
      TopLevelCount = topLevel,
      IncludeSubfolders = includeSubfolders,
      Truncated = truncated,
      MaxTracks = maxTracks,
      SubfolderCount = subfolderCount,
      SkippedUnreadable = skippedUnreadable,
      SkippedLinks = skippedLinks,
      UnreadableFolders = unreadableFolders
    };
  }

  /// <summary>
  /// Whether a file can be opened for reading. Opening is the check — permission bits alone do not say whether an
  /// SMB mount will hand the file over. The handle is closed at once; nothing is read.
  /// </summary>
  private static bool CanOpenForRead(string path)
  {
    try
    {
      using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
      return true;
    }
    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
    {
      return false;
    }
  }

  /// <summary>
  /// Whether <paramref name="fullPath"/>'s real path (see <see cref="ResolveRealPath"/>) is inside the real path of
  /// the media root, an allowed browse directory or a bookmark. A path that cannot be resolved is not allowed.
  /// </summary>
  private bool IsRealPathAllowed(string fullPath)
  {
    string real;
    try
    {
      real = ResolveRealPath(fullPath);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      _logger.LogWarning(ex, "Could not resolve links in {Path}", fullPath);
      return false;
    }

    var options = _options.CurrentValue;
    var allowed = new[] { GetFullPath(null) }
      .Concat(options.AllowedBrowseDirectories)
      .Concat(options.BookmarkedPaths.Select(b => b.Path));

    foreach (var dir in allowed)
    {
      if (string.IsNullOrWhiteSpace(dir))
      {
        continue;
      }

      string allowedReal;
      try
      {
        allowedReal = ResolveRealPath(Path.GetFullPath(dir));
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
      {
        continue;
      }

      if (IsSameOrInside(real, allowedReal))
      {
        return true;
      }
    }

    return false;
  }

  private static bool IsSameOrInside(string fullPath, string dir)
  {
    var trimmed = Path.TrimEndingDirectorySeparator(dir);
    var path = Path.TrimEndingDirectorySeparator(fullPath);
    return path.Equals(trimmed, StringComparison.OrdinalIgnoreCase)
      || path.StartsWith(trimmed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
  }

  /// <summary>
  /// Resolves every symbolic link along <paramref name="fullPath"/>, component by component, and returns the
  /// resulting absolute path (the equivalent of <c>realpath</c>; .NET has no single call for it —
  /// <see cref="FileSystemInfo.ResolveLinkTarget(bool)"/> only resolves the last component). Components that do not
  /// exist are kept as they are.
  /// </summary>
  /// <exception cref="IOException">More than 40 links were followed (a cycle, or an absurd chain).</exception>
  internal static string ResolveRealPath(string fullPath)
  {
    var remaining = new Queue<string>();
    var current = Path.GetPathRoot(fullPath) ?? string.Empty;
    foreach (var part in fullPath[current.Length..].Split(
      [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
    {
      remaining.Enqueue(part);
    }

    var hops = 0;
    while (remaining.Count > 0)
    {
      var next = Path.Combine(current, remaining.Dequeue());
      FileSystemInfo info = Directory.Exists(next) ? new DirectoryInfo(next) : new FileInfo(next);
      if (info.LinkTarget is { } target)
      {
        if (++hops > 40)
        {
          throw new IOException($"Too many levels of symbolic links resolving '{fullPath}'");
        }

        // A relative target is relative to the link's own folder. The target may itself pass through links, so its
        // components go back on the front of the queue and are resolved in turn.
        var targetFull = Path.GetFullPath(Path.IsPathRooted(target) ? target : Path.Combine(current, target));
        var rest = remaining.ToArray();
        remaining.Clear();
        current = Path.GetPathRoot(targetFull) ?? string.Empty;
        foreach (var part in targetFull[current.Length..].Split(
          [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
          remaining.Enqueue(part);
        }
        foreach (var part in rest)
        {
          remaining.Enqueue(part);
        }
        continue;
      }

      current = next;
    }

    return current;
  }

  /// <inheritdoc/>
  public async Task<int> GetFileCountAsync(CancellationToken cancellationToken = default)
  {
    if (_audioFileRepository == null)
    {
      return 0;
    }

    return await _audioFileRepository.GetCountAsync(cancellationToken);
  }

  /// <inheritdoc/>
  public async Task<FileScanResult> ScanForChangesAsync(
    string? path = null,
    bool recursive = false,
    CancellationToken cancellationToken = default)
  {
    var basePath = GetFullPath(path);

    if (!Directory.Exists(basePath))
    {
      _logger.LogWarning("Directory not found for scan: {Path}", basePath);
      return new FileScanResult();
    }

    var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
    var currentFilePaths = Directory.GetFiles(basePath, "*.*", searchOption)
      .Where(IsSupportedAudioFile)
      .ToList();

    // Get current file infos
    var currentFiles = new List<AudioFileInfo>();
    foreach (var file in currentFilePaths)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var audioFile = await CreateAudioFileInfoAsync(file, cancellationToken);
      if (audioFile != null)
      {
        currentFiles.Add(audioFile);
      }
    }

    if (_audioFileRepository == null)
    {
      // No repository - all files are considered new
      return new FileScanResult
      {
        NewFiles = currentFiles,
        ModifiedFiles = Array.Empty<AudioFileInfo>(),
        RemovedPaths = Array.Empty<string>(),
        TotalFiles = currentFiles.Count
      };
    }

    // Get previously scanned files from database
    var previousFiles = await _audioFileRepository.GetByDirectoryAsync(
      path ?? string.Empty, recursive, cancellationToken);
    var previousPathSet = previousFiles.ToDictionary(f => f.Path, f => f);

    // Find new and modified files
    var newFiles = new List<AudioFileInfo>();
    var modifiedFiles = new List<AudioFileInfo>();

    foreach (var file in currentFiles)
    {
      if (!previousPathSet.TryGetValue(file.Path, out var previousFile))
      {
        newFiles.Add(file);
      }
      else if (file.LastModifiedAt != previousFile.LastModifiedAt)
      {
        modifiedFiles.Add(file);
      }
    }

    // Find removed files
    var currentPathSet = currentFiles.Select(f => f.Path).ToHashSet();
    var removedPaths = previousFiles
      .Where(f => !currentPathSet.Contains(f.Path))
      .Select(f => f.Path)
      .ToList();

    _logger.LogInformation(
      "Scan complete: {New} new, {Modified} modified, {Removed} removed files",
      newFiles.Count, modifiedFiles.Count, removedPaths.Count);

    return new FileScanResult
    {
      NewFiles = newFiles,
      ModifiedFiles = modifiedFiles,
      RemovedPaths = removedPaths,
      TotalFiles = currentFiles.Count
    };
  }

  /// <summary>
  /// Gets the full file system path from a path relative to the media root, or from an absolute path.
  /// </summary>
  /// <remarks>
  /// The resolved path must be the media root or inside it, or inside one of
  /// <see cref="FilePlayerOptions.AllowedBrowseDirectories"/> or <see cref="FilePlayerOptions.BookmarkedPaths"/>.
  /// That is the same set <c>FilesController.IsPathAllowed</c> accepts (AUD-75 fix 2). Before, only the media
  /// root was accepted, so every file in a bookmark outside it (e.g. "Local Audio Files" at
  /// <c>/opt/radio-console/media/audio</c> with the root on the NAS) threw here: the controller's absolute-path
  /// listing fell back to a listing without metadata and logged one traversal warning per file.
  /// Every prefix comparison carries a trailing separator, so <c>/mnt/nas</c> does not admit <c>/mnt/nasty</c>.
  /// </remarks>
  private string GetFullPath(string? relativePath)
  {
    var configuredPath = _options.CurrentValue.RootDirectory;

    // If the configured path is absolute, use it directly (e.g., "/mnt/music", "/home/user/Music").
    // Otherwise, combine it relative to the application root directory.
    var basePath = string.IsNullOrEmpty(configuredPath)
      ? _rootDir
      : Path.IsPathRooted(configuredPath)
        ? configuredPath
        : Path.Combine(_rootDir, configuredPath);

    if (string.IsNullOrEmpty(relativePath))
    {
      return basePath;
    }

    // Path.Combine returns relativePath unchanged when it is rooted, so an absolute path is checked as itself.
    var combined = Path.GetFullPath(Path.Combine(basePath, relativePath));
    var resolvedBase = Path.GetFullPath(basePath);

    if (!IsWithinAllowedDirectory(combined, resolvedBase))
    {
      _logger.LogWarning("Path traversal attempt blocked: {RelativePath} resolved to {FullPath} (outside {Base} and the allowed directories)",
        relativePath, combined, resolvedBase);
      throw new UnauthorizedAccessException($"Path '{relativePath}' is outside the allowed media directory");
    }

    return combined;
  }

  /// <summary>
  /// Whether <paramref name="fullPath"/> (already resolved) is the media root, or is inside it or inside an
  /// allowed browse directory or bookmark.
  /// </summary>
  private bool IsWithinAllowedDirectory(string fullPath, string resolvedBase)
  {
    var options = _options.CurrentValue;
    var allowed = new[] { resolvedBase }
      .Concat(options.AllowedBrowseDirectories)
      .Concat(options.BookmarkedPaths.Select(b => b.Path));

    foreach (var dir in allowed)
    {
      if (string.IsNullOrWhiteSpace(dir))
      {
        continue;
      }

      var resolvedDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir));
      if (fullPath.Equals(resolvedDir, StringComparison.OrdinalIgnoreCase)
          || fullPath.StartsWith(resolvedDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
      {
        return true;
      }
    }

    return false;
  }

  /// <summary>
  /// Gets the relative path from a full file system path.
  /// </summary>
  private string GetRelativePath(string fullPath)
  {
    var configuredPath = _options.CurrentValue.RootDirectory;
    var basePath = string.IsNullOrEmpty(configuredPath)
      ? _rootDir
      : Path.Combine(_rootDir, configuredPath);

    var relativePath = Path.GetRelativePath(basePath, fullPath);
    return relativePath;
  }

  /// <summary>
  /// Creates an AudioFileInfo object from a file path, including metadata extraction.
  /// </summary>
  private async Task<AudioFileInfo?> CreateAudioFileInfoAsync(
    string fullPath,
    CancellationToken cancellationToken)
  {
    try
    {
      var fileInfo = new FileInfo(fullPath);
      var relativePath = GetRelativePath(fullPath);

      // Extract metadata using SoundFlow
      var metadata = await ExtractMetadataAsync(fullPath, cancellationToken);

      return new AudioFileInfo
      {
        Path = relativePath,
        FileName = fileInfo.Name,
        Extension = fileInfo.Extension.ToLowerInvariant(),
        SizeBytes = fileInfo.Length,
        CreatedAt = fileInfo.CreationTimeUtc,
        LastModifiedAt = fileInfo.LastWriteTimeUtc,
        Title = metadata.Title ?? Path.GetFileNameWithoutExtension(fileInfo.Name),
        Artist = metadata.Artist,
        Album = metadata.Album,
        Duration = metadata.Duration,
        TrackNumber = metadata.TrackNumber,
        Genre = metadata.Genre,
        Year = metadata.Year
      };
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error creating AudioFileInfo for {Path}", fullPath);
      return null;
    }
  }

  /// <summary>
  /// Extracts metadata from an audio file via <see cref="AudioTagReader"/> (SoundFlow, then TagLib).
  /// </summary>
  /// <remarks>
  /// Uses Task.Run to offload CPU-bound metadata reading from the thread pool.
  /// For production systems with high load, consider implementing a dedicated
  /// thread pool or queueing system to prevent thread pool starvation.
  /// </remarks>
  private async Task<(
    string? Title,
    string? Artist,
    string? Album,
    TimeSpan? Duration,
    int? TrackNumber,
    string? Genre,
    int? Year)> ExtractMetadataAsync(
    string filePath,
    CancellationToken cancellationToken)
  {
    try
    {
      return await Task.Run<(string?, string?, string?, TimeSpan?, int?, string?, int?)>(() =>
      {
        // AUD-32: SoundFlow first, TagLib when SoundFlow rejects the tag. Blank tags come back null.
        var tags = AudioTagReader.Read(filePath, _logger);
        if (tags == null)
        {
          return (null, null, null, null, null, null, null);
        }

        return (tags.Title, tags.Artist, tags.Album, tags.Duration, tags.TrackNumber, tags.Genre, tags.Year);
      }, cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to extract metadata from {Path}", filePath);
      return (null, null, null, null, null, null, null);
    }
  }
}

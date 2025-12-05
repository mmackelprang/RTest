using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using SoundFlow.Metadata;
using System.Diagnostics;

namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// File browser service for discovering and listing audio files.
/// </summary>
public class FileBrowser : IFileBrowser
{
  private readonly ILogger<FileBrowser> _logger;
  private readonly IOptionsMonitor<FilePlayerOptions> _options;
  private readonly IMetricsCollector? _metricsCollector;
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
  public FileBrowser(
    ILogger<FileBrowser> logger,
    IOptionsMonitor<FilePlayerOptions> options,
    string rootDir,
    IMetricsCollector? metricsCollector = null)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _options = options ?? throw new ArgumentNullException(nameof(options));
    _rootDir = rootDir ?? throw new ArgumentNullException(nameof(rootDir));
    _metricsCollector = metricsCollector;
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
      _logger.LogWarning("Directory not found: {Path}", basePath);
      return Array.Empty<AudioFileInfo>();
    }

    var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
    var files = Directory.GetFiles(basePath, "*.*", searchOption)
      .Where(IsSupportedAudioFile)
      .ToList();

    _logger.LogInformation("Found {Count} audio files in {Path} (recursive: {Recursive})", 
      files.Count, basePath, recursive);

    var audioFiles = new List<AudioFileInfo>();
    var previousCount = 0; // TODO: In a real implementation, this would track existing files from a database

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
    
    // Track new files (simplified implementation - in production, this would compare with existing database)
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

  /// <summary>
  /// Gets the full file system path from a relative path.
  /// </summary>
  private string GetFullPath(string? relativePath)
  {
    var configuredPath = _options.CurrentValue.RootDirectory;
    var basePath = string.IsNullOrEmpty(configuredPath)
      ? _rootDir
      : Path.Combine(_rootDir, configuredPath);

    if (string.IsNullOrEmpty(relativePath))
    {
      return basePath;
    }

    return Path.Combine(basePath, relativePath);
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
  /// Extracts metadata from an audio file using SoundFlow's SoundMetadataReader.
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
        var result = SoundMetadataReader.Read(filePath);
        
        if (!result.IsSuccess || result.Value == null)
        {
          return (null, null, null, null, null, null, null);
        }

        var formatInfo = result.Value;
        var tags = formatInfo.Tags;

        TimeSpan? duration = null;
        if (formatInfo.Duration != TimeSpan.Zero)
        {
          duration = formatInfo.Duration;
        }

        return (
          string.IsNullOrWhiteSpace(tags?.Title) ? null : tags.Title,
          string.IsNullOrWhiteSpace(tags?.Artist) ? null : tags.Artist,
          string.IsNullOrWhiteSpace(tags?.Album) ? null : tags.Album,
          duration,
          null, // TODO: SoundFlow SoundTags doesn't currently expose track number - consider enhancement
          null, // TODO: SoundFlow SoundTags doesn't currently expose genre - consider enhancement
          null // TODO: SoundFlow SoundTags doesn't currently expose year - consider enhancement
        );
      }, cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to extract metadata from {Path}", filePath);
      return (null, null, null, null, null, null, null);
    }
  }
}

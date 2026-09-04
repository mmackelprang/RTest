using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.SoundFlow;
using Radio.Infrastructure.Audio.Sources.Events;

namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// Factory for creating audio file event sources.
/// </summary>
public class AudioFileEventSourceFactory
{
  private readonly ILogger<AudioFileEventSourceFactory> _logger;
  private readonly ILogger<AudioFileEventSource> _sourceLogger;
  private readonly IOptionsMonitor<FilePlayerOptions> _options;
  private readonly SoundFlowPlaybackService? _playbackService;

  /// <summary>
  /// Initializes a new instance of the <see cref="AudioFileEventSourceFactory"/> class.
  /// </summary>
  /// <param name="logger">The factory logger.</param>
  /// <param name="sourceLogger">The source logger.</param>
  /// <param name="options">The file player options.</param>
  /// <param name="playbackService">Optional SoundFlow playback service.</param>
  public AudioFileEventSourceFactory(
    ILogger<AudioFileEventSourceFactory> logger,
    ILogger<AudioFileEventSource> sourceLogger,
    IOptionsMonitor<FilePlayerOptions> options,
    SoundFlowPlaybackService? playbackService = null)
  {
    _logger = logger;
    _sourceLogger = sourceLogger;
    _options = options;
    _playbackService = playbackService;
  }

  /// <summary>
  /// Creates an audio file event source from a file path.
  /// </summary>
  /// <param name="filePath">The path to the audio file.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>An audio file event source.</returns>
  public async Task<IEventAudioSource> CreateFromFileAsync(
    string filePath,
    CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

    // Resolve relative paths against the configured root directory
    var fullPath = ResolveFilePath(filePath);

    if (!File.Exists(fullPath))
    {
      throw new FileNotFoundException($"Audio file not found: {fullPath}");
    }

    _logger.LogInformation("Creating audio file event source: {FilePath}", fullPath);

    // Get the duration of the audio file
    var duration = await GetAudioDurationAsync(fullPath, cancellationToken);

    return new AudioFileEventSource(fullPath, duration, _sourceLogger, _playbackService);
  }

  /// <summary>
  /// Creates an event source over a file the caller has already located, with a duration the
  /// caller already knows.
  /// </summary>
  /// <remarks>
  /// ⚠ Deliberately NOT a variant of <see cref="CreateFromFileAsync"/>, and both differences are
  /// load-bearing for ADR-029's RemoteMedia arm.
  ///
  /// It does not re-root. <see cref="CreateFromFileAsync"/> sends a relative path through
  /// ResolveFilePath, which combines it with FilePlayer:RootDirectory — "media/audio" in the repo
  /// and "/mnt/nas/music" on the appliance. GvMedia:CacheDirectory ships as the RELATIVE
  /// "./data/gvmedia", so a fetched recording would be looked for under the music root and the
  /// play would fail with a FileNotFoundException after a successful fetch.
  ///
  /// And it accepts the duration rather than estimating it. ADR-029 §4.1 calls the passthrough of
  /// VoicemailItemDto.DurationSeconds "a correctness fix, not decoration": AudioFileEventSource
  /// detects completion from this value, so a wrong duration ends playback early or leaves it
  /// hanging. A null duration means the provider reported 0 — "unknown" per ADR-022 §4.2 — and
  /// only then does this fall back to the same size-based estimate CreateFromFileAsync uses, so
  /// this arc carries no second bytes-per-second constant.
  /// </remarks>
  /// <param name="absolutePath">
  /// A rooted path to an existing audio file. Not resolved against any root.
  /// </param>
  /// <param name="duration">The authoritative duration, or null to estimate it from the file size.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>An audio file event source.</returns>
  /// <exception cref="ArgumentException">The path is not rooted.</exception>
  /// <exception cref="FileNotFoundException">The file does not exist.</exception>
  public async Task<IEventAudioSource> CreateFromAbsolutePathAsync(
    string absolutePath,
    TimeSpan? duration,
    CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

    if (!Path.IsPathRooted(absolutePath))
    {
      throw new ArgumentException(
        "CreateFromAbsolutePathAsync requires a rooted path; it deliberately does not resolve "
        + "against FilePlayer:RootDirectory.", nameof(absolutePath));
    }

    if (!File.Exists(absolutePath))
    {
      throw new FileNotFoundException($"Audio file not found: {absolutePath}");
    }

    var effective = duration ?? await GetAudioDurationAsync(absolutePath, cancellationToken);

    // ⚠ The path is deliberately absent from this line, unlike CreateFromFileAsync's. For the
    // RemoteMedia arm the filename is a hash of a voicemail id, and the rule the arc holds to is
    // that an id's derived forms appear only as GvMediaCache.MaskFor produces them —
    // EventPlaybackService already logs that mask. One correlating token per playback, not two.
    _logger.LogDebug(
      "Creating audio file event source from an absolute path, duration {Duration} ({Source})",
      effective, duration is null ? "estimated" : "authoritative");

    return new AudioFileEventSource(absolutePath, effective, _sourceLogger, _playbackService);
  }

  /// <summary>
  /// Creates an audio file event source from a stream.
  /// </summary>
  /// <param name="name">The display name for the event.</param>
  /// <param name="audioStream">The audio stream.</param>
  /// <param name="duration">The duration of the audio.</param>
  /// <returns>An audio file event source.</returns>
  public IEventAudioSource CreateFromStream(
    string name,
    Stream audioStream,
    TimeSpan duration)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    ArgumentNullException.ThrowIfNull(audioStream);

    _logger.LogInformation("Creating audio file event source from stream: {Name}", name);

    return new AudioFileEventSource(name, audioStream, duration, _sourceLogger, _playbackService);
  }

  /// <summary>
  /// Gets a list of subdirectories in the notification sounds folder.
  /// </summary>
  /// <param name="subdirectory">Optional subdirectory to list subdirectories from.</param>
  /// <returns>A list of subdirectory names.</returns>
  public IReadOnlyList<string> GetSubdirectories(string? subdirectory = null)
  {
    var rootPath = _options.CurrentValue.RootDirectory;
    var searchPath = subdirectory != null
      ? Path.Combine(rootPath, subdirectory)
      : rootPath;

    // Convert to absolute path for consistent results
    var absoluteSearchPath = Path.GetFullPath(searchPath);

    if (!Directory.Exists(absoluteSearchPath))
    {
      _logger.LogWarning("Directory does not exist: {Path}", absoluteSearchPath);
      return Array.Empty<string>();
    }

    var directories = Directory.GetDirectories(absoluteSearchPath)
      .Select(Path.GetFileName)
      .Where(name => !string.IsNullOrEmpty(name))
      .OrderBy(name => name)
      .ToList();

    return directories.AsReadOnly()!;
  }

  /// <summary>
  /// Gets a list of available notification sounds from the configured directory.
  /// Returns absolute file paths for direct use with playback endpoints.
  /// </summary>
  /// <param name="subdirectory">Optional subdirectory to search in.</param>
  /// <returns>A list of available audio file paths (absolute paths).</returns>
  public IReadOnlyList<string> GetAvailableNotificationSounds(string? subdirectory = null)
  {
    var rootPath = _options.CurrentValue.RootDirectory;
    var searchPath = subdirectory != null
      ? Path.Combine(rootPath, subdirectory)
      : rootPath;

    // Convert to absolute path for consistent results
    var absoluteSearchPath = Path.GetFullPath(searchPath);

    if (!Directory.Exists(absoluteSearchPath))
    {
      _logger.LogWarning("Notification sounds directory does not exist: {Path}", absoluteSearchPath);
      return Array.Empty<string>();
    }

    var supportedExtensions = new[] { ".wav", ".mp3", ".ogg", ".flac" };
    var files = Directory.GetFiles(absoluteSearchPath, "*.*")
      .Where(f => supportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
      .Select(f => Path.GetFullPath(f)) // Ensure absolute paths
      .ToList();

    return files.AsReadOnly();
  }

  private string ResolveFilePath(string filePath)
  {
    if (Path.IsPathRooted(filePath))
    {
      return filePath;
    }

    var rootPath = _options.CurrentValue.RootDirectory;
    return Path.Combine(rootPath, filePath);
  }

  private async Task<TimeSpan> GetAudioDurationAsync(string filePath, CancellationToken cancellationToken)
  {
    return await Task.Run(() =>
    {
      var extension = Path.GetExtension(filePath).ToLowerInvariant();
      var fileInfo = new FileInfo(filePath);

      if (fileInfo.Length == 0)
      {
        return TimeSpan.Zero;
      }

      var estimatedDuration = extension switch
      {
        ".wav" => EstimateWavDuration(fileInfo.Length),
        ".mp3" => EstimateMp3Duration(fileInfo.Length),
        ".ogg" => EstimateOggDuration(fileInfo.Length),
        ".flac" => EstimateFlacDuration(fileInfo.Length),
        _ => TimeSpan.FromSeconds(5)
      };

      _logger.LogDebug("Estimated duration for {File}: {Duration}", filePath, estimatedDuration);

      return estimatedDuration;
    }, cancellationToken);
  }

  private static TimeSpan EstimateWavDuration(long bytes)
  {
    // Typical WAV: 44100 Hz, 16-bit, stereo = 176400 bytes/second
    const int bytesPerSecond = 176400;
    const int headerSize = 44;

    if (bytes <= headerSize)
    {
      return TimeSpan.Zero;
    }

    var audioBytes = bytes - headerSize;
    return TimeSpan.FromSeconds((double)audioBytes / bytesPerSecond);
  }

  private static TimeSpan EstimateMp3Duration(long bytes)
  {
    // Typical MP3: 128 kbps = 16000 bytes/second
    const int bytesPerSecond = 16000;
    return TimeSpan.FromSeconds((double)bytes / bytesPerSecond);
  }

  private static TimeSpan EstimateOggDuration(long bytes)
  {
    // Typical OGG Vorbis: 128 kbps = 16000 bytes/second
    const int bytesPerSecond = 16000;
    return TimeSpan.FromSeconds((double)bytes / bytesPerSecond);
  }

  private static TimeSpan EstimateFlacDuration(long bytes)
  {
    // Typical FLAC: ~800 kbps = 100000 bytes/second (varies widely)
    const int bytesPerSecond = 100000;
    return TimeSpan.FromSeconds((double)bytes / bytesPerSecond);
  }
}

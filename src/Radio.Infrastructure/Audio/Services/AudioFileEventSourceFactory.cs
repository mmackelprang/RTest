using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
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

  /// <summary>
  /// Initializes a new instance of the <see cref="AudioFileEventSourceFactory"/> class.
  /// </summary>
  /// <param name="logger">The factory logger.</param>
  /// <param name="sourceLogger">The source logger.</param>
  /// <param name="options">The file player options.</param>
  public AudioFileEventSourceFactory(
    ILogger<AudioFileEventSourceFactory> logger,
    ILogger<AudioFileEventSource> sourceLogger,
    IOptionsMonitor<FilePlayerOptions> options)
  {
    _logger = logger;
    _sourceLogger = sourceLogger;
    _options = options;
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

    return new AudioFileEventSource(fullPath, duration, _sourceLogger);
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

    return new AudioFileEventSource(name, audioStream, duration, _sourceLogger);
  }

  /// <summary>
  /// Gets a list of available notification sounds from the configured directory.
  /// </summary>
  /// <param name="subdirectory">Optional subdirectory to search in.</param>
  /// <returns>A list of available audio file paths.</returns>
  public IReadOnlyList<string> GetAvailableNotificationSounds(string? subdirectory = null)
  {
    var rootPath = _options.CurrentValue.RootDirectory;
    var searchPath = subdirectory != null
      ? Path.Combine(rootPath, subdirectory)
      : rootPath;

    if (!Directory.Exists(searchPath))
    {
      _logger.LogWarning("Notification sounds directory does not exist: {Path}", searchPath);
      return Array.Empty<string>();
    }

    var supportedExtensions = new[] { ".wav", ".mp3", ".ogg", ".flac" };
    var files = Directory.GetFiles(searchPath, "*.*")
      .Where(f => supportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
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

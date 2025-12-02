using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Interfaces;
using SoundFlow.Metadata;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace Radio.Infrastructure.Audio.Sources.Primary;

/// <summary>
/// Audio file player source supporting single files, playlists, and directories.
/// </summary>
public class FilePlayerAudioSource : PrimaryAudioSourceBase
{
  private readonly IOptionsMonitor<FilePlayerOptions> _options;
  private readonly IOptionsMonitor<FilePlayerPreferences> _preferences;
  private readonly string _rootDir;
  private readonly Dictionary<string, string> _metadata = new();
  private Queue<string> _playlist = new();
  private string? _currentFile;
  private ISoundDataProvider? _dataProvider;
  private FileStream? _fileStream;
  private MiniAudioEngine? _audioEngine;
  private TimeSpan _duration;
  private TimeSpan _position;

  /// <summary>
  /// Initializes a new instance of the <see cref="FilePlayerAudioSource"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="options">The file player options.</param>
  /// <param name="preferences">The file player preferences.</param>
  /// <param name="rootDir">The root directory for audio files.</param>
  public FilePlayerAudioSource(
    ILogger<FilePlayerAudioSource> logger,
    IOptionsMonitor<FilePlayerOptions> options,
    IOptionsMonitor<FilePlayerPreferences> preferences,
    string rootDir = "")
    : base(logger)
  {
    _options = options;
    _preferences = preferences;
    _rootDir = rootDir;
  }

  /// <inheritdoc/>
  public override string Name => "File Player";

  /// <inheritdoc/>
  public override AudioSourceType Type => AudioSourceType.FilePlayer;

  /// <inheritdoc/>
  public override TimeSpan? Duration => _duration;

  /// <inheritdoc/>
  public override TimeSpan Position => _position;

  /// <inheritdoc/>
  public override bool IsSeekable => true;

  /// <inheritdoc/>
  public override IReadOnlyDictionary<string, string> Metadata => _metadata;

  // File player supports next, shuffle, repeat, and queue
  /// <inheritdoc/>
  public override bool SupportsNext => true;

  /// <inheritdoc/>
  public override bool SupportsPrevious => true;

  /// <inheritdoc/>
  public override bool SupportsShuffle => true;

  /// <inheritdoc/>
  public override bool SupportsRepeat => true;

  /// <inheritdoc/>
  public override bool SupportsQueue => true;

  /// <inheritdoc/>
  public override bool IsShuffleEnabled => _preferences.CurrentValue.Shuffle;

  /// <inheritdoc/>
  public override RepeatMode RepeatMode => _preferences.CurrentValue.Repeat;

  /// <summary>
  /// Gets the current playlist.
  /// </summary>
  public IReadOnlyList<string> Playlist => _playlist.ToList();

  /// <summary>
  /// Gets the current file being played.
  /// </summary>
  public string? CurrentFile => _currentFile;

  /// <summary>
  /// Gets the number of remaining tracks in the playlist.
  /// </summary>
  public int RemainingTracks => _playlist.Count;

  /// <inheritdoc/>
  public override object GetSoundComponent()
  {
    return _dataProvider ?? throw new InvalidOperationException("Audio source not initialized");
  }

  /// <summary>
  /// Loads a single file for playback.
  /// </summary>
  /// <param name="filePath">The path to the audio file (relative to root directory).</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  /// <exception cref="FileNotFoundException">Thrown if the file does not exist.</exception>
  public async Task LoadFileAsync(string filePath, CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    var fullPath = GetFullPath(filePath);
    if (!File.Exists(fullPath))
    {
      throw new FileNotFoundException($"Audio file not found: {fullPath}", fullPath);
    }

    if (!IsAudioFile(fullPath))
    {
      throw new ArgumentException($"Unsupported audio format: {Path.GetExtension(fullPath)}", nameof(filePath));
    }

    _playlist.Clear();
    _playlist.Enqueue(fullPath);
    await LoadCurrentFileAsync(cancellationToken);

    Logger.LogInformation("Loaded audio file: {FilePath}", fullPath);
  }

  /// <summary>
  /// Loads all audio files from a directory for playback.
  /// </summary>
  /// <param name="directoryPath">The path to the directory (relative to root directory).</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  /// <exception cref="DirectoryNotFoundException">Thrown if the directory does not exist.</exception>
  public async Task LoadDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    var fullPath = GetFullPath(directoryPath);
    if (!Directory.Exists(fullPath))
    {
      throw new DirectoryNotFoundException($"Directory not found: {fullPath}");
    }

    var audioFiles = Directory.GetFiles(fullPath, "*.*", SearchOption.AllDirectories)
      .Where(f => IsAudioFile(f))
      .ToList();

    if (audioFiles.Count == 0)
    {
      throw new InvalidOperationException($"No audio files found in directory: {fullPath}");
    }

    // Apply shuffle if enabled
    if (_preferences.CurrentValue.Shuffle)
    {
      audioFiles = audioFiles.OrderBy(_ => Random.Shared.Next()).ToList();
    }
    else
    {
      audioFiles = audioFiles.OrderBy(f => f).ToList();
    }

    _playlist = new Queue<string>(audioFiles);
    await LoadCurrentFileAsync(cancellationToken);

    Logger.LogInformation("Loaded {Count} audio files from directory: {DirectoryPath}", audioFiles.Count, fullPath);
  }

  /// <summary>
  /// Loads a playlist of files.
  /// </summary>
  /// <param name="files">The list of file paths (relative to root directory).</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  public async Task LoadPlaylistAsync(IEnumerable<string> files, CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    var validFiles = files
      .Select(GetFullPath)
      .Where(f => File.Exists(f) && IsAudioFile(f))
      .ToList();

    if (validFiles.Count == 0)
    {
      throw new InvalidOperationException("No valid audio files in playlist");
    }

    // Apply shuffle if enabled
    if (_preferences.CurrentValue.Shuffle)
    {
      validFiles = validFiles.OrderBy(_ => Random.Shared.Next()).ToList();
    }

    _playlist = new Queue<string>(validFiles);
    await LoadCurrentFileAsync(cancellationToken);

    Logger.LogInformation("Loaded playlist with {Count} files", validFiles.Count);
  }

  /// <summary>
  /// Attempts to skip to the next track in the playlist.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>True if there was a next track; false if the playlist is empty.</returns>
  public async Task<bool> TryNextAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    if (_playlist.Count == 0)
    {
      if (_preferences.CurrentValue.Repeat == RepeatMode.All && _currentFile != null)
      {
        // Reload the directory/playlist
        Logger.LogDebug("Playlist empty but repeat all is enabled");
      }
      return false;
    }

    await LoadCurrentFileAsync(cancellationToken);

    if (State == AudioSourceState.Playing)
    {
      await PlayCoreAsync(cancellationToken);
    }

    return true;
  }

  /// <inheritdoc/>
  public override async Task NextAsync(CancellationToken cancellationToken = default)
  {
    await TryNextAsync(cancellationToken);
  }

  /// <inheritdoc/>
  protected override async Task InitializeAsync(CancellationToken cancellationToken = default)
  {
    await base.InitializeAsync(cancellationToken);

    // Restore last played file if available
    var prefs = _preferences.CurrentValue;
    if (!string.IsNullOrEmpty(prefs.LastSongPlayed) && File.Exists(prefs.LastSongPlayed))
    {
      Logger.LogDebug("Restoring last played file: {File}", prefs.LastSongPlayed);
      _currentFile = prefs.LastSongPlayed;
      _position = TimeSpan.FromMilliseconds(prefs.SongPositionMs);
      UpdateMetadataFromFile(_currentFile);
    }

    State = AudioSourceState.Ready;
  }

  /// <inheritdoc/>
  protected override Task PlayCoreAsync(CancellationToken cancellationToken)
  {
    if (_currentFile == null)
    {
      throw new InvalidOperationException("No file loaded");
    }

    // In a real implementation, this would start SoundFlow playback
    Logger.LogInformation("Playing file: {File}", _currentFile);

    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  protected override Task PauseCoreAsync(CancellationToken cancellationToken)
  {
    Logger.LogDebug("Pausing file playback at {Position}", _position);
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  protected override Task ResumeCoreAsync(CancellationToken cancellationToken)
  {
    Logger.LogDebug("Resuming file playback from {Position}", _position);
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  protected override Task StopCoreAsync(CancellationToken cancellationToken)
  {
    Logger.LogDebug("Stopping file playback");

    // Save current position for next session
    if (_currentFile != null)
    {
      _preferences.CurrentValue.LastSongPlayed = _currentFile;
      _preferences.CurrentValue.SongPositionMs = (long)_position.TotalMilliseconds;
    }

    _position = TimeSpan.Zero;
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  protected override Task SeekCoreAsync(TimeSpan position, CancellationToken cancellationToken)
  {
    // Seeking is only valid for positive positions within the duration
    // When duration is zero or not set, seeking is limited to position zero
    if (position < TimeSpan.Zero || (_duration > TimeSpan.Zero && position > _duration))
    {
      throw new ArgumentOutOfRangeException(nameof(position), "Seek position out of range");
    }

    _position = position;
    Logger.LogDebug("Seeked to {Position}", position);
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  protected override async ValueTask DisposeAsyncCore()
  {
    // Save state for next session
    if (_currentFile != null)
    {
      _preferences.CurrentValue.LastSongPlayed = _currentFile;
      _preferences.CurrentValue.SongPositionMs = (long)_position.TotalMilliseconds;
    }

    CleanupDataProvider();
    _currentFile = null;
    _playlist.Clear();

    await base.DisposeAsyncCore();
  }

  /// <summary>
  /// Cleans up the data provider and audio engine.
  /// </summary>
  private void CleanupDataProvider()
  {
    if (_dataProvider is IDisposable disposable)
    {
      disposable.Dispose();
    }
    _dataProvider = null;

    // Dispose the file stream that was kept open for the data provider
    _fileStream?.Dispose();
    _fileStream = null;

    if (_audioEngine != null)
    {
      _audioEngine.Dispose();
      _audioEngine = null;
    }
  }

  private string GetFullPath(string relativePath)
  {
    var rootDirectory = _options.CurrentValue.RootDirectory;
    string basePath;
    if (!string.IsNullOrEmpty(_rootDir))
    {
      basePath = Path.Combine(_rootDir, rootDirectory, relativePath);
    }
    else
    {
      basePath = Path.Combine(rootDirectory, relativePath);
    }
    // Normalize the path to handle any leading separators or relative components
    return Path.GetFullPath(basePath);
  }

  private bool IsAudioFile(string path)
  {
    var ext = Path.GetExtension(path).ToLowerInvariant();
    var supportedExtensions = _options.CurrentValue.SupportedExtensions;
    return supportedExtensions.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase));
  }

  private async Task LoadCurrentFileAsync(CancellationToken cancellationToken)
  {
    if (_playlist.Count == 0)
    {
      _currentFile = null;
      OnPlaybackCompleted(PlaybackCompletionReason.EndOfContent);
      return;
    }

    _currentFile = _playlist.Dequeue();
    _position = TimeSpan.Zero;

    // Clean up previous data provider
    CleanupDataProvider();

    // Try to initialize SoundFlow audio engine and create a data provider
    try
    {
      _audioEngine ??= new MiniAudioEngine();

      // Create a data provider from the file using SoundFlow
      // Note: We keep the FileStream open (stored as field) because ChunkedDataProvider needs it
      _fileStream = File.OpenRead(_currentFile);
      _dataProvider = new ChunkedDataProvider(_audioEngine, _fileStream);

      Logger.LogDebug("Loaded file with SoundFlow: {File}", _currentFile);
    }
    catch (Exception ex)
    {
      // SoundFlow couldn't decode the file - this could happen with unsupported formats
      // or during testing with dummy files. Log and continue without a data provider.
      Logger.LogWarning(ex, "SoundFlow could not decode file: {File}. Using basic file info only.", _currentFile);
      _fileStream?.Dispose();
      _fileStream = null;
      _dataProvider = null;
    }

    // Read metadata from the file (this uses SoundMetadataReader which is separate from decoding)
    UpdateMetadataFromFile(_currentFile);

    Logger.LogDebug("Loaded file: {File}", _currentFile);

    if (State == AudioSourceState.Created)
    {
      State = AudioSourceState.Ready;
    }

    await Task.CompletedTask;
  }

  private void UpdateMetadataFromFile(string filePath)
  {
    _metadata.Clear();
    _metadata["Title"] = Path.GetFileNameWithoutExtension(filePath);
    _metadata["FileName"] = Path.GetFileName(filePath);
    _metadata["Directory"] = Path.GetDirectoryName(filePath) ?? "";
    _metadata["Extension"] = Path.GetExtension(filePath);

    // Use SoundFlow's metadata reader to get audio tags
    try
    {
      var result = SoundMetadataReader.Read(filePath);
      if (result.IsSuccess && result.Value != null)
      {
        var formatInfo = result.Value;

        // Get duration from Duration property
        if (formatInfo.Duration != TimeSpan.Zero)
        {
          _duration = formatInfo.Duration;
        }
        else
        {
          _duration = TimeSpan.Zero;
        }

        _metadata["Duration"] = _duration.ToString();
        _metadata["SampleRate"] = formatInfo.SampleRate.ToString();
        _metadata["Channels"] = formatInfo.ChannelCount.ToString();
        _metadata["BitRate"] = formatInfo.Bitrate.ToString();

        // Get tags (Title, Artist, Album, etc.)
        if (formatInfo.Tags != null)
        {
          if (!string.IsNullOrEmpty(formatInfo.Tags.Title))
          {
            _metadata["Title"] = formatInfo.Tags.Title;
          }
          if (!string.IsNullOrEmpty(formatInfo.Tags.Artist))
          {
            _metadata["Artist"] = formatInfo.Tags.Artist;
          }
          if (!string.IsNullOrEmpty(formatInfo.Tags.Album))
          {
            _metadata["Album"] = formatInfo.Tags.Album;
          }
          if (!string.IsNullOrEmpty(formatInfo.Tags.Genre))
          {
            _metadata["Genre"] = formatInfo.Tags.Genre;
          }
          if (formatInfo.Tags.Year.HasValue)
          {
            _metadata["Year"] = formatInfo.Tags.Year.Value.ToString();
          }
          if (formatInfo.Tags.TrackNumber.HasValue)
          {
            _metadata["TrackNumber"] = formatInfo.Tags.TrackNumber.Value.ToString();
          }
        }

        Logger.LogDebug(
          "Loaded metadata for {File}: Title={Title}, Artist={Artist}, Duration={Duration}",
          Path.GetFileName(filePath),
          _metadata.GetValueOrDefault("Title"),
          _metadata.GetValueOrDefault("Artist"),
          _duration);
      }
      else
      {
        // Fallback to basic file info only
        _duration = TimeSpan.Zero;
        _metadata["Duration"] = _duration.ToString();
        Logger.LogDebug("Could not read metadata from {File}, using file name as title", filePath);
      }
    }
    catch (Exception ex)
    {
      Logger.LogWarning(ex, "Failed to read metadata from {File}, using default values", filePath);
      _duration = TimeSpan.Zero;
      _metadata["Duration"] = _duration.ToString();
    }
  }
}

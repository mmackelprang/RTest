using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Events;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Fingerprinting;
using Radio.Infrastructure.Audio.Services;
using Radio.Infrastructure.Audio.SoundFlow;
using Radio.Configuration.Abstractions;
using Radio.Configuration.Models;
using Radio.Metrics;
using Radio.Fingerprinting.Services;
using Radio.Fingerprinting;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Interfaces;
using SoundFlow.Metadata;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace Radio.Infrastructure.Audio.Sources.Primary;

/// <summary>
/// Audio file player source supporting single files, playlists, and directories.
/// Supports automatic track identification via fingerprinting when file tags are missing.
/// </summary>
public class FilePlayerAudioSource : PrimaryAudioSourceBase, IPlayQueue
{
  private readonly IOptionsMonitor<FilePlayerOptions> _options;
  private readonly IOptionsMonitor<FilePlayerPreferences> _preferences;
  private readonly BackgroundIdentificationService? _identificationService;
  private readonly SoundFlowPlaybackService? _playbackService;
  private readonly IConfigurationManager? _configurationManager;
  private readonly AlbumArtCacheService? _albumArtCache;
  private readonly IOptionsMonitor<FingerprintingOptions>? _fingerprintingOptionsMonitor;
  private readonly string _rootDir;
  private readonly Dictionary<string, object> _metadata = new();
  private readonly HashSet<string> _errorFiles = new();
  private readonly object _playlistLock = new();
  private Queue<string> _playlist = new();
  private List<string> _originalOrder = new(); // Store original order for shuffle toggle
  private List<string> _playedHistory = new(); // Track played songs for Previous
  private string? _currentFile;
  private int _consecutiveSkipCount;
  private ISoundDataProvider? _dataProvider;
  private FileStream? _fileStream;
  private MiniAudioEngine? _audioEngine;
  private TimeSpan _duration;
  private TimeSpan _position;
  private int _currentIndex = -1; // Track current position in queue
  private string? _playbackId;
  private CancellationTokenSource? _playbackCts;
  private Task? _playbackMonitorTask;
  private bool _trackEndedNaturally;
  private long _pendingSeekMs;

  /// <summary>Current fingerprinting options (live from IOptionsMonitor).</summary>
  private FingerprintingOptions FpOptions =>
    _fingerprintingOptionsMonitor?.CurrentValue ?? new FingerprintingOptions();

  /// <summary>
  /// Initializes a new instance of the <see cref="FilePlayerAudioSource"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="options">The file player options.</param>
  /// <param name="preferences">The file player preferences.</param>
  /// <param name="rootDir">The root directory for audio files.</param>
  /// <param name="identificationService">Optional fingerprinting service for track identification.</param>
  /// <param name="metricsCollector">Optional metrics collector for tracking playback metrics.</param>
  /// <param name="playbackService">Optional SoundFlow playback service for audio output.</param>
  /// <param name="configurationManager">Optional configuration manager for queue persistence.</param>
  /// <param name="albumArtCache">Optional album art cache for extracting embedded cover art.</param>
  /// <param name="fingerprintingOptions">Optional fingerprinting options (controls UseShazamForAllSources toggle).</param>
  public FilePlayerAudioSource(
    ILogger<FilePlayerAudioSource> logger,
    IOptionsMonitor<FilePlayerOptions> options,
    IOptionsMonitor<FilePlayerPreferences> preferences,
    string rootDir = "",
    BackgroundIdentificationService? identificationService = null,
    IMetricsCollector? metricsCollector = null,
    SoundFlowPlaybackService? playbackService = null,
    IConfigurationManager? configurationManager = null,
    AlbumArtCacheService? albumArtCache = null,
    IOptionsMonitor<FingerprintingOptions>? fingerprintingOptions = null)
    : base(logger, metricsCollector)
  {
    _options = options;
    _preferences = preferences;
    _rootDir = rootDir;
    _identificationService = identificationService;
    _playbackService = playbackService;
    _configurationManager = configurationManager;
    _albumArtCache = albumArtCache;
    _fingerprintingOptionsMonitor = fingerprintingOptions;

    // Subscribe to track identification events if service is available
    if (_identificationService != null)
    {
      _identificationService.TrackIdentified += OnTrackIdentified;
    }
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
  public override IReadOnlyDictionary<string, object> Metadata => _metadata;

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

  // IPlayQueue implementation
  /// <inheritdoc/>
  public IReadOnlyList<QueueItem> QueueItems => GetQueueItemsInternal();

  /// <inheritdoc/>
  public int CurrentIndex => _currentIndex;

  /// <inheritdoc/>
  public int Count => _playlist.Count + (_currentFile != null ? 1 : 0);

  /// <inheritdoc/>
  public event EventHandler<QueueChangedEventArgs>? QueueChanged;

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

    if (State == AudioSourceState.Playing || State == AudioSourceState.Paused)
    {
      await StopAsync(cancellationToken);
    }

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
    _originalOrder = new List<string> { fullPath };
    _playedHistory.Clear();
    _errorFiles.Clear();
    _consecutiveSkipCount = 0;
    _currentIndex = -1; // Will be set when LoadCurrentFileAsync is called
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

    if (State == AudioSourceState.Playing || State == AudioSourceState.Paused)
    {
      await StopAsync(cancellationToken);
    }

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

    // Store original order and apply shuffle if enabled
    audioFiles = audioFiles.OrderBy(f => f).ToList();
    _originalOrder = new List<string>(audioFiles);

    if (_preferences.CurrentValue.Shuffle)
    {
      audioFiles = ShuffleList(audioFiles);
    }

    _playlist = new Queue<string>(audioFiles);
    _playedHistory.Clear();
    _errorFiles.Clear();
    _consecutiveSkipCount = 0;
    _currentIndex = -1; // Will be set when LoadCurrentFileAsync is called
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

    if (State == AudioSourceState.Playing || State == AudioSourceState.Paused)
    {
      await StopAsync(cancellationToken);
    }

    var validFiles = files
      .Select(GetFullPath)
      .Where(f => File.Exists(f) && IsAudioFile(f))
      .ToList();

    if (validFiles.Count == 0)
    {
      throw new InvalidOperationException("No valid audio files in playlist");
    }

    // Store original order and apply shuffle if enabled
    _originalOrder = new List<string>(validFiles);

    if (_preferences.CurrentValue.Shuffle)
    {
      validFiles = ShuffleList(validFiles);
    }

    _playlist = new Queue<string>(validFiles);
    _playedHistory.Clear();
    _errorFiles.Clear();
    _consecutiveSkipCount = 0;
    _currentIndex = -1; // Will be set when LoadCurrentFileAsync is called
    await LoadCurrentFileAsync(cancellationToken);

    Logger.LogInformation("Loaded playlist with {Count} files", validFiles.Count);
  }

  /// <inheritdoc/>
  public override async Task NextAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    // Track skip metric only for user-initiated skips (not auto-advance from end-of-track)
    var wasSkipped = State == AudioSourceState.Playing && _currentFile != null && !_trackEndedNaturally;
    var isAutoAdvance = _trackEndedNaturally;
    _trackEndedNaturally = false;

    // Handle RepeatMode.One - only auto-replay on natural track end.
    // User-initiated Next should advance to the next track even in RepeatOne mode.
    if (_preferences.CurrentValue.Repeat == RepeatMode.One && _currentFile != null && isAutoAdvance)
    {
      Logger.LogDebug("Repeat One enabled - replaying current track (auto-advance)");
      _position = TimeSpan.Zero;
      if (State == AudioSourceState.Playing)
      {
        await PlayCoreAsync(cancellationToken);
      }
      return;
    }

    // Add current file to history before moving to next
    lock (_playlistLock)
    {
      if (_currentFile != null && !_playedHistory.Contains(_currentFile))
      {
        _playedHistory.Add(_currentFile);
      }
    }

    // Track skip metric if this was user-initiated
    if (wasSkipped)
    {
      TrackSkipped();
    }

    // Check if playlist has more tracks
    bool hasMoreTracks;
    lock (_playlistLock)
    {
      hasMoreTracks = _playlist.Count > 0;
    }

    if (hasMoreTracks)
    {
      await LoadCurrentFileAsync(cancellationToken);

      if (State == AudioSourceState.Playing)
      {
        Logger.LogInformation("🎵 FILE PLAYER: Auto-advancing to next track: {File}", Path.GetFileName(_currentFile ?? ""));
        await PlayCoreAsync(cancellationToken);
      }
      else
      {
        Logger.LogWarning("🎵 FILE PLAYER: Skipped PlayCoreAsync for next track — state is {State} (expected Playing)",
          State);
      }
      return;
    }

    // Playlist is empty - check repeat mode
    if (_preferences.CurrentValue.Repeat == RepeatMode.All && _originalOrder.Count > 0)
    {
      Logger.LogDebug("Playlist empty but Repeat All enabled - reloading playlist");

      // Rebuild playlist from original order
      var files = new List<string>(_originalOrder);
      if (_preferences.CurrentValue.Shuffle)
      {
        files = ShuffleList(files);
      }

      lock (_playlistLock)
      {
        _playlist = new Queue<string>(files);
        _playedHistory.Clear();
      }
      await LoadCurrentFileAsync(cancellationToken);

      if (State == AudioSourceState.Playing)
      {
        await PlayCoreAsync(cancellationToken);
      }
      return;
    }

    // No repeat or reached end - stop playback
    Logger.LogDebug("Reached end of playlist with no repeat");
    await StopAsync(cancellationToken);
  }

  /// <inheritdoc/>
  public override async Task PreviousAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    // If position > 3 seconds, seek to beginning
    if (_position > TimeSpan.FromSeconds(3))
    {
      Logger.LogDebug("Position > 3 seconds, seeking to beginning");
      await SeekAsync(TimeSpan.Zero, cancellationToken);
      if (State == AudioSourceState.Playing)
      {
        await PlayCoreAsync(cancellationToken);
      }
      return;
    }

    // Go to previous track in history
    string? previousFile;
    lock (_playlistLock)
    {
      if (_playedHistory.Count > 0)
      {
        previousFile = _playedHistory[^1];
        _playedHistory.RemoveAt(_playedHistory.Count - 1);

        // Put current file back at front of playlist if it exists
        if (_currentFile != null)
        {
          var tempList = _playlist.ToList();
          tempList.Insert(0, _currentFile);
          _playlist = new Queue<string>(tempList);
        }

        // Load previous file
        _currentFile = previousFile;
      }
      else
      {
        previousFile = null;
      }
    }

    if (previousFile != null)
    {
      _position = TimeSpan.Zero;
      CleanupDataProvider();

      // Restore file metadata and data provider
      try
      {
        _audioEngine ??= new MiniAudioEngine();
        _fileStream = File.OpenRead(previousFile);
        _dataProvider = new ChunkedDataProvider(_audioEngine, _fileStream);
        Logger.LogDebug("Loaded previous file with SoundFlow: {File}", previousFile);
      }
      catch (Exception ex)
      {
        Logger.LogWarning(ex, "SoundFlow could not decode previous file: {File}", previousFile);
        _fileStream?.Dispose();
        _fileStream = null;
        _dataProvider = null;
      }

      UpdateMetadataFromFile(previousFile);

      if (State == AudioSourceState.Playing)
      {
        await PlayCoreAsync(cancellationToken);
      }

      Logger.LogInformation("Went to previous track: {File}", _currentFile);
      return;
    }

    // No previous track - handle repeat modes
    if (_preferences.CurrentValue.Repeat == RepeatMode.All && _originalOrder.Count > 0)
    {
      Logger.LogDebug("At start of playlist with Repeat All - going to last track");

      // Go to last track in original order
      var lastFile = _originalOrder[^1];
      lock (_playlistLock)
      {
        _currentFile = lastFile;
      }
      _position = TimeSpan.Zero;
      CleanupDataProvider();

      try
      {
        _audioEngine ??= new MiniAudioEngine();
        _fileStream = File.OpenRead(_currentFile);
        _dataProvider = new ChunkedDataProvider(_audioEngine, _fileStream);
      }
      catch (Exception ex)
      {
        Logger.LogWarning(ex, "SoundFlow could not decode file: {File}", _currentFile);
        _fileStream?.Dispose();
        _fileStream = null;
        _dataProvider = null;
      }

      UpdateMetadataFromFile(_currentFile);

      if (State == AudioSourceState.Playing)
      {
        await PlayCoreAsync(cancellationToken);
      }
      return;
    }

    // Already at beginning - just seek to start
    Logger.LogDebug("Already at beginning of playlist");
    await SeekAsync(TimeSpan.Zero, cancellationToken);
  }

  /// <inheritdoc/>
  public override async Task SetShuffleAsync(bool enabled, CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    if (_preferences.CurrentValue.Shuffle == enabled)
    {
      Logger.LogDebug("Shuffle mode already set to {Enabled}", enabled);
      return;
    }

    // Update preference
    _preferences.CurrentValue.Shuffle = enabled;
    Logger.LogInformation("Shuffle mode set to {Enabled}", enabled);

    List<string> remainingTracks;

    lock (_playlistLock)
    {
      // Rebuild playlist with current state
      remainingTracks = _playlist.ToList();

      // Add current file to the list if it exists
      if (_currentFile != null)
      {
        remainingTracks.Insert(0, _currentFile);
      }

      if (enabled)
      {
        // Enable shuffle - randomize remaining tracks except current
        if (_currentFile != null && remainingTracks.Count > 1)
        {
          var current = remainingTracks[0];
          var toShuffle = remainingTracks.Skip(1).ToList();
          toShuffle = ShuffleList(toShuffle);
          remainingTracks = new List<string> { current };
          remainingTracks.AddRange(toShuffle);
        }
        else if (_currentFile == null && remainingTracks.Count > 1)
        {
          // Nothing playing yet - shuffle the entire list
          remainingTracks = ShuffleList(remainingTracks);
        }
      }
      else
      {
        // Disable shuffle - restore original order for remaining tracks
        if (_originalOrder.Count > 0)
        {
          // Find current position in original order
          var currentIndex = _currentFile != null ? _originalOrder.IndexOf(_currentFile) : -1;

          if (currentIndex >= 0)
          {
            // Rebuild playlist with remaining tracks in original order
            remainingTracks = _originalOrder.Skip(currentIndex).ToList();
          }
          else
          {
            // Couldn't find current in original order - just sort what we have
            remainingTracks.Sort();
          }
        }
      }

      // Remove current file from list and rebuild playlist
      if (_currentFile != null && remainingTracks.Count > 0 && remainingTracks[0] == _currentFile)
      {
        remainingTracks.RemoveAt(0);
      }

      _playlist = new Queue<string>(remainingTracks);
    }

    // Notify listeners that the queue order has changed
    OnQueueChanged(new QueueChangedEventArgs
    {
      ChangeType = QueueChangeType.Reordered
    });

    await Task.CompletedTask;
  }

  /// <inheritdoc/>
  public override Task SetRepeatModeAsync(RepeatMode mode, CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    if (_preferences.CurrentValue.Repeat == mode)
    {
      Logger.LogDebug("Repeat mode already set to {Mode}", mode);
      return Task.CompletedTask;
    }

    _preferences.CurrentValue.Repeat = mode;
    Logger.LogInformation("Repeat mode set to {Mode}", mode);
    
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  public override async Task InitializeAsync(CancellationToken cancellationToken = default)
  {
    await base.InitializeAsync(cancellationToken);

    // Restore queue and last played state from preferences
    var prefs = _preferences.CurrentValue;
    
    // Restore queue if it exists
    if (prefs.QueueItems != null && prefs.QueueItems.Count > 0)
    {
      Logger.LogInformation("Restoring queue with {Count} items from preferences", prefs.QueueItems.Count);
      
      // Filter to only existing files
      var validFiles = prefs.QueueItems.Where(File.Exists).ToList();
      
      if (validFiles.Count > 0)
      {
        _originalOrder = new List<string>(validFiles);
        _playlist = new Queue<string>(validFiles);
        _currentIndex = Math.Max(0, Math.Min(prefs.CurrentQueueIndex, validFiles.Count - 1));
        
        // If there's a valid current index, set that as the current file
        if (_currentIndex >= 0 && _currentIndex < validFiles.Count)
        {
          _currentFile = validFiles[_currentIndex];
          
          // Remove all items before current from playlist
          for (int i = 0; i <= _currentIndex; i++)
          {
            if (_playlist.Count > 0 && _playlist.Peek() == validFiles[i])
            {
              _playlist.Dequeue();
            }
          }
          
          _position = TimeSpan.FromMilliseconds(prefs.SongPositionMs);
          _pendingSeekMs = prefs.SongPositionMs;
          UpdateMetadataFromFile(_currentFile);
          Logger.LogInformation("Restored queue position at index {Index}: {File} (seek to {Ms}ms)",
            _currentIndex, Path.GetFileName(_currentFile), prefs.SongPositionMs);
        }
        else if (_playlist.Count > 0)
        {
          // No valid current index, load first file from queue
          await LoadCurrentFileAsync(cancellationToken);
        }
      }
      else
      {
        Logger.LogWarning("Queue restoration skipped: no valid files found in saved queue");
      }
    }
    else if (!string.IsNullOrEmpty(prefs.LastSongPlayed) && File.Exists(prefs.LastSongPlayed))
    {
      // Fallback to old behavior: restore just the last played file
      Logger.LogDebug("Restoring last played file: {File}", prefs.LastSongPlayed);
      _currentFile = prefs.LastSongPlayed;
      _position = TimeSpan.FromMilliseconds(prefs.SongPositionMs);
      UpdateMetadataFromFile(_currentFile);
    }

    State = AudioSourceState.Ready;
  }

  /// <inheritdoc/>
  protected override async Task PlayCoreAsync(CancellationToken cancellationToken)
  {
    // Auto-load from queue if no file is currently loaded
    if (_currentFile == null)
    {
      if (_playlist.Count > 0)
      {
        Logger.LogDebug("No file loaded, auto-loading first file from queue");
        await LoadCurrentFileAsync(cancellationToken);

        // Verify file was loaded successfully
        if (_currentFile == null)
        {
          throw new InvalidOperationException("Failed to load file from queue");
        }
      }
      else
      {
        throw new InvalidOperationException("No file loaded and queue is empty");
      }
    }

    // Stop any existing playback before starting new playback
    // This prevents multiple audio streams playing simultaneously
    if (_playbackService != null && !string.IsNullOrEmpty(_playbackId))
    {
      Logger.LogDebug("Stopping existing playback before starting new playback: {PlaybackId}", _playbackId);
      await _playbackService.StopAsync(_playbackId, cancellationToken);
    }

    // Cancel any existing playback monitoring
    if (_playbackCts != null)
    {
      await _playbackCts.CancelAsync();
      _playbackCts.Dispose();
    }

    // Generate playback ID for this session
    _playbackId = $"file-player-{Guid.NewGuid():N}";
    _playbackCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

    var fileName = Path.GetFileName(_currentFile);
    Logger.LogInformation(
      "🎵 FILE PLAYER: Starting playback - \"{FileName}\" (Duration: {Duration}, Volume: {Volume:P0})",
      fileName, _duration, Volume);

    // Use SoundFlow playback service if available
    if (_playbackService != null)
    {
      var fullPath = GetFullPath(_currentFile);
      var success = await _playbackService.PlayFileAsync(
        _playbackId,
        fullPath,
        Volume,
        _playbackCts.Token);

      if (!success)
      {
        Logger.LogError("🎵 FILE PLAYER: Failed to start SoundFlow playback for \"{FileName}\"", fileName);
        _errorFiles.Add(GetFullPath(_currentFile));
        await AutoSkipToNextAsync(cancellationToken);
        return;
      }

      // Successful playback start — reset consecutive skip counter
      _consecutiveSkipCount = 0;

      // Restore persisted playback position if this is the first play after queue restoration
      if (_pendingSeekMs > 0)
      {
        try
        {
          await SeekAsync(TimeSpan.FromMilliseconds(_pendingSeekMs), cancellationToken);
          Logger.LogInformation("🎵 FILE PLAYER: Restored playback position to {Ms}ms", _pendingSeekMs);
        }
        catch (Exception ex)
        {
          Logger.LogWarning(ex, "Failed to seek to persisted position {Ms}ms", _pendingSeekMs);
        }
        _pendingSeekMs = 0;
      }

      Logger.LogInformation(
        "🎵 FILE PLAYER AUDIO FLOW STARTED: \"{FileName}\" routed to SoundFlow (PlaybackId={PlaybackId})",
        fileName, _playbackId);

      // Start playback monitoring task
      _playbackMonitorTask = MonitorPlaybackAsync(_playbackCts.Token);
    }
    else
    {
      Logger.LogWarning("🎵 FILE PLAYER: SoundFlow playback service not available, playback simulation only");
    }
  }

  private async Task MonitorPlaybackAsync(CancellationToken cancellationToken)
  {
    try
    {
      var interval = TimeSpan.FromSeconds(1);
      while (!cancellationToken.IsCancellationRequested)
      {
        if (State == AudioSourceState.Playing)
        {
          if (_duration > TimeSpan.Zero)
          {
            _position = _position.Add(interval);
            if (_position >= _duration)
            {
              Logger.LogDebug("🎵 FILE PLAYER: Position {Position} >= Duration {Duration}, track ended",
                _position, _duration);
              break;
            }
          }
          else if (_playbackService != null && _playbackId != null && !_playbackService.IsPlaying(_playbackId))
          {
            Logger.LogDebug("🎵 FILE PLAYER: SoundFlow reports not playing, track ended");
            break;
          }
        }
        await Task.Delay(interval, cancellationToken);
      }

      if (!cancellationToken.IsCancellationRequested)
      {
        Logger.LogInformation("🎵 FILE PLAYER: Track ended naturally, auto-advancing to next");
        _trackEndedNaturally = true;
        OnPlaybackCompleted(PlaybackCompletionReason.EndOfContent);

        // Auto-advance to next track. Keep state as Playing so NextAsync sees it
        // and starts playback of the next file. If no more tracks exist, NextAsync
        // calls StopAsync which sets state to Stopped.
        try
        {
          await NextAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
          Logger.LogError(ex, "🎵 FILE PLAYER: Error during auto-advance to next track");
        }
      }
    }
    catch (OperationCanceledException)
    {
      // Playback was stopped by user
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Error in playback monitoring");
    }
  }

  /// <inheritdoc/>
  protected override Task PauseCoreAsync(CancellationToken cancellationToken)
  {
    Logger.LogDebug("Pausing file playback at {Position}", _position);

    if (_playbackService != null && _playbackId != null)
    {
      _playbackService.Pause(_playbackId);
    }

    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  protected override Task ResumeCoreAsync(CancellationToken cancellationToken)
  {
    Logger.LogDebug("Resuming file playback from {Position}", _position);

    if (_playbackService != null && _playbackId != null)
    {
      _playbackService.Resume(_playbackId);
    }

    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  protected override async Task StopCoreAsync(CancellationToken cancellationToken)
  {
    var fileName = _currentFile != null ? Path.GetFileName(_currentFile) : "unknown";
    Logger.LogInformation(
      "🎵 FILE PLAYER AUDIO FLOW STOPPING: \"{FileName}\" (PlaybackId={PlaybackId}, Position={Position})",
      fileName, _playbackId ?? "none", _position);

    // Whether this stop is tearing down a real playback. Captured before
    // _playbackId is cleared below, and used to keep the resume position write
    // idempotent: this method zeroes _position but deliberately leaves
    // _currentFile set, so a second stop would otherwise re-enter the save
    // branch with _position == 0 and overwrite a good resume point with zero.
    var hadLivePlayback = _playbackId != null;

    // Cancel playback monitoring
    _playbackCts?.Cancel();

    // Stop SoundFlow playback
    if (_playbackService != null && _playbackId != null)
    {
      await _playbackService.StopAsync(_playbackId, cancellationToken);
      Logger.LogInformation(
        "🎵 FILE PLAYER AUDIO FLOW STOPPED: \"{FileName}\" removed from SoundFlow",
        fileName);
    }

    // Cleared regardless of whether a playback service is wired: _playbackId
    // means "this source has a live playback session", and leaving it set after
    // a stop makes the resume-position guard below read true forever.
    _playbackId = null;

    // Save current position for next session
    if (_currentFile != null && hadLivePlayback)
    {
      _preferences.CurrentValue.LastSongPlayed = _currentFile;
      _preferences.CurrentValue.SongPositionMs = (long)_position.TotalMilliseconds;
    }

    _position = TimeSpan.Zero;
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
  protected override void OnVolumeChanged(float volume)
  {
    // Apply volume to SoundFlow playback
    if (_playbackService != null && _playbackId != null)
    {
      _playbackService.SetVolume(_playbackId, volume);
      Logger.LogDebug("Volume changed to {Volume}", volume);
    }
  }

  /// <inheritdoc/>
  protected override async ValueTask DisposeAsyncCore()
  {
    // Cancel playback monitoring
    _playbackCts?.Cancel();

    // Await the playback monitor task to complete before disposing resources
    if (_playbackMonitorTask != null)
    {
      try
      {
        await _playbackMonitorTask;
      }
      catch (OperationCanceledException)
      {
        // Expected when cancellation is requested
      }
      catch (Exception ex)
      {
        Logger.LogWarning(ex, "Error awaiting playback monitor task during disposal");
      }
      _playbackMonitorTask = null;
    }

    _playbackCts?.Dispose();
    _playbackCts = null;

    // Stop SoundFlow playback
    if (_playbackService != null && _playbackId != null)
    {
      await _playbackService.StopAsync(_playbackId);
      _playbackId = null;
    }

    // Unsubscribe from events
    if (_identificationService != null)
    {
      _identificationService.TrackIdentified -= OnTrackIdentified;
    }

    // Save state for next session.
    if (_currentFile != null)
    {
      _preferences.CurrentValue.LastSongPlayed = _currentFile;

      // Only overwrite the resume position when this dispose is tearing down
      // live playback. AudioManager.DisposeAsync stops the source and THEN
      // disposes it, and StopCoreAsync zeroes _position on the way out — so an
      // unconditional write here overwrote the just-saved resume point with
      // zero on every clean shutdown.
      if (_playbackId != null)
      {
        _preferences.CurrentValue.SongPositionMs = (long)_position.TotalMilliseconds;
      }
    }

    CleanupDataProvider();
    _currentFile = null;
    _playlist.Clear();
    _originalOrder.Clear();
    _playedHistory.Clear();

    await base.DisposeAsyncCore();
  }

  /// <summary>
  /// Attempts to skip to the next track in the playlist.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>True if there was a next track; false if the playlist is empty.</returns>
  public async Task<bool> TryNextAsync(CancellationToken cancellationToken = default)
  {
    await NextAsync(cancellationToken);
    return _currentFile != null;
  }

  // IPlayQueue implementation methods

  /// <inheritdoc/>
  public Task<IReadOnlyList<QueueItem>> GetQueueAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    return Task.FromResult<IReadOnlyList<QueueItem>>(GetQueueItemsInternal());
  }

  /// <inheritdoc/>
  public Task<IReadOnlyList<QueueItem>> GetFullPlaylistAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    return Task.FromResult<IReadOnlyList<QueueItem>>(GetFullPlaylistInternal());
  }

  /// <inheritdoc/>
  public async Task JumpToFullPlaylistIndexAsync(int index, CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    var fullList = GetFullPlaylistInOrder();
    if (index < 0 || index >= fullList.Count)
    {
      throw new ArgumentOutOfRangeException(nameof(index), "Index is out of range in the full playlist");
    }

    var targetFile = fullList[index];

    // Don't jump to error files
    if (_errorFiles.Contains(targetFile))
    {
      Logger.LogWarning("Cannot jump to error file: {File}", Path.GetFileName(targetFile));
      return;
    }

    Logger.LogInformation("Jumping to full playlist index {Index}: {Track}", index, Path.GetFileName(targetFile));

    // Rebuild played history = everything before target
    _playedHistory = fullList.Take(index).ToList();

    // Current = target
    _currentFile = targetFile;
    _currentIndex = 0; // Operational index within the upcoming queue
    _position = TimeSpan.Zero;

    // Rebuild upcoming queue = everything after target
    _playlist = new Queue<string>(fullList.Skip(index + 1));

    // Load the file
    CleanupDataProvider();

    try
    {
      _audioEngine ??= new MiniAudioEngine();
      _fileStream = File.OpenRead(_currentFile);
      _dataProvider = new ChunkedDataProvider(_audioEngine, _fileStream);
      Logger.LogDebug("Loaded file with SoundFlow: {File}", _currentFile);
    }
    catch (Exception ex)
    {
      Logger.LogWarning(ex, "SoundFlow could not decode file: {File}", _currentFile);
      _errorFiles.Add(_currentFile);
      _fileStream?.Dispose();
      _fileStream = null;
      _dataProvider = null;
    }

    UpdateMetadataFromFile(_currentFile);

    // Start playback
    if (State != AudioSourceState.Playing)
    {
      State = AudioSourceState.Playing;
    }
    await PlayCoreAsync(cancellationToken);

    // Raise QueueChanged event
    OnQueueChanged(new QueueChangedEventArgs
    {
      ChangeType = QueueChangeType.CurrentChanged,
      AffectedIndex = index
    });
  }

  /// <inheritdoc/>
  public async Task AddToQueueAsync(string trackIdentifier, int? position = null, CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    var fullPath = GetFullPath(trackIdentifier);
    if (!File.Exists(fullPath))
    {
      throw new FileNotFoundException($"Audio file not found: {fullPath}", fullPath);
    }

    if (!IsAudioFile(fullPath))
    {
      throw new ArgumentException($"Unsupported audio format: {Path.GetExtension(fullPath)}", nameof(trackIdentifier));
    }

    // Get all tracks (current + queue)
    var allTracks = GetAllTracksInOrder();

    if (position.HasValue)
    {
      // Insert at specified position in the full queue
      if (position.Value < 0 || position.Value > allTracks.Count)
      {
        throw new ArgumentOutOfRangeException(nameof(position), "Position is out of range");
      }
      allTracks.Insert(position.Value, fullPath);
    }
    else
    {
      // Add to end
      allTracks.Add(fullPath);
    }

    // Also add to original order for shuffle/repeat support
    if (!_originalOrder.Contains(fullPath))
    {
      _originalOrder.Add(fullPath);
    }

    // Rebuild queue from all tracks, keeping current position
    var newCurrentIndex = _currentFile != null ? allTracks.IndexOf(_currentFile) : -1;
    RebuildQueueFromList(allTracks, newCurrentIndex);

    var actualIndex = position ?? allTracks.Count - 1;
    Logger.LogInformation("Added track to queue: {Track} at position {Position}", Path.GetFileName(fullPath), actualIndex);

    // Raise QueueChanged event
    OnQueueChanged(new QueueChangedEventArgs
    {
      ChangeType = QueueChangeType.Added,
      AffectedIndex = actualIndex,
      AffectedItem = CreateQueueItem(fullPath, actualIndex, false)
    });

    await Task.CompletedTask;
  }

  /// <inheritdoc/>
  public async Task RemoveFromQueueAsync(int index, CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    var allTracks = GetAllTracksInOrder();

    if (index < 0 || index >= allTracks.Count)
    {
      throw new ArgumentOutOfRangeException(nameof(index), "Index is out of range");
    }

    var removedFile = allTracks[index];
    var removedItem = CreateQueueItem(removedFile, index, index == _currentIndex);

    // Remove from all tracks list
    allTracks.RemoveAt(index);

    // If removing current item, skip to next
    if (index == _currentIndex)
    {
      Logger.LogInformation("Removing current item, skipping to next");
      
      // If there are more tracks, load the next one (which is now at the same index)
      if (allTracks.Count > 0)
      {
        var nextIndex = Math.Min(index, allTracks.Count - 1);
        var nextFile = allTracks[nextIndex];
        
        // Set current file and update queue
        _currentFile = nextFile;
        _currentIndex = nextIndex;
        _playlist = new Queue<string>(allTracks.Skip(nextIndex + 1));
        _position = TimeSpan.Zero;
        
        // Load the file
        CleanupDataProvider();
        try
        {
          _audioEngine ??= new MiniAudioEngine();
          _fileStream = File.OpenRead(_currentFile);
          _dataProvider = new ChunkedDataProvider(_audioEngine, _fileStream);
          Logger.LogDebug("Loaded file with SoundFlow: {File}", _currentFile);
        }
        catch (Exception ex)
        {
          Logger.LogWarning(ex, "SoundFlow could not decode file: {File}", _currentFile);
          TrackPlaybackError();
          _fileStream?.Dispose();
          _fileStream = null;
          _dataProvider = null;
        }
        
        UpdateMetadataFromFile(_currentFile);
        
        if (State == AudioSourceState.Playing)
        {
          await PlayCoreAsync(cancellationToken);
        }
      }
      else
      {
        // No more tracks, stop playback
        await StopAsync(cancellationToken);
        _currentFile = null;
        _currentIndex = -1;
        _playlist.Clear();
      }
    }
    else
    {
      // Not removing current item, just update the queue
      // Adjust current index if needed
      var currentFile = _currentFile;
      var newCurrentIndex = currentFile != null ? allTracks.IndexOf(currentFile) : -1;
      
      RebuildQueueFromList(allTracks, newCurrentIndex);
    }

    Logger.LogInformation("Removed track from queue at index {Index}: {Track}", index, Path.GetFileName(removedFile));

    // Raise QueueChanged event
    OnQueueChanged(new QueueChangedEventArgs
    {
      ChangeType = QueueChangeType.Removed,
      AffectedIndex = index,
      AffectedItem = removedItem
    });

    await Task.CompletedTask;
  }

  /// <inheritdoc/>
  public async Task ClearQueueAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    Logger.LogInformation("Clearing queue");

    // Stop playback
    await StopAsync(cancellationToken);

    // Clear all internal state
    _playlist.Clear();
    _originalOrder.Clear();
    _playedHistory.Clear();
    _errorFiles.Clear();
    _consecutiveSkipCount = 0;
    _currentFile = null;
    _currentIndex = -1;

    // Raise QueueChanged event
    OnQueueChanged(new QueueChangedEventArgs
    {
      ChangeType = QueueChangeType.Cleared
    });
  }

  /// <inheritdoc/>
  public async Task MoveQueueItemAsync(int fromIndex, int toIndex, CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    var allTracks = GetAllTracksInOrder();

    if (fromIndex < 0 || fromIndex >= allTracks.Count)
    {
      throw new ArgumentOutOfRangeException(nameof(fromIndex), "From index is out of range");
    }

    if (toIndex < 0 || toIndex >= allTracks.Count)
    {
      throw new ArgumentOutOfRangeException(nameof(toIndex), "To index is out of range");
    }

    if (fromIndex == toIndex)
    {
      return; // No-op
    }

    var movedFile = allTracks[fromIndex];
    var movedItem = CreateQueueItem(movedFile, toIndex, false);

    // Remove from old position
    allTracks.RemoveAt(fromIndex);
    
    // Insert at new position
    allTracks.Insert(toIndex, movedFile);

    // Update current index if needed
    var newCurrentIndex = _currentIndex;
    if (_currentIndex == fromIndex)
    {
      // Moving the current item
      newCurrentIndex = toIndex;
    }
    else if (fromIndex < _currentIndex && toIndex >= _currentIndex)
    {
      // Moving an item from before current to after current
      newCurrentIndex--;
    }
    else if (fromIndex > _currentIndex && toIndex <= _currentIndex)
    {
      // Moving an item from after current to before current
      newCurrentIndex++;
    }

    RebuildQueueFromList(allTracks, newCurrentIndex);

    Logger.LogInformation("Moved track from index {FromIndex} to {ToIndex}", fromIndex, toIndex);

    // Raise QueueChanged event
    OnQueueChanged(new QueueChangedEventArgs
    {
      ChangeType = QueueChangeType.Moved,
      AffectedIndex = toIndex,
      AffectedItem = movedItem
    });

    await Task.CompletedTask;
  }

  /// <inheritdoc/>
  public async Task JumpToIndexAsync(int index, CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    var allTracks = GetAllTracksInOrder();

    if (index < 0 || index >= allTracks.Count)
    {
      throw new ArgumentOutOfRangeException(nameof(index), "Index is out of range");
    }

    var targetFile = allTracks[index];

    Logger.LogInformation("Jumping to queue index {Index}: {Track}", index, Path.GetFileName(targetFile));

    // Update current file and index
    _currentFile = targetFile;
    _currentIndex = index;
    _position = TimeSpan.Zero;

    // Rebuild queue to start from this position
    RebuildQueueFromList(allTracks, index);

    // Load the file
    CleanupDataProvider();

    try
    {
      _audioEngine ??= new MiniAudioEngine();
      _fileStream = File.OpenRead(_currentFile);
      _dataProvider = new ChunkedDataProvider(_audioEngine, _fileStream);
      Logger.LogDebug("Loaded file with SoundFlow: {File}", _currentFile);
    }
    catch (Exception ex)
    {
      Logger.LogWarning(ex, "SoundFlow could not decode file: {File}", _currentFile);
      _fileStream?.Dispose();
      _fileStream = null;
      _dataProvider = null;
    }

    UpdateMetadataFromFile(_currentFile);

    // Start playback
    if (State != AudioSourceState.Playing)
    {
      State = AudioSourceState.Playing;
    }
    await PlayCoreAsync(cancellationToken);

    // Raise QueueChanged event
    OnQueueChanged(new QueueChangedEventArgs
    {
      ChangeType = QueueChangeType.CurrentChanged,
      AffectedIndex = index,
      AffectedItem = CreateQueueItem(targetFile, index, true)
    });
  }

  /// <summary>
  /// Shuffles a list using the Fisher-Yates algorithm.
  /// </summary>
  /// <param name="list">The list to shuffle.</param>
  /// <returns>A new shuffled list.</returns>
  private static List<string> ShuffleList(List<string> list)
  {
    var shuffled = new List<string>(list);
    var random = Random.Shared;
    var n = shuffled.Count;
    
    // Fisher-Yates shuffle algorithm
    for (var i = n - 1; i > 0; i--)
    {
      var j = random.Next(i + 1);
      (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
    }
    
    return shuffled;
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

  /// <summary>
  /// Gets all tracks in order (current + queue).
  /// </summary>
  private List<string> GetAllTracksInOrder()
  {
    lock (_playlistLock)
    {
      var allTracks = new List<string>();
      if (_currentFile != null)
      {
        allTracks.Add(_currentFile);
      }
      allTracks.AddRange(_playlist);
      return allTracks;
    }
  }

  /// <summary>
  /// Gets the full playlist in order: played + current + upcoming.
  /// </summary>
  private List<string> GetFullPlaylistInOrder()
  {
    lock (_playlistLock)
    {
      var fullList = new List<string>(_playedHistory);
      if (_currentFile != null)
      {
        fullList.Add(_currentFile);
      }
      fullList.AddRange(_playlist);
      return fullList;
    }
  }

  /// <summary>
  /// Builds the full playlist with state annotations for each item.
  /// </summary>
  private List<QueueItem> GetFullPlaylistInternal()
  {
    // Snapshot collections under lock to avoid concurrent modification from skip/next
    string[] playedSnapshot;
    string? currentSnapshot;
    string[] playlistSnapshot;
    HashSet<string> errorSnapshot;

    lock (_playlistLock)
    {
      playedSnapshot = _playedHistory.ToArray();
      currentSnapshot = _currentFile;
      playlistSnapshot = _playlist.ToArray();
      errorSnapshot = new HashSet<string>(_errorFiles);
    }

    var items = new List<QueueItem>();
    var fullPlaylistIndex = 0;

    // Played items
    foreach (var file in playedSnapshot)
    {
      var state = errorSnapshot.Contains(file) ? QueueItemState.Error : QueueItemState.Played;
      items.Add(CreateQueueItemWithState(file, fullPlaylistIndex, state));
      fullPlaylistIndex++;
    }

    // Current item
    if (currentSnapshot != null)
    {
      var state = errorSnapshot.Contains(currentSnapshot) ? QueueItemState.Error : QueueItemState.Current;
      items.Add(CreateQueueItemWithState(currentSnapshot, fullPlaylistIndex, state));
      fullPlaylistIndex++;
    }

    // Upcoming items
    foreach (var file in playlistSnapshot)
    {
      var state = errorSnapshot.Contains(file) ? QueueItemState.Error : QueueItemState.Upcoming;
      items.Add(CreateQueueItemWithState(file, fullPlaylistIndex, state));
      fullPlaylistIndex++;
    }

    return items;
  }

  /// <summary>
  /// Auto-skips to the next track when playback fails, with infinite-loop protection.
  /// </summary>
  private async Task AutoSkipToNextAsync(CancellationToken cancellationToken)
  {
    _consecutiveSkipCount++;

    if (_consecutiveSkipCount > _originalOrder.Count)
    {
      Logger.LogError("Auto-skip limit reached ({Count} consecutive failures) — stopping playback", _consecutiveSkipCount);
      State = AudioSourceState.Error;
      return;
    }

    Logger.LogWarning("Auto-skipping unplayable file (consecutive skip #{Count})", _consecutiveSkipCount);
    await NextAsync(cancellationToken);
  }

  /// <summary>
  /// Rebuilds the queue from a list of tracks, setting the current file at the specified index.
  /// </summary>
  private void RebuildQueueFromList(List<string> allTracks, int currentIndex)
  {
    if (currentIndex >= 0 && currentIndex < allTracks.Count)
    {
      _currentFile = allTracks[currentIndex];
      _currentIndex = currentIndex;
      _playlist = new Queue<string>(allTracks.Skip(currentIndex + 1));
    }
    else
    {
      _currentFile = null;
      _currentIndex = -1;
      _playlist = new Queue<string>(allTracks);
    }
  }

  /// <summary>
  /// Gets the queue items for the IPlayQueue interface.
  /// </summary>
  private List<QueueItem> GetQueueItemsInternal()
  {
    var items = new List<QueueItem>();
    var allTracks = GetAllTracksInOrder();

    for (var i = 0; i < allTracks.Count; i++)
    {
      var file = allTracks[i];
      items.Add(CreateQueueItem(file, i, i == _currentIndex));
    }

    return items;
  }

  /// <summary>
  /// Creates a QueueItem from a file path.
  /// </summary>
  private QueueItem CreateQueueItem(string filePath, int index, bool isCurrent)
  {
    // Read metadata from file using SoundFlow
    var title = Path.GetFileNameWithoutExtension(filePath);
    var artist = "--";
    var album = "--";
    TimeSpan? duration = null;

    try
    {
      var result = SoundMetadataReader.Read(filePath);
      if (result.IsSuccess && result.Value != null)
      {
        var formatInfo = result.Value;

        // Use TagLib for accurate duration (SoundFlow miscalculates VBR MP3s)
        duration = AccurateDurationReader.GetDuration(filePath, Logger);
        if (duration == null && formatInfo.Duration != TimeSpan.Zero)
        {
          duration = formatInfo.Duration;
        }

        if (formatInfo.Tags != null)
        {
          if (!string.IsNullOrEmpty(formatInfo.Tags.Title))
          {
            title = formatInfo.Tags.Title;
          }
          if (!string.IsNullOrEmpty(formatInfo.Tags.Artist))
          {
            artist = formatInfo.Tags.Artist;
          }
          if (!string.IsNullOrEmpty(formatInfo.Tags.Album))
          {
            album = formatInfo.Tags.Album;
          }
        }
      }
    }
    catch (Exception ex)
    {
      Logger.LogDebug(ex, "Failed to read metadata for {File}, using defaults", filePath);
    }

    // Populate album art from embedded picture tags so Up Next tiles render real art
    // instead of falling back to the music_note placeholder. Content-addressed cache
    // makes repeated reads idempotent.
    var albumArtUrl = TryGetEmbeddedAlbumArtUrl(filePath);

    return new QueueItem
    {
      Id = filePath,
      Title = title,
      Artist = artist,
      Album = album,
      Duration = duration,
      AlbumArtUrl = albumArtUrl,
      Index = index,
      IsCurrent = isCurrent,
      State = isCurrent ? QueueItemState.Current : QueueItemState.Upcoming,
      FullPlaylistIndex = index
    };
  }

  /// <summary>
  /// Creates a QueueItem with an explicit state and full playlist index.
  /// </summary>
  private QueueItem CreateQueueItemWithState(string filePath, int fullPlaylistIndex, QueueItemState state)
  {
    var title = Path.GetFileNameWithoutExtension(filePath);
    var artist = "--";
    var album = "--";
    TimeSpan? duration = null;

    try
    {
      var result = SoundMetadataReader.Read(filePath);
      if (result.IsSuccess && result.Value != null)
      {
        var formatInfo = result.Value;

        duration = AccurateDurationReader.GetDuration(filePath, Logger);
        if (duration == null && formatInfo.Duration != TimeSpan.Zero)
        {
          duration = formatInfo.Duration;
        }

        if (formatInfo.Tags != null)
        {
          if (!string.IsNullOrEmpty(formatInfo.Tags.Title))
          {
            title = formatInfo.Tags.Title;
          }
          if (!string.IsNullOrEmpty(formatInfo.Tags.Artist))
          {
            artist = formatInfo.Tags.Artist;
          }
          if (!string.IsNullOrEmpty(formatInfo.Tags.Album))
          {
            album = formatInfo.Tags.Album;
          }
        }
      }
    }
    catch (Exception ex)
    {
      Logger.LogDebug(ex, "Failed to read metadata for {File}, using defaults", filePath);
    }

    // Populate album art from embedded picture tags so Up Next / Recent tiles render real
    // art instead of falling back to the music_note placeholder. Content-addressed cache
    // makes repeated reads idempotent.
    var albumArtUrl = TryGetEmbeddedAlbumArtUrl(filePath);

    return new QueueItem
    {
      Id = filePath,
      Title = title,
      Artist = artist,
      Album = album,
      Duration = duration,
      AlbumArtUrl = albumArtUrl,
      Index = fullPlaylistIndex, // For operational compatibility
      IsCurrent = state == QueueItemState.Current,
      State = state,
      FullPlaylistIndex = fullPlaylistIndex
    };
  }

  /// <summary>
  /// Raises the QueueChanged event and saves queue state to preferences.
  /// </summary>
  private void OnQueueChanged(QueueChangedEventArgs args)
  {
    QueueChanged?.Invoke(this, args);
    SaveQueueStateToPreferences();
  }

  /// <summary>
  /// Saves the current queue state to preferences for persistence.
  /// </summary>
  private void SaveQueueStateToPreferences()
  {
    try
    {
      // Get all tracks in order (current + queue)
      var allTracks = GetAllTracksInOrder();

      // Save queue state using IConfigurationManager if available
      // This ensures proper persistence rather than relying on mutating IOptionsMonitor snapshots
      if (_configurationManager != null)
      {
        // Fire-and-forget async save to avoid blocking
        _ = SaveQueueStateAsync(allTracks, _currentIndex);
      }
      else
      {
        // Fallback: Try to mutate preferences snapshot (may not persist reliably)
        _preferences.CurrentValue.QueueItems = allTracks;
        _preferences.CurrentValue.CurrentQueueIndex = _currentIndex;
      }

      Logger.LogDebug("Saved queue state: {Count} items, current index: {Index}", allTracks.Count, _currentIndex);
    }
    catch (Exception ex)
    {
      Logger.LogWarning(ex, "Failed to save queue state to preferences");
    }
  }

  /// <summary>
  /// Asynchronously saves queue state to configuration store.
  /// </summary>
  private async Task SaveQueueStateAsync(List<string> queueItems, int currentIndex)
  {
    if (_configurationManager == null)
    {
      return;
    }

    try
    {
      var mainStoreId = _configurationManager.CurrentStoreType ==
        Radio.Configuration.Models.ConfigurationStoreType.Sqlite ? "sqlite" : "config";

      IConfigurationStore store;
      try
      {
        store = await _configurationManager.GetStoreAsync(mainStoreId);
      }
      catch
      {
        store = await _configurationManager.CreateStoreAsync(mainStoreId);
      }

      // Serialize queue items as JSON array
      var queueJson = System.Text.Json.JsonSerializer.Serialize(queueItems);

      var entries = new List<ConfigurationEntry>
      {
        new() { Key = $"{FilePlayerPreferences.SectionName}:QueueItems", Value = queueJson },
        new() { Key = $"{FilePlayerPreferences.SectionName}:CurrentQueueIndex", Value = currentIndex.ToString() }
      };

      await store.SetEntriesAsync(entries);
      await store.SaveAsync();

      Logger.LogTrace("Queue state persisted to configuration store: {Count} items", queueItems.Count);
    }
    catch (Exception ex)
    {
      Logger.LogWarning(ex, "Failed to save queue state to configuration store");
    }
  }

  private string GetFullPath(string path)
  {
    // If path is already absolute, return it as-is (normalized)
    if (Path.IsPathRooted(path))
    {
      return Path.GetFullPath(path);
    }

    // Otherwise, treat as relative path and combine with root directory
    var rootDirectory = _options.CurrentValue.RootDirectory;
    string basePath;
    if (!string.IsNullOrEmpty(_rootDir))
    {
      basePath = Path.Combine(_rootDir, rootDirectory, path);
    }
    else
    {
      basePath = Path.Combine(rootDirectory, path);
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
    string? fileToLoad;
    lock (_playlistLock)
    {
      if (_playlist.Count == 0)
      {
        _currentFile = null;
        _currentIndex = -1;
        fileToLoad = null;
      }
      else
      {
        _currentFile = _playlist.Dequeue();
        _currentIndex = 0;
        fileToLoad = _currentFile;
      }
    }

    if (fileToLoad == null)
    {
      OnPlaybackCompleted(PlaybackCompletionReason.EndOfContent);
      return;
    }

    _position = TimeSpan.Zero;

    // Clean up previous data provider
    CleanupDataProvider();

    // Try to initialize SoundFlow audio engine and create a data provider
    try
    {
      _audioEngine ??= new MiniAudioEngine();

      // Create a data provider from the file using SoundFlow
      // Note: We keep the FileStream open (stored as field) because ChunkedDataProvider needs it
      _fileStream = File.OpenRead(fileToLoad);
      _dataProvider = new ChunkedDataProvider(_audioEngine, _fileStream);

      Logger.LogDebug("Loaded file with SoundFlow: {File}", fileToLoad);
    }
    catch (Exception ex)
    {
      // SoundFlow couldn't decode the file - this could happen with unsupported formats
      // or during testing with dummy files. Log and continue without a data provider.
      Logger.LogWarning(ex, "SoundFlow could not decode file: {File}. Using basic file info only.", fileToLoad);
      lock (_playlistLock) { _errorFiles.Add(fileToLoad); }
      _fileStream?.Dispose();
      _fileStream = null;
      _dataProvider = null;
    }

    // Read metadata from the file (this uses SoundMetadataReader which is separate from decoding)
    UpdateMetadataFromFile(fileToLoad);

    Logger.LogDebug("Loaded file: {File}", fileToLoad);

    if (State == AudioSourceState.Created)
    {
      State = AudioSourceState.Ready;
    }

    // Raise queue changed event for current item change
    OnQueueChanged(new QueueChangedEventArgs
    {
      ChangeType = QueueChangeType.CurrentChanged,
      AffectedIndex = _currentIndex,
      AffectedItem = CreateQueueItem(fileToLoad, _currentIndex, true)
    });

    await Task.CompletedTask;
  }

  private void UpdateMetadataFromFile(string filePath)
  {
    _metadata.Clear();
    
    // Set default values first
    _metadata[StandardMetadataKeys.Title] = Path.GetFileNameWithoutExtension(filePath);
    _metadata[StandardMetadataKeys.Artist] = StandardMetadataKeys.DefaultArtist;
    _metadata[StandardMetadataKeys.Album] = StandardMetadataKeys.DefaultAlbum;
    _metadata[StandardMetadataKeys.AlbumArtUrl] = StandardMetadataKeys.DefaultAlbumArtUrl;
    
    // Additional file info (not standard metadata)
    _metadata["FileName"] = Path.GetFileName(filePath);
    _metadata["Directory"] = Path.GetDirectoryName(filePath) ?? "";
    _metadata["Extension"] = Path.GetExtension(filePath);
    // Full path — consumed by the Web layer's DisplayNames.Track helper when the
    // metadata reader produced a generic "Track N" title and the original filename
    // is the best available source for a human-readable display name.
    _metadata["FilePath"] = filePath;

    // Use SoundFlow's metadata reader to get audio tags
    try
    {
      var result = SoundMetadataReader.Read(filePath);
      if (result.IsSuccess && result.Value != null)
      {
        var formatInfo = result.Value;

        // Use TagLib for accurate duration (SoundFlow miscalculates VBR MP3s)
        var accurateDuration = AccurateDurationReader.GetDuration(filePath, Logger);
        if (accurateDuration != null)
        {
          _duration = accurateDuration.Value;
        }
        else if (formatInfo.Duration != TimeSpan.Zero)
        {
          _duration = formatInfo.Duration;
        }
        else
        {
          _duration = TimeSpan.Zero;
        }

        _metadata[StandardMetadataKeys.Duration] = _duration;
        _metadata["SampleRate"] = formatInfo.SampleRate;
        _metadata["Channels"] = formatInfo.ChannelCount;
        _metadata["BitRate"] = formatInfo.Bitrate;

        // Extract embedded album art via TagLib (SoundFlow doesn't read pictures)
        ExtractEmbeddedAlbumArt(filePath);

        // Get tags (Title, Artist, Album, etc.) - override defaults if available
        if (formatInfo.Tags != null)
        {
          if (!string.IsNullOrEmpty(formatInfo.Tags.Title))
          {
            _metadata[StandardMetadataKeys.Title] = formatInfo.Tags.Title;
          }
          if (!string.IsNullOrEmpty(formatInfo.Tags.Artist))
          {
            _metadata[StandardMetadataKeys.Artist] = formatInfo.Tags.Artist;
          }
          if (!string.IsNullOrEmpty(formatInfo.Tags.Album))
          {
            _metadata[StandardMetadataKeys.Album] = formatInfo.Tags.Album;
          }
          if (!string.IsNullOrEmpty(formatInfo.Tags.Genre))
          {
            _metadata[StandardMetadataKeys.Genre] = formatInfo.Tags.Genre;
          }
          if (formatInfo.Tags.Year.HasValue)
          {
            _metadata[StandardMetadataKeys.Year] = formatInfo.Tags.Year.Value;
          }
          if (formatInfo.Tags.TrackNumber.HasValue)
          {
            _metadata[StandardMetadataKeys.TrackNumber] = formatInfo.Tags.TrackNumber.Value;
          }
        }

        Logger.LogDebug(
          "Loaded metadata for {File}: Title={Title}, Artist={Artist}, Duration={Duration}",
          Path.GetFileName(filePath),
          _metadata.GetValueOrDefault(StandardMetadataKeys.Title),
          _metadata.GetValueOrDefault(StandardMetadataKeys.Artist),
          _duration);

        // Check if metadata is incomplete (using defaults), or if Shazam toggle is on
        bool hasIncompleteMetadata =
          _metadata[StandardMetadataKeys.Artist].Equals(StandardMetadataKeys.DefaultArtist) ||
          _metadata[StandardMetadataKeys.Album].Equals(StandardMetadataKeys.DefaultAlbum);
        bool needsFingerprinting = hasIncompleteMetadata || FpOptions.UseShazamForAllSources;

        if (needsFingerprinting)
        {
          Logger.LogDebug("File {File} needs fingerprinting (incomplete={Incomplete}, shazamAll={ShazamAll})",
            filePath, hasIncompleteMetadata, FpOptions.UseShazamForAllSources);
          _metadata["NeedsFingerprintingLookup"] = true;
        }
      }
      else
      {
        // Fallback to basic file info only
        _duration = TimeSpan.Zero;
        _metadata[StandardMetadataKeys.Duration] = _duration;
        _metadata["NeedsFingerprintingLookup"] = true;
        Logger.LogDebug("Could not read metadata from {File}, using file name as title", filePath);
      }
    }
    catch (Exception ex)
    {
      Logger.LogWarning(ex, "Failed to read metadata from {File}, using default values", filePath);
      _duration = TimeSpan.Zero;
      _metadata[StandardMetadataKeys.Duration] = _duration;
      _metadata["NeedsFingerprintingLookup"] = true;
    }

    // Request immediate identification if this track needs fingerprinting
    if (_metadata.TryGetValue("NeedsFingerprintingLookup", out var needsLookup)
        && needsLookup is true)
    {
      _identificationService?.RequestImmediateIdentification();
    }
  }

  /// <summary>
  /// Extracts embedded album art from audio file metadata using TagLib.
  /// Saves to the album art cache and sets AlbumArtUrl on the current-track metadata if found.
  /// </summary>
  private void ExtractEmbeddedAlbumArt(string filePath)
  {
    var cachedUrl = TryGetEmbeddedAlbumArtUrl(filePath);
    if (cachedUrl != null)
    {
      _metadata[StandardMetadataKeys.AlbumArtUrl] = cachedUrl;
    }
  }

  /// <summary>
  /// Reads embedded album art from <paramref name="filePath"/> via TagLib, writes it to the
  /// content-addressed album-art cache, and returns the resulting proxy URL
  /// (e.g. <c>/api/albumart/&lt;hash&gt;.jpg</c>). Returns <c>null</c> when no cache service
  /// is configured, no embedded picture exists, or any error occurs.
  /// Safe to call from both the current-track metadata path and the queue-item enqueue path:
  /// the cache is content-addressed so repeated calls for the same file are idempotent.
  /// </summary>
  private string? TryGetEmbeddedAlbumArtUrl(string filePath)
  {
    if (_albumArtCache == null)
    {
      return null;
    }

    try
    {
      using var tagFile = TagLib.File.Create(filePath);
      var pictures = tagFile.Tag.Pictures;
      if (pictures == null || pictures.Length == 0)
      {
        return null;
      }

      // Prefer FrontCover, fall back to first picture
      var picture = pictures.FirstOrDefault(p => p.Type == TagLib.PictureType.FrontCover)
                    ?? pictures[0];

      var data = picture.Data?.Data;
      if (data == null || data.Length == 0)
      {
        return null;
      }

      var mime = !string.IsNullOrEmpty(picture.MimeType) ? picture.MimeType : "image/jpeg";
      var cachedUrl = _albumArtCache.Save(data, mime);

      Logger.LogDebug("Extracted embedded album art from {File}: {Url} ({Bytes} bytes)",
        Path.GetFileName(filePath), cachedUrl, data.Length);

      return cachedUrl;
    }
    catch (Exception ex)
    {
      Logger.LogDebug(ex, "Failed to extract embedded album art from {File}", filePath);
      return null;
    }
  }

  /// <summary>
  /// Handles the TrackIdentified event from the fingerprinting service.
  /// Updates metadata with identified track information when file tags are incomplete.
  /// </summary>
  private void OnTrackIdentified(object? sender, TrackIdentifiedEventArgs e)
  {
    // Only update metadata if this is the active source
    if (State != AudioSourceState.Playing && State != AudioSourceState.Paused)
    {
      return;
    }

    var track = e.Track;

    // Check if current file needs fingerprinting metadata lookup
    bool needsLookup = _metadata.ContainsKey("NeedsFingerprintingLookup")
      && _metadata["NeedsFingerprintingLookup"] is bool b && b;

    // When UseShazamForAllSources is enabled, SongRec metadata replaces ID3 metadata
    // (SongRec is more authoritative and has better cover art from Apple Music CDN)
    if (FpOptions.UseShazamForAllSources && needsLookup)
    {
      if (!string.IsNullOrEmpty(track.Title))
      {
        _metadata[StandardMetadataKeys.Title] = track.Title;
      }
      if (!string.IsNullOrEmpty(track.Artist))
      {
        _metadata[StandardMetadataKeys.Artist] = track.Artist;
      }
      if (!string.IsNullOrEmpty(track.Album))
      {
        _metadata[StandardMetadataKeys.Album] = track.Album;
      }
      if (!string.IsNullOrEmpty(track.CoverArtUrl))
      {
        _metadata[StandardMetadataKeys.AlbumArtUrl] = track.CoverArtUrl;
      }

      _metadata["NeedsFingerprintingLookup"] = false;
      _metadata["IdentificationConfidence"] = e.Confidence;
      _metadata["IdentifiedAt"] = e.IdentifiedAt;
      _metadata["MetadataSource"] = "Shazam";

      Logger.LogInformation(
        "Shazam metadata replaced ID3 for file: '{Title}' by '{Artist}'",
        track.Title, track.Artist);
      return;
    }

    // Update album art from fingerprinting if still using default (no embedded art found).
    if (_metadata.ContainsKey(StandardMetadataKeys.AlbumArtUrl) &&
        _metadata[StandardMetadataKeys.AlbumArtUrl].Equals(StandardMetadataKeys.DefaultAlbumArtUrl) &&
        !string.IsNullOrEmpty(track.CoverArtUrl))
    {
      _metadata[StandardMetadataKeys.AlbumArtUrl] = track.CoverArtUrl;
      Logger.LogInformation("Album art URL set for '{Title}': {Url}", track.Title, track.CoverArtUrl);
    }

    if (!needsLookup)
    {
      return;
    }

    Logger.LogInformation(
      "Updating FilePlayer metadata from fingerprinting: {Title} by {Artist} (confidence: {Confidence:P0})",
      track.Title, track.Artist, e.Confidence);

    // Only update fields that are using defaults (incomplete)
    if (_metadata[StandardMetadataKeys.Artist].Equals(StandardMetadataKeys.DefaultArtist))
    {
      _metadata[StandardMetadataKeys.Artist] = track.Artist;
    }

    if (_metadata[StandardMetadataKeys.Album].Equals(StandardMetadataKeys.DefaultAlbum) &&
        !string.IsNullOrEmpty(track.Album))
    {
      _metadata[StandardMetadataKeys.Album] = track.Album;
    }

    // Update title if it's just the filename (contains extension or equals filename without extension)
    var currentTitle = _metadata[StandardMetadataKeys.Title]?.ToString() ?? "";
    var filename = Path.GetFileNameWithoutExtension(_currentFile ?? "");
    if (currentTitle.Equals(filename, StringComparison.OrdinalIgnoreCase))
    {
      _metadata[StandardMetadataKeys.Title] = track.Title;
    }

    // Add optional metadata if not already present
    if (!_metadata.ContainsKey(StandardMetadataKeys.Genre) && track.Genre != null)
    {
      _metadata[StandardMetadataKeys.Genre] = track.Genre;
    }

    if (!_metadata.ContainsKey(StandardMetadataKeys.Year) && track.ReleaseYear.HasValue)
    {
      _metadata[StandardMetadataKeys.Year] = track.ReleaseYear.Value;
    }

    if (!_metadata.ContainsKey(StandardMetadataKeys.TrackNumber) && track.TrackNumber.HasValue)
    {
      _metadata[StandardMetadataKeys.TrackNumber] = track.TrackNumber.Value;
    }

    // Mark that fingerprinting has been applied
    _metadata["NeedsFingerprintingLookup"] = false;
    _metadata["IdentificationConfidence"] = e.Confidence;
    _metadata["IdentifiedAt"] = e.IdentifiedAt;
    _metadata["MetadataSource"] = "Fingerprinting";

    Logger.LogInformation(
      "File metadata enhanced via fingerprinting: {Title} by {Artist}",
      _metadata[StandardMetadataKeys.Title], _metadata[StandardMetadataKeys.Artist]);
  }
}

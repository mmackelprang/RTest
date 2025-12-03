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
public class FilePlayerAudioSource : PrimaryAudioSourceBase, IPlayQueue
{
  private readonly IOptionsMonitor<FilePlayerOptions> _options;
  private readonly IOptionsMonitor<FilePlayerPreferences> _preferences;
  private readonly string _rootDir;
  private readonly Dictionary<string, string> _metadata = new();
  private Queue<string> _playlist = new();
  private List<string> _originalOrder = new(); // Store original order for shuffle toggle
  private List<string> _playedHistory = new(); // Track played songs for Previous
  private string? _currentFile;
  private ISoundDataProvider? _dataProvider;
  private FileStream? _fileStream;
  private MiniAudioEngine? _audioEngine;
  private TimeSpan _duration;
  private TimeSpan _position;
  private int _currentIndex = -1; // Track current position in queue

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
    _currentIndex = -1; // Will be set when LoadCurrentFileAsync is called
    await LoadCurrentFileAsync(cancellationToken);

    Logger.LogInformation("Loaded playlist with {Count} files", validFiles.Count);
  }

  /// <inheritdoc/>
  public override async Task NextAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    // Handle RepeatMode.One - replay current track
    if (_preferences.CurrentValue.Repeat == RepeatMode.One && _currentFile != null)
    {
      Logger.LogDebug("Repeat One enabled - replaying current track");
      _position = TimeSpan.Zero;
      if (State == AudioSourceState.Playing)
      {
        await PlayCoreAsync(cancellationToken);
      }
      return;
    }

    // Add current file to history before moving to next
    if (_currentFile != null && !_playedHistory.Contains(_currentFile))
    {
      _playedHistory.Add(_currentFile);
    }

    // Check if playlist has more tracks
    if (_playlist.Count > 0)
    {
      await LoadCurrentFileAsync(cancellationToken);

      if (State == AudioSourceState.Playing)
      {
        await PlayCoreAsync(cancellationToken);
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

      _playlist = new Queue<string>(files);
      _playedHistory.Clear();
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
    if (_playedHistory.Count > 0)
    {
      var previousFile = _playedHistory[^1];
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
      _position = TimeSpan.Zero;
      CleanupDataProvider();

      // Restore file metadata and data provider
      try
      {
        _audioEngine ??= new MiniAudioEngine();
        _fileStream = File.OpenRead(_currentFile);
        _dataProvider = new ChunkedDataProvider(_audioEngine, _fileStream);
        Logger.LogDebug("Loaded previous file with SoundFlow: {File}", _currentFile);
      }
      catch (Exception ex)
      {
        Logger.LogWarning(ex, "SoundFlow could not decode previous file: {File}", _currentFile);
        _fileStream?.Dispose();
        _fileStream = null;
        _dataProvider = null;
      }

      UpdateMetadataFromFile(_currentFile);

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
      _currentFile = lastFile;
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

    // Rebuild playlist with current state
    var remainingTracks = _playlist.ToList();
    
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
    var allTracks = new List<string>();
    if (_currentFile != null)
    {
      allTracks.Add(_currentFile);
    }
    allTracks.AddRange(_playlist);
    return allTracks;
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
        
        if (formatInfo.Duration != TimeSpan.Zero)
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

    return new QueueItem
    {
      Id = filePath,
      Title = title,
      Artist = artist,
      Album = album,
      Duration = duration,
      Index = index,
      IsCurrent = isCurrent
    };
  }

  /// <summary>
  /// Raises the QueueChanged event.
  /// </summary>
  private void OnQueueChanged(QueueChangedEventArgs args)
  {
    QueueChanged?.Invoke(this, args);
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
      _currentIndex = -1;
      OnPlaybackCompleted(PlaybackCompletionReason.EndOfContent);
      return;
    }

    _currentFile = _playlist.Dequeue();
    _position = TimeSpan.Zero;

    // Update current index - it should be 0 since we're loading the front of the queue
    // But we need to account for the overall position in the original order
    var allTracks = new List<string> { _currentFile };
    allTracks.AddRange(_playlist);
    _currentIndex = 0;

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

    // Raise queue changed event for current item change
    OnQueueChanged(new QueueChangedEventArgs
    {
      ChangeType = QueueChangeType.CurrentChanged,
      AffectedIndex = _currentIndex,
      AffectedItem = CreateQueueItem(_currentFile, _currentIndex, true)
    });

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

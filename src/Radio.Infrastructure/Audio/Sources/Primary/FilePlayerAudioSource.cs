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

  // AUD-33: when (UTC ticks, 0 = unknown) the current file became the track whose metadata we hold,
  // and which file that was. Written on the playback path, read on the identification thread.
  private long _trackStartedAtTicks;
  private string? _trackStartedFile;
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

  // AUD-98: queue-state writes go through this gate one at a time; a snapshot whose generation is no
  // longer the newest is skipped (see SaveQueueStateAsync). _lastQueueSave is the most recent write.
  private readonly SemaphoreSlim _queueSaveGate = new(1, 1);
  private long _queueSaveGeneration;
  private Task _lastQueueSave = Task.CompletedTask;
  // Every key in the store matching each queue key case-insensitively, read on the first write and only
  // ever touched inside _queueSaveGate.
  private Dictionary<string, string[]>? _queueKeyVariants;

  // AUD-96: queue rows come from here, never from the file on the request path. See the class remarks.
  private readonly QueueItemMetadataCache _queueMetadata;

  /// <summary>
  /// Reads one file's queue row (tags + embedded art). A test seam: production uses
  /// <see cref="ReadQueueItemMetadata"/>. Called only on the metadata cache's background reader.
  /// </summary>
  internal Func<string, QueueItemMetadata> QueueMetadataReader { get; set; }

  /// <summary>
  /// A file's size and last-write time, or <c>null</c> when it is missing. A test seam: production uses
  /// <see cref="StatQueueFile"/>. Called only on the metadata cache's background reader.
  /// </summary>
  internal Func<string, QueueFileStamp?> QueueFileStat { get; set; } = StatQueueFile;

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
  /// <param name="fingerprintingOptions">Optional fingerprinting options. Controls the
  /// UseShazamForAllSources gate only — what is done with a fingerprint ANSWER is decided per
  /// field by SourceMetadataPrecedence and is not configurable (AUD-1).</param>
  /// <param name="getActiveSource">Optional accessor for the audio manager's active source (see <see cref="PrimaryAudioSourceBase.IsActiveSource"/>).</param>
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
    IOptionsMonitor<FingerprintingOptions>? fingerprintingOptions = null,
    Func<IAudioSource?>? getActiveSource = null)
    : base(logger, metricsCollector, getActiveSource)
  {
    _options = options;
    _preferences = preferences;
    _rootDir = rootDir;
    _identificationService = identificationService;
    _playbackService = playbackService;
    _configurationManager = configurationManager;
    _albumArtCache = albumArtCache;
    _fingerprintingOptionsMonitor = fingerprintingOptions;

    // AUD-96. The lambdas read the properties at call time, so a test can swap either seam after
    // construction.
    QueueMetadataReader = ReadQueueItemMetadata;
    _queueMetadata = new QueueItemMetadataCache(
      path => QueueMetadataReader(path),
      path => QueueFileStat(path),
      Logger);

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
  /// <remarks>
  /// AUD-96: a 64-bit FNV-1a hash of exactly what <see cref="GetFullPlaylistAsync"/> derives an item's
  /// identity and state from — the played, current and upcoming paths in order, and which of them are
  /// in error — mixed with the metadata cache's <see cref="QueueItemMetadataCache.Version"/>, which
  /// advances when rows' title, artist, album, duration or art change. Hashing the lists rather than
  /// counting mutations is deliberate: this class changes its queue from more than a dozen places, and a
  /// counter would be correct only while every one of them remembered to bump it. Seeded per instance
  /// with a random 64-bit value, so a re-created File Player is practically certain not to repeat an old
  /// instance's value. Touches no file.
  /// <para>
  /// ⚠ Several of those places mutate the lists without <c>_playlistLock</c> (a pre-existing pattern this
  /// row did not change). The lists are therefore copied with <c>ToArray</c>, which does not throw on a
  /// concurrent change the way enumeration does — but can copy a slot a concurrent <c>Clear</c> or
  /// <c>Dequeue</c> has just nulled, so null entries are hashed as a marker rather than dereferenced. If
  /// anything still throws, a fresh fallback value is returned (one practically certain to differ from
  /// every earlier read), so the caller re-reads the playlist rather than skipping it.
  /// </para>
  /// </remarks>
  public long QueueVersion
  {
    get
    {
      try
      {
        string?[] played;
        string? current;
        string?[] upcoming;
        string?[] errors;
        lock (_playlistLock)
        {
          played = _playedHistory.ToArray();
          current = _currentFile;
          upcoming = _playlist.ToArray();
          errors = _errorFiles.ToArray();
        }

        HashSet<string> errorSet = new(errors.OfType<string>(), StringComparer.Ordinal);
        ulong hash = FnvOffset ^ _versionSeed;
        foreach (string? path in played)
        {
          hash = MixEntry(hash, 'P', path, errorSet);
        }
        if (current != null)
        {
          hash = MixEntry(hash, 'C', current, errorSet);
        }
        foreach (string? path in upcoming)
        {
          hash = MixEntry(hash, 'U', path, errorSet);
        }

        hash = Mix(hash, (ulong)_queueMetadata.Version);
        return unchecked((long)hash);
      }
      catch (Exception)
      {
        // A torn read of lists being changed under it. Report a value no earlier read is practically
        // likely to have returned, so the poller fetches the playlist on its next pass instead of skipping.
        return unchecked((long)(_versionSeed ^ FnvPrime * (ulong)Interlocked.Increment(ref _versionFallback)));
      }
    }
  }

  // Per-instance seed for QueueVersion, and the counter its fallback draws from.
  private readonly ulong _versionSeed = (ulong)Random.Shared.NextInt64();
  private long _versionFallback;

  private const ulong FnvOffset = 14695981039346656037UL;
  private const ulong FnvPrime = 1099511628211UL;

  private static ulong Mix(ulong hash, ulong value)
  {
    for (int i = 0; i < 8; i++)
    {
      hash ^= (value >> (i * 8)) & 0xFF;
      hash *= FnvPrime;
    }
    return hash;
  }

  // One entry: its segment (played / current / upcoming), its error flag, its path, and a terminator so
  // that adjacent paths cannot run together into the same byte stream.
  private static ulong MixEntry(ulong hash, char segment, string? path, HashSet<string> errors)
  {
    hash = Mix(hash, segment);
    if (path == null)
    {
      // A slot nulled by a concurrent Clear/Dequeue (see QueueVersion). The next read will not see it.
      return Mix(hash, 0xDEAD_BEEFUL);
    }
    hash = Mix(hash, errors.Contains(path) ? 1UL : 0UL);
    foreach (char c in path)
    {
      hash = Mix(hash, c);
    }
    return Mix(hash, 0xFFFF_FFFF_FFFF_FFFFUL);
  }

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
    PrimeQueueMetadata(_originalOrder);
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
    PrimeQueueMetadata(audioFiles);
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
    PrimeQueueMetadata(validFiles);
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
    bool addedToHistory = false;
    lock (_playlistLock)
    {
      if (_currentFile != null && !_playedHistory.Contains(_currentFile))
      {
        _playedHistory.Add(_currentFile);
        addedToHistory = true;
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

    // No repeat or reached end - stop playback.
    //
    // AUD-98: the last track stays the current one (StopCoreAsync leaves _currentFile set), so take back
    // the history entry added above — otherwise the full playlist lists it twice, once played and once
    // current, a restart persists both, and Previous pops it onto the front of the queue as a copy of
    // itself. Only an entry THIS call added is removed.
    if (addedToHistory)
    {
      lock (_playlistLock)
      {
        if (_playedHistory.Count > 0 && _playedHistory[^1] == _currentFile)
        {
          _playedHistory.RemoveAt(_playedHistory.Count - 1);
        }
      }
    }

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
      // AUD-98: the history just changed, and this path raises no QueueChanged — so persist it here, or a
      // restart before the next queue change would bring back the list as it was before this Previous.
      SaveQueueStateToPreferences();

      _position = TimeSpan.Zero;
      CleanupDataProvider();

      // Restore file metadata and data provider
      try
      {
        _audioEngine ??= SerializedMiniAudioEngine.Create();
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

      // AUD-98: go to the last track of the list as it stands, and mark every track before it played, so
      // the list keeps every track and its order. This used to set the current track to the last entry of
      // _originalOrder and change nothing else, which dropped the track that had been current and left the
      // last one listed twice (as current, and still at the end of the upcoming queue). With Shuffle off
      // and no queue edits the last track of the list IS the last entry of _originalOrder; with Shuffle on
      // it is the last track in shuffled order. _originalOrder is only the fallback for an empty list.
      string lastFile;
      lock (_playlistLock)
      {
        var wholeList = new List<string>();
        if (_currentFile != null)
        {
          wholeList.Add(_currentFile);
        }
        wholeList.AddRange(_playlist);

        if (wholeList.Count > 0)
        {
          lastFile = wholeList[^1];
          _playedHistory = wholeList.Take(wholeList.Count - 1).ToList();
          _playlist = new Queue<string>();
        }
        else
        {
          lastFile = _originalOrder[^1];
        }

        _currentFile = lastFile;
      }
      SaveQueueStateToPreferences();
      _position = TimeSpan.Zero;
      CleanupDataProvider();

      try
      {
        _audioEngine ??= SerializedMiniAudioEngine.Create();
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

    // UI-28: write the store BEFORE the in-memory value. See PersistPlaybackModeAsync.
    await PersistPlaybackModeAsync(nameof(FilePlayerPreferences.Shuffle), enabled.ToString());
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
        // Disable shuffle - put the tracks not yet played back in their original order.
        //
        // AUD-98: this used to take _originalOrder from the current track onward, which ignored the
        // played history — tracks already played in shuffled order came back as upcoming, and tracks
        // before the current one in the original order that had NOT been played left the list. Now
        // the set reordered is exactly the current + upcoming tracks (counted, so a track queued twice
        // stays twice), ordered by _originalOrder; any of them _originalOrder lacks keep their
        // relative order after it. The current track stays first.
        if (_originalOrder.Count > 0)
        {
          var notYetPlaced = new Dictionary<string, int>(StringComparer.Ordinal);
          foreach (string track in remainingTracks)
          {
            notYetPlaced[track] = notYetPlaced.GetValueOrDefault(track) + 1;
          }

          var ordered = new List<string>(remainingTracks.Count);
          foreach (string track in _originalOrder.Concat(remainingTracks))
          {
            if (notYetPlaced.TryGetValue(track, out int left) && left > 0)
            {
              ordered.Add(track);
              notYetPlaced[track] = left - 1;
            }
          }

          if (_currentFile != null)
          {
            ordered.Remove(_currentFile);
            ordered.Insert(0, _currentFile);
          }

          remainingTracks = ordered;
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
  public override async Task SetRepeatModeAsync(RepeatMode mode, CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    if (_preferences.CurrentValue.Repeat == mode)
    {
      Logger.LogDebug("Repeat mode already set to {Mode}", mode);
      return;
    }

    // UI-28: write the store BEFORE the in-memory value. See PersistPlaybackModeAsync.
    await PersistPlaybackModeAsync(nameof(FilePlayerPreferences.Repeat), mode.ToString());
    _preferences.CurrentValue.Repeat = mode;
    Logger.LogInformation("Repeat mode set to {Mode}", mode);
  }

  /// <summary>
  /// UI-28. Writes one shuffle/repeat key straight to the configuration store.
  /// </summary>
  /// <remarks>
  /// <para>
  /// <see cref="IsShuffleEnabled"/> and <see cref="RepeatMode"/> read <c>_preferences.CurrentValue</c>, and
  /// that object is a cache, not storage: every <c>IConfigurationManager.SetValueAsync</c> anywhere in the
  /// process reloads the SQLite configuration provider, and the options monitor then rebuilds
  /// <c>CurrentValue</c> from configuration. Before this write existed, Shuffle and Repeat lived only in
  /// the cached object until <c>PreferencesPersistenceService</c>'s next 30-second save, so any reload in
  /// that window put them back to their stored values. Measured on the box 2026-10-01: the Web queue panel's
  /// <c>queue.state</c> save (one per <c>QueueChanged</c>, and toggling shuffle reorders the queue) reloaded
  /// within 1–10 s of each toggle; the next position broadcast then showed the buttons off, and Repeat had
  /// genuinely stopped applying.
  /// </para>
  /// <para>
  /// <b>Every case variant of the key is written, not only <c>FilePlayerPreferences:{property}</c>.</b> The
  /// System Config page saves this section through <c>ConfigurationController</c>, which lowercases the
  /// section (<c>fileplayerpreferences:shuffle</c>). SQLite keys are case-sensitive, so both rows exist; the
  /// configuration provider's keys are not, so whichever row it reads last wins. On the box (2026-10-02) the
  /// lowercase rows from 2026-02-12 sort after the PascalCase ones and hold <c>false</c> / <c>Off</c>, so
  /// writing the PascalCase key alone would have changed nothing a reload reads. Writing every variant makes
  /// the read order irrelevant.
  /// </para>
  /// <para>
  /// Written with <c>IConfigurationStore.SetEntriesAsync</c>, which does not itself reload, so this write
  /// does not wipe the other values that still live only in <c>CurrentValue</c> (the song position, for
  /// one). The store is written first so that a reload which starts after the write reads the new value.
  /// Two windows remain, each milliseconds wide: a reload already in flight when the write lands can still
  /// read the old value (the time between the provider's SQLite read and its change token firing), and
  /// <c>PreferencesPersistenceService</c>, which serialises <c>CurrentValue</c> before awaiting its own
  /// write, can land that older snapshot after this write if a toggle falls between the two.
  /// </para>
  /// <para>
  /// A store failure is logged and swallowed: the in-memory value is still set, so the toggle works until
  /// the next reload, which is how it behaved before this method existed.
  /// </para>
  /// </remarks>
  private async Task PersistPlaybackModeAsync(string property, string value)
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

      string key = $"{FilePlayerPreferences.SectionName}:{property}";
      IReadOnlyList<ConfigurationEntry> existing = await store.GetAllEntriesAsync(ConfigurationReadMode.Raw);
      List<ConfigurationEntry> writes = existing
        .Select(e => e.Key)
        .Where(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
        .Append(key)
        .Distinct(StringComparer.Ordinal)
        .Select(k => new ConfigurationEntry { Key = k, Value = value })
        .ToList();

      await store.SetEntriesAsync(writes);
      await store.SaveAsync();
    }
    catch (Exception ex)
    {
      Logger.LogWarning(ex, "Failed to persist {Property} to the configuration store", property);
    }
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

      // AUD-98: played tracks, the current track, upcoming tracks and the unshuffled order all come back.
      // Before this, only the current and upcoming tracks were saved, so every restart dropped the played
      // tracks for good and Repeat All went on to repeat only what was left.
      RestoredQueue? restored = BuildRestoredQueue(
        prefs.QueueItems, prefs.CurrentQueueIndex, prefs.OriginalOrder, File.Exists);

      if (restored != null)
      {
        lock (_playlistLock)
        {
          _playedHistory = restored.Played;
          _currentFile = restored.Current;
          _playlist = new Queue<string>(restored.Upcoming);
          _originalOrder = restored.OriginalOrder;
        }

        // AUD-96: warm the queue rows in the background, so the first queue read after a restart does
        // not pay for every file on the share. AUD-98: every restored row the panel shows — played,
        // current and upcoming. _originalOrder needs no priming: it only feeds rows after a Repeat All
        // wrap, by which time every one of its tracks is already in this list or was read on demand.
        PrimeQueueMetadata(
          restored.Played.Append(restored.Current).Concat(restored.Upcoming).ToList());

        // The operational index within current + upcoming, where the current track always sits first.
        _currentIndex = 0;

        _position = TimeSpan.FromMilliseconds(prefs.SongPositionMs);

        // ⛔ AUD-24 — OWNER RULING, 2026-09-10. `_pendingSeekMs = prefs.SongPositionMs;` used to
        // sit here, and PlayCoreAsync still consumes it. It is deliberately not assigned.
        //
        // Repairing SeekCoreAsync would otherwise have made resume-where-you-left-off start
        // working on every restart — a startup behaviour change nobody asked for, riding along on
        // a bug fix. The owner declined it for this PR; re-enabling it is exactly this one line.
        //
        // ⭐ Not assigning it also makes the two restore arms AGREE. The fallback arm below
        // ("restore just the last played file") sets _position and never set _pendingSeekMs, so
        // un-guarding this arm alone would have left a restored QUEUE resuming audibly while a
        // restored LAST-PLAYED FILE did not. Neither resumes, which is what this appliance has
        // always done.
        UpdateMetadataFromFile(restored.Current);

        // ⚠ This line used to end "(seek to {Ms}ms)". No seek ever happened — SeekCoreAsync did
        // not call the engine — so the message had claimed one on every startup for the life of
        // the file, which is the CLAUDE.md § Pre-Merge Review failure class. With the resume
        // deliberately guarded above, it would now be claiming one that definitely cannot happen.
        Logger.LogInformation(
          "Restored queue: {Played} played, {Upcoming} upcoming, current {File} — reported position set to {Ms}ms; playback will start from the beginning of the track",
          restored.Played.Count, restored.Upcoming.Count,
          Path.GetFileName(restored.Current), prefs.SongPositionMs);
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

    // The source's own Id, which is stable for the lifetime of the source instance. A per-session
    // GUID was wrong twice over: it missed AudioManager's gain/ducking lookups (AUD-2), and because
    // StopAsync deliberately KEEPS _gainOffsets (SoundFlowPlaybackService.StopAsync), every track
    // change also left one entry there that nothing would ever read or remove.
    _playbackId = Id;
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

      // Restore persisted playback position if this is the first play after queue restoration.
      //
      // ⛔ AUD-24 — DORMANT BY THE OWNER'S RULING, 2026-09-10, and deliberately kept rather than
      // deleted. InitializeAsync no longer assigns _pendingSeekMs (see the ruling recorded there),
      // and nothing else writes it, so this branch cannot currently be entered. It is the whole
      // consumer half of resume-where-you-left-off: restoring that feature is one line there, and
      // this block is what it would feed. Deleting it would make that a rewrite instead.
      //
      // ⚠ Before this row, the branch DID run and was a second silent no-op — SeekCoreAsync moved
      // a field and called nothing, under a log line reading "Restored playback position to {Ms}ms".
      // SeekCoreAsync is fixed, so if the assignment is ever restored this now works.
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
  /// <remarks>
  /// <b>AUD-24.</b> This method used to assign <c>_position</c> and return, which moved the readout
  /// and the API's reported position while the audio carried on from where it was — the defect
  /// <c>docs/known-issues-and-future-work.md</c> § 14a recorded on 2026-09-02 and the owner observed at the cabinet
  /// on 2026-09-09. Two rules hold it closed:
  /// <list type="number">
  ///   <item>the engine is asked to reposition, through the same registration this class already
  ///     uses for <c>Pause</c> / <c>Resume</c> / <c>SetVolume</c> / <c>StopAsync</c>; and</item>
  ///   <item>when a playback service is wired, <c>_position</c> advances only if that service
  ///     reports the player moved — which requires a registered player that accepted the seek.
  ///     <see cref="Position"/> reads that field, so writing it on a refusal would re-create the
  ///     defect in a smaller shape.</item>
  /// </list>
  /// Leaving the anchor where it was makes the scrubber snap back on the panel's next state read,
  /// which <c>docs/decisions/DECISION-LOG.md</c> (ADR-029 amendments, Decision 2) names as the correct
  /// user-visible answer to a refused seek. ⚠ That entry reaches the same conclusion by a mechanism
  /// this class does NOT have — it says <c>Position</c> "reads through to the player", which is true
  /// of <c>AudioFileEventSource</c> and not of this one, whose <c>Position</c> is still the
  /// wall-clock accumulator <c>MonitorPlaybackAsync</c> advances. The principle is borrowed; the
  /// read-through is not (see the plan's § 6).
  ///
  /// ⚠ <b>The no-playback-service arm still moves the field unconditionally</b>, so rule 2 above is
  /// hedged on the SERVICE being wired rather than stated as "only when the engine says it moved".
  /// A source with no playback service cannot produce audio by any route — <c>GetSoundComponent</c>
  /// returns the <c>ChunkedDataProvider</c>, whose only consumer casts it to
  /// <c>BufferedSoundGenerator&lt;float&gt;</c> and gets null — so there is nothing for the field to
  /// contradict; every path that can actually play has a service.
  /// </remarks>
  protected override Task SeekCoreAsync(TimeSpan position, CancellationToken cancellationToken)
  {
    // Seeking is only valid for non-negative positions within the duration.
    //
    // ⚠ When duration is zero or unknown, ONLY the negative check applies — any non-negative
    // position is accepted, because there is no upper bound to compare against. An earlier revision
    // of this comment said seeking was "limited to position zero" in that case, which the second
    // clause's short-circuit makes false; two tests in FilePlayerAudioSourceTests seek to 30s
    // against a stub file whose duration IS zero, and both pass.
    //
    // ⚠ This guard must stay ABOVE the engine call: an out-of-range seek is a caller error, not a
    // refusal, and the two are reported differently.
    if (position < TimeSpan.Zero || (_duration > TimeSpan.Zero && position > _duration))
    {
      throw new ArgumentOutOfRangeException(nameof(position), "Seek position out of range");
    }

    // The degraded configuration described in the remarks above: no service, no audio, so the
    // field is the position and nothing can contradict it.
    if (_playbackService is null)
    {
      _position = position;
      Logger.LogDebug("Seeked to {Position} (no playback service — reported position only)", position);
      return Task.CompletedTask;
    }

    // Id, not _playbackId. Every assignment to _playbackId is either Id or null — pinned by
    // PlaybackKeyLintTests rule 1 — so the key is never anything but Id. But the field is CLEARED
    // by StopCoreAsync and DisposeAsyncCore, and is set optimistically BEFORE PlayFileAsync, so it
    // can be non-null with no player registered and null while Id is still a perfectly good key. It
    // answers "a session was started and not yet stopped", which is why it gates the log level
    // below and NOT this lookup. Only _activePlayers knows whether a player is live, so ask it.
    var moved = _playbackService.Seek(Id, position);

    if (!moved)
    {
      // WARNING only when playback was believed live — a scrub against a stopped player is an
      // ordinary outcome, and journal volume on the appliance correlates with audible distortion
      // (CLAUDE.md § Services).
      if (_playbackId is not null)
      {
        Logger.LogWarning(
          "🎵 FILE PLAYER: seek to {Position} was refused by the player for \"{FileName}\"; the reported position stays at {Reported}",
          position, Path.GetFileName(_currentFile ?? "(none)"), _position);
      }
      else
      {
        Logger.LogDebug(
          "🎵 FILE PLAYER: seek to {Position} ignored — no live player registered", position);
      }

      return Task.CompletedTask;
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
    _queueMetadata.Dispose();

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
      _audioEngine ??= SerializedMiniAudioEngine.Create();
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
    PrimeQueueMetadata([fullPath]);

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
          _audioEngine ??= SerializedMiniAudioEngine.Create();
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
      _audioEngine ??= SerializedMiniAudioEngine.Create();
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
  /// Creates a QueueItem from a file path. AUD-96: the row's metadata comes from the cache and never from
  /// the file — a path not read yet shows its placeholder (file name, "--", no art) until the background
  /// reader fills it, after which <see cref="QueueVersion"/> moves and the queue is broadcast again.
  /// </summary>
  private QueueItem CreateQueueItem(string filePath, int index, bool isCurrent)
  {
    var metadata = _queueMetadata.GetOrSchedule(filePath);
    return new QueueItem
    {
      Id = filePath,
      Title = metadata.Title,
      Artist = metadata.Artist,
      Album = metadata.Album,
      Duration = metadata.Duration,
      AlbumArtUrl = metadata.AlbumArtUrl,
      Index = index,
      IsCurrent = isCurrent,
      State = isCurrent ? QueueItemState.Current : QueueItemState.Upcoming,
      FullPlaylistIndex = index
    };
  }

  /// <summary>
  /// Creates a QueueItem with an explicit state and full playlist index. Metadata from the cache, as
  /// <see cref="CreateQueueItem"/>.
  /// </summary>
  private QueueItem CreateQueueItemWithState(string filePath, int fullPlaylistIndex, QueueItemState state)
  {
    var metadata = _queueMetadata.GetOrSchedule(filePath);
    return new QueueItem
    {
      Id = filePath,
      Title = metadata.Title,
      Artist = metadata.Artist,
      Album = metadata.Album,
      Duration = metadata.Duration,
      AlbumArtUrl = metadata.AlbumArtUrl,
      Index = fullPlaylistIndex, // For operational compatibility
      IsCurrent = state == QueueItemState.Current,
      State = state,
      FullPlaylistIndex = fullPlaylistIndex
    };
  }

  /// <summary>
  /// AUD-96: queues <paramref name="paths"/> for the metadata reader (read if new, re-read if the file
  /// changed) and, once the cache has grown past its bound, drops rows for paths no longer queued.
  /// Returns at once; touches no file.
  /// </summary>
  private void PrimeQueueMetadata(IReadOnlyCollection<string> paths)
  {
    if (_queueMetadata.NeedsTrim)
    {
      _queueMetadata.TrimTo(GetFullPlaylistInOrder().Concat(paths).ToList());
    }
    _queueMetadata.Revalidate(paths);
  }

  /// <summary>Completes when the queue metadata reader has caught up. A test rendezvous.</summary>
  internal Task WhenQueueMetadataIdleAsync() => _queueMetadata.WhenIdleAsync();

  /// <inheritdoc/>
  public async Task<bool> WaitForQueueMetadataAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    try
    {
      await _queueMetadata.WhenIdleAsync().WaitAsync(timeout, cancellationToken);
    }
    catch (TimeoutException)
    {
      return false;
    }

    // Idle is not the same as read: a row whose read failed is idle too, and still shows its placeholder
    // (or its last good value).
    return _queueMetadata.AllVerified(GetFullPlaylistInOrder());
  }

  /// <summary>
  /// Reads one queue row from the file: the read every queue request used to do per item (AUD-96 moved
  /// it here, onto the cache's background reader). AUD-32: SoundFlow first, TagLib when SoundFlow rejects
  /// the tag; blank tags come back null and keep the defaults. Never throws.
  /// </summary>
  private QueueItemMetadata ReadQueueItemMetadata(string filePath)
  {
    var title = Path.GetFileNameWithoutExtension(filePath);
    var artist = "--";
    var album = "--";
    TimeSpan? duration = null;

    var tags = AudioTagReader.Read(filePath, Logger);
    if (tags != null)
    {
      duration = tags.Duration;
      title = tags.Title ?? title;
      artist = tags.Artist ?? artist;
      album = tags.Album ?? album;
    }

    // Embedded art so Up Next / Recent tiles render real art instead of the music_note placeholder.
    // The album-art cache is content-addressed, so the URL is stable for the same picture.
    var albumArtUrl = TryGetEmbeddedAlbumArtUrl(filePath);

    return new QueueItemMetadata(title, artist, album, duration, albumArtUrl);
  }

  /// <summary>
  /// The file's size and last-write time, or <c>null</c> when it is missing — or when the share could not be
  /// asked (<c>FileInfo.Exists</c> answers <c>false</c> on any I/O error, which is why the cache treats
  /// <c>null</c> as unverified rather than as a fact).
  /// </summary>
  private static QueueFileStamp? StatQueueFile(string filePath)
  {
    try
    {
      var info = new FileInfo(filePath);
      return info.Exists ? new QueueFileStamp(info.Length, info.LastWriteTimeUtc) : null;
    }
    catch (Exception)
    {
      return null;
    }
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
  /// Saves the whole playlist — played, current and upcoming, and the unshuffled order — so that a restart
  /// brings it back (AUD-98). The snapshot is taken on the caller's thread. With a configuration manager,
  /// the store write is queued to the thread pool and not awaited; without one, only
  /// <c>_preferences.CurrentValue</c> is updated, synchronously.
  /// </summary>
  private void SaveQueueStateToPreferences()
  {
    try
    {
      PersistedQueue snapshot = CapturePersistedQueue();

      if (_configurationManager != null)
      {
        // Task.Run, not a direct call: SemaphoreSlim.WaitAsync completes synchronously when the gate is
        // free and Microsoft.Data.Sqlite's async methods run synchronously, so a direct call would do the
        // whole store write on the caller — the playback monitor's auto-advance, or an HTTP request.
        _lastQueueSave = Task.Run(() => SaveQueueStateAsync(snapshot));
      }
      else
      {
        ApplyToPreferences(snapshot);
      }

      Logger.LogDebug("Saved queue state: {Count} items, current index: {Index}",
        snapshot.Items.Count, snapshot.CurrentIndex);
    }
    catch (Exception ex)
    {
      Logger.LogWarning(ex, "Failed to save queue state to preferences");
    }
  }

  /// <summary>
  /// AUD-98. The persisted form of the playlist, taken under <c>_playlistLock</c>: played + current +
  /// upcoming in play order, the index of the current track in that list (the number of played tracks;
  /// <c>-1</c> for an empty list), and a copy of <c>_originalOrder</c>. Each capture takes the next
  /// generation number, so the store write can tell an older snapshot from a newer one.
  /// </summary>
  private PersistedQueue CapturePersistedQueue()
  {
    lock (_playlistLock)
    {
      var items = new List<string>(_playedHistory);
      int currentIndex = items.Count;
      if (_currentFile != null)
      {
        items.Add(_currentFile);
      }
      items.AddRange(_playlist);

      // ToArray rather than enumerating: several paths assign or mutate _originalOrder without this lock
      // (pre-existing). A concurrent Add can still, rarely, make ToArray throw; SaveQueueStateToPreferences
      // catches it, that save is skipped, and the next queue change saves again.
      var originalOrder = _originalOrder.ToArray().ToList();

      return new PersistedQueue(
        items,
        items.Count == 0 ? -1 : currentIndex,
        originalOrder,
        ++_queueSaveGeneration);
    }
  }

  /// <summary>Copies a snapshot into <c>_preferences.CurrentValue</c>, as new lists.</summary>
  private void ApplyToPreferences(PersistedQueue snapshot)
  {
    FilePlayerPreferences prefs = _preferences.CurrentValue;
    prefs.QueueItems = new List<string>(snapshot.Items);
    prefs.CurrentQueueIndex = snapshot.CurrentIndex;
    prefs.OriginalOrder = new List<string>(snapshot.OriginalOrder);
  }

  /// <summary>
  /// Writes a snapshot to the configuration store, then to <c>_preferences.CurrentValue</c>.
  /// </summary>
  /// <remarks>
  /// <para>
  /// <b>Writes are serialised, and a snapshot older than the newest one is skipped</b> rather than written,
  /// so two quick queue changes cannot land in the store in the wrong order. The newest snapshot is always
  /// written: when it reaches the gate no newer one exists.
  /// </para>
  /// <para>
  /// <b>Every case variant of each key is written</b>, for the reason
  /// <see cref="PersistPlaybackModeAsync"/> gives. It matters more here: the bridge flattens a JSON array
  /// into <c>QueueItems:0</c>, <c>QueueItems:1</c>, … and its keys ignore case, so a stale variant row
  /// holding a LONGER list would leave its extra entries in the bound list behind a shorter new one — tracks
  /// from an old playlist appearing in this one. Which variants exist is read from the store once, on the
  /// first write, and reused for the life of this source. A variant row created after that first write
  /// would not be updated until the next restart. The case variants measured on the box come from the
  /// System Config page, and neither File Player preferences DTO carries these three keys.
  /// </para>
  /// <para>
  /// <b><c>CurrentValue</c> is updated after the store write</b>, because
  /// <c>PreferencesPersistenceService</c> writes <c>CurrentValue</c> back to the store every 30 seconds.
  /// Before AUD-98 this method left <c>CurrentValue</c> alone, so whenever no configuration reload happened
  /// in between, that periodic save wrote the queue from the last reload over this one. Races remain,
  /// each needing a periodic save or a reload to overlap this write. A periodic save that serialised
  /// <c>CurrentValue</c> before the update below can land the previous queue after this write. A reload
  /// already in flight can rebuild <c>CurrentValue</c> from the store as it was before this write. And the
  /// three assignments are not atomic, so a periodic save can pair the new list with the old index, which
  /// moves the played/current boundary but drops no track. Each is repaired by the next queue change,
  /// which writes again; until then a restart restores the earlier list.
  /// </para>
  /// </remarks>
  private async Task SaveQueueStateAsync(PersistedQueue snapshot)
  {
    if (_configurationManager == null)
    {
      return;
    }

    await _queueSaveGate.WaitAsync();
    try
    {
      if (snapshot.Generation != Volatile.Read(ref _queueSaveGeneration))
      {
        // A newer snapshot has been captured; its own write will follow this one through the gate.
        return;
      }

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

      var values = new Dictionary<string, string>
      {
        [$"{FilePlayerPreferences.SectionName}:{nameof(FilePlayerPreferences.QueueItems)}"] =
          System.Text.Json.JsonSerializer.Serialize(snapshot.Items),
        [$"{FilePlayerPreferences.SectionName}:{nameof(FilePlayerPreferences.CurrentQueueIndex)}"] =
          snapshot.CurrentIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
        [$"{FilePlayerPreferences.SectionName}:{nameof(FilePlayerPreferences.OriginalOrder)}"] =
          System.Text.Json.JsonSerializer.Serialize(snapshot.OriginalOrder),
      };

      if (_queueKeyVariants == null)
      {
        IReadOnlyList<ConfigurationEntry> existing = await store.GetAllEntriesAsync(ConfigurationReadMode.Raw);
        _queueKeyVariants = values.Keys.ToDictionary(
          key => key,
          key => existing
            .Select(e => e.Key)
            .Where(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
            .Append(key)
            .Distinct(StringComparer.Ordinal)
            .ToArray());
      }

      var entries = new List<ConfigurationEntry>();
      foreach (KeyValuePair<string, string> value in values)
      {
        entries.AddRange(_queueKeyVariants[value.Key]
          .Select(k => new ConfigurationEntry { Key = k, Value = value.Value }));
      }

      await store.SetEntriesAsync(entries);
      await store.SaveAsync();

      ApplyToPreferences(snapshot);

      Logger.LogTrace("Queue state persisted to configuration store: {Count} items", snapshot.Items.Count);
    }
    catch (Exception ex)
    {
      Logger.LogWarning(ex, "Failed to save queue state to configuration store");
    }
    finally
    {
      _queueSaveGate.Release();
    }
  }

  /// <summary>
  /// The most recently started queue-state write. A test rendezvous, and only for a single caller: with
  /// concurrent callers it can be an older snapshot's write, which the generation check skips.
  /// </summary>
  internal Task WhenQueueStateSavedAsync() => _lastQueueSave;

  /// <summary>AUD-98. A playlist as persisted: see <see cref="CapturePersistedQueue"/>.</summary>
  private sealed record PersistedQueue(
    List<string> Items, int CurrentIndex, List<string> OriginalOrder, long Generation);

  /// <summary>AUD-98. A playlist as restored: see <see cref="BuildRestoredQueue"/>.</summary>
  internal sealed record RestoredQueue(
    List<string> Played, string Current, List<string> Upcoming, List<string> OriginalOrder);

  /// <summary>
  /// AUD-98. Rebuilds a playlist from its persisted form, keeping only files <paramref name="exists"/>
  /// accepts. Returns <c>null</c> when none of <paramref name="savedItems"/> exists.
  /// </summary>
  /// <remarks>
  /// <list type="bullet">
  ///   <item>Entries of <paramref name="savedItems"/> before <paramref name="savedCurrentIndex"/> are the
  ///     played tracks; the first existing entry at or after it is the current track; the rest are
  ///     upcoming. A negative index is read as 0. A list saved before AUD-98 held only the current and
  ///     upcoming tracks, with an index of 0 on every box measured, so it restores as it did then: nothing
  ///     played, the first track current.</item>
  ///   <item>Missing files are skipped without moving the boundary: an entry is played or not by its
  ///     position in the SAVED list. If no existing entry sits at or after the index, the last existing
  ///     played track becomes the current one rather than leaving no current track.</item>
  ///   <item>The unshuffled order is <paramref name="savedOriginalOrder"/>'s existing files, followed by
  ///     any restored track it does not contain (so Repeat All cannot leave one out). When it is null or
  ///     has no existing file — every list saved before AUD-98 — it is the restored list in play order,
  ///     which is what the restore used before.</item>
  ///   <item>Each distinct path is checked with <paramref name="exists"/> once.</item>
  /// </list>
  /// </remarks>
  internal static RestoredQueue? BuildRestoredQueue(
    IReadOnlyList<string> savedItems,
    int savedCurrentIndex,
    IReadOnlyList<string>? savedOriginalOrder,
    Func<string, bool> exists)
  {
    var existsCache = new Dictionary<string, bool>(StringComparer.Ordinal);
    bool Exists(string? path)
    {
      if (string.IsNullOrEmpty(path))
      {
        return false;
      }
      if (!existsCache.TryGetValue(path, out bool found))
      {
        found = exists(path);
        existsCache[path] = found;
      }
      return found;
    }

    int boundary = Math.Max(0, savedCurrentIndex);
    var played = new List<string>();
    string? current = null;
    var upcoming = new List<string>();

    for (int i = 0; i < savedItems.Count; i++)
    {
      string path = savedItems[i];
      if (!Exists(path))
      {
        continue;
      }

      if (i < boundary)
      {
        played.Add(path);
      }
      else if (current == null)
      {
        current = path;
      }
      else
      {
        upcoming.Add(path);
      }
    }

    if (current == null)
    {
      if (played.Count == 0)
      {
        return null;
      }

      current = played[^1];
      played.RemoveAt(played.Count - 1);
    }

    var restoredList = new List<string>(played) { current };
    restoredList.AddRange(upcoming);

    List<string> originalOrder = (savedOriginalOrder ?? Array.Empty<string>()).Where(p => Exists(p)).ToList();
    if (originalOrder.Count == 0)
    {
      originalOrder = new List<string>(restoredList);
    }
    else
    {
      var inOrder = new HashSet<string>(originalOrder, StringComparer.Ordinal);
      originalOrder.AddRange(restoredList.Where(inOrder.Add));
    }

    return new RestoredQueue(played, current, upcoming, originalOrder);
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
      _audioEngine ??= SerializedMiniAudioEngine.Create();

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

    // Read metadata from the file (AudioTagReader: tags only, separate from decoding)
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
    // AUD-33: a different file is a new track; re-reading the same one (repeat, queue edits,
    // restore) is not, so an identification already in flight for it stays valid.
    if (!string.Equals(filePath, _trackStartedFile, StringComparison.Ordinal))
    {
      _trackStartedFile = filePath;
      Volatile.Write(ref _trackStartedAtTicks, DateTime.UtcNow.Ticks);
    }

    // AUD-96: the track being loaded is the row most likely to be looked at, so re-check its cached row
    // against the file (one stat on the background reader; a re-read only if the file changed).
    _queueMetadata.Revalidate([filePath]);

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

    // AUD-32: AudioTagReader tries SoundFlow and falls back to TagLib. SoundFlow alone rejects some
    // real ID3v2.2 tags, and that failure used to leave placeholders here — which AUD-1's per-field
    // rule then (correctly) treated as missing, letting fingerprinting replace a tagged artist.
    try
    {
      var tags = AudioTagReader.Read(filePath, Logger);
      if (tags != null)
      {
        _duration = tags.Duration ?? TimeSpan.Zero;
        _metadata[StandardMetadataKeys.Duration] = _duration;
        if (tags.SampleRate.HasValue)
        {
          _metadata["SampleRate"] = tags.SampleRate.Value;
        }
        if (tags.Channels.HasValue)
        {
          _metadata["Channels"] = tags.Channels.Value;
        }
        if (tags.Bitrate.HasValue)
        {
          _metadata["BitRate"] = tags.Bitrate.Value;
        }

        // Extract embedded album art via TagLib (SoundFlow doesn't read pictures)
        ExtractEmbeddedAlbumArt(filePath);

        // Tags override the defaults seeded above; AudioTagReader reports blank tags as null.
        if (tags.Title != null)
        {
          _metadata[StandardMetadataKeys.Title] = tags.Title;
        }
        if (tags.Artist != null)
        {
          _metadata[StandardMetadataKeys.Artist] = tags.Artist;
        }
        if (tags.Album != null)
        {
          _metadata[StandardMetadataKeys.Album] = tags.Album;
        }
        if (tags.Genre != null)
        {
          _metadata[StandardMetadataKeys.Genre] = tags.Genre;
        }
        if (tags.Year.HasValue)
        {
          _metadata[StandardMetadataKeys.Year] = tags.Year.Value;
        }
        if (tags.TrackNumber.HasValue)
        {
          _metadata[StandardMetadataKeys.TrackNumber] = tags.TrackNumber.Value;
        }

        Logger.LogDebug(
          "Loaded metadata for {File} via {Reader}: Title={Title}, Artist={Artist}, Duration={Duration}",
          Path.GetFileName(filePath),
          tags.ReadBy,
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
        // Neither SoundFlow nor TagLib could read the file. Once per track load, so a Warning: before
        // AUD-32 this was a Debug line and the failure was invisible on the appliance.
        _duration = TimeSpan.Zero;
        _metadata[StandardMetadataKeys.Duration] = _duration;
        _metadata["NeedsFingerprintingLookup"] = true;
        Logger.LogWarning("Could not read tags from {File}; using the file name as the title", filePath);
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
  /// Fills metadata fields the file's own tags left missing, deciding each field
  /// independently (AUD-1). Cover art is filled whenever it is missing, including for a file
  /// whose tags are complete; title/artist/album are filled only while
  /// NeedsFingerprintingLookup is set, which this method clears once it has run.
  /// </summary>
  private void OnTrackIdentified(object? sender, TrackIdentifiedEventArgs e)
  {
    // TrackIdentified is broadcast to every subscriber, not just the source the
    // audio came from. Only the active source may adopt an identification —
    // fingerprinting always taps the active source's audio.
    if (!IsActiveSource)
    {
      return;
    }

    // Only update metadata if this is the active source
    if (State != AudioSourceState.Playing && State != AudioSourceState.Paused)
    {
      return;
    }

    // AUD-33: capture + recognition takes ~15 s. A result whose sample began before this file
    // became the current track describes the previous one; merging it here put Eve 6 on
    // "Meditating Beat" on the appliance (2026-09-26).
    var trackStartedTicks = Volatile.Read(ref _trackStartedAtTicks);
    // AUD-34: the boundary is also this source's last activation, so a sample captured from the
    // previous source (or before a switch away and back on the same track) is dropped too.
    if (e.WasCapturedBefore(LatestTrackBoundaryUtc(trackStartedTicks)))
    {
      Logger.LogInformation(
        "Dropped fingerprint result '{Title}' by '{Artist}': sampled before the current file started or the file player became the active source",
        e.Track.Title, e.Track.Artist);
      // The service marked this song as recently identified before raising the event; without this,
      // a straddling capture that named the NEW file would suppress its own re-identification.
      _identificationService?.ForgetRecentIdentification(e.Track);
      return;
    }

    var track = e.Track;

    // Check if current file needs fingerprinting metadata lookup
    bool needsLookup = _metadata.ContainsKey("NeedsFingerprintingLookup")
      && _metadata["NeedsFingerprintingLookup"] is bool b && b;

    // AUD-1: cover art, by the same per-field rule as everything else (owner decision
    // 2026-09-08). Kept above the needsLookup return, as the art fill was before AUD-1: a
    // file whose tags are complete but which carries no embedded art still gets art.
    //
    // ExtractEmbeddedAlbumArt has already put a content-addressed /api/albumart/<hash> path
    // here when the file had an APIC frame; absent that, AlbumArtUrl still holds the
    // DefaultAlbumArtUrl UpdateMetadataFromFile seeded, which the shared rule treats as
    // missing. ⚠ Before AUD-1, whenever UseShazamForAllSources (true on the appliance) and
    // the lookup flag were both set and SongRec returned art, a separate branch REPLACED
    // embedded art — measured on the appliance: one stable hash per song from the embedded
    // art, at least nine different hashes for the same song from SongRec.
    var filledArt = false;
    if (SourceMetadataPrecedence.ShouldFillAlbumArt(_metadata)
        && !string.IsNullOrEmpty(track.CoverArtUrl))
    {
      _metadata[StandardMetadataKeys.AlbumArtUrl] = track.CoverArtUrl;
      filledArt = true;
      Logger.LogInformation("Album art URL set for '{Title}': {Url}", track.Title, track.CoverArtUrl);
    }

    if (!needsLookup)
    {
      return;
    }

    // AUD-1: the same per-field rule BluetoothAudioSource uses, from the same helper. ID3
    // tags are source metadata and win where they exist; only missing fields are filled.
    // (Before AUD-1, UseShazamForAllSources — true on the appliance — sent every first
    // identification of a track down a branch that replaced all four fields.)
    //
    // The filename is an extra "no title" placeholder because UpdateMetadataFromFile seeds
    // Title with Path.GetFileNameWithoutExtension and replaces it only when the file has a
    // non-blank Title tag (AudioTagReader reports blank tags as null), so "title equals filename" means "no title tag". ⚠ It cannot tell
    // that apart from a file whose Title tag genuinely equals its filename — that file's
    // title is treated as missing, exactly as it was before AUD-1.
    var filled = SourceMetadataPrecedence.FillMissingFrom(
      _metadata,
      track,
      Path.GetFileNameWithoutExtension(_currentFile ?? string.Empty));

    Logger.LogInformation(
      "Fingerprint result for file '{Title}' by '{Artist}' (confidence: {Confidence:P0}); filled from it: {Fields}",
      track.Title, track.Artist, e.Confidence, filled.Describe(filledArt));

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

    // Mark that an identification has been processed for this track — which stops SongRec
    // re-applying every cycle. ⚠ "MetadataSource" records that an identification ran, NOT
    // that it contributed anything: after AUD-1 a fully tagged file still gets
    // "Fingerprinting" here with every field its own. No code under src/ looks the key up by
    // name (it can still travel with the whole metadata dictionary).
    _metadata["NeedsFingerprintingLookup"] = false;
    _metadata["IdentificationConfidence"] = e.Confidence;
    _metadata["IdentifiedAt"] = e.IdentifiedAt;
    _metadata["MetadataSource"] = "Fingerprinting";
  }
}

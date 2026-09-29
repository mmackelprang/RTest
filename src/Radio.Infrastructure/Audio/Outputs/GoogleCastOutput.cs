using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Extensions;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Metrics;
using Sharpcaster;
using Sharpcaster.Channels;
using Sharpcaster.Models;
using Sharpcaster.Models.ChromecastStatus;
using Sharpcaster.Models.Media;

namespace Radio.Infrastructure.Audio.Outputs;

/// <summary>
/// Google Chromecast audio output implementation using SharpCaster.
/// Streams audio to Chromecast devices via HTTP stream endpoint.
/// </summary>
public class GoogleCastOutput : AudioOutputBase
{
  private readonly ILogger<GoogleCastOutput> _logger;
  private readonly GoogleCastOutputOptions _options;
  private readonly CastDeviceCacheRepository? _cacheRepository;
  private readonly IMetricsCollector? _metricsCollector;
  private readonly ICastDeviceVolumeStore? _volumeStore;
  private ChromecastClient? _client;
  private ChromecastReceiver? _connectedReceiver;

  // Serializes the connection-state SWAPS of _client / _connectedReceiver /
  // ConnectedDevice — not every access of them. (An earlier revision of this
  // comment said it guarded every read. It never did, and believing it makes the
  // unlocked dereferences described below look safer than they actually are.)
  //
  // WHAT IT SERIALIZES: seven await-free critical sections, each touching
  // _connectionGeneration and/or those three fields as one consistent unit:
  //     InitializeAsync         bump, install a fresh client, clear receiver+device
  //     ConnectAsync's claim    bump, snapshot _client
  //     TryPublishConnection    generation check, then publish client+receiver+device
  //     IsCurrentGeneration     generation check only — mutates nothing
  //     DisconnectAsync         bump, snapshot, clear receiver+device
  //     DisposeAsync            bump, snapshot _client (deliberately NOT clearing it)
  //     StopAsync               snapshot _client only — touches no generation
  // Being await-free is the point: no writer can be preempted mid-swap, so no
  // reader THAT TAKES THE LOCK can observe a half-applied connection. Readers
  // that skip the lock get no such guarantee — see below for why that is sound.
  //
  // Held ONLY for those swaps — never across a SharpCaster network call. That is
  // deliberate: a connect can sit for tens of seconds inside calls that do not
  // observe cancellation, and a teardown that had to queue behind it would turn a
  // data race into a hang. Instead teardown takes the state out from under the
  // connect and does its network work on a snapshot.
  //
  // READS ARE DELIBERATELY UNSYNCHRONIZED. Most reads of these fields — across
  // the start/stream/volume/teardown paths — take no lock at all. Two of them
  // null-check a field and then dereference it on a second, separate read:
  // SetCastVolumeAsync and SetCastMuteAsync, as do the `_client!` dereferences in
  // the Start/stream helpers. (SyncInitialVolumeAsync was a third until AUD-5; it
  // now takes its client as a parameter and does not read the field at all.)
  // Reference assignment is
  // atomic, so such a read always yields a whole reference — but "whole" is not
  // "non-null", and check-then-dereference is sound only because of this:
  //
  //     PRECONDITION: _client IS NEVER SET BACK TO NULL. It is assigned in
  //     exactly two places — InitializeAsync and TryPublishConnectionAsync — and
  //     both assign a non-null client, so the field only ever moves null -> set
  //     -> set, never set -> null. DisconnectAsync and DisposeAsync deliberately
  //     leave it set (see the note in DisposeAsync). A reader that has ONCE
  //     observed non-null can therefore never subsequently observe null; it may
  //     see a stale or a newer client, but not a null one.
  //
  //     Note what that does NOT say: the field IS null before the first
  //     successful InitializeAsync, and stays null if the ChromecastClient ctor
  //     throws. So the `_client!` dereferences are not licensed by this
  //     precondition alone — each sits behind a caller's null guard (StartAsync
  //     for the Start/stream chain, the reload guard for the metadata path).
  //     What the precondition buys them is that their guard stays valid across
  //     the awaits that follow it. The two check-then-dereference sites named
  //     above rely on it directly.
  //
  //     NULL THIS FIELD AND EVERY UNLOCKED DEREFERENCE ABOVE BECOMES AN NRE —
  //     snapshot it under the lock at each of those sites first.
  //
  // _connectedReceiver and ConnectedDevice ARE cleared (InitializeAsync,
  // DisconnectAsync), so unlocked reads of those two can legitimately see null.
  // That is safe only because no unlocked site dereferences them —
  // _connectedReceiver is read purely as an "is anything connected" flag, and
  // ConnectedDevice only through `?.`. Keep it that way.
  //
  // What the unlocked reads do accept is a bounded race: a command aimed at a
  // client that has since been superseded or torn down. For SetCastVolumeAsync
  // and SetCastMuteAsync that is genuinely cheap — SharpCaster throws, the
  // surrounding try/catch logs it, and the winning connection is untouched. A
  // wasted network call, not corrupt state.
  //
  // SyncInitialVolumeAsync WAS the exception to that reassurance, and AUD-5 addressed
  // it. It is a read rather than a command, and its success path fires
  // CastVolumeChanged — whose subscriber wrote AudioManager.MasterVolume, a setter
  // that schedules a persist. Nothing re-checked _connectionGeneration between the
  // status response and the event fire, so a teardown landing in that window could
  // publish the volume of a connection that was no longer current, and the console
  // kept it across a restart. Two things changed, and which one does what matters:
  //   - The method now takes its client AND its generation as parameters, and
  //     re-checks the generation immediately before the fire. That removes the network
  //     round-trip from the window. It does NOT make the window empty: the lock is
  //     released before the Invoke, because subscriber code must never run under it.
  //   - AudioStateUpdateService.OnCastVolumeChanged now ignores IsInitialSync events
  //     outright. THAT is what makes an initial read unable to move master volume, and
  //     it has no timing dependence at all.
  // Widening this lock was and remains the wrong fix: the exposure is the event fire,
  // not the field read, and holding it across a SharpCaster call is the hang described
  // above.
  private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

  // Bumped by anything that supersedes an in-flight connect: a newer connect, a
  // disconnect, or disposal. A connect captures this when it starts and
  // re-validates before publishing its result — if it changed, the attempt lost
  // the race and tears down what it built instead of overwriting the winner.
  private int _connectionGeneration;

  /// <summary>
  /// <b>Test seam (kind C — substitution).</b> Awaited inside <see cref="ConnectAsync"/>
  /// after the receiver has been resolved but before the network connect, which is
  /// precisely where the connect/teardown race used to corrupt state. Set by
  /// <c>GoogleCastOutputConcurrencyTests</c> (<c>:51</c>, <c>:116</c>).
  /// <b>Why the real path is unreachable:</b> the window is microseconds wide, so a stress
  /// loop lands on it only by luck; the hook makes the interleaving deterministic.
  /// <b>NOT covered by this seam:</b> nothing — it inserts a pause, it does not replace a
  /// collaborator. The connect either side of it is the real one.
  /// Null (and therefore free) in production.
  /// </summary>
  internal Func<Task>? ConnectRaceHookForTests { get; set; }

  /// <summary>
  /// <b>Test seam (kind C — substitution).</b> Substitutes the SharpCaster transport
  /// connect. Set by <c>GoogleCastOutputConcurrencyTests:121</c>.
  /// <b>Why the real path is unreachable:</b> a fake socket can never complete a Cast
  /// handshake, so offline the connect always throws and diverts into the error handler —
  /// making the supersede-after-a-SUCCESSFUL-connect path, which is the whole point of the
  /// generation check, unreachable without hardware.
  /// <b>NOT covered by this seam:</b> the real SharpCaster handshake and everything its
  /// failure modes imply. The generation check, the publish and the teardown either side
  /// are real; the socket is not. Only hardware UAT covers the transport itself — and per
  /// <c>AUD-3</c> residue (a), that UAT has never been performed.
  /// Null (and therefore free) in production.
  /// </summary>
  internal Func<ChromecastReceiver, Task>? ConnectTransportOverrideForTests { get; set; }

  /// <summary>
  /// <b>Test seam (kind C — substitution).</b> Substitutes the Cast status read inside
  /// <see cref="SyncInitialVolumeAsync"/>. Set by <c>GoogleCastOutputConcurrencyTests</c>.
  /// <b>Why the real path is unreachable:</b> no fake socket can answer a Cast GET_STATUS,
  /// so offline the read always throws and the method diverts into its catch before the
  /// generation check is ever evaluated. Awaiting inside this delegate is also what lets a
  /// test interleave a teardown at exactly the point the network round-trip occupies in
  /// production.
  /// <b>NOT covered by this seam:</b> SharpCaster's status parsing. The generation check,
  /// the priming and the event fire either side of it are real.
  /// Null (and therefore free) in production.
  /// </summary>
  internal Func<Task<(float Volume, bool Muted)?>>? CastStatusReadOverrideForTests { get; set; }

  /// <summary>
  /// <b>Test seam (kind C — substitution).</b> Substitutes the Cast SET_VOLUME inside
  /// <see cref="PushVolumeToDeviceAsync"/>. Set by <c>GoogleCastOutputVolumeMemoryTests</c>.
  /// <b>Why the real path is unreachable:</b> the same as
  /// <see cref="CastStatusReadOverrideForTests"/> — no fake socket speaks the Cast protocol.
  /// <b>NOT covered by this seam:</b> whether the device honours the level. The choice of
  /// level, and the echo-filter baseline set before sending it, are real.
  /// Null (and therefore free) in production.
  /// </summary>
  internal Func<float, Task>? CastSetVolumeOverrideForTests { get; set; }
  private string? _streamUrl;

  // Direct Channel streaming (experimental)
  private IAudioEngine? _audioEngine;
  private DirectCastStreamingService? _directStreaming;
  private DirectCastAudioChannel? _directChannel;

  // Cache discovered receivers to use the original objects for connection
  // Indexed by device ID (DeviceUri.ToString()) and also by IP address for fallback matching
  private readonly Dictionary<string, ChromecastReceiver> _discoveredReceivers = new();
  private readonly Dictionary<string, ChromecastReceiver> _discoveredReceiversByIp = new();
  private CastNowPlayingMetadata? _nowPlayingMetadata;

  // Metadata update debounce: when metadata changes rapidly (source switch
  // → "No Track" → actual track → fingerprint ID), coalesce into a single
  // Cast media reload to avoid garbled audio from rapid reconnections.
  private CancellationTokenSource? _metadataDebouncesCts;
  private readonly object _debounceLock = new();

  // Volume sync: track locally-initiated volume changes to filter out echo events
  private float _lastSetVolume = -1f;
  private bool _lastSetMute;
  private bool _suppressNextVolumeEvent;

  // AUD-80: the level the current connection should hold on the device — the volume
  // remembered for it, else the level it reported when first seen, else NaN (unknown).
  // SyncVolumeAfterStartAsync pushes this after the receiver app launches; NaN means
  // "leave the device alone". Written from the connect path and from SharpCaster's
  // status callback, so accessed through Volatile (a float write is atomic; a float?
  // would not be).
  private float _connectionVolume = float.NaN;

  /// <inheritdoc />
  protected override ILogger Logger => _logger;

  /// <inheritdoc />
  public override AudioOutputType Type => AudioOutputType.GoogleCast;

  /// <summary>
  /// Event raised when a Chromecast device is discovered.
  /// </summary>
  public event EventHandler<ChromecastDeviceDiscoveredEventArgs>? DeviceDiscovered;

  /// <summary>
  /// Event raised when connected to a Chromecast device.
  /// </summary>
  public event EventHandler<ChromecastConnectedEventArgs>? Connected;

  /// <summary>
  /// Event raised when disconnected from a Chromecast device.
  /// </summary>
  public event EventHandler<ChromecastDisconnectedEventArgs>? Disconnected;

  /// <summary>
  /// Event raised with the Cast device's volume and mute state: once after each connect
  /// with the level the device holds after the initial sync — which may be a remembered
  /// level this application just pushed (AUD-80) — flagged <c>IsInitialSync</c>; and
  /// whenever the state changes externally (Google Home app, voice command, physical
  /// controls). Not fired for the device's confirmation of a level this application set.
  /// </summary>
  public event EventHandler<CastVolumeChangedEventArgs>? CastVolumeChanged;

  /// <summary>
  /// Gets the currently connected Chromecast device information.
  /// </summary>
  public ChromecastDeviceInfo? ConnectedDevice { get; private set; }

  /// <summary>
  /// Gets the Google Cast output options for external inspection (e.g., by controllers
  /// to determine the streaming mode).
  /// </summary>
  public GoogleCastOutputOptions Options => _options;

  /// <summary>
  /// Initializes a new instance of the <see cref="GoogleCastOutput"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="options">The Google Cast output options.</param>
  /// <param name="cacheRepository">Optional SQLite-backed cache repository.</param>
  /// <param name="metricsCollector">Optional metrics collector for streaming metrics.</param>
  /// <param name="volumeStore">
  /// Optional per-device volume memory (AUD-80). Without it every connection adopts the
  /// device's own level and nothing is restored.
  /// </param>
  public GoogleCastOutput(
    ILogger<GoogleCastOutput> logger,
    IOptions<AudioOutputOptions> options,
    CastDeviceCacheRepository? cacheRepository = null,
    IMetricsCollector? metricsCollector = null,
    ICastDeviceVolumeStore? volumeStore = null)
    : base("cast-output", "Google Cast Output",
        options?.Value?.GoogleCast?.DefaultVolume ?? 0.7f,
        options?.Value?.GoogleCast?.Enabled ?? false)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _options = options?.Value?.GoogleCast ?? throw new ArgumentNullException(nameof(options));
    _cacheRepository = cacheRepository;
    _metricsCollector = metricsCollector;
    _volumeStore = volumeStore;
  }

  /// <inheritdoc />
  protected override void OnVolumeChanged(float volume)
  {
    // Apply volume to connected device if available
    SetCastVolumeAsync(volume).SafeFireAndForget(_logger, "SetCastVolume");
  }

  /// <inheritdoc />
  protected override void OnMuteChanged(bool muted)
  {
    // Apply mute state to connected device if available
    SetCastMuteAsync(muted).SafeFireAndForget(_logger, "SetCastMute");
  }

  /// <inheritdoc />
  public override async Task InitializeAsync(CancellationToken cancellationToken = default)
  {
    ValidateCanInitialize();

    State = AudioOutputState.Initializing;

    try
    {
      _logger.LogInformation("Initializing Google Cast output");

      // Swap in a fresh client under the lock, taking the old one as a snapshot.
      // Bumping the generation invalidates any connect still in flight — it will
      // discard itself rather than publish onto the client we just replaced.
      ChromecastClient? stale;
      await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
      try
      {
        _connectionGeneration++;
        stale = _client;
        _client = new ChromecastClient();
        _connectedReceiver = null;
        ConnectedDevice = null;
      }
      finally
      {
        _lifecycleLock.Release();
      }

      // Network work happens on the snapshot, outside the lock.
      if (stale != null)
      {
        try { await stale.DisconnectAsync().ConfigureAwait(false); }
        catch (Exception ex) { _logger.LogDebug(ex, "Error disconnecting previous Cast client during reinit"); }
      }

      State = AudioOutputState.Ready;
      _logger.LogInformation("Google Cast output initialized");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to initialize Google Cast output");
      State = AudioOutputState.Error;
      throw;
    }
  }

  /// <summary>
  /// Returns cached Cast devices without running mDNS discovery.
  /// Fast — no network I/O. Removes stale entries before returning.
  /// </summary>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>A list of cached Chromecast devices.</returns>
  public async Task<IReadOnlyList<ChromecastDeviceInfo>> GetCachedDevicesAsync(
    CancellationToken ct = default)
  {
    ThrowIfDisposed();

    var cachedDevices = await LoadCacheAsync();

    // Remove stale entries
    var expirationCutoff = DateTime.UtcNow.AddDays(-_options.CacheExpirationDays);
    var staleKeys = cachedDevices
      .Where(kv => kv.Value.LastSeen < expirationCutoff)
      .Select(kv => kv.Key)
      .ToList();
    foreach (var key in staleKeys)
    {
      cachedDevices.Remove(key);
    }

    _logger.LogDebug("Returning {Count} cached Cast devices", cachedDevices.Count);
    return cachedDevices.Values.Select(c => c.Device).ToList();
  }

  /// <summary>
  /// Discovers available Chromecast devices on the network.
  /// Merges live mDNS results with a persistent cache so previously seen
  /// devices remain available even if they are temporarily offline.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A list of discovered Chromecast devices.</returns>
  public async Task<IReadOnlyList<ChromecastDeviceInfo>> DiscoverDevicesAsync(
    CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    _logger.LogInformation(
      "Starting Chromecast device discovery (timeout: {Timeout}s)",
      _options.DiscoveryTimeoutSeconds);

    // Load persistent cache first (SQLite if available, fallback to JSON)
    var cachedDevices = await LoadCacheAsync();

    try
    {
      ChromecastLocator locator = new ChromecastLocator();
      var discoveredDevices = await locator.FindReceiversAsync(TimeSpan.FromSeconds(10));

      foreach (var device in discoveredDevices)
      {
        if (device?.DeviceUri == null)
        {
          _logger.LogDebug("Skipping discovered device with null DeviceUri");
          continue;
        }

        var deviceId = device.DeviceUri.ToString();

        var deviceInfo = new ChromecastDeviceInfo
        {
          Id = deviceId,
          FriendlyName = device.Name ?? "Unknown",
          IpAddress = device.DeviceUri.Host,
          Port = device.DeviceUri.Port,
          Model = device.Model ?? "Unknown"
        };

        // Update or add to cache
        cachedDevices[deviceId] = new CachedCastDevice
        {
          Device = deviceInfo,
          LastSeen = DateTime.UtcNow
        };

        // Keep the live receiver for connection (indexed by ID and by IP for fallback)
        _discoveredReceivers[deviceId] = device;
        _discoveredReceiversByIp[deviceInfo.IpAddress] = device;

        DeviceDiscovered?.Invoke(this, new ChromecastDeviceDiscoveredEventArgs { Device = deviceInfo });

        _logger.LogDebug(
          "Discovered Chromecast: {Name} at {IP}:{Port}",
          deviceInfo.FriendlyName, deviceInfo.IpAddress, deviceInfo.Port);
      }

      _logger.LogInformation("Discovered {Count} Chromecast device(s) via mDNS, {CacheCount} total cached",
        discoveredDevices.Count(), cachedDevices.Count);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Error during Chromecast device discovery");
    }

    // Remove stale entries
    var expirationCutoff = DateTime.UtcNow.AddDays(-_options.CacheExpirationDays);
    var staleKeys = cachedDevices
      .Where(kv => kv.Value.LastSeen < expirationCutoff)
      .Select(kv => kv.Key)
      .ToList();
    foreach (var key in staleKeys)
    {
      if (cachedDevices.TryGetValue(key, out var stale))
      {
        _discoveredReceiversByIp.Remove(stale.Device.IpAddress);
      }
      cachedDevices.Remove(key);
      _discoveredReceivers.Remove(key);
    }

    // Save merged cache
    await SaveCacheAsync(cachedDevices);

    return cachedDevices.Values.Select(c => c.Device).ToList();
  }

  /// <summary>
  /// Loads the persistent device cache. Uses SQLite if available, falling back to JSON.
  /// Migrates JSON data to SQLite on first load.
  /// </summary>
  private async Task<Dictionary<string, CachedCastDevice>> LoadCacheAsync()
  {
    // Try SQLite first
    if (_cacheRepository != null)
    {
      var sqliteCache = await _cacheRepository.GetAllAsync();

      // One-time migration: if JSON file exists and SQLite is empty, import from JSON
      if (sqliteCache.Count == 0 && !string.IsNullOrEmpty(_options.CacheFilePath) &&
          File.Exists(_options.CacheFilePath))
      {
        try
        {
          var json = await File.ReadAllTextAsync(_options.CacheFilePath);
          var jsonCache = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, CachedCastDevice>>(json);
          if (jsonCache != null && jsonCache.Count > 0)
          {
            _logger.LogInformation("Migrating {Count} Cast devices from JSON to SQLite", jsonCache.Count);
            await _cacheRepository.SaveAllAsync(jsonCache);
            sqliteCache = jsonCache;

            // Remove the old JSON file after successful migration
            File.Delete(_options.CacheFilePath);
            _logger.LogInformation("Deleted old JSON cache file: {Path}", _options.CacheFilePath);
          }
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "Failed to migrate Cast device cache from JSON to SQLite");
        }
      }

      return sqliteCache;
    }

    // Fallback to JSON file
    try
    {
      if (!string.IsNullOrEmpty(_options.CacheFilePath) && File.Exists(_options.CacheFilePath))
      {
        var json = await File.ReadAllTextAsync(_options.CacheFilePath);
        var cached = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, CachedCastDevice>>(json);
        if (cached != null)
        {
          _logger.LogDebug("Loaded {Count} cached Cast devices from JSON", cached.Count);
          return cached;
        }
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to load Cast device cache");
    }

    return new Dictionary<string, CachedCastDevice>();
  }

  /// <summary>
  /// Saves the device cache. Uses SQLite if available, falling back to JSON.
  /// </summary>
  private async Task SaveCacheAsync(Dictionary<string, CachedCastDevice> cache)
  {
    if (_cacheRepository != null)
    {
      await _cacheRepository.SaveAllAsync(cache);
      var expirationCutoff = DateTime.UtcNow.AddDays(-_options.CacheExpirationDays);
      await _cacheRepository.RemoveStaleAsync(expirationCutoff);
      return;
    }

    // Fallback to JSON file
    try
    {
      var directory = Path.GetDirectoryName(_options.CacheFilePath);
      if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
      {
        Directory.CreateDirectory(directory);
      }

      var json = System.Text.Json.JsonSerializer.Serialize(cache, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
      await File.WriteAllTextAsync(_options.CacheFilePath!, json);
      _logger.LogDebug("Saved {Count} Cast devices to JSON cache", cache.Count);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to save Cast device cache");
    }
  }

  /// <summary>
  /// Connects to a specific Chromecast device.
  /// </summary>
  /// <param name="device">The device to connect to.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  public async Task ConnectAsync(ChromecastDeviceInfo device, CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(device);

    // Recover from Error state by reinitializing
    if (State == AudioOutputState.Error)
    {
      _logger.LogInformation("Recovering from Error state before connecting");
      await InitializeAsync(cancellationToken);
    }

    // Stop streaming before switching to a new device, and remember to restart after
    var wasStreaming = State == AudioOutputState.Streaming;
    if (wasStreaming)
    {
      _logger.LogInformation("Stopping current Cast stream before connecting to new device");
      await StopAsync(cancellationToken);
    }

    if (State != AudioOutputState.Ready && State != AudioOutputState.Stopped)
    {
      throw new InvalidOperationException(
        $"Cannot connect in state {State}. Output must be in Ready or Stopped state.");
    }

    State = AudioOutputState.Connecting;

    // Everything from here on must leave State somewhere recoverable. Connecting
    // is a dead end for this class: ConnectAsync refuses to run unless the state
    // is Ready/Stopped, and ValidateCanInitialize only accepts Created/Error — so
    // any path that returns while still Connecting wedges the output until the
    // process restarts. That includes the claim block below, which can throw
    // ObjectDisposedException on a disposed lock.
    try
    {
      // Claim this attempt. Anything that supersedes it (another connect, a
      // disconnect, disposal) bumps the generation, and the commit below then
      // abandons rather than publishing over the winner.
      int myGeneration;
      ChromecastClient? client;
      await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
      try
      {
        myGeneration = ++_connectionGeneration;
        client = _client;
      }
      finally
      {
        _lifecycleLock.Release();
      }

      _logger.LogInformation(
        "Connecting to Chromecast: {Name} at {IP}:{Port}",
        device.FriendlyName, device.IpAddress, device.Port);

      // Resolve the receiver into a LOCAL, so a concurrent teardown clearing
      // _connectedReceiver cannot null it out from under the connect below.
      // Try live discovery first by exact ID, then by IP (IDs can differ due to
      // URI normalization).
      ChromecastReceiver? receiver;
      if (_discoveredReceivers.TryGetValue(device.Id, out receiver))
      {
        _logger.LogDebug("Using live ChromecastReceiver from discovery (matched by ID)");
      }
      else if (_discoveredReceiversByIp.TryGetValue(device.IpAddress, out receiver))
      {
        _logger.LogInformation("Live ChromecastReceiver matched by IP {IP} (ID mismatch: cached={CachedId}, live={LiveId})",
          device.IpAddress, device.Id, receiver.DeviceUri);
      }
      else
      {
        // Device is from persistent cache, not live discovery.
        // Verify reachability with a TCP connect check before attempting full connection.
        var connectPort = await FindReachablePortAsync(device, cancellationToken).ConfigureAwait(false);

        var deviceUri = new Uri($"https://{device.IpAddress}:{connectPort}");
        _logger.LogInformation("TCP check passed, creating ChromecastReceiver from cache: {Uri}", deviceUri);

        // Create a fresh ChromecastClient for cached connections to avoid stale
        // socket state. Built locally and only published at commit time.
        var stale = client;
        client = new ChromecastClient();
        if (stale != null)
        {
          try { await stale.DisconnectAsync().ConfigureAwait(false); }
          catch (Exception ex) { _logger.LogDebug(ex, "Error disconnecting previous Cast client for cached connection"); }
        }

        receiver = new ChromecastReceiver
        {
          DeviceUri = deviceUri,
          Port = connectPort,
          Name = device.FriendlyName,
          Model = device.Model
        };
      }

      if (client == null)
      {
        throw new InvalidOperationException("Client not initialized. Call InitializeAsync first.");
      }

      if (ConnectRaceHookForTests != null)
      {
        await ConnectRaceHookForTests().ConfigureAwait(false);
      }

      _logger.LogDebug("Calling ConnectChromecast with URI: {Uri}", receiver.DeviceUri);
      if (ConnectTransportOverrideForTests != null)
      {
        await ConnectTransportOverrideForTests(receiver).ConfigureAwait(false);
      }
      else
      {
        await client.ConnectChromecast(receiver).ConfigureAwait(false);
      }

      // Publish only if nothing superseded us while we were on the network.
      if (!await TryPublishConnectionAsync(myGeneration, client, receiver, device, cancellationToken).ConfigureAwait(false))
      {
        _logger.LogWarning(
          "Cast connect to {Name} was superseded while connecting — discarding this connection",
          device.FriendlyName);
        try { await client.DisconnectAsync().ConfigureAwait(false); }
        catch (Exception ex) { _logger.LogDebug(ex, "Error discarding superseded Cast connection"); }

        // Hand the state machine back to the winner. Returning while still
        // Connecting would leave the output permanently unusable — and losing
        // this race is the NORMAL outcome of a teardown during startup
        // auto-connect, not an edge case.
        State = AudioOutputState.Ready;
        return;
      }

      // Subscribe to receiver status changes for bidirectional volume sync
      SubscribeToReceiverStatus(client);

      // Read initial device volume. The generation goes with it: the read is a
      // network round-trip, and this connection can be superseded inside it.
      await SyncInitialVolumeAsync(client, myGeneration, device).ConfigureAwait(false);

      Connected?.Invoke(this, new ChromecastConnectedEventArgs { Device = device });

      _logger.LogInformation(
        "Connected to Chromecast: {Name}",
        device.FriendlyName);

      State = AudioOutputState.Ready;

      // Resume streaming if we auto-stopped to switch devices
      if (wasStreaming)
      {
        _logger.LogInformation("Restarting Cast stream after device switch");
        await StartAsync(cancellationToken).ConfigureAwait(false);
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to connect to Chromecast: {Name}", device.FriendlyName);
      State = AudioOutputState.Error;
      throw;
    }
  }

  /// <summary>
  /// Publishes a completed connection, but only if this attempt is still the
  /// current one. Returns false when a newer connect, a disconnect, or disposal
  /// superseded it while it was on the network — in which case the caller owns
  /// tearing down what it built.
  /// </summary>
  private async Task<bool> TryPublishConnectionAsync(
    int generation,
    ChromecastClient client,
    ChromecastReceiver receiver,
    ChromecastDeviceInfo device,
    CancellationToken cancellationToken)
  {
    await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      if (_connectionGeneration != generation)
      {
        return false;
      }

      _client = client;
      _connectedReceiver = receiver;
      ConnectedDevice = device;
      Name = $"Cast: {device.FriendlyName}";
      return true;
    }
    finally
    {
      _lifecycleLock.Release();
    }
  }

  /// <summary>
  /// Disconnects from the currently connected Chromecast device.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  public async Task DisconnectAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    // Take the connection state out from under any in-flight connect FIRST,
    // then do the network work on the snapshot. Bumping the generation here is
    // what lets a teardown preempt a connect parked in an uncancellable
    // SharpCaster call instead of queueing behind it — queueing would convert
    // this race into a multi-second hang on the output picker.
    ChromecastClient? client;
    ChromecastDeviceInfo? disconnectedDevice;
    bool hadConnection;
    await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      _connectionGeneration++;
      hadConnection = _connectedReceiver != null;
      client = _client;
      disconnectedDevice = ConnectedDevice;
      _connectedReceiver = null;
      ConnectedDevice = null;
    }
    finally
    {
      _lifecycleLock.Release();
    }

    if (!hadConnection)
    {
      _logger.LogWarning("No Chromecast device connected");
      return;
    }

    try
    {
      _logger.LogInformation("Disconnecting from Chromecast: {Name}", disconnectedDevice?.FriendlyName);

      UnsubscribeFromReceiverStatus(client);

      if (client != null)
      {
        await client.DisconnectAsync().ConfigureAwait(false);
      }

      Name = "Google Cast Output";

      Disconnected?.Invoke(this, new ChromecastDisconnectedEventArgs
      {
        Device = disconnectedDevice,
        Reason = "User requested disconnect"
      });

      State = AudioOutputState.Ready;
      _logger.LogInformation("Disconnected from Chromecast");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error disconnecting from Chromecast");
      throw;
    }
  }

  /// <inheritdoc />
  public override async Task StartAsync(CancellationToken cancellationToken = default)
  {
    ValidateCanStart();

    if (_connectedReceiver == null)
    {
      _logger.LogInformation("No Chromecast device connected yet — output ready, waiting for device connection");
      State = AudioOutputState.Ready;
      return;
    }

    try
    {
      var startTimer = System.Diagnostics.Stopwatch.StartNew();
      _logger.LogInformation("Starting Google Cast output (mode: {Mode})", _options.StreamingMode);

      if (_client != null)
      {
        // Launch the receiver application (default CC1AD845 or custom receiver)
        var launchStatus = await _client.LaunchApplicationAsync(_options.ApplicationId);
        _logger.LogInformation("Cast: Receiver launched on {Device} ({LaunchMs}ms)",
          ConnectedDevice?.FriendlyName, startTimer.ElapsedMilliseconds);

        // Allow receiver to start initializing before sending commands
        await Task.Delay(250, cancellationToken);

        // Branch on streaming mode
        if (string.Equals(_options.StreamingMode, "DirectChannel", StringComparison.OrdinalIgnoreCase))
        {
          await StartDirectChannelAsync(launchStatus, cancellationToken);
        }
        else
        {
          await StartHttpMp3Async(cancellationToken);
        }
      }

      IsEnabledInternal = true;
      State = AudioOutputState.Streaming;

      _logger.LogInformation("Google Cast output started streaming to {Name} (mode: {Mode}, setupMs: {SetupMs})",
        ConnectedDevice?.FriendlyName, _options.StreamingMode, startTimer.ElapsedMilliseconds);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to start Google Cast output");
      State = AudioOutputState.Error;
      throw;
    }
  }

  /// <summary>
  /// Starts the DirectChannel streaming mode: sends Base64-encoded WAV chunks
  /// directly over a custom Cast message bus, bypassing HTTP entirely.
  /// </summary>
  private async Task StartDirectChannelAsync(
    Sharpcaster.Models.ChromecastStatus.ChromecastStatus? launchStatus,
    CancellationToken cancellationToken)
  {
    if (_audioEngine == null)
    {
      _logger.LogWarning("Cast: DirectChannel mode requires audio engine — call SetAudioEngine() first. Falling back to HttpMp3.");
      await StartHttpMp3Async(cancellationToken);
      return;
    }

    // Extract the transport ID from the launched application status.
    // This is the destination address for all custom messages to the receiver.
    var transportId = launchStatus?.Application?.TransportId;
    if (string.IsNullOrEmpty(transportId))
    {
      _logger.LogWarning("Cast: Could not get transport ID from launch status — falling back to HttpMp3");
      await StartHttpMp3Async(cancellationToken);
      return;
    }

    _logger.LogInformation(
      "Cast: Starting DirectChannel streaming — transport: {TransportId}, namespace: {Namespace}, chunk: {ChunkMs}ms",
      transportId, _options.DirectChannelNamespace, _options.DirectChannelChunkSizeMs);

    // Create the custom audio channel and wire it to the client.
    _directChannel = new DirectCastAudioChannel(_options.DirectChannelNamespace, _logger);
    _directChannel.Client = _client!;

    // Register the channel with SharpCaster's internal channel list so incoming
    // messages on our namespace get routed to OnMessageReceived. SharpCaster v3.0.0
    // has no public RegisterChannel API, so we inject via reflection.
    RegisterCustomChannel(_client!, _directChannel);

    // Create the streaming service and start sending audio
    _directStreaming = new DirectCastStreamingService(
      _logger, _audioEngine, _directChannel, _options, _metricsCollector);
    _directStreaming.SetTransportId(transportId);
    _directStreaming.Start();

    // Sync volume to Cast device
    await SyncVolumeAfterStartAsync();
  }

  /// <summary>
  /// Starts the standard HttpMp3 streaming mode: the Cast device fetches audio
  /// from an HTTP MP3 stream endpoint.
  /// </summary>
  private async Task StartHttpMp3Async(CancellationToken cancellationToken)
  {
    // Subscribe to status changes to monitor device transitions
    var mediaChannel = _client!.GetChannel<MediaChannel>();
    if (mediaChannel != null)
    {
      mediaChannel.StatusChanged += (_, status) =>
      {
        _logger.LogInformation(
          "Cast: StatusChanged event — PlayerState: {State}, IdleReason: {IdleReason}, MediaSessionId: {SessionId}",
          status?.PlayerState, status?.IdleReason, status?.MediaSessionId);
      };
    }

    // Load media if we have a stream URL
    if (!string.IsNullOrEmpty(_streamUrl))
    {
      await LoadMediaOnCastAsync(mediaChannel, cancellationToken);
      await SyncVolumeAfterStartAsync();
    }
    else
    {
      _logger.LogWarning("Cast: No stream URL set — Chromecast will not receive audio");
    }
  }

  /// <summary>
  /// Re-applies this connection's volume (<c>_connectionVolume</c>, AUD-80) after the
  /// receiver application launches. Leaves the device alone when that level is unknown.
  /// </summary>
  /// <remarks>
  /// This used to push the output's own <c>Volume</c> — which nothing sets after
  /// construction, so it was always <c>GoogleCast.DefaultVolume</c> — on every start, and
  /// without baselining the echo filter. Measured on the box 2026-09-29: every connect was
  /// followed by <c>Synced volume from Cast device: 70 % (initial: false)</c>.
  /// <c>internal</c> so a test can drive it: the StartAsync chain that calls it needs a
  /// launched receiver application, which no offline test can produce.
  /// </remarks>
  internal async Task SyncVolumeAfterStartAsync()
  {
    var target = Volatile.Read(ref _connectionVolume);
    if (float.IsNaN(target))
    {
      _logger.LogInformation(
        "Cast: no remembered or reported volume for {Name} — leaving the device's own volume unchanged",
        ConnectedDevice?.FriendlyName);
      return;
    }

    try
    {
      if (await PushVolumeToDeviceAsync(_client!, target).ConfigureAwait(false))
      {
        _logger.LogInformation("Cast: Volume synced to {Volume:P0}", target);
      }
    }
    catch (Exception volEx)
    {
      _logger.LogWarning(volEx, "Cast: Failed to sync volume after start");
    }
  }

  /// <summary>
  /// Registers a custom channel with SharpCaster's internal channel list via reflection.
  /// SharpCaster v3.0.0 has no public API for this, but the Channels property is a
  /// List&lt;IChromecastChannel&gt; that we can append to.
  /// </summary>
  private void RegisterCustomChannel(ChromecastClient client, DirectCastAudioChannel channel)
  {
    try
    {
      // SharpCaster stores channels as IEnumerable<IChromecastChannel> backed by an array.
      // We need to replace it with a new array that includes our custom channel.
      var prop = client.GetType().GetProperty("Channels",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
      if (prop == null)
      {
        _logger.LogWarning("Cast: Could not find Channels property on ChromecastClient");
        return;
      }

      var existing = prop.GetValue(client) as System.Collections.IEnumerable;
      if (existing == null)
      {
        _logger.LogWarning("Cast: Channels property is null");
        return;
      }

      // Build a new list from existing channels + our custom one
      var newList = new List<object>();
      foreach (var ch in existing)
      {
        newList.Add(ch);
      }
      newList.Add(channel);

      // Convert to array of the interface type
      var interfaceType = prop.PropertyType.GetGenericArguments().FirstOrDefault();
      if (interfaceType != null)
      {
        var arr = Array.CreateInstance(interfaceType, newList.Count);
        for (int i = 0; i < newList.Count; i++)
        {
          arr.SetValue(newList[i], i);
        }
        prop.SetValue(client, arr);
      }
      else
      {
        // Fallback: set as List
        prop.SetValue(client, newList);
      }

      _logger.LogInformation("Cast: Registered custom channel for namespace {Ns} (total channels: {Count})",
        channel.Namespace, newList.Count);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Cast: Failed to register custom channel via reflection");
    }
  }

  /// <inheritdoc />
  public override async Task StopAsync(CancellationToken cancellationToken = default)
  {
    if (!ValidateCanStop())
    {
      return;
    }

    try
    {
      State = AudioOutputState.Stopping;
      _logger.LogInformation("Stopping Google Cast output");

      // Stop DirectChannel streaming if active
      if (_directStreaming != null)
      {
        await _directStreaming.StopAsync();
        await _directStreaming.DisposeAsync();
        _directStreaming = null;
        _directChannel = null;
        _logger.LogInformation("DirectChannel streaming stopped");
      }

      // Stop media playback on the Chromecast. Snapshot the client under the
      // lock so a concurrent connect swapping _client cannot make us send the
      // media stop down a half-built connection.
      ChromecastClient? stopClient;
      await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
      try
      {
        stopClient = _client;
      }
      finally
      {
        _lifecycleLock.Release();
      }

      if (stopClient != null)
      {
        var mediaChannel = stopClient.GetChannel<MediaChannel>();
        if (mediaChannel != null)
        {
          try
          {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await mediaChannel.StopAsync().WaitAsync(cts.Token);
          }
          catch (TimeoutException)
          {
            _logger.LogWarning("Timed out stopping Cast media — device may be unreachable");
          }
          catch (OperationCanceledException)
          {
            _logger.LogWarning("Cast media stop cancelled — device may be unreachable");
          }
          catch (System.Reflection.TargetInvocationException tie)
            when (tie.InnerException?.Message.Contains("INVALID_MEDIA_SESSION_ID") == true)
          {
            _logger.LogDebug("Cast media session already ended, ignoring stop error");
          }
          catch (InvalidOperationException ioe)
            when (ioe.Message.Contains("INVALID_MEDIA_SESSION_ID"))
          {
            _logger.LogDebug("Cast media session already ended, ignoring stop error");
          }
          catch (ArgumentNullException)
          {
            _logger.LogDebug("Cast: No active media session to stop (MediaSessionId is null)");
          }
          catch (Exception ex)
          {
            // SharpCaster's MediaChannel.StopAsync → SendAsync can throw once the
            // session is already tearing down (socket closed / channel disposed by
            // the concurrent disconnect). The disconnect itself still succeeds, so
            // log-and-continue rather than letting it escape to the outer catch —
            // which logged a spurious ERROR *and* skipped the clean teardown below
            // (leaving IsEnabledInternal stuck true).
            _logger.LogWarning(ex, "Cast media stop failed during teardown; continuing");
          }
        }
      }

      IsEnabledInternal = false;
      State = AudioOutputState.Stopped;

      _logger.LogInformation("Google Cast output stopped");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to stop Google Cast output");
      State = AudioOutputState.Stopped;
    }
  }

  /// <summary>
  /// Sets the stream URL that will be used when streaming to Chromecast.
  /// The Chromecast will connect to this URL to receive the audio stream.
  /// </summary>
  /// <param name="streamUrl">The HTTP stream URL.</param>
  public void SetStreamUrl(string streamUrl)
  {
    _streamUrl = streamUrl;
    _logger.LogDebug("Stream URL set to: {Url}", streamUrl);
  }

  /// <summary>
  /// Gets the active DirectChannel streaming service, if any.
  /// Used for diagnostics and latency measurement.
  /// </summary>
  public DirectCastStreamingService? DirectStreaming => _directStreaming;

  /// <summary>
  /// Sets the audio engine reference for DirectChannel streaming mode.
  /// When set, the DirectCastStreamingService can create a stream reader
  /// to read PCM audio directly from the engine's output tap.
  /// Only required when <see cref="GoogleCastOutputOptions.StreamingMode"/> is "DirectChannel".
  /// </summary>
  /// <param name="audioEngine">The audio engine instance.</param>
  public void SetAudioEngine(IAudioEngine audioEngine)
  {
    _audioEngine = audioEngine;
    _logger.LogInformation("Cast: Audio engine set for DirectChannel streaming");
  }

  /// <summary>
  /// Sets now-playing metadata that will be sent to the Cast device.
  /// The metadata is included when loading media and can be updated mid-stream
  /// via <see cref="UpdateNowPlayingMetadataAsync"/>.
  /// </summary>
  public void SetNowPlayingMetadata(string? title, string? artist, string? album, string? albumArtUrl)
  {
    _nowPlayingMetadata = new CastNowPlayingMetadata(title, artist, album, albumArtUrl);
    _logger.LogDebug("Cast now-playing metadata set: {Title} - {Artist} [{Album}]", title, artist, album);
  }

  /// <summary>
  /// Tests Cast playback with an arbitrary URL to diagnose audio issues.
  /// Re-launches the receiver app and loads the URL fresh.
  /// </summary>
  public async Task<object> TestPlayUrlAsync(string url, string contentType)
  {
    if (_client == null)
    {
      return new { success = false, error = "Not connected to a Cast device" };
    }

    try
    {
      // Re-launch the media receiver app to get a clean session
      _logger.LogInformation("Cast test: Launching media receiver app");
      await _client.LaunchApplicationAsync(_options.ApplicationId);
      await Task.Delay(500);

      var media = new Media
      {
        ContentId = url,
        ContentUrl = url,
        ContentType = contentType,
        StreamType = StreamType.Buffered
      };

      var payload = JsonSerializer.Serialize(media);
      _logger.LogInformation("Cast test: Payload = {Payload}", payload);

      var mediaChannel = _client.GetChannel<MediaChannel>();
      if (mediaChannel == null)
      {
        return new { success = false, error = "MediaChannel not available" };
      }

      var loadStatus = await mediaChannel.LoadAsync(media, true);
      _logger.LogInformation(
        "Cast test: Load response — PlayerState: {State}, IdleReason: {Reason}, MediaSessionId: {Id}",
        loadStatus?.PlayerState, loadStatus?.IdleReason, loadStatus?.MediaSessionId);

      // Wait for playback to start
      await Task.Delay(3000);
      var finalStatus = await mediaChannel.GetMediaStatusAsync().WaitAsync(TimeSpan.FromSeconds(5));
      _logger.LogInformation(
        "Cast test: Final status (3s) — PlayerState: {State}, IdleReason: {Reason}",
        finalStatus?.PlayerState, finalStatus?.IdleReason);

      return new
      {
        success = finalStatus?.PlayerState is PlayerStateType.Playing or PlayerStateType.Buffering,
        loadState = loadStatus?.PlayerState.ToString(),
        finalState = finalStatus?.PlayerState.ToString(),
        finalIdleReason = finalStatus?.IdleReason,
        url,
        contentType,
        payload
      };
    }
    catch (Exception ex)
    {
      return new { success = false, error = ex.Message, url };
    }
  }

  /// <summary>
  /// Updates now-playing metadata on the Cast device by reloading media.
  /// Uses debouncing to coalesce rapid metadata changes (e.g., source switch
  /// → "No Track" → actual track → fingerprint identification) into a single
  /// Cast media reload. Without debouncing, each change triggers a full stream
  /// reconnection, causing garbled audio during the first few seconds.
  /// </summary>
  public async Task UpdateNowPlayingMetadataAsync(
    string? title, string? artist, string? album, string? albumArtUrl,
    CancellationToken cancellationToken = default)
  {
    _nowPlayingMetadata = new CastNowPlayingMetadata(title, artist, album, albumArtUrl);

    if (State != AudioOutputState.Streaming || _client == null || string.IsNullOrEmpty(_streamUrl))
    {
      _logger.LogDebug("Cast not streaming, metadata stored for next load");
      return;
    }

    // Debounce: cancel any pending metadata reload and schedule a new one.
    // This ensures rapid changes coalesce into a single Cast media reload.
    lock (_debounceLock)
    {
      _metadataDebouncesCts?.Cancel();
      _metadataDebouncesCts?.Dispose();
      _metadataDebouncesCts = new CancellationTokenSource();
    }

    var debounceCts = _metadataDebouncesCts;
    var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
      debounceCts?.Token ?? CancellationToken.None, cancellationToken);

    _logger.LogDebug("Cast metadata update debounced: {Title} - {Artist}", title, artist);

    // Fire-and-forget the delayed reload so we don't block the caller
    _ = Task.Run(async () =>
    {
      try
      {
        // Wait for metadata to stabilize — if another update comes
        // within this window, this task gets cancelled via debounceCts.
        // 3s covers the typical gap between source switch ("No Track")
        // and actual track metadata being available (~2s).
        await Task.Delay(3000, linkedCts.Token);

        await LoadMediaWithRecoveryAsync(cancellationToken);
        _logger.LogInformation(
          "Cast metadata updated: {Title} - {Artist}", title, artist);
      }
      catch (OperationCanceledException)
      {
        // Debounced away — a newer update superseded this one
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Failed to update Cast now-playing metadata");
      }
      finally
      {
        linkedCts.Dispose();
      }
    }, CancellationToken.None);
  }

  /// <summary>
  /// Attempts to load media on the Cast device. If the session has expired
  /// (receiver app closed after idle), relaunches the app and retries once.
  /// </summary>
  private async Task LoadMediaWithRecoveryAsync(CancellationToken cancellationToken)
  {
    var media = BuildMedia();
    var mediaChannel = _client!.GetChannel<MediaChannel>();
    if (mediaChannel == null)
    {
      return;
    }

    try
    {
      await mediaChannel.LoadAsync(media, true).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
    }
    catch (Exception ex) when (IsCastSessionExpired(ex))
    {
      _logger.LogInformation("Cast session expired — relaunching media receiver");
      await RelaunchMediaReceiverAsync(cancellationToken);

      // Retry load after relaunch
      mediaChannel = _client.GetChannel<MediaChannel>();
      if (mediaChannel != null)
      {
        try
        {
          await mediaChannel.LoadAsync(media, true).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
          _logger.LogInformation("Cast: Media loaded successfully after session recovery");
        }
        catch (TimeoutException)
        {
          _logger.LogDebug("Cast: Recovered LoadAsync timed out — device may still be processing");
        }
      }
    }
    catch (TimeoutException)
    {
      _logger.LogDebug("Cast: Metadata LoadAsync timed out — device may still be processing");
    }
  }

  /// <summary>
  /// Determines whether an exception indicates the Cast session has expired.
  /// </summary>
  private static bool IsCastSessionExpired(Exception ex)
  {
    var message = ex is System.Reflection.TargetInvocationException tie
      ? tie.InnerException?.Message ?? ex.Message
      : ex.Message;

    return message.Contains("INVALID_MEDIA_SESSION_ID", StringComparison.OrdinalIgnoreCase)
        || message.Contains("No running applications", StringComparison.OrdinalIgnoreCase)
        || message.Contains("session not found", StringComparison.OrdinalIgnoreCase);
  }

  /// <summary>
  /// Relaunches the media receiver after an idle timeout.
  /// </summary>
  private async Task RelaunchMediaReceiverAsync(CancellationToken cancellationToken)
  {
    try
    {
      await _client!.LaunchApplicationAsync(_options.ApplicationId);
      await Task.Delay(500, cancellationToken);
      _logger.LogInformation("Cast: Media receiver relaunched after idle recovery");
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Cast: Failed to relaunch media receiver");
    }
  }

  /// <summary>
  /// Standard Google Cast protocol port.
  /// </summary>
  private const int StandardCastPort = 8009;

  /// <summary>
  /// Finds a reachable TCP port on a cached Cast device.
  /// Tries the cached port first, then falls back to the standard Cast port (8009).
  /// </summary>
  private async Task<int> FindReachablePortAsync(ChromecastDeviceInfo device, CancellationToken ct)
  {
    // Always try the standard Cast protocol port (8009) first.
    // Port 443 on Google Home devices is HTTPS management, not Cast protocol —
    // it accepts TCP but times out on Cast protocol messages.
    var portsToTry = device.Port == StandardCastPort
      ? new[] { StandardCastPort }
      : new[] { StandardCastPort, device.Port };

    foreach (var port in portsToTry)
    {
      _logger.LogDebug("Verifying Cast device reachability at {IP}:{Port}", device.IpAddress, port);
      using var tcpCheck = new TcpClient();
      try
      {
        using var tcpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        tcpCts.CancelAfter(TimeSpan.FromSeconds(3));
        await tcpCheck.ConnectAsync(device.IpAddress, port, tcpCts.Token);
        if (port != device.Port)
        {
          _logger.LogInformation(
            "Cast device '{Name}': using standard port {Port} instead of discovered port {DiscoveredPort}",
            device.FriendlyName, port, device.Port);
        }
        return port;
      }
      catch
      {
        _logger.LogDebug("TCP check failed on {IP}:{Port}", device.IpAddress, port);
      }
    }

    throw new InvalidOperationException(
      $"Cast device '{device.FriendlyName}' at {device.IpAddress} is not reachable (tried ports {string.Join(", ", portsToTry)})");
  }

  /// <summary>
  /// Loads media on the Cast device with retry logic. The first load after
  /// LaunchApplicationAsync often fails silently (receiver not yet ready).
  /// If the first load results in FINISHED/Idle quickly, we retry once.
  /// </summary>
  private async Task LoadMediaOnCastAsync(MediaChannel? mediaChannel, CancellationToken cancellationToken)
  {
    if (mediaChannel == null)
    {
      _logger.LogWarning("Cast: MediaChannel is null — cannot load media");
      return;
    }

    _logger.LogInformation(
      "Cast: Loading media URL {StreamUrl} (type: audio/mpeg, stream: Live, metadata: {HasMetadata})",
      _streamUrl, _nowPlayingMetadata != null);

    var media = BuildMedia();

    // First attempt
    MediaStatus? status = null;
    try
    {
      status = await mediaChannel.LoadAsync(media, true).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
      _logger.LogInformation(
        "Cast: Media load response — PlayerState: {State}, IdleReason: {IdleReason}, MediaSessionId: {SessionId}",
        status?.PlayerState, status?.IdleReason, status?.MediaSessionId);
    }
    catch (TimeoutException)
    {
      _logger.LogInformation("Cast: Media load timed out (5s) — continuing in background");
    }

    // If the load immediately resulted in Idle/FINISHED or didn't start playing,
    // wait and retry — receiver may not have been fully initialized
    if (status?.PlayerState is PlayerStateType.Idle ||
        status?.IdleReason is "FINISHED" or "ERROR" or "CANCELLED")
    {
      _logger.LogInformation("Cast: First load resulted in {State}/{Reason} — retrying after 1s delay",
        status?.PlayerState, status?.IdleReason);
      await Task.Delay(1000, cancellationToken);

      try
      {
        media = BuildMedia(); // Rebuild in case metadata changed
        status = await mediaChannel.LoadAsync(media, true).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        _logger.LogInformation(
          "Cast: Retry load response — PlayerState: {State}, IdleReason: {IdleReason}, MediaSessionId: {SessionId}",
          status?.PlayerState, status?.IdleReason, status?.MediaSessionId);
      }
      catch (TimeoutException)
      {
        _logger.LogInformation("Cast: Retry load timed out — Cast device may still be processing");
      }
    }
  }

  /// <summary>
  /// Builds a Media object with the current stream URL and now-playing metadata.
  /// </summary>
  private Media BuildMedia()
  {
    var media = new Media
    {
      ContentId = _streamUrl,
      ContentUrl = _streamUrl,
      ContentType = "audio/mpeg",
      StreamType = StreamType.Live
    };

    if (_nowPlayingMetadata != null)
    {
      var metadata = new MediaMetadata
      {
        MetadataType = MetadataType.Music,
        Title = _nowPlayingMetadata.Title ?? "",
        SubTitle = _nowPlayingMetadata.Artist ?? ""
      };

      // Add album art if we have a valid absolute URL
      if (!string.IsNullOrEmpty(_nowPlayingMetadata.AlbumArtUrl) &&
          _nowPlayingMetadata.AlbumArtUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
      {
        metadata.Images = new[]
        {
          new Image { Url = _nowPlayingMetadata.AlbumArtUrl }
        };
      }

      media.Metadata = metadata;
    }

    return media;
  }

  /// <summary>
  /// Monitors a background LoadAsync task and runs connectivity diagnostics on failure.
  /// </summary>
  private async Task MonitorBackgroundLoadAsync(Task<MediaStatus?> loadTask)
  {
    try
    {
      var status = await loadTask;
      _logger.LogInformation(
        "Cast: Background load completed — PlayerState: {State}, IdleReason: {IdleReason}",
        status?.PlayerState, status?.IdleReason);
    }
    catch (TimeoutException)
    {
      _logger.LogWarning("Cast: LoadAsync timed out (30s) — Cast device never reached PLAYING state");
      await DiagnoseStreamConnectivityAsync();
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Cast: Background load failed");
      await DiagnoseStreamConnectivityAsync();
    }
  }

  /// <summary>
  /// Self-tests the stream URL from this machine to diagnose Cast connectivity issues.
  /// If the URL is reachable locally but Cast can't play it, the issue is likely a firewall.
  /// </summary>
  private async Task DiagnoseStreamConnectivityAsync()
  {
    if (string.IsNullOrEmpty(_streamUrl))
    {
      return;
    }

    try
    {
      using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
      var response = await httpClient.GetAsync(_streamUrl, HttpCompletionOption.ResponseHeadersRead);
      _logger.LogWarning(
        "Cast: Stream URL {Url} IS reachable from this machine (HTTP {Status}) but the Cast device cannot reach it. " +
        "This is almost certainly a FIREWALL issue. Fix with: " +
        "netsh advfirewall firewall add rule name=\"Radio Console Stream\" dir=in action=allow protocol=TCP localport=8080",
        _streamUrl, (int)response.StatusCode);
    }
    catch (Exception ex)
    {
      _logger.LogError(
        "Cast: Stream URL {Url} is NOT reachable even from this machine: {Error}. Is the HTTP stream server running?",
        _streamUrl, ex.Message);
    }
  }

  /// <summary>
  /// Subscribes to ReceiverChannel status events for bidirectional volume sync.
  /// </summary>
  /// <param name="client">
  /// The client to subscribe on, passed explicitly rather than read from
  /// <c>_client</c> so the caller's snapshot is used — a concurrent connect or
  /// teardown may already have swapped the field.
  /// </param>
  private void SubscribeToReceiverStatus(ChromecastClient? client)
  {
    if (client == null)
    {
      return;
    }

    var receiverChannel = client.GetChannel<ReceiverChannel>();
    if (receiverChannel != null)
    {
      receiverChannel.ReceiverStatusChanged += OnReceiverStatusChanged;
      _logger.LogDebug("Subscribed to Cast receiver status changes for volume sync");
    }
  }

  /// <summary>
  /// Unsubscribes from ReceiverChannel status events.
  /// </summary>
  /// <param name="client">The client to unsubscribe from (caller's snapshot).</param>
  private void UnsubscribeFromReceiverStatus(ChromecastClient? client)
  {
    if (client == null)
    {
      return;
    }

    var receiverChannel = client.GetChannel<ReceiverChannel>();
    if (receiverChannel != null)
    {
      receiverChannel.ReceiverStatusChanged -= OnReceiverStatusChanged;
    }
  }

  /// <summary>
  /// Reads the initial device volume after connecting, restores the volume remembered for
  /// this device if there is one (AUD-80), and syncs our local state.
  /// </summary>
  /// <param name="client">
  /// The client to read from, passed explicitly rather than read from <c>_client</c>
  /// so the caller's snapshot is used — the same reason
  /// <see cref="SubscribeToReceiverStatus"/> takes one: a concurrent connect or
  /// teardown may already have swapped the field.
  /// </param>
  /// <param name="generation">
  /// The connection generation the caller claimed. Re-checked after the network read
  /// and before anything is published; the comment on that check states exactly what
  /// it does and does not guarantee.
  /// </param>
  /// <param name="device">The device connected to; its <c>Id</c> keys the volume memory.</param>
  private async Task SyncInitialVolumeAsync(ChromecastClient client, int generation, ChromecastDeviceInfo device)
  {
    try
    {
      (float Volume, bool Muted)? reading = null;
      try
      {
        reading = await ReadInitialCastVolumeAsync(client).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        _logger.LogDebug(ex, "Could not read initial Cast device volume — will sync on first status update");
      }

      if (reading != null)
      {
        // Primed BEFORE the currency check, and therefore primed even for a reading
        // that is about to be discarded. That is deliberate. These two fields are the
        // echo filter's baseline, not connection state: _lastSetVolume starts at the
        // -1f sentinel, and OnReceiverStatusChanged reports any status event arriving
        // while it is still -1f as an EXTERNAL change. Skipping the priming here would
        // convert a suppressed initial sync into a spurious user-authored one — the
        // same write to master volume, through the other door.
        _lastSetVolume = reading.Value.Volume;
        _lastSetMute = reading.Value.Muted;

        _logger.LogInformation(
          "Cast device initial volume: {Volume:P0}, Muted: {Muted}",
          reading.Value.Volume, reading.Value.Muted);
      }

      var remembered = await GetRememberedVolumeAsync(device.Id).ConfigureAwait(false);

      // AUD-5. The read above is a network round-trip, and four sites bump the
      // generation while it is in flight: InitializeAsync, a newer connect's claim,
      // DisconnectAsync and DisposeAsync. Publishing this reading for a connection
      // that has since been superseded is what the defect was — the subscriber wrote
      // AudioManager.MasterVolume, whose setter schedules a persist.
      //
      // What this check does and does not do, stated precisely because this file
      // has shipped comments that claimed more than the code enforced:
      //   IT DOES remove the network round-trip from the window. A supersede landing
      //     any time between the claim and the status response is caught here.
      //   IT DOES NOT make the window empty. The lock is released before anything
      //     below runs — subscriber code must never run under _lifecycleLock — so a
      //     bump landing after the release can still see this connection write
      //     _connectionVolume, push its remembered level to its own device, and fire.
      //   IT IS NOT what stops an initial read moving master volume. That is
      //     AudioStateUpdateService.OnCastVolumeChanged, which ignores IsInitialSync
      //     events outright and has no timing dependence at all. This check is the
      //     producer honouring its own contract for whatever subscribes next.
      if (!await IsCurrentGenerationAsync(generation).ConfigureAwait(false))
      {
        _logger.LogInformation(
          "Cast initial volume read belongs to a superseded connection (generation {Generation}) — not published",
          generation);
        return;
      }

      // AUD-80. Which level this connection holds, in order of preference:
      //   1. the level remembered for THIS device — pushed to it if it differs;
      //   2. for a device never seen, the level it reported — adopted and remembered;
      //   3. neither (the read failed, nothing remembered) — unknown; the device is left
      //      alone and its first status update is handled as before.
      // Deliberately NOT the console's master volume for a never-seen device, and NOT
      // GoogleCast.DefaultVolume: master volume is the local speakers' level (casting
      // never reads it), and pushing a configured default is exactly what made every
      // reconnect land at 70 %.
      float? effectiveVolume = reading?.Volume;
      if (remembered is float target)
      {
        Volatile.Write(ref _connectionVolume, target);
        if (reading == null || Math.Abs(reading.Value.Volume - target) > 0.01f)
        {
          try
          {
            if (await PushVolumeToDeviceAsync(client, target).ConfigureAwait(false))
            {
              effectiveVolume = target;
              _logger.LogInformation(
                "Cast: restored remembered volume {Volume:P0} on {Name} (device reported {Reported:P0})",
                target, device.FriendlyName, reading?.Volume);
            }
          }
          catch (Exception ex)
          {
            // _connectionVolume keeps the target, so the push after the receiver
            // launches (SyncVolumeAfterStartAsync) tries again.
            _logger.LogWarning(ex, "Cast: could not restore remembered volume on {Name}", device.FriendlyName);
          }
        }
      }
      else if (reading != null)
      {
        Volatile.Write(ref _connectionVolume, reading.Value.Volume);
        _volumeStore?.Remember(device.Id, reading.Value.Volume);
      }
      else
      {
        Volatile.Write(ref _connectionVolume, float.NaN);
      }

      if (reading == null || effectiveVolume == null)
      {
        // Nothing was observed on the device, so there is nothing to report — the same
        // contract as before AUD-80.
        return;
      }

      // Fire event so subscribers can observe the device's volume after this sync
      CastVolumeChanged?.Invoke(this, new CastVolumeChangedEventArgs
      {
        Volume = effectiveVolume.Value,
        IsMuted = reading.Value.Muted,
        IsInitialSync = true
      });
    }
    catch (Exception ex)
    {
      // Also where an ObjectDisposedException from IsCurrentGenerationAsync lands when
      // disposal races the read. Swallowing it is intended: a disposed output must not
      // publish, and an exception escaping into ConnectAsync would put the output in
      // Error for a sync that is not needed to stream.
      _logger.LogDebug(ex, "Initial Cast volume sync did not complete");
    }
  }

  /// <summary>
  /// The volume remembered for <paramref name="deviceId"/>, or null when there is no
  /// store, nothing is remembered, or the store cannot be read.
  /// </summary>
  private async Task<float?> GetRememberedVolumeAsync(string deviceId)
  {
    if (_volumeStore == null)
    {
      return null;
    }

    try
    {
      return await _volumeStore.GetVolumeAsync(deviceId).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Could not read the remembered volume for Cast device {DeviceId}", deviceId);
      return null;
    }
  }

  /// <summary>
  /// Sends <paramref name="volume"/> to the device as a SET_VOLUME. Returns false when the
  /// client exposes no receiver channel (nothing sent). Throws what SharpCaster throws.
  /// </summary>
  private async Task<bool> PushVolumeToDeviceAsync(ChromecastClient client, float volume)
  {
    // Baseline the echo filter to the level being set BEFORE sending it. The device
    // confirms a SET_VOLUME with a status carrying the new level, and
    // OnReceiverStatusChanged must see that as our own change (within 0.01 of
    // _lastSetVolume), not an external one. Until AUD-80 the after-start push skipped
    // this, so every connect's confirmation of DefaultVolume (70 %) arrived as an
    // external change and was written — and persisted — as master volume.
    var previous = _lastSetVolume;
    _lastSetVolume = volume;
    try
    {
      if (CastSetVolumeOverrideForTests != null)
      {
        await CastSetVolumeOverrideForTests(volume).ConfigureAwait(false);
        return true;
      }

      var receiverChannel = client.GetChannel<ReceiverChannel>();
      if (receiverChannel == null)
      {
        _lastSetVolume = previous;
        return false;
      }

      await receiverChannel.SetVolume(volume).ConfigureAwait(false);
      return true;
    }
    catch
    {
      _lastSetVolume = previous;
      throw;
    }
  }

  /// <summary>
  /// Performs the Cast status read behind <see cref="SyncInitialVolumeAsync"/>, or
  /// the test substitute for it. Returns null when the client exposes no receiver
  /// channel or the status carries no volume.
  /// </summary>
  private async Task<(float Volume, bool Muted)?> ReadInitialCastVolumeAsync(ChromecastClient client)
  {
    if (CastStatusReadOverrideForTests != null)
    {
      return await CastStatusReadOverrideForTests().ConfigureAwait(false);
    }

    var receiverChannel = client.GetChannel<ReceiverChannel>();
    if (receiverChannel == null)
    {
      return null;
    }

    var status = await receiverChannel.GetChromecastStatusAsync().ConfigureAwait(false);
    if (status?.Volume?.Level == null)
    {
      return null;
    }

    return ((float)status.Volume.Level.Value, status.Volume.Muted ?? false);
  }

  /// <summary>
  /// True when <paramref name="generation"/> is still the current connection
  /// generation. Await-free inside the lock, like every other critical section here.
  /// </summary>
  /// <remarks>
  /// Deliberately takes no <see cref="CancellationToken"/>. Callers must sit inside a
  /// catch that tolerates <see cref="ObjectDisposedException"/>: <c>DisposeAsync</c>
  /// disposes <c>_lifecycleLock</c>, and a disposed output must not publish anything
  /// anyway, so the throw is the right outcome rather than a case to handle.
  /// </remarks>
  private async Task<bool> IsCurrentGenerationAsync(int generation)
  {
    await _lifecycleLock.WaitAsync().ConfigureAwait(false);
    try
    {
      return _connectionGeneration == generation;
    }
    finally
    {
      _lifecycleLock.Release();
    }
  }

  /// <summary>
  /// Handles ReceiverStatusChanged events from SharpCaster.
  /// Detects external volume/mute changes and fires <see cref="CastVolumeChanged"/>.
  /// </summary>
  private void OnReceiverStatusChanged(object? sender, ChromecastStatus status)
  {
    if (status.Volume == null)
    {
      return;
    }

    var deviceVolume = (float)(status.Volume.Level ?? 0);
    var deviceMuted = status.Volume.Muted ?? false;

    // Filter out echo events from our own SetVolume/SetMute calls
    if (_suppressNextVolumeEvent)
    {
      _suppressNextVolumeEvent = false;
      _lastSetVolume = deviceVolume;
      _lastSetMute = deviceMuted;
      return;
    }

    // Check if volume actually changed from what we last set
    var volumeChanged = Math.Abs(deviceVolume - _lastSetVolume) > 0.01f;
    var muteChanged = deviceMuted != _lastSetMute;

    if (!volumeChanged && !muteChanged)
    {
      return;
    }

    _lastSetVolume = deviceVolume;
    _lastSetMute = deviceMuted;

    if (volumeChanged)
    {
      // AUD-80: a level set on the speaker (buttons, Google Home) is the level to come
      // back to. Keyed by ConnectedDevice, which a handler left attached to a torn-down
      // client can misattribute (C-126 in the AUD-5 plan) — bounded, as that note says.
      Volatile.Write(ref _connectionVolume, deviceVolume);
      RememberVolume(deviceVolume);
    }

    _logger.LogInformation(
      "Cast device volume changed externally: {Volume:P0}, Muted: {Muted}",
      deviceVolume, deviceMuted);

    CastVolumeChanged?.Invoke(this, new CastVolumeChangedEventArgs
    {
      Volume = deviceVolume,
      IsMuted = deviceMuted,
      IsInitialSync = false
    });
  }

  /// <summary>Records <paramref name="volume"/> for the connected device, if any (AUD-80).</summary>
  private void RememberVolume(float volume)
  {
    var deviceId = ConnectedDevice?.Id;
    if (deviceId != null)
    {
      _volumeStore?.Remember(deviceId, volume);
    }
  }

  private async Task SetCastVolumeAsync(float volume)
  {
    if (_client == null || _connectedReceiver == null || State != AudioOutputState.Streaming)
    {
      return;
    }

    try
    {
      var receiverChannel = _client.GetChannel<ReceiverChannel>();
      if (receiverChannel != null)
      {
        _suppressNextVolumeEvent = true;
        _lastSetVolume = volume;
        await receiverChannel.SetVolume(volume);
        Volatile.Write(ref _connectionVolume, volume);
        RememberVolume(volume);
        _logger.LogDebug("Chromecast volume set to {Volume:P0}", volume);
      }
    }
    catch (Exception ex)
    {
      _suppressNextVolumeEvent = false;
      _logger.LogWarning(ex, "Failed to set Chromecast volume");
    }
  }

  private async Task SetCastMuteAsync(bool mute)
  {
    if (_client == null || _connectedReceiver == null || State != AudioOutputState.Streaming)
    {
      return;
    }

    try
    {
      var receiverChannel = _client.GetChannel<ReceiverChannel>();
      if (receiverChannel != null)
      {
        _suppressNextVolumeEvent = true;
        _lastSetMute = mute;
        await receiverChannel.SetMute(mute);
        _logger.LogDebug("Chromecast mute set to {Mute}", mute);
      }
    }
    catch (Exception ex)
    {
      _suppressNextVolumeEvent = false;
      _logger.LogWarning(ex, "Failed to set Chromecast mute state");
    }
  }

  /// <inheritdoc />
  public override async ValueTask DisposeAsync()
  {
    if (IsDisposed)
    {
      return;
    }

    if (_connectedReceiver != null)
    {
      try
      {
        await DisconnectAsync();
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Error during disconnect in dispose");
      }
    }

    if (_directStreaming != null)
    {
      await _directStreaming.DisposeAsync();
      _directStreaming = null;
      _directChannel = null;
    }

    lock (_debounceLock)
    {
      _metadataDebouncesCts?.Cancel();
      _metadataDebouncesCts?.Dispose();
      _metadataDebouncesCts = null;
    }

    // Final generation bump so a connect still in flight discards itself rather
    // than publishing onto a disposed output, and take the client as a snapshot.
    ChromecastClient? client;
    await _lifecycleLock.WaitAsync().ConfigureAwait(false);
    try
    {
      _connectionGeneration++;
      client = _client;
      // Deliberately NOT nulled: several Start/stream helpers dereference
      // _client with `!` on the assumption it outlives the output, and nulling
      // it here would turn disposal-during-startup into an NRE instead of the
      // ObjectDisposedException those paths already expect.
    }
    finally
    {
      _lifecycleLock.Release();
    }

    if (client != null)
    {
      await client.DisconnectAsync().ConfigureAwait(false);
    }

    _lifecycleLock.Dispose();
    DisposeBase();
  }
}

/// <summary>
/// Information about a discovered Chromecast device.
/// </summary>
public record ChromecastDeviceInfo
{
  /// <summary>
  /// Gets or sets the unique identifier for the device.
  /// </summary>
  public required string Id { get; init; }

  /// <summary>
  /// Gets or sets the friendly name of the device.
  /// </summary>
  public required string FriendlyName { get; init; }

  /// <summary>
  /// Gets or sets the IP address of the device.
  /// </summary>
  public required string IpAddress { get; init; }

  /// <summary>
  /// Gets or sets the port number.
  /// </summary>
  public required int Port { get; init; }

  /// <summary>
  /// Gets or sets the device model.
  /// </summary>
  public required string Model { get; init; }
}

/// <summary>
/// Event arguments for Chromecast device discovery.
/// </summary>
public class ChromecastDeviceDiscoveredEventArgs : EventArgs
{
  /// <summary>
  /// Gets the discovered device.
  /// </summary>
  public required ChromecastDeviceInfo Device { get; init; }
}

/// <summary>
/// Event arguments for Chromecast connection.
/// </summary>
public class ChromecastConnectedEventArgs : EventArgs
{
  /// <summary>
  /// Gets the connected device.
  /// </summary>
  public required ChromecastDeviceInfo Device { get; init; }
}

/// <summary>
/// Event arguments for Chromecast disconnection.
/// </summary>
public class ChromecastDisconnectedEventArgs : EventArgs
{
  /// <summary>
  /// Gets the disconnected device.
  /// </summary>
  public ChromecastDeviceInfo? Device { get; init; }

  /// <summary>
  /// Gets the reason for disconnection.
  /// </summary>
  public string? Reason { get; init; }
}

/// <summary>
/// Event arguments for Cast device volume changes (external).
/// </summary>
public class CastVolumeChangedEventArgs : EventArgs
{
  /// <summary>
  /// Gets the new volume level (0.0 to 1.0).
  /// </summary>
  public required float Volume { get; init; }

  /// <summary>
  /// Gets whether the device is muted.
  /// </summary>
  public required bool IsMuted { get; init; }

  /// <summary>
  /// Gets whether this is the initial sync after connecting (not an external change).
  /// </summary>
  public bool IsInitialSync { get; init; }
}

/// <summary>
/// Now-playing metadata to display on Cast devices (Google Home app).
/// </summary>
/// <param name="Title">Track title.</param>
/// <param name="Artist">Artist name.</param>
/// <param name="Album">Album name.</param>
/// <param name="AlbumArtUrl">Absolute URL to album art image.</param>
public record CastNowPlayingMetadata(string? Title, string? Artist, string? Album, string? AlbumArtUrl);

/// <summary>
/// A cached Cast device entry with a last-seen timestamp.
/// </summary>
public class CachedCastDevice
{
  /// <summary>
  /// Gets or sets the device info.
  /// </summary>
  public required ChromecastDeviceInfo Device { get; set; }

  /// <summary>
  /// Gets or sets when the device was last seen on the network.
  /// </summary>
  public DateTime LastSeen { get; set; }
}

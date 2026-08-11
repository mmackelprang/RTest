using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Outputs;
using Radio.Infrastructure.Audio.Services;
using Radio.Infrastructure.Audio.SoundFlow;
using Radio.Configuration.Models;
using IAppConfigurationManager = Radio.Configuration.Abstractions.IConfigurationManager;

namespace Radio.API.Services;

/// <summary>
/// Background service that initializes and starts the audio engine on application startup.
/// Also handles graceful shutdown of the audio engine and automatic source/output selection.
/// </summary>
public class AudioEngineInitializationService : IHostedService
{
  private readonly ILogger<AudioEngineInitializationService> _logger;
  private readonly IAudioEngine _audioEngine;
  private readonly IAudioDeviceManager _deviceManager;
  private readonly IAudioManager? _audioManager;
  private readonly IOptionsMonitor<AudioPreferences> _audioPreferences;
  private readonly IMasterMixer _masterMixer;
  private readonly IAppConfigurationManager? _configManager;
  private readonly IOptions<BluetoothOptions> _bluetoothOptions;
  private readonly IOptions<AudioOutputOptions> _audioOutputOptions;
  private readonly IBluetoothService? _bluetoothService;
  private readonly BluetoothAutoSwitchService? _bluetoothAutoSwitch;
  private readonly GoogleCastOutput? _castOutput;
  private readonly HttpStreamOutput? _httpOutput;
  private readonly IServiceProvider _serviceProvider;

  // Cancelled by StopAsync so the Cast confirm-or-roll-back background work
  // stops instead of racing engine tear-down (it would otherwise still be
  // sitting in its watchdog delay when the host shuts down).
  private readonly CancellationTokenSource _serviceStoppingCts = new();

  // Serialises the local-output rollback so the explicit bail-out paths and the
  // watchdog can never both drive SetActiveOutputAsync.
  private readonly SemaphoreSlim _fallbackLock = new(1, 1);
  private bool _localFallbackApplied;

  // Cancelled once the output question is settled in local's favour. This both
  // retires the watchdog and abandons any still-running Cast connect attempt —
  // without the latter, a connect that completes after the rollback would start
  // streaming to Cast while the local sink is unmuted, recreating the very
  // dual-output bug the startup mute exists to prevent.
  private readonly CancellationTokenSource _castResolvedCts = new();

  /// <summary>
  /// The background "confirm Cast is really streaming, else fall back to local"
  /// task, retained so shutdown can drain it and tests can await the outcome
  /// deterministically instead of racing a fire-and-forget task.
  /// Null when the persisted output was not google-cast.
  /// </summary>
  internal Task? CastAutoConnectTask { get; private set; }

  /// <summary>
  /// How long to let Cast discovery settle before looking in the device cache.
  /// Test seam — production always uses the 3 s default.
  /// </summary>
  internal TimeSpan CastDiscoverySettleDelay { get; set; } = TimeSpan.FromSeconds(3);

  /// <summary>
  /// Overrides <see cref="GoogleCastOutputOptions.StartupConnectTimeoutSeconds"/>.
  /// Test seam so watchdog tests don't wait the production timeout.
  /// </summary>
  internal TimeSpan? CastConnectTimeoutOverride { get; set; }

  /// <summary>
  /// Initializes a new instance of the AudioEngineInitializationService.
  /// </summary>
  public AudioEngineInitializationService(
    ILogger<AudioEngineInitializationService> logger,
    IAudioEngine audioEngine,
    IAudioDeviceManager deviceManager,
    IOptionsMonitor<AudioPreferences> audioPreferences,
    IMasterMixer masterMixer,
    IOptions<BluetoothOptions> bluetoothOptions,
    IOptions<AudioOutputOptions> audioOutputOptions,
    IServiceProvider serviceProvider)
  {
    _logger = logger;
    _audioEngine = audioEngine;
    _deviceManager = deviceManager;
    _audioPreferences = audioPreferences;
    _masterMixer = masterMixer;
    _bluetoothOptions = bluetoothOptions;
    _audioOutputOptions = audioOutputOptions;
    _serviceProvider = serviceProvider;

    // Try to get IAudioManager (optional)
    _audioManager = serviceProvider.GetService<IAudioManager>();
    _configManager = serviceProvider.GetService<IAppConfigurationManager>();
    _bluetoothService = serviceProvider.GetService<IBluetoothService>();
    _bluetoothAutoSwitch = serviceProvider.GetService<BluetoothAutoSwitchService>();
    _castOutput = serviceProvider.GetService<GoogleCastOutput>();
    _httpOutput = serviceProvider.GetService<HttpStreamOutput>();
  }

  /// <summary>
  /// Starts the service and initializes the audio engine.
  /// </summary>
  public async Task StartAsync(CancellationToken cancellationToken)
  {
    try
    {
      // Clean up orphaned play history entries from unclean shutdown.
      // Runs before audio engine init to prevent fingerprinting from creating duplicates.
      await CloseOrphanedPlayHistoryEntriesAsync(cancellationToken);

      _logger.LogInformation("Initializing audio engine...");

      // Wire the virtual outputs + config manager into the engine so
      // SetActiveOutputAsync can activate/deactivate them and persist the
      // choice. The engine treats these as optional dependencies; the
      // controller-side activation paths still go through the same gate.
      if (_audioEngine is SoundFlowAudioEngine sfEngine)
      {
        sfEngine.AttachOutputCoordination(_castOutput, _httpOutput, _configManager);
      }

      // Initialize the audio engine
      await _audioEngine.InitializeAsync(cancellationToken);
      
      // Start the audio engine
      await _audioEngine.StartAsync(cancellationToken);
      
      _logger.LogInformation("Audio engine initialized and started successfully");

      // Load persisted device display settings from config store (hidden/visible/friendly names).
      // IOptionsMonitor only loads from appsettings.json; user changes are saved to SQLite.
      if (_deviceManager is SoundFlowDeviceManager sfDeviceManager)
      {
        await sfDeviceManager.LoadDisplaySettingsFromStoreAsync(cancellationToken);
      }

      // Enumerate devices
      var outputDevices = await _deviceManager.GetOutputDevicesAsync(cancellationToken);
      var inputDevices = await _deviceManager.GetInputDevicesAsync(cancellationToken);
      
      _logger.LogInformation("Found {OutputCount} output devices and {InputCount} input devices",
        outputDevices.Count, inputDevices.Count);
      
      // Log device details
      foreach (var device in outputDevices)
      {
        _logger.LogInformation("Output device: {DeviceName} (ID: {DeviceId}, Default: {IsDefault})",
          device.Name, device.Id, device.IsDefault);
      }
      
      foreach (var device in inputDevices)
      {
        _logger.LogInformation("Input device: {DeviceName} (ID: {DeviceId})",
          device.Name, device.Id);
      }
      
      // Apply startup audio preferences (output device, source)
      await ApplyStartupPreferencesAsync(outputDevices, cancellationToken);

      // Initialize AudioManager (restores volume/mute/balance from config store)
      if (_audioManager != null)
      {
        await _audioManager.InitializeAsync(cancellationToken);
      }

      // Activate persisted audio source (after volume is restored so audio starts at correct level)
      await ActivatePersistedSourceAsync(cancellationToken);

      // Pre-warm Bluetooth source if configured (creates source without switching to it)
      if (_bluetoothAutoSwitch != null)
      {
        await _bluetoothAutoSwitch.PreWarmBluetoothAsync(cancellationToken);
      }

      // Enable Bluetooth discoverability on startup if configured
      await EnableBluetoothOnStartupAsync(cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to initialize audio engine");
      // Don't throw - allow the application to start even if audio fails
    }
  }

  /// <summary>
  /// Applies user preferences for audio source and output on startup.
  /// If no preferences exist, defaults to Radio source and default output device.
  /// </summary>
  private async Task ApplyStartupPreferencesAsync(
    IReadOnlyList<AudioDeviceInfo> outputDevices,
    CancellationToken cancellationToken)
  {
    try
    {
      var prefs = _audioPreferences.CurrentValue;

      // Try reading persisted output device from the config store first,
      // since IOptionsMonitor reads from appsettings.json which doesn't
      // get updated when the user changes the output device at runtime.
      string? persistedOutput = null;
      if (_configManager != null)
      {
        try
        {
          var storeId = _configManager.CurrentStoreType == ConfigurationStoreType.Sqlite ? "sqlite" : "config";
          persistedOutput = await _configManager.GetValueAsync<string>(storeId, "AudioPreferences:CurrentOutput", ct: cancellationToken);
          if (!string.IsNullOrEmpty(persistedOutput))
          {
            _logger.LogInformation("Found persisted output device preference: {DeviceId}", persistedOutput);
          }
        }
        catch (Exception ex)
        {
          _logger.LogDebug(ex, "Could not read persisted output device preference from config store");
        }
      }

      // Prefer persisted value from config store, fall back to IOptionsMonitor
      var preferredOutputId = !string.IsNullOrEmpty(persistedOutput) ? persistedOutput : prefs.CurrentOutput;

      // Handle virtual outputs (google-cast, http-stream)
      if (preferredOutputId == "google-cast" || preferredOutputId == "http-stream")
      {
        _logger.LogInformation("Restoring {Output} output from startup preferences", preferredOutputId);

        // Single gate call: atomically activates the virtual output, stops the
        // other virtual output, sets local-output mute, and persists the choice.
        // This replaces the previous startup path which only activated outputs
        // and forgot to mute the local sink — that omission caused dual-output
        // (Cast + soundbar) after service restart with persisted "google-cast".
        await _audioEngine.SetActiveOutputAsync(preferredOutputId, cancellationToken);

        // Cast still needs background auto-connect to the saved default device.
        // The gate handles activation; auto-connect remains here since it
        // depends on the persisted default-Cast-device lookup.
        //
        // IMPORTANT: the gate above has just muted the local sink. "google-cast"
        // is only a *desired* output at this point — nothing has connected yet.
        // StartCastAutoConnect owns confirming it, and rolling back to the local
        // output if it cannot be confirmed, so the local sink is never left
        // muted for a Cast device that never arrives.
        if (preferredOutputId == "google-cast")
        {
          CastAutoConnectTask = StartCastAutoConnect(cancellationToken);
        }
      }
      else
      {
        // Physical output device
        string? outputToUse = null;
        string? preferredDeviceName = null;
        if (!string.IsNullOrEmpty(preferredOutputId))
        {
          var preferredOutput = outputDevices.FirstOrDefault(d => d.Id == preferredOutputId);
          if (preferredOutput != null)
          {
            outputToUse = preferredOutput.Id;
            preferredDeviceName = preferredOutput.Name;
            _logger.LogInformation("Using preferred output device: {DeviceName}", preferredOutput.Name);
          }
          else
          {
            _logger.LogWarning("Preferred output device {OutputId} not found, using default", preferredOutputId);
          }
        }

        if (outputToUse == null)
        {
          var defaultOutput = outputDevices.FirstOrDefault(d => d.IsDefault);
          if (defaultOutput != null)
          {
            outputToUse = defaultOutput.Id;
            preferredDeviceName = defaultOutput.Name;
            _logger.LogInformation("Using default output device: {DeviceName}", defaultOutput.Name);
          }
        }

        if (outputToUse != null)
        {
          try
          {
            await _deviceManager.SetOutputDeviceAsync(outputToUse, cancellationToken);
            _logger.LogInformation("Output device set successfully");
          }
          catch (Exception ex)
          {
            _logger.LogWarning(ex, "Failed to set output device");
          }

          // Verify the output device actually connected to the correct PipeWire node.
          // After PipeWire restarts, MiniAudio device indices may shift, causing the
          // wrong device to be selected even though the ID matches.
          try
          {
            var currentDevices = await _deviceManager.GetOutputDevicesAsync(cancellationToken);
            var selectedId = _deviceManager.GetSelectedOutputDeviceId();
            var activeDevice = selectedId != null
              ? currentDevices.FirstOrDefault(d => d.Id == selectedId)
              : null;
            if (activeDevice != null && preferredDeviceName != null &&
                !activeDevice.Name.Contains(preferredDeviceName, StringComparison.OrdinalIgnoreCase))
            {
              _logger.LogWarning(
                "Output device mismatch: expected \"{Expected}\" but connected to \"{Actual}\" — searching by name",
                preferredDeviceName, activeDevice.Name);

              var correctDevice = currentDevices.FirstOrDefault(d =>
                d.Name.Contains(preferredDeviceName, StringComparison.OrdinalIgnoreCase));
              if (correctDevice != null)
              {
                _logger.LogInformation("Found correct device \"{Name}\" at ID {Id} — switching",
                  correctDevice.Name, correctDevice.Id);
                await _deviceManager.SetOutputDeviceAsync(correctDevice.Id, cancellationToken);
                outputToUse = correctDevice.Id;
              }
              else
              {
                _logger.LogWarning("Could not find device matching \"{Name}\" — using current device",
                  preferredDeviceName);
              }
            }
          }
          catch (Exception ex)
          {
            _logger.LogWarning(ex, "Output device verification failed — continuing with current device");
          }

          // Notify the gate that local is the active output. This stops any
          // lingering virtual outputs, unmutes the local sink, and persists
          // AudioPreferences:CurrentOutput so the next restart picks the same device.
          try
          {
            await _audioEngine.SetActiveOutputAsync(outputToUse, cancellationToken);
          }
          catch (Exception ex)
          {
            _logger.LogWarning(ex, "Failed to set active output via gate for local device {DeviceId}", outputToUse);
          }
        }
      }

      _logger.LogInformation("Audio startup output configuration applied");
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to apply startup preferences");
    }
  }

  /// <summary>
  /// Activates the last-used audio source on startup. Falls back to Radio if no preference exists.
  /// Must be called AFTER AudioManager.InitializeAsync() so volume is restored before audio starts.
  /// </summary>
  private async Task ActivatePersistedSourceAsync(CancellationToken cancellationToken)
  {
    if (_audioManager == null)
    {
      _logger.LogDebug("AudioManager not available, skipping source activation on startup");
      return;
    }

    try
    {
      var prefs = _audioPreferences.CurrentValue;
      var sourceToActivate = !string.IsNullOrEmpty(prefs.CurrentSource)
        ? prefs.CurrentSource
        : "Radio";

      if (!Enum.TryParse<AudioSourceType>(sourceToActivate, true, out var sourceType))
      {
        _logger.LogWarning("Invalid persisted source type '{Source}', falling back to Radio", sourceToActivate);
        sourceType = AudioSourceType.Radio;
      }

      _logger.LogInformation("Activating persisted source: {SourceType} (from {Origin})",
        sourceType,
        !string.IsNullOrEmpty(prefs.CurrentSource) ? "preferences" : "default");

      await _audioManager.GetOrCreateSourceAsync(sourceType, switchToSource: true, cancellationToken);
      _logger.LogInformation("Source {SourceType} activated on startup", sourceType);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to activate persisted source, UI will handle default selection");
    }
  }

  /// <summary>
  /// Enables Bluetooth discoverability on startup if configured.
  /// On Windows, the adapter will start but A2DP sink (acting as a speaker)
  /// is not natively supported — phones can see the device but cannot stream audio.
  /// This works on the target Linux/Raspberry Pi platform where BlueZ supports A2DP sink.
  /// </summary>
  private async Task EnableBluetoothOnStartupAsync(CancellationToken cancellationToken)
  {
    try
    {
      var opts = _bluetoothOptions.Value;
      if (!opts.Enabled || !opts.EnableOnStartup)
      {
        _logger.LogDebug("Bluetooth auto-start disabled (Enabled={Enabled}, EnableOnStartup={EnableOnStartup})",
          opts.Enabled, opts.EnableOnStartup);
        return;
      }

      if (_bluetoothService == null)
      {
        _logger.LogDebug("Bluetooth service not available, skipping auto-start");
        return;
      }

      var deviceName = opts.DeviceName;
      _logger.LogInformation("Enabling Bluetooth on startup as '{DeviceName}'...", deviceName);
      var success = await _bluetoothService.StartAsync(deviceName, cancellationToken);

      if (success)
      {
        _logger.LogInformation("Bluetooth started successfully, device is discoverable as '{DeviceName}'", deviceName);
      }
      else
      {
        _logger.LogWarning("Bluetooth StartAsync returned false — adapter may not be available");
      }
    }
    catch (Exception ex)
    {
      // Bluetooth failure must not block application startup
      _logger.LogWarning(ex, "Failed to enable Bluetooth on startup — continuing without Bluetooth");
    }
  }

  /// <summary>
  /// Starts background auto-connect to the saved default Cast device and, either
  /// way, guarantees the local output does not stay muted for a Cast device that
  /// never becomes usable.
  ///
  /// The gate (<see cref="IAudioEngine.SetActiveOutputAsync"/>) has already muted
  /// the local sink by the time this runs — that mute is correct and deliberate
  /// (it is what keeps Cast and the soundbar from playing simultaneously), but it
  /// nominates an output rather than confirming one. Every path out of this method
  /// therefore either ends with Cast actually <c>Streaming</c> or rolls back to the
  /// local output. The rollback goes through the gate specifically so it also
  /// rewrites the persisted <c>AudioPreferences:CurrentOutput</c> — otherwise the
  /// same inconsistent preference replays the failure on every subsequent restart.
  /// </summary>
  /// <returns>The background confirm-or-roll-back task.</returns>
  private Task StartCastAutoConnect(CancellationToken cancellationToken)
  {
    var castOptions = _audioOutputOptions.Value.GoogleCast;
    var isDirectChannel = string.Equals(castOptions.StreamingMode, "DirectChannel", StringComparison.OrdinalIgnoreCase);

    // In DirectChannel mode, wire the audio engine so GoogleCastOutput can
    // create a stream reader for sending PCM data over the Cast message bus.
    if (isDirectChannel && _castOutput != null)
    {
      _castOutput.SetAudioEngine(_audioEngine);
      _logger.LogInformation("DirectChannel mode: audio engine wired to Cast output");
    }

    var timeout = CastConnectTimeoutOverride
      ?? TimeSpan.FromSeconds(Math.Max(1, castOptions.StartupConnectTimeoutSeconds));

    // Scheduled with CancellationToken.None and cancelled from inside instead, so
    // the retained task always runs to completion rather than surfacing as a bare
    // cancelled task to whoever awaits it.
    return Task.Run(
      () => RunCastAutoConnectAsync(isDirectChannel, castOptions.StreamingMode, timeout, cancellationToken),
      CancellationToken.None);
  }

  /// <summary>
  /// Attempts the Cast auto-connect, then independently watchdogs the result.
  /// The attempt and the watchdog run concurrently so the watchdog also covers
  /// bail-outs the attempt does not explicitly handle.
  /// </summary>
  private async Task RunCastAutoConnectAsync(
    bool isDirectChannel,
    string streamingMode,
    TimeSpan timeout,
    CancellationToken cancellationToken)
  {
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
      cancellationToken, _serviceStoppingCts.Token, _castResolvedCts.Token);
    var ct = linkedCts.Token;

    await Task.WhenAll(
      TryConnectCastAsync(isDirectChannel, streamingMode, ct),
      WatchdogCastStreamingAsync(timeout, ct)).ConfigureAwait(false);
  }

  /// <summary>
  /// The auto-connect attempt. Every early return falls back to local first —
  /// these are the paths that previously logged and left the local sink muted.
  /// </summary>
  private async Task TryConnectCastAsync(bool isDirectChannel, string streamingMode, CancellationToken ct)
  {
    try
    {
      var castDeviceId = await ResolveDefaultCastDeviceIdAsync(ct).ConfigureAwait(false);

      // Bail-out 1: no default Cast device configured, or no Cast output at all.
      if (string.IsNullOrEmpty(castDeviceId) || _castOutput == null)
      {
        _logger.LogWarning(
          "Persisted output is google-cast but there is nothing to connect to " +
          "(defaultCastDeviceConfigured={HasDevice}, castOutputAvailable={HasOutput})",
          !string.IsNullOrEmpty(castDeviceId), _castOutput != null);
        await FallBackToLocalOutputAsync("no default Cast device configured", ct).ConfigureAwait(false);
        return;
      }

      _logger.LogInformation("Auto-connecting to default Cast device on startup: {Id}", castDeviceId);

      // Capture the output-selection epoch BEFORE any connect work. The connect
      // runs outside the engine's output lock (it is far too long to hold it),
      // so this token is what proves at the end that nothing reselected the
      // output while we were on the network.
      var castEpoch = _audioEngine is SoundFlowAudioEngine epochEngine
        ? await epochEngine.BeginCastConnectAsync(ct).ConfigureAwait(false)
        : (int?)null;

      // Give Cast discovery a moment to populate the cache.
      await Task.Delay(CastDiscoverySettleDelay, ct).ConfigureAwait(false);

      var cached = await _castOutput.GetCachedDevicesAsync(ct).ConfigureAwait(false);
      var device = cached.FirstOrDefault(d => d.Id == castDeviceId);

      // Bail-out 2: the saved device isn't on the network.
      if (device == null)
      {
        _logger.LogWarning("Default Cast device {Id} not found in cache after startup, skipping auto-connect",
          castDeviceId);
        await FallBackToLocalOutputAsync("default Cast device not discovered", ct).ConfigureAwait(false);
        return;
      }

      if (_castOutput.State == AudioOutputState.Created)
      {
        await _castOutput.InitializeAsync(ct).ConfigureAwait(false);
      }

      await _castOutput.ConnectAsync(device, ct).ConfigureAwait(false);

      // Wire the HTTP audio stream (HttpMp3 mode only)
      if (!isDirectChannel && _httpOutput?.State == AudioOutputState.Streaming)
      {
        var streamUrl = GetRoutableStreamUrl(_httpOutput.Mp3StreamUrl, _httpOutput.Port, device.IpAddress);
        _castOutput.SetStreamUrl(streamUrl);
      }

      await _castOutput.StartAsync(ct).ConfigureAwait(false);

      // Late-success guard, now decided by the engine under its output lock.
      // Several calls in the connect chain above do not observe cancellation at
      // all (the SharpCaster-facing ConnectChromecast / LaunchApplicationAsync /
      // volume-sync calls), so the rollback can have fired and unmuted the local
      // sink while we were parked inside one of them. Finishing the connect at
      // that point would leave Cast streaming AND local unmuted — the dual-output
      // bug the startup mute exists to prevent.
      //
      // Deliberately NOT a check of _localFallbackApplied here: reading a flag
      // and then tearing down are two steps with a window between them, and the
      // output can move inside that window. TryCommitCastConnectAsync does the
      // check and the teardown under one acquisition of the engine's lock.
      if (castEpoch.HasValue && _audioEngine is SoundFlowAudioEngine commitEngine &&
          !await commitEngine.TryCommitCastConnectAsync(castEpoch.Value, CancellationToken.None).ConfigureAwait(false))
      {
        return;
      }

      _logger.LogInformation("Startup: Auto-connected to Cast device: {Name} (mode: {Mode})",
        device.FriendlyName, streamingMode);

      // Deliberately no "success" short-circuit beyond that: GoogleCastOutput
      // .StartAsync returns having set state to Ready (not Streaming) when no
      // receiver is actually attached. The watchdog distinguishes the two.
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
      // Host is shutting down — StopAsync owns tear-down. Do not rewrite the
      // user's persisted output preference on the way out.
    }
    catch (Exception ex)
    {
      // Bail-out 3: anything else thrown mid-connect.
      _logger.LogWarning(ex, "Failed to auto-connect to Cast device on startup");
      await FallBackToLocalOutputAsync("Cast auto-connect failed", ct).ConfigureAwait(false);
    }
  }

  /// <summary>
  /// The durable guard: if Cast has not actually reached
  /// <see cref="AudioOutputState.Streaming"/> within the timeout, roll back to the
  /// local output. This covers the explicit bail-outs above plus any future path
  /// that leaves Cast nominated-but-not-working.
  /// </summary>
  private async Task WatchdogCastStreamingAsync(TimeSpan timeout, CancellationToken ct)
  {
    try
    {
      await Task.Delay(timeout, ct).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
      return;
    }

    // Already rolled back by one of the explicit bail-outs.
    if (Volatile.Read(ref _localFallbackApplied))
    {
      return;
    }

    // Cast is genuinely working — the startup mute is correct, leave it alone.
    if (_castOutput?.State == AudioOutputState.Streaming)
    {
      _logger.LogInformation("Cast output confirmed streaming — local output stays muted");
      return;
    }

    _logger.LogWarning(
      "Cast output did not reach Streaming within {Timeout}s (state={State}) — rolling back to local output",
      timeout.TotalSeconds, _castOutput?.State.ToString() ?? "<no cast output>");

    await FallBackToLocalOutputAsync("Cast not streaming before timeout", ct).ConfigureAwait(false);
  }

  /// <summary>
  /// Resolves the default Cast device id, preferring the SQLite config store.
  ///
  /// <c>DevicesController</c> writes <c>AudioPreferences:DefaultCastDeviceId</c>
  /// to the config store when the user sets a default Cast device, but
  /// IOptionsMonitor only ever reflects appsettings.json — the same asymmetry
  /// already called out for CurrentOutput in <see cref="ApplyStartupPreferencesAsync"/>.
  /// Reading only IOptionsMonitor made a perfectly valid saved device invisible at
  /// startup, which widened the window where Cast was nominated but unreachable.
  /// </summary>
  private async Task<string?> ResolveDefaultCastDeviceIdAsync(CancellationToken cancellationToken)
  {
    if (_configManager != null)
    {
      try
      {
        var storeId = _configManager.CurrentStoreType == ConfigurationStoreType.Sqlite ? "sqlite" : "config";
        var persisted = await _configManager.GetValueAsync<string>(
          storeId, "AudioPreferences:DefaultCastDeviceId", ct: cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(persisted))
        {
          _logger.LogInformation("Found persisted default Cast device: {DeviceId}", persisted);
          return persisted;
        }
      }
      catch (Exception ex)
      {
        _logger.LogDebug(ex, "Could not read persisted default Cast device from config store");
      }
    }

    var fromAppSettings = _audioPreferences.CurrentValue.DefaultCastDeviceId;
    return string.IsNullOrEmpty(fromAppSettings) ? null : fromAppSettings;
  }

  /// <summary>
  /// Rolls the active output back to a local device, unmuting the local sink and
  /// persisting the corrected preference. Idempotent on success; a transient
  /// failure (e.g. no devices enumerated yet) leaves the door open for the
  /// watchdog to retry.
  /// </summary>
  private async Task FallBackToLocalOutputAsync(string reason, CancellationToken cancellationToken)
  {
    try
    {
      await _fallbackLock.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
      return;
    }
    catch (ObjectDisposedException)
    {
      // Raced shutdown disposal. SemaphoreSlim.WaitAsync checks disposal before
      // it checks the token, so a cancelled token does not pre-empt this.
      return;
    }

    bool resolved;
    try
    {
      resolved = await ApplyLocalFallbackAsync(reason, cancellationToken).ConfigureAwait(false);
    }
    finally
    {
      _fallbackLock.Release();
    }

    // Signalled outside the lock: cancelling runs continuations synchronously,
    // and doing that while holding the semaphore invites surprises.
    if (resolved)
    {
      // Retire the watchdog and abandon any in-flight Cast connect.
      try
      {
        await _castResolvedCts.CancelAsync().ConfigureAwait(false);
      }
      catch (ObjectDisposedException)
      {
        // Raced service shutdown; nothing left to signal.
      }
    }
  }

  /// <summary>
  /// The guarded body of the rollback. Runs under <c>_fallbackLock</c>.
  /// </summary>
  /// <returns>
  /// True when the output question is settled and no further rollback should be
  /// attempted; false when nothing was done and a later attempt may still help.
  /// </returns>
  private async Task<bool> ApplyLocalFallbackAsync(string reason, CancellationToken cancellationToken)
  {
    try
    {
      if (_localFallbackApplied)
      {
        return false;
      }

      // Don't stomp a newer choice: by watchdog time the user may have picked a
      // different output through the UI. A null ActiveOutputId means the engine
      // never reported one, so treat it as "still ours" and proceed — failing
      // open here is what keeps the local sink from staying muted.
      var active = _audioEngine.ActiveOutputId;
      if (!string.IsNullOrEmpty(active) &&
          !string.Equals(active, "google-cast", StringComparison.OrdinalIgnoreCase))
      {
        _logger.LogInformation(
          "Skipping local-output fallback ({Reason}): active output is now {ActiveOutput}", reason, active);
        Volatile.Write(ref _localFallbackApplied, true);
        return true;
      }

      // Re-enumerate rather than reusing the startup snapshot — the watchdog can
      // fire tens of seconds later, by which time the device list may differ.
      var devices = await _deviceManager.GetOutputDevicesAsync(cancellationToken).ConfigureAwait(false);
      var target = devices.FirstOrDefault(d => d.IsDefault) ?? devices.FirstOrDefault();
      if (target == null)
      {
        // Nothing to unmute: with no output device there is no local playback
        // device either. Left retryable on purpose.
        _logger.LogError(
          "Cast output unusable ({Reason}) and no local output device is available — " +
          "cannot restore local audio", reason);
        return false;
      }

      _logger.LogWarning(
        "Falling back to local output \"{DeviceName}\" ({DeviceId}) because Cast could not be confirmed ({Reason})",
        target.Name, target.Id, reason);

      try
      {
        await _deviceManager.SetOutputDeviceAsync(target.Id, cancellationToken).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        // Non-fatal: the gate call below still unmutes and persists.
        _logger.LogWarning(ex, "Fallback: could not select local output device {DeviceId}", target.Id);
      }

      // The gate tears down the half-open Cast/HTTP outputs, unmutes the local
      // sink, and persists AudioPreferences:CurrentOutput so the next restart
      // does not replay this failure.
      await _audioEngine.SetActiveOutputAsync(target.Id, cancellationToken).ConfigureAwait(false);
      Volatile.Write(ref _localFallbackApplied, true);

      _logger.LogInformation(
        "Local output \"{DeviceName}\" restored and unmuted after Cast fallback", target.Name);
      return true;
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      // Shutting down — leave state alone.
      return false;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to fall back to local output after Cast could not be confirmed");
      return false;
    }
  }

  /// <summary>
  /// Resolves the stream URL to use the local LAN IP (Cast devices need a routable address).
  /// </summary>
  private string GetRoutableStreamUrl(string streamUrl, int port, string? targetDeviceIp)
  {
    try
    {
      var localIp = GetLocalIPAddress(targetDeviceIp);
      if (localIp != null)
      {
        var uri = new Uri(streamUrl);
        return $"http://{localIp}:{port}{uri.PathAndQuery}";
      }
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Could not resolve routable stream URL");
    }
    return streamUrl;
  }

  /// <summary>
  /// Gets the local LAN IP address, preferring one on the same subnet as the target.
  /// </summary>
  private static string? GetLocalIPAddress(string? targetDeviceIp)
  {
    IPAddress? targetIp = null;
    if (!string.IsNullOrEmpty(targetDeviceIp))
    {
      IPAddress.TryParse(targetDeviceIp, out targetIp);
    }

    string? fallbackIp = null;
    foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
    {
      if (ni.OperationalStatus != OperationalStatus.Up)
      {
        continue;
      }

      if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
      {
        continue;
      }

      var desc = ni.Description.ToLowerInvariant();
      var name = ni.Name.ToLowerInvariant();
      if (desc.Contains("hyper-v") || desc.Contains("virtual") ||
          name.Contains("vethernet") || name.Contains("wsl") ||
          name.Contains("docker") || name.Contains("br-"))
      {
        continue;
      }

      foreach (var addr in ni.GetIPProperties().UnicastAddresses)
      {
        if (addr.Address.AddressFamily != AddressFamily.InterNetwork)
        {
          continue;
        }

        if (IPAddress.IsLoopback(addr.Address))
        {
          continue;
        }

        if (targetIp != null && addr.IPv4Mask != null)
        {
          var localBytes = addr.Address.GetAddressBytes();
          var maskBytes = addr.IPv4Mask.GetAddressBytes();
          var targetBytes = targetIp.GetAddressBytes();
          bool sameSubnet = true;
          for (int i = 0; i < 4; i++)
          {
            if ((localBytes[i] & maskBytes[i]) != (targetBytes[i] & maskBytes[i]))
            { sameSubnet = false; break; }
          }
          if (sameSubnet)
          {
            return addr.Address.ToString();
          }
        }

        fallbackIp ??= addr.Address.ToString();
      }
    }
    return fallbackIp;
  }

  /// <summary>
  /// Closes orphaned play history entries left by unclean shutdown.
  /// Must run before audio engine initialization so fingerprinting doesn't create duplicates.
  /// </summary>
  private async Task CloseOrphanedPlayHistoryEntriesAsync(CancellationToken cancellationToken)
  {
    try
    {
      using var scope = _serviceProvider.CreateScope();
      var repo = scope.ServiceProvider.GetService<IPlayHistoryRepository>();
      if (repo != null)
      {
        var closed = await repo.CloseOrphanedEntriesAsync(TimeSpan.FromMinutes(2), cancellationToken);
        if (closed > 0)
        {
          _logger.LogInformation("Cleaned up {Count} orphaned play history entries from previous shutdown", closed);
        }
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to clean up orphaned play history entries");
    }
  }

  /// <summary>
  /// Stops the service and gracefully shuts down the audio engine.
  /// </summary>
  public async Task StopAsync(CancellationToken cancellationToken)
  {
    try
    {
      _logger.LogInformation("Stopping audio engine...");

      // Stop the Cast confirm-or-roll-back work before tearing anything down, so
      // its watchdog can't fire a fallback (and a config write) mid-shutdown.
      // Bounded drain: this work is best-effort and must not delay shutdown.
      try
      {
        await _serviceStoppingCts.CancelAsync();
      }
      catch (ObjectDisposedException)
      {
        // StopAsync called twice — already torn down.
      }

      var castTask = CastAutoConnectTask;
      if (castTask != null)
      {
        await Task.WhenAny(castTask, Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None));
      }

      // Graceful Cast shutdown: stop media + CLOSE_APP + disconnect receiver
      // so the Chromecast returns to its default state instead of holding a
      // stale session that the next startup has to fight through. Single
      // source of truth: the same TearDownCastOutputAsync that the
      // SetActiveOutputAsync gate uses when transitioning away from Cast.
      // Best-effort; never blocks engine stop (5s internal cap + try/catch).
      if (_audioEngine is SoundFlowAudioEngine sfEngine)
      {
        await sfEngine.TearDownCastOutputAsync(cancellationToken);
      }

      if (_audioEngine.State == Radio.Core.Interfaces.Audio.AudioEngineState.Running)
      {
        await _audioEngine.StopAsync(cancellationToken);
      }

      _logger.LogInformation("Audio engine stopped successfully");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error stopping audio engine");
    }
    finally
    {
      // Dispose only once the background work has actually finished — it holds a
      // linked token source built from these, and the drain above is capped at 1s.
      // If it is still running the process is exiting anyway, so leaving them to
      // the process teardown is strictly safer than disposing underneath it.
      var pending = CastAutoConnectTask;
      if (pending == null || pending.IsCompleted)
      {
        _serviceStoppingCts.Dispose();
        _castResolvedCts.Dispose();
        _fallbackLock.Dispose();
      }
      else
      {
        _logger.LogDebug(
          "Cast auto-connect task still running at shutdown — deferring synchronisation-primitive disposal");
      }
    }
  }
}

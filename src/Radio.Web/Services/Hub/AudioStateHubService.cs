using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Radio.Configuration.Bridge;
using Radio.Web.Models;

namespace Radio.Web.Services.Hub;

/// <summary>
/// SignalR hub service for real-time audio state updates
/// Handles: PlaybackStateChanged, NowPlayingChanged, QueueChanged,
/// RadioStateChanged, VolumeChanged, SourceChanged, FingerprintStatusChanged,
/// PhoneCallStateChanged, EncoderConnectionChanged,
/// EncoderConfigStatusChanged, EncoderHudChanged, SleepStateChanged, EventPlaybackChanged,
/// ConfigChanged
/// </summary>
/// <remarks>
/// ⚠ EVERY event on this class is raised through <see cref="NotifyAsync(Func{Task})"/> or its
/// generic twin, and a new one must be too — see those methods' remarks for what the direct
/// <c>await SomeEvent.Invoke()</c> form does wrong (queue row `UI-7`). This is enforced, not
/// requested: <c>AsyncEventFanOutLintTests</c> in <c>Radio.Core.Tests</c> fails the TEST step if an
/// event declared in this file is raised by <c>X.Invoke(…)</c>, through a one-level local alias, or
/// by a bare <c>X(…)</c> call. ⚠ It is a regex over source text, not a proof: an event declared here
/// and raised from another part of a partial type would pass, and it does not scan <c>tests/</c>.
///
/// ⚠ This is a SINGLETON (Program.cs:411) and its component subscribers are PER CIRCUIT, so the
/// invocation list grows with open browsers. Ten production types subscribe; `UI-7` §0.2 has the
/// census. Anything reasoning about "the subscriber" of this class is reasoning about a class that
/// does not exist. (`UI-7`'s dossier says two browsers puts NINE handlers on NowPlayingChanged;
/// pre-merge review narrowed that — Sleep.razor uses EmptyLayout, so it and MainLayout cannot both
/// render on one circuit, and the reachable ceiling is nearer seven. The argument needs only ≥2.)
/// </remarks>
public class AudioStateHubService : IAsyncDisposable
{
  private readonly ILogger<AudioStateHubService> _logger;
  private readonly IConfiguration _configuration;
  // Web-process instance of the SQLite-config reload notifier. Calling
  // NotifyReload() forces this process's SqliteConfigurationProvider to re-read
  // the shared config DB — the cross-process half of the ConfigChanged bridge.
  // Optional so the test fixtures that new this service up directly keep
  // compiling; production always injects the registered singleton.
  // (This said "~9" until UI-7. The real count was ~20 within days of it being
  // written, which is why it now carries no number: the sentence never needed
  // one, and a number nobody re-counts is a comment that goes quietly false.)
  private readonly ConfigStoreChangeNotifier? _configStoreNotifier;
  private readonly IHubConnectionTransport? _transport;
  private HubConnection? _hubConnection;
  private bool _isDisposed;
  private readonly SemaphoreSlim _connectionLock = new(1, 1);
  // Background retry loop activated when the initial StartAsync fails (radio-api not yet
  // listening at deploy time, network blip, etc.). Mirrors the recovery pattern used by
  // GvBridgeHubService and AudioVisualizationHubService.
  private CancellationTokenSource? _retryCts;

  // Events that components can subscribe to.
  // NowPlayingChanged and VolumeChanged pass the typed payload from SignalR
  // so subscribers can use it directly instead of making a redundant HTTP call.
  public event Func<Task>? PlaybackStateChanged;
  public event Func<NowPlayingDto?, Task>? NowPlayingChanged;
  public event Func<Task>? QueueChanged;
  // RadioStateChanged carries the full RadioStateDto payload (including
  // NowPlayingMatchId) so subscribers don't need to re-fetch via REST.
  // The REST hop drops NowPlayingMatchId because RadioController has no
  // access to AudioStateUpdateService._currentMatchId — the broadcast is
  // the only path that carries it.
  public event Func<RadioStateDto, Task>? RadioStateChanged;
  public event Func<VolumeDto?, Task>? VolumeChanged;
  public event Func<Task>? SourceChanged;
  public event Func<Task>? FingerprintStatusChanged;
  public event Func<Task>? PhoneCallStateChanged;
  /// <summary>Raised when the encoder's presence changes, carrying which transition occurred.
  /// Typed because absent-at-boot and dropped-mid-session share <c>IsConnected=false</c> and call for
  /// different notifications (ENC-0).</summary>
  public event Func<EncoderConnectionDto, Task>? EncoderConnectionChanged;
  /// <summary>Raised when the encoder's configuration tier changes (ENC-12). Fires on change only, so
  /// this is a handful of events per connection rather than a stream.</summary>
  public event Func<EncoderConfigStatusDto, Task>? EncoderConfigStatusChanged;
  /// <summary>Raised when an encoder produced on-screen feedback, carrying which knob acted and what
  /// to show (ENC-4). The API coalesces value updates to >= 50 ms before broadcasting, so a knob
  /// being turned reaches this at up to 20 Hz rather than at the poll rate.</summary>
  public event Func<EncoderHudDto, Task>? EncoderHudChanged;
  public event Func<bool, Task>? SleepStateChanged;
  /// <summary>
  /// Raised when the one attended event playback changes state (ADR-029 D6 §8.1). Typed, like
  /// NowPlayingChanged and unlike PlaybackStateChanged: the payload IS the state, so a subscriber
  /// that re-fetched it over REST would be adding a round trip to a push that already carries
  /// everything. Fires on transitions only — there is no position tick (§8.2).
  /// </summary>
  public event Func<EventPlaybackSnapshotDto?, Task>? EventPlaybackChanged;
  // Fired after a cross-process ConfigChanged push has reloaded this process's
  // config snapshot.
  // ⚠ IT HAS ZERO SUBSCRIBERS, in src/ and in tests/ (UI-7 C-208). The previous
  // wording here — "Optional for subscribers that want an immediate re-render" —
  // described a subscriber that has never existed. The live effect of the
  // ConfigChanged push is _configStoreNotifier?.NotifyReload() in the handler
  // below; this event is not that, and the topbar / sleep clocks repaint on
  // their own 1 s timers regardless.
  // ⛔ RETAINED DELIBERATELY, not overlooked: it is public API, and a dead-code
  // deletion inside a defect-class PR muddies a diff whose value is that it is
  // mechanical. Deleting it is a separate decision; UI-7 §6.1 files it.
  // ⚠ An earlier revision of this comment also claimed "VisualizerPanelTests pins
  // this class's event set by name" as a third reason. Pre-merge review falsified
  // it: that test reflects GetEvents() but asserts only Contain("EncoderConfigStatusChanged")
  // and NotContain("VisualizationModeChanged"), so deleting ConfigChanged would NOT
  // fail it. The two reasons above carry the decision on their own.
  public event Func<Task>? ConfigChanged;

  // Throttle disconnect log messages to avoid spam when API is down
  private static DateTime _lastDisconnectLogUtc = DateTime.MinValue;
  private static readonly TimeSpan DisconnectLogInterval = TimeSpan.FromSeconds(10);

  // Throttle for AcceptPayload's rejection warning — see ShouldLogRejection for why it is throttled.
  // ⚠ INSTANCE, not static, and unlike _lastDisconnectLogUtc directly above. That is load-bearing for
  // test isolation: AudioStateHubServiceNullPayloadTests has two tests that each build a fresh hub and
  // reject a null RadioStateChanged (:46 and :65), and one of them asserts the warning was logged. A
  // static throttle would suppress whichever ran second and fail on execution order alone.
  // Keyed by event name so a flood of one event cannot mask the first rejection of another.
  private readonly Dictionary<string, DateTime> _lastNullPayloadLogUtc = [];
  // ⚠ Locked, where _lastDisconnectLogUtc is not, and the difference is not fussiness: a torn read of
  // a Dictionary during a concurrent write corrupts it, where a torn DateTime read only costs a stale
  // throttle decision.
  private readonly object _nullPayloadLogLock = new();
  private static readonly TimeSpan NullPayloadLogInterval = TimeSpan.FromSeconds(10);

  public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;
  public HubConnectionState ConnectionState => _hubConnection?.State ?? HubConnectionState.Disconnected;

  public AudioStateHubService(
    ILogger<AudioStateHubService> logger,
    IConfiguration configuration,
    ConfigStoreChangeNotifier? configStoreNotifier = null,
    IHubConnectionTransport? transport = null)
  {
    _logger = logger;
    _configuration = configuration;
    _configStoreNotifier = configStoreNotifier;
    _transport = transport;
  }

  public async Task StartAsync(CancellationToken cancellationToken = default)
  {
    await _connectionLock.WaitAsync(cancellationToken);
    try
    {
      // Already connected — nothing to do.
      if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
      {
        _logger.LogDebug("Hub connection already initialized and connected");
        return;
      }
      // Connection object exists but isn't connected yet (Connecting, Reconnecting, or
      // Disconnected with a background retry loop polling). Returning is safe — once the
      // hub establishes, the Reconnected handler or the retry-loop success path replays
      // the group subscriptions. Without this guard a failed initial StartAsync would
      // leave _hubConnection non-null and every subsequent call would skip silently —
      // the exact bug fixed in this change.
      if (_hubConnection != null)
      {
        _logger.LogDebug("Hub connection initialization already in progress (State={State})", _hubConnection.State);
        return;
      }

      var apiBaseUrl = _configuration["ApiBaseUrl"] ?? WebConstants.DefaultApiBaseUrl;
      var hubUrl = $"{apiBaseUrl}{WebConstants.HubPaths.Audio}";

      _logger.LogInformation("Initializing SignalR connection to {HubUrl}", hubUrl);

      _hubConnection = new HubConnectionBuilder()
        .WithUrl(hubUrl, options => _transport?.Configure(options))
        .WithAutomaticReconnect(new RetryPolicy())
        .ConfigureLogging(logging =>
        {
          logging.SetMinimumLevel(LogLevel.Information);
        })
        .Build();

      // Register event handlers
      // Server sends PlaybackStateChanged with a PlaybackStateDto payload —
      // accept and discard it so SignalR dispatches the message.
      _hubConnection.On<object>("PlaybackStateChanged", async (_) =>
      {
        _logger.LogDebug("Received PlaybackStateChanged event");
        await NotifyAsync(PlaybackStateChanged);
      });

      // Server sends NowPlayingChanged with a NowPlayingDto payload —
      // deserialize and pass through so subscribers can use it directly.
      // ⛔ A null here is DATA, not a contract violation (ADR-033). See OnNowPlayingMessageAsync.
      _hubConnection.On<NowPlayingDto?>("NowPlayingChanged", OnNowPlayingMessageAsync);

      // Server sends QueueChanged with a list payload —
      // accept and discard it so SignalR dispatches the message.
      _hubConnection.On<object>("QueueChanged", async (_) =>
      {
        _logger.LogDebug("Received QueueChanged event");
        await NotifyAsync(QueueChanged);
      });

      // Server sends RadioStateChanged with a RadioStateDto payload —
      // deserialize and pass through so subscribers can read NowPlayingMatchId
      // directly. (Previously the payload was discarded and subscribers
      // re-fetched via REST, which strips NowPlayingMatchId and silently
      // broke the recognition stream's NOW-row anchor.)
      //
      // ⚠ The type argument is RadioStateDto? — NULLABLE — and that is deliberate. The payload
      // arrives from a JSON deserializer across a process boundary, where C# nullable annotations
      // are erased. A JSON `null` binds to default(T) whatever this file annotates. Declaring it
      // non-nullable did not prevent a null; it only hid that one was representable (UI-12 §0.2).
      _hubConnection.On<RadioStateDto?>("RadioStateChanged", OnRadioStateMessageAsync);

      // Server sends VolumeChanged with a VolumeDto payload —
      // deserialize and pass through so subscribers can update directly.
      // ⛔ A null here is DATA, not a contract violation (ADR-033). See OnVolumeMessageAsync.
      _hubConnection.On<VolumeDto?>("VolumeChanged", OnVolumeMessageAsync);

      _hubConnection.On("SourceChanged", async () =>
      {
        _logger.LogDebug("Received SourceChanged event");
        await NotifyAsync(SourceChanged);
      });

      // Server sends FingerprintStatusChanged with a FingerprintStatusDto payload —
      // accept and discard it so SignalR dispatches the message.
      _hubConnection.On<object>("FingerprintStatusChanged", async (_) =>
      {
        _logger.LogDebug("Received FingerprintStatusChanged event");
        await NotifyAsync(FingerprintStatusChanged);
      });

      // Server sends PhoneCallStateChanged with state payload
      _hubConnection.On<object>("PhoneCallStateChanged", async (_) =>
      {
        _logger.LogDebug("Received PhoneCallStateChanged event");
        await NotifyAsync(PhoneCallStateChanged);
      });

      // Server sends EncoderConnectionChanged when encoder device connects/disconnects
      _hubConnection.On<EncoderConnectionDto?>("EncoderConnectionChanged", OnEncoderConnectionMessageAsync);

      // Server sends EncoderConfigStatusChanged when the configuration tier changes (ENC-12).
      _hubConnection.On<EncoderConfigStatusDto?>("EncoderConfigStatusChanged", OnEncoderConfigStatusMessageAsync);

      // Server sends EncoderHudChanged when a knob acts (ENC-4).
      _hubConnection.On<EncoderHudDto?>("EncoderHudChanged", OnEncoderHudMessageAsync);

      // Server sends SleepStateChanged with bool payload (true=sleeping, false=awake)
      _hubConnection.On<bool>("SleepStateChanged", async (isSleeping) =>
      {
        _logger.LogDebug("Received SleepStateChanged event: IsSleeping={IsSleeping}", isSleeping);
        await NotifyAsync(SleepStateChanged, isSleeping);
      });

      // Server sends EventPlaybackChanged on every attended-playback transition (ADR-029 D6 §8.1).
      // Transitions only — there is no position tick, and §8.2 refuses one outright.
      // ⛔ A null here is DATA, not a contract violation (ADR-033). See OnEventPlaybackMessageAsync.
      _hubConnection.On<EventPlaybackSnapshotDto?>("EventPlaybackChanged", OnEventPlaybackMessageAsync);

      // Server sends ConfigChanged (section name) when a config write lands in the
      // API process. radio-web is a SEPARATE process, so the in-process
      // ConfigStoreChangeNotifier never fired here — trigger it now so the
      // SQLite-backed IOptionsMonitor snapshots (e.g. DisplayOptions.TimeFormat)
      // re-read the shared store and the topbar / sleep clocks repaint on their
      // next 1 s tick. See ConfigurationController.BroadcastConfigChangedAsync.
      _hubConnection.On<string>("ConfigChanged", async (section) =>
      {
        _logger.LogDebug("Received ConfigChanged event for section {Section}", section);
        _configStoreNotifier?.NotifyReload();
        await NotifyAsync(ConfigChanged);
      });

      // Connection lifecycle events — throttled to avoid log spam when API is down
      _hubConnection.Closed += (error) =>
      {
        if (error != null && IsConnectionRefused(error))
        {
          // Throttle connection-refused spam — the ApiConnectionLoggingHandler logs these
          var now = DateTime.UtcNow;
          if (now - _lastDisconnectLogUtc >= DisconnectLogInterval)
          {
            _lastDisconnectLogUtc = now;
            _logger.LogWarning("Audio hub connection lost — API unavailable");
          }
        }
        else if (error != null)
        {
          _logger.LogWarning(error, "Audio hub connection closed with error");
        }
        else
        {
          _logger.LogInformation("Audio hub connection closed");
        }

        return Task.CompletedTask;
      };

      _hubConnection.Reconnecting += (error) =>
      {
        if (error == null || !IsConnectionRefused(error))
        {
          _logger.LogWarning(error, "Audio hub reconnecting...");
        }

        return Task.CompletedTask;
      };

      _hubConnection.Reconnected += async (connectionId) =>
      {
        _lastDisconnectLogUtc = DateTime.MinValue; // Reset throttle
        _logger.LogInformation("Audio hub reconnected. ConnectionId: {ConnectionId}", connectionId);

        // Re-subscribe to group-based channels after reconnect
        try
        {
          await _hubConnection.InvokeAsync("SubscribeToRadioState");
          await _hubConnection.InvokeAsync("SubscribeToQueue");
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "Failed to re-subscribe to groups after reconnect");
        }
      };

      // Start the connection. If radio-api hasn't bound its listener yet (typical
      // during a fresh deploy that starts api + web together), the negotiate POST
      // fails fast — handle that as a recoverable startup race rather than a hard
      // failure that locks the hub object into a dead state.
      try
      {
        await _hubConnection.StartAsync(cancellationToken);
        _logger.LogInformation("SignalR connection established successfully");
        await SubscribeToGroupsAsync(cancellationToken);
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Initial connection to AudioStateHub at {Url} failed — retrying in background", hubUrl);
        StartRetryLoop(hubUrl);
      }
    }
    finally
    {
      _connectionLock.Release();
    }
  }

  /// <summary>
  /// Subscribes to the group-based channels that require explicit opt-in
  /// (RadioState, Queue). Used both by the initial StartAsync path and by
  /// the background retry loop after it successfully connects.
  /// </summary>
  private async Task SubscribeToGroupsAsync(CancellationToken cancellationToken)
  {
    if (_hubConnection == null) return;
    try
    {
      await _hubConnection.InvokeAsync("SubscribeToRadioState", cancellationToken);
      await _hubConnection.InvokeAsync("SubscribeToQueue", cancellationToken);
      _logger.LogInformation("Subscribed to RadioState and Queue groups");
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to subscribe to SignalR groups");
    }
  }

  /// <summary>
  /// Polls the hub until <see cref="HubConnection.StartAsync(CancellationToken)"/> succeeds,
  /// then re-subscribes to the RadioState + Queue groups. Idempotent — cancels any prior
  /// loop before starting a new one. Mirrors <c>AudioVisualizationHubService.StartRetryLoop</c>.
  /// </summary>
  private void StartRetryLoop(string hubUrl)
  {
    _retryCts?.Cancel();
    _retryCts?.Dispose();
    _retryCts = new CancellationTokenSource();
    var ct = _retryCts.Token;
    _ = Task.Run(async () =>
    {
      var delays = new[] { 2, 5, 10, 30 };
      for (var attempt = 0; !ct.IsCancellationRequested; attempt++)
      {
        var delaySec = delays[Math.Min(attempt, delays.Length - 1)];
        try { await Task.Delay(TimeSpan.FromSeconds(delaySec), ct); }
        catch (OperationCanceledException) { return; }

        if (ct.IsCancellationRequested || _hubConnection == null) return;
        if (_hubConnection.State != HubConnectionState.Disconnected) return;

        try
        {
          await _hubConnection.StartAsync(ct);
          _logger.LogInformation("Connected to AudioStateHub at {Url} (retry #{Attempt})", hubUrl, attempt + 1);
          await SubscribeToGroupsAsync(ct);
          return;
        }
        catch (Exception ex)
        {
          _logger.LogDebug(ex, "Audio state hub retry #{Attempt} failed", attempt + 1);
        }
      }
    }, ct);
  }

  /// <summary>
  /// Locally triggers the SourceChanged event without going through SignalR.
  /// Call after a source switch API call succeeds to immediately notify
  /// NowPlayingPanel and other subscribers (bypasses the 500ms polling delay).
  /// </summary>
  public async Task NotifySourceChangedAsync()
  {
    await NotifyAsync(SourceChanged);
  }

  public async Task StopAsync(CancellationToken cancellationToken = default)
  {
    _retryCts?.Cancel();
    _retryCts?.Dispose();
    _retryCts = null;

    await _connectionLock.WaitAsync(cancellationToken);
    try
    {
      if (_hubConnection != null)
      {
        _logger.LogInformation("Stopping SignalR connection");
        await _hubConnection.StopAsync(cancellationToken);
        _logger.LogInformation("SignalR connection stopped");
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error stopping SignalR connection");
    }
    finally
    {
      _connectionLock.Release();
    }
  }

  public async ValueTask DisposeAsync()
  {
    if (_isDisposed)
    {
      return;
    }

    _isDisposed = true;
    _retryCts?.Cancel();
    _retryCts?.Dispose();
    _retryCts = null;

    if (_hubConnection != null)
    {
      await _hubConnection.DisposeAsync();
      _hubConnection = null;
    }

    _connectionLock.Dispose();
    GC.SuppressFinalize(this);
  }

  /// <summary>
  /// Rejects a null payload for an event whose delegate cannot represent one, logging the rejection.
  /// Returns true when the payload may be dispatched.
  /// </summary>
  /// <remarks>
  /// ⚠⚠ THIS BELONGS HERE AND NOT IN <see cref="NotifyAsync{T}"/>, AND THE DIFFERENCE IS A REAL
  /// DEFECT, NOT A STYLE PREFERENCE (queue row `UI-12` §0.5). Three events on this class are TYPED to
  /// accept null — NowPlayingChanged (:60), VolumeChanged (:68) and EventPlaybackChanged (:90) — and
  /// none of the three may be dropped. NotifyAsync&lt;T&gt;'s T is erased to a type parameter and
  /// cannot tell them apart from the four non-nullable ones this guard serves, so a null check there
  /// would silently drop a working broadcast: the exact defect `UI-12` was filed to avoid causing.
  ///
  /// ⚠ The three events declared Func&lt;T?, Task&gt; — NowPlayingChanged, VolumeChanged and
  /// EventPlaybackChanged — must NOT route through here; their nulls are data. ⚠ AND THE THREE ARE
  /// NOT EQUIVALENT: each of OnNowPlayingMessageAsync, OnVolumeMessageAsync and
  /// OnEventPlaybackMessageAsync states what its own null means, beside the code that dispatches it.
  /// Gated since `UI-14` by AudioStateHubServicePassThroughTests.
  ///
  /// ⛔ Do NOT "simplify" this by moving the check down into the fan-out.
  ///
  /// ⚠ IT IS NOT SILENT, and that is the point rather than a nicety. The row objected to a silent
  /// drop. Had a null RadioStateDto ever arrived it would have reached RadioControlPanel.razor:1053
  /// (`if (dto.RdsRelevantChanged)`) and thrown a NullReferenceException that NotifyAsync swallows per
  /// subscriber; the other three subscribers — NowPlayingPanel.razor:646, RadioPage.razor:337 and
  /// AudioStateStore.cs:241 — assign it and repaint, blanking the readout WITHOUT throwing. One
  /// greppable line replaces that. ⚠ SUBJUNCTIVE ON PURPOSE: no null has arrived, nothing was thrown
  /// and nothing was wiped. An earlier revision wrote it as history, which invites a hunt through
  /// journalctl for an NRE that was never logged.
  ///
  /// 📌 No known producer can trigger this on ANY of the four events the guard serves, and the
  /// evidence lives here so no caller has to re-derive it. Every sender is in
  /// Radio.API/Services/AudioStateUpdateService.cs. RadioStateChanged at :478-479 sends the result of
  /// RadioStateMapper.cs:48, which returns a <c>new RadioStateDto { … }</c>; EncoderConnectionChanged
  /// (:1002), EncoderConfigStatusChanged (:1028) and EncoderHudChanged (:1115) each build an anonymous
  /// object inline with <c>new { … }</c>. None of the four can be null. The guard is against the
  /// untyped WIRE; it is not a claim that the current server is wrong.
  /// </remarks>
  private bool AcceptPayload<T>(string eventName, [NotNullWhen(true)] T? payload) where T : class
  {
    if (payload is not null)
    {
      return true;
    }

    if (ShouldLogRejection(eventName))
    {
      _logger.LogWarning(
        "Discarded a {EventName} broadcast with a null payload — its subscribers are typed to receive "
        + "a non-null {PayloadType}. The sender violated the hub contract.",
        eventName, typeof(T).Name);
    }

    return false;
  }

  /// <summary>
  /// True the first time <paramref name="eventName"/> is rejected, and at most once per
  /// <see cref="NullPayloadLogInterval"/> for that same event thereafter.
  /// </summary>
  /// <remarks>
  /// ⚠ The rejection path sits on a documented 20 Hz route, which is why it is throttled at all.
  /// EncoderHudChanged reaches this at up to 20 Hz while a knob is moving (its declaration at :79-82;
  /// the server sends it at AudioStateUpdateService.cs:1114-1115). And src/Radio.Web/appsettings.json
  /// puts MinimumLevel.Default at Information (:50-51) with NO restrictedToMinimumLevel on the Console
  /// sink (:64-68) — unlike Radio.API since LOG-11 — so under systemd every Warning from this process
  /// lands in `journalctl -u radio-web`, on a box where log volume correlates with audible audio
  /// distortion (CLAUDE.md § Deployment).
  ///
  /// ⭐ The FIRST rejection of each event name always logs. The throttle suppresses the repeat, never
  /// the news, which is what keeps <see cref="AcceptPayload{T}"/>'s "it is not silent" property true.
  /// Same shape as _lastDisconnectLogUtc / DisconnectLogInterval at :109-111, used at :287-292.
  /// </remarks>
  private bool ShouldLogRejection(string eventName)
  {
    var now = DateTime.UtcNow;
    lock (_nullPayloadLogLock)
    {
      if (_lastNullPayloadLogUtc.TryGetValue(eventName, out var last)
        && now - last < NullPayloadLogInterval)
      {
        return false;
      }

      _lastNullPayloadLogUtc[eventName] = now;
      return true;
    }
  }

  /// <summary>Applies one "RadioStateChanged" broadcast.</summary>
  /// <remarks>
  /// ⚠ internal for the test seam — but NOT because the event cannot be raised, which is the reason
  /// this comment used to give. <c>HubEventFire</c> reflects the compiler-generated backing field
  /// (HubEventFire.cs:91-92), and RadioControlPanelBandSyncTests.cs:167-173 fires RadioStateChanged
  /// on a hub instance today, green.
  ///
  /// ⭐ That fires the EVENT, which is DOWNSTREAM of the guard below, so a test written that way
  /// passes whether the guard is present, absent or inverted — it proves nothing about this method.
  /// The real obstacle is that the SignalR On&lt;T&gt; lambda which used to hold this body needs a
  /// STARTED HubConnection, and no fixture in Radio.Web.Tests has one: every fixture that starts this
  /// service runs it over OfflineHubTransport (HermeticTestRig.cs:91-97), which fails the connection.
  /// Radio.Web.csproj:31 already declares InternalsVisibleTo("Radio.Web.Tests").
  /// </remarks>
  internal async Task OnRadioStateMessageAsync(RadioStateDto? dto)
  {
    _logger.LogDebug("Received RadioStateChanged event");
    if (!AcceptPayload(nameof(RadioStateChanged), dto))
    {
      return;
    }

    await NotifyAsync(RadioStateChanged, dto);
  }

  /// <summary>Applies one "EncoderConnectionChanged" broadcast. ⚠ internal for the test seam.</summary>
  internal async Task OnEncoderConnectionMessageAsync(EncoderConnectionDto? dto)
  {
    _logger.LogDebug(
      "Received EncoderConnectionChanged: IsConnected={IsConnected}, WasEverConnected={WasEver}",
      dto?.IsConnected, dto?.WasEverConnected);
    if (!AcceptPayload(nameof(EncoderConnectionChanged), dto))
    {
      return;
    }

    await NotifyAsync(EncoderConnectionChanged, dto);
  }

  /// <summary>Applies one "EncoderConfigStatusChanged" broadcast. ⚠ internal for the test seam.</summary>
  internal async Task OnEncoderConfigStatusMessageAsync(EncoderConfigStatusDto? dto)
  {
    _logger.LogDebug("Received EncoderConfigStatusChanged: {Status}", dto?.Status);
    if (!AcceptPayload(nameof(EncoderConfigStatusChanged), dto))
    {
      return;
    }

    await NotifyAsync(EncoderConfigStatusChanged, dto);
  }

  /// <summary>Applies one "EncoderHudChanged" broadcast. ⚠ internal for the test seam.</summary>
  /// <remarks>
  /// No log line per message — this arrives at up to 20 Hz while a knob is moving (ENC-4; the server
  /// coalesces to ≥ 50 ms before sending, AudioStateUpdateService.cs:1114-1115). The rejection path in
  /// <see cref="AcceptPayload{T}"/> does log, on a payload that sender cannot produce — :1115 builds
  /// its anonymous object inline — and is throttled per event name so that an untyped wire which DOES
  /// produce one cannot flood the journal at 20 Hz.
  /// </remarks>
  internal async Task OnEncoderHudMessageAsync(EncoderHudDto? dto)
  {
    if (!AcceptPayload(nameof(EncoderHudChanged), dto))
    {
      return;
    }

    await NotifyAsync(EncoderHudChanged, dto);
  }

  // ---------------------------------------------------------------------------------------------
  // The three events where NULL IS DATA. ⛔ None of these may grow an AcceptPayload call, and
  // NotifyAsync<T> below must never grow a null check — that is the `UI-12` §0.5 / ADR-033 defect,
  // and `UI-14` exists because the realistic form of it passed the entire suite until these seams
  // made it observable.
  // ---------------------------------------------------------------------------------------------

  /// <summary>Applies one "NowPlayingChanged" broadcast. ⚠ internal for the test seam.</summary>
  /// <remarks>
  /// ⛔ A NULL PAYLOAD IS DATA AND MUST REACH EVERY SUBSCRIBER. The event is declared
  /// <c>Func&lt;NowPlayingDto?, Task&gt;</c>, and under ADR-033 the declaration is the specification.
  ///
  /// ⚠ It does not mean one thing. FIVE production types subscribe to this event and a null means
  /// three different things to them:
  /// <list type="bullet">
  /// <item><c>NowPlayingDock.OnNowPlayingChanged</c> calls <c>ClearDockState()</c> — the dock resets
  /// to "No Track Playing". Demonstrated, not assumed:
  /// <c>NowPlayingDockTests.Dock_NullNowPlayingDto_ClearsState</c>.</item>
  /// <item><c>Sleep.OnNowPlayingChanged</c> calls <c>ApplyNowPlaying(null)</c> — the sleep screen's
  /// track block and art disappear. <c>SleepTests.Sleep_NullNowPlayingDto_ClearsTrackBlock</c>, whose
  /// own comment gives the cost: stale metadata "for hours". (Sleep.razor injects THIS service under
  /// the alias <c>AudioState</c>; it is a direct subscriber, not an AudioStateStore one.)</item>
  /// <item><c>NowPlayingPanel.OnNowPlayingChanged</c> treats it as a REST RE-FETCH TRIGGER
  /// (<c>RefreshPlaybackStateAsync</c>) — not as a clear.</item>
  /// <item><c>AudioStateStore.OnHubNowPlayingChanged</c> KEEPS its cached NowPlaying and notifies
  /// anyway; <c>MainLayout.OnNowPlayingChanged</c> discards the payload and re-renders.</item>
  /// </list>
  ///
  /// ⚠ A dropped null is PERMANENT, which is what separates this from the guarded four. Nothing
  /// re-sends it: AudioStateUpdateService only broadcasts on a CHANGE, so once its own
  /// <c>_lastNowPlaying</c> records the silence, the intervening "nothing is playing" is gone and the
  /// dock and sleep screen strand on the previous track until the next real track arrives.
  ///
  /// 📌 No producer can send one today — <c>AudioStateUpdateService.BuildNowPlayingDto</c> returns a
  /// non-nullable type and always builds a <c>new NowPlayingDto { … }</c>. That is a property of the
  /// current server source, not of the type system: the payload arrives from a JSON deserializer
  /// across a process boundary where nullable annotations are erased. Same argument as
  /// <see cref="AcceptPayload{T}"/>'s, pointed the other way.
  /// </remarks>
  internal async Task OnNowPlayingMessageAsync(NowPlayingDto? dto)
  {
    _logger.LogDebug("Received NowPlayingChanged event");
    await NotifyAsync(NowPlayingChanged, dto);
  }

  /// <summary>Applies one "VolumeChanged" broadcast. ⚠ internal for the test seam.</summary>
  /// <remarks>
  /// ⛔ A NULL PAYLOAD IS DATA AND MUST REACH EVERY SUBSCRIBER — declared
  /// <c>Func&lt;VolumeDto?, Task&gt;</c> (ADR-033).
  ///
  /// ⚠ IT IS NOT "AN ABSENT SNAPSHOT", and an earlier revision of the AcceptPayload remark said the
  /// three nullable events behaved alike. They do not. Of the three subscribers:
  /// <list type="bullet">
  /// <item><c>NowPlayingPanel.OnVolumeChangedEvent</c> — a REST RE-FETCH TRIGGER
  /// (<c>RefreshPlaybackStateAsync</c>). This is the only subscriber that does real work on a null,
  /// and it is the one that goes to the network.</item>
  /// <item><c>AudioStateStore.OnHubVolumeChanged</c> — DISCARDS the payload, keeps Volume/IsMuted, and
  /// notifies its own subscribers regardless.</item>
  /// <item><c>MainLayout.OnVolumeChanged</c> — early-returns; the mute chip is unchanged.</item>
  /// </list>
  /// So the cost of dropping one is precisely: the panel's volume/mute readout stays stale until the
  /// next non-null broadcast. Stated at that size on purpose.
  ///
  /// 📌 No producer can send one — the sole sender builds a <c>new VolumeDto { … }</c>.
  /// </remarks>
  internal async Task OnVolumeMessageAsync(VolumeDto? dto)
  {
    _logger.LogDebug("Received VolumeChanged event");
    await NotifyAsync(VolumeChanged, dto);
  }

  /// <summary>Applies one "EventPlaybackChanged" broadcast. ⚠ internal for the test seam.</summary>
  /// <remarks>
  /// ⛔ A NULL PAYLOAD IS DATA BY DECLARATION — <c>Func&lt;EventPlaybackSnapshotDto?, Task&gt;</c> —
  /// ⚠ AND UNEVIDENCED IN PRACTICE. No producer sends one: the sole sender,
  /// <c>AudioStateUpdateService.OnEventPlaybackChanged</c>, always builds an anonymous
  /// <c>new { … }</c>. ⛔ Do NOT write a contract for it that the code does not have. This seam exists
  /// so the declaration and the dispatch cannot silently disagree, NOT because a null is expected.
  ///
  /// 📌 What one WOULD do, since it is cheap to state and expensive to re-derive. There is ONE
  /// subscriber — <c>AudioStateStore.OnHubEventPlaybackChanged</c> — and it does TWO things: it
  /// assigns <c>EventPlayback = dto</c> (a null clears the cached snapshot) AND it sets
  /// <c>_eventPlaybackBroadcastSeen</c>. The second is the non-obvious half: that flag is what makes a
  /// broadcast beat the one-shot REST seed (the ENC-12 broadcast-wins ordering that
  /// <c>AudioStateStore.EnsureEventPlaybackSeededAsync</c> documents). Dropping a null would therefore
  /// not merely fail to clear a snapshot — it would leave a later, staler seed free to win.
  /// </remarks>
  internal async Task OnEventPlaybackMessageAsync(EventPlaybackSnapshotDto? dto)
  {
    _logger.LogDebug("Received EventPlaybackChanged event");
    await NotifyAsync(EventPlaybackChanged, dto);
  }

  /// <summary>
  /// Awaits every subscriber of a parameterless hub event in registration order, catching and
  /// logging each one's exception separately.
  /// </summary>
  /// <remarks>
  /// ⚠ The <see cref="Delegate.GetInvocationList"/> loop is the whole point of this method, and the
  /// one-liner it replaces was wrong in two independent ways (queue row `UI-7`, and `UI-6` before it
  /// for the same shape in <see cref="Radio.Web.Services.AudioStateStore"/>).
  ///
  /// <c>await SomeEvent.Invoke()</c> on a multicast <c>Func&lt;Task&gt;</c> RUNS every subscriber but
  /// returns only the LAST one's <see cref="Task"/>. Every earlier subscriber ran to its first
  /// <c>await</c> and its continuation was never observed, so the caller's <c>await</c> completed
  /// while N−1 handlers were still in flight and their exceptions faulted tasks nobody held.
  ///
  /// ⚠ The sharper half: a subscriber that throws SYNCHRONOUSLY — before its first <c>await</c> —
  /// threw out of <c>Invoke</c> itself, so every handler registered AFTER it never ran at all. That
  /// is starvation, not a lost log line, and catching INSIDE the loop is what resumes the list.
  /// <see cref="Radio.Infrastructure.Audio.Services.DuckingService"/> documents the same shape as a
  /// known, accepted limitation for two subscribers and says a third would want exactly this loop —
  /// note that its event is a synchronous EventHandler&lt;T&gt;, so it precedents the starvation half
  /// only (plan UI-7 C-209).
  ///
  /// ⚠⚠ THIS CLASS IS NOT THE DORMANT CASE, AND THE ROW THAT FILED IT SAID IT WAS. `UI-7` C-203:
  /// AudioStateStore is NOT the only subscriber. Ten production types subscribe — the store,
  /// EncoderHudService, and eight rendered components — and this service is registered AddSingleton
  /// (Program.cs:411) while the components subscribe PER CIRCUIT. Two open browsers already puts nine
  /// handlers on NowPlayingChanged. Every consequence above was happening on the appliance.
  /// ⛔ Do NOT "simplify" this back to a null check and an Invoke.
  ///
  /// ⚠ Subscribers now run SEQUENTIALLY rather than being started back-to-back, and the invocation
  /// list here is longer than the store's. The handlers are Blazor InvokeAsync(StateHasChanged)
  /// dispatches, which queue onto their own circuit's renderer and return, so serializing them costs
  /// a dispatch each rather than a render each — AudioStateStore.cs:438-442 makes the same argument.
  /// </remarks>
  private async Task NotifyAsync(Func<Task>? handler)
  {
    if (handler == null)
    {
      return;
    }

    foreach (var subscriber in handler.GetInvocationList())
    {
      try
      {
        // Inside the try, so a synchronous throw is caught and the NEXT subscriber still runs.
        await ((Func<Task>)subscriber).Invoke();
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Error notifying AudioStateHubService subscriber");
      }
    }
  }

  /// <summary>
  /// Awaits every subscriber of a hub event that carries a payload, in registration order, catching
  /// and logging each one's exception separately.
  /// </summary>
  /// <remarks>
  /// The generic twin of the parameterless overload above — see its remarks for why the loop exists.
  /// Generic rather than duplicated per event, because a hand-rolled loop per event is one chance to
  /// drift per event — which is precisely what UI-6 found when AudioStateStore's two hand-rolled
  /// sites had already diverged (one carried a try/catch and the other carried none).
  /// (An earlier revision said "THIRTEEN events between the two overloads". There are FOURTEEN, and
  /// pre-merge review caught it. The number is now absent rather than corrected — the constructor
  /// comment above makes the same point about the "~9" it used to carry.)
  ///
  /// ⛔ AND IT MUST NEVER GROW A NULL CHECK. T is erased here: this method cannot tell a contract
  /// violation from data, and three of the seven reference-payload events it fans out treat null as
  /// data. ⚠ The realistic form of that mistake — keeping AcceptPayload and adding a "defensive"
  /// check here as well — passed the ENTIRE Radio.Web.Tests assembly on 2026-09-09, measured, while
  /// silently dropping every NowPlayingChanged(null). It is gated now
  /// (AudioStateHubServicePassThroughTests), and the gate, not this comment, is what stops it.
  /// </remarks>
  private async Task NotifyAsync<T>(Func<T, Task>? handler, T arg)
  {
    if (handler == null)
    {
      return;
    }

    foreach (var subscriber in handler.GetInvocationList())
    {
      try
      {
        await ((Func<T, Task>)subscriber).Invoke(arg);
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Error notifying AudioStateHubService subscriber");
      }
    }
  }

  private static bool IsConnectionRefused(Exception ex)
  {
    var current = ex;
    while (current != null)
    {
      if (current is SocketException { SocketErrorCode: SocketError.ConnectionRefused })
      {
        return true;
      }

      current = current.InnerException;
    }
    return false;
  }

  /// <summary>
  /// Custom retry policy with exponential backoff
  /// </summary>
  private class RetryPolicy : IRetryPolicy
  {
    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
      // Exponential backoff: 2s, 4s, 8s, 16s, 30s (max)
      var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, retryContext.PreviousRetryCount)));
      return delay;
    }
  }
}

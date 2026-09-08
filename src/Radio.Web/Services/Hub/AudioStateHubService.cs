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
      _hubConnection.On<NowPlayingDto?>("NowPlayingChanged", async (dto) =>
      {
        _logger.LogDebug("Received NowPlayingChanged event");
        await NotifyAsync(NowPlayingChanged, dto);
      });

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
      _hubConnection.On<RadioStateDto>("RadioStateChanged", async (dto) =>
      {
        _logger.LogDebug("Received RadioStateChanged event");
        // ⚠ ASYMMETRY, PRE-EXISTING AND DELIBERATELY PRESERVED. RadioStateChanged is
        // Func<RadioStateDto, Task> — a NON-nullable payload, like the three Encoder events below
        // — but unlike them it has never carried a `dto != null` guard, on `main` or here. UI-7
        // changed only the fan-out and did not add one, because adding a guard would silently drop
        // a broadcast the panel currently receives, which is a behaviour change this row has no
        // mandate for. Pre-merge review raised it: the guard comment below indicts this site by its
        // own logic. Mitigating, and the reason it is safe to leave: the new per-subscriber catch
        // means a resulting NullReferenceException is now logged rather than faulting a task nobody
        // holds. Worth its own row, not a rider here.
        await NotifyAsync(RadioStateChanged, dto);
      });

      // Server sends VolumeChanged with a VolumeDto payload —
      // deserialize and pass through so subscribers can update directly.
      _hubConnection.On<VolumeDto?>("VolumeChanged", async (dto) =>
      {
        _logger.LogDebug("Received VolumeChanged event");
        await NotifyAsync(VolumeChanged, dto);
      });

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
      _hubConnection.On<EncoderConnectionDto>("EncoderConnectionChanged", async (dto) =>
      {
        _logger.LogDebug(
          "Received EncoderConnectionChanged: IsConnected={IsConnected}, WasEverConnected={WasEver}",
          dto?.IsConnected, dto?.WasEverConnected);
        // The payload guard is NOT a subscriber guard and must survive: this delegate is
        // Func<{Dto}, Task> with a NON-nullable payload, so handing it null would give
        // subscribers a null they are typed to never receive. NotifyAsync only replaces the
        // `EncoderConnectionChanged != null` half.
        if (dto != null)
        {
          await NotifyAsync(EncoderConnectionChanged, dto);
        }
      });

      // Server sends EncoderConfigStatusChanged when the configuration tier changes (ENC-12).
      _hubConnection.On<EncoderConfigStatusDto>("EncoderConfigStatusChanged", async (dto) =>
      {
        _logger.LogDebug("Received EncoderConfigStatusChanged: {Status}", dto?.Status);
        // The payload guard is NOT a subscriber guard and must survive: this delegate is
        // Func<{Dto}, Task> with a NON-nullable payload, so handing it null would give
        // subscribers a null they are typed to never receive. NotifyAsync only replaces the
        // `EncoderConfigStatusChanged != null` half.
        if (dto != null)
        {
          await NotifyAsync(EncoderConfigStatusChanged, dto);
        }
      });

      // Server sends EncoderHudChanged when a knob acts (ENC-4).
      _hubConnection.On<EncoderHudDto>("EncoderHudChanged", async (dto) =>
      {
        // No log line per message. This arrives at up to 20 Hz while a knob is moving.
        // The payload guard is NOT a subscriber guard and must survive: this delegate is
        // Func<{Dto}, Task> with a NON-nullable payload, so handing it null would give
        // subscribers a null they are typed to never receive. NotifyAsync only replaces the
        // `EncoderHudChanged != null` half.
        if (dto != null)
        {
          await NotifyAsync(EncoderHudChanged, dto);
        }
      });

      // Server sends SleepStateChanged with bool payload (true=sleeping, false=awake)
      _hubConnection.On<bool>("SleepStateChanged", async (isSleeping) =>
      {
        _logger.LogDebug("Received SleepStateChanged event: IsSleeping={IsSleeping}", isSleeping);
        await NotifyAsync(SleepStateChanged, isSleeping);
      });

      // Server sends EventPlaybackChanged on every attended-playback transition (ADR-029 D6 §8.1).
      // Transitions only — there is no position tick, and §8.2 refuses one outright.
      _hubConnection.On<EventPlaybackSnapshotDto?>("EventPlaybackChanged", async (dto) =>
      {
        _logger.LogDebug("Received EventPlaybackChanged event");
        await NotifyAsync(EventPlaybackChanged, dto);
      });

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

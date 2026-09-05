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
public class AudioStateHubService : IAsyncDisposable
{
  private readonly ILogger<AudioStateHubService> _logger;
  private readonly IConfiguration _configuration;
  // Web-process instance of the SQLite-config reload notifier. Calling
  // NotifyReload() forces this process's SqliteConfigurationProvider to re-read
  // the shared config DB — the cross-process half of the ConfigChanged bridge.
  // Optional so the ~9 test fixtures that new this service up directly keep
  // compiling; production always injects the registered singleton.
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
  // config snapshot. Optional for subscribers that want an immediate re-render;
  // the topbar / sleep clocks don't need it (their 1 s timers repaint anyway).
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
        if (PlaybackStateChanged != null)
        {
          await PlaybackStateChanged.Invoke();
        }
      });

      // Server sends NowPlayingChanged with a NowPlayingDto payload —
      // deserialize and pass through so subscribers can use it directly.
      _hubConnection.On<NowPlayingDto?>("NowPlayingChanged", async (dto) =>
      {
        _logger.LogDebug("Received NowPlayingChanged event");
        if (NowPlayingChanged != null)
        {
          await NowPlayingChanged.Invoke(dto);
        }
      });

      // Server sends QueueChanged with a list payload —
      // accept and discard it so SignalR dispatches the message.
      _hubConnection.On<object>("QueueChanged", async (_) =>
      {
        _logger.LogDebug("Received QueueChanged event");
        if (QueueChanged != null)
        {
          await QueueChanged.Invoke();
        }
      });

      // Server sends RadioStateChanged with a RadioStateDto payload —
      // deserialize and pass through so subscribers can read NowPlayingMatchId
      // directly. (Previously the payload was discarded and subscribers
      // re-fetched via REST, which strips NowPlayingMatchId and silently
      // broke the recognition stream's NOW-row anchor.)
      _hubConnection.On<RadioStateDto>("RadioStateChanged", async (dto) =>
      {
        _logger.LogDebug("Received RadioStateChanged event");
        if (RadioStateChanged != null)
        {
          await RadioStateChanged.Invoke(dto);
        }
      });

      // Server sends VolumeChanged with a VolumeDto payload —
      // deserialize and pass through so subscribers can update directly.
      _hubConnection.On<VolumeDto?>("VolumeChanged", async (dto) =>
      {
        _logger.LogDebug("Received VolumeChanged event");
        if (VolumeChanged != null)
        {
          await VolumeChanged.Invoke(dto);
        }
      });

      _hubConnection.On("SourceChanged", async () =>
      {
        _logger.LogDebug("Received SourceChanged event");
        if (SourceChanged != null)
        {
          await SourceChanged.Invoke();
        }
      });

      // Server sends FingerprintStatusChanged with a FingerprintStatusDto payload —
      // accept and discard it so SignalR dispatches the message.
      _hubConnection.On<object>("FingerprintStatusChanged", async (_) =>
      {
        _logger.LogDebug("Received FingerprintStatusChanged event");
        if (FingerprintStatusChanged != null)
        {
          await FingerprintStatusChanged.Invoke();
        }
      });

      // Server sends PhoneCallStateChanged with state payload
      _hubConnection.On<object>("PhoneCallStateChanged", async (_) =>
      {
        _logger.LogDebug("Received PhoneCallStateChanged event");
        if (PhoneCallStateChanged != null)
        {
          await PhoneCallStateChanged.Invoke();
        }
      });

      // Server sends EncoderConnectionChanged when encoder device connects/disconnects
      _hubConnection.On<EncoderConnectionDto>("EncoderConnectionChanged", async (dto) =>
      {
        _logger.LogDebug(
          "Received EncoderConnectionChanged: IsConnected={IsConnected}, WasEverConnected={WasEver}",
          dto?.IsConnected, dto?.WasEverConnected);
        if (EncoderConnectionChanged != null && dto != null)
        {
          await EncoderConnectionChanged.Invoke(dto);
        }
      });

      // Server sends EncoderConfigStatusChanged when the configuration tier changes (ENC-12).
      _hubConnection.On<EncoderConfigStatusDto>("EncoderConfigStatusChanged", async (dto) =>
      {
        _logger.LogDebug("Received EncoderConfigStatusChanged: {Status}", dto?.Status);
        if (EncoderConfigStatusChanged != null && dto != null)
        {
          await EncoderConfigStatusChanged.Invoke(dto);
        }
      });

      // Server sends EncoderHudChanged when a knob acts (ENC-4).
      _hubConnection.On<EncoderHudDto>("EncoderHudChanged", async (dto) =>
      {
        // No log line per message. This arrives at up to 20 Hz while a knob is moving.
        if (EncoderHudChanged != null && dto != null)
        {
          await EncoderHudChanged.Invoke(dto);
        }
      });

      // Server sends SleepStateChanged with bool payload (true=sleeping, false=awake)
      _hubConnection.On<bool>("SleepStateChanged", async (isSleeping) =>
      {
        _logger.LogDebug("Received SleepStateChanged event: IsSleeping={IsSleeping}", isSleeping);
        if (SleepStateChanged != null)
        {
          await SleepStateChanged.Invoke(isSleeping);
        }
      });

      // Server sends EventPlaybackChanged on every attended-playback transition (ADR-029 D6 §8.1).
      // Transitions only — there is no position tick, and §8.2 refuses one outright.
      _hubConnection.On<EventPlaybackSnapshotDto?>("EventPlaybackChanged", async (dto) =>
      {
        _logger.LogDebug("Received EventPlaybackChanged event");
        if (EventPlaybackChanged != null)
        {
          await EventPlaybackChanged.Invoke(dto);
        }
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
        var handler = ConfigChanged;
        if (handler != null)
        {
          await handler.Invoke();
        }
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
    if (SourceChanged != null)
    {
      await SourceChanged.Invoke();
    }
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

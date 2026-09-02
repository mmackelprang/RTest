using System.Net.Sockets;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Radio.Web.Models;

namespace Radio.Web.Services.Hub;

/// <summary>
/// SignalR hub service for real-time audio visualization data
/// Connects to /hubs/visualization and provides spectrum, waveform, and level meter data
/// </summary>
public class AudioVisualizationHubService : IAsyncDisposable
{
  private readonly ILogger<AudioVisualizationHubService> _logger;
  private readonly IConfiguration _configuration;
  private HubConnection? _hubConnection;
  private bool _isDisposed;
  private readonly SemaphoreSlim _connectionLock = new(1, 1);
  // Background retry loop activated when the initial StartAsync fails (radio-api not yet
  // listening at deploy time, network blip, etc.). The loop polls until the hub is
  // reachable, then replays any subscriptions the UI recorded while we were offline.
  // Mirrors the pattern in GvBridgeHubService.
  private CancellationTokenSource? _retryCts;

  // Events that components can subscribe to
  public event Func<SpectrumDataDto, Task>? OnSpectrumData;
  public event Func<LevelDataDto, Task>? OnLevelData;
  public event Func<WaveformDataDto, Task>? OnWaveformData;
  public event Func<VisualizationDataDto, Task>? OnVisualizationData;

  // Track active group subscriptions so we can re-subscribe on reconnect
  private readonly HashSet<string> _activeSubscriptions = new();
  private readonly object _subscriptionLock = new();

  // Throttle disconnect log messages to avoid spam when API is down
  private static DateTime _lastDisconnectLogUtc = DateTime.MinValue;
  private static readonly TimeSpan DisconnectLogInterval = TimeSpan.FromSeconds(10);

  public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;
  public HubConnectionState ConnectionState => _hubConnection?.State ?? HubConnectionState.Disconnected;

  private readonly IHubConnectionTransport? _transport;

  public AudioVisualizationHubService(
    ILogger<AudioVisualizationHubService> logger,
    IConfiguration configuration,
    IHubConnectionTransport? transport = null)
  {
    _logger = logger;
    _configuration = configuration;
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
      // Connection object exists but isn't connected. Two cases:
      //   1. SignalR's WithAutomaticReconnect is between attempts — let it run.
      //   2. The initial StartAsync threw and left _hubConnection in Disconnected state.
      //      In that case our background _retryCts loop is already polling — also leave it alone.
      // Either way, returning is safe: when the connection eventually establishes, the
      // Reconnected handler (or the retry-loop success path) replays subscriptions.
      if (_hubConnection != null)
      {
        _logger.LogDebug("Hub connection initialization already in progress (State={State})", _hubConnection.State);
        return;
      }

      var apiBaseUrl = _configuration["ApiBaseUrl"] ?? WebConstants.DefaultApiBaseUrl;
      var hubUrl = $"{apiBaseUrl}{WebConstants.HubPaths.Visualization}";

      _logger.LogInformation("Initializing SignalR connection to {HubUrl}", hubUrl);

      _hubConnection = new HubConnectionBuilder()
        .WithUrl(hubUrl, options => _transport?.Configure(options))
        .WithAutomaticReconnect(new RetryPolicy())
        .ConfigureLogging(logging =>
        {
          logging.SetMinimumLevel(LogLevel.Information);
        })
        .Build();

      // Register event handlers for broadcast events
      // Event names must match what VisualizationBroadcastService sends
      _hubConnection.On<SpectrumDataDto>("ReceiveSpectrum", async (data) =>
      {
        _logger.LogTrace("Received ReceiveSpectrum event");
        await InvokeEventHandlersAsync(OnSpectrumData, data, "OnSpectrumData");
      });

      _hubConnection.On<LevelDataDto>("ReceiveLevels", async (data) =>
      {
        _logger.LogTrace("Received ReceiveLevels event");
        await InvokeEventHandlersAsync(OnLevelData, data, "OnLevelData");
      });

      _hubConnection.On<WaveformDataDto>("ReceiveWaveform", async (data) =>
      {
        _logger.LogTrace("Received ReceiveWaveform event");
        await InvokeEventHandlersAsync(OnWaveformData, data, "OnWaveformData");
      });

      _hubConnection.On<VisualizationDataDto>("ReceiveVisualization", async (data) =>
      {
        _logger.LogTrace("Received ReceiveVisualization event");
        await InvokeEventHandlersAsync(OnVisualizationData, data, "OnVisualizationData");
      });

      // Connection lifecycle events — throttled to avoid log spam when API is down
      _hubConnection.Reconnecting += exception =>
      {
        if (exception == null || !IsConnectionRefused(exception))
        {
          _logger.LogWarning(exception, "Visualization hub reconnecting");
        }

        return Task.CompletedTask;
      };

      _hubConnection.Reconnected += async connectionId =>
      {
        _lastDisconnectLogUtc = DateTime.MinValue; // Reset throttle
        _logger.LogInformation("Visualization hub reconnected with ID: {ConnectionId}", connectionId);

        // Re-subscribe to all active groups — SignalR group membership is per-connection,
        // so after reconnect (new ConnectionId) the old memberships are gone.
        string[] subscriptions;
        lock (_subscriptionLock)
        {
          subscriptions = _activeSubscriptions.ToArray();
        }

        foreach (var group in subscriptions)
        {
          try
          {
            await _hubConnection.InvokeAsync($"SubscribeTo{group}");
            _logger.LogDebug("Re-subscribed to {Group} after reconnect", group);
          }
          catch (Exception ex)
          {
            _logger.LogWarning(ex, "Failed to re-subscribe to {Group} after reconnect", group);
          }
        }

        if (subscriptions.Length > 0)
        {
          _logger.LogInformation("Re-subscribed to {Count} visualization groups after reconnect", subscriptions.Length);
        }
      };

      _hubConnection.Closed += exception =>
      {
        if (exception != null && IsConnectionRefused(exception))
        {
          var now = DateTime.UtcNow;
          if (now - _lastDisconnectLogUtc >= DisconnectLogInterval)
          {
            _lastDisconnectLogUtc = now;
            _logger.LogWarning("Visualization hub connection lost — API unavailable");
          }
        }
        else
        {
          _logger.LogWarning(exception, "Visualization hub connection closed");
        }

        return Task.CompletedTask;
      };

      try
      {
        await _hubConnection.StartAsync(cancellationToken);
        _logger.LogInformation("Connected to AudioVisualizationHub");
      }
      catch (Exception ex)
      {
        // Initial connect failed — typically because radio-api hasn't bound port 5000 yet
        // during a fresh deploy (api + web start in parallel; web is faster). Don't leave
        // _hubConnection in a dead non-null state that future StartAsync calls skip past;
        // instead, kick off a background retry loop and leave the connection object intact
        // so once it succeeds, the existing event handlers are wired up.
        _logger.LogWarning(ex, "Initial connection to AudioVisualizationHub at {Url} failed — retrying in background", hubUrl);
        StartRetryLoop(hubUrl);
      }
    }
    finally
    {
      _connectionLock.Release();
    }
  }

  /// <summary>
  /// Polls the hub until <see cref="HubConnection.StartAsync(CancellationToken)"/> succeeds,
  /// then replays any group subscriptions the UI recorded while we were offline. Cancelled
  /// on Stop/Dispose. Idempotent — cancels any previous loop before starting a new one.
  /// Mirrors <c>GvBridgeHubService.StartRetryLoop</c>.
  /// </summary>
  private void StartRetryLoop(string hubUrl)
  {
    _retryCts?.Cancel();
    _retryCts?.Dispose();
    _retryCts = new CancellationTokenSource();
    var ct = _retryCts.Token;
    _ = Task.Run(async () =>
    {
      // Tight at first (the typical case is a 1–3 second startup race), then back off.
      var delays = new[] { 2, 5, 10, 30 };
      for (var attempt = 0; !ct.IsCancellationRequested; attempt++)
      {
        var delaySec = delays[Math.Min(attempt, delays.Length - 1)];
        try { await Task.Delay(TimeSpan.FromSeconds(delaySec), ct); }
        catch (OperationCanceledException) { return; }

        if (ct.IsCancellationRequested || _hubConnection == null) return;
        if (_hubConnection.State != HubConnectionState.Disconnected) return; // someone else got it

        try
        {
          await _hubConnection.StartAsync(ct);
          _logger.LogInformation("Connected to AudioVisualizationHub at {Url} (retry #{Attempt})", hubUrl, attempt + 1);
          await ReplaySubscriptionsAsync();
          return;
        }
        catch (Exception ex)
        {
          _logger.LogDebug(ex, "Visualization hub retry #{Attempt} failed", attempt + 1);
        }
      }
    }, ct);
  }

  /// <summary>
  /// Invokes each recorded subscription against the hub after a successful retry-loop
  /// connect. The <c>Reconnected</c> event already handles this for post-disconnect
  /// recoveries, but doesn't fire for an initial-connect-after-retry, so we do it here.
  /// </summary>
  private async Task ReplaySubscriptionsAsync()
  {
    string[] subscriptions;
    lock (_subscriptionLock) { subscriptions = _activeSubscriptions.ToArray(); }
    foreach (var group in subscriptions)
    {
      try
      {
        await _hubConnection!.InvokeAsync($"SubscribeTo{group}");
        _logger.LogDebug("Replayed subscription to {Group} after initial connect", group);
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Failed to replay subscription to {Group} after initial connect", group);
      }
    }
    if (subscriptions.Length > 0)
    {
      _logger.LogInformation("Replayed {Count} visualization subscription(s) after initial connect", subscriptions.Length);
    }
  }

  public async Task StopAsync()
  {
    // Cancel any in-flight initial-connect retry first so it doesn't race with the
    // explicit stop below.
    _retryCts?.Cancel();
    _retryCts?.Dispose();
    _retryCts = null;

    await _connectionLock.WaitAsync();
    try
    {
      if (_hubConnection != null)
      {
        await _hubConnection.StopAsync();
        _logger.LogInformation("Disconnected from AudioVisualizationHub");
      }
    }
    finally
    {
      _connectionLock.Release();
    }
  }

  // Subscription methods.
  //
  // Subscribe semantics: record the desired subscription FIRST, then invoke the RPC if
  // the hub is connected. The recorded set is the source of truth for what the UI wants;
  // the RPC is the live binding. When the hub is offline (initial deploy race, or a
  // mid-session drop), the retry/reconnect paths replay the recorded set on connect.
  // This is why _activeSubscriptions.Add() runs unconditionally — losing the intent would
  // mean the user's mode picker selection silently fails to take effect after recovery.
  public async Task SubscribeToSpectrumAsync()
  {
    lock (_subscriptionLock) { _activeSubscriptions.Add("Spectrum"); }
    if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
    {
      await _hubConnection.InvokeAsync("SubscribeToSpectrum");
      _logger.LogDebug("Subscribed to spectrum updates");
    }
    else
    {
      _logger.LogDebug("Recorded spectrum subscription intent; will activate when hub connects");
    }
  }

  public async Task UnsubscribeFromSpectrumAsync()
  {
    lock (_subscriptionLock) { _activeSubscriptions.Remove("Spectrum"); }
    if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
    {
      await _hubConnection.InvokeAsync("UnsubscribeFromSpectrum");
      _logger.LogDebug("Unsubscribed from spectrum updates");
    }
  }

  public async Task SubscribeToLevelsAsync()
  {
    lock (_subscriptionLock) { _activeSubscriptions.Add("Levels"); }
    if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
    {
      await _hubConnection.InvokeAsync("SubscribeToLevels");
      _logger.LogDebug("Subscribed to level updates");
    }
    else
    {
      _logger.LogDebug("Recorded levels subscription intent; will activate when hub connects");
    }
  }

  public async Task UnsubscribeFromLevelsAsync()
  {
    lock (_subscriptionLock) { _activeSubscriptions.Remove("Levels"); }
    if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
    {
      await _hubConnection.InvokeAsync("UnsubscribeFromLevels");
      _logger.LogDebug("Unsubscribed from level updates");
    }
  }

  public async Task SubscribeToWaveformAsync()
  {
    lock (_subscriptionLock) { _activeSubscriptions.Add("Waveform"); }
    if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
    {
      await _hubConnection.InvokeAsync("SubscribeToWaveform");
      _logger.LogDebug("Subscribed to waveform updates");
    }
    else
    {
      _logger.LogDebug("Recorded waveform subscription intent; will activate when hub connects");
    }
  }

  public async Task UnsubscribeFromWaveformAsync()
  {
    lock (_subscriptionLock) { _activeSubscriptions.Remove("Waveform"); }
    if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
    {
      await _hubConnection.InvokeAsync("UnsubscribeFromWaveform");
      _logger.LogDebug("Unsubscribed from waveform updates");
    }
  }

  public async Task SubscribeToAllAsync()
  {
    lock (_subscriptionLock)
    {
      _activeSubscriptions.Add("Spectrum");
      _activeSubscriptions.Add("Levels");
      _activeSubscriptions.Add("Waveform");
    }
    if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
    {
      await _hubConnection.InvokeAsync("SubscribeToAll");
      _logger.LogDebug("Subscribed to all visualization updates");
    }
    else
    {
      _logger.LogDebug("Recorded all-visualization subscription intent; will activate when hub connects");
    }
  }

  public async Task UnsubscribeFromAllAsync()
  {
    lock (_subscriptionLock) { _activeSubscriptions.Clear(); }
    if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
    {
      await _hubConnection.InvokeAsync("UnsubscribeFromAll");
      _logger.LogDebug("Unsubscribed from all visualization updates");
    }
  }

  // Get methods for on-demand data
  public async Task<SpectrumDataDto?> GetSpectrumAsync()
  {
    if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
    {
      try
      {
        return await _hubConnection.InvokeAsync<SpectrumDataDto>("GetSpectrum");
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error getting spectrum data");
        return null;
      }
    }
    return null;
  }

  public async Task<LevelDataDto?> GetLevelsAsync()
  {
    if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
    {
      try
      {
        return await _hubConnection.InvokeAsync<LevelDataDto>("GetLevels");
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error getting level data");
        return null;
      }
    }
    return null;
  }

  public async Task<WaveformDataDto?> GetWaveformAsync()
  {
    if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
    {
      try
      {
        return await _hubConnection.InvokeAsync<WaveformDataDto>("GetWaveform");
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error getting waveform data");
        return null;
      }
    }
    return null;
  }

  public async Task<VisualizationDataDto?> GetVisualizationAsync()
  {
    if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
    {
      try
      {
        return await _hubConnection.InvokeAsync<VisualizationDataDto>("GetVisualization");
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error getting visualization data");
        return null;
      }
    }
    return null;
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
  }

  /// <summary>
  /// Safely invokes event handlers, catching exceptions from individual handlers
  /// to ensure all subscribers are notified even if one throws.
  /// </summary>
  private async Task InvokeEventHandlersAsync<T>(Func<T, Task>? eventHandler, T data, string eventName)
  {
    if (eventHandler == null)
    {
      return;
    }

    var handlers = eventHandler.GetInvocationList();
    foreach (var handler in handlers)
    {
      try
      {
        var func = (Func<T, Task>)handler;
        await func(data);
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Exception in {EventName} event handler", eventName);
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

  // Custom retry policy for automatic reconnection
  private class RetryPolicy : IRetryPolicy
  {
    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
      // Exponential backoff: 0s, 2s, 5s, 10s, 30s, then 30s max
      return retryContext.PreviousRetryCount switch
      {
        0 => TimeSpan.Zero,
        1 => TimeSpan.FromSeconds(2),
        2 => TimeSpan.FromSeconds(5),
        3 => TimeSpan.FromSeconds(10),
        _ => TimeSpan.FromSeconds(30)
      };
    }
  }
}

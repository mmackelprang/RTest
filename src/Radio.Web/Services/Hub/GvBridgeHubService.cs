using Microsoft.AspNetCore.SignalR.Client;

namespace Radio.Web.Services.Hub;

/// <summary>
/// SignalR client that connects to RotaryPhone.API's GV Bridge hub for real-time
/// extension connection status and call adapter mode change notifications.
/// </summary>
public class GvBridgeHubService : IAsyncDisposable
{
  private readonly ILogger<GvBridgeHubService> _logger;
  private readonly IConfiguration _configuration;
  private readonly SemaphoreSlim _connectionLock = new(1, 1);
  private HubConnection? _hubConnection;
  private CancellationTokenSource? _retryCts;

  public event Action<bool>? ExtensionConnectionChanged;
  public event Action<string>? ModeChanged;

  public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

  public GvBridgeHubService(ILogger<GvBridgeHubService> logger, IConfiguration configuration)
  {
    _logger = logger;
    _configuration = configuration;
  }

  public async Task StartAsync()
  {
    if (!await _connectionLock.WaitAsync(0))
    {
      _logger.LogDebug("GV Bridge hub connection already in progress");
      return;
    }

    try
    {
      if (_hubConnection != null)
      {
        return;
      }

      var baseUrl = _configuration.GetValue<string>("RotaryPhone:ApiBaseUrl") ?? "http://radio:5004";
      var hubUrl = $"{baseUrl.TrimEnd('/')}/hubs/gvbridge";

      _hubConnection = new HubConnectionBuilder()
        .WithUrl(hubUrl)
        .WithAutomaticReconnect(new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5),
          TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) })
        .Build();

      _hubConnection.On<object>("ExtensionConnectionChanged", (data) =>
      {
        // Payload: { connected: bool }
        if (data is System.Text.Json.JsonElement json && json.TryGetProperty("connected", out var prop))
        {
          var connected = prop.GetBoolean();
          _logger.LogDebug("GV Bridge extension connection changed: {Connected}", connected);
          ExtensionConnectionChanged?.Invoke(connected);
        }
      });

      _hubConnection.On<object>("ModeChanged", (data) =>
      {
        // Payload: { activeMode: string }
        if (data is System.Text.Json.JsonElement json && json.TryGetProperty("activeMode", out var prop))
        {
          var mode = prop.GetString() ?? "";
          _logger.LogDebug("GV Bridge mode changed: {Mode}", mode);
          ModeChanged?.Invoke(mode);
        }
      });

      _hubConnection.Reconnecting += ex =>
      {
        _logger.LogWarning(ex, "GV Bridge hub reconnecting...");
        return Task.CompletedTask;
      };

      _hubConnection.Reconnected += connectionId =>
      {
        _logger.LogInformation("GV Bridge hub reconnected: {ConnectionId}", connectionId);
        return Task.CompletedTask;
      };

      _hubConnection.Closed += ex =>
      {
        _logger.LogWarning(ex, "GV Bridge hub connection closed");
        return Task.CompletedTask;
      };

      try
      {
        await _hubConnection.StartAsync();
        _logger.LogInformation("Connected to GV Bridge hub at {Url}", hubUrl);
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Failed to connect to GV Bridge hub at {Url} — will retry in background", hubUrl);
        StartRetryLoop(hubUrl);
      }
    }
    finally
    {
      _connectionLock.Release();
    }
  }

  private void StartRetryLoop(string hubUrl)
  {
    _retryCts = new CancellationTokenSource();
    _ = Task.Run(async () =>
    {
      var delays = new[] { 10, 30, 60 };
      for (int attempt = 0; !_retryCts.Token.IsCancellationRequested; attempt++)
      {
        var delaySec = delays[Math.Min(attempt, delays.Length - 1)];
        await Task.Delay(TimeSpan.FromSeconds(delaySec), _retryCts.Token);
        try
        {
          if (_hubConnection?.State == HubConnectionState.Disconnected)
          {
            await _hubConnection.StartAsync(_retryCts.Token);
            _logger.LogInformation("Connected to GV Bridge hub at {Url} (retry #{Attempt})", hubUrl, attempt + 1);
            return;
          }
        }
        catch (Exception ex)
        {
          _logger.LogDebug(ex, "GV Bridge hub retry #{Attempt} failed", attempt + 1);
        }
      }
    }, _retryCts.Token);
  }

  public async Task StopAsync()
  {
    _retryCts?.Cancel();
    _retryCts?.Dispose();
    _retryCts = null;
    if (_hubConnection != null)
    {
      await _hubConnection.DisposeAsync();
      _hubConnection = null;
    }
  }

  public async ValueTask DisposeAsync()
  {
    await StopAsync();
    _connectionLock.Dispose();
  }
}

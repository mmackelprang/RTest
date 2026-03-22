using Microsoft.AspNetCore.SignalR.Client;

namespace Radio.Web.Services.Hub;

/// <summary>
/// SignalR client that connects to RotaryPhone.API's GV Trunk hub for real-time
/// SIP registration, SMS, missed call, and call state notifications.
/// </summary>
public class GvTrunkHubService : IAsyncDisposable
{
  private readonly ILogger<GvTrunkHubService> _logger;
  private readonly IConfiguration _configuration;
  private readonly SemaphoreSlim _connectionLock = new(1, 1);
  private HubConnection? _hubConnection;
  private CancellationTokenSource? _retryCts;

  public event Action<bool>? RegistrationChanged;
  public event Action<object>? SmsReceived;
  public event Action<object>? MissedCallReceived;
  public event Action<string, string>? CallStateChanged;

  public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

  public GvTrunkHubService(ILogger<GvTrunkHubService> logger, IConfiguration configuration)
  {
    _logger = logger;
    _configuration = configuration;
  }

  public async Task StartAsync()
  {
    if (!await _connectionLock.WaitAsync(0))
    {
      _logger.LogDebug("GV Trunk hub connection already in progress");
      return;
    }

    try
    {
      if (_hubConnection != null)
      {
        return;
      }

      var baseUrl = _configuration.GetValue<string>("RotaryPhone:ApiBaseUrl") ?? "http://radio:5004";
      var hubUrl = $"{baseUrl.TrimEnd('/')}/hubs/gvtrunk";

      _hubConnection = new HubConnectionBuilder()
        .WithUrl(hubUrl)
        .WithAutomaticReconnect(new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5),
          TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) })
        .Build();

      _hubConnection.On<object>("RegistrationChanged", (data) =>
      {
        if (data is System.Text.Json.JsonElement json && json.TryGetProperty("isRegistered", out var prop))
        {
          var registered = prop.GetBoolean();
          _logger.LogDebug("GV Trunk registration changed: {Registered}", registered);
          RegistrationChanged?.Invoke(registered);
        }
      });

      _hubConnection.On<object>("SmsReceived", (notification) =>
      {
        _logger.LogDebug("GV Trunk SMS received");
        SmsReceived?.Invoke(notification);
      });

      _hubConnection.On<object>("MissedCallReceived", (notification) =>
      {
        _logger.LogDebug("GV Trunk missed call received");
        MissedCallReceived?.Invoke(notification);
      });

      _hubConnection.On<object>("CallStateChanged", (data) =>
      {
        if (data is System.Text.Json.JsonElement json)
        {
          var phoneId = json.TryGetProperty("phoneId", out var pidProp) ? pidProp.GetString() ?? "" : "";
          var callState = json.TryGetProperty("callState", out var csProp) ? csProp.GetString() ?? "" : "";
          _logger.LogDebug("GV Trunk call state changed: {PhoneId} → {State}", phoneId, callState);
          CallStateChanged?.Invoke(phoneId, callState);
        }
      });

      _hubConnection.Reconnecting += ex =>
      {
        _logger.LogWarning(ex, "GV Trunk hub reconnecting...");
        return Task.CompletedTask;
      };

      _hubConnection.Reconnected += connectionId =>
      {
        _logger.LogInformation("GV Trunk hub reconnected: {ConnectionId}", connectionId);
        return Task.CompletedTask;
      };

      _hubConnection.Closed += ex =>
      {
        _logger.LogWarning(ex, "GV Trunk hub connection closed");
        return Task.CompletedTask;
      };

      try
      {
        await _hubConnection.StartAsync();
        _logger.LogInformation("Connected to GV Trunk hub at {Url}", hubUrl);
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Failed to connect to GV Trunk hub at {Url} — will retry in background", hubUrl);
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
            _logger.LogInformation("Connected to GV Trunk hub at {Url} (retry #{Attempt})", hubUrl, attempt + 1);
            return;
          }
        }
        catch (Exception ex)
        {
          _logger.LogDebug(ex, "GV Trunk hub retry #{Attempt} failed", attempt + 1);
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

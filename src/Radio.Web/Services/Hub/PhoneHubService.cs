using Microsoft.AspNetCore.SignalR.Client;

namespace Radio.Web.Services.Hub;

/// <summary>
/// SignalR client that connects to RotaryPhone.API's hub for real-time
/// call state, incoming call, and history update notifications.
/// </summary>
public class PhoneHubService : IAsyncDisposable
{
  private readonly ILogger<PhoneHubService> _logger;
  private readonly IConfiguration _configuration;
  private readonly SemaphoreSlim _connectionLock = new(1, 1);
  private HubConnection? _hubConnection;
  private CancellationTokenSource? _retryCts;

  public event Action<string, string>? CallStateChanged;
  public event Action<string, string>? IncomingCall;
  public event Action? CallHistoryUpdated;
  public event Action<object>? SystemStatusChanged;

  public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

  public PhoneHubService(ILogger<PhoneHubService> logger, IConfiguration configuration)
  {
    _logger = logger;
    _configuration = configuration;
  }

  public async Task StartAsync()
  {
    if (!await _connectionLock.WaitAsync(0))
    {
      _logger.LogDebug("Phone hub connection already in progress");
      return;
    }

    try
    {
      if (_hubConnection != null)
      {
        return;
      }

      var hubUrl = _configuration.GetValue<string>("RotaryPhone:HubUrl") ?? "http://localhost:5004/hub";

      _hubConnection = new HubConnectionBuilder()
        .WithUrl(hubUrl)
        .WithAutomaticReconnect(new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5),
          TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) })
        .Build();

      _hubConnection.On<string, string>("CallStateChanged", (phoneId, state) =>
      {
        _logger.LogDebug("Phone call state changed: {PhoneId} → {State}", phoneId, state);
        CallStateChanged?.Invoke(phoneId, state);
      });

      _hubConnection.On<string, string>("IncomingCall", (phoneId, phoneNumber) =>
      {
        _logger.LogInformation("Incoming call from {PhoneNumber}", phoneNumber);
        IncomingCall?.Invoke(phoneId, phoneNumber);
      });

      _hubConnection.On("CallHistoryUpdated", () =>
      {
        CallHistoryUpdated?.Invoke();
      });

      _hubConnection.On<object>("SystemStatusChanged", (status) =>
      {
        SystemStatusChanged?.Invoke(status);
      });

      _hubConnection.Reconnecting += ex =>
      {
        _logger.LogWarning(ex, "Phone hub reconnecting...");
        return Task.CompletedTask;
      };

      _hubConnection.Reconnected += connectionId =>
      {
        _logger.LogInformation("Phone hub reconnected: {ConnectionId}", connectionId);
        return Task.CompletedTask;
      };

      _hubConnection.Closed += ex =>
      {
        _logger.LogWarning(ex, "Phone hub connection closed");
        return Task.CompletedTask;
      };

      try
      {
        await _hubConnection.StartAsync();
        _logger.LogInformation("Connected to RotaryPhone hub at {Url}", hubUrl);
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Failed to connect to RotaryPhone hub at {Url} — will retry in background", hubUrl);
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
            _logger.LogInformation("Connected to RotaryPhone hub at {Url} (retry #{Attempt})", hubUrl, attempt + 1);
            return;
          }
        }
        catch (Exception ex)
        {
          _logger.LogDebug(ex, "Phone hub retry #{Attempt} failed", attempt + 1);
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

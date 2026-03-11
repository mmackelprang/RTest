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
  private HubConnection? _hubConnection;

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
      _logger.LogWarning(ex, "Failed to connect to RotaryPhone hub at {Url} — phone features unavailable", hubUrl);
    }
  }

  public async Task StopAsync()
  {
    if (_hubConnection != null)
    {
      await _hubConnection.DisposeAsync();
      _hubConnection = null;
    }
  }

  public async ValueTask DisposeAsync()
  {
    await StopAsync();
  }
}

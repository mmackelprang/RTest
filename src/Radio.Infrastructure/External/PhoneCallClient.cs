using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.External;

namespace Radio.Infrastructure.External;

/// <summary>
/// SignalR client that connects to the RotaryPhone hub and relays call state changes.
/// Handles connection lifecycle with exponential backoff reconnection.
/// Note: The RotaryPhone hub API is assumed but not yet verified — expect protocol
/// mismatches during initial integration.
/// </summary>
public class PhoneCallClient : IPhoneIntegrationService
{
  private readonly ILogger<PhoneCallClient> _logger;
  private readonly IOptionsMonitor<PhoneIntegrationOptions> _options;
  private HubConnection? _hubConnection;
  private bool _isDisposed;
  private PhoneCallState _currentState = PhoneCallState.Idle;
  private string? _callerNumber;
  private string? _callerName;

  public PhoneCallClient(
    ILogger<PhoneCallClient> logger,
    IOptionsMonitor<PhoneIntegrationOptions> options)
  {
    _logger = logger;
    _options = options;
  }

  /// <inheritdoc />
  public PhoneCallState CurrentState => _currentState;

  /// <inheritdoc />
  public string? CallerNumber => _callerNumber;

  /// <inheritdoc />
  public string? CallerName => _callerName;

  /// <inheritdoc />
  public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

  /// <inheritdoc />
  public event EventHandler<PhoneCallStateChangedEventArgs>? CallStateChanged;

  /// <inheritdoc />
  public async Task StartAsync(CancellationToken cancellationToken = default)
  {
    var opts = _options.CurrentValue;

    _logger.LogInformation("Connecting to RotaryPhone hub at {Url}", opts.HubUrl);

    _hubConnection = new HubConnectionBuilder()
      .WithUrl(opts.HubUrl)
      .WithAutomaticReconnect(new PhoneRetryPolicy(opts))
      .ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning))
      .Build();

    // Register for call state changes from the RotaryPhone server.
    // The actual method name and parameter shape depend on the RotaryPhone hub contract —
    // we register multiple overloads to be resilient to protocol mismatches.
    _hubConnection.On<string, string>("CallStateChanged", OnCallStateChanged);
    _hubConnection.On<string, string, string>("CallStateChanged", OnCallStateChangedWithName);

    _hubConnection.Closed += OnConnectionClosed;
    _hubConnection.Reconnecting += _ =>
    {
      _logger.LogWarning("RotaryPhone hub reconnecting...");
      return Task.CompletedTask;
    };
    _hubConnection.Reconnected += _ =>
    {
      _logger.LogInformation("RotaryPhone hub reconnected");
      return Task.CompletedTask;
    };

    try
    {
      await _hubConnection.StartAsync(cancellationToken);
      _logger.LogInformation("Connected to RotaryPhone hub");
    }
    catch (Exception ex)
    {
      // Don't fail hard — RotaryPhone server might not be running
      _logger.LogWarning(ex, "Could not connect to RotaryPhone hub at {Url}. Will retry.", opts.HubUrl);
    }
  }

  /// <inheritdoc />
  public async Task StopAsync(CancellationToken cancellationToken = default)
  {
    if (_hubConnection != null)
    {
      try
      {
        await _hubConnection.StopAsync(cancellationToken);
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Error stopping RotaryPhone hub connection");
      }
    }
  }

  private void OnCallStateChanged(string state, string phoneNumber)
  {
    OnCallStateChangedWithName(state, phoneNumber, callerName: null);
  }

  private void OnCallStateChangedWithName(string state, string phoneNumber, string? callerName)
  {
    var parsedState = ParseCallState(state);
    _currentState = parsedState;
    _callerNumber = phoneNumber;
    _callerName = callerName;

    _logger.LogInformation("Phone call state: {State}, Number: {Number}, Name: {Name}",
      parsedState, phoneNumber, callerName ?? "(unknown)");

    CallStateChanged?.Invoke(this, new PhoneCallStateChangedEventArgs
    {
      State = parsedState,
      PhoneNumber = phoneNumber,
      CallerName = callerName
    });
  }

  private static PhoneCallState ParseCallState(string state)
  {
    // Be lenient with the state string — the RotaryPhone server format is unverified
    return state.ToLowerInvariant() switch
    {
      "ringing" or "ring" or "incoming" => PhoneCallState.Ringing,
      "incall" or "in_call" or "active" or "answered" => PhoneCallState.InCall,
      "ended" or "hangup" or "idle" => PhoneCallState.Ended,
      _ => PhoneCallState.Idle
    };
  }

  private Task OnConnectionClosed(Exception? error)
  {
    if (error != null)
      _logger.LogWarning(error, "RotaryPhone hub connection closed with error");
    else
      _logger.LogInformation("RotaryPhone hub connection closed");

    return Task.CompletedTask;
  }

  /// <inheritdoc />
  public async ValueTask DisposeAsync()
  {
    if (_isDisposed) return;
    _isDisposed = true;

    if (_hubConnection != null)
    {
      await _hubConnection.DisposeAsync();
      _hubConnection = null;
    }

    GC.SuppressFinalize(this);
  }

  /// <summary>
  /// Exponential backoff retry policy for RotaryPhone hub connection.
  /// </summary>
  private class PhoneRetryPolicy : IRetryPolicy
  {
    private readonly PhoneIntegrationOptions _opts;

    public PhoneRetryPolicy(PhoneIntegrationOptions opts)
    {
      _opts = opts;
    }

    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
      var delay = Math.Min(
        _opts.ReconnectBaseDelayMs * Math.Pow(2, retryContext.PreviousRetryCount),
        _opts.ReconnectMaxDelayMs);
      return TimeSpan.FromMilliseconds(delay);
    }
  }
}

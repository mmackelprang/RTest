using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.External;
using Radio.Core.Utilities;

namespace Radio.Infrastructure.External;

/// <summary>
/// SignalR client that connects to the RotaryPhone hub and relays call state changes.
/// Handles connection lifecycle with exponential backoff reconnection.
/// </summary>
/// <remarks>
/// <para>
/// <b>The hub contract (PHN-12), read from RotaryPhone's own source rather than assumed.</b>
/// <c>RotaryPhoneController.Server/Services/SignalRNotifierService.cs</c>, <c>OnStateChanged</c>:
/// </para>
/// <code>
/// SendAsync("CallStateChanged", phoneId, manager.CurrentState.ToString());   // Idle | Dialing | Ringing | InCall
/// if (state == Ringing &amp;&amp; IncomingPhoneNumber != null)
///   SendAsync("IncomingCall", phoneId, manager.IncomingPhoneNumber);         // the number may be "Unknown"
/// </code>
/// <para>
/// ⚠ <b>Until PHN-12 this client bound <c>CallStateChanged</c> as <c>(state, phoneNumber)</c></b> — the
/// phone id was parsed as the state, which no switch arm matches, so every event read as <c>Idle</c>, the
/// API never saw <c>Ringing</c>, and the incoming-call announcement never ran on the box. It also
/// registered a three-argument <c>CallStateChanged</c> nobody sends, and never subscribed to
/// <c>IncomingCall</c>, the only event that carries the number. <c>PhoneCallClientContractTests</c>
/// (Radio.API.Tests) now drives this client over a real SignalR connection with exactly those two sends.
/// </para>
/// </remarks>
public class PhoneCallClient : IPhoneIntegrationService
{
  private readonly ILogger<PhoneCallClient> _logger;
  private readonly IOptionsMonitor<PhoneIntegrationOptions> _options;
  private readonly Action<HttpConnectionOptions>? _configureConnection;
  private readonly object _gate = new();
  private HubConnection? _hubConnection;
  private bool _isDisposed;
  private PhoneCallState _currentState = PhoneCallState.Idle;
  private string? _callerNumber;
  private string? _callerName;

  // The number this ring was last raised for, so a re-sent IncomingCall with the same number does not
  // restart the announcement (PhoneCallIntegrationService treats every Ringing as a new call). Cleared on
  // any other state.
  private string? _ringRaisedFor;

  public PhoneCallClient(
    ILogger<PhoneCallClient> logger,
    IOptionsMonitor<PhoneIntegrationOptions> options)
    : this(logger, options, configureConnection: null)
  {
  }

  /// <summary>
  /// Test seam: <paramref name="configureConnection"/> lets a test point the connection at an in-process
  /// server (<c>TestServer</c>). Production uses the public constructor.
  /// </summary>
  internal PhoneCallClient(
    ILogger<PhoneCallClient> logger,
    IOptionsMonitor<PhoneIntegrationOptions> options,
    Action<HttpConnectionOptions>? configureConnection)
  {
    _logger = logger;
    _options = options;
    _configureConnection = configureConnection;
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
      .WithUrl(opts.HubUrl, o => _configureConnection?.Invoke(o))
      .WithAutomaticReconnect(new PhoneRetryPolicy(opts))
      .ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning))
      .Build();

    // RotaryPhone's contract — see the class remarks. One registration per method, in the server's
    // argument order. (A second, three-argument CallStateChanged used to be registered "for resilience";
    // nothing sends it.)
    _hubConnection.On<string, string>("CallStateChanged", OnHubCallStateChanged);
    _hubConnection.On<string, string>("IncomingCall", OnHubIncomingCall);

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

  /// <inheritdoc />
  public async Task ReportCallerResolvedAsync(string phoneNumber, string resolvedName, CancellationToken ct = default)
  {
    if (_hubConnection is not null && _hubConnection.State == HubConnectionState.Connected)
    {
      await _hubConnection.SendAsync("ReportCallerResolved", phoneNumber, resolvedName, ct);
    }
  }

  /// <summary>
  /// The hub's <c>CallStateChanged(phoneId, state)</c>. <c>internal</c> so the tests drive the method the
  /// live registration is wired to, not a copy of it.
  /// </summary>
  /// <remarks>
  /// <c>Ringing</c> is NOT raised from here: it carries no number, and RotaryPhone sends the number in the
  /// <c>IncomingCall</c> that follows immediately — every inbound path it has sets the number before it
  /// sets <c>Ringing</c> (<c>CallManager.SimulateIncomingCall</c> uses <c>"Unknown"</c>). Raising here as
  /// well would announce "Unknown caller" and then cut it off with the real name a few milliseconds later,
  /// since <c>PhoneCallIntegrationService</c> treats every <c>Ringing</c> as a new call.
  /// </remarks>
  internal void OnHubCallStateChanged(string phoneId, string state)
  {
    var parsedState = ParseCallState(state);
    if (parsedState == PhoneCallState.Ringing)
    {
      lock (_gate)
      {
        if (_currentState != PhoneCallState.Ringing)
        {
          // A new ring: whatever number the last one was raised for no longer counts.
          _ringRaisedFor = null;
        }
        _currentState = PhoneCallState.Ringing;
      }
      _logger.LogDebug("Phone {PhoneId} ringing; the number follows in IncomingCall", phoneId);
      return;
    }

    lock (_gate)
    {
      _currentState = parsedState;
      _callerNumber = null;
      _callerName = null;
      _ringRaisedFor = null;
    }

    _logger.LogInformation("Phone call state: {State}", parsedState);
    CallStateChanged?.Invoke(this, new PhoneCallStateChangedEventArgs { State = parsedState });
  }

  /// <summary>
  /// The hub's <c>IncomingCall(phoneId, number)</c>: raises <see cref="PhoneCallState.Ringing"/> with the
  /// number, once per number per ring. A caller-ID update (RotaryPhone re-sends it when the real number
  /// replaces <c>"Unknown"</c>) raises again, which restarts the announcement with the better name.
  /// </summary>
  internal void OnHubIncomingCall(string phoneId, string phoneNumber)
  {
    lock (_gate)
    {
      if (_currentState == PhoneCallState.Ringing && string.Equals(_ringRaisedFor, phoneNumber, StringComparison.Ordinal))
      {
        return;
      }
      _currentState = PhoneCallState.Ringing;
      _callerNumber = phoneNumber;
      _callerName = null;
      _ringRaisedFor = phoneNumber;
    }

    _logger.LogInformation("Phone call state: {State}, Number: {Number}",
      PhoneCallState.Ringing, LogSafeText.ForPhone(phoneNumber));

    CallStateChanged?.Invoke(this, new PhoneCallStateChangedEventArgs
    {
      State = PhoneCallState.Ringing,
      PhoneNumber = phoneNumber,
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
    {
      _logger.LogWarning(error, "RotaryPhone hub connection closed with error");
    }
    else
    {
      _logger.LogInformation("RotaryPhone hub connection closed");
    }

    return Task.CompletedTask;
  }

  /// <inheritdoc />
  public async ValueTask DisposeAsync()
  {
    if (_isDisposed)
    {
      return;
    }
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

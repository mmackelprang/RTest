using Microsoft.AspNetCore.SignalR.Client;
using Radio.Core.Utilities;

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

  // GV (gvbridge) push — rides the existing /hub RotaryHub (ADR-022 D5).
  // NOTE: "GvSmsReceived" deliberately differs from GvTrunkHubService.SmsReceived
  // (/hubs/gvtrunk, different payload). Do not rename to plain SmsReceived.
  public event Action<Radio.Web.Models.SmsMessageDto>? GvSmsReceived;
  public event Action<Radio.Web.Models.VoicemailItemDto>? GvVoicemailReceived;

  /// <summary>
  /// Fired when read-state changes from ANY source (ADR-024 §4). Path (a): our own
  /// marks (ships with RotaryPhone's routes). Path (b): externally-originated flips —
  /// phone/GV-web reads — once RotaryPhone's poller-flip fast-follow lands (same event,
  /// no change here). Consumers MUST de-dupe by (id-or-threadId + isRead); RotaryPhone
  /// broadcasts unconditionally, including back to the originator.
  /// </summary>
  public event Action<Radio.Web.Models.ReadStateChangedDto>? ReadStateChanged;

  public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

  private readonly IHubConnectionTransport? _transport;

  public PhoneHubService(
    ILogger<PhoneHubService> logger,
    IConfiguration configuration,
    IHubConnectionTransport? transport = null)
  {
    _logger = logger;
    _configuration = configuration;
    _transport = transport;
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

      var hubUrl = _configuration.GetValue<string>("RotaryPhone:HubUrl") ?? "http://radio:5004/hub";

      _hubConnection = new HubConnectionBuilder()
        .WithUrl(hubUrl, options => _transport?.Configure(options))
        .WithAutomaticReconnect(new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5),
          TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) })
        .Build();

      _hubConnection.On<string, string>("CallStateChanged", (phoneId, state) =>
      {
        _logger.LogDebug("Phone call state changed: {PhoneId} → {State}", phoneId, state);
        CallStateChanged?.Invoke(phoneId, state);
      });

      // PHN-5 (P8): the masking lives in RaiseIncomingCallForTest so the live handler + the unit
      // test share one source of truth, the same arrangement as ReadStateChanged below.
      _hubConnection.On<string, string>("IncomingCall", RaiseIncomingCallForTest);

      _hubConnection.On("CallHistoryUpdated", () =>
      {
        CallHistoryUpdated?.Invoke();
      });

      _hubConnection.On<object>("SystemStatusChanged", (status) =>
      {
        SystemStatusChanged?.Invoke(status);
      });

      _hubConnection.On<Radio.Web.Models.SmsMessageDto>("SmsReceived", m =>
      {
        _logger.LogDebug("GV SMS received on thread {ThreadId}", m.ThreadId);
        GvSmsReceived?.Invoke(m);
      });

      _hubConnection.On<Radio.Web.Models.VoicemailItemDto>("VoicemailReceived", v =>
      {
        _logger.LogDebug("GV voicemail received {Id}", v.Id);
        GvVoicemailReceived?.Invoke(v);
      });

      // GV-4 (ADR-024 §4): unified read-state change. Same /hub connection; defensive
      // Kind guard lives in RaiseReadStateChangedForTest so the live handler + the unit
      // test share one source of truth. Unknown Kind is ignored at Debug, never throws.
      _hubConnection.On<Radio.Web.Models.ReadStateChangedDto>("ReadStateChanged",
        RaiseReadStateChangedForTest);

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

  /// <summary>
  /// Masks the number for the log and raises <see cref="IncomingCall"/>. The live /hub
  /// <c>.On&lt;string, string&gt;("IncomingCall", …)</c> handler is wired directly to this method,
  /// so the masking is the single source of truth and a test that drives it drives production —
  /// the same seam, and the same reason, as <see cref="RaiseReadStateChangedForTest"/>.
  /// </summary>
  /// <remarks>
  /// ⚠ <paramref name="phoneNumber"/> is still passed RAW to <see cref="IncomingCall"/>
  /// subscribers, and that is correct: the UI displays it. PHN-5 masks what is written to a sink
  /// that persists, not what is handed to the caller. This line was the highest-value one in that
  /// row — Radio.Web's Console sink carries no <c>restrictedToMinimumLevel</c>, so an Information
  /// line here reaches <c>journalctl -u radio-web</c> on every incoming call, on a stock box.
  /// </remarks>
  internal void RaiseIncomingCallForTest(string phoneId, string phoneNumber)
  {
    _logger.LogInformation("Incoming call from {PhoneNumber}", LogSafeText.ForPhone(phoneNumber));
    IncomingCall?.Invoke(phoneId, phoneNumber);
  }

  /// <summary>
  /// Applies the defensive Kind guard (ADR-024 §4.2) and raises <see cref="ReadStateChanged"/>.
  /// The live /hub `.On&lt;ReadStateChangedDto&gt;("ReadStateChanged", …)` handler is wired
  /// directly to this method so the guard is the single source of truth; the unit tests
  /// invoke it to exercise both branches without a live connection. Only "Voicemail"/"Sms"
  /// (case-insensitive) are known — anything else is ignored at Debug, never thrown.
  /// </summary>
  internal void RaiseReadStateChangedForTest(Radio.Web.Models.ReadStateChangedDto dto)
  {
    if (dto is null ||
        (!string.Equals(dto.Kind, "Voicemail", StringComparison.OrdinalIgnoreCase) &&
         !string.Equals(dto.Kind, "Sms", StringComparison.OrdinalIgnoreCase)))
    {
      _logger.LogDebug("Ignoring ReadStateChanged with unknown Kind '{Kind}'", dto?.Kind);
      return;
    }
    ReadStateChanged?.Invoke(dto);
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

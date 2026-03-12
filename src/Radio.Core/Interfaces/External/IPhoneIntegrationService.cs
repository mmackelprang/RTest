namespace Radio.Core.Interfaces.External;

/// <summary>
/// Service that connects to the RotaryPhone SignalR hub and reports call state changes.
/// </summary>
public interface IPhoneIntegrationService : IAsyncDisposable
{
  /// <summary>Current phone call state.</summary>
  PhoneCallState CurrentState { get; }

  /// <summary>Caller phone number for the current/last call (null if unknown).</summary>
  string? CallerNumber { get; }

  /// <summary>Caller display name for the current/last call (null if unknown).</summary>
  string? CallerName { get; }

  /// <summary>Whether the SignalR connection to the phone server is active.</summary>
  bool IsConnected { get; }

  /// <summary>Start the SignalR connection to the RotaryPhone hub.</summary>
  Task StartAsync(CancellationToken cancellationToken = default);

  /// <summary>Stop the SignalR connection.</summary>
  Task StopAsync(CancellationToken cancellationToken = default);

  /// <summary>Send resolved caller name back to RotaryPhone for UI/logging.</summary>
  Task ReportCallerResolvedAsync(string phoneNumber, string resolvedName, CancellationToken ct = default);

  /// <summary>Fired when the phone call state changes.</summary>
  event EventHandler<PhoneCallStateChangedEventArgs>? CallStateChanged;
}

/// <summary>
/// Phone call states reported by the RotaryPhone server.
/// </summary>
public enum PhoneCallState
{
  /// <summary>No active call.</summary>
  Idle,

  /// <summary>Incoming call ringing.</summary>
  Ringing,

  /// <summary>Call in progress (answered).</summary>
  InCall,

  /// <summary>Call ended.</summary>
  Ended
}

/// <summary>
/// Event args for phone call state changes.
/// </summary>
public class PhoneCallStateChangedEventArgs : EventArgs
{
  /// <summary>The new call state.</summary>
  public PhoneCallState State { get; init; }

  /// <summary>Caller phone number (null if unknown).</summary>
  public string? PhoneNumber { get; init; }

  /// <summary>Caller display name (null if not resolved).</summary>
  public string? CallerName { get; init; }
}

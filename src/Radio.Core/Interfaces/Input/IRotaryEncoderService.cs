using Radio.Core.Configuration;
namespace Radio.Core.Interfaces.Input;

/// <summary>
/// Service that reads rotary encoder events from USB HID hardware.
/// Fires events for encoder rotation and button presses.
/// </summary>
public interface IRotaryEncoderService : IDisposable
{
  /// <summary>Whether the encoder device is currently connected.</summary>
  bool IsConnected { get; }

  /// <summary>
  /// How far the last configuration push got on the current connection (ENC-11).
  ///
  /// <para>
  /// Consumers must treat anything other than <see cref="RotaryEncoderConfigStatus.Configured"/> as
  /// "the device may still be on factory defaults" — where one detent is worth 100 volume points —
  /// and clamp accordingly rather than trusting the movement they are handed.
  /// </para>
  /// </summary>
  RotaryEncoderConfigStatus ConfigStatus { get; }

  /// <summary>Start reading from the HID device.</summary>
  Task StartAsync(CancellationToken cancellationToken = default);

  /// <summary>Stop reading from the HID device.</summary>
  Task StopAsync(CancellationToken cancellationToken = default);

  /// <summary>Fired when an encoder is turned.</summary>
  event EventHandler<EncoderTurnedEventArgs>? EncoderTurned;

  /// <summary>Fired when an encoder button is pressed or released.</summary>
  event EventHandler<EncoderButtonEventArgs>? ButtonPressed;

  /// <summary>Fired when the HID device connects or disconnects.</summary>
  event EventHandler<EncoderConnectionEventArgs>? ConnectionChanged;

  /// <summary>
  /// Fired when <see cref="ConfigStatus"/> changes value (ENC-12).
  ///
  /// <para>
  /// <b>On change only.</b> The push loop assigns this property once per attempt and may assign the
  /// same value repeatedly — <c>Transient</c> on attempts 1 and 2 is the ordinary case. Broadcasting
  /// every assignment would put SignalR traffic on the wire for a state that did not change, on a box
  /// where incidental load correlates with audible audio distortion.
  /// </para>
  /// </summary>
  event EventHandler<EncoderConfigStatusEventArgs>? ConfigStatusChanged;
}

/// <summary>
/// Event args for encoder rotation.
/// </summary>
public class EncoderTurnedEventArgs : EventArgs
{
  /// <summary>Encoder index (0-3).</summary>
  public int EncoderIndex { get; init; }

  /// <summary>Rotation delta (positive = clockwise, negative = counter-clockwise).</summary>
  public int Delta { get; init; }
}

/// <summary>
/// Event args for encoder button press/release.
/// </summary>
public class EncoderButtonEventArgs : EventArgs
{
  /// <summary>Encoder index (0-3).</summary>
  public int EncoderIndex { get; init; }

  /// <summary>True if the button is pressed, false if released.</summary>
  public bool IsPressed { get; init; }
}

/// <summary>
/// Event args for encoder device connection state change.
/// </summary>
public class EncoderConnectionEventArgs : EventArgs
{
  /// <summary>True if device connected, false if disconnected.</summary>
  public bool IsConnected { get; init; }

  /// <summary>
  /// True when the device has connected at least once during this process's lifetime.
  ///
  /// <para>
  /// ENC-0. The notification policy is asymmetric and cannot be applied without this. Absent at boot
  /// gets a badge and no toast — the owner is most likely standing at the cabinet having just
  /// installed or unplugged something. Disappearing mid-session gets a toast, because it is genuinely
  /// surprising and may land mid-interaction. Those are the same <see cref="IsConnected"/> value and
  /// they are not the same event.
  /// </para>
  /// </summary>
  public bool WasEverConnected { get; init; }
}

/// <summary>
/// Event args for a configuration-tier change (ENC-12).
/// </summary>
public class EncoderConfigStatusEventArgs : EventArgs
{
  /// <summary>The tier the device is now in.</summary>
  public RotaryEncoderConfigStatus Status { get; init; }

  /// <summary>The tier it was in immediately before. Never equal to <see cref="Status"/>.</summary>
  public RotaryEncoderConfigStatus PreviousStatus { get; init; }
}

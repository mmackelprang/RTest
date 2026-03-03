namespace Radio.Core.Interfaces.Input;

/// <summary>
/// Service that reads rotary encoder events from USB HID hardware.
/// Fires events for encoder rotation and button presses.
/// </summary>
public interface IRotaryEncoderService : IDisposable
{
  /// <summary>Whether the encoder device is currently connected.</summary>
  bool IsConnected { get; }

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
}

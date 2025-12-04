namespace Radio.Core.Models.Audio;

/// <summary>
/// Event arguments for radio state change events.
/// </summary>
public class RadioStateChangedEventArgs : EventArgs
{
  /// <summary>
  /// Gets the current frequency in MHz (for FM) or kHz (for AM).
  /// </summary>
  public double Frequency { get; init; }

  /// <summary>
  /// Gets the current radio band (AM or FM).
  /// </summary>
  public RadioBand Band { get; init; }

  /// <summary>
  /// Gets the signal strength as a percentage (0-100).
  /// </summary>
  public int SignalStrength { get; init; }

  /// <summary>
  /// Gets a value indicating whether the radio is receiving a stereo signal (FM only).
  /// </summary>
  public bool IsStereo { get; init; }
}

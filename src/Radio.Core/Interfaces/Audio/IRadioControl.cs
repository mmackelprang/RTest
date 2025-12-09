using Radio.Core.Models.Audio;

namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Unified interface for controlling radio receiver operations.
/// Combines functionality from both RTL-SDR and Raddy RF320 radio devices.
/// </summary>
public interface IRadioControl
{
  #region Lifecycle

  /// <summary>
  /// Starts the radio receiver and begins audio processing.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the asynchronous operation.</returns>
  Task<bool> StartupAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Stops the radio receiver and cleanly shuts down all audio/radio processes.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the asynchronous operation.</returns>
  Task ShutdownAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Gets whether the receiver is currently running.
  /// </summary>
  bool IsRunning { get; }

  #endregion

  #region Frequency Control

  /// <summary>
  /// Gets the current tuned frequency.
  /// The frequency is always stored in Hertz (Hz) internally to avoid unit ambiguity.
  /// Use <see cref="Frequency.Megahertz"/> or <see cref="Frequency.Kilohertz"/> properties for unit conversion.
  /// </summary>
  Frequency CurrentFrequency { get; }

  /// <summary>
  /// Sets the radio frequency to a specific value.
  /// </summary>
  /// <param name="frequency">The frequency to tune to. Use <see cref="Frequency.FromMegahertz"/> or <see cref="Frequency.FromKilohertz"/> to create from common units.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the asynchronous operation.</returns>
  /// <exception cref="ArgumentOutOfRangeException">Thrown when the frequency is outside the valid range for the current band.</exception>
  Task SetFrequencyAsync(Frequency frequency, CancellationToken cancellationToken = default);

  /// <summary>
  /// Steps the radio frequency up by one frequency step.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the asynchronous operation.</returns>
  Task StepFrequencyUpAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Steps the radio frequency down by one frequency step.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the asynchronous operation.</returns>
  Task StepFrequencyDownAsync(CancellationToken cancellationToken = default);

  #endregion

  #region Scanning

  /// <summary>
  /// Starts scanning for stations in the specified direction.
  /// Scanning will continue until a strong signal is found or <see cref="StopScanAsync"/> is called.
  /// </summary>
  /// <param name="direction">The direction to scan (up or down).</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the asynchronous operation.</returns>
  Task StartScanAsync(ScanDirection direction, CancellationToken cancellationToken = default);

  /// <summary>
  /// Stops the current scanning operation.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the asynchronous operation.</returns>
  Task StopScanAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Gets whether a scan operation is currently in progress.
  /// </summary>
  bool IsScanning { get; }

  /// <summary>
  /// Gets the current scan direction if scanning is active; otherwise, null.
  /// </summary>
  ScanDirection? ScanDirection { get; }

  #endregion

  #region Band and Modulation

  /// <summary>
  /// Gets the current radio band (AM, FM, etc.).
  /// </summary>
  RadioBand CurrentBand { get; }

  /// <summary>
  /// Sets the radio band (AM, FM, WB, VHF, SW).
  /// </summary>
  /// <param name="band">The band to switch to.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the asynchronous operation.</returns>
  Task SetBandAsync(RadioBand band, CancellationToken cancellationToken = default);

  /// <summary>
  /// Gets the frequency step size used for tuning up/down.
  /// The step is stored in Hertz (Hz) to avoid unit ambiguity.
  /// </summary>
  Frequency FrequencyStep { get; }

  /// <summary>
  /// Sets the frequency step size for tuning up/down.
  /// </summary>
  /// <param name="step">The step size. Use <see cref="Frequency.FromMegahertz"/> or <see cref="Frequency.FromKilohertz"/> to create from common units.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the asynchronous operation.</returns>
  /// <exception cref="ArgumentOutOfRangeException">Thrown when the step size is invalid.</exception>
  Task SetFrequencyStepAsync(Frequency step, CancellationToken cancellationToken = default);

  #endregion

  #region Audio Control

  /// <summary>
  /// Gets or sets the volume level (0.0 to 1.0).
  /// This is the device-specific volume, separate from master volume.
  /// <para>
  /// <b>Synchronization contract:</b> This property and <see cref="DeviceVolume"/> are alternative representations of the same value.
  /// Implementers MUST keep these properties synchronized. Setting either property MUST update the other accordingly.
  /// </para>
  /// <para>
  /// <b>Conversion:</b> <c>DeviceVolume = (int)Math.Round(Volume * 100)</c> and <c>Volume = DeviceVolume / 100.0f</c>.
  /// </para>
  /// </summary>
  float Volume { get; set; }

  /// <summary>
  /// Gets or sets the device-specific volume level (0-100).
  /// This is an alternative representation of <see cref="Volume"/> for UI compatibility.
  /// <para>
  /// <b>Synchronization contract:</b> This property and <see cref="Volume"/> are alternative representations of the same value.
  /// Implementers MUST keep these properties synchronized. Setting either property MUST update the other accordingly.
  /// </para>
  /// <para>
  /// <b>Conversion:</b> <c>DeviceVolume = (int)Math.Round(Volume * 100)</c> and <c>Volume = DeviceVolume / 100.0f</c>.
  /// </para>
  /// </summary>
  int DeviceVolume { get; set; }

  /// <summary>
  /// Gets or sets whether the receiver is muted.
  /// </summary>
  bool IsMuted { get; set; }

  /// <summary>
  /// Gets or sets the squelch threshold (0.0 to 1.0).
  /// Squelch mutes audio when signal strength is below this threshold.
  /// </summary>
  float SquelchThreshold { get; set; }

  #endregion

  #region Equalizer

  /// <summary>
  /// Gets the current equalizer mode applied to the radio device.
  /// </summary>
  RadioEqualizerMode EqualizerMode { get; }

  /// <summary>
  /// Sets the equalizer mode for the radio device.
  /// </summary>
  /// <param name="mode">The equalizer mode to apply.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the asynchronous operation.</returns>
  Task SetEqualizerModeAsync(RadioEqualizerMode mode, CancellationToken cancellationToken = default);

  #endregion

  #region Gain Control

  /// <summary>
  /// Gets or sets whether automatic gain control is enabled.
  /// </summary>
  bool AutoGainEnabled { get; set; }

  /// <summary>
  /// Gets or sets the manual gain value in dB (only effective when AutoGainEnabled is false).
  /// </summary>
  float Gain { get; set; }

  #endregion

  #region State and Signal

  /// <summary>
  /// Gets the current signal strength as a percentage (0-100).
  /// </summary>
  int SignalStrength { get; }

  /// <summary>
  /// Gets a value indicating whether the radio is receiving a stereo signal (FM only).
  /// </summary>
  bool IsStereo { get; }

  /// <summary>
  /// Gets or sets the power state of the radio device (for devices that support power control).
  /// </summary>
  Task<bool> GetPowerStateAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Toggles the power state of the radio device (for devices that support power control).
  /// </summary>
  Task TogglePowerStateAsync(CancellationToken cancellationToken = default);

  #endregion

  #region Events

  /// <summary>
  /// Occurs when any radio state property changes (frequency, band, signal strength, stereo status).
  /// </summary>
  event EventHandler<RadioStateChangedEventArgs>? StateChanged;

  /// <summary>
  /// Event raised when frequency changes.
  /// </summary>
  event EventHandler<RadioControlFrequencyChangedEventArgs>? FrequencyChanged;

  /// <summary>
  /// Event raised when signal strength is updated.
  /// </summary>
  event EventHandler<RadioControlSignalStrengthEventArgs>? SignalStrengthUpdated;

  #endregion
}

/// <summary>
/// Event arguments for radio frequency changes.
/// Named to avoid conflicts with RTLSDRCore.FrequencyChangedEventArgs.
/// </summary>
public class RadioControlFrequencyChangedEventArgs : EventArgs
{
  /// <summary>
  /// Gets the previous frequency.
  /// </summary>
  public Frequency OldFrequency { get; }

  /// <summary>
  /// Gets the new frequency.
  /// </summary>
  public Frequency NewFrequency { get; }

  /// <summary>
  /// Creates new frequency changed event args.
  /// </summary>
  /// <param name="oldFrequency">Previous frequency.</param>
  /// <param name="newFrequency">New frequency.</param>
  public RadioControlFrequencyChangedEventArgs(Frequency oldFrequency, Frequency newFrequency)
  {
    OldFrequency = oldFrequency;
    NewFrequency = newFrequency;
  }
}

/// <summary>
/// Event arguments for radio signal strength updates.
/// Named to avoid conflicts with RTLSDRCore.SignalStrengthEventArgs.
/// </summary>
public class RadioControlSignalStrengthEventArgs : EventArgs
{
  /// <summary>
  /// Gets the signal strength (0.0 to 1.0).
  /// </summary>
  public float SignalStrength { get; }

  /// <summary>
  /// Creates new signal strength event args.
  /// </summary>
  /// <param name="signalStrength">Signal strength value.</param>
  public RadioControlSignalStrengthEventArgs(float signalStrength)
  {
    SignalStrength = signalStrength;
  }
}

using Radio.Core.Models.Audio;

namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Interface for radio-specific controls including frequency tuning, band selection, scanning, and equalization.
/// </summary>
public interface IRadioControls
{
  /// <summary>
  /// Gets the current tuned frequency in MHz (for FM) or kHz (for AM).
  /// </summary>
  double CurrentFrequency { get; }

  /// <summary>
  /// Gets the current radio band (AM or FM).
  /// </summary>
  RadioBand CurrentBand { get; }

  /// <summary>
  /// Gets the frequency step size used for tuning up/down in MHz (FM) or kHz (AM).
  /// </summary>
  double FrequencyStep { get; }

  /// <summary>
  /// Gets the current signal strength as a percentage (0-100).
  /// </summary>
  int SignalStrength { get; }

  /// <summary>
  /// Gets a value indicating whether the radio is receiving a stereo signal (FM only).
  /// </summary>
  bool IsStereo { get; }

  /// <summary>
  /// Gets the current equalizer mode applied to the radio device.
  /// </summary>
  RadioEqualizerMode EqualizerMode { get; }

  /// <summary>
  /// Gets or sets the device-specific volume level (0-100).
  /// This is separate from the master volume and affects only the radio device output.
  /// </summary>
  int DeviceVolume { get; set; }

  /// <summary>
  /// Gets a value indicating whether the radio is currently scanning for stations.
  /// </summary>
  bool IsScanning { get; }

  /// <summary>
  /// Gets the current scan direction if scanning is active; otherwise, null.
  /// </summary>
  ScanDirection? ScanDirection { get; }

  /// <summary>
  /// Sets the radio frequency to a specific value.
  /// </summary>
  /// <param name="frequency">The frequency to tune to in MHz (FM) or kHz (AM).</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>A task representing the asynchronous operation.</returns>
  /// <exception cref="ArgumentOutOfRangeException">Thrown when the frequency is outside the valid range for the current band.</exception>
  Task SetFrequencyAsync(double frequency, CancellationToken ct = default);

  /// <summary>
  /// Steps the radio frequency up by one frequency step.
  /// </summary>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>A task representing the asynchronous operation.</returns>
  Task StepFrequencyUpAsync(CancellationToken ct = default);

  /// <summary>
  /// Steps the radio frequency down by one frequency step.
  /// </summary>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>A task representing the asynchronous operation.</returns>
  Task StepFrequencyDownAsync(CancellationToken ct = default);

  /// <summary>
  /// Sets the radio band (AM or FM).
  /// </summary>
  /// <param name="band">The band to switch to.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>A task representing the asynchronous operation.</returns>
  Task SetBandAsync(RadioBand band, CancellationToken ct = default);

  /// <summary>
  /// Sets the frequency step size for tuning up/down.
  /// </summary>
  /// <param name="step">The step size in MHz (FM) or kHz (AM).</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>A task representing the asynchronous operation.</returns>
  /// <exception cref="ArgumentOutOfRangeException">Thrown when the step size is invalid.</exception>
  Task SetFrequencyStepAsync(double step, CancellationToken ct = default);

  /// <summary>
  /// Sets the equalizer mode for the radio device.
  /// </summary>
  /// <param name="mode">The equalizer mode to apply.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>A task representing the asynchronous operation.</returns>
  Task SetEqualizerModeAsync(RadioEqualizerMode mode, CancellationToken ct = default);

  /// <summary>
  /// Starts scanning for stations in the specified direction.
  /// Scanning will continue until a strong signal is found or <see cref="StopScanAsync"/> is called.
  /// </summary>
  /// <param name="direction">The direction to scan (up or down).</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>A task representing the asynchronous operation.</returns>
  Task StartScanAsync(ScanDirection direction, CancellationToken ct = default);

  /// <summary>
  /// Stops the current scanning operation.
  /// </summary>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>A task representing the asynchronous operation.</returns>
  Task StopScanAsync(CancellationToken ct = default);

  Task<bool> GetPowerStateAsync(CancellationToken ct = default);
  Task TogglePowerStateAsync(CancellationToken ct = default);

  /// <summary>
  /// Occurs when any radio state property changes (frequency, band, signal strength, stereo status).
  /// </summary>
  event EventHandler<RadioStateChangedEventArgs>? StateChanged;
}

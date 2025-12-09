namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Factory for creating radio audio sources based on device type.
/// Supports runtime device selection and availability checking.
/// </summary>
public interface IRadioFactory
{
  /// <summary>
  /// Creates a radio audio source for the specified device type.
  /// </summary>
  /// <param name="deviceType">The type of radio device (e.g., "RTLSDRCore", "RF320").</param>
  /// <returns>A primary audio source implementing IRadioControl.</returns>
  /// <exception cref="ArgumentException">Thrown when the device type is not supported.</exception>
  /// <exception cref="InvalidOperationException">Thrown when the device is not available.</exception>
  IPrimaryAudioSource CreateRadioSource(string deviceType);

  /// <summary>
  /// Gets the list of available radio device types that can be created.
  /// </summary>
  /// <returns>Collection of device type identifiers.</returns>
  IEnumerable<string> GetAvailableDeviceTypes();

  /// <summary>
  /// Gets the default radio device type from configuration.
  /// </summary>
  /// <returns>The default device type identifier.</returns>
  string GetDefaultDeviceType();

  /// <summary>
  /// Checks if a specific radio device type is available and can be created.
  /// </summary>
  /// <param name="deviceType">The device type to check.</param>
  /// <returns>True if the device type is available; otherwise, false.</returns>
  bool IsDeviceAvailable(string deviceType);
}

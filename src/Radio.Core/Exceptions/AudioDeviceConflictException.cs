namespace Radio.Core.Exceptions;

/// <summary>
/// Exception thrown when there is a conflict with an audio device,
/// such as attempting to use a USB port that is already in use.
/// </summary>
public class AudioDeviceConflictException : Exception
{
  /// <summary>
  /// Gets the device ID involved in the conflict.
  /// </summary>
  public string? DeviceId { get; }

  /// <summary>
  /// Gets the ID of the source that is conflicting.
  /// </summary>
  public string? ConflictingSourceId { get; }

  /// <summary>
  /// Initializes a new instance of the <see cref="AudioDeviceConflictException"/> class.
  /// </summary>
  public AudioDeviceConflictException()
  {
  }

  /// <summary>
  /// Initializes a new instance of the <see cref="AudioDeviceConflictException"/> class
  /// with a specified error message.
  /// </summary>
  /// <param name="message">The error message.</param>
  public AudioDeviceConflictException(string message) : base(message)
  {
  }

  /// <summary>
  /// Initializes a new instance of the <see cref="AudioDeviceConflictException"/> class
  /// with a specified error message and inner exception.
  /// </summary>
  /// <param name="message">The error message.</param>
  /// <param name="innerException">The inner exception.</param>
  public AudioDeviceConflictException(string message, Exception innerException)
    : base(message, innerException)
  {
  }

  /// <summary>
  /// Initializes a new instance of the <see cref="AudioDeviceConflictException"/> class
  /// with detailed conflict information.
  /// </summary>
  /// <param name="message">The error message.</param>
  /// <param name="deviceId">The device ID involved in the conflict.</param>
  /// <param name="conflictingSourceId">The ID of the source that is conflicting.</param>
  public AudioDeviceConflictException(string message, string deviceId, string conflictingSourceId)
    : base(message)
  {
    DeviceId = deviceId;
    ConflictingSourceId = conflictingSourceId;
  }
}

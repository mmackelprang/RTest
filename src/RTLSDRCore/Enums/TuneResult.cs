namespace RTLSDRCore.Enums;

/// <summary>
/// Outcome of <see cref="RadioReceiver.TrySetFrequency"/> and <see cref="RadioReceiver.TrySetBand"/>
/// (AUD-99): it separates a frequency the receiver cannot take (the caller's mistake), a band the
/// device can never receive (a capability gap) and a device that refused a valid request (a fault).
/// </summary>
public enum TuneResult
{
  /// <summary>Tuned, or recorded for the next start when the device is closed.</summary>
  Success,

  /// <summary>The frequency is outside every preset band and outside what the device receives; nothing was changed.</summary>
  OutOfRange,

  /// <summary>The device refused the HF switch, the sample rate or the frequency, or streaming could not resume.</summary>
  Refused,

  /// <summary>
  /// The band needs HF direct sampling and the open device has none
  /// (<see cref="Hardware.ISdrDevice.SupportsHfDirectSampling"/> false). Permanent for this device
  /// and setting; nothing was changed.
  /// </summary>
  Unsupported,
}

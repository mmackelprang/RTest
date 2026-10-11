namespace RTLSDRCore.Enums;

/// <summary>
/// Outcome of <see cref="RadioReceiver.TrySetFrequency"/> (AUD-99): it separates a frequency the
/// receiver cannot take (the caller's mistake) from a device that refused a valid one (a fault).
/// </summary>
public enum TuneResult
{
  /// <summary>Tuned, or recorded for the next start when the device is closed.</summary>
  Success,

  /// <summary>The frequency is outside every preset band and outside what the device receives; nothing was changed.</summary>
  OutOfRange,

  /// <summary>The device refused the HF switch, the sample rate or the frequency, or streaming could not resume.</summary>
  Refused,
}

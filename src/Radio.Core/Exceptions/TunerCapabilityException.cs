namespace Radio.Core.Exceptions;

/// <summary>
/// Thrown when a band needs a receiver capability the device does not have: today, an AM or
/// Shortwave request on a device without HF direct sampling (AUD-99). A permanent gap for this
/// device and its <c>RTLSDR:HfDirectSampling</c> setting, not a transient fault, so the API maps it
/// to 409 rather than the 503 of a plain <see cref="TunerRefusedException"/>.
/// </summary>
/// <remarks>
/// Derives from <see cref="TunerRefusedException"/>, so a caller that handles any refusal (the
/// encoder's preset and band selectors) handles this one too.
/// </remarks>
public class TunerCapabilityException : TunerRefusedException
{
  /// <summary>Initializes a new instance with a message.</summary>
  /// <param name="message">What the device cannot do.</param>
  public TunerCapabilityException(string message)
    : base(message)
  {
  }

  /// <summary>Initializes a new instance with a message and the exception that caused it.</summary>
  /// <param name="message">What the device cannot do.</param>
  /// <param name="innerException">The cause.</param>
  public TunerCapabilityException(string message, Exception innerException)
    : base(message, innerException)
  {
  }
}

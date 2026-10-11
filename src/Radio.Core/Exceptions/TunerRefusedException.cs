namespace Radio.Core.Exceptions;

/// <summary>
/// Thrown when the radio hardware refused a tune, a band change or the mode switch a band needs
/// (AUD-99). A fault on the server side, not a bad request: the API maps it to 503, never to the
/// 400 an out-of-range frequency gets. Its subclass <see cref="TunerCapabilityException"/> (a band
/// the device can never receive) maps to 409 instead.
/// </summary>
/// <remarks>
/// Derives from <see cref="InvalidOperationException"/>, which <c>TuneInBandAsync</c> threw for a
/// receiver fault before this type existed, so a caller that catches that keeps working.
/// </remarks>
public class TunerRefusedException : InvalidOperationException
{
  /// <summary>Initializes a new instance with a message.</summary>
  /// <param name="message">What the tuner refused.</param>
  public TunerRefusedException(string message)
    : base(message)
  {
  }

  /// <summary>Initializes a new instance with a message and the exception that caused it.</summary>
  /// <param name="message">What the tuner refused.</param>
  /// <param name="innerException">The cause.</param>
  public TunerRefusedException(string message, Exception innerException)
    : base(message, innerException)
  {
  }
}

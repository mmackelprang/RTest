namespace Radio.Core.Exceptions;

/// <summary>
/// Thrown when a tuning step is not one of the current band's allowed steps (AUD-102), e.g. 7 Hz on
/// FM or 2.5 kHz on AM. A bad request: the API maps it to 400 with <see cref="AllowedStepsHz"/>.
/// </summary>
/// <remarks>
/// Derives from <see cref="ArgumentOutOfRangeException"/>, which <c>SetFrequencyStepAsync</c>
/// already threw for a step of zero or less, so a caller that catches that keeps working.
/// </remarks>
public class FrequencyStepNotAllowedException : ArgumentOutOfRangeException
{
  /// <summary>Initializes a new instance.</summary>
  /// <param name="paramName">The name of the step parameter.</param>
  /// <param name="stepHz">The refused step, in Hz.</param>
  /// <param name="bandName">The current band's name, e.g. <c>FM Broadcast</c>.</param>
  /// <param name="allowedStepsHz">The band's allowed steps, in Hz.</param>
  public FrequencyStepNotAllowedException(string paramName, long stepHz, string bandName, IReadOnlyList<long> allowedStepsHz)
    : base(paramName, stepHz,
      $"A {stepHz} Hz step is not allowed on the {bandName} band. Allowed steps (Hz): {string.Join(", ", allowedStepsHz)}")
  {
    StepHz = stepHz;
    BandName = bandName;
    AllowedStepsHz = allowedStepsHz;
  }

  /// <summary>The refused step, in Hz.</summary>
  public long StepHz { get; }

  /// <summary>The name of the band the step was refused on.</summary>
  public string BandName { get; }

  /// <summary>The band's allowed steps, in Hz.</summary>
  public IReadOnlyList<long> AllowedStepsHz { get; }
}

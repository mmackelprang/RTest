namespace Radio.Core.Configuration;

/// <summary>
/// The last frequency tuned on each band, so switching bands returns you where you were.
///
/// <para>
/// <b>Its own configuration section on purpose.</b> <c>RadioPreferences</c> and
/// <c>RadioOptions</c> both declare <c>SectionName = "Radio"</c>, and that section already has two
/// writers with different field sets — one persisting live state, one a three-field settings-page
/// DTO. A map added there would be dropped by whichever wrote last, silently, and the symptom would
/// be "the band memory works until you open Settings".
/// </para>
/// </summary>
public class RadioBandMemory
{
  public const string SectionName = "RadioBandMemory";

  /// <summary>
  /// Band name (<c>RadioBand.ToString()</c>) to last-tuned frequency in <b>hertz</b>.
  ///
  /// <para>
  /// Hertz because that is what <c>Frequency.Hertz</c> and the whole radio API carry. The older
  /// <c>RadioPreferences.LastFrequency</c> is a <c>double</c> whose doc comment says "MHz (for FM)
  /// or kHz (for AM)" while the code writing it stores hertz — do not copy that.
  /// </para>
  /// </summary>
  public Dictionary<string, long> LastFrequencyHzByBand { get; set; } = [];
}

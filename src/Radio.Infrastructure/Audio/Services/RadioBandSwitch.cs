using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;

namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// The one band change that uses band memory (AUD-105): remember where the dial is on the band
/// being left, then tune the new band at its remembered frequency (or its default). Used by
/// <c>POST /api/radio/band</c> (and so by the panel's band buttons) and by the encoder's SOURCE
/// selector, which until AUD-105 was the only caller that restored a band's frequency; the API left
/// the dial wherever <c>RadioReceiver.SetBand</c> clamped it, i.e. on a band edge (FM → AM gave
/// 1710 kHz, SW → FM gave 87.5 MHz).
/// </summary>
public static class RadioBandSwitch
{
  /// <summary>
  /// Changes <paramref name="radio"/> to <paramref name="band"/> at that band's frequency from
  /// <paramref name="memory"/>.
  /// </summary>
  /// <remarks>
  /// <para>
  /// <b>A running seek is stopped first</b>, as preset recall does, so the frequency remembered for
  /// the band being left is where the seek was, not a moment of it, and the seek does not carry on
  /// into the new band.
  /// </para>
  /// <para>
  /// <b>The outgoing band is remembered next.</b> The preferences loop records the dial only every
  /// 30 s, so without this a station tuned less than 30 s before a band change would be lost: the
  /// return trip would restore the previous one. Re-selecting the current band therefore restores
  /// the frequency it is on, not an older one. (The real memory service ignores a frequency outside
  /// the band, e.g. a custom band's, which the source reports as FM.)
  /// </para>
  /// <para>
  /// <b>One tune, in the band.</b> With a frequency to restore, the change is one
  /// <see cref="IRadioControl.TuneInBandAsync"/>: one receiver band switch, and the band is the one
  /// asked for, never inferred from the frequency. VHF (30–300 MHz) contains FM, AIR and WB, and
  /// the bottom edges AIR and VHF fall back to (108 and 30 MHz) are also FM's top and SW's top, so
  /// <see cref="IRadioControl.SetFrequencyAsync"/> would land on the wrong band (found in the
  /// AUD-105 box UAT and by its review). With nothing to restore, or a frequency outside the band
  /// (a configured default is not range-checked by the memory service), it is
  /// <see cref="IRadioControl.SetBandAsync"/>, i.e. the receiver's clamp.
  /// </para>
  /// <para>
  /// <b>The band is read back afterwards.</b> Neither call promises that the band changed (a tuner
  /// may complete without switching), so success is what the tuner reports, not the absence of an
  /// exception.
  /// </para>
  /// <para>
  /// Exceptions from the tuner (<c>TunerRefusedException</c>, <c>TunerCapabilityException</c>)
  /// propagate to the caller, which maps them. A failed memory read or write does not propagate (the
  /// real service logs it and carries on).
  /// </para>
  /// </remarks>
  /// <param name="radio">The tuner.</param>
  /// <param name="memory">The band memory.</param>
  /// <param name="band">The band to change to.</param>
  /// <param name="logger">Receives a Warning when the tuner did not change band.</param>
  /// <param name="cancellationToken">Cancels the memory reads and writes and the tuner calls.</param>
  /// <returns>
  /// True when the tuner reports <paramref name="band"/> afterwards; false when it stayed on another
  /// band.
  /// </returns>
  public static async Task<bool> SwitchAsync(
    IRadioControl radio,
    IRadioBandMemory memory,
    RadioBand band,
    ILogger logger,
    CancellationToken cancellationToken = default)
  {
    if (radio.IsScanning)
    {
      await radio.StopScanAsync(cancellationToken);
    }

    await memory.SetAsync(radio.CurrentBand, radio.CurrentFrequency, cancellationToken);

    Frequency? restore = await memory.GetAsync(band, cancellationToken);
    bool tuned = false;
    if (restore is { } frequency)
    {
      try
      {
        await radio.TuneInBandAsync(band, frequency, cancellationToken);
        tuned = true;
      }
      catch (ArgumentOutOfRangeException)
      {
        // Thrown before anything is tuned (IRadioControl.TuneInBandAsync), so falling back is safe.
        logger.LogWarning("The {Band} frequency to restore, {Hz} Hz, is outside the band; using the band's clamp",
          band, frequency.Hertz);
      }
    }

    if (!tuned)
    {
      await radio.SetBandAsync(band, cancellationToken);
    }

    if (radio.CurrentBand != band)
    {
      logger.LogWarning("Tuner did not change to {Band}; it reports {Actual}", band, radio.CurrentBand);
      return false;
    }

    return true;
  }
}

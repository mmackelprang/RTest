using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;

namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// The one band change that uses band memory (AUD-105): remember where the dial is on the band
/// being left, change band, then tune the new band's remembered frequency (or its default). Used by
/// <c>POST /api/radio/band</c> (and so by the panel's band buttons) and by the encoder's SOURCE
/// selector, which until AUD-105 was the only caller that restored a band's frequency; the API left
/// the dial wherever <c>RadioReceiver.SetBand</c> clamped it, i.e. on a band edge (FM → AM gave
/// 1710 kHz, SW → FM gave 87.5 MHz).
/// </summary>
public static class RadioBandSwitch
{
  /// <summary>
  /// Changes <paramref name="radio"/> to <paramref name="band"/> and restores that band's frequency
  /// from <paramref name="memory"/>.
  /// </summary>
  /// <remarks>
  /// <para>
  /// <b>The outgoing band is remembered first.</b> The preferences loop records the dial only every
  /// 30 s, so without this a station tuned less than 30 s before a band change would be lost: the
  /// return trip would restore the previous one. Re-selecting the current band therefore restores
  /// the frequency it is on, not an older one.
  /// </para>
  /// <para>
  /// <b>The band is read back before anything is restored.</b>
  /// <see cref="IRadioControl.SetBandAsync"/> does not promise that the band changed (a tuner may
  /// complete the call without switching), and restoring the new band's frequency onto a tuner
  /// still on the old band would tune the wrong band.
  /// </para>
  /// <para>
  /// Exceptions from the tuner (for example <c>TunerRefusedException</c> from the band change or
  /// the restore) propagate to the caller, which maps them; a failed memory write does not
  /// (<see cref="IRadioBandMemory.SetAsync"/> logs and swallows it in the real service).
  /// </para>
  /// </remarks>
  /// <param name="radio">The tuner.</param>
  /// <param name="memory">The band memory.</param>
  /// <param name="band">The band to change to.</param>
  /// <param name="logger">Receives a Warning when the tuner did not change band.</param>
  /// <param name="cancellationToken">Cancels the memory reads and writes and the tuner calls.</param>
  /// <returns>
  /// True when the tuner reports <paramref name="band"/> afterwards; false when it stayed on another
  /// band (nothing was restored).
  /// </returns>
  public static async Task<bool> SwitchAsync(
    IRadioControl radio,
    IRadioBandMemory memory,
    RadioBand band,
    ILogger logger,
    CancellationToken cancellationToken = default)
  {
    await memory.SetAsync(radio.CurrentBand, radio.CurrentFrequency, cancellationToken);

    await radio.SetBandAsync(band, cancellationToken);

    if (radio.CurrentBand != band)
    {
      logger.LogWarning("Tuner did not change to {Band}; it reports {Actual}", band, radio.CurrentBand);
      return false;
    }

    Frequency? restore = await memory.GetAsync(band, cancellationToken);
    if (restore is { } frequency)
    {
      await radio.SetFrequencyAsync(frequency, cancellationToken);
    }

    return true;
  }
}

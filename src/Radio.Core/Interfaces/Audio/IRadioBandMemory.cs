using Radio.Core.Models.Audio;

namespace Radio.Core.Interfaces.Audio;

/// <summary>Remembers where the dial was left on each band.</summary>
public interface IRadioBandMemory
{
  /// <summary>
  /// The frequency to tune when entering <paramref name="band"/>, or null when there is nothing
  /// remembered and no default is known for it.
  /// </summary>
  Task<Frequency?> GetAsync(RadioBand band, CancellationToken cancellationToken = default);

  /// <summary>Records where the dial was left on <paramref name="band"/>.</summary>
  Task SetAsync(RadioBand band, Frequency frequency, CancellationToken cancellationToken = default);
}

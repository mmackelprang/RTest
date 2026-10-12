using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;

namespace Radio.Infrastructure.Services;

/// <summary>
/// The SDR device whose range decides what the console can receive and sweep: one choice shared by
/// the band list's receivable flag (<see cref="RadioBandService"/>, AUD-102) and the band map's
/// reason (<c>BandMapService</c>, AUD-103): the same rule picks the device for both. The band map reuses
/// a read for <c>BandMapService.ReceivingDeviceCacheDuration</c> (5 minutes) and the band list does not,
/// so for up to that long after a dongle swap or an <c>RTLSDR:HfDirectSampling</c> change the two can
/// answer from different reads.
/// </summary>
public static class SdrReceivingDevice
{
  /// <summary>
  /// The radio's own device when the radio source has one (<see cref="SdrHardwareInfo.IsInUse"/>),
  /// otherwise the first enumerated SDR, otherwise null. A null factory, or one that throws, counts
  /// as no device: the callers must still answer.
  /// </summary>
  /// <param name="radioFactory">The radio factory, or null.</param>
  public static SdrHardwareInfo? Find(IRadioFactory? radioFactory)
  {
    if (radioFactory == null)
    {
      return null;
    }

    try
    {
      IReadOnlyList<SdrHardwareInfo> hardware = radioFactory.GetSdrHardware() ?? Array.Empty<SdrHardwareInfo>();
      return hardware.FirstOrDefault(h => h.IsInUse) ?? hardware.FirstOrDefault();
    }
    catch (Exception)
    {
      return null;
    }
  }
}

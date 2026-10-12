using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RTLSDRCore.Enums;
using RTLSDRCore.Hardware;
using RTLSDRCore.Models;

namespace Radio.Infrastructure.Audio.Factories;

/// <summary>
/// Reads <c>RTLSDR:HfDirectSampling</c> (AUD-99) and creates RTL-SDR devices with it. Every place
/// that creates a device (the radio source and the idle band sweep) comes through here, so they
/// cannot make different HF decisions for the same dongle.
/// </summary>
/// <remarks>
/// Read through <see cref="IConfiguration"/>, so the config store (the SQLite bridge provider)
/// overrides <c>appsettings.json</c>, and read each time a device is created, so a change takes
/// effect the next time the radio starts or an idle sweep opens the dongle.
/// </remarks>
public static class SdrHfConfiguration
{
  /// <summary>
  /// The configured setting; <see cref="HfDirectSamplingSetting.Auto"/> when the key is absent, or
  /// when its value is not one of <c>Auto</c>, <c>Q</c>, <c>I</c>, <c>Off</c> (logged at Warning).
  /// </summary>
  /// <param name="configuration">Application configuration; null reads as absent.</param>
  /// <param name="logger">Receives the warning for an invalid value; may be null.</param>
  public static HfDirectSamplingSetting Read(IConfiguration? configuration, ILogger? logger)
  {
    string? value = configuration?[HfDirectSampling.ConfigurationKey];
    if (HfDirectSampling.TryParseSetting(value, out HfDirectSamplingSetting setting))
    {
      return setting;
    }

    logger?.LogWarning("{Key} is '{Value}', which is not Auto, Q, I or Off; using Auto",
      HfDirectSampling.ConfigurationKey, value);
    return HfDirectSamplingSetting.Auto;
  }

  /// <summary>
  /// The idle band sweep's device factory (AUD-103 review, L5): each call reads the setting afresh from
  /// <paramref name="configuration"/> with <see cref="Read"/> and creates the device with it. After a
  /// change of the setting the idle sweep sees it on its next open, while the radio applies it only at
  /// its next start and the band map's reason within <c>BandMapService.ReceivingDeviceCacheDuration</c>.
  /// </summary>
  /// <param name="configuration">Returns the application configuration; it may return null (read as absent).</param>
  /// <param name="logger">Receives <see cref="Read"/>'s warning; may be null.</param>
  /// <param name="create">Creates the device for a setting; <see cref="CreateFirstRtlSdrOrNull"/> when omitted.</param>
  public static Func<ISdrDevice?> IdleSweepDeviceFactory(
    Func<IConfiguration?> configuration,
    ILogger? logger,
    Func<HfDirectSamplingSetting, ISdrDevice?>? create = null)
  {
    ArgumentNullException.ThrowIfNull(configuration);
    Func<HfDirectSamplingSetting, ISdrDevice?> createDevice = create ?? CreateFirstRtlSdrOrNull;
    return () => createDevice(Read(configuration(), logger));
  }

  /// <summary>
  /// Creates the first enumerated RTL-SDR (unopened) with <paramref name="setting"/>, or null when
  /// there is none. Unlike <see cref="SdrDeviceFactory.CreateFirstAvailable()"/> it never falls back to
  /// the mock device.
  /// </summary>
  /// <param name="setting">The HF setting.</param>
  public static ISdrDevice? CreateFirstRtlSdrOrNull(HfDirectSamplingSetting setting)
  {
    DeviceInfo? info = SdrDeviceFactory.EnumerateDevices(setting)
      .FirstOrDefault(d => d.Type == DeviceType.RTLSDR && d.IsAvailable);
    return info == null ? null : SdrDeviceFactory.CreateDevice(info, setting);
  }
}

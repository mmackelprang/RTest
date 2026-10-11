using RTLSDRCore.Enums;
using RTLSDRCore.Hardware;

namespace RTLSDRCore.Models;

  /// <summary>
  /// Contains information about an SDR device
  /// </summary>
  public class DeviceInfo
  {
      /// <summary>
      /// Gets or sets the device index
      /// </summary>
      public int Index { get; set; }

      /// <summary>
      /// Gets or sets the device name
      /// </summary>
      public string Name { get; set; } = string.Empty;

      /// <summary>
      /// Gets or sets the device manufacturer
      /// </summary>
      public string Manufacturer { get; set; } = string.Empty;

      /// <summary>
      /// Gets or sets the device serial number
      /// </summary>
      public string Serial { get; set; } = string.Empty;

      /// <summary>
      /// Gets or sets the device type
      /// </summary>
      public DeviceType Type { get; set; }

      /// <summary>
      /// Gets or sets whether the device is currently available
      /// </summary>
      public bool IsAvailable { get; set; }

      /// <summary>
      /// Gets or sets the device's USB product string (EEPROM iProduct, e.g. <c>NESDR SMArt v5</c>).
      /// Not the same as <see cref="Name"/>, which for an RTL-SDR is librtlsdr's name for the
      /// USB vendor/product ID pair. Empty when the device does not report one.
      /// </summary>
      public string Product { get; set; } = string.Empty;

      /// <summary>
      /// Gets or sets the tuner chip type. For an RTL-SDR this is read from the device when it is
      /// opened (<c>rtlsdr_get_tuner_type</c>), so a device that has not been opened in this process
      /// reports <c>Unknown (not opened yet)</c>.
      /// </summary>
      public string TunerType { get; set; } = string.Empty;

      /// <summary>
      /// Gets or sets the direct-sampling input this device uses for the HF bands (AUD-99), or
      /// <see cref="DirectSamplingMode.Off"/> when it has no HF bands.
      /// </summary>
      public DirectSamplingMode HfDirectSampling { get; set; }

      /// <summary>
      /// Gets or sets the minimum supported frequency in Hz. For an RTL-SDR this is
      /// <see cref="RTLSDRCore.Hardware.HfDirectSampling.HfMinFrequencyHz"/> when <see cref="HfDirectSampling"/> is not
      /// <see cref="DirectSamplingMode.Off"/>, and <see cref="RtlSdrDevice.TunerMinFrequencyHz"/> otherwise.
      /// </summary>
      public long MinFrequencyHz { get; set; }

      /// <summary>
      /// Gets or sets the maximum supported frequency in Hz
      /// </summary>
      public long MaxFrequencyHz { get; set; }

      /// <summary>
      /// Gets or sets the supported sample rates
      /// </summary>
      public int[] SupportedSampleRates { get; set; } = Array.Empty<int>();

      /// <summary>
      /// Gets or sets available gain values in dB
      /// </summary>
      public float[] AvailableGains { get; set; } = Array.Empty<float>();

      /// <inheritdoc/>
      public override string ToString() =>
          $"[{Index}] {Name} ({Manufacturer}) - {TunerType}";
  }

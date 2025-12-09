using RTLSDRCore.Enums;

namespace RTLSDRCore.Models
{
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
        /// Gets or sets the tuner type
        /// </summary>
        public string TunerType { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the minimum supported frequency in Hz
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
}

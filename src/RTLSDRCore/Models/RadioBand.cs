using RTLSDRCore.Enums;

namespace RTLSDRCore.Models
{
    /// <summary>
    /// Represents a radio band with its frequency range and characteristics
    /// </summary>
    public class RadioBand
    {
        /// <summary>
        /// Gets or sets the band type identifier
        /// </summary>
        public BandType Type { get; set; }

        /// <summary>
        /// Gets or sets the display name of the band
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the minimum frequency in Hz
        /// </summary>
        public long MinFrequencyHz { get; set; }

        /// <summary>
        /// Gets or sets the maximum frequency in Hz
        /// </summary>
        public long MaxFrequencyHz { get; set; }

        /// <summary>
        /// Gets or sets the default frequency step for tuning in Hz
        /// </summary>
        public long DefaultStepHz { get; set; }

        /// <summary>
        /// Gets or sets the default modulation type for this band
        /// </summary>
        public ModulationType DefaultModulation { get; set; }

        /// <summary>
        /// Gets or sets an optional description of the band usage
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the default bandwidth for this band in Hz
        /// </summary>
        public int DefaultBandwidthHz { get; set; }

        /// <summary>
        /// Gets the center frequency of the band in Hz
        /// </summary>
        public long CenterFrequencyHz => (MinFrequencyHz + MaxFrequencyHz) / 2;

        /// <summary>
        /// Gets the bandwidth of the entire band in Hz
        /// </summary>
        public long BandWidthHz => MaxFrequencyHz - MinFrequencyHz;

        /// <summary>
        /// Checks if a frequency is within this band
        /// </summary>
        /// <param name="frequencyHz">Frequency to check in Hz</param>
        /// <returns>True if the frequency is within the band</returns>
        public bool ContainsFrequency(long frequencyHz) =>
            frequencyHz >= MinFrequencyHz && frequencyHz <= MaxFrequencyHz;

        /// <summary>
        /// Clamps a frequency to be within this band
        /// </summary>
        /// <param name="frequencyHz">Frequency to clamp in Hz</param>
        /// <returns>The clamped frequency</returns>
        public long ClampFrequency(long frequencyHz) =>
            Math.Clamp(frequencyHz, MinFrequencyHz, MaxFrequencyHz);

        /// <summary>
        /// Formats a frequency in Hz to a human-readable string
        /// </summary>
        /// <param name="frequencyHz">Frequency in Hz</param>
        /// <returns>Formatted frequency string</returns>
        public static string FormatFrequency(long frequencyHz)
        {
            if (frequencyHz >= 1_000_000_000)
                return $"{frequencyHz / 1_000_000_000.0:F3} GHz";
            if (frequencyHz >= 1_000_000)
                return $"{frequencyHz / 1_000_000.0:F3} MHz";
            if (frequencyHz >= 1_000)
                return $"{frequencyHz / 1_000.0:F1} kHz";
            return $"{frequencyHz} Hz";
        }

        /// <inheritdoc/>
        public override string ToString() =>
            $"{Name}: {FormatFrequency(MinFrequencyHz)} - {FormatFrequency(MaxFrequencyHz)} ({DefaultModulation})";
    }
}

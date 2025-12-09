using RTLSDRCore.Enums;

namespace RTLSDRCore.Models
{
    /// <summary>
    /// Represents the current state of the radio receiver
    /// </summary>
    public class RadioState
    {
        /// <summary>
        /// Gets or sets the current frequency in Hz
        /// </summary>
        public long FrequencyHz { get; set; }

        /// <summary>
        /// Gets or sets the current band type
        /// </summary>
        public BandType CurrentBand { get; set; }

        /// <summary>
        /// Gets or sets the current modulation type
        /// </summary>
        public ModulationType Modulation { get; set; }

        /// <summary>
        /// Gets or sets the operational state of the receiver
        /// </summary>
        public ReceiverState State { get; set; }

        /// <summary>
        /// Gets or sets the current signal strength (0.0 to 1.0)
        /// </summary>
        public float SignalStrength { get; set; }

        /// <summary>
        /// Gets or sets the signal-to-noise ratio in dB
        /// </summary>
        public float SignalToNoiseRatio { get; set; }

        /// <summary>
        /// Gets or sets the current volume level (0.0 to 1.0)
        /// </summary>
        public float Volume { get; set; } = 1.0f;

        /// <summary>
        /// Gets or sets whether squelch is open (signal above threshold)
        /// </summary>
        public bool SquelchOpen { get; set; }

        /// <summary>
        /// Gets or sets the squelch threshold (0.0 to 1.0)
        /// </summary>
        public float SquelchThreshold { get; set; } = 0.1f;

        /// <summary>
        /// Gets or sets whether the receiver is muted
        /// </summary>
        public bool IsMuted { get; set; }

        /// <summary>
        /// Gets or sets the gain level in dB
        /// </summary>
        public float GainDb { get; set; }

        /// <summary>
        /// Gets or sets whether automatic gain control is enabled
        /// </summary>
        public bool AutoGainEnabled { get; set; } = true;

        /// <summary>
        /// Gets or sets the current filter bandwidth in Hz
        /// </summary>
        public int BandwidthHz { get; set; }

        /// <summary>
        /// Gets or sets the current audio format
        /// </summary>
        public AudioFormat AudioFormat { get; set; } = AudioFormat.Default;

        /// <summary>
        /// Gets or sets the device name
        /// </summary>
        public string DeviceName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the last error message, if any
        /// </summary>
        public string? LastError { get; set; }

        /// <summary>
        /// Gets the frequency formatted as a human-readable string
        /// </summary>
        public string FrequencyDisplay => RadioBand.FormatFrequency(FrequencyHz);

        /// <summary>
        /// Creates a deep copy of the radio state
        /// </summary>
        /// <returns>A new RadioState instance with copied values</returns>
        public RadioState Clone() => new()
        {
            FrequencyHz = FrequencyHz,
            CurrentBand = CurrentBand,
            Modulation = Modulation,
            State = State,
            SignalStrength = SignalStrength,
            SignalToNoiseRatio = SignalToNoiseRatio,
            Volume = Volume,
            SquelchOpen = SquelchOpen,
            SquelchThreshold = SquelchThreshold,
            IsMuted = IsMuted,
            GainDb = GainDb,
            AutoGainEnabled = AutoGainEnabled,
            BandwidthHz = BandwidthHz,
            AudioFormat = AudioFormat,
            DeviceName = DeviceName,
            LastError = LastError
        };

        /// <inheritdoc/>
        public override string ToString() =>
            $"{FrequencyDisplay} | {CurrentBand} | {Modulation} | {State} | Signal: {SignalStrength:P0}";
    }
}

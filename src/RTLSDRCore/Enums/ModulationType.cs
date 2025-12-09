namespace RTLSDRCore.Enums
{
    /// <summary>
    /// Defines the supported modulation types for radio signals
    /// </summary>
    public enum ModulationType
    {
        /// <summary>
        /// Amplitude Modulation - used for AM broadcast and aircraft communications
        /// </summary>
        AM,

        /// <summary>
        /// Narrowband Frequency Modulation - used for VHF, weather, and two-way radio
        /// </summary>
        NFM,

        /// <summary>
        /// Wideband Frequency Modulation - used for FM broadcast
        /// </summary>
        WFM,

        /// <summary>
        /// Lower Sideband - used for amateur radio below 10 MHz
        /// </summary>
        LSB,

        /// <summary>
        /// Upper Sideband - used for amateur radio above 10 MHz
        /// </summary>
        USB,

        /// <summary>
        /// Continuous Wave/Morse code
        /// </summary>
        CW,

        /// <summary>
        /// Raw IQ data without demodulation
        /// </summary>
        RAW
    }
}

namespace RTLSDRCore.Enums
{
    /// <summary>
    /// Defines the supported radio band types
    /// </summary>
    public enum BandType
    {
        /// <summary>
        /// AM Broadcast band (530 kHz - 1710 kHz)
        /// </summary>
        AM,

        /// <summary>
        /// FM Broadcast band (87.5 MHz - 108 MHz)
        /// </summary>
        FM,

        /// <summary>
        /// Shortwave band (1.6 MHz - 30 MHz)
        /// </summary>
        Shortwave,

        /// <summary>
        /// Aircraft/Aviation band (108 MHz - 137 MHz)
        /// </summary>
        Aircraft,

        /// <summary>
        /// Weather/NOAA band (162.4 MHz - 162.55 MHz)
        /// </summary>
        Weather,

        /// <summary>
        /// VHF band (30 MHz - 300 MHz)
        /// </summary>
        VHF,

        /// <summary>
        /// Custom band with user-defined parameters
        /// </summary>
        Custom
    }
}

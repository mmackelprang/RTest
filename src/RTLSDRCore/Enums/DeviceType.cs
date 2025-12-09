namespace RTLSDRCore.Enums
{
    /// <summary>
    /// Defines the types of SDR devices supported
    /// </summary>
    public enum DeviceType
    {
        /// <summary>
        /// Mock device for testing without physical hardware
        /// </summary>
        Mock,

        /// <summary>
        /// RTL-SDR USB dongle (RTL2832U based)
        /// </summary>
        RTLSDR,

        /// <summary>
        /// Generic SDR device
        /// </summary>
        Generic
    }
}

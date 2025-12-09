namespace RTLSDRCore.Enums
{
    /// <summary>
    /// Represents the operational state of the radio receiver
    /// </summary>
    public enum ReceiverState
    {
        /// <summary>
        /// Receiver is stopped and not processing audio
        /// </summary>
        Stopped,

        /// <summary>
        /// Receiver is starting up and initializing
        /// </summary>
        Starting,

        /// <summary>
        /// Receiver is running and processing audio
        /// </summary>
        Running,

        /// <summary>
        /// Receiver is scanning for signals
        /// </summary>
        Scanning,

        /// <summary>
        /// Receiver is stopping
        /// </summary>
        Stopping,

        /// <summary>
        /// Receiver encountered an error
        /// </summary>
        Error
    }
}

using RTLSDRCore.Models;

namespace RTLSDRCore.Hardware
{
    /// <summary>
    /// Interface for SDR hardware device abstraction
    /// </summary>
    public interface ISdrDevice : IDisposable
    {
        /// <summary>
        /// Gets the device information
        /// </summary>
        DeviceInfo DeviceInfo { get; }

        /// <summary>
        /// Gets whether the device is currently open and connected
        /// </summary>
        bool IsOpen { get; }

        /// <summary>
        /// Gets whether the device is currently streaming samples
        /// </summary>
        bool IsStreaming { get; }

        /// <summary>
        /// Opens the device for communication
        /// </summary>
        /// <returns>True if successful</returns>
        bool Open();

        /// <summary>
        /// Closes the device connection
        /// </summary>
        void Close();

        /// <summary>
        /// Sets the center frequency
        /// </summary>
        /// <param name="frequencyHz">Frequency in Hz</param>
        /// <returns>True if successful</returns>
        bool SetFrequency(long frequencyHz);

        /// <summary>
        /// Gets the current center frequency
        /// </summary>
        /// <returns>Frequency in Hz</returns>
        long GetFrequency();

        /// <summary>
        /// Sets the sample rate
        /// </summary>
        /// <param name="sampleRate">Sample rate in Hz</param>
        /// <returns>True if successful</returns>
        bool SetSampleRate(int sampleRate);

        /// <summary>
        /// Gets the current sample rate
        /// </summary>
        /// <returns>Sample rate in Hz</returns>
        int GetSampleRate();

        /// <summary>
        /// Sets the gain mode (automatic or manual)
        /// </summary>
        /// <param name="automatic">True for automatic gain control</param>
        /// <returns>True if successful</returns>
        bool SetGainMode(bool automatic);

        /// <summary>
        /// Sets the manual gain value
        /// </summary>
        /// <param name="gainDb">Gain in dB</param>
        /// <returns>True if successful</returns>
        bool SetGain(float gainDb);

        /// <summary>
        /// Gets the current gain value
        /// </summary>
        /// <returns>Gain in dB</returns>
        float GetGain();

        /// <summary>
        /// Sets the frequency correction in PPM
        /// </summary>
        /// <param name="ppm">Correction in parts per million</param>
        /// <returns>True if successful</returns>
        bool SetFrequencyCorrection(int ppm);

        /// <summary>
        /// Gets the frequency correction in PPM
        /// </summary>
        /// <returns>Correction in parts per million</returns>
        int GetFrequencyCorrection();

        /// <summary>
        /// Starts streaming IQ samples
        /// </summary>
        /// <returns>True if successful</returns>
        bool StartStreaming();

        /// <summary>
        /// Stops streaming IQ samples
        /// </summary>
        void StopStreaming();

        /// <summary>
        /// Reads IQ samples from the device
        /// </summary>
        /// <param name="buffer">Buffer to fill with samples</param>
        /// <returns>Number of samples read</returns>
        int ReadSamples(Span<IqSample> buffer);

        /// <summary>
        /// Event raised when new IQ samples are available
        /// </summary>
        event EventHandler<IqSamplesEventArgs>? SamplesAvailable;

        /// <summary>
        /// Event raised when an error occurs
        /// </summary>
        event EventHandler<DeviceErrorEventArgs>? ErrorOccurred;
    }

    /// <summary>
    /// Event arguments for IQ sample data
    /// </summary>
    public class IqSamplesEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the IQ samples
        /// </summary>
        public IqSample[] Samples { get; }

        /// <summary>
        /// Gets the timestamp when samples were captured
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        /// Creates new IQ samples event args
        /// </summary>
        /// <param name="samples">The samples</param>
        public IqSamplesEventArgs(IqSample[] samples)
        {
            Samples = samples;
            Timestamp = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Event arguments for device errors
    /// </summary>
    public class DeviceErrorEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the error message
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Gets the exception, if any
        /// </summary>
        public Exception? Exception { get; }

        /// <summary>
        /// Creates new device error event args
        /// </summary>
        /// <param name="message">Error message</param>
        /// <param name="exception">Optional exception</param>
        public DeviceErrorEventArgs(string message, Exception? exception = null)
        {
            Message = message;
            Exception = exception;
        }
    }
}

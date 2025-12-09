using RTLSDRCore.Enums;
using RTLSDRCore.Models;

namespace RTLSDRCore
{
    /// <summary>
    /// Interface for controlling radio receiver operations
    /// </summary>
    public interface IRadioControl
    {
        #region Lifecycle

        /// <summary>
        /// Starts the radio receiver and begins audio processing
        /// </summary>
        /// <returns>True if startup was successful</returns>
        bool Startup();

        /// <summary>
        /// Stops the radio receiver and cleanly shuts down all audio/radio processes
        /// </summary>
        void Shutdown();

        /// <summary>
        /// Gets whether the receiver is currently running
        /// </summary>
        bool IsRunning { get; }

        #endregion

        #region Frequency Control

        /// <summary>
        /// Gets the current tuned frequency in Hz
        /// </summary>
        long CurrentFrequency { get; }

        /// <summary>
        /// Sets the frequency directly, automatically selecting the appropriate band if possible
        /// </summary>
        /// <param name="frequencyHz">Frequency in Hz</param>
        /// <returns>True if successful</returns>
        bool SetFrequency(long frequencyHz);

        /// <summary>
        /// Sets the frequency within the current band
        /// </summary>
        /// <param name="frequencyHz">Frequency in Hz</param>
        /// <returns>True if successful</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when frequency is outside the current band</exception>
        bool SetFrequencyInBand(long frequencyHz);

        /// <summary>
        /// Tunes the radio frequency up by the specified step
        /// </summary>
        /// <param name="stepHz">Frequency step in Hz (default: 100 kHz)</param>
        /// <returns>True if successful, false if at upper band limit</returns>
        bool TuneFrequencyUp(long stepHz = 100_000);

        /// <summary>
        /// Tunes the radio frequency down by the specified step
        /// </summary>
        /// <param name="stepHz">Frequency step in Hz (default: 100 kHz)</param>
        /// <returns>True if successful, false if at lower band limit</returns>
        bool TuneFrequencyDown(long stepHz = 100_000);

        #endregion

        #region Scanning

        /// <summary>
        /// Scans radio frequencies upward, stopping when a strong signal is detected.
        /// Note: This method blocks the calling thread. Call from a background thread to avoid UI freezing.
        /// The receiver must be started before calling this method.
        /// </summary>
        /// <param name="stepHz">Frequency step in Hz</param>
        /// <param name="signalThreshold">Signal strength threshold (0.0 to 1.0) to stop scanning</param>
        /// <param name="dwellTimeMs">Time to wait at each frequency in milliseconds</param>
        /// <returns>True if a signal was found, false if scan reached band limit or was cancelled</returns>
        /// <exception cref="InvalidOperationException">Thrown when receiver is not started</exception>
        bool ScanFrequencyUp(long stepHz = 100_000, float signalThreshold = 0.3f, int dwellTimeMs = 100);

        /// <summary>
        /// Scans radio frequencies downward, stopping when a strong signal is detected.
        /// Note: This method blocks the calling thread. Call from a background thread to avoid UI freezing.
        /// The receiver must be started before calling this method.
        /// </summary>
        /// <param name="stepHz">Frequency step in Hz</param>
        /// <param name="signalThreshold">Signal strength threshold (0.0 to 1.0) to stop scanning</param>
        /// <param name="dwellTimeMs">Time to wait at each frequency in milliseconds</param>
        /// <returns>True if a signal was found, false if scan reached band limit or was cancelled</returns>
        /// <exception cref="InvalidOperationException">Thrown when receiver is not started</exception>
        bool ScanFrequencyDown(long stepHz = 100_000, float signalThreshold = 0.3f, int dwellTimeMs = 100);

        /// <summary>
        /// Cancels any ongoing scan operation
        /// </summary>
        void CancelScan();

        /// <summary>
        /// Gets whether a scan operation is currently in progress
        /// </summary>
        bool IsScanning { get; }

        #endregion

        #region Band and Modulation

        /// <summary>
        /// Sets the current radio band
        /// </summary>
        /// <param name="bandType">Type of band to set</param>
        /// <param name="specificFrequency">Optional specific frequency within the band</param>
        /// <returns>True if successful</returns>
        bool SetBand(BandType bandType, long? specificFrequency = null);

        /// <summary>
        /// Sets the modulation type, overriding the band default
        /// </summary>
        /// <param name="modulation">Modulation type to use</param>
        /// <returns>True if successful</returns>
        bool SetModulation(ModulationType modulation);

        /// <summary>
        /// Gets the current modulation type
        /// </summary>
        ModulationType CurrentModulation { get; }

        /// <summary>
        /// Gets all available radio bands
        /// </summary>
        /// <returns>List of available radio bands</returns>
        IReadOnlyList<RadioBand> GetAvailableBands();

        #endregion

        #region Audio Control

        /// <summary>
        /// Gets or sets the volume level (0.0 to 1.0)
        /// </summary>
        float Volume { get; set; }

        /// <summary>
        /// Gets or sets whether the receiver is muted
        /// </summary>
        bool IsMuted { get; set; }

        /// <summary>
        /// Gets or sets the squelch threshold (0.0 to 1.0)
        /// </summary>
        float SquelchThreshold { get; set; }

        /// <summary>
        /// Sets the audio output format
        /// </summary>
        /// <param name="format">Audio format configuration</param>
        void SetAudioOutputFormat(AudioFormat format);

        /// <summary>
        /// Gets the current audio output format
        /// </summary>
        /// <returns>Current audio format</returns>
        AudioFormat GetAudioOutputFormat();

        #endregion

        #region Gain Control

        /// <summary>
        /// Gets or sets whether automatic gain control is enabled
        /// </summary>
        bool AutoGainEnabled { get; set; }

        /// <summary>
        /// Gets or sets the manual gain value in dB (only effective when AutoGainEnabled is false)
        /// </summary>
        float Gain { get; set; }

        #endregion

        #region State and Signal

        /// <summary>
        /// Gets the current radio state
        /// </summary>
        /// <returns>Current radio state information</returns>
        RadioState GetRadioState();

        /// <summary>
        /// Gets the current signal strength (0.0 to 1.0)
        /// </summary>
        float SignalStrength { get; }

        #endregion

        #region Events

        /// <summary>
        /// Event raised when new audio samples are available
        /// </summary>
        event EventHandler<AudioDataEventArgs>? AudioDataAvailable;

        /// <summary>
        /// Event raised when signal strength is updated
        /// </summary>
        event EventHandler<SignalStrengthEventArgs>? SignalStrengthUpdated;

        /// <summary>
        /// Event raised when receiver state changes
        /// </summary>
        event EventHandler<ReceiverStateChangedEventArgs>? StateChanged;

        /// <summary>
        /// Event raised when frequency changes
        /// </summary>
        event EventHandler<FrequencyChangedEventArgs>? FrequencyChanged;

        #endregion
    }

    /// <summary>
    /// Event arguments for frequency changes
    /// </summary>
    public class FrequencyChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the previous frequency in Hz
        /// </summary>
        public long OldFrequency { get; }

        /// <summary>
        /// Gets the new frequency in Hz
        /// </summary>
        public long NewFrequency { get; }

        /// <summary>
        /// Creates new frequency changed event args
        /// </summary>
        /// <param name="oldFrequency">Previous frequency in Hz</param>
        /// <param name="newFrequency">New frequency in Hz</param>
        public FrequencyChangedEventArgs(long oldFrequency, long newFrequency)
        {
            OldFrequency = oldFrequency;
            NewFrequency = newFrequency;
        }
    }
}

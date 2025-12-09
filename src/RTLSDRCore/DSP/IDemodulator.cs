using RTLSDRCore.Models;

namespace RTLSDRCore.DSP
{
    /// <summary>
    /// Interface for signal demodulators
    /// </summary>
    public interface IDemodulator
    {
        /// <summary>
        /// Gets the name of the demodulator
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets or sets the sample rate in Hz
        /// </summary>
        int SampleRate { get; set; }

        /// <summary>
        /// Gets or sets the bandwidth in Hz
        /// </summary>
        int Bandwidth { get; set; }

        /// <summary>
        /// Demodulates IQ samples to audio samples
        /// </summary>
        /// <param name="input">Input IQ samples</param>
        /// <param name="output">Output audio samples</param>
        /// <returns>Number of output samples produced</returns>
        int Demodulate(ReadOnlySpan<IqSample> input, Span<float> output);

        /// <summary>
        /// Resets the demodulator state
        /// </summary>
        void Reset();
    }
}

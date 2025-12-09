using RTLSDRCore.Models;

namespace RTLSDRCore.DSP
{
    /// <summary>
    /// Amplitude Modulation (AM) demodulator using envelope detection
    /// </summary>
    public class AmDemodulator : IDemodulator
    {
        private float _dcBlockerState;
        private readonly float _dcBlockerAlpha;

        /// <inheritdoc/>
        public string Name => "AM";

        /// <inheritdoc/>
        public int SampleRate { get; set; } = 2_400_000;

        /// <inheritdoc/>
        public int Bandwidth { get; set; } = 10_000;

        /// <summary>
        /// Gets or sets the DC blocker time constant
        /// </summary>
        public float DcBlockerTimeConstant { get; set; } = 0.9999f;

        /// <summary>
        /// Creates a new AM demodulator
        /// </summary>
        public AmDemodulator()
        {
            _dcBlockerAlpha = DcBlockerTimeConstant;
        }

        /// <inheritdoc/>
        public int Demodulate(ReadOnlySpan<IqSample> input, Span<float> output)
        {
            var outputCount = Math.Min(input.Length, output.Length);

            for (var i = 0; i < outputCount; i++)
            {
                // Envelope detection: magnitude of the complex signal
                var magnitude = input[i].Magnitude;

                // DC blocking filter to remove the carrier
                _dcBlockerState = _dcBlockerAlpha * _dcBlockerState + (1 - _dcBlockerAlpha) * magnitude;
                output[i] = magnitude - _dcBlockerState;
            }

            return outputCount;
        }

        /// <inheritdoc/>
        public void Reset()
        {
            _dcBlockerState = 0;
        }
    }
}

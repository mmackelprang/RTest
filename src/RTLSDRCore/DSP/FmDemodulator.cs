using RTLSDRCore.Models;

namespace RTLSDRCore.DSP
{
    /// <summary>
    /// Frequency Modulation (FM) demodulator using quadrature detection
    /// </summary>
    public class FmDemodulator : IDemodulator
    {
        private IqSample _previousSample;
        private float _gain;

        /// <inheritdoc/>
        public string Name => "FM";

        /// <inheritdoc/>
        public int SampleRate { get; set; } = 2_400_000;

        /// <inheritdoc/>
        public int Bandwidth { get; set; } = 200_000;

        /// <summary>
        /// Gets or sets the maximum frequency deviation in Hz
        /// </summary>
        public float MaxDeviation { get; set; } = 75_000;

        /// <summary>
        /// Creates a new FM demodulator
        /// </summary>
        public FmDemodulator()
        {
            UpdateGain();
        }

        private void UpdateGain()
        {
            // Gain to normalize the output based on max deviation and sample rate
            _gain = SampleRate / (2.0f * MathF.PI * MaxDeviation);
        }

        /// <inheritdoc/>
        public int Demodulate(ReadOnlySpan<IqSample> input, Span<float> output)
        {
            var outputCount = Math.Min(input.Length, output.Length);

            for (var i = 0; i < outputCount; i++)
            {
                var current = input[i];

                // Quadrature demodulation: phase difference between consecutive samples
                // Using: atan2(Q[n]*I[n-1] - I[n]*Q[n-1], I[n]*I[n-1] + Q[n]*Q[n-1])
                var conjugate = _previousSample.Conjugate;
                var product = current * conjugate;

                // Calculate phase difference using atan2
                var phaseDiff = MathF.Atan2(product.Q, product.I);

                // Scale to audio range
                output[i] = phaseDiff * _gain;

                _previousSample = current;
            }

            return outputCount;
        }

        /// <inheritdoc/>
        public void Reset()
        {
            _previousSample = new IqSample(0, 0);
        }
    }

    /// <summary>
    /// Wideband FM demodulator for broadcast FM (mono)
    /// </summary>
    public class WfmDemodulator : FmDemodulator
    {
        /// <summary>
        /// Creates a new wideband FM demodulator
        /// </summary>
        public WfmDemodulator()
        {
            Bandwidth = 200_000;
            MaxDeviation = 75_000;
        }
    }

    /// <summary>
    /// Narrowband FM demodulator for VHF/UHF communications
    /// </summary>
    public class NfmDemodulator : FmDemodulator
    {
        /// <summary>
        /// Creates a new narrowband FM demodulator
        /// </summary>
        public NfmDemodulator()
        {
            Bandwidth = 12_500;
            MaxDeviation = 5_000;
        }
    }
}

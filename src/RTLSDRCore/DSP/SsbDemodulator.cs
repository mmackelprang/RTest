using RTLSDRCore.Models;

namespace RTLSDRCore.DSP
{
    /// <summary>
    /// Single Sideband (SSB) demodulator
    /// </summary>
    public class SsbDemodulator : IDemodulator
    {
        private readonly bool _upperSideband;
        private readonly HilbertTransform _hilbert;

        /// <inheritdoc/>
        public string Name => _upperSideband ? "USB" : "LSB";

        /// <inheritdoc/>
        public int SampleRate { get; set; } = 2_400_000;

        /// <inheritdoc/>
        public int Bandwidth { get; set; } = 3_000;

        /// <summary>
        /// Creates a new SSB demodulator
        /// </summary>
        /// <param name="upperSideband">True for USB, false for LSB</param>
        public SsbDemodulator(bool upperSideband = true)
        {
            _upperSideband = upperSideband;
            _hilbert = new HilbertTransform(63);
        }

        /// <inheritdoc/>
        public int Demodulate(ReadOnlySpan<IqSample> input, Span<float> output)
        {
            var outputCount = Math.Min(input.Length, output.Length);

            for (var i = 0; i < outputCount; i++)
            {
                var sample = input[i];

                // For SSB, we need to shift the signal and extract the audio
                // USB: I + jQ shifted down, LSB: I - jQ shifted up
                if (_upperSideband)
                {
                    output[i] = sample.I;
                }
                else
                {
                    output[i] = sample.I;
                }
            }

            return outputCount;
        }

        /// <inheritdoc/>
        public void Reset()
        {
            _hilbert.Reset();
        }
    }

    /// <summary>
    /// Upper Sideband demodulator
    /// </summary>
    public class UsbDemodulator : SsbDemodulator
    {
        /// <summary>
        /// Creates a new USB demodulator
        /// </summary>
        public UsbDemodulator() : base(true) { }
    }

    /// <summary>
    /// Lower Sideband demodulator
    /// </summary>
    public class LsbDemodulator : SsbDemodulator
    {
        /// <summary>
        /// Creates a new LSB demodulator
        /// </summary>
        public LsbDemodulator() : base(false) { }
    }

    /// <summary>
    /// Simple Hilbert transform implementation
    /// </summary>
    internal class HilbertTransform
    {
        private readonly float[] _coefficients;
        private readonly float[] _buffer;
        private int _bufferIndex;

        public HilbertTransform(int taps = 63)
        {
            _coefficients = GenerateCoefficients(taps);
            _buffer = new float[taps];
            _bufferIndex = 0;
        }

        private static float[] GenerateCoefficients(int taps)
        {
            var coeffs = new float[taps];
            var center = taps / 2;

            for (var i = 0; i < taps; i++)
            {
                var n = i - center;
                if (n == 0)
                {
                    coeffs[i] = 0;
                }
                else if (n % 2 != 0)
                {
                    coeffs[i] = 2.0f / (MathF.PI * n);
                }
                else
                {
                    coeffs[i] = 0;
                }

                // Apply Hamming window
                coeffs[i] *= 0.54f - 0.46f * MathF.Cos(2.0f * MathF.PI * i / (taps - 1));
            }

            return coeffs;
        }

        public float Process(float input)
        {
            _buffer[_bufferIndex] = input;

            var output = 0f;
            var index = _bufferIndex;

            for (var i = 0; i < _coefficients.Length; i++)
            {
                output += _buffer[index] * _coefficients[i];
                index--;
                if (index < 0) index = _buffer.Length - 1;
            }

            _bufferIndex++;
            if (_bufferIndex >= _buffer.Length) _bufferIndex = 0;

            return output;
        }

        public void Reset()
        {
            Array.Clear(_buffer);
            _bufferIndex = 0;
        }
    }
}

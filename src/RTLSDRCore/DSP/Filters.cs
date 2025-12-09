using RTLSDRCore.Models;

namespace RTLSDRCore.DSP
{
    /// <summary>
    /// Low-pass FIR filter
    /// </summary>
    public class LowPassFilter
    {
        private readonly float[] _coefficients;
        private readonly float[] _buffer;
        private int _bufferIndex;

        /// <summary>
        /// Gets the cutoff frequency in Hz
        /// </summary>
        public float CutoffFrequency { get; }

        /// <summary>
        /// Gets the sample rate in Hz
        /// </summary>
        public int SampleRate { get; }

        /// <summary>
        /// Gets the number of taps
        /// </summary>
        public int Taps => _coefficients.Length;

        /// <summary>
        /// Creates a new low-pass filter
        /// </summary>
        /// <param name="sampleRate">Sample rate in Hz</param>
        /// <param name="cutoffFrequency">Cutoff frequency in Hz</param>
        /// <param name="taps">Number of filter taps (odd number recommended)</param>
        public LowPassFilter(int sampleRate, float cutoffFrequency, int taps = 63)
        {
            SampleRate = sampleRate;
            CutoffFrequency = cutoffFrequency;
            _coefficients = GenerateCoefficients(sampleRate, cutoffFrequency, taps);
            _buffer = new float[taps];
            _bufferIndex = 0;
        }

        private static float[] GenerateCoefficients(int sampleRate, float cutoff, int taps)
        {
            var coeffs = new float[taps];
            var fc = cutoff / sampleRate;
            var center = taps / 2;

            for (var i = 0; i < taps; i++)
            {
                var n = i - center;
                if (n == 0)
                {
                    coeffs[i] = 2 * fc;
                }
                else
                {
                    coeffs[i] = MathF.Sin(2 * MathF.PI * fc * n) / (MathF.PI * n);
                }

                // Apply Hamming window
                coeffs[i] *= 0.54f - 0.46f * MathF.Cos(2 * MathF.PI * i / (taps - 1));
            }

            // Normalize
            var sum = coeffs.Sum();
            for (var i = 0; i < taps; i++)
            {
                coeffs[i] /= sum;
            }

            return coeffs;
        }

        /// <summary>
        /// Processes a single sample through the filter
        /// </summary>
        /// <param name="input">Input sample</param>
        /// <returns>Filtered output sample</returns>
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

        /// <summary>
        /// Processes an array of samples
        /// </summary>
        /// <param name="input">Input samples</param>
        /// <param name="output">Output buffer</param>
        public void Process(ReadOnlySpan<float> input, Span<float> output)
        {
            var count = Math.Min(input.Length, output.Length);
            for (var i = 0; i < count; i++)
            {
                output[i] = Process(input[i]);
            }
        }

        /// <summary>
        /// Resets the filter state
        /// </summary>
        public void Reset()
        {
            Array.Clear(_buffer);
            _bufferIndex = 0;
        }
    }

    /// <summary>
    /// IQ low-pass filter for complex signals
    /// </summary>
    public class IqLowPassFilter
    {
        private readonly LowPassFilter _iFilter;
        private readonly LowPassFilter _qFilter;

        /// <summary>
        /// Creates a new IQ low-pass filter
        /// </summary>
        /// <param name="sampleRate">Sample rate in Hz</param>
        /// <param name="cutoffFrequency">Cutoff frequency in Hz</param>
        /// <param name="taps">Number of filter taps</param>
        public IqLowPassFilter(int sampleRate, float cutoffFrequency, int taps = 63)
        {
            _iFilter = new LowPassFilter(sampleRate, cutoffFrequency, taps);
            _qFilter = new LowPassFilter(sampleRate, cutoffFrequency, taps);
        }

        /// <summary>
        /// Processes a single IQ sample
        /// </summary>
        /// <param name="input">Input IQ sample</param>
        /// <returns>Filtered IQ sample</returns>
        public IqSample Process(IqSample input)
        {
            return new IqSample(
                _iFilter.Process(input.I),
                _qFilter.Process(input.Q)
            );
        }

        /// <summary>
        /// Processes an array of IQ samples
        /// </summary>
        /// <param name="input">Input samples</param>
        /// <param name="output">Output buffer</param>
        public void Process(ReadOnlySpan<IqSample> input, Span<IqSample> output)
        {
            var count = Math.Min(input.Length, output.Length);
            for (var i = 0; i < count; i++)
            {
                output[i] = Process(input[i]);
            }
        }

        /// <summary>
        /// Resets both filters
        /// </summary>
        public void Reset()
        {
            _iFilter.Reset();
            _qFilter.Reset();
        }
    }

    /// <summary>
    /// Decimator for reducing sample rate
    /// </summary>
    public class Decimator
    {
        private readonly IqLowPassFilter _antiAliasFilter;
        private readonly int _factor;
        private int _counter;

        /// <summary>
        /// Gets the decimation factor
        /// </summary>
        public int Factor => _factor;

        /// <summary>
        /// Gets the input sample rate
        /// </summary>
        public int InputSampleRate { get; }

        /// <summary>
        /// Gets the output sample rate
        /// </summary>
        public int OutputSampleRate => InputSampleRate / _factor;

        /// <summary>
        /// Creates a new decimator
        /// </summary>
        /// <param name="inputSampleRate">Input sample rate in Hz</param>
        /// <param name="decimationFactor">Decimation factor (must be >= 1)</param>
        public Decimator(int inputSampleRate, int decimationFactor)
        {
            if (decimationFactor < 1)
                throw new ArgumentException("Decimation factor must be at least 1", nameof(decimationFactor));

            InputSampleRate = inputSampleRate;
            _factor = decimationFactor;

            // Anti-aliasing filter at the output Nyquist frequency
            var cutoff = inputSampleRate / (2.0f * decimationFactor) * 0.9f;
            _antiAliasFilter = new IqLowPassFilter(inputSampleRate, cutoff);
        }

        /// <summary>
        /// Decimates an array of IQ samples
        /// </summary>
        /// <param name="input">Input samples</param>
        /// <param name="output">Output buffer</param>
        /// <returns>Number of output samples</returns>
        public int Decimate(ReadOnlySpan<IqSample> input, Span<IqSample> output)
        {
            var outputIndex = 0;
            var maxOutput = output.Length;

            for (var i = 0; i < input.Length && outputIndex < maxOutput; i++)
            {
                var filtered = _antiAliasFilter.Process(input[i]);

                _counter++;
                if (_counter >= _factor)
                {
                    output[outputIndex++] = filtered;
                    _counter = 0;
                }
            }

            return outputIndex;
        }

        /// <summary>
        /// Resets the decimator state
        /// </summary>
        public void Reset()
        {
            _antiAliasFilter.Reset();
            _counter = 0;
        }
    }

    /// <summary>
    /// Automatic Gain Control (AGC)
    /// </summary>
    public class AgcProcessor
    {
        private float _gain = 1.0f;
        private float _peakLevel;

        /// <summary>
        /// Gets or sets the target output level (0.0 to 1.0)
        /// </summary>
        public float TargetLevel { get; set; } = 0.7f;

        /// <summary>
        /// Gets or sets the attack time constant (0.0 to 1.0, lower = faster)
        /// </summary>
        public float AttackRate { get; set; } = 0.01f;

        /// <summary>
        /// Gets or sets the decay time constant (0.0 to 1.0, lower = faster)
        /// </summary>
        public float DecayRate { get; set; } = 0.0001f;

        /// <summary>
        /// Gets or sets the maximum gain
        /// </summary>
        public float MaxGain { get; set; } = 100.0f;

        /// <summary>
        /// Gets or sets the minimum gain
        /// </summary>
        public float MinGain { get; set; } = 0.01f;

        /// <summary>
        /// Gets the current gain value
        /// </summary>
        public float CurrentGain => _gain;

        /// <summary>
        /// Processes samples through AGC
        /// </summary>
        /// <param name="input">Input samples</param>
        /// <param name="output">Output buffer</param>
        public void Process(ReadOnlySpan<float> input, Span<float> output)
        {
            var count = Math.Min(input.Length, output.Length);

            for (var i = 0; i < count; i++)
            {
                var sample = input[i] * _gain;
                var absLevel = MathF.Abs(sample);

                // Update peak level with attack/decay
                if (absLevel > _peakLevel)
                {
                    _peakLevel = _peakLevel + AttackRate * (absLevel - _peakLevel);
                }
                else
                {
                    _peakLevel = _peakLevel + DecayRate * (absLevel - _peakLevel);
                }

                // Adjust gain
                if (_peakLevel > 0)
                {
                    var targetGain = TargetLevel / _peakLevel;
                    _gain = Math.Clamp(targetGain, MinGain, MaxGain);
                }

                // Clip output to prevent distortion
                output[i] = Math.Clamp(sample, -1.0f, 1.0f);
            }
        }

        /// <summary>
        /// Resets the AGC state
        /// </summary>
        public void Reset()
        {
            _gain = 1.0f;
            _peakLevel = 0;
        }
    }
}

using RTLSDRCore.Models;

namespace RTLSDRCore.DSP;

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
              if (index < 0)
      {
        index = _buffer.Length - 1;
      }
    }

          _bufferIndex++;
          if (_bufferIndex >= _buffer.Length)
    {
      _bufferIndex = 0;
    }

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
  /// Decimator for reducing IQ sample rate with properly-sized anti-alias filter.
  /// Calculates filter taps from the transition bandwidth to ensure adequate
  /// stop-band attenuation for the given decimation factor.
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
      /// Creates a new decimator with automatically-calculated filter taps.
      /// </summary>
      /// <param name="inputSampleRate">Input sample rate in Hz</param>
      /// <param name="decimationFactor">Decimation factor (must be >= 1)</param>
      /// <param name="cutoffHz">Optional cutoff frequency override. Defaults to 80% of output Nyquist.</param>
      public Decimator(int inputSampleRate, int decimationFactor, float cutoffHz = 0)
      {
          if (decimationFactor < 1)
    {
      throw new ArgumentException("Decimation factor must be at least 1", nameof(decimationFactor));
    }

    InputSampleRate = inputSampleRate;
          _factor = decimationFactor;

          // Anti-aliasing filter at 80% of the output Nyquist frequency
          var outputNyquist = inputSampleRate / (2.0f * decimationFactor);
          var cutoff = cutoffHz > 0 ? cutoffHz : outputNyquist * 0.8f;

          // Calculate taps from transition bandwidth.
          // For Hamming window: taps ≈ 3.3 / (transition_width / sample_rate)
          var transitionHz = outputNyquist - cutoff;
          if (transitionHz <= 0)
    {
      transitionHz = outputNyquist * 0.2f;
    }

    var normalizedTransition = transitionHz / inputSampleRate;
          var taps = (int)Math.Ceiling(3.3 / normalizedTransition);
          taps = Math.Max(taps, 63);
          taps |= 1; // ensure odd

          _antiAliasFilter = new IqLowPassFilter(inputSampleRate, cutoff, taps);
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
  /// Audio decimator that operates on float samples with multi-stage decimation.
  /// Uses multiple stages to avoid the problem of very low normalized cutoff
  /// frequencies that require impractically many filter taps.
  /// </summary>
  public class AudioDecimator
  {
      private readonly List<DecimationStage> _stages = new();
      private readonly int _totalFactor;

      /// <summary>
      /// Gets the total decimation factor across all stages.
      /// </summary>
      public int Factor => _totalFactor;

      /// <summary>
      /// Gets the input sample rate.
      /// </summary>
      public int InputSampleRate { get; }

      /// <summary>
      /// Gets the output sample rate.
      /// </summary>
      public int OutputSampleRate { get; }

      /// <summary>
      /// Creates a new multi-stage audio decimator.
      /// </summary>
      /// <param name="inputSampleRate">Input sample rate in Hz.</param>
      /// <param name="outputSampleRate">Desired output sample rate in Hz.</param>
      public AudioDecimator(int inputSampleRate, int outputSampleRate)
      {
          if (outputSampleRate <= 0)
    {
      throw new ArgumentException("Output sample rate must be positive", nameof(outputSampleRate));
    }

    if (inputSampleRate < outputSampleRate)
    {
      throw new ArgumentException("Input sample rate must be >= output sample rate", nameof(inputSampleRate));
    }

    InputSampleRate = inputSampleRate;
          OutputSampleRate = outputSampleRate;
          _totalFactor = inputSampleRate / outputSampleRate;

          // Factor the total decimation into stages where each stage has a
          // reasonable decimation factor (max ~10) so the anti-alias filter
          // has a practical normalized cutoff frequency.
          var remaining = _totalFactor;
          var currentRate = inputSampleRate;

          foreach (var factor in FactorizeDecimation(remaining))
          {
              var nextRate = currentRate / factor;
              // Anti-alias cutoff at 80% of the output Nyquist for this stage
              var nyquist = nextRate / 2.0f;
              var cutoff = nyquist * 0.8f;
              // Calculate taps from the required transition bandwidth.
              // For Hamming window: taps ≈ 3.3 / (transition_width / sample_rate)
              // Transition band = Nyquist - cutoff (in Hz)
              var transitionHz = nyquist - cutoff;
              var normalizedTransition = transitionHz / currentRate;
              var taps = (int)Math.Ceiling(3.3 / normalizedTransition);
              taps = Math.Max(taps, 63);
              taps |= 1; // ensure odd
              _stages.Add(new DecimationStage(
                  new LowPassFilter(currentRate, cutoff, taps), factor));
              currentRate = nextRate;
          }
      }

      /// <summary>
      /// Decimates float audio samples.
      /// </summary>
      /// <param name="input">Input audio samples.</param>
      /// <param name="output">Output buffer.</param>
      /// <returns>Number of output samples written.</returns>
      public int Decimate(ReadOnlySpan<float> input, Span<float> output)
      {
          if (_stages.Count == 0)
          {
              var count = Math.Min(input.Length, output.Length);
              input.Slice(0, count).CopyTo(output);
              return count;
          }

          // Fast path for single-stage (most common: e.g., 5:1)
          // Avoids all intermediate allocations.
          if (_stages.Count == 1)
          {
              var stage = _stages[0];
              var outputIndex = 0;
              var maxOutput = output.Length;

              for (var i = 0; i < input.Length && outputIndex < maxOutput; i++)
              {
                  var filtered = stage.Filter.Process(input[i]);
                  stage.Counter++;
                  if (stage.Counter >= stage.Factor)
                  {
                      output[outputIndex++] = filtered;
                      stage.Counter = 0;
                  }
              }

              return outputIndex;
          }

          // Multi-stage path (allocates intermediate buffers)
          var current = input.ToArray();

          foreach (var stage in _stages)
          {
              var stageOutput = new float[current.Length / stage.Factor + 1];
              var outputIndex = 0;

              for (var i = 0; i < current.Length && outputIndex < stageOutput.Length; i++)
              {
                  var filtered = stage.Filter.Process(current[i]);
                  stage.Counter++;
                  if (stage.Counter >= stage.Factor)
                  {
                      stageOutput[outputIndex++] = filtered;
                      stage.Counter = 0;
                  }
              }

              current = new float[outputIndex];
              Array.Copy(stageOutput, current, outputIndex);
          }

          var resultCount = Math.Min(current.Length, output.Length);
          current.AsSpan(0, resultCount).CopyTo(output);
          return resultCount;
      }

      /// <summary>
      /// Resets all stages.
      /// </summary>
      public void Reset()
      {
          foreach (var stage in _stages)
          {
              stage.Filter.Reset();
              stage.Counter = 0;
          }
      }

      /// <summary>
      /// Breaks a large decimation factor into a sequence of smaller factors
      /// (each &lt;= 10) for multi-stage decimation.
      /// </summary>
      private static List<int> FactorizeDecimation(int totalFactor)
      {
          var factors = new List<int>();
          var remaining = totalFactor;

          while (remaining > 10)
          {
              // Find the largest factor <= 10 that divides remaining
              var bestFactor = 2;
              for (var f = 10; f >= 2; f--)
              {
                  if (remaining % f == 0)
                  {
                      bestFactor = f;
                      break;
                  }
              }

              factors.Add(bestFactor);
              remaining /= bestFactor;
          }

          if (remaining > 1)
          {
              factors.Add(remaining);
          }

          return factors;
      }

      private class DecimationStage
      {
          public LowPassFilter Filter { get; }
          public int Factor { get; }
          public int Counter { get; set; }

          public DecimationStage(LowPassFilter filter, int factor)
          {
              Filter = filter;
              Factor = factor;
          }
      }
  }

  /// <summary>
  /// FM de-emphasis filter. Compensates for the pre-emphasis applied during
  /// FM broadcast transmission (high frequencies are boosted before transmission
  /// and must be attenuated on receive). Without this filter, FM audio sounds
  /// harsh/bright and the 19kHz stereo pilot + 38kHz subcarrier alias into
  /// the audio band during decimation, causing an "underwater" effect.
  ///
  /// Implements a single-pole IIR low-pass: y[n] = (1-α)·x[n] + α·y[n-1]
  /// where α = exp(-1/(τ·sampleRate)).
  /// </summary>
  public class DeEmphasisFilter
  {
      private readonly float _alpha;
      private float _previous;

      /// <summary>
      /// Creates a de-emphasis filter with the specified time constant.
      /// </summary>
      /// <param name="sampleRate">Sample rate in Hz.</param>
      /// <param name="timeConstantUs">Time constant in microseconds.
      /// 75μs for North America/South Korea, 50μs for Europe/Japan.</param>
      public DeEmphasisFilter(int sampleRate, float timeConstantUs = 75f)
      {
          var tau = timeConstantUs * 1e-6f;
          _alpha = MathF.Exp(-1.0f / (tau * sampleRate));
      }

      /// <summary>
      /// Processes samples through the de-emphasis filter in-place.
      /// </summary>
      public void Process(Span<float> samples)
      {
          var oneMinusAlpha = 1.0f - _alpha;
          for (var i = 0; i < samples.Length; i++)
          {
              _previous = oneMinusAlpha * samples[i] + _alpha * _previous;
              samples[i] = _previous;
          }
      }

      /// <summary>
      /// Resets the filter state.
      /// </summary>
      public void Reset()
      {
          _previous = 0;
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

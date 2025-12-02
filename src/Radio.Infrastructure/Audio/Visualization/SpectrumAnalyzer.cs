using System.Numerics;

namespace Radio.Infrastructure.Audio.Visualization;

/// <summary>
/// Performs FFT-based spectrum analysis on audio samples.
/// Converts time-domain samples to frequency-domain magnitude data.
/// </summary>
internal sealed class SpectrumAnalyzer
{
  private readonly int _fftSize;
  private readonly int _sampleRate;
  private readonly float _frequencyResolution;
  private readonly float[] _inputBuffer;
  private readonly Complex[] _fftBuffer;
  private readonly float[] _magnitudes;
  private readonly float[] _smoothedMagnitudes;
  private readonly float[] _windowFunction;
  private readonly bool _applyWindow;
  private readonly float _smoothingFactor;
  private int _inputPosition;
  private readonly object _lock = new();

  /// <summary>
  /// Gets the number of frequency bins (FFT size / 2).
  /// </summary>
  public int BinCount => _fftSize / 2;

  /// <summary>
  /// Gets the frequency resolution (Hz per bin).
  /// </summary>
  public float FrequencyResolution => _frequencyResolution;

  /// <summary>
  /// Initializes a new instance of the <see cref="SpectrumAnalyzer"/> class.
  /// </summary>
  /// <param name="fftSize">The FFT size (must be power of 2).</param>
  /// <param name="sampleRate">The sample rate in Hz.</param>
  /// <param name="applyWindow">Whether to apply Hann window function.</param>
  /// <param name="smoothingFactor">Smoothing factor for magnitude values (0.0 to 1.0).</param>
  public SpectrumAnalyzer(int fftSize, int sampleRate, bool applyWindow = true, float smoothingFactor = 0.5f)
  {
    if (!IsPowerOfTwo(fftSize))
    {
      throw new ArgumentException("FFT size must be a power of 2", nameof(fftSize));
    }

    if (sampleRate <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(sampleRate), "Sample rate must be positive");
    }

    _fftSize = fftSize;
    _sampleRate = sampleRate;
    _frequencyResolution = (float)sampleRate / fftSize;
    _applyWindow = applyWindow;
    _smoothingFactor = Math.Clamp(smoothingFactor, 0f, 1f);

    _inputBuffer = new float[fftSize];
    _fftBuffer = new Complex[fftSize];
    _magnitudes = new float[fftSize / 2];
    _smoothedMagnitudes = new float[fftSize / 2];
    _windowFunction = new float[fftSize];

    // Generate Hann window function
    for (var i = 0; i < fftSize; i++)
    {
      _windowFunction[i] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / (fftSize - 1)));
    }
  }

  /// <summary>
  /// Adds samples to the input buffer for processing.
  /// </summary>
  /// <param name="samples">The audio samples to add.</param>
  public void AddSamples(float[] samples)
  {
    lock (_lock)
    {
      foreach (var sample in samples)
      {
        _inputBuffer[_inputPosition] = sample;
        _inputPosition = (_inputPosition + 1) % _fftSize;
      }
    }
  }

  /// <summary>
  /// Adds samples to the input buffer for processing.
  /// </summary>
  /// <param name="samples">The audio samples span to add.</param>
  /// <param name="count">The number of samples to add.</param>
  public void AddSamples(Span<float> samples, int count)
  {
    lock (_lock)
    {
      for (var i = 0; i < count; i++)
      {
        _inputBuffer[_inputPosition] = samples[i];
        _inputPosition = (_inputPosition + 1) % _fftSize;
      }
    }
  }

  /// <summary>
  /// Computes the FFT and returns magnitude values.
  /// </summary>
  /// <returns>Array of magnitude values (0.0 to 1.0) for each frequency bin.</returns>
  public float[] GetMagnitudes()
  {
    lock (_lock)
    {
      // Copy input buffer to FFT buffer with windowing
      for (var i = 0; i < _fftSize; i++)
      {
        var circularIndex = (_inputPosition + i) % _fftSize;
        var value = _inputBuffer[circularIndex];

        if (_applyWindow)
        {
          value *= _windowFunction[i];
        }

        _fftBuffer[i] = new Complex(value, 0);
      }

      // Perform FFT in place
      FFT(_fftBuffer);

      // Calculate magnitudes (normalized)
      var maxMagnitude = 0f;
      for (var i = 0; i < _magnitudes.Length; i++)
      {
        _magnitudes[i] = (float)_fftBuffer[i].Magnitude / _fftSize * 2;
        if (_magnitudes[i] > maxMagnitude)
        {
          maxMagnitude = _magnitudes[i];
        }
      }

      // Normalize to 0-1 range if there's audio
      if (maxMagnitude > 0.001f)
      {
        for (var i = 0; i < _magnitudes.Length; i++)
        {
          _magnitudes[i] = Math.Clamp(_magnitudes[i] / maxMagnitude, 0f, 1f);
        }
      }

      // Apply smoothing
      for (var i = 0; i < _smoothedMagnitudes.Length; i++)
      {
        _smoothedMagnitudes[i] = _smoothedMagnitudes[i] * _smoothingFactor +
                                 _magnitudes[i] * (1f - _smoothingFactor);
      }

      // Return a copy of the smoothed magnitudes
      var result = new float[_smoothedMagnitudes.Length];
      Array.Copy(_smoothedMagnitudes, result, result.Length);
      return result;
    }
  }

  /// <summary>
  /// Gets the frequency values for each bin.
  /// </summary>
  /// <returns>Array of frequency values in Hz.</returns>
  public float[] GetFrequencies()
  {
    var frequencies = new float[BinCount];
    for (var i = 0; i < BinCount; i++)
    {
      frequencies[i] = i * _frequencyResolution;
    }
    return frequencies;
  }

  /// <summary>
  /// Resets the analyzer state.
  /// </summary>
  public void Reset()
  {
    lock (_lock)
    {
      Array.Clear(_inputBuffer);
      Array.Clear(_magnitudes);
      Array.Clear(_smoothedMagnitudes);
      _inputPosition = 0;
    }
  }

  /// <summary>
  /// Performs Cooley-Tukey FFT in place.
  /// </summary>
  private static void FFT(Complex[] buffer)
  {
    var n = buffer.Length;

    // Bit reversal permutation
    var bits = (int)Math.Log2(n);
    for (var i = 0; i < n; i++)
    {
      var j = BitReverse(i, bits);
      if (j > i)
      {
        (buffer[i], buffer[j]) = (buffer[j], buffer[i]);
      }
    }

    // Cooley-Tukey iterative FFT
    for (var len = 2; len <= n; len *= 2)
    {
      var ang = 2 * Math.PI / len;
      var wlen = new Complex(Math.Cos(ang), -Math.Sin(ang));

      for (var i = 0; i < n; i += len)
      {
        var w = Complex.One;
        for (var j = 0; j < len / 2; j++)
        {
          var u = buffer[i + j];
          var v = buffer[i + j + len / 2] * w;
          buffer[i + j] = u + v;
          buffer[i + j + len / 2] = u - v;
          w *= wlen;
        }
      }
    }
  }

  /// <summary>
  /// Reverses the bits of an integer.
  /// </summary>
  private static int BitReverse(int n, int bits)
  {
    var reversed = 0;
    for (var i = 0; i < bits; i++)
    {
      reversed = (reversed << 1) | (n & 1);
      n >>= 1;
    }
    return reversed;
  }

  /// <summary>
  /// Checks if a number is a power of two.
  /// </summary>
  private static bool IsPowerOfTwo(int n) => n > 0 && (n & (n - 1)) == 0;
}

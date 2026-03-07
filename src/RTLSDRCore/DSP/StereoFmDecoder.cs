namespace RTLSDRCore.DSP;

/// <summary>
/// FM stereo multiplex (MPX) decoder.
///
/// Broadcast FM stereo encodes audio as:
///   0–15 kHz   : L+R (mono-compatible sum)
///   19 kHz     : Pilot tone (identifies stereo transmission)
///   23–53 kHz  : DSB-SC modulated L-R (carrier suppressed at 38 kHz)
///
/// Decoding steps:
///   1. Bandpass filter to isolate 19 kHz pilot
///   2. PLL locks to pilot, generates phase-coherent 38 kHz carrier
///   3. Multiply composite by 38 kHz carrier → recovers L-R baseband
///   4. Low-pass filter L+R and L-R to 15 kHz
///   5. Matrix decode: L = (L+R + L-R) / 2, R = (L+R - L-R) / 2
/// </summary>
public class StereoFmDecoder
{
  private readonly int _sampleRate;

  // Pilot detection: 19 kHz bandpass filter (narrow)
  private readonly BandPassFilter _pilotBpf;

  // PLL state for tracking pilot phase
  private float _pllPhase;
  private float _pllFrequency;
  private readonly float _pllCenterFrequency;
  private readonly float _pllBandwidth;
  private readonly float _pllAlpha; // proportional gain
  private readonly float _pllBeta;  // integral gain

  // Audio filters: 15 kHz LPF for L+R and L-R
  private readonly LowPassFilter _lrSumFilter;
  private readonly LowPassFilter _lrDiffFilter;

  // Pilot strength tracking for stereo/mono detection
  private float _pilotStrength;
  private const float PilotThreshold = 0.01f; // minimum pilot level for stereo
  private const float PilotSmoothingAlpha = 0.001f; // slow tracking

  /// <summary>
  /// Whether a stereo pilot was detected in the most recent block.
  /// </summary>
  public bool StereoDetected => _pilotStrength > PilotThreshold;

  /// <summary>
  /// Current pilot signal strength (smoothed RMS).
  /// </summary>
  public float PilotStrength => _pilotStrength;

  /// <summary>
  /// Current PLL phase in radians (0 to 2π), tracking the 19 kHz pilot.
  /// Used by RDS decoder to derive the phase-coherent 57 kHz (3× pilot) carrier.
  /// </summary>
  public float PllPhase => _pllPhase;

  /// <summary>
  /// Current PLL-tracked frequency in Hz (~19 kHz when locked).
  /// Used by RDS decoder to compute the 57 kHz carrier step rate.
  /// </summary>
  public float PllFrequency => _pllFrequency;

  /// <summary>
  /// Creates a new FM stereo decoder.
  /// </summary>
  /// <param name="sampleRate">Sample rate of the demodulated composite signal (e.g., 240000).</param>
  public StereoFmDecoder(int sampleRate)
  {
    _sampleRate = sampleRate;

    // 19 kHz bandpass for pilot extraction (narrow: 18.5–19.5 kHz)
    _pilotBpf = new BandPassFilter(sampleRate, 18500f, 19500f, taps: 127);

    // PLL tuned to 19 kHz
    _pllCenterFrequency = 19000f;
    _pllFrequency = _pllCenterFrequency;
    _pllPhase = 0;

    // PLL loop bandwidth ~50 Hz — fast enough to acquire, narrow enough to reject noise
    _pllBandwidth = 50f;
    var omega = 2.0f * MathF.PI * _pllBandwidth / sampleRate;
    var dampingFactor = 0.707f; // critically damped
    _pllAlpha = 2.0f * dampingFactor * omega;
    _pllBeta = omega * omega;

    // 15 kHz LPF for audio recovery (65 taps is adequate at 240 kHz)
    _lrSumFilter = new LowPassFilter(sampleRate, 15000f, taps: 65);
    _lrDiffFilter = new LowPassFilter(sampleRate, 15000f, taps: 65);
  }

  /// <summary>
  /// Decodes a block of FM composite (MPX) mono samples into interleaved stereo.
  /// </summary>
  /// <param name="composite">Input: demodulated FM composite signal at demod sample rate.</param>
  /// <param name="stereoOutput">Output: interleaved L, R samples (must be 2× input length).</param>
  /// <returns>Number of stereo sample pairs written (= input length).</returns>
  public int Decode(ReadOnlySpan<float> composite, Span<float> stereoOutput)
  {
    var count = composite.Length;

    for (int i = 0; i < count; i++)
    {
      var sample = composite[i];

      // 1. Extract pilot tone via bandpass filter
      var pilot = _pilotBpf.Process(sample);

      // Track pilot strength (smoothed absolute value)
      var absPilot = MathF.Abs(pilot);
      _pilotStrength += PilotSmoothingAlpha * (absPilot - _pilotStrength);

      // 2. PLL tracks the pilot phase
      // Phase detector: multiply pilot with PLL quadrature output
      var pllSin = MathF.Sin(_pllPhase);
      var pllCos = MathF.Cos(_pllPhase);
      var phaseError = pilot * pllCos; // error signal (zero when locked)

      // Loop filter (2nd order)
      _pllFrequency += _pllBeta * phaseError;
      _pllPhase += 2.0f * MathF.PI * _pllFrequency / _sampleRate
                   + _pllAlpha * phaseError;

      // Keep phase in [0, 2π)
      if (_pllPhase > 2.0f * MathF.PI)
      {
        _pllPhase -= 2.0f * MathF.PI;
      }
      else if (_pllPhase < 0)
      {
        _pllPhase += 2.0f * MathF.PI;
      }

      // 3. Generate 38 kHz carrier from doubled PLL phase
      var carrier38 = MathF.Sin(2.0f * _pllPhase);

      // 4. Recover L+R and L-R
      var lrSum = _lrSumFilter.Process(sample);
      var lrDiff = _lrDiffFilter.Process(sample * carrier38 * 2.0f);

      float left, right;
      if (StereoDetected)
      {
        // 5. Matrix decode
        left = (lrSum + lrDiff) * 0.5f;
        right = (lrSum - lrDiff) * 0.5f;
      }
      else
      {
        // No pilot — output mono (avoid noise from L-R channel)
        left = lrSum;
        right = lrSum;
      }

      stereoOutput[i * 2] = left;
      stereoOutput[i * 2 + 1] = right;
    }

    return count;
  }

  /// <summary>
  /// Resets all filter and PLL state.
  /// </summary>
  public void Reset()
  {
    _pilotBpf.Reset();
    _lrSumFilter.Reset();
    _lrDiffFilter.Reset();
    _pllPhase = 0;
    _pllFrequency = _pllCenterFrequency;
    _pilotStrength = 0;
  }
}

/// <summary>
/// Bandpass FIR filter (combines low-pass and high-pass).
/// Passes frequencies between lowCutoff and highCutoff.
/// </summary>
public class BandPassFilter
{
  private readonly float[] _coefficients;
  private readonly float[] _buffer;
  private int _bufferIndex;

  /// <summary>
  /// Creates a bandpass FIR filter.
  /// </summary>
  /// <param name="sampleRate">Sample rate in Hz.</param>
  /// <param name="lowCutoff">Lower cutoff frequency in Hz.</param>
  /// <param name="highCutoff">Upper cutoff frequency in Hz.</param>
  /// <param name="taps">Number of filter taps (odd recommended).</param>
  public BandPassFilter(int sampleRate, float lowCutoff, float highCutoff, int taps = 63)
  {
    _coefficients = GenerateCoefficients(sampleRate, lowCutoff, highCutoff, taps);
    _buffer = new float[taps];
    _bufferIndex = 0;
  }

  private static float[] GenerateCoefficients(int sampleRate, float lowCutoff, float highCutoff, int taps)
  {
    var coeffs = new float[taps];
    var fcLow = lowCutoff / sampleRate;
    var fcHigh = highCutoff / sampleRate;
    var center = taps / 2;

    for (var i = 0; i < taps; i++)
    {
      var n = i - center;
      if (n == 0)
      {
        coeffs[i] = 2.0f * (fcHigh - fcLow);
      }
      else
      {
        // sinc(highCutoff) - sinc(lowCutoff) = bandpass
        coeffs[i] = (MathF.Sin(2 * MathF.PI * fcHigh * n) -
                      MathF.Sin(2 * MathF.PI * fcLow * n)) / (MathF.PI * n);
      }

      // Blackman-Harris window (better sidelobe rejection than Hamming for narrow BPF)
      var w = 2.0f * MathF.PI * i / (taps - 1);
      coeffs[i] *= 0.35875f - 0.48829f * MathF.Cos(w)
                   + 0.14128f * MathF.Cos(2 * w)
                   - 0.01168f * MathF.Cos(3 * w);
    }

    // Normalize to unity gain at center frequency
    var centerFreq = (lowCutoff + highCutoff) / 2.0f;
    var sumReal = 0f;
    var sumImag = 0f;
    for (var i = 0; i < taps; i++)
    {
      var phase = 2.0f * MathF.PI * centerFreq / sampleRate * i;
      sumReal += coeffs[i] * MathF.Cos(phase);
      sumImag += coeffs[i] * MathF.Sin(phase);
    }
    var magnitude = MathF.Sqrt(sumReal * sumReal + sumImag * sumImag);
    if (magnitude > 0.001f)
    {
      for (var i = 0; i < taps; i++)
      {
        coeffs[i] /= magnitude;
      }
    }

    return coeffs;
  }

  /// <summary>
  /// Processes a single sample through the filter.
  /// </summary>
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
  /// Resets the filter state.
  /// </summary>
  public void Reset()
  {
    Array.Clear(_buffer);
    _bufferIndex = 0;
  }
}

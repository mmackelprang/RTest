using RTLSDRCore.Models;

namespace RTLSDRCore.DSP;

  /// <summary>
  /// Amplitude Modulation (AM) demodulator using envelope detection
  /// </summary>
  /// <remarks>
  /// The output is the envelope with its DC (the carrier) removed by a one-pole DC blocker. The
  /// blocker's time constant is in seconds (<see cref="DcBlockerTimeConstantSeconds"/>), so it
  /// behaves the same at any <see cref="SampleRate"/>. After construction and after
  /// <see cref="Reset"/>, the blocker is seeded with the mean envelope of the first non-empty
  /// block, so the carrier does not come out as a large DC step that decays over the time constant
  /// (AUD-101: such a step would also drive the AGC that follows down for its decay time).
  /// </remarks>
  public class AmDemodulator : IDemodulator
  {
      private float _dcBlockerState;
      private bool _dcSeeded;
      private float _dcBlockerAlpha;
      private int _alphaSampleRate;
      private double _alphaTimeConstant;

      /// <inheritdoc/>
      public string Name => "AM";

      /// <inheritdoc/>
      public int SampleRate { get; set; } = 2_400_000;

      /// <inheritdoc/>
      /// <remarks>Not read by the demodulator: the channel filter in front of it sets the bandwidth.</remarks>
      public int Bandwidth { get; set; } = 10_000;

      /// <summary>
      /// Gets or sets the DC blocker's time constant, in seconds. Default 41.7 ms (a high-pass corner
      /// of about 3.8 Hz), which is what the former fixed coefficient of 0.9999 gave at 240 kS/s.
      /// </summary>
      public double DcBlockerTimeConstantSeconds { get; set; } = 0.0417;

      /// <inheritdoc/>
      public int Demodulate(ReadOnlySpan<IqSample> input, Span<float> output)
      {
          var outputCount = Math.Min(input.Length, output.Length);
          if (outputCount == 0)
          {
              return 0;
          }

          if (_alphaSampleRate != SampleRate || _alphaTimeConstant != DcBlockerTimeConstantSeconds)
          {
              _alphaSampleRate = SampleRate;
              _alphaTimeConstant = DcBlockerTimeConstantSeconds;
              _dcBlockerAlpha = (float)Math.Exp(-1.0 / (DcBlockerTimeConstantSeconds * Math.Max(1, SampleRate)));
          }

          if (!_dcSeeded)
          {
              var sum = 0f;
              for (var i = 0; i < outputCount; i++)
              {
                  sum += input[i].Magnitude;
              }
              _dcBlockerState = sum / outputCount;
              _dcSeeded = true;
          }

          var alpha = _dcBlockerAlpha;
          var oneMinusAlpha = 1 - alpha;
          for (var i = 0; i < outputCount; i++)
          {
              // Envelope detection: magnitude of the complex signal
              var magnitude = input[i].Magnitude;

              // DC blocking filter to remove the carrier
              _dcBlockerState = alpha * _dcBlockerState + oneMinusAlpha * magnitude;
              output[i] = magnitude - _dcBlockerState;
          }

          return outputCount;
      }

      /// <inheritdoc/>
      /// <remarks>The DC blocker is seeded again from the next non-empty block.</remarks>
      public void Reset()
      {
          _dcBlockerState = 0;
          _dcSeeded = false;
      }
  }

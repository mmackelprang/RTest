using Radio.Infrastructure.Audio.Validation;
using SoundFlow.Abstracts;

namespace Radio.Infrastructure.Audio.SoundFlow;

/// <summary>
/// A passthrough audio modifier that taps audio samples for diagnostic validation.
/// Follows the same pattern as VisualizationTapModifier — buffers samples, copies on full,
/// submits to IAudioValidator. Zero effect on audio (pure passthrough).
/// </summary>
public class AudioValidatorTapModifier : SoundModifier
{
  private readonly IAudioValidator _validator;
  private readonly string _stageName;
  private readonly float[] _sampleBuffer;
  private readonly int _bufferSize;
  private int _bufferIndex;
  private readonly object _lock = new();

  /// <summary>
  /// Creates a new validator tap modifier.
  /// </summary>
  /// <param name="validator">The audio validator to submit samples to.</param>
  /// <param name="stageName">Pipeline stage identifier for log correlation.</param>
  /// <param name="bufferSize">Sample buffer size before submitting (default 4096).</param>
  public AudioValidatorTapModifier(
    IAudioValidator validator,
    string stageName = "V3-Mixer",
    int bufferSize = 4096)
  {
    _validator = validator;
    _stageName = stageName;
    _bufferSize = bufferSize;
    _sampleBuffer = new float[bufferSize];
    _bufferIndex = 0;
    Name = $"Validator Tap ({stageName})";
  }

  /// <inheritdoc/>
  public override float ProcessSample(float sample, int channel)
  {
    lock (_lock)
    {
      if (_bufferIndex < _bufferSize)
      {
        _sampleBuffer[_bufferIndex++] = sample;
      }

      if (_bufferIndex >= _bufferSize)
      {
        try
        {
          _validator.Submit(_sampleBuffer.AsSpan(0, _bufferSize), _stageName);
        }
        catch
        {
          // Ignore validation errors — best-effort tap
        }

        _bufferIndex = 0;
      }
    }

    // Pass through unchanged
    return sample;
  }

  /// <summary>
  /// Flushes remaining buffered samples to the validator.
  /// </summary>
  public void Flush()
  {
    lock (_lock)
    {
      if (_bufferIndex > 0)
      {
        try
        {
          _validator.Submit(_sampleBuffer.AsSpan(0, _bufferIndex), _stageName);
        }
        catch
        {
          // Ignore
        }

        _bufferIndex = 0;
      }
    }
  }

  /// <summary>
  /// Resets the sample buffer.
  /// </summary>
  public void Reset()
  {
    lock (_lock)
    {
      _bufferIndex = 0;
      Array.Clear(_sampleBuffer, 0, _bufferSize);
    }
  }
}

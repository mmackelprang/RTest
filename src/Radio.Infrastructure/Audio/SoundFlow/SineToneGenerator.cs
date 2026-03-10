using SoundFlow.Abstracts;
using SoundFlow.Structs;

namespace Radio.Infrastructure.Audio.SoundFlow;

/// <summary>
/// A SoundFlow audio component that generates a stereo diagnostic tone.
/// Left channel: 200Hz sine, Right channel: 300Hz sine.
/// Uses integer sample index (not phase accumulator) for drift-free precision.
/// </summary>
public class SineToneGenerator : SoundComponent
{
  private readonly int _leftHz;
  private readonly int _rightHz;
  private readonly float _amplitude;
  private readonly int _sampleRate;
  private long _sampleIndex;

  /// <summary>
  /// Creates a new sine tone generator.
  /// </summary>
  /// <param name="engine">The SoundFlow audio engine.</param>
  /// <param name="format">The audio format (must be stereo).</param>
  /// <param name="leftHz">Left channel frequency in Hz (default 200).</param>
  /// <param name="rightHz">Right channel frequency in Hz (default 300).</param>
  /// <param name="amplitude">Amplitude 0.0 to 1.0 (default 0.8).</param>
  public SineToneGenerator(
    AudioEngine engine,
    AudioFormat format,
    int leftHz = 200,
    int rightHz = 300,
    float amplitude = 0.8f)
    : base(engine, format)
  {
    _leftHz = leftHz;
    _rightHz = rightHz;
    _amplitude = amplitude;
    _sampleRate = format.SampleRate;
    Name = "Diagnostic Tone";
  }

  /// <inheritdoc/>
  protected override void GenerateAudio(Span<float> buffer, int channels)
  {
    var frames = buffer.Length / channels;
    for (var i = 0; i < frames; i++)
    {
      var idx = i * channels;
      var n = _sampleIndex++;

      // Left channel: leftHz sine
      buffer[idx] = (float)(Math.Sin(2.0 * Math.PI * _leftHz * n / _sampleRate) * _amplitude);

      // Right channel: rightHz sine
      if (channels > 1)
      {
        buffer[idx + 1] = (float)(Math.Sin(2.0 * Math.PI * _rightHz * n / _sampleRate) * _amplitude);
      }

      // Fill any extra channels with silence
      for (var ch = 2; ch < channels; ch++)
      {
        buffer[idx + ch] = 0f;
      }
    }
  }
}

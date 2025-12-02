namespace Radio.Core.Models.Audio;

/// <summary>
/// Represents a buffer of audio samples for fingerprinting.
/// </summary>
public sealed record AudioSampleBuffer
{
  /// <summary>Gets the raw audio samples as float values (-1.0 to 1.0).</summary>
  public required float[] Samples { get; init; }

  /// <summary>Gets the sample rate in Hz.</summary>
  public required int SampleRate { get; init; }

  /// <summary>Gets the number of audio channels.</summary>
  public required int Channels { get; init; }

  /// <summary>Gets the duration of the audio buffer.</summary>
  public required TimeSpan Duration { get; init; }

  /// <summary>Gets the source name or identifier (optional).</summary>
  public string? SourceName { get; init; }

  /// <summary>
  /// Calculates the number of samples per channel.
  /// </summary>
  public int SamplesPerChannel => Samples.Length / Channels;
}

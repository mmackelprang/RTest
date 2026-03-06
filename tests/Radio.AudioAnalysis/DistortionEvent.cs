namespace Radio.AudioAnalysis;

/// <summary>
/// Types of audio distortion that can be detected by waveform comparison.
/// </summary>
public enum DistortionType
{
  /// <summary>Unexpected silence (zero samples) inserted into the audio stream.</summary>
  SilenceInsertion,

  /// <summary>One or more samples repeated verbatim (buffer stall / stuck pointer).</summary>
  RepeatedSamples,

  /// <summary>Samples missing from the stream, causing a time discontinuity.</summary>
  DroppedSamples,

  /// <summary>Sample values clipped at +1.0 or -1.0 (hard clipping).</summary>
  AmplitudeClipping,

  /// <summary>Overall gain differs from reference (e.g., volume scaling error).</summary>
  GainError,

  /// <summary>Byte-level corruption causing sample misalignment.</summary>
  ByteShiftCorruption,

  /// <summary>Non-linear distortion producing harmonic frequencies.</summary>
  HarmonicDistortion,

  /// <summary>Left and right channels swapped relative to reference.</summary>
  ChannelSwap
}

/// <summary>
/// A detected distortion event within a captured audio segment.
/// </summary>
/// <param name="Type">The type of distortion detected.</param>
/// <param name="SampleOffset">Sample index where the event starts (interleaved).</param>
/// <param name="Duration">Number of samples affected.</param>
/// <param name="Severity">Severity on 0.0-1.0 scale (0 = negligible, 1 = severe).</param>
/// <param name="Description">Human-readable description of the event.</param>
public record DistortionEvent(
  DistortionType Type,
  int SampleOffset,
  int Duration,
  float Severity,
  string Description);

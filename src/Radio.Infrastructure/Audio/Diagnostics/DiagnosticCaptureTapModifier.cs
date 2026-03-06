using Radio.Infrastructure.Audio.SoundFlow;

namespace Radio.Infrastructure.Audio.Diagnostics;

/// <summary>
/// A passthrough audio tap that captures samples into a <see cref="CaptureSession"/>.
/// Extends <see cref="BufferedTapModifier"/> — created on-demand, attached to mixer
/// only during diagnostic capture, then detached.
/// </summary>
public class DiagnosticCaptureTapModifier : BufferedTapModifier
{
  private readonly CaptureSession _session;

  /// <summary>
  /// Creates a new capture tap that forwards flushed samples to a CaptureSession.
  /// </summary>
  /// <param name="session">The capture session to write samples into.</param>
  /// <param name="bufferSize">BufferedTapModifier buffer size (samples per flush).</param>
  public DiagnosticCaptureTapModifier(CaptureSession session, int bufferSize = 4096)
    : base(bufferSize)
  {
    _session = session ?? throw new ArgumentNullException(nameof(session));
    Name = $"DiagnosticCaptureTap ({session.StageName})";
  }

  /// <summary>
  /// Called on ThreadPool when the buffer is full.
  /// Forwards the buffer contents to the capture session.
  /// </summary>
  protected override void ProcessFlushBuffer(float[] buffer)
  {
    _session.AddSamples(buffer);
  }
}

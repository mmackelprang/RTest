using Radio.Core.Interfaces.Audio;
using SoundFlow.Structs;

namespace Radio.Infrastructure.Audio.SoundFlow;

/// <summary>
/// A passthrough audio modifier that taps audio samples and forwards them
/// to the visualization service without modifying the audio.
/// </summary>
public class VisualizationTapModifier : BufferedTapModifier
{
  private readonly IVisualizerService _visualizerService;
  private readonly AudioFormat _format;

  /// <summary>
  /// Initializes a new instance of the <see cref="VisualizationTapModifier"/> class.
  /// </summary>
  /// <param name="visualizerService">The visualizer service to send samples to.</param>
  /// <param name="format">The audio format.</param>
  /// <param name="bufferSize">Size of the sample buffer before sending to visualizer (default: 2048).</param>
  public VisualizationTapModifier(
    IVisualizerService visualizerService,
    AudioFormat format,
    int bufferSize = 2048)
    : base(bufferSize)
  {
    _visualizerService = visualizerService ?? throw new ArgumentNullException(nameof(visualizerService));
    _format = format;
    Name = "Visualization Tap";
  }

  /// <inheritdoc/>
  protected override void ProcessFlushBuffer(float[] buffer)
  {
    _visualizerService.ProcessSamples(buffer);
  }
}

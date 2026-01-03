using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces.Audio;
using SoundFlow.Abstracts;
using SoundFlow.Structs;

namespace Radio.Infrastructure.Audio.SoundFlow;

/// <summary>
/// A passthrough audio modifier that taps audio samples and forwards them
/// to the visualization service without modifying the audio.
/// </summary>
public class VisualizationTapModifier : SoundModifier
{
  private readonly IVisualizerService _visualizerService;
  private readonly AudioFormat _format;
  private readonly float[] _sampleBuffer;
  private readonly int _bufferSize;
  private int _bufferIndex;
  private readonly object _lock = new();
  private int _totalSamplesProcessed;
  private DateTime _lastLogTime = DateTime.MinValue;

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
  {
    _visualizerService = visualizerService ?? throw new ArgumentNullException(nameof(visualizerService));
    _format = format;
    _bufferSize = bufferSize;
    _sampleBuffer = new float[bufferSize];
    _bufferIndex = 0;
    Name = "Visualization Tap";
  }

  /// <inheritdoc/>
  public override float ProcessSample(float sample, int channel)
  {
    // Collect samples in the buffer
    lock (_lock)
    {
      _totalSamplesProcessed++;

      if (_bufferIndex < _bufferSize)
      {
        _sampleBuffer[_bufferIndex++] = sample;
      }

      // When buffer is full, send to visualizer and reset
      if (_bufferIndex >= _bufferSize)
      {
        try
        {
          // Create a copy to avoid issues with the lock
          var samplesForVisualizer = new float[_bufferSize];
          Array.Copy(_sampleBuffer, samplesForVisualizer, _bufferSize);

          // Log periodically (every 5 seconds)
          var now = DateTime.UtcNow;
          if ((now - _lastLogTime).TotalSeconds >= 5)
          {
            Console.WriteLine($"[VisualizationTap] Sending {_bufferSize} samples to visualizer (total: {_totalSamplesProcessed})");
            _lastLogTime = now;
          }

          // Process samples synchronously to ensure they reach the visualizer
          _visualizerService.ProcessSamples(samplesForVisualizer);
        }
        catch (Exception ex)
        {
          Console.WriteLine($"[VisualizationTap] Error: {ex.Message}");
        }

        _bufferIndex = 0;
      }
    }

    // Pass through unchanged - this is a tap, not an effect
    return sample;
  }

  /// <summary>
  /// Flushes any remaining samples in the buffer to the visualizer.
  /// </summary>
  public void Flush()
  {
    lock (_lock)
    {
      if (_bufferIndex > 0)
      {
        try
        {
          var remainingSamples = new float[_bufferIndex];
          Array.Copy(_sampleBuffer, remainingSamples, _bufferIndex);
          _visualizerService.ProcessSamples(remainingSamples);
        }
        catch
        {
          // Ignore visualization errors
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

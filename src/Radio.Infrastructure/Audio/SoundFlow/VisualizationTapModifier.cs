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
  private readonly float[] _flushBuffer;
  private readonly int _bufferSize;
  private int _bufferIndex;
  private readonly object _lock = new();
  private volatile bool _flushInProgress;

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
    _flushBuffer = new float[bufferSize];
    _bufferIndex = 0;
    Name = "Visualization Tap";
  }

  /// <inheritdoc/>
  public override float ProcessSample(float sample, int channel)
  {
    // Hot path: called 96,000 times/second for stereo 48kHz.
    // Only buffer samples on the audio thread — FFT, RMS, and waveform
    // analysis are offloaded to ThreadPool to avoid blocking the callback.
    bool shouldFlush = false;
    lock (_lock)
    {
      if (_bufferIndex < _bufferSize)
      {
        _sampleBuffer[_bufferIndex++] = sample;
      }

      // When buffer is full, copy to flush buffer and reset
      if (_bufferIndex >= _bufferSize)
      {
        if (!_flushInProgress)
        {
          Array.Copy(_sampleBuffer, _flushBuffer, _bufferSize);
          shouldFlush = true;
        }
        _bufferIndex = 0;
      }
    }

    // Heavy work (mono conversion, FFT, RMS, waveform) runs on ThreadPool.
    // Previously this ran synchronously on the audio thread, causing
    // periodic distortion every ~21ms (2048 samples at 48kHz stereo).
    if (shouldFlush)
    {
      _flushInProgress = true;
      ThreadPool.QueueUserWorkItem(_ =>
      {
        try
        {
          _visualizerService.ProcessSamples(_flushBuffer);
        }
        catch (Exception)
        {
          // Ignore visualization errors — best-effort tap
        }
        finally
        {
          _flushInProgress = false;
        }
      });
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

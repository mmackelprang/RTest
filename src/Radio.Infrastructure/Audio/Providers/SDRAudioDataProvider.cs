using Microsoft.Extensions.Logging;
using RTLSDRCore;
using SoundFlow.Interfaces;
using SoundFlow.Structs;

namespace Radio.Infrastructure.Audio.Providers;

/// <summary>
/// Custom SoundFlow data provider that bridges RTLSDRCore's AudioDataAvailable event
/// to the SoundFlow audio pipeline.
/// </summary>
public class SDRAudioDataProvider : ISoundDataProvider
{
  private readonly RadioReceiver _radioReceiver;
  private readonly ILogger<SDRAudioDataProvider> _logger;
  private readonly object _lock = new();
  private readonly Queue<float> _audioBuffer = new();
  private AudioFormat _format;
  private bool _isPlaying;
  private bool _disposed;

  /// <summary>
  /// Initializes a new instance of the <see cref="SDRAudioDataProvider"/> class.
  /// </summary>
  /// <param name="radioReceiver">The RTL-SDR radio receiver.</param>
  /// <param name="logger">The logger instance.</param>
  public SDRAudioDataProvider(RadioReceiver radioReceiver, ILogger<SDRAudioDataProvider> logger)
  {
    _radioReceiver = radioReceiver ?? throw new ArgumentNullException(nameof(radioReceiver));
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    // Get audio format from radio receiver
    _format = new AudioFormat
    {
      SampleRate = radioReceiver.AudioFormat.SampleRate,
      Channels = radioReceiver.AudioFormat.Channels
    };

    // Subscribe to audio data events
    _radioReceiver.AudioDataAvailable += OnAudioDataAvailable;

    _logger.LogDebug("SDRAudioDataProvider initialized with SampleRate={SampleRate}, Channels={Channels}",
      _format.SampleRate, _format.Channels);
  }

  /// <inheritdoc/>
  public AudioFormat Format => _format;

  /// <inheritdoc/>
  public long Position => 0; // Live stream has no position

  /// <inheritdoc/>
  public long Length => -1; // Live stream has no length

  /// <inheritdoc/>
  public bool IsSeekable => false; // Live stream cannot be seeked

  /// <inheritdoc/>
  public bool IsPlaying
  {
    get => _isPlaying;
    set
    {
      if (_isPlaying != value)
      {
        _isPlaying = value;
        _logger.LogDebug("SDRAudioDataProvider IsPlaying changed to {IsPlaying}", value);
      }
    }
  }

  /// <inheritdoc/>
  public int Read(Span<float> buffer)
  {
    if (_disposed)
    {
      return 0;
    }

    lock (_lock)
    {
      var samplesRead = 0;
      var maxSamples = Math.Min(buffer.Length, _audioBuffer.Count);

      for (int i = 0; i < maxSamples; i++)
      {
        buffer[i] = _audioBuffer.Dequeue();
        samplesRead++;
      }

      // If we don't have enough samples, fill the rest with silence
      for (int i = samplesRead; i < buffer.Length; i++)
      {
        buffer[i] = 0f;
      }

      return samplesRead;
    }
  }

  /// <inheritdoc/>
  public bool Seek(long position)
  {
    // Live stream cannot be seeked
    return false;
  }

  /// <summary>
  /// Handles audio data from RTLSDRCore RadioReceiver.
  /// </summary>
  private void OnAudioDataAvailable(object? sender, AudioDataEventArgs e)
  {
    if (_disposed || !_isPlaying)
    {
      return;
    }

    lock (_lock)
    {
      // Add samples to buffer
      foreach (var sample in e.Samples)
      {
        _audioBuffer.Enqueue(sample);
      }

      // Limit buffer size to prevent excessive memory usage
      // Keep max 5 seconds of audio buffered
      var maxBufferSize = _format.SampleRate * _format.Channels * 5;
      while (_audioBuffer.Count > maxBufferSize)
      {
        _audioBuffer.Dequeue();
      }
    }
  }

  /// <inheritdoc/>
  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }

    _disposed = true;

    // Unsubscribe from events
    _radioReceiver.AudioDataAvailable -= OnAudioDataAvailable;

    // Clear buffer
    lock (_lock)
    {
      _audioBuffer.Clear();
    }

    _logger.LogDebug("SDRAudioDataProvider disposed");
  }
}

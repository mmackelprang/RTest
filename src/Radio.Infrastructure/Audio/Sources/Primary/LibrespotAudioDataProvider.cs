using Microsoft.Extensions.Logging;
using Radio.Infrastructure.Audio.Services;
using System.Collections.Concurrent;

namespace Radio.Infrastructure.Audio.Sources.Primary;

/// <summary>
/// Custom audio data provider for librespot integrated Spotify audio streams.
/// Buffers PCM audio samples from librespot stdout pipe and provides them to the audio engine.
/// This class acts as a bridge between LibrespotManager's audio buffer
/// and the SoundFlow audio pipeline.
/// </summary>
/// <remarks>
/// This implementation uses a concurrent queue to buffer audio samples
/// from the librespot process. The audio engine reads from this buffer in real-time.
/// 
/// Audio format from librespot pipe backend:
/// - Sample rate: 44.1kHz (standard Spotify output)
/// - Channels: 2 (stereo)
/// - Format: PCM 16-bit signed integers (little-endian)
/// - Byte order: Little-endian (Intel/AMD standard)
/// 
/// The audio data comes as raw PCM bytes from librespot's stdout,
/// which is read by LibrespotManager and queued for consumption.
/// </remarks>
public class LibrespotAudioDataProvider : IDisposable
{
  private readonly LibrespotManager _librespotManager;
  private readonly ILogger _logger;
  private readonly ConcurrentQueue<byte[]> _localBuffer;
  private readonly int _maxLocalBufferChunks;
  private bool _isDisposed;
  private long _totalSamplesReceived;
  private long _totalSamplesDropped;
  private bool _isPolling;
  private Task? _pollingTask;
  private CancellationTokenSource? _pollingCts;

  /// <summary>
  /// Initializes a new instance of the <see cref="LibrespotAudioDataProvider"/> class.
  /// </summary>
  /// <param name="librespotManager">The librespot manager providing PCM audio data.</param>
  /// <param name="logger">Logger for diagnostic output.</param>
  /// <param name="maxLocalBufferChunks">Maximum number of audio chunks to buffer locally (default: 10).</param>
  public LibrespotAudioDataProvider(
    LibrespotManager librespotManager,
    ILogger logger,
    int maxLocalBufferChunks = 10)
  {
    _librespotManager = librespotManager ?? throw new ArgumentNullException(nameof(librespotManager));
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _localBuffer = new ConcurrentQueue<byte[]>();
    _maxLocalBufferChunks = maxLocalBufferChunks;

    _logger.LogDebug(
      "LibrespotAudioDataProvider created: SampleRate={SampleRate}Hz, Channels={Channels}, BitsPerSample={BitsPerSample}",
      SampleRate,
      Channels,
      BitsPerSample);

    // Subscribe to audio data events from LibrespotManager
    _librespotManager.AudioDataReceived += OnAudioDataReceived;
  }

  /// <summary>
  /// Gets the sample rate (44.1kHz for Spotify).
  /// </summary>
  public int SampleRate => 44100;

  /// <summary>
  /// Gets the number of audio channels (2 for stereo).
  /// </summary>
  public int Channels => 2;

  /// <summary>
  /// Gets the bits per sample (16-bit PCM).
  /// </summary>
  public int BitsPerSample => 16;

  /// <summary>
  /// Gets the number of audio sample chunks currently buffered locally.
  /// </summary>
  public int BufferedChunks => _localBuffer.Count;

  /// <summary>
  /// Gets the total number of audio samples received from librespot.
  /// </summary>
  public long TotalSamplesReceived => _totalSamplesReceived;

  /// <summary>
  /// Gets the total number of audio samples dropped due to buffer overflow.
  /// </summary>
  public long TotalSamplesDropped => _totalSamplesDropped;

  /// <summary>
  /// Starts polling for audio data from the LibrespotManager.
  /// This should be called when playback begins.
  /// </summary>
  public void StartPolling()
  {
    if (_isPolling || _isDisposed)
    {
      return;
    }

    _isPolling = true;
    _pollingCts = new CancellationTokenSource();
    _pollingTask = Task.Run(() => PollAudioDataAsync(_pollingCts.Token));
    _logger.LogDebug("Started polling for librespot audio data");
  }

  /// <summary>
  /// Stops polling for audio data.
  /// This should be called when playback stops or pauses.
  /// </summary>
  public void StopPolling()
  {
    if (!_isPolling)
    {
      return;
    }

    _isPolling = false;
    _pollingCts?.Cancel();
    _pollingTask?.Wait(TimeSpan.FromSeconds(2));
    _pollingCts?.Dispose();
    _pollingCts = null;
    _pollingTask = null;
    _logger.LogDebug("Stopped polling for librespot audio data");
  }

  /// <summary>
  /// Tries to dequeue an audio chunk from the local buffer.
  /// </summary>
  /// <param name="audioData">The dequeued audio data, or null if buffer is empty.</param>
  /// <returns>True if data was dequeued, false if buffer is empty.</returns>
  public bool TryDequeueAudioData(out byte[]? audioData)
  {
    return _localBuffer.TryDequeue(out audioData);
  }

  /// <summary>
  /// Handles audio data events from the LibrespotManager.
  /// Queues the PCM audio samples for consumption by the audio engine.
  /// </summary>
  /// <param name="sender">The event sender (LibrespotManager).</param>
  /// <param name="e">Audio data event arguments containing PCM samples.</param>
  private void OnAudioDataReceived(object? sender, AudioDataEventArgs e)
  {
    if (_isDisposed || !_isPolling)
    {
      return;
    }

    _totalSamplesReceived += e.AudioData.Length / (BitsPerSample / 8);

    // Queue the audio samples for playback
    // Note: We clone the array to avoid issues if LibrespotManager reuses the buffer
    var audioDataCopy = e.AudioData.ToArray();

    if (_localBuffer.Count < _maxLocalBufferChunks)
    {
      _localBuffer.Enqueue(audioDataCopy);
    }
    else
    {
      // Buffer overflow - drop oldest chunk and add new one
      _localBuffer.TryDequeue(out _);
      _localBuffer.Enqueue(audioDataCopy);
      _totalSamplesDropped += audioDataCopy.Length / (BitsPerSample / 8);
      _logger.LogDebug("Local buffer overflow, dropped oldest chunk");
    }
  }

  /// <summary>
  /// Continuously polls the LibrespotManager for audio data.
  /// This is a fallback mechanism in case events are not sufficient.
  /// </summary>
  private async Task PollAudioDataAsync(CancellationToken cancellationToken)
  {
    try
    {
      while (!cancellationToken.IsCancellationRequested && !_isDisposed)
      {
        // Try to get audio data from the manager's buffer
        while (_librespotManager.TryDequeueAudioData(out var audioData))
        {
          if (audioData != null)
          {
            _totalSamplesReceived += audioData.Length / (BitsPerSample / 8);

            if (_localBuffer.Count < _maxLocalBufferChunks)
            {
              _localBuffer.Enqueue(audioData);
            }
            else
            {
              // Buffer overflow - drop oldest chunk and add new one
              _localBuffer.TryDequeue(out _);
              _localBuffer.Enqueue(audioData);
              _totalSamplesDropped += audioData.Length / (BitsPerSample / 8);
              _logger.LogDebug("Local buffer overflow during polling, dropped oldest chunk");
            }
          }
        }

        // Sleep briefly to avoid busy-waiting
        await Task.Delay(10, cancellationToken);
      }
    }
    catch (OperationCanceledException)
    {
      // Normal cancellation
      _logger.LogDebug("Audio polling cancelled");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error during audio data polling");
    }
  }

  /// <summary>
  /// Disposes the audio data provider and releases resources.
  /// </summary>
  public void Dispose()
  {
    if (_isDisposed)
    {
      return;
    }

    _isDisposed = true;

    StopPolling();

    // Unsubscribe from events
    _librespotManager.AudioDataReceived -= OnAudioDataReceived;

    _logger.LogDebug(
      "LibrespotAudioDataProvider disposed. Total samples received: {TotalReceived}, dropped: {TotalDropped}",
      _totalSamplesReceived,
      _totalSamplesDropped);
  }
}

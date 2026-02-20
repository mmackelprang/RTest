using System.Text.Json;
using Microsoft.Extensions.Logging;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;

namespace Radio.Infrastructure.Audio.Outputs;

/// <summary>
/// Streams audio directly over the Cast protocol's custom message bus.
/// Reads PCM from the audio engine's tapped output stream, wraps each chunk
/// in a WAV header, Base64-encodes it, and sends it as a JSON message to the
/// Cast receiver via <see cref="DirectCastAudioChannel"/>.
/// </summary>
/// <remarks>
/// This bypasses the HTTP MP3 streaming path entirely, eliminating the
/// encode → HTTP fetch → decode round-trip and potentially reducing latency
/// from 4-10 seconds to under 1 second.
///
/// Audio math at 48kHz, 16-bit, stereo (192KB/sec PCM):
/// <list type="table">
///   <listheader>
///     <term>Chunk Size</term><description>PCM bytes | Base64 | Msgs/sec</description>
///   </listheader>
///   <item><term>50ms</term><description>9,600 | ~12.8KB | 20</description></item>
///   <item><term>100ms</term><description>19,200 | ~25.6KB | 10</description></item>
///   <item><term>200ms</term><description>38,400 | ~51.2KB | 5</description></item>
/// </list>
/// All sizes are within the Cast protocol's 64KB message limit.
/// </remarks>
public sealed class DirectCastStreamingService : IAsyncDisposable
{
  private readonly ILogger _logger;
  private readonly IAudioEngine _audioEngine;
  private readonly DirectCastAudioChannel _channel;
  private readonly GoogleCastOutputOptions _options;

  private string? _transportId;
  private Stream? _streamReader;
  private CancellationTokenSource? _cts;
  private Task? _streamingTask;
  private long _sequenceNumber;
  private bool _disposed;

  // Diagnostics
  private long _totalChunksSent;
  private long _totalBytesSent;
  private long _sendErrors;
  private DateTime _lastChunkTime;

  /// <summary>
  /// Gets the total number of audio chunks sent to the Cast device.
  /// </summary>
  public long TotalChunksSent => _totalChunksSent;

  /// <summary>
  /// Gets the total Base64-encoded bytes sent.
  /// </summary>
  public long TotalBytesSent => _totalBytesSent;

  /// <summary>
  /// Gets the number of send errors encountered.
  /// </summary>
  public long SendErrors => _sendErrors;

  /// <summary>
  /// Gets the time of the last successfully sent chunk.
  /// </summary>
  public DateTime LastChunkTime => _lastChunkTime;

  /// <summary>
  /// Gets whether the service is currently streaming.
  /// </summary>
  public bool IsStreaming => _streamingTask != null && !_streamingTask.IsCompleted;

  /// <summary>
  /// Initializes a new instance of the <see cref="DirectCastStreamingService"/> class.
  /// </summary>
  /// <param name="logger">Logger instance.</param>
  /// <param name="audioEngine">Audio engine for creating stream readers.</param>
  /// <param name="channel">The custom Cast channel for sending audio messages.</param>
  /// <param name="options">Google Cast output configuration.</param>
  public DirectCastStreamingService(
    ILogger logger,
    IAudioEngine audioEngine,
    DirectCastAudioChannel channel,
    GoogleCastOutputOptions options)
  {
    _logger = logger;
    _audioEngine = audioEngine;
    _channel = channel;
    _options = options;
  }

  /// <summary>
  /// Sets the transport ID of the launched Cast receiver application.
  /// This is the destination for all audio messages.
  /// </summary>
  /// <param name="transportId">The receiver's transport ID from LaunchApplicationAsync.</param>
  public void SetTransportId(string transportId)
  {
    _transportId = transportId;
    _logger.LogInformation("DirectCast: Transport ID set to {TransportId}", transportId);
  }

  /// <summary>
  /// Starts streaming audio to the Cast device.
  /// Creates a stream reader and begins the background send loop.
  /// </summary>
  public void Start()
  {
    if (IsStreaming)
    {
      _logger.LogWarning("DirectCast: Already streaming, ignoring Start()");
      return;
    }

    if (string.IsNullOrEmpty(_transportId))
    {
      throw new InvalidOperationException("Transport ID not set. Call SetTransportId() first.");
    }

    _cts = new CancellationTokenSource();
    _sequenceNumber = 0;
    _totalChunksSent = 0;
    _totalBytesSent = 0;
    _sendErrors = 0;

    // Create a stream reader with minimal lag for lowest latency.
    // The reader's built-in real-time pacing prevents tight-loop spinning.
    _streamReader = _audioEngine.CreateStreamReader("direct-cast", lagSeconds: 0.05);

    _logger.LogInformation(
      "DirectCast: Starting streaming — chunk size {ChunkMs}ms, namespace {Namespace}",
      _options.DirectChannelChunkSizeMs, _options.DirectChannelNamespace);

    _streamingTask = Task.Run(() => StreamingLoopAsync(_cts.Token));
  }

  /// <summary>
  /// Stops streaming and releases the stream reader.
  /// </summary>
  public async Task StopAsync()
  {
    if (!IsStreaming)
    {
      return;
    }

    _logger.LogInformation(
      "DirectCast: Stopping streaming — sent {Chunks} chunks, {Bytes} bytes, {Errors} errors",
      _totalChunksSent, _totalBytesSent, _sendErrors);

    _cts?.Cancel();

    if (_streamingTask != null)
    {
      try
      {
        await _streamingTask.WaitAsync(TimeSpan.FromSeconds(5));
      }
      catch (TimeoutException)
      {
        _logger.LogWarning("DirectCast: Streaming task did not stop within 5 seconds");
      }
      catch (OperationCanceledException)
      {
        // Expected
      }
    }

    _streamReader?.Dispose();
    _streamReader = null;
    _cts?.Dispose();
    _cts = null;
  }

  /// <summary>
  /// Main streaming loop. Reads PCM audio, encodes as WAV chunks, and sends
  /// over the Cast channel at a rate determined by the chunk size.
  /// </summary>
  private async Task StreamingLoopAsync(CancellationToken ct)
  {
    // Calculate chunk size in bytes from milliseconds
    // At 48kHz, 16-bit, stereo: 192,000 bytes/sec = 192 bytes/ms
    var chunkMs = Math.Clamp(_options.DirectChannelChunkSizeMs, 50, 200);
    var sampleRate = 48000; // Matches AudioEngine default
    var channels = 2;
    var bitsPerSample = 16;
    var bytesPerMs = sampleRate * channels * (bitsPerSample / 8) / 1000;
    var chunkBytes = chunkMs * bytesPerMs;

    var pcmBuffer = new byte[chunkBytes];

    _logger.LogInformation(
      "DirectCast: Streaming loop started — {ChunkMs}ms chunks = {ChunkBytes} bytes PCM, " +
      "{MsgsPerSec} msgs/sec target",
      chunkMs, chunkBytes, 1000 / chunkMs);

    try
    {
      while (!ct.IsCancellationRequested)
      {
        // Read a full chunk of PCM audio.
        // TappedOutputStreamReader.ReadAsync paces itself to real-time when no
        // data is available, so this naturally throttles to the correct rate.
        var totalRead = 0;
        while (totalRead < chunkBytes && !ct.IsCancellationRequested)
        {
          var bytesRead = await _streamReader!.ReadAsync(
            pcmBuffer, totalRead, chunkBytes - totalRead, ct);
          if (bytesRead == 0)
          {
            // Reader returned 0 — engine may be shutting down
            await Task.Delay(10, ct);
            continue;
          }
          totalRead += bytesRead;
        }

        if (ct.IsCancellationRequested) break;

        // Wrap PCM in WAV header for self-contained decoding on receiver
        var wavChunk = WavChunkEncoder.Encode(pcmBuffer, totalRead, sampleRate, channels, bitsPerSample);

        // Base64-encode and build JSON message
        var base64 = Convert.ToBase64String(wavChunk);
        var seq = Interlocked.Increment(ref _sequenceNumber);
        var message = JsonSerializer.Serialize(new
        {
          type = "audio",
          data = base64,
          seq
        });

        // Send over the Cast channel
        try
        {
          await _channel.SendMessageAsync(message, _transportId!);
          Interlocked.Increment(ref _totalChunksSent);
          Interlocked.Add(ref _totalBytesSent, message.Length);
          _lastChunkTime = DateTime.UtcNow;

          // Periodic diagnostics
          if (_totalChunksSent % 100 == 0)
          {
            _logger.LogDebug(
              "DirectCast: Sent {Chunks} chunks ({MB:F1} MB), seq {Seq}, {Errors} errors",
              _totalChunksSent, _totalBytesSent / 1_000_000.0, seq, _sendErrors);
          }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
          break;
        }
        catch (Exception ex)
        {
          Interlocked.Increment(ref _sendErrors);

          // Log periodically to avoid spam on persistent errors
          if (_sendErrors <= 3 || _sendErrors % 50 == 0)
          {
            _logger.LogWarning(ex,
              "DirectCast: Failed to send chunk seq {Seq} (error #{ErrorCount})",
              seq, _sendErrors);
          }

          // Back off briefly on errors to avoid tight-loop retries
          await Task.Delay(100, ct);
        }
      }
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
      // Normal shutdown
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "DirectCast: Streaming loop terminated unexpectedly");
    }

    _logger.LogInformation("DirectCast: Streaming loop ended");
  }

  /// <inheritdoc />
  public async ValueTask DisposeAsync()
  {
    if (_disposed) return;
    _disposed = true;

    await StopAsync();
  }
}

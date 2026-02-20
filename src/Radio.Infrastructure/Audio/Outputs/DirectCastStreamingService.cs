using System.Text.Json;
using Microsoft.Extensions.Logging;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.SoundFlow;

namespace Radio.Infrastructure.Audio.Outputs;

/// <summary>
/// Streams audio directly over the Cast protocol's custom message bus.
/// Reads PCM from the audio engine's tapped output stream, encodes each chunk
/// as MP3 via LAME, Base64-encodes it, and sends it as a JSON message to the
/// Cast receiver via <see cref="DirectCastAudioChannel"/>.
/// </summary>
/// <remarks>
/// This bypasses the HTTP MP3 streaming path entirely, eliminating the
/// encode → HTTP fetch → decode round-trip and potentially reducing latency
/// from 4-10 seconds to under 1 second.
///
/// The receiver uses Media Source Extensions (MSE) with an audio/mpeg
/// SourceBuffer to continuously append MP3 frames for gapless playback.
/// A persistent LAME encoder maintains the bit reservoir across chunks,
/// ensuring consistent encoding quality (no startup artifacts).
///
/// Audio math at 48kHz, 16-bit, stereo (192KB/sec PCM, 320kbps MP3):
/// <list type="table">
///   <listheader>
///     <term>Chunk Size</term><description>PCM bytes | ~MP3 bytes | Msgs/sec</description>
///   </listheader>
///   <item><term>50ms</term><description>9,600 | ~2KB | 20</description></item>
///   <item><term>100ms</term><description>19,200 | ~4KB | 10</description></item>
///   <item><term>200ms</term><description>38,400 | ~8KB | 5</description></item>
/// </list>
/// All sizes are well within the Cast protocol's 64KB message limit.
/// MP3 encoding reduces Base64 overhead by ~6x compared to WAV.
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
  private bool _lastChunkWasSilence = true;

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

    // Log output tap diagnostics to help debug data flow issues
    if (_audioEngine is SoundFlowAudioEngine sfEngine)
    {
      var tapDiag = sfEngine.GetOutputTapDiagnostics();
      if (tapDiag.HasValue)
      {
        _logger.LogInformation(
          "DirectCast: Output tap state — {WriteCalls} writes, {Bytes} bytes, " +
          "writePos: {WritePos}, readers: {Readers}, lastWrite: {LastWrite}",
          tapDiag.Value.TotalWriteCalls, tapDiag.Value.TotalBytesWritten,
          tapDiag.Value.WritePosition, tapDiag.Value.ActiveReaderCount,
          tapDiag.Value.LastWriteTime);
      }
      else
      {
        _logger.LogWarning("DirectCast: Output tap diagnostics not available (tap is null)!");
      }
    }

    // Use 1 second of lag to ensure the reader starts with real audio data.
    // The reader starts behind the write position by this amount, giving it
    // immediate access to recently-written audio. With too-small lag (e.g. 50ms),
    // the reader can start at the write position with no data available, and
    // then receive silence indefinitely because silence reads don't advance
    // the read pointer.
    _streamReader = _audioEngine.CreateStreamReader("direct-cast", lagSeconds: 1.0);

    // Log initial reader state
    if (_streamReader is TappedOutputStreamReader tapReader)
    {
      _logger.LogInformation(
        "DirectCast: Reader created — id: {ReaderId}, initialAvailable: {Available} bytes",
        tapReader.ReaderId, tapReader.Available);
    }

    _logger.LogInformation(
      "DirectCast: Starting streaming — chunk size {ChunkMs}ms, namespace {Namespace}, lag: 1.0s",
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
  /// Main streaming loop. Reads raw PCM audio and sends it directly over the
  /// Cast channel. The receiver converts Int16LE samples to Float32 and plays
  /// via Web Audio API — no encode/decode step means zero gap at chunk boundaries.
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
      "raw Int16LE (no MP3), {MsgsPerSec} msgs/sec target, ~{Base64KB:F1}KB Base64/msg",
      chunkMs, chunkBytes, 1000 / chunkMs, chunkBytes * 4.0 / 3 / 1024);

    // Real-time pacing: the TappedOutputStreamReader may return buffered data
    // much faster than real-time (ring buffer backlog). Use a stopwatch to
    // enforce that we never send faster than one chunk per chunkMs.
    var pacingTimer = System.Diagnostics.Stopwatch.StartNew();
    long chunksSinceStart = 0;

    try
    {
      while (!ct.IsCancellationRequested)
      {
        // Read a full chunk of PCM audio
        var totalRead = 0;
        var readCalls = 0;
        while (totalRead < chunkBytes && !ct.IsCancellationRequested)
        {
          var bytesRead = await _streamReader!.ReadAsync(
            pcmBuffer, totalRead, chunkBytes - totalRead, ct);
          readCalls++;
          if (bytesRead == 0)
          {
            // Reader returned 0 — engine may be shutting down
            await Task.Delay(10, ct);
            continue;
          }
          totalRead += bytesRead;

          // Detailed per-read diagnostics for first 5 chunks
          if (chunksSinceStart < 5 && _streamReader is TappedOutputStreamReader tapReader)
          {
            var hasNonZero = false;
            for (var b = totalRead - bytesRead; b < totalRead && !hasNonZero; b++)
              if (pcmBuffer[b] != 0) hasNonZero = true;
            _logger.LogInformation(
              "DirectCast: Read #{Call}: {BytesRead} bytes, nonZero: {NonZero}, availAfter: {Avail}, totalSoFar: {Total}/{Target}",
              readCalls, bytesRead, hasNonZero, tapReader.Available, totalRead, chunkBytes);
          }
        }

        if (ct.IsCancellationRequested) break;

        // Real-time pacing: wait until it's time to send this chunk.
        chunksSinceStart++;
        var expectedElapsed = TimeSpan.FromMilliseconds(chunksSinceStart * chunkMs);
        var actualElapsed = pacingTimer.Elapsed;
        if (expectedElapsed > actualElapsed)
        {
          var delay = expectedElapsed - actualElapsed;
          await Task.Delay(delay, ct);
        }

        // Detect silence transitions
        {
          var isSilence = true;
          for (var i = 0; i < totalRead && isSilence; i++)
          {
            if (pcmBuffer[i] != 0) isSilence = false;
          }

          if (chunksSinceStart <= 10 || isSilence != _lastChunkWasSilence)
          {
            _logger.LogInformation(
              "DirectCast: Chunk {Seq} — {BytesRead} bytes PCM, silence: {IsSilence}",
              chunksSinceStart, totalRead, isSilence);
          }
          _lastChunkWasSilence = isSilence;
        }

        // Send raw PCM directly — no MP3 encoding.
        // The receiver converts Int16LE to Float32 and plays via Web Audio API.
        var base64 = Convert.ToBase64String(pcmBuffer, 0, totalRead);
        var seq = Interlocked.Increment(ref _sequenceNumber);
        var message = JsonSerializer.Serialize(new
        {
          type = "audio",
          data = base64,
          seq,
          fmt = "pcm",
          sr = sampleRate,
          ch = channels
        });

        // Send over the Cast channel
        try
        {
          await _channel.SendMessageAsync(message, _transportId!);
          Interlocked.Increment(ref _totalChunksSent);
          Interlocked.Add(ref _totalBytesSent, message.Length);
          _lastChunkTime = DateTime.UtcNow;

          // Periodic diagnostics (every 100 chunks = ~10 seconds at 100ms)
          if (_totalChunksSent % 100 == 0)
          {
            var rate = _totalChunksSent / pacingTimer.Elapsed.TotalSeconds;
            _logger.LogDebug(
              "DirectCast: Sent {Chunks} chunks ({MB:F1} MB), seq {Seq}, " +
              "rate: {Rate:F1}/sec, {Errors} errors",
              _totalChunksSent, _totalBytesSent / 1_000_000.0, seq, rate, _sendErrors);
          }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
          break;
        }
        catch (Exception ex)
        {
          Interlocked.Increment(ref _sendErrors);

          if (_sendErrors <= 3 || _sendErrors % 50 == 0)
          {
            _logger.LogWarning(ex,
              "DirectCast: Failed to send chunk seq {Seq} (error #{ErrorCount})",
              seq, _sendErrors);
          }

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

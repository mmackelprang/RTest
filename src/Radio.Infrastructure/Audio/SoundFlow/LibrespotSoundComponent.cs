using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SoundFlow.Abstracts;
using SoundFlow.Structs;

namespace Radio.Infrastructure.Audio.SoundFlow;

/// <summary>
/// A pull-based SoundFlow component that reads audio from librespot's stdout pipe.
///
/// Design: SoundFlow drives timing by calling GenerateAudio when it needs samples.
/// This component reads from the pipe only when needed, creating natural backpressure
/// that rate-limits librespot to real-time playback.
///
/// The pipe itself acts as the primary buffer. When we don't read, the pipe fills up,
/// and librespot blocks on its write calls.
/// </summary>
public class LibrespotSoundComponent : SoundComponent
{
  private readonly ILogger _logger;
  private readonly Stream _pipeStream;
  private readonly BlockingCollection<float[]> _sampleQueue;
  private readonly CancellationTokenSource _cts;
  private readonly Task _readerTask;
  private readonly int _inputSampleRate;
  private readonly int _outputSampleRate;
  private readonly int _channels;
  private bool _isDisposed;

  // Circular buffer for leftover samples between GenerateAudio calls
  private float[] _leftoverSamples = Array.Empty<float>();
  private int _leftoverOffset;

  // Audio flow tracking
  private bool _hasLoggedFirstAudio;
  private long _totalChunksProcessed;
  private long _totalSamplesDelivered;

  /// <summary>
  /// Creates a new LibrespotSoundComponent.
  /// </summary>
  /// <param name="engine">The SoundFlow audio engine.</param>
  /// <param name="outputFormat">The output audio format (from SoundFlow engine).</param>
  /// <param name="pipeStream">The librespot stdout pipe stream.</param>
  /// <param name="logger">Logger for diagnostics.</param>
  /// <param name="inputSampleRate">Librespot's output sample rate (default: 44100).</param>
  public LibrespotSoundComponent(
    AudioEngine engine,
    AudioFormat outputFormat,
    Stream pipeStream,
    ILogger logger,
    int inputSampleRate = 44100)
    : base(engine, outputFormat)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _pipeStream = pipeStream ?? throw new ArgumentNullException(nameof(pipeStream));
    _inputSampleRate = inputSampleRate;
    _outputSampleRate = outputFormat.SampleRate;
    _channels = outputFormat.Channels;

    // Small bounded queue - holds about 200ms of audio
    // This creates backpressure: when full, the reader blocks, which blocks pipe reads,
    // which blocks librespot writes
    int chunksToBuffer = 10; // ~230ms at 44.1kHz with 1024-sample chunks
    _sampleQueue = new BlockingCollection<float[]>(chunksToBuffer);

    _cts = new CancellationTokenSource();
    _readerTask = Task.Run(() => ReadPipeAsync(_cts.Token));

    Name = "Librespot Audio";
    _logger.LogInformation(
      "LibrespotSoundComponent created: InputRate={InputRate}Hz, OutputRate={OutputRate}Hz, Channels={Channels}",
      _inputSampleRate, _outputSampleRate, _channels);
  }

  /// <summary>
  /// Background task that reads from the librespot pipe and queues samples.
  /// This task blocks on both pipe reads (waiting for data) and queue adds (when full).
  /// The blocking on queue adds is what creates backpressure to librespot.
  /// </summary>
  private async Task ReadPipeAsync(CancellationToken ct)
  {
    // Librespot outputs 16-bit signed stereo PCM
    // Read in chunks of ~23ms (1024 frames at 44.1kHz)
    const int FramesPerChunk = 1024;
    const int BytesPerSample = 2;
    int bytesPerFrame = _channels * BytesPerSample;
    int bytesPerChunk = FramesPerChunk * bytesPerFrame;
    var buffer = new byte[bytesPerChunk];

    _logger.LogDebug("Librespot pipe reader started");

    try
    {
      while (!ct.IsCancellationRequested)
      {
        // Read from pipe - this blocks until data is available
        int totalRead = 0;
        while (totalRead < bytesPerChunk && !ct.IsCancellationRequested)
        {
          int bytesRead = await _pipeStream.ReadAsync(
            buffer, totalRead, bytesPerChunk - totalRead, ct);

          if (bytesRead == 0)
          {
            _logger.LogInformation("Librespot pipe closed (EOF)");
            _sampleQueue.CompleteAdding();
            return;
          }
          totalRead += bytesRead;
        }

        if (ct.IsCancellationRequested) break;

        // Convert 16-bit PCM to float samples
        int frameCount = totalRead / bytesPerFrame;
        int sampleCount = frameCount * _channels;
        var floatSamples = new float[sampleCount];

        var shortSpan = MemoryMarshal.Cast<byte, short>(buffer.AsSpan(0, totalRead));
        for (int i = 0; i < shortSpan.Length && i < floatSamples.Length; i++)
        {
          floatSamples[i] = shortSpan[i] / 32768.0f;
        }

        // Log first audio data
        if (!_hasLoggedFirstAudio)
        {
          _hasLoggedFirstAudio = true;
          _logger.LogInformation(
            "🎵 SPOTIFY AUDIO FLOW STARTED: First audio chunk received from librespot pipe " +
            "(SampleRate={InputRate}Hz, Channels={Channels}, ChunkSamples={SampleCount})",
            _inputSampleRate, _channels, sampleCount);
        }

        _totalChunksProcessed++;

        // Add to queue - THIS BLOCKS when queue is full!
        // This is the key backpressure mechanism.
        // When the queue is full, we stop reading from the pipe.
        // When we stop reading, the pipe buffer fills up.
        // When the pipe buffer is full, librespot's writes block.
        // This rate-limits librespot to our consumption rate.
        try
        {
          _sampleQueue.Add(floatSamples, ct);
        }
        catch (InvalidOperationException)
        {
          // Queue was completed
          break;
        }
      }
    }
    catch (OperationCanceledException)
    {
      _logger.LogDebug("Librespot pipe reader cancelled");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error reading from librespot pipe");
    }
    finally
    {
      if (!_sampleQueue.IsAddingCompleted)
      {
        _sampleQueue.CompleteAdding();
      }
      _logger.LogDebug("Librespot pipe reader stopped");
    }
  }

  /// <summary>
  /// Called by SoundFlow when it needs audio samples.
  /// This is the "pull" - SoundFlow's playback rate drives when we consume data.
  /// </summary>
  protected override void GenerateAudio(Span<float> buffer, int channels)
  {
    if (_isDisposed)
    {
      buffer.Clear();
      return;
    }

    int samplesWritten = 0;
    int samplesNeeded = buffer.Length;

    // First, use any leftover samples from previous call
    if (_leftoverSamples.Length > 0 && _leftoverOffset < _leftoverSamples.Length)
    {
      int leftoverAvailable = _leftoverSamples.Length - _leftoverOffset;
      int toCopy = Math.Min(leftoverAvailable, samplesNeeded);
      _leftoverSamples.AsSpan(_leftoverOffset, toCopy).CopyTo(buffer);
      samplesWritten += toCopy;
      _leftoverOffset += toCopy;

      // Clear leftovers if fully consumed
      if (_leftoverOffset >= _leftoverSamples.Length)
      {
        _leftoverSamples = Array.Empty<float>();
        _leftoverOffset = 0;
      }
    }

    // Get more chunks from the queue as needed
    while (samplesWritten < samplesNeeded)
    {
      // Try to get a chunk without blocking (we're on the audio thread)
      if (!_sampleQueue.TryTake(out var chunk))
      {
        // No data available - fill rest with silence
        // This is an underrun, but it's better than blocking the audio thread
        if (_hasLoggedFirstAudio && samplesWritten == 0)
        {
          _logger.LogDebug("🎵 SPOTIFY: Audio underrun - no data available, filling with silence");
        }
        buffer.Slice(samplesWritten).Clear();
        return;
      }
      _totalSamplesDelivered += chunk.Length;

      // Handle sample rate conversion if needed
      float[] processedChunk = chunk;
      if (_inputSampleRate != _outputSampleRate)
      {
        processedChunk = ResampleChunk(chunk);
      }

      int available = processedChunk.Length;
      int needed = samplesNeeded - samplesWritten;

      if (available <= needed)
      {
        // Use entire chunk
        processedChunk.AsSpan().CopyTo(buffer.Slice(samplesWritten));
        samplesWritten += available;
      }
      else
      {
        // Use part of chunk, save rest for next call
        processedChunk.AsSpan(0, needed).CopyTo(buffer.Slice(samplesWritten));
        samplesWritten += needed;

        // Store leftover
        _leftoverSamples = processedChunk;
        _leftoverOffset = needed;
      }
    }
  }

  /// <summary>
  /// Simple linear resampling from input to output sample rate.
  /// </summary>
  private float[] ResampleChunk(float[] input)
  {
    if (_inputSampleRate == _outputSampleRate)
      return input;

    double ratio = (double)_outputSampleRate / _inputSampleRate;
    int inputFrames = input.Length / _channels;
    int outputFrames = (int)(inputFrames * ratio);
    var output = new float[outputFrames * _channels];

    for (int outFrame = 0; outFrame < outputFrames; outFrame++)
    {
      double inFramePos = outFrame / ratio;
      int inFrame0 = (int)inFramePos;
      int inFrame1 = Math.Min(inFrame0 + 1, inputFrames - 1);
      double frac = inFramePos - inFrame0;

      for (int ch = 0; ch < _channels; ch++)
      {
        float s0 = input[inFrame0 * _channels + ch];
        float s1 = input[inFrame1 * _channels + ch];
        output[outFrame * _channels + ch] = (float)(s0 + (s1 - s0) * frac);
      }
    }

    return output;
  }

  /// <summary>
  /// Stops the pipe reader and cleans up resources.
  /// </summary>
  public void Stop()
  {
    if (!_cts.IsCancellationRequested)
    {
      _logger.LogInformation(
        "🎵 SPOTIFY AUDIO FLOW STOPPING: Cancelling pipe reader " +
        "(ChunksProcessed={Chunks}, SamplesDelivered={Samples})",
        _totalChunksProcessed, _totalSamplesDelivered);
      _cts.Cancel();
    }
  }

  protected override void Dispose(bool disposing)
  {
    if (_isDisposed)
    {
      base.Dispose(disposing);
      return;
    }

    if (disposing)
    {
      _isDisposed = true;
      _cts.Cancel();

      // Wait briefly for reader to stop
      if (!_readerTask.Wait(1000))
      {
        _logger.LogWarning("Librespot pipe reader did not stop within timeout");
      }

      _sampleQueue.Dispose();
      _cts.Dispose();

      _logger.LogInformation(
        "🎵 SPOTIFY AUDIO FLOW STOPPED: LibrespotSoundComponent disposed " +
        "(TotalChunks={Chunks}, TotalSamples={Samples})",
        _totalChunksProcessed, _totalSamplesDelivered);
    }

    base.Dispose(disposing);
  }
}

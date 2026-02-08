using Microsoft.Extensions.Logging;
using SoundFlow.Abstracts;
using SoundFlow.Structs;
using System.Runtime.InteropServices;

namespace Radio.Infrastructure.Audio.SoundFlow;

/// <summary>
/// Strategy for handling buffer overflow in BufferedSoundGenerator.
/// </summary>
public enum BufferOverflowStrategy
{
    /// <summary>
    /// Drop the oldest samples to make room for new ones. 
    /// Suitable for live streams (e.g., SDR) where latency is more important than continuity.
    /// </summary>
    DropOldest,

    /// <summary>
    /// Block the adding thread until space is available.
    /// Suitable for file playback to provide backpressure to the source.
    /// </summary>
    Block
}

/// <summary>
/// A generic SoundFlow audio component that buffers audio samples from an external source
/// and outputs them to the SoundFlow mixer.
/// Supports float (SDR) and short sample types.
/// </summary>
/// <typeparam name="T">The sample type (float or short).</typeparam>
public class BufferedSoundGenerator<T> : SoundComponent where T : struct
{
    private readonly ILogger _logger;
    private readonly object _bufferLock = new();
    private readonly Queue<T> _sampleBuffer = new();
    private readonly int _maxBufferSamples;
    private readonly BufferOverflowStrategy _overflowStrategy;
    private bool _isDisposed;
    private long _totalSamplesReceived;
    private long _totalSamplesDropped;
    private long _totalSamplesOutput;
    private DateTime _lastLogTime = DateTime.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="BufferedSoundGenerator{T}"/> class.
    /// </summary>
    /// <param name="engine">The SoundFlow audio engine.</param>
    /// <param name="format">The audio format for output.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <param name="maxBufferSeconds">Maximum seconds of audio to buffer (default: 2).</param>
    /// <param name="overflowStrategy">Strategy for buffer overflow (default: DropOldest).</param>
    public BufferedSoundGenerator(
        AudioEngine engine,
        AudioFormat format,
        ILogger logger,
        float maxBufferSeconds = 2.0f,
        BufferOverflowStrategy overflowStrategy = BufferOverflowStrategy.DropOldest)
        : base(engine, format)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _overflowStrategy = overflowStrategy;

        // Calculate max buffer based on output format
        // Note: This assumes input sample rate matches output sample rate.
        var samplesPerSecond = format.SampleRate * format.Channels;
        _maxBufferSamples = (int)(samplesPerSecond * maxBufferSeconds);

        Name = $"Buffered Generator ({typeof(T).Name})";

        _logger.LogDebug(
            "BufferedSoundGenerator created: Type={Type}, OutputSampleRate={SampleRate}Hz, OutputChannels={Channels}, MaxBufferSamples={MaxBuffer}, Strategy={Strategy}",
            typeof(T).Name, format.SampleRate, format.Channels, _maxBufferSamples, _overflowStrategy);
    }

    /// <summary>
    /// Adds samples to the buffer.
    /// </summary>
    /// <param name="samples">The samples to add.</param>
    public void AddSamples(ReadOnlySpan<T> samples)
    {
        if (_isDisposed) return;

        lock (_bufferLock)
        {
            if (_overflowStrategy == BufferOverflowStrategy.Block)
            {
                // If strategy is block, we wait until there is room.
                while ((_sampleBuffer.Count + samples.Length > _maxBufferSamples) && !_isDisposed)
                {
                    Monitor.Wait(_bufferLock);
                }
                
                if (_isDisposed) return;
            }

            _totalSamplesReceived += samples.Length;

            foreach (var sample in samples)
            {
                if (_sampleBuffer.Count >= _maxBufferSamples)
                {
                    if (_overflowStrategy == BufferOverflowStrategy.DropOldest)
                    {
                        _sampleBuffer.Dequeue();
                        _totalSamplesDropped++;
                    }
                    else
                    {
                        // In Block mode, we normally shouldn't get here unless checks above failed (e.g. huge chunk)
                        // Forced drop if absolutely necessary
                        _sampleBuffer.Dequeue();
                        _totalSamplesDropped++;
                        if (_totalSamplesDropped % 1000 == 0) // throttling log
                           _logger.LogDebug("BufferedSoundGenerator forced to drop sample in Block mode. Buffer full.");
                    }
                }
                _sampleBuffer.Enqueue(sample);
            }
        }
    }

    /// <summary>
    /// Generates audio samples for the SoundFlow mixer.
    /// </summary>
    protected override void GenerateAudio(Span<float> buffer, int channels)
    {
        if (_isDisposed)
        {
            buffer.Clear();
            return;
        }

        int samplesWritten = 0;

        lock (_bufferLock)
        {
            // We need to fill 'buffer' with floats
            // If T is float, we can copy directly
            // If T is short, we need to convert

            if (typeof(T) == typeof(float))
            {
                while (samplesWritten < buffer.Length && _sampleBuffer.Count > 0)
                {
                    var sample = _sampleBuffer.Dequeue();
                    buffer[samplesWritten++] = (float)(object)sample;
                    _totalSamplesOutput++;
                }
            }
            else if (typeof(T) == typeof(short))
            {
                 while (samplesWritten < buffer.Length && _sampleBuffer.Count > 0)
                {
                    var sample = _sampleBuffer.Dequeue();
                    short sVal = (short)(object)sample;
                    // Convert short to float (-1.0 to 1.0)
                    buffer[samplesWritten++] = sVal / 32768.0f;
                    _totalSamplesOutput++;
                }
            }
            
            // If we consumed data, pulse any blocked producers
            if (_overflowStrategy == BufferOverflowStrategy.Block && samplesWritten > 0)
            {
                Monitor.PulseAll(_bufferLock);
            }
        }

        // Fill remainder with silence
        if (samplesWritten < buffer.Length)
        {
            buffer.Slice(samplesWritten).Fill(0);
        }

        LogStats();
    }

    private void LogStats()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastLogTime).TotalSeconds >= 10)
        {
            int currentBuffer;
            lock (_bufferLock)
            {
                currentBuffer = _sampleBuffer.Count;
            }
            
            // Don't log if completely idle (no received samples ever)
            if (_totalSamplesReceived > 0)
            {
                _logger.LogDebug(
                    "Buffered audio ({Type}): received={Received}, output={Output}, dropped={Dropped}, buffered={Buffered}",
                    typeof(T).Name, _totalSamplesReceived, _totalSamplesOutput, _totalSamplesDropped, currentBuffer);
                _lastLogTime = now;
            }
        }
    }

    /// <summary>
    /// Clears the audio buffer.
    /// </summary>
    public void ClearBuffer()
    {
        lock (_bufferLock)
        {
            _sampleBuffer.Clear();
            if (_overflowStrategy == BufferOverflowStrategy.Block)
            {
                // Waking up producers might let them fill it again, 
                // but clearing is usually done for flush/seek.
                Monitor.PulseAll(_bufferLock);
            }
        }
        _logger.LogDebug("Audio buffer cleared");
    }

    /// <summary>
    /// Gets diagnostic information about the buffer state.
    /// </summary>
    public BufferDiagnostics GetDiagnostics()
    {
        lock (_bufferLock)
        {
            return new BufferDiagnostics
            {
                TotalReceived = _totalSamplesReceived,
                TotalOutput = _totalSamplesOutput,
                TotalDropped = _totalSamplesDropped,
                BufferCount = _sampleBuffer.Count,
                BufferCapacity = _maxBufferSamples
            };
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
            lock (_bufferLock)
            {
                _isDisposed = true; // Set disposed flag INSIDE lock
                _sampleBuffer.Clear();
                Monitor.PulseAll(_bufferLock); // Wake up blocked producers
            }

            _logger.LogInformation(
                "BufferedSoundGenerator disposed. Total samples: received={Received}, output={Output}, dropped={Dropped}",
                _totalSamplesReceived, _totalSamplesOutput, _totalSamplesDropped);
        }
        else
        {
             _isDisposed = true;
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// Diagnostic snapshot of buffer state for a BufferedSoundGenerator.
/// </summary>
public struct BufferDiagnostics
{
    public long TotalReceived { get; set; }
    public long TotalOutput { get; set; }
    public long TotalDropped { get; set; }
    public int BufferCount { get; set; }
    public int BufferCapacity { get; set; }
}

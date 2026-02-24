using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces;
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
    private readonly IMetricsCollector? _metricsCollector;
    private readonly object _bufferLock = new();
    private readonly T[] _ringBuffer;
    private int _writePos;
    private int _readPos;
    private int _count;
    private readonly int _maxBufferSamples;
    private readonly BufferOverflowStrategy _overflowStrategy;
    private bool _isDisposed;
    private long _totalSamplesReceived;
    private long _totalSamplesDropped;
    private long _totalSamplesOutput;
    private long _underrunCount;
    private long _lastReportedDropped;
    private long _lastReportedUnderruns;
    private DateTime _lastLogTime = DateTime.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="BufferedSoundGenerator{T}"/> class.
    /// </summary>
    /// <param name="engine">The SoundFlow audio engine.</param>
    /// <param name="format">The audio format for output.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <param name="maxBufferSeconds">Maximum seconds of audio to buffer (default: 2).</param>
    /// <param name="overflowStrategy">Strategy for buffer overflow (default: DropOldest).</param>
    /// <param name="metricsCollector">Optional metrics collector for pipeline metrics.</param>
    public BufferedSoundGenerator(
        AudioEngine engine,
        AudioFormat format,
        ILogger logger,
        float maxBufferSeconds = 2.0f,
        BufferOverflowStrategy overflowStrategy = BufferOverflowStrategy.DropOldest,
        IMetricsCollector? metricsCollector = null)
        : base(engine, format)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metricsCollector = metricsCollector;
        _overflowStrategy = overflowStrategy;

        // Calculate max buffer based on output format
        // Note: This assumes input sample rate matches output sample rate.
        var samplesPerSecond = format.SampleRate * format.Channels;
        _maxBufferSamples = (int)(samplesPerSecond * maxBufferSeconds);
        _ringBuffer = new T[_maxBufferSamples];

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
                while ((_count + samples.Length > _maxBufferSamples) && !_isDisposed)
                {
                    Monitor.Wait(_bufferLock);
                }

                if (_isDisposed) return;
            }

            _totalSamplesReceived += samples.Length;

            var toWrite = samples.Length;

            // If incoming data exceeds free space, drop oldest samples
            var freeSpace = _maxBufferSamples - _count;
            if (toWrite > freeSpace)
            {
                var toDrop = toWrite - freeSpace;
                _readPos = (_readPos + toDrop) % _maxBufferSamples;
                _count -= toDrop;
                _totalSamplesDropped += toDrop;
            }

            // Bulk copy into ring buffer (handles wrap-around)
            var firstChunk = Math.Min(toWrite, _maxBufferSamples - _writePos);
            samples.Slice(0, firstChunk).CopyTo(_ringBuffer.AsSpan(_writePos, firstChunk));
            if (firstChunk < toWrite)
            {
                samples.Slice(firstChunk).CopyTo(_ringBuffer.AsSpan(0, toWrite - firstChunk));
            }
            _writePos = (_writePos + toWrite) % _maxBufferSamples;
            _count += toWrite;
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
            var toRead = Math.Min(buffer.Length, _count);

            if (typeof(T) == typeof(float))
            {
                // Fast path: bulk copy from ring buffer (handles wrap-around)
                // Reinterpret T[] as float[] since T is float at this branch
                var floatRing = MemoryMarshal.Cast<T, float>(_ringBuffer.AsSpan());
                var firstChunk = Math.Min(toRead, _maxBufferSamples - _readPos);
                floatRing.Slice(_readPos, firstChunk).CopyTo(buffer.Slice(0, firstChunk));
                if (firstChunk < toRead)
                {
                    floatRing.Slice(0, toRead - firstChunk)
                        .CopyTo(buffer.Slice(firstChunk, toRead - firstChunk));
                }
                samplesWritten = toRead;
            }
            else if (typeof(T) == typeof(short))
            {
                // Short → float conversion (per-sample, unavoidable)
                for (var i = 0; i < toRead; i++)
                {
                    var idx = (_readPos + i) % _maxBufferSamples;
                    short sVal = (short)(object)_ringBuffer[idx];
                    buffer[i] = sVal / 32768.0f;
                }
                samplesWritten = toRead;
            }

            _readPos = (_readPos + toRead) % _maxBufferSamples;
            _count -= toRead;
            _totalSamplesOutput += toRead;

            if (_overflowStrategy == BufferOverflowStrategy.Block && toRead > 0)
            {
                Monitor.PulseAll(_bufferLock);
            }
        }

        // Fill remainder with silence
        if (samplesWritten < buffer.Length)
        {
            buffer.Slice(samplesWritten).Fill(0);

            if (_totalSamplesReceived > 0)
            {
                _underrunCount++;
            }
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
                currentBuffer = _count;
            }

            // Don't log if completely idle (no received samples ever)
            if (_totalSamplesReceived > 0)
            {
                _logger.LogDebug(
                    "Buffered audio ({Type}): received={Received}, output={Output}, dropped={Dropped}, buffered={Buffered}",
                    typeof(T).Name, _totalSamplesReceived, _totalSamplesOutput, _totalSamplesDropped, currentBuffer);
                _lastLogTime = now;

                // Report metrics (outside lock — reads of long fields are safe for approximate values)
                if (_metricsCollector != null)
                {
                    var tags = new Dictionary<string, string> { ["type"] = typeof(T).Name };
                    var fillPercent = (double)currentBuffer / _maxBufferSamples * 100.0;
                    _metricsCollector.Gauge("audio.buffer.fill_percent", fillPercent, tags);

                    var droppedDelta = _totalSamplesDropped - _lastReportedDropped;
                    if (droppedDelta > 0)
                    {
                        _metricsCollector.Increment("audio.buffer.samples_dropped", droppedDelta, tags);
                        _lastReportedDropped = _totalSamplesDropped;
                    }

                    var underrunDelta = _underrunCount - _lastReportedUnderruns;
                    if (underrunDelta > 0)
                    {
                        _metricsCollector.Increment("audio.buffer.underruns", underrunDelta, tags);
                        _lastReportedUnderruns = _underrunCount;
                    }
                }
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
            _readPos = 0;
            _writePos = 0;
            _count = 0;
            if (_overflowStrategy == BufferOverflowStrategy.Block)
            {
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
                BufferCount = _count,
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
                _isDisposed = true;
                _readPos = 0;
                _writePos = 0;
                _count = 0;
                Monitor.PulseAll(_bufferLock);
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

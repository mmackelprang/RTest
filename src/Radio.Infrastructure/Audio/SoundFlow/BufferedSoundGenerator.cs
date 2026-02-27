using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces;
using Radio.Infrastructure.Audio.Validation;
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
    private readonly IAudioValidator? _audioValidator;
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
    private long _totalSamplesCompensated;
    private long _underrunCount;
    private long _lastReportedDropped;
    private long _lastReportedUnderruns;
    private DateTime _lastLogTime = DateTime.MinValue;
    private DateTime _lastUnderrunLogTime;
    private long _underrunSamplesSinceLastLog;
    private int _underrunCountSinceLastLog;

    // Buffer level tracking between log intervals
    private int _minBufferSinceLastLog = int.MaxValue;
    private int _maxBufferSinceLastLog;

    // Clock drift compensation: when producer (e.g., BT/PipeWire) runs on a different
    // clock than consumer (MiniAudio/ALSA), the buffer slowly drains or fills. We
    // periodically check the buffer level and duplicate a frame of samples when the
    // level drops below a threshold, preventing progressive underruns.
    private readonly int _driftCompensationThreshold; // samples
    private readonly int _driftCompensationTarget;     // samples
    private DateTime _lastDriftCheckTime;
    private int _lastDriftCheckLevel;
    private int _driftCheckCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="BufferedSoundGenerator{T}"/> class.
    /// </summary>
    /// <param name="engine">The SoundFlow audio engine.</param>
    /// <param name="format">The audio format for output.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <param name="maxBufferSeconds">Maximum seconds of audio to buffer (default: 2).</param>
    /// <param name="overflowStrategy">Strategy for buffer overflow (default: DropOldest).</param>
    /// <param name="metricsCollector">Optional metrics collector for pipeline metrics.</param>
    /// <param name="audioValidator">Optional audio validator for diagnostic pipeline taps.</param>
    public BufferedSoundGenerator(
        AudioEngine engine,
        AudioFormat format,
        ILogger logger,
        float maxBufferSeconds = 2.0f,
        BufferOverflowStrategy overflowStrategy = BufferOverflowStrategy.DropOldest,
        IMetricsCollector? metricsCollector = null,
        IAudioValidator? audioValidator = null)
        : base(engine, format)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metricsCollector = metricsCollector;
        _audioValidator = audioValidator;
        _overflowStrategy = overflowStrategy;

        // Calculate max buffer based on output format
        // Note: This assumes input sample rate matches output sample rate.
        var samplesPerSecond = format.SampleRate * format.Channels;
        _maxBufferSamples = (int)(samplesPerSecond * maxBufferSeconds);
        _ringBuffer = new T[_maxBufferSamples];

        // Clock drift compensation thresholds: check every second, compensate when
        // buffer drops below 15% of capacity, target refill to 25%.
        _driftCompensationThreshold = (int)(_maxBufferSamples * 0.15);
        _driftCompensationTarget = (int)(_maxBufferSamples * 0.25);

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

        // V2-Producer validation: submit samples after ring buffer write
        if (_audioValidator != null && typeof(T) == typeof(float))
        {
            var floatSamples = MemoryMarshal.Cast<T, float>(samples);
            _audioValidator.Submit(floatSamples, "V2-Producer");
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

            // Track min/max buffer levels between log intervals
            if (_count < _minBufferSinceLastLog) _minBufferSinceLastLog = _count;
            if (_count > _maxBufferSinceLastLog) _maxBufferSinceLastLog = _count;

            if (_overflowStrategy == BufferOverflowStrategy.Block && toRead > 0)
            {
                Monitor.PulseAll(_bufferLock);
            }
        }

        // V2-Consumer validation: submit samples after ring buffer read
        if (_audioValidator != null && samplesWritten > 0)
        {
            _audioValidator.Submit(buffer.Slice(0, samplesWritten), "V2-Consumer");
        }

        // Clock drift compensation: when the buffer is draining faster than the
        // producer fills it (e.g., BT clock vs ALSA clock drift), push back the
        // read position by one frame of audio. This duplicates the last frame,
        // which is inaudible at the sub-millisecond scale but prevents the buffer
        // from draining to zero and causing full underruns (silence gaps).
        if (samplesWritten == buffer.Length && _overflowStrategy == BufferOverflowStrategy.DropOldest)
        {
            CompensateClockDrift(channels);
        }

        // Fill remainder with silence on underrun
        if (samplesWritten < buffer.Length)
        {
            var deficit = buffer.Length - samplesWritten;
            buffer.Slice(samplesWritten).Fill(0);

            if (_totalSamplesReceived > 0)
            {
                _underrunCount++;
                _underrunSamplesSinceLastLog += deficit;
                _underrunCountSinceLastLog++;

                // Log underrun bursts: throttled to once per second to avoid log spam
                // while still revealing the pattern of when underruns occur.
                var now = DateTime.UtcNow;
                var sinceLastLog = _lastUnderrunLogTime == default
                    ? 0.0 : (now - _lastUnderrunLogTime).TotalSeconds;
                if (sinceLastLog >= 1.0 || _lastUnderrunLogTime == default)
                {
                    int buffered;
                    lock (_bufferLock) { buffered = _count; }
                    _logger.LogWarning(
                        "⚠️ Buffer underrun ({Type}): {Count} underruns, {Deficit} zero samples in last {Interval:F1}s " +
                        "(buffer: {Buffered}/{Capacity}, total underruns: {TotalUnderruns})",
                        typeof(T).Name, _underrunCountSinceLastLog, _underrunSamplesSinceLastLog,
                        sinceLastLog,
                        buffered, _maxBufferSamples, _underrunCount);
                    _underrunSamplesSinceLastLog = 0;
                    _underrunCountSinceLastLog = 0;
                    _lastUnderrunLogTime = now;
                }
            }
        }

        LogStats();
    }

    /// <summary>
    /// Detects clock drift between producer and consumer by monitoring buffer level
    /// trends. When the buffer is consistently draining (producer slower than consumer),
    /// rewinds the read pointer to duplicate recent audio and prevent underrun.
    /// This compensates for BT/PipeWire running on a different clock than ALSA playback.
    /// </summary>
    private void CompensateClockDrift(int channels)
    {
        var now = DateTime.UtcNow;
        if (_lastDriftCheckTime == default)
        {
            _lastDriftCheckTime = now;
            lock (_bufferLock) { _lastDriftCheckLevel = _count; }
            return;
        }

        // Check every ~2 seconds to smooth out transient jitter
        if ((now - _lastDriftCheckTime).TotalSeconds < 2.0) return;

        int currentLevel;
        lock (_bufferLock) { currentLevel = _count; }

        _driftCheckCount++;

        // Only compensate if:
        // 1. Buffer is below threshold (draining toward underrun)
        // 2. Level decreased since last check (sustained drain, not recovery)
        // 3. We've had at least 3 checks (avoid reacting to startup/pre-fill)
        // 4. Data has been received (not idle)
        var isDraining = currentLevel < _driftCompensationThreshold
                         && currentLevel < _lastDriftCheckLevel
                         && _driftCheckCount > 3
                         && _totalSamplesReceived > 0;

        if (isDraining)
        {
            // Rewind read pointer to duplicate recent audio back up to target level.
            // Frame-align the compensation amount to avoid splitting stereo pairs.
            var deficit = _driftCompensationTarget - currentLevel;
            var frameSamples = Math.Max(channels, 2);
            // Align to frame boundary and cap at 10ms of audio to keep it inaudible
            var maxCompensation = (int)(Format.SampleRate * Format.Channels * 0.01); // 10ms
            deficit = Math.Min(deficit, maxCompensation);
            deficit = (deficit / frameSamples) * frameSamples; // frame-align

            if (deficit > 0)
            {
                lock (_bufferLock)
                {
                    if (_count + deficit <= _maxBufferSamples)
                    {
                        _readPos = (_readPos - deficit + _maxBufferSamples) % _maxBufferSamples;
                        _count += deficit;
                        _totalSamplesCompensated += deficit;
                    }
                }

                _logger.LogInformation(
                    "🔄 Clock drift compensation: duplicated {Samples} samples (buffer: {Level}→{NewLevel}/{Capacity}, total compensated: {Total})",
                    deficit, currentLevel, currentLevel + deficit, _maxBufferSamples, _totalSamplesCompensated);
            }
        }

        _lastDriftCheckLevel = currentLevel;
        _lastDriftCheckTime = now;
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
                var minBuf = _minBufferSinceLastLog == int.MaxValue ? currentBuffer : _minBufferSinceLastLog;
                var maxBuf = _maxBufferSinceLastLog;
                var fillPct = (double)currentBuffer / _maxBufferSamples * 100.0;
                var minPct = (double)minBuf / _maxBufferSamples * 100.0;

                _logger.LogInformation(
                    "📊 Buffer ({Type}): fill={FillPct:F1}% ({Buffered}/{Capacity}), min={MinBuf} ({MinPct:F1}%), max={MaxBuf}, " +
                    "recv={Received}, out={Output}, drop={Dropped}, comp={Compensated}, under={Underruns}",
                    typeof(T).Name, fillPct, currentBuffer, _maxBufferSamples,
                    minBuf, minPct, maxBuf,
                    _totalSamplesReceived, _totalSamplesOutput,
                    _totalSamplesDropped, _totalSamplesCompensated, _underrunCount);

                _minBufferSinceLastLog = currentBuffer;
                _maxBufferSinceLastLog = currentBuffer;
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
    /// Pre-fills the buffer with silence to provide a cushion for the mixer.
    /// Call this BEFORE adding the generator to the mixer to prevent startup underruns.
    /// Also protects against brief producer stalls (USB jitter, GC pauses).
    /// </summary>
    /// <param name="seconds">Seconds of silence to pre-fill (default: 0.5).</param>
    public void PreFillSilence(float seconds = 0.5f)
    {
        var samplesToFill = (int)(Format.SampleRate * Format.Channels * seconds);
        samplesToFill = Math.Min(samplesToFill, _maxBufferSamples / 2); // Never exceed half capacity

        lock (_bufferLock)
        {
            // Fill ring buffer with zeros (silence)
            // Since the ring buffer is already zero-initialized, we just advance the write pointer
            _writePos = samplesToFill % _maxBufferSamples;
            _count = samplesToFill;
        }

        _logger.LogInformation(
            "Pre-filled buffer with {Samples} samples ({Seconds:F2}s) of silence as startup cushion",
            samplesToFill, seconds);
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
                TotalCompensated = _totalSamplesCompensated,
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
                "BufferedSoundGenerator disposed. Total samples: received={Received}, output={Output}, dropped={Dropped}, compensated={Compensated}",
                _totalSamplesReceived, _totalSamplesOutput, _totalSamplesDropped, _totalSamplesCompensated);
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
    public long TotalCompensated { get; set; }
    public int BufferCount { get; set; }
    public int BufferCapacity { get; set; }
}

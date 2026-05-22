using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces;
using Radio.Metrics;
using SoundFlow.Abstracts;
using SoundFlow.Structs;
using System.Runtime.InteropServices;

namespace Radio.Infrastructure.Audio.SoundFlow;

/// <summary>
/// Delegate for diagnostic capture hooks on BufferedSoundGenerator.
/// Uses a non-generic delegate to accept ReadOnlySpan (ref struct) parameters.
/// </summary>
public delegate void DiagnosticCaptureCallback(ReadOnlySpan<float> samples);

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
    private long _totalSamplesCompensated;
    private long _underrunCount;
    private long _lastReportedDropped;
    private long _lastReportedUnderruns;
    private DateTime _lastLogTime = DateTime.MinValue;
    private DateTime _lastUnderrunLogTime;
    private long _underrunSamplesSinceLastLog;
    private int _underrunCountSinceLastLog;
    private DateTime _lastCompensationLogTime;
    private long _compensationSamplesSinceLastLog;
    private int _compensationCountSinceLastLog;

    // Buffer level tracking between log intervals
    private int _minBufferSinceLastLog = int.MaxValue;
    private int _maxBufferSinceLastLog;

    // Instrumentation: callback timing (all allocation-free via Stopwatch.GetTimestamp)
    private long _lastGenerateAudioTimestamp;
    private double _maxCallbackIntervalMs;
    private double _minCallbackIntervalMs = double.MaxValue;
    private long _missedDeadlineCount;
    private double _maxCallbackExecutionMs;

    // Instrumentation: lock contention
    private long _addSamplesContentionCount;
    private double _maxAddSamplesLockWaitMs;
    private long _generateAudioContentionCount;
    private double _maxGenerateAudioLockWaitMs;

    // GC pause correlation — counts are sampled in LogStats (every 10s) and cached
    // via Volatile so the audio callback avoids GC.CollectionCount() syscalls.
    private int _cachedGen0Count;
    private int _cachedGen1Count;
    private int _cachedGen2Count;
    private int _prevGen0Count;
    private int _prevGen1Count;
    private int _prevGen2Count;
    private long _gcCorrelatedMissedDeadlines;

    // Throttle per-miss logging to avoid overwhelming journald
    // (high-frequency LogWarning calls cause journald CPU spike → memory pressure → more GC → feedback loop)
    private long _lastMissedDeadlineLogTicks;

    // Tracking for delta-based metrics reporting
    private long _lastReportedMissedDeadlines;
    private long _lastReportedGcCorrelatedMisses;

    // Pre-allocated metrics tags to avoid Dictionary allocation in LogStats
    private readonly Dictionary<string, string>? _metricsTags;

    // Unique identity for lifecycle tracking across mixer add/remove/dispose
    private static int _nextGeneratorId;

    /// <summary>Unique ID for tracking this generator through its lifecycle.</summary>
    public int GeneratorId { get; }

    /// <summary>Total samples received via AddSamples (lifetime).</summary>
    public long TotalSamplesReceived => _totalSamplesReceived;

    /// <summary>Total samples output via GenerateAudio (lifetime).</summary>
    public long TotalSamplesOutput => _totalSamplesOutput;

    /// <summary>Whether this generator has been disposed.</summary>
    public new bool IsDisposed => _isDisposed;

    // Clock drift compensation: when producer (e.g., BT/PipeWire) runs on a different
    // clock than consumer (MiniAudio/ALSA), the buffer slowly drains or fills. We
    // periodically check the buffer level and duplicate a frame of samples when the
    // level drops below a threshold, preventing progressive underruns.
    private const float DriftCompensationThresholdPercent = 0.15f;
    private const float DriftCompensationTargetPercent = 0.25f;
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
    /// <param name="maxBufferSeconds">Maximum seconds of audio to buffer (default: 4).</param>
    /// <param name="overflowStrategy">Strategy for buffer overflow (default: DropOldest).</param>
    /// <param name="metricsCollector">Optional metrics collector for pipeline metrics.</param>
    public BufferedSoundGenerator(
        AudioEngine engine,
        AudioFormat format,
        ILogger logger,
        float maxBufferSeconds = 4.0f,
        BufferOverflowStrategy overflowStrategy = BufferOverflowStrategy.DropOldest,
        IMetricsCollector? metricsCollector = null)
        : base(engine, format)
    {
        GeneratorId = Interlocked.Increment(ref _nextGeneratorId);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metricsCollector = metricsCollector;
        _overflowStrategy = overflowStrategy;

        // Calculate max buffer based on output format
        // Note: This assumes input sample rate matches output sample rate.
        var samplesPerSecond = format.SampleRate * format.Channels;
        _maxBufferSamples = (int)(samplesPerSecond * maxBufferSeconds);
        _ringBuffer = new T[_maxBufferSamples];

        _driftCompensationThreshold = (int)(_maxBufferSamples * DriftCompensationThresholdPercent);
        _driftCompensationTarget = (int)(_maxBufferSamples * DriftCompensationTargetPercent);

        Name = $"Buffered Generator ({typeof(T).Name})";

        if (_metricsCollector != null)
        {
            _metricsTags = new Dictionary<string, string> { ["type"] = typeof(T).Name };
        }

        _logger.LogInformation(
            "BufferedSoundGenerator #{GeneratorId} created: Type={Type}, OutputSampleRate={SampleRate}Hz, OutputChannels={Channels}, MaxBufferSamples={MaxBuffer}, Strategy={Strategy}",
            GeneratorId, typeof(T).Name, format.SampleRate, format.Channels, _maxBufferSamples, _overflowStrategy);
    }

    /// <summary>
    /// Optional diagnostic hook: called with raw input samples in AddSamples (float path only).
    /// Set to null when not capturing (zero cost). Invoked OUTSIDE the buffer lock.
    /// </summary>
    public DiagnosticCaptureCallback? DiagnosticInputCapture { get; set; }

    /// <summary>
    /// Optional diagnostic hook: called with output samples in GenerateAudio (after read from ring buffer).
    /// Set to null when not capturing (zero cost). Invoked OUTSIDE the buffer lock.
    /// </summary>
    public DiagnosticCaptureCallback? DiagnosticOutputCapture { get; set; }

    /// <summary>
    /// Adds samples to the buffer.
    /// </summary>
    /// <param name="samples">The samples to add.</param>
    public void AddSamples(ReadOnlySpan<T> samples)
    {
        if (_isDisposed)
        {
            return;
        }

        // Defense-in-depth: truncate to frame boundary to prevent L/R channel shift.
        // PipeWire BT transport can deliver non-frame-aligned chunks during packet gaps.
        var channelCount = Format.Channels;
        if (channelCount > 1 && samples.Length % channelCount != 0)
        {
            var aligned = samples.Length / channelCount * channelCount;
            if (aligned <= 0)
            {
                return;
            }
            samples = samples.Slice(0, aligned);
        }

        // Measure lock contention: try non-blocking first, only time if contended
        double lockWaitMs = 0;
        var entered = Monitor.TryEnter(_bufferLock);
        if (!entered)
        {
            var waitStart = Stopwatch.GetTimestamp();
            Monitor.Enter(_bufferLock);
            lockWaitMs = (double)(Stopwatch.GetTimestamp() - waitStart) / Stopwatch.Frequency * 1000.0;
            _addSamplesContentionCount++;
            if (lockWaitMs > _maxAddSamplesLockWaitMs)
            {
                _maxAddSamplesLockWaitMs = lockWaitMs;
            }
        }
        try
        {
            if (_overflowStrategy == BufferOverflowStrategy.Block)
            {
                while ((_count + samples.Length > _maxBufferSamples) && !_isDisposed)
                {
                    Monitor.Wait(_bufferLock);
                }

                if (_isDisposed)
                {
                    return;
                }
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
        finally
        {
            Monitor.Exit(_bufferLock);
        }

        // Diagnostic capture hook — invoked outside the lock to avoid extending lock hold time.
        // The capture callback does a fast span copy into CaptureSession's own buffer.
        if (typeof(T) == typeof(float))
        {
            DiagnosticInputCapture?.Invoke(System.Runtime.InteropServices.MemoryMarshal.Cast<T, float>(samples));
        }
    }

    /// <summary>
    /// Generates audio samples for the SoundFlow mixer.
    /// </summary>
    protected override void GenerateAudio(Span<float> buffer, int channels)
    {
        var callbackStartTicks = Stopwatch.GetTimestamp();

        // Track interval between successive callbacks
        if (_lastGenerateAudioTimestamp > 0)
        {
            var intervalMs = (double)(callbackStartTicks - _lastGenerateAudioTimestamp)
                / Stopwatch.Frequency * 1000.0;
            if (intervalMs > _maxCallbackIntervalMs)
            {
                _maxCallbackIntervalMs = intervalMs;
            }
            if (intervalMs < _minCallbackIntervalMs)
            {
                _minCallbackIntervalMs = intervalMs;
            }
            // Expected quantum is buffer.Length / (sampleRate * channels) * 1000
            // At 512 samples / (48000 * 2) = ~5.33ms per channel, ~10.67ms total
            // Flag anything > 2x expected as a missed deadline
            var expectedMs = (double)buffer.Length / (Format.SampleRate * Format.Channels) * 1000.0;
            if (intervalMs > expectedMs * 2)
            {
                _missedDeadlineCount++;
                // Read cached GC counts (sampled every 10s in LogStats) to avoid
                // GC.CollectionCount() syscalls on the audio callback thread.
                var gen0 = Volatile.Read(ref _cachedGen0Count);
                var gen1 = Volatile.Read(ref _cachedGen1Count);
                var gen2 = Volatile.Read(ref _cachedGen2Count);
                if (gen0 != _prevGen0Count || gen1 != _prevGen1Count || gen2 != _prevGen2Count)
                {
                    _gcCorrelatedMissedDeadlines++;
                    // Throttle per-miss logging to once per 5s to avoid overwhelming journald
                    // (high-frequency warnings cause journald CPU spike → memory pressure → more GC)
                    var now = Stopwatch.GetTimestamp();
                    if ((now - _lastMissedDeadlineLogTicks) / (double)Stopwatch.Frequency >= 5.0)
                    {
                        _lastMissedDeadlineLogTicks = now;
                        _logger.LogWarning(
                            "🔬 Missed callback deadline ({Interval:F1}ms) with GC activity: " +
                            "Gen0 +{G0}, Gen1 +{G1}, Gen2 +{G2}",
                            intervalMs,
                            gen0 - _prevGen0Count, gen1 - _prevGen1Count, gen2 - _prevGen2Count);
                    }
                }
                _prevGen0Count = gen0;
                _prevGen1Count = gen1;
                _prevGen2Count = gen2;
            }
        }
        _lastGenerateAudioTimestamp = callbackStartTicks;

        if (_isDisposed)
        {
            buffer.Clear();
            return;
        }

        int samplesWritten = 0;

        // Measure lock contention: try non-blocking first, only time if contended
        double generateLockWaitMs = 0;
        var lockEntered = Monitor.TryEnter(_bufferLock);
        if (!lockEntered)
        {
            var waitStart = Stopwatch.GetTimestamp();
            Monitor.Enter(_bufferLock);
            generateLockWaitMs = (double)(Stopwatch.GetTimestamp() - waitStart)
                / Stopwatch.Frequency * 1000.0;
            _generateAudioContentionCount++;
            if (generateLockWaitMs > _maxGenerateAudioLockWaitMs)
            {
                _maxGenerateAudioLockWaitMs = generateLockWaitMs;
            }
        }
        try
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
            if (_count < _minBufferSinceLastLog)
            {
                _minBufferSinceLastLog = _count;
            }
            if (_count > _maxBufferSinceLastLog)
            {
                _maxBufferSinceLastLog = _count;
            }

            if (_overflowStrategy == BufferOverflowStrategy.Block && toRead > 0)
            {
                Monitor.PulseAll(_bufferLock);
            }
        }
        finally
        {
            Monitor.Exit(_bufferLock);
        }

        // Diagnostic output capture hook — invoked outside the lock.
        if (samplesWritten > 0)
        {
            DiagnosticOutputCapture?.Invoke(buffer.Slice(0, samplesWritten));
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

                // Counters bump on EVERY underrun (independent of log throttle)
                if (_metricsCollector != null && _metricsTags != null)
                {
                    _metricsCollector.Increment("audio.buffer.underrun_total", 1, _metricsTags);
                    _metricsCollector.Increment("audio.buffer.underrun_samples_total", deficit, _metricsTags);
                }

                // Log underrun bursts: throttled to once per second to avoid log spam
                // while still revealing the pattern of when underruns occur.
                var now = DateTime.UtcNow;
                var sinceLastLog = _lastUnderrunLogTime == default
                    ? 0.0 : (now - _lastUnderrunLogTime).TotalSeconds;
                if (sinceLastLog >= 1.0 || _lastUnderrunLogTime == default)
                {
                    int buffered;
                    lock (_bufferLock)
                    {
                        buffered = _count;
                    }
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

        var executionMs = (double)(Stopwatch.GetTimestamp() - callbackStartTicks)
            / Stopwatch.Frequency * 1000.0;
        if (executionMs > _maxCallbackExecutionMs)
        {
            _maxCallbackExecutionMs = executionMs;
        }
    }

    /// <summary>
    /// Detects clock drift between producer and consumer by monitoring buffer level
    /// trends. When the buffer is consistently draining (producer slower than consumer),
    /// rewinds the read pointer to duplicate recent audio and prevent underrun.
    /// This compensates for BT/PipeWire running on a different clock than ALSA playback.
    ///
    /// Path C refinement (2026-05-22): per-call cap dropped from 10 ms to 2 ms and the
    /// 2-second cooldown was removed so the same total compensation is redistributed
    /// across ~5× more events that are each individually sub-perceptual. See
    /// docs/plans/2026-05-22-bt-drift-compensation-refinement.md.
    /// </summary>
    private void CompensateClockDrift(int channels)
    {
        var now = DateTime.UtcNow;
        if (_lastDriftCheckTime == default)
        {
            _lastDriftCheckTime = now;
            lock (_bufferLock)
            {
                _lastDriftCheckLevel = _count;
            }
            return;
        }

        // No cooldown — the per-call cap below keeps each correction small (≤2 ms),
        // so running on every Process call redistributes the necessary compensation
        // across many short events rather than concentrating it into audible 10 ms
        // duplications every ~2 seconds.

        int currentLevel;
        lock (_bufferLock)
        {
            currentLevel = _count;
        }

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
            // Per-call cap: 2 ms of audio (96 samples at 48 kHz stereo). Smaller events
            // are less audible per occurrence; total samples compensated per second stays
            // roughly the same because it is driven by the underlying rate mismatch.
            var frameSamples = Math.Max(channels, 2);
            var maxCompensationPerCall = (int)(Format.SampleRate * Format.Channels * 0.002);
            var deficit = Math.Min(_driftCompensationTarget - currentLevel, maxCompensationPerCall);
            deficit = (deficit / frameSamples) * frameSamples; // frame-align

            if (deficit > 0)
            {
                bool applied = false;
                lock (_bufferLock)
                {
                    if (_count + deficit <= _maxBufferSamples)
                    {
                        _readPos = (_readPos - deficit + _maxBufferSamples) % _maxBufferSamples;
                        _count += deficit;
                        _totalSamplesCompensated += deficit;
                        applied = true;
                    }
                }

                if (applied)
                {
                    // Counters bump on EVERY successful compensation (independent of log throttle)
                    if (_metricsCollector != null && _metricsTags != null)
                    {
                        _metricsCollector.Increment("audio.buffer.drift_compensation_total", 1, _metricsTags);
                        _metricsCollector.Increment("audio.buffer.drift_compensation_samples_total", deficit, _metricsTags);
                    }

                    _compensationCountSinceLastLog++;
                    _compensationSamplesSinceLastLog += deficit;

                    // Log compensation bursts at Info: throttled to once per 5 seconds.
                    // With no cooldown, compensation can now fire on every Process call
                    // while draining; the 5 s throttle aggregates the burst into a single
                    // line revealing event-count + total-duplicated-samples cadence.
                    var compNow = DateTime.UtcNow;
                    var compSinceLastLog = _lastCompensationLogTime == default
                        ? 0.0 : (compNow - _lastCompensationLogTime).TotalSeconds;
                    if (compSinceLastLog >= 5.0 || _lastCompensationLogTime == default)
                    {
                        _logger.LogInformation(
                            "🔄 Clock drift compensation ({Type}): {Count} events, {Samples} duplicated samples in last {Interval:F1}s " +
                            "(buffer: {Level}→{NewLevel}/{Capacity}, total compensated: {Total})",
                            typeof(T).Name, _compensationCountSinceLastLog, _compensationSamplesSinceLastLog,
                            compSinceLastLog,
                            currentLevel, currentLevel + deficit, _maxBufferSamples, _totalSamplesCompensated);
                        _compensationSamplesSinceLastLog = 0;
                        _compensationCountSinceLastLog = 0;
                        _lastCompensationLogTime = compNow;
                    }
                }
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
            // Sample GC collection counts here (every 10s) so the audio callback
            // can read cached values via Volatile.Read instead of making syscalls.
            Volatile.Write(ref _cachedGen0Count, GC.CollectionCount(0));
            Volatile.Write(ref _cachedGen1Count, GC.CollectionCount(1));
            Volatile.Write(ref _cachedGen2Count, GC.CollectionCount(2));

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

                _logger.LogDebug(
                    "📊 Buffer ({Type}): fill={FillPct:F1}% ({Buffered}/{Capacity}), min={MinBuf} ({MinPct:F1}%), max={MaxBuf}, " +
                    "recv={Received}, out={Output}, drop={Dropped}, comp={Compensated}, under={Underruns}",
                    typeof(T).Name, fillPct, currentBuffer, _maxBufferSamples,
                    minBuf, minPct, maxBuf,
                    _totalSamplesReceived, _totalSamplesOutput,
                    _totalSamplesDropped, _totalSamplesCompensated, _underrunCount);

                _logger.LogDebug(
                    "🔬 Timing ({Type}): callback interval min={MinInterval:F2}ms max={MaxInterval:F2}ms, " +
                    "missed deadlines={Missed} (GC-correlated={GcMisses}), execution max={MaxExec:F2}ms, " +
                    "lock contention: addSamples={AddContentions} (max {AddWait:F2}ms), " +
                    "generateAudio={GenContentions} (max {GenWait:F2}ms)",
                    typeof(T).Name,
                    _minCallbackIntervalMs == double.MaxValue ? 0 : _minCallbackIntervalMs,
                    _maxCallbackIntervalMs, _missedDeadlineCount, _gcCorrelatedMissedDeadlines,
                    _maxCallbackExecutionMs,
                    _addSamplesContentionCount, _maxAddSamplesLockWaitMs,
                    _generateAudioContentionCount, _maxGenerateAudioLockWaitMs);

                // Reset per-window tracking (contention counts and missed deadlines are cumulative)
                _maxCallbackIntervalMs = 0;
                _minCallbackIntervalMs = double.MaxValue;
                _maxCallbackExecutionMs = 0;

                _minBufferSinceLastLog = currentBuffer;
                _maxBufferSinceLastLog = currentBuffer;
                _lastLogTime = now;

                // Report metrics (outside lock — reads of long fields are safe for approximate values)
                if (_metricsCollector != null && _metricsTags != null)
                {
                    var fillPercent = (double)currentBuffer / _maxBufferSamples * 100.0;
                    _metricsCollector.Gauge("audio.buffer.fill_percent", fillPercent, _metricsTags);
                    _metricsCollector.Gauge("audio.callback.max_interval_ms", _maxCallbackIntervalMs, _metricsTags);
                    _metricsCollector.Gauge("audio.callback.max_execution_ms", _maxCallbackExecutionMs, _metricsTags);
                    _metricsCollector.Gauge("audio.lock.add_samples_max_wait_ms", _maxAddSamplesLockWaitMs, _metricsTags);
                    _metricsCollector.Gauge("audio.lock.generate_audio_max_wait_ms", _maxGenerateAudioLockWaitMs, _metricsTags);

                    var droppedDelta = _totalSamplesDropped - _lastReportedDropped;
                    if (droppedDelta > 0)
                    {
                        _metricsCollector.Increment("audio.buffer.samples_dropped", droppedDelta, _metricsTags);
                        _lastReportedDropped = _totalSamplesDropped;
                    }

                    var underrunDelta = _underrunCount - _lastReportedUnderruns;
                    if (underrunDelta > 0)
                    {
                        _metricsCollector.Increment("audio.buffer.underruns", underrunDelta, _metricsTags);
                        _lastReportedUnderruns = _underrunCount;
                    }

                    var missedDelta = _missedDeadlineCount - _lastReportedMissedDeadlines;
                    if (missedDelta > 0)
                    {
                        _metricsCollector.Increment("audio.callback.missed_deadlines", missedDelta, _metricsTags);
                        _lastReportedMissedDeadlines = _missedDeadlineCount;
                    }

                    var gcDelta = _gcCorrelatedMissedDeadlines - _lastReportedGcCorrelatedMisses;
                    if (gcDelta > 0)
                    {
                        _metricsCollector.Increment("audio.callback.gc_correlated_misses", gcDelta, _metricsTags);
                        _lastReportedGcCorrelatedMisses = _gcCorrelatedMissedDeadlines;
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
    /// <param name="seconds">Seconds of silence to pre-fill (default: 1.5).</param>
    public void PreFillSilence(float seconds = 1.5f)
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
                "BufferedSoundGenerator #{GeneratorId} disposed. Total samples: received={Received}, output={Output}, dropped={Dropped}, compensated={Compensated}",
                GeneratorId, _totalSamplesReceived, _totalSamplesOutput, _totalSamplesDropped, _totalSamplesCompensated);
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

using Microsoft.Extensions.Logging;
using SoundFlow.Abstracts;
using SoundFlow.Structs;
using System.Runtime.InteropServices;

namespace Radio.Infrastructure.Audio.SoundFlow;

/// <summary>
/// A generic SoundFlow audio component that buffers audio samples from an external source
/// and outputs them to the SoundFlow mixer.
/// Supports float (SDR) and short (Spotify/Librespot) sample types.
/// </summary>
/// <typeparam name="T">The sample type (float or short).</typeparam>
public class BufferedSoundGenerator<T> : SoundComponent where T : struct
{
    private readonly ILogger _logger;
    private readonly object _bufferLock = new();
    private readonly Queue<T> _sampleBuffer = new();
    private readonly int _maxBufferSamples;
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
    public BufferedSoundGenerator(
        AudioEngine engine,
        AudioFormat format,
        ILogger logger,
        float maxBufferSeconds = 2.0f)
        : base(engine, format)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Calculate max buffer based on output format
        var samplesPerSecond = format.SampleRate * format.Channels;
        _maxBufferSamples = (int)(samplesPerSecond * maxBufferSeconds);

        Name = $"Buffered Generator ({typeof(T).Name})";

        _logger.LogDebug(
            "BufferedSoundGenerator created: Type={Type}, OutputSampleRate={SampleRate}Hz, OutputChannels={Channels}, MaxBufferSamples={MaxBuffer}",
            typeof(T).Name, format.SampleRate, format.Channels, _maxBufferSamples);
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
            _totalSamplesReceived += samples.Length;

            foreach (var sample in samples)
            {
                if (_sampleBuffer.Count >= _maxBufferSamples)
                {
                    _sampleBuffer.Dequeue();
                    _totalSamplesDropped++;
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
                    // Unsafe cast or dynamic? 
                    // Since we checked typeof(T), we can cast the dequeued item
                    // But C# generics are tricky here.
                    // Let's use pattern matching or Convert.
                    
                    var sample = _sampleBuffer.Dequeue();
                    buffer[samplesWritten++] = (float)(object)sample;
                }
            }
            else if (typeof(T) == typeof(short))
            {
                while (samplesWritten < buffer.Length && _sampleBuffer.Count > 0)
                {
                    var sample = _sampleBuffer.Dequeue();
                    short s = (short)(object)sample;
                    buffer[samplesWritten++] = s / 32768f;
                }
            }
            else
            {
                // Fallback or error
                _logger.LogError("Unsupported sample type: {Type}", typeof(T).Name);
                buffer.Clear();
                return;
            }
        }

        _totalSamplesOutput += samplesWritten;

        // Fill remaining buffer with silence
        if (samplesWritten < buffer.Length)
        {
            buffer.Slice(samplesWritten).Clear();
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

            _logger.LogDebug(
                "Buffered audio ({Type}): received={Received}, output={Output}, dropped={Dropped}, buffered={Buffered}",
                typeof(T).Name, _totalSamplesReceived, _totalSamplesOutput, _totalSamplesDropped, currentBuffer);
            _lastLogTime = now;
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
        }
        _logger.LogDebug("Audio buffer cleared");
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
            ClearBuffer();
            _logger.LogInformation(
                "BufferedSoundGenerator disposed. Total samples: received={Received}, output={Output}, dropped={Dropped}",
                _totalSamplesReceived, _totalSamplesOutput, _totalSamplesDropped);
        }

        _isDisposed = true;
        base.Dispose(disposing);
    }
}

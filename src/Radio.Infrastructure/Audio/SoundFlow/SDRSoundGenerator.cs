using Microsoft.Extensions.Logging;
using RTLSDRCore;
using SoundFlow.Abstracts;
using SoundFlow.Enums;
using SoundFlow.Structs;

namespace Radio.Infrastructure.Audio.SoundFlow;

/// <summary>
/// A SoundFlow audio component that generates audio from an RTL-SDR radio receiver.
/// This component reads raw PCM float samples from the SDR and outputs them to the
/// SoundFlow mixer, enabling live radio playback.
/// </summary>
/// <remarks>
/// This approach directly integrates SDR audio with SoundFlow's audio graph by
/// extending SoundComponent, avoiding the need for decoders or intermediate streams.
/// The SDR provides push-based audio (via AudioDataAvailable events), which is
/// buffered and then pulled by SoundFlow during GenerateAudio calls.
/// </remarks>
public class SDRSoundGenerator : SoundComponent
{
  private readonly RadioReceiver _radioReceiver;
  private readonly ILogger _logger;
  private readonly object _bufferLock = new();
  private readonly Queue<float> _sampleBuffer = new();
  private readonly int _maxBufferSamples;
  private bool _isDisposed;
  private long _totalSamplesReceived;
  private long _totalSamplesDropped;
  private long _totalSamplesOutput;
  private DateTime _lastLogTime = DateTime.MinValue;

  /// <summary>
  /// Initializes a new instance of the <see cref="SDRSoundGenerator"/> class.
  /// </summary>
  /// <param name="engine">The SoundFlow audio engine.</param>
  /// <param name="format">The audio format for output.</param>
  /// <param name="radioReceiver">The RTL-SDR radio receiver providing demodulated audio.</param>
  /// <param name="logger">Logger for diagnostic output.</param>
  /// <param name="maxBufferSeconds">Maximum seconds of audio to buffer (default: 2).</param>
  public SDRSoundGenerator(
    AudioEngine engine,
    AudioFormat format,
    RadioReceiver radioReceiver,
    ILogger logger,
    float maxBufferSeconds = 2.0f)
    : base(engine, format)
  {
    _radioReceiver = radioReceiver ?? throw new ArgumentNullException(nameof(radioReceiver));
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    // Calculate max buffer based on output format (not SDR format, as we may be resampling)
    var samplesPerSecond = format.SampleRate * format.Channels;
    _maxBufferSamples = (int)(samplesPerSecond * maxBufferSeconds);

    // Subscribe to audio data events from RTL-SDR
    _radioReceiver.AudioDataAvailable += OnAudioDataAvailable;

    Name = "SDR Radio";

    _logger.LogDebug(
      "SDRSoundGenerator created: OutputSampleRate={SampleRate}Hz, OutputChannels={Channels}, MaxBufferSamples={MaxBuffer}",
      format.SampleRate, format.Channels, _maxBufferSamples);
  }

  /// <summary>
  /// Generates audio samples for the SoundFlow mixer.
  /// This method is called by SoundFlow's audio thread to pull samples.
  /// </summary>
  /// <param name="buffer">The buffer to fill with audio samples.</param>
  /// <param name="channels">The number of audio channels.</param>
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
      // Copy available samples from our buffer to SoundFlow's buffer
      while (samplesWritten < buffer.Length && _sampleBuffer.Count > 0)
      {
        buffer[samplesWritten++] = _sampleBuffer.Dequeue();
      }
    }

    _totalSamplesOutput += samplesWritten;

    // Fill remaining buffer with silence if we don't have enough samples
    if (samplesWritten < buffer.Length)
    {
      buffer.Slice(samplesWritten).Clear();
    }

    // Log periodically (every 10 seconds)
    var now = DateTime.UtcNow;
    if ((now - _lastLogTime).TotalSeconds >= 10)
    {
      int currentBuffer;
      lock (_bufferLock)
      {
        currentBuffer = _sampleBuffer.Count;
      }

      _logger.LogDebug(
        "SDR audio: received={Received}, output={Output}, dropped={Dropped}, buffered={Buffered}",
        _totalSamplesReceived, _totalSamplesOutput, _totalSamplesDropped, currentBuffer);
      _lastLogTime = now;
    }
  }

  /// <summary>
  /// Handles audio data events from the RTL-SDR receiver.
  /// Queues the float samples for output by GenerateAudio.
  /// </summary>
  private void OnAudioDataAvailable(object? sender, AudioDataEventArgs e)
  {
    if (_isDisposed)
    {
      return;
    }

    lock (_bufferLock)
    {
      _totalSamplesReceived += e.Samples.Length;

      foreach (var sample in e.Samples)
      {
        if (_sampleBuffer.Count >= _maxBufferSamples)
        {
          // Buffer full, drop oldest sample
          _sampleBuffer.Dequeue();
          _totalSamplesDropped++;
        }
        _sampleBuffer.Enqueue(sample);
      }
    }
  }

  /// <summary>
  /// Gets the number of samples currently buffered.
  /// </summary>
  public int BufferedSamples
  {
    get
    {
      lock (_bufferLock)
      {
        return _sampleBuffer.Count;
      }
    }
  }

  /// <summary>
  /// Gets the total number of samples received from SDR.
  /// </summary>
  public long TotalSamplesReceived => _totalSamplesReceived;

  /// <summary>
  /// Gets the total number of samples dropped due to buffer overflow.
  /// </summary>
  public long TotalSamplesDropped => _totalSamplesDropped;

  /// <summary>
  /// Gets the total number of samples output to SoundFlow.
  /// </summary>
  public long TotalSamplesOutput => _totalSamplesOutput;

  /// <summary>
  /// Clears the audio buffer.
  /// </summary>
  public void ClearBuffer()
  {
    lock (_bufferLock)
    {
      _sampleBuffer.Clear();
    }
    _logger.LogDebug("SDR audio buffer cleared");
  }

  /// <inheritdoc/>
  protected override void Dispose(bool disposing)
  {
    if (_isDisposed)
    {
      base.Dispose(disposing);
      return;
    }

    if (disposing)
    {
      // Unsubscribe from audio events
      _radioReceiver.AudioDataAvailable -= OnAudioDataAvailable;

      // Clear buffer
      ClearBuffer();

      _logger.LogInformation(
        "SDRSoundGenerator disposed. Total samples: received={Received}, output={Output}, dropped={Dropped}",
        _totalSamplesReceived, _totalSamplesOutput, _totalSamplesDropped);
    }

    _isDisposed = true;
    base.Dispose(disposing);
  }
}

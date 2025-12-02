using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;

namespace Radio.Infrastructure.Audio.Visualization;

/// <summary>
/// Visualization service that provides spectrum analysis, level metering, and waveform display.
/// Combines SpectrumAnalyzer, LevelMeter, and WaveformAnalyzer components.
/// </summary>
public sealed class VisualizerService : IVisualizerService
{
  private readonly ILogger<VisualizerService> _logger;
  private readonly VisualizerOptions _options;
  private readonly int _sampleRate;

  private readonly SpectrumAnalyzer _spectrumAnalyzer;
  private readonly LevelMeter _levelMeter;
  private readonly WaveformAnalyzer _waveformAnalyzer;

  private bool _isActive;
  private bool _disposed;
  private readonly object _lock = new();

  /// <inheritdoc/>
  public bool IsActive => _isActive;

  /// <inheritdoc/>
  public int SampleRate => _sampleRate;

  /// <inheritdoc/>
  public int FFTSize => _options.FFTSize;

  /// <summary>
  /// Initializes a new instance of the <see cref="VisualizerService"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="visualizerOptions">The visualizer configuration options.</param>
  /// <param name="audioEngineOptions">The audio engine options for sample rate.</param>
  public VisualizerService(
    ILogger<VisualizerService> logger,
    IOptions<VisualizerOptions> visualizerOptions,
    IOptions<AudioEngineOptions> audioEngineOptions)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    ArgumentNullException.ThrowIfNull(visualizerOptions);
    ArgumentNullException.ThrowIfNull(audioEngineOptions);

    _options = visualizerOptions.Value;
    _sampleRate = audioEngineOptions.Value.SampleRate;

    // Initialize components
    _spectrumAnalyzer = new SpectrumAnalyzer(
      _options.FFTSize,
      _sampleRate,
      _options.ApplyWindowFunction,
      _options.SpectrumSmoothing);

    _levelMeter = new LevelMeter(
      _sampleRate,
      _options.PeakDecayRate,
      _options.RmsSmoothing,
      _options.PeakHoldTimeMs);

    _waveformAnalyzer = new WaveformAnalyzer(
      _options.WaveformSampleCount,
      _sampleRate);

    _logger.LogInformation(
      "VisualizerService initialized (FFTSize: {FFTSize}, SampleRate: {SampleRate}, WaveformSamples: {WaveformSamples})",
      _options.FFTSize, _sampleRate, _options.WaveformSampleCount);
  }

  /// <inheritdoc/>
  public void ProcessSamples(float[] samples)
  {
    ThrowIfDisposed();

    lock (_lock)
    {
      _isActive = true;

      // Convert interleaved stereo to mono for spectrum analysis
      var monoSamples = ConvertToMono(samples, samples.Length);

      _spectrumAnalyzer.AddSamples(monoSamples);
      _levelMeter.ProcessSamples(samples);
      _waveformAnalyzer.AddSamples(samples);
    }
  }

  /// <inheritdoc/>
  public void ProcessSamples(Span<float> samples, int count)
  {
    ThrowIfDisposed();

    lock (_lock)
    {
      _isActive = true;

      // Convert interleaved stereo to mono for spectrum analysis
      var monoSamples = ConvertToMono(samples, count);

      _spectrumAnalyzer.AddSamples(monoSamples);
      _levelMeter.ProcessSamples(samples, count);
      _waveformAnalyzer.AddSamples(samples, count);
    }
  }

  /// <inheritdoc/>
  public SpectrumData GetSpectrumData()
  {
    ThrowIfDisposed();

    lock (_lock)
    {
      var magnitudes = _spectrumAnalyzer.GetMagnitudes();
      var frequencies = _spectrumAnalyzer.GetFrequencies();

      return new SpectrumData
      {
        Magnitudes = magnitudes,
        Frequencies = frequencies,
        BinCount = _spectrumAnalyzer.BinCount,
        FrequencyResolution = _spectrumAnalyzer.FrequencyResolution,
        MaxFrequency = _spectrumAnalyzer.BinCount * _spectrumAnalyzer.FrequencyResolution,
        Timestamp = DateTimeOffset.UtcNow
      };
    }
  }

  /// <inheritdoc/>
  public LevelData GetLevelData()
  {
    ThrowIfDisposed();

    lock (_lock)
    {
      return new LevelData
      {
        LeftPeak = _levelMeter.LeftPeak,
        RightPeak = _levelMeter.RightPeak,
        LeftRms = _levelMeter.LeftRms,
        RightRms = _levelMeter.RightRms,
        LeftPeakDb = _levelMeter.LeftPeakDb,
        RightPeakDb = _levelMeter.RightPeakDb,
        LeftRmsDb = _levelMeter.LeftRmsDb,
        RightRmsDb = _levelMeter.RightRmsDb,
        MonoPeak = _levelMeter.MonoPeak,
        MonoRms = _levelMeter.MonoRms,
        IsClipping = _levelMeter.IsClipping,
        Timestamp = DateTimeOffset.UtcNow
      };
    }
  }

  /// <inheritdoc/>
  public WaveformData GetWaveformData()
  {
    ThrowIfDisposed();

    lock (_lock)
    {
      var (left, right) = _waveformAnalyzer.GetSamples();

      return new WaveformData
      {
        LeftSamples = left,
        RightSamples = right,
        SampleCount = _waveformAnalyzer.SampleCount,
        Duration = _waveformAnalyzer.Duration,
        Timestamp = DateTimeOffset.UtcNow
      };
    }
  }

  /// <inheritdoc/>
  public void Reset()
  {
    ThrowIfDisposed();

    lock (_lock)
    {
      _spectrumAnalyzer.Reset();
      _levelMeter.Reset();
      _waveformAnalyzer.Reset();
      _isActive = false;

      _logger.LogDebug("VisualizerService reset");
    }
  }

  /// <summary>
  /// Converts interleaved stereo samples to mono by averaging channels.
  /// </summary>
  private static float[] ConvertToMono(ReadOnlySpan<float> stereoSamples, int count)
  {
    var monoCount = count / 2;
    var mono = new float[monoCount];

    for (var i = 0; i < monoCount; i++)
    {
      var leftIndex = i * 2;
      var rightIndex = leftIndex + 1;

      if (rightIndex < count)
      {
        mono[i] = (stereoSamples[leftIndex] + stereoSamples[rightIndex]) / 2f;
      }
      else
      {
        mono[i] = stereoSamples[leftIndex];
      }
    }

    return mono;
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
  }

  /// <inheritdoc/>
  public void Dispose()
  {
    if (_disposed) return;

    _disposed = true;
    _isActive = false;

    _logger.LogInformation("VisualizerService disposed");
  }
}

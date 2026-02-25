using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces.Audio;

namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// Background service that polls RMS levels from the visualizer and learns
/// per-source average loudness using exponential moving average. When a source
/// is in "auto" mode and has enough samples, the service computes and applies
/// a gain offset to normalize all sources to a common target level (-18 dBFS).
/// </summary>
public sealed class SourceLevelLearningService : BackgroundService
{
  private readonly ILogger<SourceLevelLearningService> _logger;
  private readonly IVisualizerService _visualizerService;
  private readonly IAudioManager _audioManager;
  private readonly AudioPreferencePersistence _persistence;

  /// <summary>Silence threshold — don't learn from near-silent audio.</summary>
  private const float SilenceThreshold = 0.001f;

  /// <summary>Minimum gain change before applying (avoids jitter).</summary>
  private const float GainChangeThreshold = 0.02f;

  /// <summary>Max auto-gain — delegates to centralized constant.</summary>
  private const float MaxAutoGain = AudioPreferencePersistence.MaxGain;

  public SourceLevelLearningService(
    ILogger<SourceLevelLearningService> logger,
    IVisualizerService visualizerService,
    IAudioManager audioManager,
    AudioPreferencePersistence persistence)
  {
    _logger = logger;
    _visualizerService = visualizerService;
    _audioManager = audioManager;
    _persistence = persistence;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    _logger.LogInformation("SourceLevelLearningService starting (10s initial delay)");

    // Let audio engine stabilize before starting
    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

    _logger.LogInformation("SourceLevelLearningService active, polling every 3s");

    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        ProcessRmsSample();
      }
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
      {
        break;
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Error in source level learning loop");
      }
    }

    _logger.LogInformation("SourceLevelLearningService stopped");
  }

  private void ProcessRmsSample()
  {
    // Get active source
    var activeSource = _audioManager.ActiveSource;
    if (activeSource == null)
      return;

    var sourceType = activeSource.Type;

    // Read current RMS level
    var levelData = _visualizerService.GetLevelData();
    var monoRms = levelData.MonoRms;

    // Skip silence — don't learn from it
    if (monoRms < SilenceThreshold)
      return;

    // Back-calculate pre-gain RMS to avoid feedback loop.
    // The visualizer sees post-gain audio, so divide out the current gain offset
    // to learn the source's intrinsic loudness rather than the corrected level.
    // However, when gain is at a clamp boundary (0.1 or 2.0), the system can't
    // fully compensate and the back-calculation produces misleading values —
    // skip learning in that case to preserve the last valid measurement.
    var currentGain = _persistence.GetSourceGain(sourceType);
    if (currentGain <= 0.1f || currentGain >= MaxAutoGain)
      return;
    var preGainRms = monoRms / currentGain;

    // Update the EMA for this source (using pre-gain RMS)
    _persistence.UpdateSourceLearnedRms(sourceType, preGainRms);

    // Check if source is in auto mode
    var mode = _persistence.GetSourceGainMode(sourceType);
    if (mode != "auto")
      return;

    // Check if we have enough samples
    var sampleCount = _persistence.GetSourceSampleCount(sourceType);
    if (sampleCount < AudioPreferencePersistence.MinSamplesForAutoGain)
      return;

    // Compute suggested gain
    var learnedRms = _persistence.GetSourceLearnedRms(sourceType);
    if (!learnedRms.HasValue || learnedRms.Value <= SilenceThreshold)
      return;

    var suggestedGain = Math.Clamp(
      AudioPreferencePersistence.TargetRms / learnedRms.Value,
      0.1f, MaxAutoGain);

    // Only apply if different enough from current gain (avoid jitter)
    currentGain = _persistence.GetSourceGain(sourceType);
    if (Math.Abs(suggestedGain - currentGain) <= GainChangeThreshold)
      return;

    // Re-check mode right before applying — closes race window where user
    // changed gain (switching to manual) between our initial check and now
    if (_persistence.GetSourceGainMode(sourceType) != "auto")
      return;

    // Apply auto-gain (internal — doesn't switch to manual mode)
    _persistence.SetSourceGainInternal(sourceType, suggestedGain);

    // If this source is currently active, update live playback
    _audioManager.SetSourceGainInternal(sourceType, suggestedGain);

    _logger.LogInformation(
      "Auto-gain applied: {SourceType} learned RMS={LearnedRms:F4}, gain={Gain:F2} ({Samples} samples)",
      sourceType, learnedRms.Value, suggestedGain, sampleCount);
  }
}

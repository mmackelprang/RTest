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

  /// <summary>
  /// Minimum gain change before applying. 0.5 means gain must differ by at least
  /// 0.5x from current before we adjust — prevents audible pumping from music dynamics.
  /// </summary>
  private const float GainChangeThreshold = 0.5f;

  /// <summary>Max auto-gain — delegates to centralized constant.</summary>
  private const float MaxAutoGain = AudioPreferencePersistence.MaxGain;

  /// <summary>Fast EMA alpha for clipping recovery (0.5 = converges in ~4 samples).</summary>
  private const float FastEmaAlpha = 0.5f;

  /// <summary>Target peak for gain correction (~-1.4 dBFS, safely below unity).</summary>
  private const float TargetPeak = 0.85f;

  /// <summary>Minimum interval between gain applications to prevent audible pumping.</summary>
  private static readonly TimeSpan GainApplyInterval = TimeSpan.FromSeconds(30);

  /// <summary>Tracks last gain apply time per source type.</summary>
  private readonly Dictionary<AudioSourceType, DateTime> _lastGainApplyTime = new();

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

    // Read current level data (RMS + peak + clipping flag)
    var levelData = _visualizerService.GetLevelData();
    var monoRms = levelData.MonoRms;
    var monoPeak = levelData.MonoPeak;
    var isClipping = levelData.IsClipping;

    // Skip silence — don't learn from it
    if (monoRms < SilenceThreshold && monoPeak < SilenceThreshold)
      return;

    var currentGain = _persistence.GetSourceGain(sourceType);

    // Low boundary — back-calculation unreliable at very low gain
    if (currentGain <= 0.1f)
      return;

    // --- Clipping at max gain: the "death spiral" recovery path ---
    // When gain is at max AND we're clipping, the old code returned early
    // and never updated the EMA, locking the bad value forever. Instead,
    // back-calculate the true source RMS and use a fast EMA to converge.
    if (isClipping && currentGain >= MaxAutoGain)
    {
      var preGainRms = monoRms / currentGain;
      _persistence.UpdateSourceLearnedRms(sourceType, preGainRms, FastEmaAlpha);

      // Immediately correct gain based on peak overshoot
      var mode = _persistence.GetSourceGainMode(sourceType);
      if (monoPeak > 0.001f && mode == "auto")
      {
        var correctedGain = Math.Clamp(currentGain * TargetPeak / monoPeak, 0.1f, MaxAutoGain);
        _persistence.SetSourceGainInternal(sourceType, correctedGain);
        _audioManager.SetSourceGainInternal(sourceType, correctedGain);

        _logger.LogWarning(
          "Clipping correction: {SourceType} peak={Peak:F3}, gain {OldGain:F2} → {NewGain:F2}",
          sourceType, monoPeak, currentGain, correctedGain);
      }

      return; // Let EMA converge on subsequent polls
    }

    // --- Normal path: gain in bounds, learn and adjust ---

    // Skip learning at upper boundary when not clipping (measurement unreliable)
    if (currentGain >= MaxAutoGain)
      return;

    // Back-calculate pre-gain RMS to avoid feedback loop
    var normalPreGainRms = monoRms / currentGain;
    _persistence.UpdateSourceLearnedRms(sourceType, normalPreGainRms);

    // Check if source is in auto mode
    var normalMode = _persistence.GetSourceGainMode(sourceType);
    if (normalMode != "auto")
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

    // Rate limit: don't change gain more often than every 30s to prevent
    // audible pumping from natural music dynamics (quiet verse → loud chorus)
    var now = DateTime.UtcNow;
    if (_lastGainApplyTime.TryGetValue(sourceType, out var lastApply) &&
        now - lastApply < GainApplyInterval)
      return;

    // Re-check mode right before applying — closes race window where user
    // changed gain (switching to manual) between our initial check and now
    if (_persistence.GetSourceGainMode(sourceType) != "auto")
      return;

    // Apply auto-gain (internal — doesn't switch to manual mode)
    _persistence.SetSourceGainInternal(sourceType, suggestedGain);
    _audioManager.SetSourceGainInternal(sourceType, suggestedGain);
    _lastGainApplyTime[sourceType] = now;

    _logger.LogInformation(
      "Auto-gain applied: {SourceType} learned RMS={LearnedRms:F4}, gain={Gain:F2} ({Samples} samples)",
      sourceType, learnedRms.Value, suggestedGain, sampleCount);
  }
}

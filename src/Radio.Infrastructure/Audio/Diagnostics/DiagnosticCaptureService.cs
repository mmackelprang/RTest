using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.SoundFlow;

namespace Radio.Infrastructure.Audio.Diagnostics;

/// <summary>
/// Orchestrates multi-stage diagnostic audio capture.
/// Creates capture sessions for up to 4 pipeline stages, wires hooks/taps,
/// captures for a bounded duration, then detaches and writes WAV files.
/// </summary>
public class DiagnosticCaptureService
{
  private readonly ILogger<DiagnosticCaptureService> _logger;
  private readonly IAudioEngine _audioEngine;
  private readonly IAudioManager? _audioManager;

  private CancellationTokenSource? _activeCts;
  private readonly object _captureLock = new();

  // Max capture duration to prevent runaway memory usage
  private const int MaxCaptureDurationSeconds = 60;
  private const int DefaultSampleRate = 48000;
  private const int DefaultChannels = 2;

  public DiagnosticCaptureService(
    ILogger<DiagnosticCaptureService> logger,
    IAudioEngine audioEngine,
    IAudioManager? audioManager = null)
  {
    _logger = logger;
    _audioEngine = audioEngine;
    _audioManager = audioManager;
  }

  /// <summary>Whether a capture is currently in progress.</summary>
  public bool IsCapturing { get { lock (_captureLock) { return _activeCts != null; } } }

  /// <summary>
  /// Starts a bounded diagnostic capture across multiple pipeline stages.
  /// </summary>
  /// <param name="durationSeconds">Capture duration (1-60 seconds).</param>
  /// <param name="outputDirectory">Directory for WAV file output. Auto-generated if null.</param>
  /// <param name="cancellationToken">External cancellation token.</param>
  /// <returns>Capture result with file paths and sample counts.</returns>
  public async Task<CaptureResult> CaptureAsync(
    int durationSeconds = 10,
    string? outputDirectory = null,
    CancellationToken cancellationToken = default)
  {
    lock (_captureLock)
    {
      if (IsCapturing)
        throw new InvalidOperationException("A capture is already in progress");
    }

    durationSeconds = Math.Clamp(durationSeconds, 1, MaxCaptureDurationSeconds);

    outputDirectory ??= Path.Combine("data", "diagnostics",
      DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
    Directory.CreateDirectory(outputDirectory);

    var startTime = DateTime.UtcNow;
    _logger.LogInformation(
      "Starting diagnostic capture: {Duration}s, output={OutputDir}",
      durationSeconds, outputDirectory);

    // Create capture sessions for each pipeline stage
    var sessions = new Dictionary<string, CaptureSession>
    {
      ["generator-input"] = new("generator-input", durationSeconds, DefaultSampleRate, DefaultChannels),
      ["generator-output"] = new("generator-output", durationSeconds, DefaultSampleRate, DefaultChannels),
      ["post-modifiers"] = new("post-modifiers", durationSeconds, DefaultSampleRate, DefaultChannels)
    };

    // Create a tap modifier for post-modifier capture
    DiagnosticCaptureTapModifier? postModifierTap = null;

    try
    {
      using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      lock (_captureLock) { _activeCts = cts; }

      // Wire up generator hooks if we can access the active source's generator
      BufferedSoundGenerator<float>? activeGenerator = null;

      if (_audioEngine is SoundFlowAudioEngine sfEngine)
      {
        // Create and attach post-modifier tap
        postModifierTap = new DiagnosticCaptureTapModifier(sessions["post-modifiers"]);
        sfEngine.AddDiagnosticModifier(postModifierTap);

        // Try to hook into the active generator via the active audio source
        activeGenerator = SoundFlowAudioEngine.GetGeneratorFromSource(_audioManager?.ActiveSource);
        if (activeGenerator != null)
        {
          var inputSession = sessions["generator-input"];
          var outputSession = sessions["generator-output"];
          activeGenerator.DiagnosticInputCapture = (ReadOnlySpan<float> span) => inputSession.AddSamples(span);
          activeGenerator.DiagnosticOutputCapture = (ReadOnlySpan<float> span) => outputSession.AddSamples(span);
        }
        else
        {
          _logger.LogWarning("No active generator found — generator-input and generator-output stages will be empty");
        }
      }

      // Start all sessions
      foreach (var session in sessions.Values)
        session.Start();

      // Wait for the capture duration
      try
      {
        await Task.Delay(TimeSpan.FromSeconds(durationSeconds), cts.Token);
      }
      catch (OperationCanceledException)
      {
        _logger.LogInformation("Capture stopped early by cancellation");
      }

      // Stop all sessions
      foreach (var session in sessions.Values)
        session.Stop();

      // Detach hooks and taps
      if (activeGenerator != null)
      {
        activeGenerator.DiagnosticInputCapture = null;
        activeGenerator.DiagnosticOutputCapture = null;
      }

      if (postModifierTap != null && _audioEngine is SoundFlowAudioEngine sfEngine2)
        sfEngine2.RemoveDiagnosticModifier(postModifierTap);

      // Write WAV files
      var stageFiles = new Dictionary<string, string>();
      var stageSampleCounts = new Dictionary<string, int>();

      foreach (var (name, session) in sessions)
      {
        var sampleCount = session.CapturedSamples;
        stageSampleCounts[name] = sampleCount;

        if (sampleCount > 0)
        {
          var filePath = Path.Combine(outputDirectory, $"{name}.wav");
          session.WriteToFile(filePath);
          stageFiles[name] = filePath;
          _logger.LogInformation("Wrote {SampleCount} samples to {File}", sampleCount, filePath);
        }
        else
        {
          _logger.LogWarning("No samples captured for stage {Stage}", name);
        }
      }

      var result = new CaptureResult
      {
        StartTime = startTime,
        Duration = DateTime.UtcNow - startTime,
        OutputDirectory = outputDirectory,
        StageFiles = stageFiles,
        StageSampleCounts = stageSampleCounts,
        Success = true
      };

      _logger.LogInformation(
        "Diagnostic capture complete: {StageCount} stages, {Duration:F1}s",
        stageFiles.Count, result.Duration.TotalSeconds);

      return result;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Diagnostic capture failed");

      // Clean up tap if attached
      if (postModifierTap != null && _audioEngine is SoundFlowAudioEngine sfCleanup)
        sfCleanup.RemoveDiagnosticModifier(postModifierTap);

      return new CaptureResult
      {
        StartTime = startTime,
        Duration = DateTime.UtcNow - startTime,
        OutputDirectory = outputDirectory,
        Success = false,
        ErrorMessage = ex.Message
      };
    }
    finally
    {
      lock (_captureLock)
      {
        _activeCts = null;
      }
    }
  }

  /// <summary>
  /// Stops an active capture early.
  /// </summary>
  public void StopCapture()
  {
    lock (_captureLock)
    {
      _activeCts?.Cancel();
    }
  }
}

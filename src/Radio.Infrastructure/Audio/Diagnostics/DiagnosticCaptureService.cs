using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
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
  private readonly DiagnosticsOptions _options;

  private CancellationTokenSource? _activeCts;
  private readonly object _captureLock = new();

  // Max capture duration to prevent runaway memory usage
  private const int MaxCaptureDurationSeconds = 60;
  private const int DefaultSampleRate = 48000;
  private const int DefaultChannels = 2;

  public DiagnosticCaptureService(
    ILogger<DiagnosticCaptureService> logger,
    IAudioEngine audioEngine,
    IAudioManager? audioManager = null,
    IOptions<DiagnosticsOptions>? options = null)
  {
    _logger = logger;
    _audioEngine = audioEngine;
    _audioManager = audioManager;
    _options = options?.Value ?? new DiagnosticsOptions();
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
      {
        throw new InvalidOperationException("A capture is already in progress");
      }
    }

    durationSeconds = Math.Clamp(durationSeconds, 1, MaxCaptureDurationSeconds);

    // Track whether we generated the output path ourselves. Retention pruning only
    // runs against our managed base directory, never a caller-supplied path.
    var usedManagedOutputDir = outputDirectory is null;
    outputDirectory ??= Path.Combine(_options.CaptureBaseDirectory,
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
      {
        session.Start();
      }

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
      {
        session.Stop();
      }

      // Detach hooks and taps
      if (activeGenerator != null)
      {
        activeGenerator.DiagnosticInputCapture = null;
        activeGenerator.DiagnosticOutputCapture = null;
      }

      if (postModifierTap != null && _audioEngine is SoundFlowAudioEngine sfEngine2)
      {
        sfEngine2.RemoveDiagnosticModifier(postModifierTap);
      }

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

      // Prune old capture runs so the diagnostics directory stays bounded (~1.1 GB
      // of stale captures had accumulated on the live box). Best-effort — a prune
      // failure must never fail the capture that just succeeded.
      if (usedManagedOutputDir && _options.RetentionEnabled)
      {
        try
        {
          PruneCaptureDirectory(_options.CaptureBaseDirectory, _options.MaxRetainedRuns,
            _options.RetentionDays, _logger);
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "Diagnostics retention prune failed (non-fatal)");
        }
      }

      return result;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Diagnostic capture failed");

      // Clean up tap if attached
      if (postModifierTap != null && _audioEngine is SoundFlowAudioEngine sfCleanup)
      {
        sfCleanup.RemoveDiagnosticModifier(postModifierTap);
      }

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

  /// <summary>
  /// Prunes old capture-run subdirectories under <paramref name="baseDirectory"/> to keep
  /// the diagnostics directory bounded. Runs are deleted if they are older than
  /// <paramref name="retentionDays"/> (age cap, skipped when &lt;= 0), and any that remain
  /// beyond the newest <paramref name="maxRetainedRuns"/> (count cap, skipped when &lt;= 0)
  /// are deleted too. Best-effort and self-contained: per-directory failures are logged and
  /// skipped rather than thrown. Internal + static so it can be unit-tested against a temp dir.
  /// </summary>
  internal static void PruneCaptureDirectory(
    string baseDirectory,
    int maxRetainedRuns,
    int retentionDays,
    ILogger logger)
  {
    if (string.IsNullOrWhiteSpace(baseDirectory) || !Directory.Exists(baseDirectory))
    {
      return;
    }

    List<(DirectoryInfo Dir, DateTime When)> runs;
    try
    {
      runs = new DirectoryInfo(baseDirectory)
        .GetDirectories()
        .Select(d => (Dir: d, When: ResolveRunTimestamp(d)))
        .ToList();
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Diagnostics retention: failed to enumerate {BaseDir}", baseDirectory);
      return;
    }

    // Use a case-insensitive set of full paths to avoid deleting the same run twice
    // when it is caught by both the age cap and the count cap.
    var toDelete = new Dictionary<string, DirectoryInfo>(StringComparer.OrdinalIgnoreCase);

    // Age cap: delete runs older than the cutoff.
    if (retentionDays > 0)
    {
      var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
      foreach (var run in runs)
      {
        if (run.When < cutoff)
        {
          toDelete[run.Dir.FullName] = run.Dir;
        }
      }
    }

    // Count cap: of the runs surviving the age cap, keep only the newest N.
    if (maxRetainedRuns > 0)
    {
      var survivors = runs
        .Where(r => !toDelete.ContainsKey(r.Dir.FullName))
        .OrderByDescending(r => r.When)
        .ToList();
      foreach (var run in survivors.Skip(maxRetainedRuns))
      {
        toDelete[run.Dir.FullName] = run.Dir;
      }
    }

    foreach (var dir in toDelete.Values)
    {
      try
      {
        dir.Delete(recursive: true);
        logger.LogInformation("Diagnostics retention: deleted old capture run {Run}", dir.Name);
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "Diagnostics retention: failed to delete capture run {Run}", dir.FullName);
      }
    }
  }

  /// <summary>
  /// Resolves a capture-run directory's timestamp. Runs are named yyyyMMdd-HHmmss (UTC);
  /// any non-conforming name falls back to the directory's last-write time so pruning
  /// still has a stable ordering.
  /// </summary>
  private static DateTime ResolveRunTimestamp(DirectoryInfo dir)
  {
    if (DateTime.TryParseExact(
          dir.Name,
          "yyyyMMdd-HHmmss",
          CultureInfo.InvariantCulture,
          DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
          out var parsed))
    {
      return parsed;
    }

    return dir.LastWriteTimeUtc;
  }
}

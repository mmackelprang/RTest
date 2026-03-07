using Microsoft.AspNetCore.Mvc;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Diagnostics;
using Radio.Infrastructure.Audio.SoundFlow;

namespace Radio.API.Controllers;

/// <summary>
/// API controller for audio pipeline diagnostics.
/// Provides per-stage metrics for debugging audio issues.
/// </summary>
[ApiController]
[Route("api/diagnostics")]
[Produces("application/json")]
public class AudioDiagnosticsController : ControllerBase
{
  private readonly ILogger<AudioDiagnosticsController> _logger;
  private readonly IAudioEngine _audioEngine;
  private readonly IAudioManager? _audioManager;
  private readonly DiagnosticCaptureService _captureService;

  public AudioDiagnosticsController(
    ILogger<AudioDiagnosticsController> logger,
    IAudioEngine audioEngine,
    DiagnosticCaptureService captureService,
    IAudioManager? audioManager = null)
  {
    _logger = logger;
    _audioEngine = audioEngine;
    _captureService = captureService;
    _audioManager = audioManager;
  }

  /// <summary>
  /// Gets diagnostic information about the audio pipeline.
  /// Returns per-stage metrics to identify where audio flow breaks.
  /// </summary>
  [HttpGet("audio-pipeline")]
  public ActionResult<AudioPipelineDiagnosticsDto> GetAudioPipelineDiagnostics()
  {
    var dto = new AudioPipelineDiagnosticsDto
    {
      Timestamp = DateTime.UtcNow,
      EngineState = _audioEngine.State.ToString(),
      EngineReady = _audioEngine.IsReady
    };

    // Get SoundFlow engine diagnostics
    if (_audioEngine is SoundFlowAudioEngine sfEngine)
    {
      var pipelineDiag = sfEngine.GetPipelineDiagnostics();
      dto.Pipeline = new PipelineDiagnosticsDto
      {
        EngineState = pipelineDiag.EngineState,
        PlaybackDeviceActive = pipelineDiag.PlaybackDeviceActive,
        ModifierCount = pipelineDiag.ModifierCount,
        OutputTapAvailableBytes = pipelineDiag.OutputTapAvailableBytes,
        FingerprintTapTotalSamples = pipelineDiag.FingerprintTapTotalSamples,
        FingerprintTapLastProcessedTime = pipelineDiag.FingerprintTapLastProcessedTime
      };
    }

    // Get active source info
    var activeSource = _audioManager?.ActiveSource;
    if (activeSource != null)
    {
      dto.ActiveSource = new ActiveSourceDiagnosticsDto
      {
        Name = activeSource.Name,
        Type = activeSource.Type.ToString(),
        State = activeSource.State.ToString()
      };
    }

    _logger.LogDebug("Audio pipeline diagnostics requested");
    return Ok(dto);
  }

  /// <summary>
  /// Starts a bounded diagnostic capture of audio at multiple pipeline stages.
  /// Writes WAV files for each stage (generator-input, generator-output, post-modifiers).
  /// </summary>
  [HttpPost("capture")]
  public async Task<ActionResult<CaptureResultDto>> StartCapture(
    [FromBody] CaptureRequestDto? request, CancellationToken ct)
  {
    if (_captureService.IsCapturing)
    {
      return Conflict(new { error = "A capture is already in progress" });
    }

    var duration = request?.DurationSeconds ?? 10;
    duration = Math.Clamp(duration, 1, 60);

    _logger.LogInformation("Starting diagnostic capture: {Duration}s", duration);

    var result = await _captureService.CaptureAsync(
      duration, request?.OutputDirectory, ct);

    return Ok(new CaptureResultDto
    {
      StartTime = result.StartTime,
      DurationSeconds = result.Duration.TotalSeconds,
      OutputDirectory = result.OutputDirectory,
      StageFiles = result.StageFiles,
      StageSampleCounts = result.StageSampleCounts,
      Success = result.Success,
      ErrorMessage = result.ErrorMessage
    });
  }

  /// <summary>
  /// Stops an active diagnostic capture early.
  /// </summary>
  [HttpPost("capture/stop")]
  public ActionResult StopCapture()
  {
    if (!_captureService.IsCapturing)
    {
      return NotFound(new { error = "No active capture to stop" });
    }

    _captureService.StopCapture();
    _logger.LogInformation("Diagnostic capture stop requested");
    return Ok(new { message = "Capture stop requested" });
  }
}

public class CaptureRequestDto
{
  public int DurationSeconds { get; set; } = 10;
  public string? OutputDirectory { get; set; }
}

public class CaptureResultDto
{
  public DateTime StartTime { get; set; }
  public double DurationSeconds { get; set; }
  public string OutputDirectory { get; set; } = "";
  public Dictionary<string, string> StageFiles { get; set; } = new();
  public Dictionary<string, int> StageSampleCounts { get; set; } = new();
  public bool Success { get; set; }
  public string? ErrorMessage { get; set; }
}

public class AudioPipelineDiagnosticsDto
{
  public DateTime Timestamp { get; set; }
  public string EngineState { get; set; } = "";
  public bool EngineReady { get; set; }
  public PipelineDiagnosticsDto? Pipeline { get; set; }
  public ActiveSourceDiagnosticsDto? ActiveSource { get; set; }
}

public class PipelineDiagnosticsDto
{
  public string EngineState { get; set; } = "";
  public bool PlaybackDeviceActive { get; set; }
  public int ModifierCount { get; set; }
  public long OutputTapAvailableBytes { get; set; }
  public long FingerprintTapTotalSamples { get; set; }
  public DateTime? FingerprintTapLastProcessedTime { get; set; }
}

public class ActiveSourceDiagnosticsDto
{
  public string Name { get; set; } = "";
  public string Type { get; set; } = "";
  public string State { get; set; } = "";
}

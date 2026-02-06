using Microsoft.AspNetCore.Mvc;
using Radio.Core.Interfaces.Audio;
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

  public AudioDiagnosticsController(
    ILogger<AudioDiagnosticsController> logger,
    IAudioEngine audioEngine,
    IAudioManager? audioManager = null)
  {
    _logger = logger;
    _audioEngine = audioEngine;
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

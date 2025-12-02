using Microsoft.AspNetCore.SignalR;
using Radio.API.Hubs;
using Radio.API.Models;
using Radio.Core.Interfaces.Audio;

namespace Radio.API.Services;

/// <summary>
/// Background service that broadcasts visualization data to SignalR clients.
/// Sends spectrum, level, and waveform data at a configurable frame rate.
/// </summary>
public class VisualizationBroadcastService : BackgroundService
{
  private readonly ILogger<VisualizationBroadcastService> _logger;
  private readonly IHubContext<AudioVisualizationHub> _hubContext;
  private readonly IVisualizerService _visualizerService;

  /// <summary>
  /// Gets or sets the target frame rate for broadcasts (default: 30 fps).
  /// </summary>
  public int TargetFrameRate { get; set; } = 30;

  /// <summary>
  /// Gets or sets whether broadcasting is enabled (default: true).
  /// </summary>
  public bool IsEnabled { get; set; } = true;

  /// <summary>
  /// Initializes a new instance of the VisualizationBroadcastService.
  /// </summary>
  public VisualizationBroadcastService(
    ILogger<VisualizationBroadcastService> logger,
    IHubContext<AudioVisualizationHub> hubContext,
    IVisualizerService visualizerService)
  {
    _logger = logger;
    _hubContext = hubContext;
    _visualizerService = visualizerService;
  }

  /// <summary>
  /// Executes the background service.
  /// </summary>
  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    _logger.LogInformation("VisualizationBroadcastService starting with target frame rate: {FrameRate} fps", TargetFrameRate);

    var frameDelay = TimeSpan.FromMilliseconds(1000.0 / TargetFrameRate);

    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        if (IsEnabled && _visualizerService.IsActive)
        {
          await BroadcastVisualizationDataAsync(stoppingToken);
        }

        await Task.Delay(frameDelay, stoppingToken);
      }
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
      {
        // Normal shutdown, don't log as error
        break;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error broadcasting visualization data");
        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
      }
    }

    _logger.LogInformation("VisualizationBroadcastService stopped");
  }

  private async Task BroadcastVisualizationDataAsync(CancellationToken cancellationToken)
  {
    // Get visualization data
    var spectrumData = _visualizerService.GetSpectrumData();
    var levelData = _visualizerService.GetLevelData();
    var waveformData = _visualizerService.GetWaveformData();

    // Broadcast spectrum to subscribed clients
    var spectrumDto = MapToSpectrumDto(spectrumData);
    await _hubContext.Clients.Group("Spectrum")
      .SendAsync("ReceiveSpectrum", spectrumDto, cancellationToken);

    // Broadcast levels to subscribed clients
    var levelDto = MapToLevelDto(levelData);
    await _hubContext.Clients.Group("Levels")
      .SendAsync("ReceiveLevels", levelDto, cancellationToken);

    // Broadcast waveform to subscribed clients
    var waveformDto = MapToWaveformDto(waveformData);
    await _hubContext.Clients.Group("Waveform")
      .SendAsync("ReceiveWaveform", waveformDto, cancellationToken);
  }

  private static SpectrumDataDto MapToSpectrumDto(SpectrumData data)
  {
    return new SpectrumDataDto
    {
      Magnitudes = data.Magnitudes,
      Frequencies = data.Frequencies,
      BinCount = data.BinCount,
      FrequencyResolution = data.FrequencyResolution,
      MaxFrequency = data.MaxFrequency,
      TimestampMs = data.Timestamp.ToUnixTimeMilliseconds()
    };
  }

  private static LevelDataDto MapToLevelDto(LevelData data)
  {
    return new LevelDataDto
    {
      LeftPeak = data.LeftPeak,
      RightPeak = data.RightPeak,
      LeftRms = data.LeftRms,
      RightRms = data.RightRms,
      LeftPeakDb = data.LeftPeakDb,
      RightPeakDb = data.RightPeakDb,
      IsClipping = data.IsClipping,
      TimestampMs = data.Timestamp.ToUnixTimeMilliseconds()
    };
  }

  private static WaveformDataDto MapToWaveformDto(WaveformData data)
  {
    return new WaveformDataDto
    {
      LeftSamples = data.LeftSamples,
      RightSamples = data.RightSamples,
      SampleCount = data.SampleCount,
      DurationMs = data.Duration.TotalMilliseconds,
      TimestampMs = data.Timestamp.ToUnixTimeMilliseconds()
    };
  }
}

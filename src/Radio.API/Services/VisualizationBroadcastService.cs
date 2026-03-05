using Microsoft.AspNetCore.SignalR;
using Radio.API.Hubs;
using Radio.API.Models;
using Radio.Core.Interfaces.Audio;

// AudioVisualizationHub.ConnectedClients is used to skip all visualization work
// (FFT, waveform, level metering, SignalR broadcasts) when no UI clients are watching.

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
  /// Gets or sets the target frame rate for broadcasts (default: 20 fps).
  /// Lower rates reduce CPU/memory pressure on resource-constrained hardware.
  /// </summary>
  public int TargetFrameRate { get; set; } = 20;

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
    var idleDelay = TimeSpan.FromMilliseconds(500); // Check less often when no clients
    var wasProcessing = false;

    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        var hasClients = AudioVisualizationHub.ConnectedClients > 0;

        // Enable/disable sample processing based on whether anyone is watching.
        // When disabled, VisualizerService.ProcessSamples() becomes a no-op,
        // eliminating FFT, level metering, waveform buffering, and all related
        // lock contention and memory allocations on the audio thread.
        _visualizerService.IsProcessingEnabled = hasClients && IsEnabled;

        if (hasClients && IsEnabled && _visualizerService.IsActive)
        {
          if (!wasProcessing)
          {
            _logger.LogInformation(
              "Visualization broadcasting resumed ({Clients} client(s) connected)",
              AudioVisualizationHub.ConnectedClients);
          }
          wasProcessing = true;
          await BroadcastVisualizationDataAsync(stoppingToken);
          await Task.Delay(frameDelay, stoppingToken);
        }
        else
        {
          if (wasProcessing)
          {
            _logger.LogInformation("Visualization broadcasting paused (no clients connected)");
          }
          wasProcessing = false;
          await Task.Delay(idleDelay, stoppingToken);
        }
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

    _visualizerService.IsProcessingEnabled = false;
    _logger.LogInformation("VisualizationBroadcastService stopped");
  }

  private async Task BroadcastVisualizationDataAsync(CancellationToken cancellationToken)
  {
    // Note: SignalR doesn't provide a simple way to check if groups have connections.
    // The data generation is lightweight, so we generate and send to all subscribed groups.
    // If performance becomes a concern, consider tracking group membership separately.

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

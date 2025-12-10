using Microsoft.AspNetCore.SignalR;
using Radio.API.Models;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;

namespace Radio.API.Hubs;

/// <summary>
/// SignalR hub for real-time audio visualization data.
/// Provides spectrum, level, and waveform data to connected clients.
/// </summary>
/// <remarks>
/// Note: The connected client count is tracked using a static field, which means in multi-instance
/// deployments each instance will maintain its own count. For accurate cross-instance metrics,
/// consider using a distributed counter service (e.g., Redis) or a scoped service.
/// </remarks>
public class AudioVisualizationHub : Hub
{
  private readonly ILogger<AudioVisualizationHub> _logger;
  private readonly IVisualizerService _visualizerService;
  private readonly IMetricsCollector? _metricsCollector;
  private static int _connectedClients = 0;
  private static readonly object _lockObject = new();

  /// <summary>
  /// Initializes a new instance of the AudioVisualizationHub.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="visualizerService">The visualizer service.</param>
  /// <param name="metricsCollector">Optional metrics collector.</param>
  public AudioVisualizationHub(
    ILogger<AudioVisualizationHub> logger,
    IVisualizerService visualizerService,
    IMetricsCollector? metricsCollector = null)
  {
    _logger = logger;
    _visualizerService = visualizerService;
    _metricsCollector = metricsCollector;
  }

  /// <summary>
  /// Gets the current spectrum data.
  /// </summary>
  /// <returns>The current spectrum data.</returns>
  public SpectrumDataDto GetSpectrum()
  {
    var data = _visualizerService.GetSpectrumData();
    return MapToSpectrumDto(data);
  }

  /// <summary>
  /// Gets the current audio levels.
  /// </summary>
  /// <returns>The current level data.</returns>
  public LevelDataDto GetLevels()
  {
    var data = _visualizerService.GetLevelData();
    return MapToLevelDto(data);
  }

  /// <summary>
  /// Gets the current waveform data.
  /// </summary>
  /// <returns>The current waveform data.</returns>
  public WaveformDataDto GetWaveform()
  {
    var data = _visualizerService.GetWaveformData();
    return MapToWaveformDto(data);
  }

  /// <summary>
  /// Gets all visualization data combined.
  /// </summary>
  /// <returns>Combined visualization data.</returns>
  public VisualizationDataDto GetVisualization()
  {
    return new VisualizationDataDto
    {
      Spectrum = GetSpectrum(),
      Levels = GetLevels(),
      Waveform = GetWaveform(),
      IsActive = _visualizerService.IsActive
    };
  }

  /// <summary>
  /// Subscribes to spectrum data updates.
  /// Adds the connection to the "Spectrum" group.
  /// </summary>
  public async Task SubscribeToSpectrum()
  {
    await Groups.AddToGroupAsync(Context.ConnectionId, "Spectrum");
    _logger.LogDebug("Client {ConnectionId} subscribed to spectrum updates", Context.ConnectionId);
  }

  /// <summary>
  /// Unsubscribes from spectrum data updates.
  /// </summary>
  public async Task UnsubscribeFromSpectrum()
  {
    await Groups.RemoveFromGroupAsync(Context.ConnectionId, "Spectrum");
    _logger.LogDebug("Client {ConnectionId} unsubscribed from spectrum updates", Context.ConnectionId);
  }

  /// <summary>
  /// Subscribes to level data updates.
  /// Adds the connection to the "Levels" group.
  /// </summary>
  public async Task SubscribeToLevels()
  {
    await Groups.AddToGroupAsync(Context.ConnectionId, "Levels");
    _logger.LogDebug("Client {ConnectionId} subscribed to level updates", Context.ConnectionId);
  }

  /// <summary>
  /// Unsubscribes from level data updates.
  /// </summary>
  public async Task UnsubscribeFromLevels()
  {
    await Groups.RemoveFromGroupAsync(Context.ConnectionId, "Levels");
    _logger.LogDebug("Client {ConnectionId} unsubscribed from level updates", Context.ConnectionId);
  }

  /// <summary>
  /// Subscribes to waveform data updates.
  /// Adds the connection to the "Waveform" group.
  /// </summary>
  public async Task SubscribeToWaveform()
  {
    await Groups.AddToGroupAsync(Context.ConnectionId, "Waveform");
    _logger.LogDebug("Client {ConnectionId} subscribed to waveform updates", Context.ConnectionId);
  }

  /// <summary>
  /// Unsubscribes from waveform data updates.
  /// </summary>
  public async Task UnsubscribeFromWaveform()
  {
    await Groups.RemoveFromGroupAsync(Context.ConnectionId, "Waveform");
    _logger.LogDebug("Client {ConnectionId} unsubscribed from waveform updates", Context.ConnectionId);
  }

  /// <summary>
  /// Subscribes to all visualization data updates.
  /// </summary>
  public async Task SubscribeToAll()
  {
    await SubscribeToSpectrum();
    await SubscribeToLevels();
    await SubscribeToWaveform();
    _logger.LogDebug("Client {ConnectionId} subscribed to all visualization updates", Context.ConnectionId);
  }

  /// <summary>
  /// Unsubscribes from all visualization data updates.
  /// </summary>
  public async Task UnsubscribeFromAll()
  {
    await UnsubscribeFromSpectrum();
    await UnsubscribeFromLevels();
    await UnsubscribeFromWaveform();
    _logger.LogDebug("Client {ConnectionId} unsubscribed from all visualization updates", Context.ConnectionId);
  }

  /// <summary>
  /// Called when a client connects.
  /// </summary>
  public override async Task OnConnectedAsync()
  {
    lock (_lockObject)
    {
      _connectedClients++;
      _metricsCollector?.Gauge("websocket.connected_clients", _connectedClients);
    }

    _logger.LogInformation("Client {ConnectionId} connected to AudioVisualizationHub (total: {Count})", 
      Context.ConnectionId, _connectedClients);
    await base.OnConnectedAsync();
  }

  /// <summary>
  /// Called when a client disconnects.
  /// </summary>
  public override async Task OnDisconnectedAsync(Exception? exception)
  {
    lock (_lockObject)
    {
      _connectedClients--;
      _metricsCollector?.Gauge("websocket.connected_clients", _connectedClients);
    }

    if (exception != null)
    {
      _logger.LogWarning(exception, "Client {ConnectionId} disconnected with error (total: {Count})", 
        Context.ConnectionId, _connectedClients);
    }
    else
    {
      _logger.LogInformation("Client {ConnectionId} disconnected (total: {Count})", 
        Context.ConnectionId, _connectedClients);
    }
    await base.OnDisconnectedAsync(exception);
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

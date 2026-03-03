using Microsoft.Extensions.Logging;

namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// Tracks the current visualization mode and enabled state.
/// Fires events when changes occur so the API layer can broadcast via SignalR.
/// </summary>
public class VisualizationModeService
{
  private readonly ILogger<VisualizationModeService> _logger;
  private readonly object _lock = new();
  private string _currentMode = "VUMeter";
  private bool _isEnabled = true;

  // Available modes matching the Web UI VisualizerPanel
  private static readonly string[] Modes =
    ["VUMeter", "Waveform", "Spectrum", "Spectrogram", "Circular", "PhaseScope"];

  public VisualizationModeService(ILogger<VisualizationModeService> logger)
  {
    _logger = logger;
  }

  /// <summary>Current visualization mode name.</summary>
  public string CurrentMode
  {
    get { lock (_lock) return _currentMode; }
  }

  /// <summary>Whether visualization is enabled.</summary>
  public bool IsEnabled
  {
    get { lock (_lock) return _isEnabled; }
  }

  /// <summary>Fired when the visualization mode or enabled state changes.</summary>
  public event EventHandler<VisualizationModeChangedEventArgs>? ModeChanged;

  /// <summary>
  /// Cycle to the next visualization mode.
  /// </summary>
  /// <param name="direction">Positive = forward, negative = backward.</param>
  public void CycleMode(int direction)
  {
    lock (_lock)
    {
      var currentIndex = Array.IndexOf(Modes, _currentMode);
      if (currentIndex < 0) currentIndex = 0;

      var newIndex = ((currentIndex + direction) % Modes.Length + Modes.Length) % Modes.Length;
      _currentMode = Modes[newIndex];
    }

    _logger.LogInformation("Visualization mode changed to {Mode}", _currentMode);
    ModeChanged?.Invoke(this, new VisualizationModeChangedEventArgs
    {
      Mode = _currentMode,
      IsEnabled = _isEnabled
    });
  }

  /// <summary>
  /// Toggle visualization on/off.
  /// </summary>
  public void ToggleEnabled()
  {
    lock (_lock)
    {
      _isEnabled = !_isEnabled;
    }

    _logger.LogInformation("Visualization {State}", _isEnabled ? "enabled" : "disabled");
    ModeChanged?.Invoke(this, new VisualizationModeChangedEventArgs
    {
      Mode = _currentMode,
      IsEnabled = _isEnabled
    });
  }
}

/// <summary>
/// Event args for visualization mode changes.
/// </summary>
public class VisualizationModeChangedEventArgs : EventArgs
{
  /// <summary>The new visualization mode name.</summary>
  public required string Mode { get; init; }

  /// <summary>Whether visualization is enabled.</summary>
  public bool IsEnabled { get; init; }
}

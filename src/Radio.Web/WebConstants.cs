namespace Radio.Web;

/// <summary>
/// Shared constants used across the Radio.Web project.
/// </summary>
public static class WebConstants
{
  /// <summary>
  /// Default API base URL when not configured via appsettings.
  /// </summary>
  public const string DefaultApiBaseUrl = "http://localhost:5000";

  /// <summary>
  /// SignalR hub path constants.
  /// </summary>
  public static class HubPaths
  {
    public const string Audio = "/hubs/audio";
    public const string Visualization = "/hubs/visualization";
  }

  /// <summary>
  /// DOM element ID constants referenced from C# and JS.
  /// </summary>
  public static class ElementIds
  {
    public const string VisualizerCanvas = "visualizer-panel-canvas";
    public const string QueueScrollContainer = "queue-scroll-container";
  }
}

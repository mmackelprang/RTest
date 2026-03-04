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
  /// SignalR hub path constants. Delegates to <see cref="Radio.Core.Constants.ApiPaths.Hubs"/>.
  /// </summary>
  public static class HubPaths
  {
    public const string Audio = Radio.Core.Constants.ApiPaths.Hubs.Audio;
    public const string Visualization = Radio.Core.Constants.ApiPaths.Hubs.Visualization;
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

namespace Radio.Core.Constants;

/// <summary>
/// Shared path constants for SignalR hubs and audio streams.
/// Referenced by both Radio.API (endpoint registration) and Radio.Web (client connections).
/// </summary>
public static class ApiPaths
{
  /// <summary>SignalR hub paths.</summary>
  public static class Hubs
  {
    public const string Audio = "/hubs/audio";
    public const string Visualization = "/hubs/visualization";
  }

  /// <summary>Audio stream endpoint paths.</summary>
  public static class Streams
  {
    public const string Audio = "/stream/audio";
    public const string Mp3 = "/stream/audio/mp3";
  }
}

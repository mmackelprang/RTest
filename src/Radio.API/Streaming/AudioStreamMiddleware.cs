using Radio.Core.Interfaces.Audio;

namespace Radio.API.Streaming;

/// <summary>
/// Middleware for streaming audio data over HTTP.
/// Provides PCM audio stream for Chromecast and other clients.
/// </summary>
public class AudioStreamMiddleware
{
  private readonly RequestDelegate _next;
  private readonly ILogger<AudioStreamMiddleware> _logger;

  /// <summary>
  /// Initializes a new instance of the AudioStreamMiddleware.
  /// </summary>
  public AudioStreamMiddleware(RequestDelegate next, ILogger<AudioStreamMiddleware> logger)
  {
    _next = next;
    _logger = logger;
  }

  /// <summary>
  /// Processes the HTTP request for audio streaming.
  /// </summary>
  public async Task InvokeAsync(HttpContext context, IAudioEngine audioEngine)
  {
    // Check if this is a request for the audio stream endpoint
    if (!context.Request.Path.StartsWithSegments("/stream/audio"))
    {
      await _next(context);
      return;
    }

    _logger.LogInformation("Audio stream requested from {RemoteIp}", context.Connection.RemoteIpAddress);

    try
    {
      // Check if audio engine is ready
      if (!audioEngine.IsReady)
      {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsync("Audio engine not ready");
        return;
      }

      // Set response headers for audio streaming
      context.Response.ContentType = "audio/L16;rate=48000;channels=2";
      context.Response.Headers.CacheControl = "no-cache, no-store";
      context.Response.Headers.Connection = "keep-alive";
      context.Response.Headers["X-Content-Type-Options"] = "nosniff";

      // Get the mixed audio output stream
      var audioStream = audioEngine.GetMixedOutputStream();

      // Buffer for reading audio data
      var buffer = new byte[4096];
      var cancellationToken = context.RequestAborted;

      _logger.LogInformation("Starting audio stream to {RemoteIp}", context.Connection.RemoteIpAddress);

      // Stream audio data to client
      while (!cancellationToken.IsCancellationRequested)
      {
        var bytesRead = await audioStream.ReadAsync(buffer, cancellationToken);

        if (bytesRead > 0)
        {
          await context.Response.Body.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
          await context.Response.Body.FlushAsync(cancellationToken);
        }
        else
        {
          // No data available, wait briefly before trying again
          await Task.Delay(10, cancellationToken);
        }
      }
    }
    catch (OperationCanceledException)
    {
      _logger.LogInformation("Audio stream disconnected (client closed connection)");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error streaming audio");
      if (!context.Response.HasStarted)
      {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsync("Error streaming audio");
      }
    }
  }
}

/// <summary>
/// Extension methods for adding audio stream middleware to the pipeline.
/// </summary>
public static class AudioStreamMiddlewareExtensions
{
  /// <summary>
  /// Adds audio streaming middleware to the application pipeline.
  /// </summary>
  /// <param name="app">The application builder.</param>
  /// <returns>The application builder for chaining.</returns>
  public static IApplicationBuilder UseAudioStream(this IApplicationBuilder app)
  {
    return app.UseMiddleware<AudioStreamMiddleware>();
  }
}

using System.Net.Sockets;

namespace Radio.Web.Services;

/// <summary>
/// DelegatingHandler that throttles "connection refused" log messages to once per 10 seconds.
/// When the API is down, every HTTP call throws — this handler ensures we log
/// a single consolidated WARNING instead of per-call ERROR stack traces.
/// Also logs when the connection is restored after a failure.
/// </summary>
public class ApiConnectionLoggingHandler : DelegatingHandler
{
  private readonly ILogger<ApiConnectionLoggingHandler> _logger;

  // Static so all handler instances (one per HttpClient) share the same throttle state
  private static DateTime _lastLogTimeUtc = DateTime.MinValue;
  private static bool _wasDisconnected;
  private static readonly object ThrottleLock = new();
  private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(10);

  public ApiConnectionLoggingHandler(ILogger<ApiConnectionLoggingHandler> logger)
  {
    _logger = logger;
  }

  protected override async Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request, CancellationToken cancellationToken)
  {
    try
    {
      var response = await base.SendAsync(request, cancellationToken);

      // Log restoration after a previous disconnection
      bool shouldLogRestored;
      lock (ThrottleLock)
      {
        shouldLogRestored = _wasDisconnected;
        if (_wasDisconnected)
        {
          _wasDisconnected = false;
          _lastLogTimeUtc = DateTime.MinValue; // Reset throttle for next outage
        }
      }
      if (shouldLogRestored)
      {
        var host = request.RequestUri != null
          ? $"{request.RequestUri.Scheme}://{request.RequestUri.Host}:{request.RequestUri.Port}" : "unknown";
        _logger.LogInformation("API connection restored at {ApiHost}", host);
      }

      return response;
    }
    catch (HttpRequestException ex) when (IsConnectionRefused(ex))
    {
      LogThrottled(request.RequestUri);
      throw;
    }
  }

  private void LogThrottled(Uri? requestUri)
  {
    lock (ThrottleLock)
    {
      _wasDisconnected = true;
      var now = DateTime.UtcNow;
      if (now - _lastLogTimeUtc < LogInterval)
      {
        return;
      }

      _lastLogTimeUtc = now;
    }

    var host = requestUri != null ? $"{requestUri.Scheme}://{requestUri.Host}:{requestUri.Port}" : "unknown";
    _logger.LogWarning("API unavailable at {ApiHost} — retrying in the background", host);
  }

  private static bool IsConnectionRefused(HttpRequestException ex)
  {
    // Check for SocketException with ConnectionRefused anywhere in the chain
    var inner = ex.InnerException;
    while (inner != null)
    {
      if (inner is SocketException { SocketErrorCode: SocketError.ConnectionRefused })
      {
        return true;
      }

      inner = inner.InnerException;
    }
    return false;
  }
}

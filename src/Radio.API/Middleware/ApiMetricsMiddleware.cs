using System.Diagnostics;
using Radio.Core.Interfaces;
using Radio.Metrics;

namespace Radio.API.Middleware;

/// <summary>
/// Middleware that tracks API request metrics including request count,
/// latency, error rates, and per-endpoint breakdowns.
/// </summary>
public class ApiMetricsMiddleware
{
  private readonly RequestDelegate _next;
  private readonly IMetricsCollector? _metricsCollector;
  private readonly ILogger<ApiMetricsMiddleware> _logger;

  public ApiMetricsMiddleware(
    RequestDelegate next,
    IMetricsCollector? metricsCollector,
    ILogger<ApiMetricsMiddleware> logger)
  {
    _next = next;
    _metricsCollector = metricsCollector;
    _logger = logger;
  }

  public async Task InvokeAsync(HttpContext context)
  {
    if (_metricsCollector == null)
    {
      await _next(context);
      return;
    }

    _metricsCollector.Increment("api.requests_total");

    var endpoint = GetEndpointTag(context);
    _metricsCollector.Increment($"api.requests.{endpoint}");

    var stopwatch = Stopwatch.StartNew();
    try
    {
      await _next(context);
    }
    catch (Exception)
    {
      _metricsCollector.Increment("api.errors.unhandled");
      throw;
    }
    finally
    {
      stopwatch.Stop();
      _metricsCollector.Gauge("api.request_duration_ms", stopwatch.Elapsed.TotalMilliseconds);

      var statusCode = context.Response.StatusCode;
      if (statusCode >= 400 && statusCode < 500)
      {
        _metricsCollector.Increment("api.errors.client");
      }
      else if (statusCode >= 500)
      {
        _metricsCollector.Increment("api.errors.server");
      }
    }
  }

  /// <summary>
  /// Normalizes the request path to a stable metric tag.
  /// Uses the route template if available, otherwise the first two path segments.
  /// </summary>
  private static string GetEndpointTag(HttpContext context)
  {
    var endpoint = context.GetEndpoint();
    var routePattern = (endpoint as Microsoft.AspNetCore.Routing.RouteEndpoint)?.RoutePattern.RawText;
    if (!string.IsNullOrEmpty(routePattern))
    {
      return routePattern.Replace('/', '.').TrimStart('.');
    }

    // Fallback: first two segments (e.g., "api.audio")
    var path = context.Request.Path.Value ?? "/";
    var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
    return segments.Length switch
    {
      0 => "root",
      1 => segments[0],
      _ => $"{segments[0]}.{segments[1]}"
    };
  }
}

/// <summary>
/// Extension methods for adding API metrics middleware.
/// </summary>
public static class ApiMetricsMiddlewareExtensions
{
  /// <summary>
  /// Adds the API metrics middleware to the application pipeline.
  /// </summary>
  /// <param name="builder">The application builder.</param>
  /// <returns>The application builder for chaining.</returns>
  public static IApplicationBuilder UseApiMetrics(this IApplicationBuilder builder)
  {
    return builder.UseMiddleware<ApiMetricsMiddleware>();
  }
}

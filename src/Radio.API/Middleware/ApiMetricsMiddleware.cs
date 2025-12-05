using Radio.Core.Interfaces;

namespace Radio.API.Middleware;

/// <summary>
/// Middleware that tracks API request metrics.
/// </summary>
public class ApiMetricsMiddleware
{
  private readonly RequestDelegate _next;
  private readonly IMetricsCollector? _metricsCollector;
  private readonly ILogger<ApiMetricsMiddleware> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="ApiMetricsMiddleware"/> class.
  /// </summary>
  /// <param name="next">The next middleware in the pipeline.</param>
  /// <param name="metricsCollector">Optional metrics collector.</param>
  /// <param name="logger">The logger instance.</param>
  public ApiMetricsMiddleware(
    RequestDelegate next,
    IMetricsCollector? metricsCollector,
    ILogger<ApiMetricsMiddleware> logger)
  {
    _next = next;
    _metricsCollector = metricsCollector;
    _logger = logger;
  }

  /// <summary>
  /// Invokes the middleware to track the API request.
  /// </summary>
  /// <param name="context">The HTTP context.</param>
  public async Task InvokeAsync(HttpContext context)
  {
    // Track API request
    _metricsCollector?.Increment("api.requests_total");

    // Continue with the request
    await _next(context);
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

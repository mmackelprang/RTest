using Microsoft.AspNetCore.Mvc;
using Radio.Core.Interfaces;
using Radio.Core.Metrics;

namespace Radio.API.Controllers;

/// <summary>
/// API controller for metrics data access.
/// Provides endpoints for historical data and current snapshots.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class MetricsController : ControllerBase
{
  private readonly ILogger<MetricsController> _logger;
  private readonly IMetricsReader _metricsReader;
  private readonly IMetricsCollector? _metricsCollector;

  /// <summary>
  /// Initializes a new instance of the MetricsController.
  /// </summary>
  public MetricsController(
    ILogger<MetricsController> logger,
    IMetricsReader metricsReader,
    IMetricsCollector? metricsCollector = null)
  {
    _logger = logger;
    _metricsReader = metricsReader;
    _metricsCollector = metricsCollector;
  }

  /// <summary>
  /// Gets historical time-series data for a metric.
  /// </summary>
  /// <param name="key">The metric key (e.g., "audio.songs_played")</param>
  /// <param name="start">Start timestamp</param>
  /// <param name="end">End timestamp</param>
  /// <param name="resolution">Time bucket resolution (Minute, Hour, Day)</param>
  /// <param name="ct">Cancellation token</param>
  /// <returns>List of metric data points</returns>
  [HttpGet("history")]
  [ProducesResponseType(typeof(IReadOnlyList<MetricPoint>), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status500InternalServerError)]
  public async Task<ActionResult<IReadOnlyList<MetricPoint>>> GetHistory(
    [FromQuery] string key,
    [FromQuery] DateTimeOffset start,
    [FromQuery] DateTimeOffset end,
    [FromQuery] MetricResolution resolution = MetricResolution.Minute,
    CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(key))
    {
      return BadRequest("Metric key is required");
    }

    if (start >= end)
    {
      return BadRequest("Start time must be before end time");
    }

    try
    {
      var history = await _metricsReader.GetHistoryAsync(
        key,
        start,
        end,
        resolution,
        null,
        ct);

      return Ok(history);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to retrieve metric history for {Key}", key);
      return StatusCode(500, "An error occurred while retrieving metric history");
    }
  }

  /// <summary>
  /// Gets aggregate/current snapshot values for one or more metrics.
  /// For counters: returns total sum across all time periods.
  /// For gauges: returns the most recent value.
  /// </summary>
  /// <param name="keys">Comma-separated list of metric keys</param>
  /// <param name="ct">Cancellation token</param>
  /// <returns>Dictionary of metric keys to their aggregate values</returns>
  [HttpGet("snapshots")]
  [ProducesResponseType(typeof(IReadOnlyDictionary<string, double>), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status500InternalServerError)]
  public async Task<ActionResult<IReadOnlyDictionary<string, double>>> GetSnapshots(
    [FromQuery] string keys,
    CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(keys))
    {
      return BadRequest("At least one metric key is required");
    }

    try
    {
      var keyList = keys.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
      var snapshots = await _metricsReader.GetCurrentSnapshotsAsync(keyList, ct);

      return Ok(snapshots);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to retrieve metric snapshots");
      return StatusCode(500, "An error occurred while retrieving metric snapshots");
    }
  }

  /// <summary>
  /// Gets the aggregate value for a single metric.
  /// For counters: returns total sum across all time periods.
  /// For gauges: returns the most recent value.
  /// </summary>
  /// <param name="key">The metric key</param>
  /// <param name="ct">Cancellation token</param>
  /// <returns>The aggregate value, or 404 if metric not found</returns>
  [HttpGet("aggregate")]
  [ProducesResponseType(typeof(double), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status500InternalServerError)]
  public async Task<ActionResult<double>> GetAggregate(
    [FromQuery] string key,
    CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(key))
    {
      return BadRequest("Metric key is required");
    }

    try
    {
      var value = await _metricsReader.GetAggregateAsync(key, ct);

      if (value == null)
      {
        return NotFound($"Metric '{key}' not found");
      }

      return Ok(value.Value);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to retrieve aggregate for {Key}", key);
      return StatusCode(500, "An error occurred while retrieving metric aggregate");
    }
  }

  /// <summary>
  /// Lists all available metric keys in the system.
  /// </summary>
  /// <param name="ct">Cancellation token</param>
  /// <returns>List of metric keys</returns>
  [HttpGet("keys")]
  [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status500InternalServerError)]
  public async Task<ActionResult<IReadOnlyList<string>>> ListMetricKeys(
    CancellationToken ct = default)
  {
    try
    {
      var keys = await _metricsReader.ListMetricKeysAsync(ct);
      return Ok(keys);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to list metric keys");
      return StatusCode(500, "An error occurred while listing metric keys");
    }
  }

  /// <summary>
  /// Records a UI event metric from the frontend.
  /// This endpoint allows the frontend to track user interactions like button clicks.
  /// </summary>
  /// <param name="request">The event data including event name and optional metadata</param>
  /// <returns>Success status</returns>
  /// <response code="200">Event recorded successfully</response>
  /// <response code="400">Invalid request data</response>
  [HttpPost("event")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public IActionResult RecordUIEvent([FromBody] UIEventRequest request)
  {
    if (string.IsNullOrWhiteSpace(request?.EventName))
    {
      return BadRequest(new { error = "Event name is required" });
    }

    try
    {
      // Record the event as a counter metric
      var metricName = $"ui.{request.EventName.ToLowerInvariant().Replace(' ', '_')}";
      _metricsCollector?.Increment(metricName, 1.0, request.Tags);

      _logger.LogDebug("Recorded UI event: {EventName} with tags: {Tags}", 
        request.EventName, 
        request.Tags != null ? string.Join(", ", request.Tags.Select(kvp => $"{kvp.Key}={kvp.Value}")) : "none");

      return Ok(new { success = true, metric = metricName });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to record UI event: {EventName}", request.EventName);
      return BadRequest(new { error = "Failed to record event" });
    }
  }
}

/// <summary>
/// Request model for recording UI events.
/// </summary>
public class UIEventRequest
{
  /// <summary>
  /// The name of the UI event (e.g., "button_clicks", "play_clicked", "volume_changed").
  /// Will be converted to metric name like "ui.button_clicks".
  /// </summary>
  public string EventName { get; set; } = string.Empty;

  /// <summary>
  /// Optional tags/metadata for the event (e.g., button name, screen location).
  /// </summary>
  public IDictionary<string, string>? Tags { get; set; }
}

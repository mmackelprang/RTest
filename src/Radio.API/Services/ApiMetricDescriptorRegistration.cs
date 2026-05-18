using Microsoft.Extensions.Hosting;
using Radio.Metrics;

namespace Radio.API.Services;

/// <summary>
/// One-shot hosted service that registers authoritative
/// <see cref="MetricDescriptor"/> entries for the API-tier metric keys
/// emitted by middleware, audio outputs, and infrastructure services.
/// <para>
/// PR D #11 of the Arc follow-up backlog. Replaces the client-side
/// <c>MapKeyToUnit</c> key-pattern heuristic with a server-registered
/// unit table for representative metrics. The dashboard falls back to
/// the heuristic for any key not described here, so this list is a
/// migration surface, not a hard contract — adding/removing entries
/// is safe across deploys.
/// </para>
/// </summary>
public sealed class ApiMetricDescriptorRegistration : IHostedService
{
  private readonly IMetricDescriptorRegistry _registry;

  public ApiMetricDescriptorRegistration(IMetricDescriptorRegistry registry)
  {
    _registry = registry ?? throw new ArgumentNullException(nameof(registry));
  }

  public Task StartAsync(CancellationToken cancellationToken)
  {
    // API middleware metrics (see ApiMetricsMiddleware).
    _registry.Register(new MetricDescriptor
    {
      Key = "api.requests_total",
      Unit = MetricUnit.Count,
      Category = "API",
      DisplayName = "Requests",
    });
    _registry.Register(new MetricDescriptor
    {
      Key = "api.request_duration_ms",
      Unit = MetricUnit.Milliseconds,
      Category = "API",
      DisplayName = "Request Duration",
      Warn = 500,
      Critical = 2000,
    });
    _registry.Register(new MetricDescriptor
    {
      Key = "api.errors.unhandled",
      Unit = MetricUnit.Count,
      Category = "API",
      DisplayName = "Unhandled Errors",
    });
    _registry.Register(new MetricDescriptor
    {
      Key = "api.errors.client",
      Unit = MetricUnit.Count,
      Category = "API",
      DisplayName = "Client Errors (4xx)",
    });
    _registry.Register(new MetricDescriptor
    {
      Key = "api.errors.server",
      Unit = MetricUnit.Count,
      Category = "API",
      DisplayName = "Server Errors (5xx)",
    });

    // SignalR websocket metrics (see AudioStateHub).
    _registry.Register(new MetricDescriptor
    {
      Key = "websocket.connected_clients",
      Unit = MetricUnit.Count,
      Category = "WebSocket",
      DisplayName = "Connected Clients",
    });

    // Audio buffer health (see BufferedSoundGenerator).
    _registry.Register(new MetricDescriptor
    {
      Key = "audio.buffer.fill_percent",
      Unit = MetricUnit.Percent,
      Category = "Audio | Buffer",
      DisplayName = "Buffer Fill",
      // Inverted: low fill is bad. Dashboard uses InvertThresholds for
      // patterns matching "buffer_fill" / "fill" — descriptor reflects
      // the raw band; UI flips the colour.
      Warn = 50,
      Critical = 20,
    });
    _registry.Register(new MetricDescriptor
    {
      Key = "audio.buffer.underruns",
      Unit = MetricUnit.Count,
      Category = "Audio | Buffer",
      DisplayName = "Buffer Underruns",
      Warn = 1,
      Critical = 10,
    });
    _registry.Register(new MetricDescriptor
    {
      Key = "audio.buffer.samples_dropped",
      Unit = MetricUnit.Count,
      Category = "Audio | Buffer",
      DisplayName = "Samples Dropped",
    });

    // Audio callback timing.
    _registry.Register(new MetricDescriptor
    {
      Key = "audio.callback.max_interval_ms",
      Unit = MetricUnit.Milliseconds,
      Category = "Audio | Callback",
      DisplayName = "Max Callback Interval",
    });
    _registry.Register(new MetricDescriptor
    {
      Key = "audio.callback.max_execution_ms",
      Unit = MetricUnit.Milliseconds,
      Category = "Audio | Callback",
      DisplayName = "Max Callback Execution",
    });
    _registry.Register(new MetricDescriptor
    {
      Key = "audio.callback.missed_deadlines",
      Unit = MetricUnit.Count,
      Category = "Audio | Callback",
      DisplayName = "Missed Deadlines",
    });

    // Cast streaming metrics (see DirectCastStreamingService).
    _registry.Register(new MetricDescriptor
    {
      Key = "audio.cast.direct.chunks_sent",
      Unit = MetricUnit.Count,
      Category = "Audio | Cast",
      DisplayName = "Chunks Sent",
    });
    _registry.Register(new MetricDescriptor
    {
      Key = "audio.cast.direct.bytes_sent_mb",
      Unit = MetricUnit.Megabytes,
      Category = "Audio | Cast",
      DisplayName = "Bytes Sent",
    });
    _registry.Register(new MetricDescriptor
    {
      Key = "audio.cast.direct.silence_percent",
      Unit = MetricUnit.Percent,
      Category = "Audio | Cast",
      DisplayName = "Silence",
    });

    return Task.CompletedTask;
  }

  public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

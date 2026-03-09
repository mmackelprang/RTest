# Radio.Metrics

A lightweight metrics collection and time-series storage library with SQLite backend.

## Installation

```bash
dotnet add package Radio.Metrics
```

## Quick Start

```csharp
using Radio.Metrics;

// Register in DI
builder.Services.AddMetrics(builder.Configuration);
```

```json
// appsettings.json
{
  "Metrics": {
    "Enabled": true,
    "FlushIntervalSeconds": 60,
    "DatabasePath": "./data/metrics.db",
    "RetentionMinuteData": 120,
    "RetentionHourData": 48,
    "RetentionDayData": 365,
    "RollupIntervalMinutes": 60
  }
}
```

### Collecting Metrics

```csharp
public class OrderService
{
  private readonly IMetricsCollector _metrics;

  public OrderService(IMetricsCollector metrics) => _metrics = metrics;

  public void ProcessOrder(Order order)
  {
    _metrics.Increment("orders.processed");
    _metrics.Gauge("orders.queue_depth", GetQueueDepth());
  }
}
```

### Reading Metrics

```csharp
public class DashboardService
{
  private readonly IMetricsReader _reader;

  public DashboardService(IMetricsReader reader) => _reader = reader;

  public async Task<IReadOnlyList<MetricPoint>> GetOrderHistory()
  {
    return await _reader.GetHistoryAsync(
      "orders.processed",
      DateTimeOffset.UtcNow.AddHours(-24),
      DateTimeOffset.UtcNow,
      MetricResolution.Hour);
  }
}
```

## Key Types

| Type | Description |
|------|-------------|
| `IMetricsCollector` | Collect counters and gauges via `Increment()` and `Gauge()` |
| `IMetricsReader` | Query history, snapshots, aggregates, and metric keys |
| `MetricPoint` | A single time-series data point with value, count, min/max/last |
| `MetricType` | Counter (monotonic) or Gauge (variable) |
| `MetricResolution` | Minute, Hour, or Day time buckets |
| `MetricsOptions` | Configuration (enabled, flush interval, DB path, retention) |

## Features

- **Buffered writes** — metrics accumulate in memory, flushed periodically to SQLite
- **Multi-resolution storage** — minute, hour, and day tables with automatic rollup
- **Configurable retention** — prune old data based on per-resolution policies
- **System monitoring** — built-in CPU, memory, disk, and temperature gauges
- **Thread-safe** — concurrent metric collection from any thread
- **Zero external dependencies** — only SQLite and Microsoft.Extensions.*

## Architecture

```
IMetricsCollector (Increment/Gauge)
  -> BufferedMetricsCollector (in-memory buffers, timer-based flush)
    -> SqliteMetricsRepository (upsert to resolution tables)

MetricsRollupService (background)
  -> Minute -> Hour -> Day aggregation
  -> Retention-based pruning

SystemMonitorService (background)
  -> CPU, memory, disk, temperature gauges
```

## License

MIT

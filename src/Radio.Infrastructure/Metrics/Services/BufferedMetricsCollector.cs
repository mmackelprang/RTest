namespace Radio.Infrastructure.Metrics.Services;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Metrics;
using Radio.Infrastructure.Metrics.Data;
using Radio.Infrastructure.Metrics.Repositories;
using System.Collections.Concurrent;

/// <summary>
/// Buffered metrics collector that aggregates metrics in memory and flushes periodically.
/// Implements IHostedService for background operation.
/// </summary>
public sealed class BufferedMetricsCollector : IMetricsCollector, IHostedService, IAsyncDisposable
{
  private readonly ILogger<BufferedMetricsCollector> _logger;
  private readonly MetricsOptions _options;
  private readonly MetricsDbContext _dbContext;
  private readonly SqliteMetricsRepository _repository;
  
  private readonly ConcurrentDictionary<string, MetricBuffer> _buffers = new();
  private readonly SemaphoreSlim _flushLock = new(1, 1);
  
  private Timer? _flushTimer;
  private bool _isRunning;

  public BufferedMetricsCollector(
    ILogger<BufferedMetricsCollector> logger,
    IOptions<MetricsOptions> options,
    MetricsDbContext dbContext,
    SqliteMetricsRepository repository)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    _repository = repository ?? throw new ArgumentNullException(nameof(repository));
  }

  /// <inheritdoc/>
  public void Increment(string key, double value = 1.0, IDictionary<string, string>? tags = null)
  {
    if (!_options.Enabled || !_isRunning)
    {
      return;
    }

    var buffer = _buffers.GetOrAdd(key, _ => new MetricBuffer(key, MetricType.Counter));
    buffer.AddValue(value, DateTimeOffset.UtcNow);
  }

  /// <inheritdoc/>
  public void Gauge(string key, double value, IDictionary<string, string>? tags = null)
  {
    if (!_options.Enabled || !_isRunning)
    {
      return;
    }

    var buffer = _buffers.GetOrAdd(key, _ => new MetricBuffer(key, MetricType.Gauge));
    buffer.AddValue(value, DateTimeOffset.UtcNow);
  }

  /// <inheritdoc/>
  public int BufferedCount => _buffers.Count;

  /// <inheritdoc/>
  public async Task StartAsync(CancellationToken ct)
  {
    if (!_options.Enabled)
    {
      _logger.LogInformation("Metrics collection is disabled");
      return;
    }

    _logger.LogInformation("Starting metrics collector service");

    // Initialize database
    await _dbContext.InitializeAsync(ct);

    // Start flush timer
    var flushInterval = TimeSpan.FromSeconds(_options.FlushIntervalSeconds);
    _flushTimer = new Timer(
      async _ => await FlushAsync(CancellationToken.None),
      null,
      flushInterval,
      flushInterval);

    _isRunning = true;
    _logger.LogInformation("Metrics collector started. Flushing every {Interval} seconds",
      _options.FlushIntervalSeconds);
  }

  /// <inheritdoc/>
  public async Task StopAsync(CancellationToken ct)
  {
    _logger.LogInformation("Stopping metrics collector service");

    _isRunning = false;

    // Stop timer
    if (_flushTimer != null)
    {
      await _flushTimer.DisposeAsync();
      _flushTimer = null;
    }

    // Final flush
    await FlushAsync(ct);

    _logger.LogInformation("Metrics collector stopped");
  }

  /// <summary>
  /// Flushes all buffered metrics to the database.
  /// </summary>
  private async Task FlushAsync(CancellationToken ct)
  {
    if (_buffers.IsEmpty)
    {
      return;
    }

    await _flushLock.WaitAsync(ct);
    try
    {
      // Snapshot current buffers and create new ones
      var buffersToFlush = new List<MetricBuffer>();
      foreach (var kvp in _buffers)
      {
        if (_buffers.TryRemove(kvp.Key, out var buffer))
        {
          buffersToFlush.Add(buffer);
        }
      }

      if (buffersToFlush.Count == 0)
      {
        return;
      }

      _logger.LogDebug("Flushing {Count} metric buffers to database", buffersToFlush.Count);

      // Process each buffer
      foreach (var buffer in buffersToFlush)
      {
        var buckets = buffer.GetBuckets(MetricResolution.Minute);
        if (buckets.Any())
        {
          await _repository.SaveBucketsAsync(
            buffer.Key,
            buffer.Type,
            buffer.Unit,
            MetricResolution.Minute,
            buckets,
            ct);
        }
      }

      _logger.LogInformation("Flushed {Count} metrics to database", buffersToFlush.Count);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error flushing metrics to database");
    }
    finally
    {
      _flushLock.Release();
    }
  }

  public async ValueTask DisposeAsync()
  {
    if (_flushTimer != null)
    {
      await _flushTimer.DisposeAsync();
    }

    _flushLock.Dispose();
    _buffers.Clear();
  }
}

/// <summary>
/// In-memory buffer for a single metric.
/// Thread-safe accumulation of metric values.
/// </summary>
internal sealed class MetricBuffer
{
  private readonly object _lock = new();
  private readonly List<MetricSample> _samples = new();

  public string Key { get; }
  public MetricType Type { get; }
  public string? Unit { get; set; }

  public MetricBuffer(string key, MetricType type)
  {
    Key = key;
    Type = type;
  }

  public void AddValue(double value, DateTimeOffset timestamp)
  {
    lock (_lock)
    {
      _samples.Add(new MetricSample
      {
        Value = value,
        Timestamp = timestamp
      });
    }
  }

  public IEnumerable<MetricBucket> GetBuckets(MetricResolution resolution)
  {
    List<MetricSample> samples;
    lock (_lock)
    {
      samples = new List<MetricSample>(_samples);
      _samples.Clear();
    }

    if (samples.Count == 0)
    {
      return Array.Empty<MetricBucket>();
    }

    // Group samples by time bucket
    var bucketSize = resolution switch
    {
      MetricResolution.Minute => 60,
      MetricResolution.Hour => 3600,
      MetricResolution.Day => 86400,
      _ => 60
    };

    var buckets = samples
      .GroupBy(s => (s.Timestamp.ToUnixTimeSeconds() / bucketSize) * bucketSize)
      .Select(group => CreateBucket(group.Key, group))
      .ToList();

    return buckets;
  }

  private MetricBucket CreateBucket(long timestamp, IEnumerable<MetricSample> samples)
  {
    var samplesList = samples.ToList();
    
    if (Type == MetricType.Counter)
    {
      // For counters, sum all deltas
      return new MetricBucket
      {
        Timestamp = timestamp,
        ValueSum = samplesList.Sum(s => s.Value),
        ValueCount = samplesList.Count
      };
    }
    else
    {
      // For gauges, calculate min/max/avg/last
      var values = samplesList.Select(s => s.Value).ToList();
      return new MetricBucket
      {
        Timestamp = timestamp,
        ValueSum = values.Sum(),
        ValueCount = values.Count,
        ValueMin = values.Min(),
        ValueMax = values.Max(),
        ValueLast = samplesList.OrderByDescending(s => s.Timestamp).First().Value
      };
    }
  }
}

internal sealed record MetricSample
{
  public required double Value { get; init; }
  public required DateTimeOffset Timestamp { get; init; }
}

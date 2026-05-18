namespace Radio.Metrics;

using System.Collections.Concurrent;

/// <summary>
/// In-memory <see cref="IMetricDescriptorRegistry"/> implementation. Registered
/// once at startup and consulted by the metrics API for the lifetime of the
/// process. Thread-safe so producers can register from <c>BackgroundService</c>
/// constructors / hosted-service start hooks concurrently.
/// </summary>
public sealed class MetricDescriptorRegistry : IMetricDescriptorRegistry
{
  // ConcurrentDictionary for atomic Register; a separate List for insertion
  // order is rebuilt on every Register so All is stable across enumeration.
  // The All count is bounded by the number of distinct metric keys (~dozens
  // in practice) — rebuilding is cheap and avoids ordering surprises during
  // a re-registration.
  private readonly ConcurrentDictionary<string, MetricDescriptor> _byKey = new();
  private readonly object _orderLock = new();
  private List<MetricDescriptor> _ordered = new();

  /// <inheritdoc />
  public IReadOnlyList<MetricDescriptor> All
  {
    get
    {
      lock (_orderLock)
      {
        // Snapshot to keep the returned list immune to mid-enumeration writes.
        return _ordered.ToArray();
      }
    }
  }

  /// <inheritdoc />
  public MetricDescriptor? GetByKey(string key)
  {
    if (string.IsNullOrEmpty(key))
    {
      return null;
    }
    return _byKey.TryGetValue(key, out var d) ? d : null;
  }

  /// <inheritdoc />
  public void Register(MetricDescriptor descriptor)
  {
    if (descriptor == null)
    {
      throw new ArgumentNullException(nameof(descriptor));
    }
    if (string.IsNullOrEmpty(descriptor.Key))
    {
      throw new ArgumentException("Descriptor.Key is required", nameof(descriptor));
    }

    var isNew = !_byKey.ContainsKey(descriptor.Key);
    _byKey[descriptor.Key] = descriptor;

    lock (_orderLock)
    {
      if (isNew)
      {
        _ordered = new List<MetricDescriptor>(_ordered) { descriptor };
      }
      else
      {
        // Replace in-place; preserve insertion order.
        var copy = new List<MetricDescriptor>(_ordered.Count);
        foreach (var d in _ordered)
        {
          copy.Add(d.Key == descriptor.Key ? descriptor : d);
        }
        _ordered = copy;
      }
    }
  }
}

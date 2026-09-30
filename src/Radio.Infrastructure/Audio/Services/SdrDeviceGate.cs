using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// Arbitrates the single RTL-SDR dongle between the radio source and idle
/// band sweeps (AUD-76). The radio's claim always wins: claiming cancels a
/// live sweep lease and waits (bounded) for the sweep to release the device.
/// </summary>
/// <remarks>
/// The gate records who holds the device; it does not open or close it.
/// Holders are expected to have closed their device handle before releasing.
/// </remarks>
public sealed class SdrDeviceGate
{
  /// <summary>How long a radio claim waits for a cancelled sweep lease to be disposed.</summary>
  public static readonly TimeSpan LeaseReleaseTimeout = TimeSpan.FromSeconds(3);

  private readonly object _lock = new();
  private readonly ILogger<SdrDeviceGate> _logger;
  private readonly TimeProvider _timeProvider;
  private bool _heldByRadio;
  private SweepLease? _lease;

  /// <summary>Creates the gate.</summary>
  /// <param name="logger">Logger; a null logger is used when omitted.</param>
  /// <param name="timeProvider">Clock for the release timeout; <see cref="TimeProvider.System"/> when omitted.</param>
  public SdrDeviceGate(ILogger<SdrDeviceGate>? logger = null, TimeProvider? timeProvider = null)
  {
    _logger = logger ?? NullLogger<SdrDeviceGate>.Instance;
    _timeProvider = timeProvider ?? TimeProvider.System;
  }

  /// <summary>True between <see cref="ClaimForRadioAsync"/> and <see cref="ReleaseFromRadio"/>.</summary>
  public bool IsHeldByRadio
  {
    get
    {
      lock (_lock)
      {
        return _heldByRadio;
      }
    }
  }

  /// <summary>True while a sweep lease has been handed out and not yet disposed.</summary>
  public bool IsLeasedForSweep
  {
    get
    {
      lock (_lock)
      {
        return _lease != null;
      }
    }
  }

  /// <summary>
  /// Marks the radio as the holder. If a sweep lease is live, cancels its
  /// token and waits up to <see cref="LeaseReleaseTimeout"/> for the lease to
  /// be disposed; on timeout logs a warning and returns anyway. Calling it
  /// again while the radio already holds the gate is harmless.
  /// </summary>
  /// <param name="cancellationToken">Stops waiting for the lease.</param>
  public async Task ClaimForRadioAsync(CancellationToken cancellationToken = default)
  {
    SweepLease? lease;
    lock (_lock)
    {
      _heldByRadio = true;
      lease = _lease;
    }

    if (lease == null)
    {
      return;
    }

    _logger.LogInformation("Radio claimed the SDR device; cancelling the idle band sweep");
    lease.RequestCancel();

    try
    {
      await lease.Released.WaitAsync(LeaseReleaseTimeout, _timeProvider, cancellationToken).ConfigureAwait(false);
    }
    catch (TimeoutException)
    {
      _logger.LogWarning(
        "Idle band sweep did not release the SDR device within {Timeout}s of cancellation; radio proceeding",
        LeaseReleaseTimeout.TotalSeconds);
    }
  }

  /// <summary>Clears the radio's hold.</summary>
  public void ReleaseFromRadio()
  {
    lock (_lock)
    {
      _heldByRadio = false;
    }
  }

  /// <summary>
  /// Hands out the device for an idle sweep.
  /// </summary>
  /// <returns>
  /// A lease, or null when the radio holds the gate or another lease is live.
  /// </returns>
  public SweepLease? TryAcquireForSweep()
  {
    lock (_lock)
    {
      if (_heldByRadio || _lease != null)
      {
        return null;
      }

      _lease = new SweepLease(this);
      return _lease;
    }
  }

  private void OnLeaseDisposed(SweepLease lease)
  {
    lock (_lock)
    {
      if (ReferenceEquals(_lease, lease))
      {
        _lease = null;
      }
    }
  }

  /// <summary>
  /// The idle sweep's hold on the device. <see cref="Token"/> is cancelled when
  /// the radio claims the gate; <see cref="Dispose"/> releases the hold and
  /// lets a waiting radio claim continue.
  /// </summary>
  public sealed class SweepLease : IDisposable
  {
    private readonly SdrDeviceGate _gate;
    private readonly CancellationTokenSource _cts = new();
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;

    internal SweepLease(SdrDeviceGate gate)
    {
      _gate = gate;
    }

    /// <summary>Cancelled when the radio claims the device.</summary>
    public CancellationToken Token => _cts.Token;

    /// <summary>Completes when the lease is disposed.</summary>
    internal Task Released => _released.Task;

    internal void RequestCancel()
    {
      try
      {
        _cts.Cancel();
      }
      catch (ObjectDisposedException)
      {
        // Already disposed: the device is already released.
      }
    }

    /// <summary>Releases the device back to the gate.</summary>
    public void Dispose()
    {
      if (Interlocked.Exchange(ref _disposed, 1) == 1)
      {
        return;
      }

      _gate.OnLeaseDisposed(this);
      _released.TrySetResult();
      _cts.Dispose();
    }
  }
}

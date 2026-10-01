using Microsoft.Extensions.Time.Testing;
using Radio.Infrastructure.Audio.Services;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Services;

/// <summary>
/// AUD-76: the gate that keeps an idle band sweep from holding the RTL-SDR
/// against the radio source. No test here waits on wall-clock time; the one
/// timeout case drives a <see cref="FakeTimeProvider"/>.
/// </summary>
public class SdrDeviceGateTests
{
  [Fact]
  public async Task ClaimForRadio_WithNoLease_CompletesImmediately()
  {
    SdrDeviceGate gate = new();

    Task claim = gate.ClaimForRadioAsync();

    Assert.True(claim.IsCompletedSuccessfully);
    await claim;
    Assert.True(gate.IsHeldByRadio);
  }

  [Fact]
  public async Task ClaimForRadio_CancelsLiveLease_AndCompletesOnlyWhenLeaseIsDisposed()
  {
    // A never-advanced fake clock: the claim's release timeout cannot fire,
    // so only the lease's disposal can complete it.
    SdrDeviceGate gate = new(timeProvider: new FakeTimeProvider());
    SdrDeviceGate.SweepLease lease = gate.TryAcquireForSweep()!;
    Assert.NotNull(lease);

    Task claim = gate.ClaimForRadioAsync();

    Assert.True(lease.Token.IsCancellationRequested);
    Assert.False(claim.IsCompleted);
    Assert.True(gate.IsHeldByRadio);

    lease.Dispose();
    await claim;

    Assert.False(gate.IsLeasedForSweep);
  }

  [Fact]
  public async Task ClaimForRadio_LeaseNeverDisposed_ProceedsAfterTimeout()
  {
    FakeTimeProvider time = new();
    SdrDeviceGate gate = new(timeProvider: time);
    SdrDeviceGate.SweepLease lease = gate.TryAcquireForSweep()!;

    Task claim = gate.ClaimForRadioAsync();
    Assert.False(claim.IsCompleted);

    time.Advance(SdrDeviceGate.LeaseReleaseTimeout);
    await claim;

    Assert.True(gate.IsHeldByRadio);
    lease.Dispose();
  }

  [Fact]
  public async Task TryAcquireForSweep_WhileRadioHolds_ReturnsNull()
  {
    SdrDeviceGate gate = new();
    await gate.ClaimForRadioAsync();

    Assert.Null(gate.TryAcquireForSweep());
  }

  [Fact]
  public void TryAcquireForSweep_WhileAnotherLeaseIsLive_ReturnsNull()
  {
    SdrDeviceGate gate = new();
    using SdrDeviceGate.SweepLease? first = gate.TryAcquireForSweep();

    Assert.NotNull(first);
    Assert.Null(gate.TryAcquireForSweep());
  }

  [Fact]
  public async Task ReleaseFromRadio_AllowsANewLease()
  {
    SdrDeviceGate gate = new();
    await gate.ClaimForRadioAsync();

    gate.ReleaseFromRadio();
    using SdrDeviceGate.SweepLease? lease = gate.TryAcquireForSweep();

    Assert.NotNull(lease);
    Assert.False(gate.IsHeldByRadio);
  }

  [Fact]
  public void DisposedLease_AllowsANewLease()
  {
    SdrDeviceGate gate = new();
    gate.TryAcquireForSweep()!.Dispose();

    using SdrDeviceGate.SweepLease? second = gate.TryAcquireForSweep();

    Assert.NotNull(second);
  }

  [Fact]
  public async Task ClaimForRadio_IsIdempotent()
  {
    SdrDeviceGate gate = new();

    await gate.ClaimForRadioAsync();
    await gate.ClaimForRadioAsync();

    Assert.True(gate.IsHeldByRadio);
    gate.ReleaseFromRadio();
    Assert.False(gate.IsHeldByRadio);
  }
}

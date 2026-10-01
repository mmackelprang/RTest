using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Radio.Core.Configuration;
using Radio.Infrastructure.Audio.Services;
using Radio.Infrastructure.Audio.Sources.Primary;
using RTLSDRCore;
using RTLSDRCore.Hardware;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Sources.Primary;

/// <summary>
/// AUD-76: the radio source claims the SDR device gate before its receiver
/// opens the dongle, and releases it after the receiver shuts down.
/// </summary>
public class SDRRadioAudioSourceDeviceGateTests
{
  private readonly List<string> _events = new();
  private readonly Mock<ISdrDevice> _device = new();
  // A never-advanced fake clock: the gate's lease-release timeout cannot
  // fire, so a claim that waits on a lease completes only when it is disposed.
  private readonly SdrDeviceGate _gate = new(timeProvider: new FakeTimeProvider());
  private bool _startStreamingResult = true;

  public SDRRadioAudioSourceDeviceGateTests()
  {
    _device.Setup(d => d.DeviceInfo).Returns(new RTLSDRCore.Models.DeviceInfo
    {
      Name = "Mock RTL-SDR",
      Type = RTLSDRCore.Enums.DeviceType.Mock,
      IsAvailable = true,
      MinFrequencyHz = 24_000_000,
      MaxFrequencyHz = 1_766_000_000,
    });
    bool open = false;
    _device.Setup(d => d.IsOpen).Returns(() => open);
    _device.Setup(d => d.Open()).Returns(() =>
    {
      lock (_events)
      {
        _events.Add($"open(gateHeld={_gate.IsHeldByRadio})");
      }
      open = true;
      return true;
    });
    _device.Setup(d => d.Close()).Callback(() =>
    {
      lock (_events)
      {
        _events.Add($"close(gateHeld={_gate.IsHeldByRadio})");
      }
      open = false;
    });
    _device.Setup(d => d.StartStreaming()).Returns(() => _startStreamingResult);
  }

  private SDRRadioAudioSource CreateSource()
  {
    Mock<IOptionsMonitor<RadioOptions>> options = new();
    options.Setup(o => o.CurrentValue).Returns(new RadioOptions());
    return new SDRRadioAudioSource(
      NullLogger<SDRRadioAudioSource>.Instance,
      new RadioReceiver(_device.Object),
      options.Object,
      deviceGate: _gate);
  }

  [Fact]
  public async Task StartupAsync_ClaimsGateBeforeReceiverOpensDevice_ShutdownReleases()
  {
    await using SDRRadioAudioSource source = CreateSource();

    Assert.True(await source.StartupAsync());

    Assert.Equal(new[] { "open(gateHeld=True)" }, _events);
    Assert.True(_gate.IsHeldByRadio);

    await source.ShutdownAsync();

    Assert.False(_gate.IsHeldByRadio);
  }

  [Fact]
  public async Task StartupAsync_WithIdleSweepLease_CancelsItAndOpensOnlyAfterItIsDisposed()
  {
    await using SDRRadioAudioSource source = CreateSource();
    SdrDeviceGate.SweepLease lease = _gate.TryAcquireForSweep()!;
    // Stand-in for BandMapService: on cancellation, close the sweep's device
    // and dispose the lease — from another thread, as the real sweep does.
    lease.Token.Register(() => Task.Run(() =>
    {
      lock (_events)
      {
        _events.Add("sweep-released");
      }
      lease.Dispose();
    }));

    Assert.True(await source.StartupAsync());

    Assert.Equal(new[] { "sweep-released", "open(gateHeld=True)" }, _events);
    await source.ShutdownAsync();
  }

  [Fact]
  public async Task StartupAsync_WhenReceiverFailsToStart_ReleasesGate()
  {
    _startStreamingResult = false;
    await using SDRRadioAudioSource source = CreateSource();

    Assert.False(await source.StartupAsync());

    // The receiver closed the device while the gate was still held, and only
    // then was the gate freed for an idle sweep.
    Assert.Equal(new[] { "open(gateHeld=True)", "close(gateHeld=True)" }, _events);
    Assert.False(_device.Object.IsOpen);
    Assert.False(_gate.IsHeldByRadio);
    Assert.NotNull(_gate.TryAcquireForSweep());
  }

  [Fact]
  public async Task StartupAsync_WhenStartupThrows_ClosesDeviceBeforeReleasingGate()
  {
    _device.Setup(d => d.SetSampleRate(It.IsAny<int>())).Throws(new InvalidOperationException("usb gone"));
    await using SDRRadioAudioSource source = CreateSource();

    Assert.False(await source.StartupAsync());

    Assert.Equal(new[] { "open(gateHeld=True)", "close(gateHeld=True)" }, _events);
    Assert.False(_gate.IsHeldByRadio);
  }

  [Fact]
  public async Task CanSweepLive_TracksReceiverRunning()
  {
    await using SDRRadioAudioSource source = CreateSource();

    Assert.False(source.CanSweepLive);
    await source.StartupAsync();
    Assert.True(source.CanSweepLive);
    await source.ShutdownAsync();
    Assert.False(source.CanSweepLive);
  }

  [Fact]
  public async Task DisposeAsync_WhileHoldingGate_ReleasesIt()
  {
    SDRRadioAudioSource source = CreateSource();
    await source.StartupAsync();
    Assert.True(_gate.IsHeldByRadio);

    await source.DisposeAsync();

    // Disposal shut the receiver down (closing the device) before freeing the gate.
    Assert.Equal(new[] { "open(gateHeld=True)", "close(gateHeld=True)" }, _events);
    Assert.False(_gate.IsHeldByRadio);
  }
}

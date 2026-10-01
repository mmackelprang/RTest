using RTLSDRCore.Models;
using RTLSDRCore.Sweep;

namespace RTLSDRCore.Tests.Sweep;

public class DeviceSweepTunerTests
{
  [Fact]
  public void Open_ConfiguresRateAndManualGain()
  {
    FakeSdrDevice device = new();
    using DeviceSweepTuner tuner = new(device, 28f);

    tuner.Open();

    Assert.Equal(new[] { "Open", "SetSampleRate:240000", "SetGainMode:False", "SetGain:28" }, device.Calls);
  }

  [Fact]
  public void Open_WhenDeviceWillNotOpen_Throws()
  {
    FakeSdrDevice device = new() { OpenResult = false };
    using DeviceSweepTuner tuner = new(device, 28f);

    Assert.Throws<InvalidOperationException>(tuner.Open);
  }

  [Fact]
  public void Dispose_ClosesAndDisposesDevice()
  {
    FakeSdrDevice device = new();
    DeviceSweepTuner tuner = new(device, 28f);
    tuner.Open();

    tuner.Dispose();

    Assert.False(device.IsOpen);
    Assert.True(device.Disposed);
    Assert.Equal(new[] { "Close", "Dispose" }, device.Calls.TakeLast(2));
  }

  [Fact]
  public void Read_LoopsOverShortReadsUntilFull()
  {
    FakeSdrDevice device = new();
    device.ReadCounts.Enqueue(1000);
    device.ReadCounts.Enqueue(0);
    device.ReadCounts.Enqueue(2000);
    using DeviceSweepTuner tuner = new(device, 28f);
    tuner.Open();

    int read = tuner.Read(new IqSample[4096], CancellationToken.None);

    Assert.Equal(4096, read);
    Assert.Equal(new[] { "ReadSamples:1000", "ReadSamples:0", "ReadSamples:2000", "ReadSamples:1096" },
      device.Calls.Where(c => c.StartsWith("ReadSamples", StringComparison.Ordinal)));
  }

  [Fact]
  public void Read_GivesUpAfterConsecutiveEmptyReads()
  {
    FakeSdrDevice device = new();
    device.ReadCounts.Enqueue(100);
    for (int i = 0; i < DeviceSweepTuner.MaxConsecutiveEmptyReads; i++)
    {
      device.ReadCounts.Enqueue(0);
    }
    using DeviceSweepTuner tuner = new(device, 28f);
    tuner.Open();

    int read = tuner.Read(new IqSample[4096], CancellationToken.None);

    Assert.Equal(100, read);
  }

  [Fact]
  public void Read_WhenCancelled_Throws()
  {
    FakeSdrDevice device = new();
    using DeviceSweepTuner tuner = new(device, 28f);
    tuner.Open();
    using CancellationTokenSource cts = new();
    cts.Cancel();

    Assert.ThrowsAny<OperationCanceledException>(() => tuner.Read(new IqSample[4096], cts.Token));
  }

  [Fact]
  public void Sweep_OverDeviceTuner_FindsTheStation()
  {
    FakeSdrDevice device = new();
    using DeviceSweepTuner tuner = new(device, 28f);
    tuner.Open();

    IReadOnlyList<ChannelLevel> levels = BandSweeper.Sweep(
      tuner, FmChannelPlan.Channels, BandSweeper.DefaultSamplesPerMeasurement, null, CancellationToken.None);

    Assert.Equal(FakeSdrDevice.StationHz, levels.MaxBy(l => l.LevelDbfs)!.FrequencyHz);
  }
}

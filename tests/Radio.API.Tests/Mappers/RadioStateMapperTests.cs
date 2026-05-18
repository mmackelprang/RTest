using Radio.API.Mappers;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;

namespace Radio.API.Tests.Mappers;

/// <summary>
/// Locks down the signal-meter projection math introduced in PR 1 of the
/// Radio Controller Polish arc: clamp at the API boundary, surface
/// overdrive via a separate <c>Clip</c> flag, and linear-fit raw percent →
/// dBu in the [-60, 0] band.
/// </summary>
public class RadioStateMapperTests
{
  [Theory]
  [InlineData(null, null)]
  [InlineData(0, 0)]
  [InlineData(50, 50)]
  [InlineData(100, 100)]
  [InlineData(101, 100)]     // 1% overshoot clamped
  [InlineData(118, 100)]     // historical worst-case from logs
  [InlineData(-5, 0)]        // negative-clipping clamp
  public void ClampSignalPercent_ReturnsExpected(int? raw, int? expected)
  {
    Assert.Equal(expected, RadioStateMapper.ClampSignalPercent(raw));
  }

  [Theory]
  [InlineData(null, false)]
  [InlineData(0, false)]
  [InlineData(99, false)]
  [InlineData(100, false)]
  [InlineData(101, true)]
  [InlineData(118, true)]
  public void IsClipping_TriggersOnlyAbove100(int? raw, bool expected)
  {
    Assert.Equal(expected, RadioStateMapper.IsClipping(raw));
  }

  [Theory]
  [InlineData(null, -60.0)]
  [InlineData(0, -60.0)]
  [InlineData(50, -30.0)]
  [InlineData(100, 0.0)]
  [InlineData(118, 0.0)]     // overdrive saturates at 0 dBu — IsClipping carries the rest
  public void SignalToDbu_LinearFit(int? raw, double expected)
  {
    Assert.Equal(expected, RadioStateMapper.SignalToDbu(raw), precision: 3);
  }

  [Theory]
  [InlineData(0, -60.0)]
  [InlineData(50, -30.0)]
  [InlineData(100, 0.0)]
  [InlineData(200, 0.0)]
  [InlineData(-10, -60.0)]
  public void PercentToDbu_LinearFitWithClamp(int percent, double expected)
  {
    Assert.Equal(expected, RadioStateMapper.PercentToDbu(percent), precision: 3);
  }

  [Fact]
  public void SignalMinDbu_AndMax_AreNoiseFloorAndFullScale()
  {
    Assert.Equal(-60.0, RadioStateMapper.SignalMinDbu);
    Assert.Equal(0.0, RadioStateMapper.SignalMaxDbu);
  }

  // ─── PR 3 of the Radio Controller Polish arc ──────────────────────────────
  // Integration-style test: drive a real IRadioControl snapshot through the
  // actual MapToRadioStateDto code path so the wire-shape RdsRadioText is
  // proven to flow from the source through the projection without being
  // dropped by a hand-crafted DTO record. Tester's PR 2 retrospective flagged
  // bUnit reflection-injected state as having missed a wire-path regression;
  // this guards against the same class of bug for the new RT field.

  [Fact]
  public void MapToRadioStateDto_FlowsRdsRadioText_FromSourceToDto()
  {
    var source = new FakeRadioControl
    {
      RdsStationNameValue = "KQED",
      RdsProgramTypeValue = "News",
      RdsRadioTextValue = "Now Playing — Morning Edition",
    };

    var dto = source.MapToRadioStateDto();

    Assert.Equal("KQED", dto.RdsStationName);
    Assert.Equal("News", dto.RdsProgramType);
    Assert.Equal("Now Playing — Morning Edition", dto.RdsRadioText);
  }

  [Fact]
  public void MapToRadioStateDto_RdsRadioText_NullPassesThrough()
  {
    var source = new FakeRadioControl
    {
      RdsStationNameValue = "KQED",
      RdsRadioTextValue = null,
    };

    var dto = source.MapToRadioStateDto();

    Assert.Null(dto.RdsRadioText);
  }

  /// <summary>
  /// Minimal <see cref="IRadioControl"/> stub for projection tests. Only the
  /// fields the mapper actually reads need to be settable; the rest get safe
  /// defaults so the test class can drop fields it doesn't care about.
  /// </summary>
  private sealed class FakeRadioControl : IRadioControl
  {
    public string? RdsStationNameValue { get; set; }
    public string? RdsProgramTypeValue { get; set; }
    public string? RdsRadioTextValue { get; set; }

    public bool IsRunning => true;
    public Frequency CurrentFrequency => Frequency.FromMegahertz(101.5);
    public bool IsScanning => false;
    public ScanDirection? ScanDirection => null;
    public int ScanStopThreshold => 50;
    public RadioBand CurrentBand => RadioBand.FM;
    public Frequency FrequencyStep => Frequency.FromKilohertz(100);
    public float Volume { get; set; } = 0.5f;
    public int DeviceVolume { get; set; } = 50;
    public bool IsMuted { get; set; }
    public float SquelchThreshold { get; set; }
    public RadioEqualizerMode EqualizerMode => RadioEqualizerMode.Normal;
    public bool AutoGainEnabled { get; set; }
    public float Gain { get; set; }
    public int SignalStrength => 50;
    public bool IsStereo => false;
    public string? RdsStationName => RdsStationNameValue;
    public string? RdsProgramType => RdsProgramTypeValue;
    public string? RdsRadioText => RdsRadioTextValue;

    public event EventHandler<RadioStateChangedEventArgs>? StateChanged;
    public event EventHandler<RadioControlFrequencyChangedEventArgs>? FrequencyChanged;
    public event EventHandler<RadioControlSignalStrengthEventArgs>? SignalStrengthUpdated;

    public Task<bool> StartupAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task ShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SetFrequencyAsync(Frequency frequency, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StepFrequencyUpAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StepFrequencyDownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StartScanAsync(ScanDirection direction, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StopScanAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SetBandAsync(RadioBand band, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SetFrequencyStepAsync(Frequency step, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SetEqualizerModeAsync(RadioEqualizerMode mode, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<bool> GetPowerStateAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task TogglePowerStateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    // Suppress warnings about unused events
    private void RaiseStateChanged() => StateChanged?.Invoke(this, new RadioStateChangedEventArgs("Test", null));
    private void RaiseFreqChanged() => FrequencyChanged?.Invoke(this, new RadioControlFrequencyChangedEventArgs(Frequency.FromMegahertz(101.5), Frequency.FromMegahertz(101.5)));
    private void RaiseSig() => SignalStrengthUpdated?.Invoke(this, new RadioControlSignalStrengthEventArgs(0.5f));
  }
}

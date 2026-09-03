using Radio.Core.Interfaces.Audio;
using Radio.Core.Interfaces.Input;
using Radio.Core.Models.Audio;

namespace Radio.Infrastructure.Tests.Platform.Input;

/// <summary>
/// Ordered record of what the fakes below were asked to do, so a test can assert a <i>sequence</i>
/// — "activate the radio, then set the band, then set the frequency" — rather than only that each
/// call happened. One log is shared by the manager and the radio because the sequence spans both.
/// </summary>
internal sealed class CallLog
{
  public List<string> Entries { get; } = [];

  public void Add(string entry) => Entries.Add(entry);
}

/// <summary>
/// A primary audio source that is also a tuner, which is the shape
/// <see cref="Radio.Infrastructure.Platform.Input.SourceSelectorService"/> tests against:
/// <c>IAudioManager.ActiveSource</c> is an <see cref="IAudioSource"/> and the selector narrows it
/// to <see cref="IRadioControl"/>.
/// </summary>
internal sealed class FakeRadioSource : IAudioSource, IRadioControl
{
  private readonly CallLog? _log;

  public FakeRadioSource(CallLog? log = null)
  {
    _log = log;
  }

  // --- Knobs the tests set ---------------------------------------------------------------------

  /// <summary>
  /// When true, <see cref="SetBandAsync"/> records the call and leaves <see cref="CurrentBand"/>
  /// alone — the RF320's behaviour, where the band selector is a physical switch.
  /// </summary>
  public bool IgnoresBandChanges { get; set; }

  public IReadOnlyList<RadioBand> SupportedBands { get; set; } =
    [RadioBand.FM, RadioBand.AM, RadioBand.SW, RadioBand.WB];

  public RadioBand CurrentBand { get; set; } = RadioBand.FM;
  public Frequency CurrentFrequency { get; set; } = Frequency.FromMegahertz(98.5);

  public List<RadioBand> BandsSet { get; } = [];
  public List<Frequency> FrequenciesSet { get; } = [];

  // --- IAudioSource ----------------------------------------------------------------------------

  public string Id { get; set; } = "fake-radio";
  public string Name { get; set; } = "Fake Tuner";
  public AudioSourceType Type => AudioSourceType.Radio;
  public AudioSourceCategory Category => AudioSourceCategory.Primary;
  public AudioSourceState State { get; set; } = AudioSourceState.Playing;
  public float Volume { get; set; } = 0.5f;

  public object GetSoundComponent() =>
    throw new NotSupportedException("The selector never touches the sound component.");

  public event EventHandler<AudioSourceStateChangedEventArgs>? StateChanged { add { } remove { } }

  public ValueTask DisposeAsync() => ValueTask.CompletedTask;

  // --- IRadioControl ---------------------------------------------------------------------------

  public bool IsRunning => true;
  public bool IsScanning => false;
  public ScanDirection? ScanDirection => null;
  public int ScanStopThreshold => 50;
  public Frequency FrequencyStep => Frequency.FromKilohertz(100);
  public int DeviceVolume { get; set; } = 50;
  public bool IsMuted { get; set; }
  public float SquelchThreshold { get; set; }
  public RadioEqualizerMode EqualizerMode => RadioEqualizerMode.Normal;
  public bool AutoGainEnabled { get; set; }
  public float Gain { get; set; }
  public int SignalStrength => 50;
  public bool IsStereo => false;
  public string? RdsStationName => null;
  public string? RdsProgramType => null;
  public string? RdsRadioText => null;

  event EventHandler<RadioStateChangedEventArgs>? IRadioControl.StateChanged { add { } remove { } }
  public event EventHandler<RadioControlFrequencyChangedEventArgs>? FrequencyChanged { add { } remove { } }
  public event EventHandler<RadioControlSignalStrengthEventArgs>? SignalStrengthUpdated { add { } remove { } }

  public Task<bool> StartupAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
  public Task ShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
  public Task StepFrequencyUpAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
  public Task StepFrequencyDownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
  public Task StartScanAsync(ScanDirection direction, CancellationToken cancellationToken = default) => Task.CompletedTask;
  public Task StopScanAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
  public Task SetFrequencyStepAsync(Frequency step, CancellationToken cancellationToken = default) => Task.CompletedTask;
  public Task SetEqualizerModeAsync(RadioEqualizerMode mode, CancellationToken cancellationToken = default) => Task.CompletedTask;
  public Task<bool> GetPowerStateAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
  public Task TogglePowerStateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

  public Task SetBandAsync(RadioBand band, CancellationToken cancellationToken = default)
  {
    BandsSet.Add(band);
    _log?.Add($"SetBand:{band}");

    if (!IgnoresBandChanges)
    {
      CurrentBand = band;
    }

    return Task.CompletedTask;
  }

  public Task SetFrequencyAsync(Frequency frequency, CancellationToken cancellationToken = default)
  {
    FrequenciesSet.Add(frequency);
    _log?.Add($"SetFrequency:{frequency.Hertz}");
    CurrentFrequency = frequency;
    return Task.CompletedTask;
  }
}

/// <summary>A primary audio source with no tuner behaviour — Bluetooth, phono, files.</summary>
internal sealed class FakePrimarySource : IAudioSource
{
  public FakePrimarySource(AudioSourceType type, string name)
  {
    Type = type;
    Name = name;
  }

  public string Id { get; set; } = Guid.NewGuid().ToString();
  public string Name { get; set; }
  public AudioSourceType Type { get; }
  public AudioSourceCategory Category => AudioSourceCategory.Primary;
  public AudioSourceState State { get; set; } = AudioSourceState.Ready;
  public float Volume { get; set; } = 0.5f;

  public object GetSoundComponent() =>
    throw new NotSupportedException("The selector never touches the sound component.");

  public event EventHandler<AudioSourceStateChangedEventArgs>? StateChanged { add { } remove { } }

  public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// An <see cref="IAudioManager"/> whose source cache and active source are set by the test.
/// <see cref="GetOrCreateSourceAsync"/> is scripted rather than real: it records the call, applies
/// whatever the test staged for that type, and returns it.
/// </summary>
internal sealed class SelectorFakeAudioManager : IAudioManager
{
  private readonly CallLog? _log;

  public SelectorFakeAudioManager(CallLog? log = null)
  {
    _log = log;
  }

  public Dictionary<AudioSourceType, IAudioSource?> Cached { get; } = [];

  /// <summary>What <see cref="GetOrCreateSourceAsync"/> returns, per type. Missing means null.</summary>
  public Dictionary<AudioSourceType, IAudioSource?> Creatable { get; } = [];

  /// <summary>When set, <see cref="GetOrCreateSourceAsync"/> throws it for that type.</summary>
  public Dictionary<AudioSourceType, Exception> CreateThrows { get; } = [];

  public List<AudioSourceType> GetOrCreateCalls { get; } = [];

  public float MasterVolume { get; set; } = 0.5f;
  public bool IsMuted { get; set; }
  public float Balance { get; set; }
  public IAudioSource? ActiveSource { get; set; }

  public IAudioEngine Engine =>
    throw new NotSupportedException("The selector never touches the engine.");

  public float GetSourceGain(AudioSourceType sourceType) => 1f;
  public void SetSourceGain(AudioSourceType sourceType, float gain) { }
  public Dictionary<string, float> GetAllSourceGains() => [];

  public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
  public Task SwitchSourceAsync(IAudioSource source, CancellationToken cancellationToken = default) => Task.CompletedTask;
  public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
  public ValueTask DisposeAsync() => ValueTask.CompletedTask;

  public IAudioSource? GetCachedSource(AudioSourceType sourceType) =>
    Cached.TryGetValue(sourceType, out var source) ? source : null;

  public Task<IAudioSource?> GetOrCreateSourceAsync(
    AudioSourceType sourceType,
    bool switchToSource = true,
    CancellationToken cancellationToken = default)
  {
    GetOrCreateCalls.Add(sourceType);
    _log?.Add($"GetOrCreate:{sourceType}");

    if (CreateThrows.TryGetValue(sourceType, out var ex))
    {
      throw ex;
    }

    var created = Creatable.TryGetValue(sourceType, out var source) ? source : null;
    if (created is not null)
    {
      Cached[sourceType] = created;
      if (switchToSource)
      {
        ActiveSource = created;
      }
    }

    return Task.FromResult(created);
  }
}

/// <summary>Records every published card without coalescing, so assertions see each phase.</summary>
internal sealed class RecordingSelectorSink : IEncoderFeedbackSink
{
  public List<EncoderHudEventArgs> Published { get; } = [];

  public event EventHandler<EncoderHudEventArgs>? Feedback;

  public void Publish(EncoderHudEventArgs update)
  {
    Published.Add(update);
    Feedback?.Invoke(this, update);
  }

  public IReadOnlyList<EncoderHudEventArgs> OfPhase(EncoderHudPhase phase) =>
    Published.Where(p => p.Phase == phase).ToList();
}

/// <summary>
/// An in-memory band memory, so a test can stage a remembered frequency, a band default, or
/// neither.
///
/// <para>
/// The two dictionaries mirror the real service's ladder — remembered first, then the band's
/// default — so a test can tell "restored where I left it" apart from "landed on the default"
/// without reaching into the configuration store.
/// </para>
/// </summary>
internal sealed class FakeBandMemory : IRadioBandMemory
{
  public Dictionary<RadioBand, Frequency> Remembered { get; } = [];

  /// <summary>What the real service would return from configuration when nothing is remembered.</summary>
  public Dictionary<RadioBand, Frequency> Defaults { get; } = [];

  public List<(RadioBand Band, Frequency Frequency)> Writes { get; } = [];

  public Task<Frequency?> GetAsync(RadioBand band, CancellationToken cancellationToken = default)
  {
    if (Remembered.TryGetValue(band, out var remembered))
    {
      return Task.FromResult<Frequency?>(remembered);
    }

    return Task.FromResult(Defaults.TryGetValue(band, out var fallback) ? fallback : (Frequency?)null);
  }

  public Task SetAsync(RadioBand band, Frequency frequency, CancellationToken cancellationToken = default)
  {
    Writes.Add((band, frequency));
    Remembered[band] = frequency;
    return Task.CompletedTask;
  }
}

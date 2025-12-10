using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Fingerprinting;

namespace Radio.Infrastructure.Audio.Sources.Primary;

/// <summary>
/// Raddy RF320 USB Radio audio source.
/// Captures audio from a USB audio input device.
/// Supports automatic track identification via fingerprinting.
/// <para>
/// <b>Hardware Limitations:</b> The RF320 is a Bluetooth-controlled radio with USB audio output.
/// It does not support software-based radio control. All IRadioControl methods are stubs that
/// maintain API compatibility but do not control the actual hardware. Radio tuning must be done
/// manually using the physical device controls.
/// </para>
/// </summary>
public class RadioAudioSource : USBAudioSourceBase, Radio.Core.Interfaces.Audio.IRadioControl
{
  private readonly IOptionsMonitor<DeviceOptions> _deviceOptions;
  private readonly IOptionsMonitor<RadioOptions> _radioOptions;
  private readonly ILogger<RadioAudioSource> _logger;

  // Default state values (cannot be changed programmatically on RF320)
  private Frequency _frequencyStep;
  private int _deviceVolume;

  /// <summary>
  /// Initializes a new instance of the <see cref="RadioAudioSource"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="deviceOptions">The device options configuration.</param>
  /// <param name="radioOptions">The radio options configuration.</param>
  /// <param name="deviceManager">The audio device manager.</param>
  /// <param name="identificationService">Optional fingerprinting service for track identification.</param>
  public RadioAudioSource(
    ILogger<RadioAudioSource> logger,
    IOptionsMonitor<DeviceOptions> deviceOptions,
    IOptionsMonitor<RadioOptions> radioOptions,
    IAudioDeviceManager deviceManager,
    BackgroundIdentificationService? identificationService = null)
    : base(logger, deviceManager, identificationService)
  {
    _deviceOptions = deviceOptions;
    _radioOptions = radioOptions;
    _logger = logger;

    // Initialize from configuration
    var options = _radioOptions.CurrentValue;
    _frequencyStep = Frequency.FromMegahertz(options.DefaultFMStepMHz);
    _deviceVolume = options.DefaultDeviceVolume;
  }

  /// <inheritdoc/>
  public override string Name => "Radio (RF320)";

  /// <inheritdoc/>
  public override AudioSourceType Type => AudioSourceType.Radio;

  /// <summary>
  /// Gets the USB port path for the radio device.
  /// </summary>
  public string USBPort => _deviceOptions.CurrentValue.Radio.USBPort;

  /// <inheritdoc/>
  protected override async Task InitializeAsync(CancellationToken cancellationToken = default)
  {
    var usbPort = _deviceOptions.CurrentValue.Radio.USBPort;

    // Set standard metadata with defaults for Radio source
    SetDefaultMetadata("Radio", "Radio", "Raddy RF320");

    await InitializeUSBCaptureAsync(usbPort, cancellationToken);
  }

  #region IRadioControl Implementation (Stub - RF320 Hardware Limitations)

  // Note: RF320 is a Bluetooth radio with USB audio output. It does not support
  // software-based control. All methods below are stubs for API compatibility.

  /// <inheritdoc/>
  /// <remarks>RF320 does not support software startup control. Use physical power button.</remarks>
  public Task<bool> StartupAsync(CancellationToken cancellationToken = default)
  {
    _logger.LogWarning("RF320 does not support software startup control");
    return Task.FromResult(true); // Assume always "running" if audio is being captured
  }

  /// <inheritdoc/>
  /// <remarks>RF320 does not support software shutdown control. Use physical power button.</remarks>
  public Task ShutdownAsync(CancellationToken cancellationToken = default)
  {
    _logger.LogWarning("RF320 does not support software shutdown control");
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  /// <remarks>RF320 running state is based on USB audio capture status.</remarks>
  public bool IsRunning => State == AudioSourceState.Playing || State == AudioSourceState.Paused;

  /// <inheritdoc/>
  /// <remarks>RF320 does not report current frequency. Returns default FM broadcast center frequency.</remarks>
  public Frequency CurrentFrequency => Frequency.FromMegahertz(98.0); // Default FM center

  /// <inheritdoc/>
  /// <remarks>RF320 does not support software frequency control. Use physical tuning controls.</remarks>
  public Task SetFrequencyAsync(Frequency frequency, CancellationToken cancellationToken = default)
  {
    _logger.LogWarning("RF320 does not support software frequency control. Use physical controls.");
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  /// <remarks>RF320 does not support software frequency stepping. Use physical tuning controls.</remarks>
  public Task StepFrequencyUpAsync(CancellationToken cancellationToken = default)
  {
    _logger.LogWarning("RF320 does not support software frequency control. Use physical controls.");
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  /// <remarks>RF320 does not support software frequency stepping. Use physical tuning controls.</remarks>
  public Task StepFrequencyDownAsync(CancellationToken cancellationToken = default)
  {
    _logger.LogWarning("RF320 does not support software frequency control. Use physical controls.");
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  /// <remarks>RF320 does not support frequency scanning. Use physical controls.</remarks>
  public bool IsScanning => false;

  /// <inheritdoc/>
  /// <remarks>RF320 does not support frequency scanning.</remarks>
  public ScanDirection? ScanDirection => null;

  /// <inheritdoc/>
  /// <remarks>RF320 does not support software-controlled scanning.</remarks>
  public Task StartScanAsync(ScanDirection direction, CancellationToken cancellationToken = default)
  {
    _logger.LogWarning("RF320 does not support software-controlled scanning");
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  /// <remarks>RF320 does not support software-controlled scanning.</remarks>
  public Task StopScanAsync(CancellationToken cancellationToken = default)
  {
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  /// <remarks>RF320 defaults to FM band. Band cannot be changed programmatically.</remarks>
  public RadioBand CurrentBand => RadioBand.FM;

  /// <inheritdoc/>
  /// <remarks>RF320 does not support software band switching. Use physical controls.</remarks>
  public Task SetBandAsync(RadioBand band, CancellationToken cancellationToken = default)
  {
    _logger.LogWarning("RF320 does not support software band control. Use physical controls.");
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  public Frequency FrequencyStep => _frequencyStep;

  /// <inheritdoc/>
  public Task SetFrequencyStepAsync(Frequency step, CancellationToken cancellationToken = default)
  {
    _frequencyStep = step;
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  /// <remarks>
  /// Uses 'new' keyword because both USBAudioSourceBase and IRadioControl define Volume.
  /// IRadioControl requires synchronization with DeviceVolume property.
  /// </remarks>
  public new float Volume
  {
    get => base.Volume;
    set
    {
      base.Volume = value;
      _deviceVolume = (int)Math.Round(value * 100);
    }
  }

  /// <inheritdoc/>
  public int DeviceVolume
  {
    get => _deviceVolume;
    set
    {
      _deviceVolume = Math.Clamp(value, 0, 100);
      base.Volume = _deviceVolume / 100.0f;
    }
  }

  /// <inheritdoc/>
  /// <remarks>RF320 mute control uses audio source volume control.</remarks>
  public bool IsMuted
  {
    get => Volume <= 0.0f;
    set => Volume = value ? 0.0f : 1.0f;
  }

  /// <inheritdoc/>
  /// <remarks>RF320 does not support software squelch control.</remarks>
  public float SquelchThreshold { get; set; } = 0.0f;

  /// <inheritdoc/>
  /// <remarks>RF320 does not support equalizer modes.</remarks>
  public RadioEqualizerMode EqualizerMode => RadioEqualizerMode.Off;

  /// <inheritdoc/>
  /// <remarks>RF320 does not support software-controlled equalizer.</remarks>
  public Task SetEqualizerModeAsync(RadioEqualizerMode mode, CancellationToken cancellationToken = default)
  {
    _logger.LogWarning("RF320 does not support software-controlled equalizer");
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  /// <remarks>RF320 does not support software gain control.</remarks>
  public bool AutoGainEnabled { get; set; } = true;

  /// <inheritdoc/>
  /// <remarks>RF320 does not support software gain control.</remarks>
  public float Gain { get; set; } = 0.0f;

  /// <inheritdoc/>
  /// <remarks>RF320 does not report signal strength.</remarks>
  public int SignalStrength => 0;

  /// <inheritdoc/>
  /// <remarks>RF320 does not report stereo status.</remarks>
  public bool IsStereo => false;

  /// <inheritdoc/>
  /// <remarks>RF320 power state based on audio capture status.</remarks>
  public Task<bool> GetPowerStateAsync(CancellationToken cancellationToken = default)
  {
    return Task.FromResult(IsRunning);
  }

  /// <inheritdoc/>
  /// <remarks>RF320 does not support software power control. Use physical power button.</remarks>
  public Task TogglePowerStateAsync(CancellationToken cancellationToken = default)
  {
    _logger.LogWarning("RF320 does not support software power control. Use physical power button.");
    return Task.CompletedTask;
  }

  /// <summary>
  /// IRadioControl StateChanged event (explicit interface implementation).
  /// RF320 does not emit these events.
  /// </summary>
#pragma warning disable CS0067 // Event is never used (RF320 hardware limitation)
  event EventHandler<RadioStateChangedEventArgs>? Radio.Core.Interfaces.Audio.IRadioControl.StateChanged
  {
    add { }
    remove { }
  }
#pragma warning restore CS0067

  /// <summary>
  /// IRadioControl FrequencyChanged event (explicit interface implementation).
  /// RF320 does not emit frequency change events.
  /// </summary>
#pragma warning disable CS0067 // Event is never used (RF320 hardware limitation)
  event EventHandler<RadioControlFrequencyChangedEventArgs>? Radio.Core.Interfaces.Audio.IRadioControl.FrequencyChanged
  {
    add { }
    remove { }
  }
#pragma warning restore CS0067

  /// <summary>
  /// IRadioControl SignalStrengthUpdated event (explicit interface implementation).
  /// RF320 does not emit signal strength events.
  /// </summary>
#pragma warning disable CS0067 // Event is never used (RF320 hardware limitation)
  event EventHandler<RadioControlSignalStrengthEventArgs>? Radio.Core.Interfaces.Audio.IRadioControl.SignalStrengthUpdated
  {
    add { }
    remove { }
  }
#pragma warning restore CS0067

  #endregion
}

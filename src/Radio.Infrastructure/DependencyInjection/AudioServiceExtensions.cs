using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Interfaces.Bluetooth;
using Radio.Core.Interfaces.External;
using Radio.Core.Interfaces.Input;
using Radio.Infrastructure.Audio;
using Radio.Infrastructure.Audio.Factories;
using Radio.Fingerprinting.Services;
using Radio.Infrastructure.Audio.Fingerprinting;
using Radio.Infrastructure.Audio.Outputs;
using Radio.Infrastructure.Audio.Services;
using Radio.Infrastructure.Audio.SoundFlow;
using Radio.Infrastructure.Audio.Visualization;
using Radio.Infrastructure.Configuration;
using Radio.Configuration.Abstractions;
using Radio.Infrastructure.Bluetooth;
using Radio.Infrastructure.External;
using Radio.Infrastructure.Platform.Input;
using Radio.Metrics;

namespace Radio.Infrastructure.DependencyInjection;

/// <summary>
/// Extension methods for registering audio services.
/// </summary>
public static class AudioServiceExtensions
{
  /// <summary>
  /// Adds the SoundFlow audio engine and related services to the service collection.
  /// </summary>
  /// <param name="services">The service collection.</param>
  /// <param name="configuration">The configuration instance.</param>
  /// <returns>The service collection for chaining.</returns>
  public static IServiceCollection AddSoundFlowAudio(
    this IServiceCollection services,
    IConfiguration configuration)
  {
    // Bind audio engine options
    services.Configure<AudioEngineOptions>(
      configuration.GetSection(AudioEngineOptions.SectionName));

    // Bind audio preferences for startup behavior
    services.Configure<AudioPreferences>(
      configuration.GetSection(AudioPreferences.SectionName));

    // Bind FilePlayer preferences
    services.Configure<FilePlayerPreferences>(
      configuration.GetSection(FilePlayerPreferences.SectionName));

    // Bind Bluetooth options/preferences
    services.Configure<BluetoothOptions>(
      configuration.GetSection(BluetoothOptions.SectionName));
    services.Configure<BluetoothPreferences>(
      configuration.GetSection(BluetoothPreferences.SectionName));

    // Bind Device options (for Vinyl, USB, etc.)
    services.Configure<DeviceOptions>(
      configuration.GetSection(DeviceOptions.SectionName));

    // Bind Generic source preferences
    services.Configure<GenericSourcePreferences>(
      configuration.GetSection(GenericSourcePreferences.SectionName));

    // Bind Radio options
    services.Configure<RadioOptions>(
      configuration.GetSection(RadioOptions.SectionName));

    // Register the master mixer (singleton to maintain state)
    services.AddSingleton<SoundFlowMasterMixer>();
    services.AddSingleton<IMasterMixer>(sp => sp.GetRequiredService<SoundFlowMasterMixer>());

    // Register the device manager (singleton to maintain reservations)
    services.AddSingleton<SoundFlowDeviceManager>();
    services.AddSingleton<IAudioDeviceManager>(sp => sp.GetRequiredService<SoundFlowDeviceManager>());

    // Register the audio engine (singleton for single audio context)
    // Note: IMetricsCollector and IVisualizerService are optional — use explicit factory
    // since MS DI's default activation can skip optional constructor parameters.
    services.AddSingleton<SoundFlowAudioEngine>(sp =>
    {
      var logger = sp.GetRequiredService<ILogger<SoundFlowAudioEngine>>();
      var options = sp.GetRequiredService<IOptions<AudioEngineOptions>>();
      var masterMixer = sp.GetRequiredService<SoundFlowMasterMixer>();
      var deviceManager = sp.GetRequiredService<SoundFlowDeviceManager>();
      var metricsCollector = sp.GetService<IMetricsCollector>();
      var visualizerService = sp.GetService<IVisualizerService>();
      return new SoundFlowAudioEngine(logger, options, masterMixer, deviceManager, metricsCollector, visualizerService);
    });
    services.AddSingleton<IAudioEngine>(sp => sp.GetRequiredService<SoundFlowAudioEngine>());

    // Register the playback service (singleton for managing SoundFlow players)
    // Visualization is handled by the MasterMixer-level tap in SoundFlowAudioEngine
    services.AddSingleton<SoundFlowPlaybackService>(sp =>
    {
      var logger = sp.GetRequiredService<ILogger<SoundFlowPlaybackService>>();
      var audioEngine = sp.GetRequiredService<SoundFlowAudioEngine>();
      return new SoundFlowPlaybackService(logger, audioEngine);
    });

    // Bind audio options for ducking configuration
    services.Configure<AudioOptions>(
      configuration.GetSection(AudioOptions.SectionName));

    // Register the ducking service (singleton to maintain state)
    services.AddSingleton<DuckingService>();
    services.AddSingleton<IDuckingService>(sp => sp.GetRequiredService<DuckingService>());

    // Register device options resolver (reads config store, falls back to IOptionsMonitor)
    services.AddSingleton<DeviceOptionsResolver>();

    // Register radio factory (singleton for device management)
    services.AddSingleton<RadioFactory>();
    services.AddSingleton<IRadioFactory>(sp => sp.GetRequiredService<RadioFactory>());

    // Register BlueZ mgmt monitor (singleton + hosted service for disconnect reason detection)
    services.AddSingleton<Platform.Bluetooth.BluetoothMgmtMonitor>();
    services.AddHostedService(sp => sp.GetRequiredService<Platform.Bluetooth.BluetoothMgmtMonitor>());

#if !WINDOWS_TARGET
    // On Linux, register LinuxBluetoothService as the concrete type so the
    // BluetoothCaptureWatchdog (FM-BT-3) can resolve it to read the native
    // PipeWire OnProcess timestamp. The IBluetoothService below resolves the
    // same instance via GetService<LinuxBluetoothService>() to avoid creating
    // two competing instances of the BT service.
    if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
          System.Runtime.InteropServices.OSPlatform.Linux))
    {
      services.AddSingleton<Platform.Bluetooth.LinuxBluetoothService>(sp =>
      {
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        var options = sp.GetRequiredService<IOptions<BluetoothOptions>>();
        var deviceManager = sp.GetRequiredService<IAudioDeviceManager>() as SoundFlowDeviceManager;
        var metricsCollector = sp.GetService<IMetricsCollector>();
        var playbackService = sp.GetService<Audio.SoundFlow.SoundFlowPlaybackService>();
        var mgmtMonitor = sp.GetService<Platform.Bluetooth.BluetoothMgmtMonitor>();
        return new Platform.Bluetooth.LinuxBluetoothService(
          loggerFactory.CreateLogger<Platform.Bluetooth.LinuxBluetoothService>(),
          options, deviceManager, metricsCollector, playbackService, mgmtMonitor);
      });
    }
#endif

    // Register Bluetooth service. On Linux + enabled, reuse the LinuxBluetoothService
    // concrete singleton registered above; otherwise fall back to the factory.
    services.AddSingleton<IBluetoothService>(sp =>
    {
#if !WINDOWS_TARGET
      var linuxService = sp.GetService<Platform.Bluetooth.LinuxBluetoothService>();
      if (linuxService != null)
      {
        return linuxService;
      }
#endif
      var logger = sp.GetRequiredService<ILoggerFactory>();
      var options = sp.GetRequiredService<IOptions<BluetoothOptions>>();
      var deviceManager = sp.GetRequiredService<IAudioDeviceManager>() as SoundFlowDeviceManager;
      var metricsCollector = sp.GetService<IMetricsCollector>();
      return Platform.Bluetooth.BluetoothServiceFactory.Create(sp, options, logger, deviceManager, metricsCollector);
    });

    // FM-BT-3 watchdog snapshot source. On Linux this resolves to the
    // LinuxBluetoothService singleton; on Windows / Mock / BT-disabled, the
    // null fallback keeps the watchdog idle.
    services.AddSingleton<ICaptureStreamSnapshotSource>(sp =>
    {
#if !WINDOWS_TARGET
      var linuxService = sp.GetService<Platform.Bluetooth.LinuxBluetoothService>();
      if (linuxService != null)
      {
        return linuxService;
      }
#endif
      return NullCaptureStreamSnapshotSource.Instance;
    });

    // FM-BT-3 watchdog. AddSingleton + AddHostedService(factory) so the
    // concrete type is resolvable from DI (per MEMORY "DI / Hosted Service
    // Gotchas").
    services.AddSingleton<BluetoothCaptureWatchdog>();
    services.AddHostedService(sp => sp.GetRequiredService<BluetoothCaptureWatchdog>());

    // Register album art cache service (singleton for disk-backed image cache)
    services.AddSingleton<AlbumArtCacheService>();
    services.AddSingleton<IAlbumArtCacheService>(sp => sp.GetRequiredService<AlbumArtCacheService>());

    // Register audio source factory (encapsulates all source-creation dependencies)
    services.AddSingleton<AudioSourceFactory>();
    services.AddSingleton<IAudioSourceFactory>(sp => sp.GetRequiredService<AudioSourceFactory>());

    // Register audio preference persistence (debounced volume/source saves)
    services.AddSingleton<AudioPreferencePersistence>(sp => new AudioPreferencePersistence(
      sp.GetRequiredService<ILogger<AudioPreferencePersistence>>(),
      sp.GetRequiredService<IAudioEngine>(),
      sp.GetRequiredService<IOptionsMonitor<AudioPreferences>>(),
      sp.GetService<Radio.Configuration.Abstractions.IConfigurationManager>()));

    // Register audio manager (singleton to maintain state)
    // Use explicit factory to ensure all optional services are resolved.
    services.AddSingleton<AudioManager>(sp => new AudioManager(
      sp.GetRequiredService<ILogger<AudioManager>>(),
      sp.GetRequiredService<IAudioEngine>(),
      sp.GetRequiredService<IAudioSourceFactory>(),
      sp.GetService<BackgroundIdentificationService>(),
      sp.GetRequiredService<AudioPreferencePersistence>(),
      sp.GetRequiredService<PlayHistoryTracker>(),
      sp.GetRequiredService<SoundFlowPlaybackService>(),
      sp.GetRequiredService<IDuckingService>()));
    services.AddSingleton<IAudioManager>(sp => sp.GetRequiredService<AudioManager>());
    services.AddSingleton<IAudioSourceManager>(sp => sp.GetRequiredService<AudioManager>());
    services.AddSingleton<IAudioMixerControl>(sp => sp.GetRequiredService<AudioManager>());

    // Active-source accessor for audio sources (Func<> defers IAudioManager
    // resolution, breaking the circular dependency — same pattern as PlayHistoryTracker).
    // Sources use this to ignore TrackIdentified events broadcast while a
    // different source is active.
    services.AddSingleton<Func<IAudioSource?>>(sp => () => sp.GetRequiredService<IAudioManager>().ActiveSource);

    // Register play history tracker (Func<> defers IAudioManager resolution, breaking circular dependency)
    services.AddSingleton<PlayHistoryTracker>(sp => new PlayHistoryTracker(
      sp.GetRequiredService<ILogger<PlayHistoryTracker>>(),
      sp.GetRequiredService<IServiceScopeFactory>(),
      () => sp.GetRequiredService<IAudioManager>().ActiveSource,
      sp.GetRequiredService<IBluetoothService>(),
      sp.GetService<BackgroundIdentificationService>(),
      sp.GetService<IMetricsCollector>()));

    // Register Bluetooth auto-switch service (Func<> defers IAudioManager resolution)
    services.AddSingleton<BluetoothAutoSwitchService>(sp => new BluetoothAutoSwitchService(
      sp.GetRequiredService<ILogger<BluetoothAutoSwitchService>>(),
      sp.GetRequiredService<IBluetoothService>(),
      sp.GetRequiredService<IOptionsMonitor<BluetoothOptions>>(),
      () => sp.GetRequiredService<IAudioManager>(),
      sp.GetService<IMetricsCollector>()));

    // Register event audio source services
    services.AddEventAudioSources(configuration);

    // Register audio output services
    services.AddAudioOutputs(configuration);

    // Register visualization services
    services.AddVisualization(configuration);

    // Register announcement service (shared by phone calls + notifications)
    services.AddSingleton<AnnouncementService>();
    services.AddSingleton<IAnnouncementService>(sp => sp.GetRequiredService<AnnouncementService>());

    // Register rotary encoder services
    services.AddRotaryEncoders(configuration);

    // Register phone call integration services
    services.AddPhoneIntegration(configuration);

    return services;
  }

  /// <summary>
  /// Adds the SoundFlow audio engine with custom options.
  /// </summary>
  /// <param name="services">The service collection.</param>
  /// <param name="configureOptions">Action to configure audio options.</param>
  /// <returns>The service collection for chaining.</returns>
  public static IServiceCollection AddSoundFlowAudio(
    this IServiceCollection services,
    Action<AudioEngineOptions> configureOptions)
  {
    // Configure options from action
    services.Configure(configureOptions);

    // Register default AudioOutputOptions so SoundFlowDeviceManager can resolve
    // IOptionsMonitor<AudioOutputOptions> (device display filtering/friendly names, runtime refresh).
    services.Configure<AudioOutputOptions>(_ => { });

    // Register the master mixer (singleton to maintain state)
    services.AddSingleton<SoundFlowMasterMixer>();
    services.AddSingleton<IMasterMixer>(sp => sp.GetRequiredService<SoundFlowMasterMixer>());

    // Register the device manager (singleton to maintain reservations)
    services.AddSingleton<SoundFlowDeviceManager>();
    services.AddSingleton<IAudioDeviceManager>(sp => sp.GetRequiredService<SoundFlowDeviceManager>());

    // Register the audio engine (singleton for single audio context)
    services.AddSingleton<SoundFlowAudioEngine>();
    services.AddSingleton<IAudioEngine>(sp => sp.GetRequiredService<SoundFlowAudioEngine>());

    return services;
  }

  /// <summary>
  /// Adds event audio source services (TTS, Audio File Events).
  /// </summary>
  /// <param name="services">The service collection.</param>
  /// <param name="configuration">The configuration instance.</param>
  /// <returns>The service collection for chaining.</returns>
  public static IServiceCollection AddEventAudioSources(
    this IServiceCollection services,
    IConfiguration configuration)
  {
    // Bind TTS options
    services.Configure<TTSOptions>(
      configuration.GetSection(TTSOptions.SectionName));

    // Bind TTS secrets (from secrets store) and resolve ${secret:...} tags
    services.Configure<TTSSecrets>(
      configuration.GetSection(TTSSecrets.SectionName));
    services.AddSecretResolution<TTSSecrets>();

    // Bind TTS preferences
    services.Configure<TTSPreferences>(
      configuration.GetSection(TTSPreferences.SectionName));

    // Bind file player options (for audio file events and file browser)
    services.Configure<FilePlayerOptions>(
      configuration.GetSection(FilePlayerOptions.SectionName));

    // Register File Browser service as scoped (IAudioFileRepository is scoped)
    services.AddScoped<FileBrowser>(sp =>
    {
      var logger = sp.GetRequiredService<ILogger<FileBrowser>>();
      var options = sp.GetRequiredService<IOptionsMonitor<FilePlayerOptions>>();
      var metricsCollector = sp.GetService<IMetricsCollector>();
      var audioFileRepository = sp.GetService<IAudioFileRepository>();
      var rootDir = configuration["RootDir"] ?? Directory.GetCurrentDirectory();
      return new FileBrowser(logger, options, rootDir, metricsCollector, audioFileRepository);
    });
    services.AddScoped<IFileBrowser>(sp => sp.GetRequiredService<FileBrowser>());

    // Register TTS factory
    services.AddSingleton<TTSFactory>();
    services.AddSingleton<ITTSFactory>(sp => sp.GetRequiredService<TTSFactory>());

    // Register audio file event source factory
    services.AddSingleton<AudioFileEventSourceFactory>();

    return services;
  }

  /// <summary>
  /// Adds audio output services (Local, Google Cast, HTTP Stream).
  /// </summary>
  /// <param name="services">The service collection.</param>
  /// <param name="configuration">The configuration instance.</param>
  /// <returns>The service collection for chaining.</returns>
  public static IServiceCollection AddAudioOutputs(
    this IServiceCollection services,
    IConfiguration configuration)
  {
    // Bind audio output options
    services.Configure<AudioOutputOptions>(
      configuration.GetSection(AudioOutputOptions.SectionName));

    // Register Local Audio Output (singleton - primary output)
    services.AddSingleton<LocalAudioOutput>();
    services.AddSingleton<IAudioOutput>(sp => sp.GetRequiredService<LocalAudioOutput>());

    // Register Cast device cache repository (singleton - shares FingerprintDbContext)
    services.AddSingleton<CastDeviceCacheRepository>();

    // Register Google Cast Output (singleton - optional external output)
    services.AddSingleton<GoogleCastOutput>();

    // Register HTTP Stream Output (singleton - provides stream URL for Chromecast)
    services.AddSingleton<HttpStreamOutput>();

    return services;
  }

  /// <summary>
  /// Adds audio visualization services (Spectrum, Level Meter, Waveform).
  /// </summary>
  /// <param name="services">The service collection.</param>
  /// <param name="configuration">The configuration instance.</param>
  /// <returns>The service collection for chaining.</returns>
  public static IServiceCollection AddVisualization(
    this IServiceCollection services,
    IConfiguration configuration)
  {
    // Bind visualizer options
    services.Configure<VisualizerOptions>(
      configuration.GetSection(VisualizerOptions.SectionName));

    // Register Visualizer Service (singleton to maintain state)
    services.AddSingleton<VisualizerService>();
    services.AddSingleton<IVisualizerService>(sp => sp.GetRequiredService<VisualizerService>());

    return services;
  }

  /// <summary>
  /// Adds rotary encoder hardware input services (HID reader + action router).
  /// </summary>
  public static IServiceCollection AddRotaryEncoders(
    this IServiceCollection services,
    IConfiguration configuration)
  {
    // Bind encoder options
    services.Configure<RotaryEncoderOptions>(
      configuration.GetSection(RotaryEncoderOptions.SectionName));

    // ENC-8. The designed configuration, with the owner's direction overrides layered on. Singleton
    // because it caches the overrides it read from the store; IConfigurationManager is optional
    // because tests and trimmed hosts run without a store, and the designed directions are the safe
    // fallback.
    services.AddSingleton<RotaryEncoderDesignedConfig>(sp => new RotaryEncoderDesignedConfig(
      sp.GetRequiredService<ILogger<RotaryEncoderDesignedConfig>>(),
      sp.GetService<Radio.Configuration.Abstractions.IConfigurationManager>()));

    // Register HID encoder service. Built by an explicit factory rather than by the container's
    // constructor selection, because TimeProvider is deliberately unregistered in production and
    // the constructor default (TimeProvider.System) is what should apply there.
    services.AddSingleton<HidRotaryEncoderService>(sp => new HidRotaryEncoderService(
      sp.GetRequiredService<ILogger<HidRotaryEncoderService>>(),
      sp.GetRequiredService<IOptionsMonitor<RotaryEncoderOptions>>(),
      sp.GetRequiredService<RotaryEncoderDesignedConfig>(),
      sp.GetService<TimeProvider>()));
    services.AddSingleton<IRotaryEncoderService>(sp => sp.GetRequiredService<HidRotaryEncoderService>());

    // Second facet of the SAME instance, not a second service: provisioning needs the live HidStream
    // that only the reader owns. Registered separately so the input interface is not widened with
    // owner-initiated concerns.
    services.AddSingleton<IRotaryEncoderProvisioning>(sp => sp.GetRequiredService<HidRotaryEncoderService>());

    // Visualization mode. The encoder ENC-7 removed was this service's ONLY writer: nothing in
    // src/ calls CycleMode or ToggleEnabled any more, so ModeChanged cannot fire and
    // AudioStateUpdateService.OnVisualizationModeChanged - and the VisualizationModeChanged SignalR
    // broadcast it makes - are unreachable.
    //
    // The registration stays for two reasons: AudioStateUpdateService still resolves this type and
    // subscribes to ModeChanged, and removing the now-dead broadcast chain is ENC-9's work rather
    // than this row's. It is recorded in design/FUTURE-WORK.md so it is not rediscovered.
    //
    // ⚠ The CAPABILITY is unaffected. Home's six-segment picker changes the mode through
    // VisualizerPanel's own state and its saved preference and never went through this service, so
    // what was lost is the encoder input that was deliberately removed - not the ability to choose
    // a mode. The System Config "Visualizer" tab is FFT size / smoothing / peak-hold; it has no
    // mode control at all.
    services.AddSingleton<VisualizationModeService>();

    // HUD feedback channel. Singleton because the coalescer is per-encoder state that must outlive
    // any single event, and because AudioStateUpdateService subscribes to it once for the process.
    services.AddSingleton<EncoderFeedbackService>();
    services.AddSingleton<IEncoderFeedbackSink>(sp => sp.GetRequiredService<EncoderFeedbackService>());

    // ENC-5. Singleton: one physical knob, one preview state, and the router that drives it is a
    // singleton.
    //
    // Built by a factory rather than by constructor injection so the two Func<> arguments defer
    // their resolution, exactly as the router defers IAudioManager. That is what keeps
    // RotaryEncoderRegistrationTests' deliberately minimal provider - AddLogging plus
    // AddRotaryEncoders and nothing else - able to resolve the router without building the audio
    // graph or the configuration store IRadioBandMemory reads through.
    services.AddSingleton<SourceSelectorService>(sp => new SourceSelectorService(
      sp.GetRequiredService<ILogger<SourceSelectorService>>(),
      () => sp.GetRequiredService<IAudioManager>(),
      () => sp.GetRequiredService<IRadioBandMemory>(),
      sp.GetRequiredService<IEncoderFeedbackSink>(),
      // GetService, not GetRequiredService: nothing registers TimeProvider in production and the
      // constructor default is TimeProvider.System, as with the router below.
      sp.GetService<TimeProvider>()));

    // ENC-7. Singleton for the same reason SourceSelectorService is, and built by a factory for the
    // same reason.
    //
    // IRadioPresetService is registered SCOPED (FingerprintingServiceExtensions), and this service
    // is a singleton driven by the HID read loop, which has no request scope. A singleton may not
    // capture a scoped service - the container either refuses the injection or satisfies it once
    // out of a scope that then outlives its use - so this takes IServiceScopeFactory, which the
    // container registers itself and so needs no deferral, and opens a scope per operation.
    //
    // That buys lifetime legality and nothing else. FingerprintDbContext, which the preset
    // repository reads through, is registered SINGLETON and hands every caller the same
    // SqliteConnection, so a repository built in a fresh scope still works over the same connection
    // as every HTTP request. No isolation is claimed here; see design/FUTURE-WORK.md.
    services.AddSingleton<PresetSelectorService>(sp => new PresetSelectorService(
      sp.GetRequiredService<ILogger<PresetSelectorService>>(),
      sp.GetRequiredService<IServiceScopeFactory>(),
      () => sp.GetRequiredService<IAudioManager>(),
      () => sp.GetRequiredService<IRadioBandMemory>(),
      sp.GetRequiredService<IEncoderFeedbackSink>(),
      sp.GetService<TimeProvider>()));

    // Register action router (Func<> defers IAudioManager resolution)
    // ISleepService is registered in Radio.API — optional here via GetService
    services.AddSingleton<RotaryEncoderActionRouter>(sp => new RotaryEncoderActionRouter(
      sp.GetRequiredService<ILogger<RotaryEncoderActionRouter>>(),
      sp.GetRequiredService<IRotaryEncoderService>(),
      () => sp.GetRequiredService<IAudioManager>(),
      sp.GetRequiredService<IOptionsMonitor<RotaryEncoderOptions>>(),
      sleepService: sp.GetService<ISleepService>(),
      hud: sp.GetRequiredService<IEncoderFeedbackSink>(),
      sourceSelector: sp.GetRequiredService<SourceSelectorService>(),
      presetSelector: sp.GetRequiredService<PresetSelectorService>(),
      // GetService, not GetRequiredService: nothing registers TimeProvider in production, and the
      // constructor default is TimeProvider.System. Tests inject a fake clock directly instead.
      timeProvider: sp.GetService<TimeProvider>()));

    return services;
  }

  /// <summary>
  /// Adds phone call integration services (SignalR client + contact lookup).
  /// </summary>
  public static IServiceCollection AddPhoneIntegration(
    this IServiceCollection services,
    IConfiguration configuration)
  {
    // Bind phone integration options
    services.Configure<PhoneIntegrationOptions>(
      configuration.GetSection(PhoneIntegrationOptions.SectionName));

    // Register SignalR phone call client
    services.AddSingleton<PhoneCallClient>();
    services.AddSingleton<IPhoneIntegrationService>(sp => sp.GetRequiredService<PhoneCallClient>());

    // Register contact lookup service with HttpClient
    services.AddHttpClient<PhoneContactLookupService>();

    // PBAP contact sync
    services.Configure<PbapOptions>(configuration.GetSection(PbapOptions.SectionName));

    var dbRootPath = configuration.GetValue<string>("Database:RootPath") ?? "./data";
    var pbapDbPath = Path.Combine(dbRootPath, "pbap-contacts.db");
    services.AddSingleton<IPbapContactRepository>(sp =>
    {
      var repo = new PbapContactRepository($"Data Source={pbapDbPath}");
      repo.InitializeAsync().GetAwaiter().GetResult();
      return repo;
    });

    services.AddSingleton<PbapSyncService>();
    services.AddSingleton<IPbapSyncService>(sp => sp.GetRequiredService<PbapSyncService>());
    services.AddHostedService(sp => sp.GetRequiredService<PbapSyncService>());

    return services;
  }

  // NOTE: RadioProtocol.Core has been removed and replaced by RTLSDRCore integration.
  // The AddRadioHardware method is no longer needed as RTLSDRCore provides equivalent functionality.
  // See TASK_4_2_SUMMARY.md for details on the RTLSDRCore integration.
}

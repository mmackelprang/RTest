using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
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

    // Register Bluetooth service factory + service
    services.AddSingleton<IBluetoothService>(sp =>
    {
      var logger = sp.GetRequiredService<ILoggerFactory>();
      var options = sp.GetRequiredService<IOptions<BluetoothOptions>>();
      var deviceManager = sp.GetRequiredService<IAudioDeviceManager>() as SoundFlowDeviceManager;
      var metricsCollector = sp.GetService<IMetricsCollector>();
      return Platform.Bluetooth.BluetoothServiceFactory.Create(sp, options, logger, deviceManager, metricsCollector);
    });

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
      () => sp.GetRequiredService<IAudioManager>()));

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

    // Register HID encoder service
    services.AddSingleton<HidRotaryEncoderService>();
    services.AddSingleton<IRotaryEncoderService>(sp => sp.GetRequiredService<HidRotaryEncoderService>());

    // Register visualization mode service (tracks current viz mode for encoder + SignalR)
    services.AddSingleton<VisualizationModeService>();

    // Register action router (Func<> defers IAudioManager resolution)
    // ISleepService is registered in Radio.API — optional here via GetService
    services.AddSingleton<RotaryEncoderActionRouter>(sp => new RotaryEncoderActionRouter(
      sp.GetRequiredService<ILogger<RotaryEncoderActionRouter>>(),
      sp.GetRequiredService<IRotaryEncoderService>(),
      () => sp.GetRequiredService<IAudioManager>(),
      sp.GetRequiredService<VisualizationModeService>(),
      sp.GetRequiredService<IOptionsMonitor<RotaryEncoderOptions>>(),
      sleepService: sp.GetService<ISleepService>()));

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

    return services;
  }

  // NOTE: RadioProtocol.Core has been removed and replaced by RTLSDRCore integration.
  // The AddRadioHardware method is no longer needed as RTLSDRCore provides equivalent functionality.
  // See TASK_4_2_SUMMARY.md for details on the RTLSDRCore integration.
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Outputs;
using Radio.Infrastructure.Audio.Services;
using Radio.Infrastructure.Audio.SoundFlow;
using Radio.Infrastructure.Audio.Visualization;
using RadioProtocol.Core;
using RadioProtocol.Core.Logging;

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

    // Register the master mixer (singleton to maintain state)
    services.AddSingleton<SoundFlowMasterMixer>();
    services.AddSingleton<IMasterMixer>(sp => sp.GetRequiredService<SoundFlowMasterMixer>());

    // Register the device manager (singleton to maintain reservations)
    services.AddSingleton<SoundFlowDeviceManager>();
    services.AddSingleton<IAudioDeviceManager>(sp => sp.GetRequiredService<SoundFlowDeviceManager>());

    // Register the audio engine (singleton for single audio context)
    services.AddSingleton<SoundFlowAudioEngine>();
    services.AddSingleton<IAudioEngine>(sp => sp.GetRequiredService<SoundFlowAudioEngine>());

    // Bind audio options for ducking configuration
    services.Configure<AudioOptions>(
      configuration.GetSection(AudioOptions.SectionName));

    // Register the ducking service (singleton to maintain state)
    services.AddSingleton<DuckingService>();
    services.AddSingleton<IDuckingService>(sp => sp.GetRequiredService<DuckingService>());

    // Register event audio source services
    services.AddEventAudioSources(configuration);

    // Register audio output services
    services.AddAudioOutputs(configuration);

    // Register visualization services
    services.AddVisualization(configuration);

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

    // Bind TTS secrets (from secrets store)
    services.Configure<TTSSecrets>(
      configuration.GetSection(TTSSecrets.SectionName));

    // Bind TTS preferences
    services.Configure<TTSPreferences>(
      configuration.GetSection(TTSPreferences.SectionName));

    // Bind file player options (for audio file events and file browser)
    services.Configure<FilePlayerOptions>(
      configuration.GetSection(FilePlayerOptions.SectionName));

    // Register File Browser service
    services.AddSingleton<FileBrowser>(sp =>
    {
      var logger = sp.GetRequiredService<ILogger<FileBrowser>>();
      var options = sp.GetRequiredService<IOptionsMonitor<FilePlayerOptions>>();
      var rootDir = configuration["RootDir"] ?? Directory.GetCurrentDirectory();
      return new FileBrowser(logger, options, rootDir);
    });
    services.AddSingleton<IFileBrowser>(sp => sp.GetRequiredService<FileBrowser>());

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

  public static IServiceCollection AddRadioHardware(this IServiceCollection services, IConfiguration config)
{
    // 1. Register the Logger Wrapper
    // This bridges RadioProtocol's IRadioLogger to your application's Serilog ILogger
    services.AddSingleton<IRadioLogger, RadioLogger>();

    // 2. Register the Manager
    services.AddSingleton<IRadioManager, RadioManager>();

    // 3. Register the Factory (if you need custom configuration)
    services.AddSingleton<RadioManagerBuilder>();

    return services;
}
}

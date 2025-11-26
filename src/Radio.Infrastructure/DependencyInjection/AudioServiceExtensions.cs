using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.SoundFlow;

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
}

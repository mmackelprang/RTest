using Microsoft.Extensions.DependencyInjection;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Services;

namespace Radio.Infrastructure.DependencyInjection;

/// <summary>
/// Registers attended event playback (ADR-029 D1).
/// </summary>
/// <remarks>
/// A standalone extension, following AddGvMedia and AddRadioWeather rather than being folded into
/// AddSoundFlowAudio — for the reason GvMediaServiceExtensions' remarks give, plus one specific to
/// this service: EventPlaybackService is the seam a controller injects, and burying its
/// registration inside a method that also wires audio hardware is how a missing registration
/// becomes a service that will not start on an appliance in a cabinet.
///
/// ⚠ It DEPENDS on AddSoundFlowAudio having been called — ITTSFactory, IDuckingService and
/// AudioFileEventSourceFactory all come from there — and on AddGvMedia for GvMediaClient.
/// Registration order in an IServiceCollection does not matter (resolution is lazy), so this is a
/// dependency on the calls happening, not on their sequence.
/// </remarks>
public static class EventPlaybackServiceExtensions
{
  /// <summary>Registers <see cref="EventPlaybackService"/> as the one attended-playback seam.</summary>
  /// <param name="services">The service collection.</param>
  /// <returns>The same service collection, for chaining.</returns>
  public static IServiceCollection AddEventPlayback(this IServiceCollection services)
  {
    // Singleton because the state is global: one audio engine, one set of speakers, one in-flight
    // attended playback (ADR-029 D6 §8.1). Registered concretely and then aliased, following the
    // AddSingleton<IDuckingService>(sp => sp.GetRequiredService<DuckingService>()) pattern, so both
    // resolve to ONE instance — two would be two Current properties and two single slots.
    services.AddSingleton<EventPlaybackService>();
    services.AddSingleton<IEventPlaybackService>(sp => sp.GetRequiredService<EventPlaybackService>());

    return services;
  }
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Core.Configuration;
using Radio.Infrastructure.Audio.Services;
using Radio.Infrastructure.Audio.Sources.Events;
using Radio.Infrastructure.DependencyInjection;
using Radio.Infrastructure.Tests.External;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Services;

/// <summary>
/// The notification sounds come from <c>NotificationSounds:Directory</c>, not the music root. On the
/// box the music root is a NAS automount, so looking there for sounds cost 5 s per call while the
/// NAS's name would not resolve, and every call to the sounds endpoint paid it.
/// </summary>
public sealed class AudioFileEventSourceFactorySoundsTests : IDisposable
{
  private readonly string _root = Directory.CreateTempSubdirectory("sounds-tests-").FullName;
  private readonly string _music;
  private readonly string _sounds;

  public AudioFileEventSourceFactorySoundsTests()
  {
    _music = Directory.CreateDirectory(Path.Combine(_root, "music")).FullName;
    _sounds = Directory.CreateDirectory(Path.Combine(_root, "sounds")).FullName;
    File.WriteAllText(Path.Combine(_music, "song.mp3"), "x");
    Directory.CreateDirectory(Path.Combine(_music, "Artist"));
    File.WriteAllText(Path.Combine(_sounds, "chime.wav"), "x");
    Directory.CreateDirectory(Path.Combine(_sounds, "alarm"));
  }

  public void Dispose() => Directory.Delete(_root, recursive: true);

  private AudioFileEventSourceFactory Factory(NotificationSoundsOptions? sounds) =>
    new(
      NullLogger<AudioFileEventSourceFactory>.Instance,
      NullLogger<AudioFileEventSource>.Instance,
      new StaticOptionsMonitor<FilePlayerOptions>(new FilePlayerOptions { RootDirectory = _music }),
      notificationSounds: sounds is null ? null : new StaticOptionsMonitor<NotificationSoundsOptions>(sounds));

  [Fact]
  public void GetAvailableNotificationSounds_ReadsTheSoundsDirectory_NotTheMusicRoot()
  {
    AudioFileEventSourceFactory factory = Factory(new NotificationSoundsOptions { Directory = _sounds });

    IReadOnlyList<string> files = factory.GetAvailableNotificationSounds();

    Assert.Equal([Path.Combine(_sounds, "chime.wav")], files);
  }

  [Fact]
  public void GetSubdirectories_ReadsTheSoundsDirectory_NotTheMusicRoot()
  {
    AudioFileEventSourceFactory factory = Factory(new NotificationSoundsOptions { Directory = _sounds });

    Assert.Equal(["alarm"], factory.GetSubdirectories());
  }

  [Fact]
  public void AMissingSoundsDirectory_IsAnEmptyList_AndNeverFallsBackToTheMusicRoot()
  {
    AudioFileEventSourceFactory factory = Factory(
      new NotificationSoundsOptions { Directory = Path.Combine(_root, "absent") });

    Assert.Empty(factory.GetAvailableNotificationSounds());
    Assert.Empty(factory.GetSubdirectories());
  }

  [Fact]
  public void WithNoOptionsSupplied_TheMusicRootIsStillNotUsed()
  {
    IReadOnlyList<string> files = Factory(sounds: null).GetAvailableNotificationSounds();

    Assert.DoesNotContain(files, file => file.StartsWith(_music, StringComparison.Ordinal));
  }

  // The original bug was a configuration key nothing read, so this goes through real binding.
  [Fact]
  public void TheConfigurationKey_ReachesTheFactory()
  {
    IConfiguration configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        ["NotificationSounds:Directory"] = _sounds,
        ["FilePlayer:RootDirectory"] = _music
      })
      .Build();
    ServiceCollection services = new();
    services.AddLogging();
    services.AddEventAudioSources(configuration);
    using ServiceProvider provider = services.BuildServiceProvider();

    AudioFileEventSourceFactory factory = provider.GetRequiredService<AudioFileEventSourceFactory>();

    Assert.Equal([Path.Combine(_sounds, "chime.wav")], factory.GetAvailableNotificationSounds());
  }
}

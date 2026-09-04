using Microsoft.Extensions.Logging.Abstractions;
using Radio.Core.Configuration;
using Radio.Infrastructure.Audio.Services;
using Radio.Infrastructure.Audio.Sources.Events;
using Radio.Infrastructure.Tests.External;

namespace Radio.Infrastructure.Tests.Audio.Events;

/// <summary>
/// Pins the two properties CreateFromAbsolutePathAsync exists for: it does not re-root, and it
/// honours a caller-supplied duration. Both are ADR-029 §4.1/§5.2 requirements for the RemoteMedia
/// arm, and both are things CreateFromFileAsync does the other way round.
/// </summary>
public class AudioFileEventSourceFactoryAbsolutePathTests : IDisposable
{
  private readonly string _dir =
    Path.Combine(Path.GetTempPath(), "radio-abs-" + Guid.NewGuid().ToString("N"));

  public AudioFileEventSourceFactoryAbsolutePathTests() => Directory.CreateDirectory(_dir);

  public void Dispose()
  {
    try
    {
      Directory.Delete(_dir, recursive: true);
    }
    catch (Exception)
    {
      // Best effort — a leftover temp directory must never fail a test during teardown.
    }
    GC.SuppressFinalize(this);
  }

  private static AudioFileEventSourceFactory CreateFactory(string rootDirectory) =>
    new(
      NullLogger<AudioFileEventSourceFactory>.Instance,
      NullLogger<AudioFileEventSource>.Instance,
      new StaticOptionsMonitor<FilePlayerOptions>(
        new FilePlayerOptions { RootDirectory = rootDirectory }));

  private string WriteFile(string name, int bytes)
  {
    var path = Path.Combine(_dir, name);
    File.WriteAllBytes(path, new byte[bytes]);
    return path;
  }

  [Fact]
  public async Task ItDoesNotResolveAgainstFilePlayerRootDirectory()
  {
    // The root points somewhere that does not exist. CreateFromFileAsync would combine against it;
    // this must not, so an absolute path under a different tree still resolves.
    var factory = CreateFactory(Path.Combine(_dir, "a-root-that-is-not-where-the-file-is"));
    var file = WriteFile("recording.mp3", 32_000);

    var source = await factory.CreateFromAbsolutePathAsync(file, TimeSpan.FromSeconds(9));

    // ⚠ The FILE PATH is the assertion this test needs, and it used to assert only Duration — which
    // a source built by combining against the (nonexistent) root would report just as happily, since
    // the caller supplied it. FilePath is the only observable that distinguishes "did not re-root"
    // from "re-rooted and happened to carry my duration through".
    Assert.Equal(file, Assert.IsType<AudioFileEventSource>(source).FilePath);
    Assert.Equal(TimeSpan.FromSeconds(9), source.Duration);
    await source.DisposeAsync();
  }

  [Fact]
  public async Task ItHonoursTheAuthoritativeDurationRatherThanEstimating()
  {
    var factory = CreateFactory(_dir);
    // 32 000 bytes estimates to 2s at the factory's flat 16 000 B/s. The authoritative value is
    // 47s, and it is the one that must win — completion is driven by it.
    var file = WriteFile("recording.mp3", 32_000);

    var source = await factory.CreateFromAbsolutePathAsync(file, TimeSpan.FromSeconds(47));

    Assert.Equal(TimeSpan.FromSeconds(47), source.Duration);
    Assert.NotEqual(TimeSpan.FromSeconds(2), source.Duration);
    await source.DisposeAsync();
  }

  [Fact]
  public async Task ANullDurationFallsBackToTheSameEstimateCreateFromFileAsyncWouldUse()
  {
    // DurationSeconds == 0 means UNKNOWN (ADR-022 §4.2). The fallback must be the factory's own
    // estimator so this arc carries no second bytes-per-second constant.
    var factory = CreateFactory(_dir);
    var file = WriteFile("recording.mp3", 32_000);

    var source = await factory.CreateFromAbsolutePathAsync(file, duration: null);

    Assert.Equal(TimeSpan.FromSeconds(2), source.Duration);
    await source.DisposeAsync();
  }

  [Fact]
  public async Task ARelativePathIsRefusedLoudly()
  {
    var factory = CreateFactory(_dir);

    await Assert.ThrowsAsync<ArgumentException>(
      () => factory.CreateFromAbsolutePathAsync("recording.mp3", TimeSpan.FromSeconds(1)));
  }

  [Fact]
  public async Task AMissingFileIsRefusedLoudly()
  {
    var factory = CreateFactory(_dir);

    await Assert.ThrowsAsync<FileNotFoundException>(
      () => factory.CreateFromAbsolutePathAsync(
        Path.Combine(_dir, "absent.mp3"), TimeSpan.FromSeconds(1)));
  }
}

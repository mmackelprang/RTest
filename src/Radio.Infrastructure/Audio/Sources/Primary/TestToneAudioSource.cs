using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.SoundFlow;

namespace Radio.Infrastructure.Audio.Sources.Primary;

/// <summary>
/// Diagnostic test tone audio source that generates 200Hz (L) / 300Hz (R) sine waves.
/// Used to validate the audio pipeline without external hardware (no BT, no USB).
/// </summary>
public class TestToneAudioSource : PrimaryAudioSourceBase
{
  private readonly ILogger<TestToneAudioSource> _logger;
  private readonly SoundFlowPlaybackService _playbackService;
  private readonly Dictionary<string, object> _metadata = new();
  private SineToneGenerator? _generator;

  public TestToneAudioSource(
    ILogger<TestToneAudioSource> logger,
    SoundFlowPlaybackService playbackService)
    : base(logger)
  {
    _logger = logger;
    _playbackService = playbackService;

    _metadata[StandardMetadataKeys.Title] = "Diagnostic Tone";
    _metadata[StandardMetadataKeys.Artist] = "200Hz L / 300Hz R";
    _metadata[StandardMetadataKeys.Album] = "Pipeline Validation";
    _metadata[StandardMetadataKeys.AlbumArtUrl] = StandardMetadataKeys.DefaultAlbumArtUrl;
    _metadata["Source"] = "TestTone";
  }

  /// <inheritdoc/>
  public override string Name => "Test Tone";

  /// <inheritdoc/>
  public override AudioSourceType Type => AudioSourceType.TestTone;

  /// <inheritdoc/>
  public override TimeSpan? Duration => null; // Infinite

  /// <inheritdoc/>
  public override TimeSpan Position => TimeSpan.Zero;

  /// <inheritdoc/>
  public override bool IsSeekable => false;

  /// <inheritdoc/>
  public override IReadOnlyDictionary<string, object> Metadata => _metadata;

  /// <inheritdoc/>
  public override object GetSoundComponent()
  {
    return _generator ?? throw new InvalidOperationException("Test tone not initialized");
  }

  /// <inheritdoc/>
  protected override async Task PlayCoreAsync(CancellationToken cancellationToken)
  {
    var engine = _playbackService.GetUnderlyingEngine();
    if (engine == null)
      throw new InvalidOperationException("Audio engine not available");

    var format = _playbackService.GetAudioFormat();

    _generator = new SineToneGenerator(engine, format);
    _logger.LogInformation("Starting diagnostic tone: 200Hz L / 300Hz R at {SampleRate}Hz",
      format.SampleRate);

    await _playbackService.PlayComponentAsync(Id, _generator, Volume, cancellationToken);
  }

  /// <inheritdoc/>
  protected override Task PauseCoreAsync(CancellationToken cancellationToken)
  {
    // Tone doesn't support pause meaningfully — stop it
    return StopCoreAsync(cancellationToken);
  }

  /// <inheritdoc/>
  protected override Task ResumeCoreAsync(CancellationToken cancellationToken)
  {
    return PlayCoreAsync(cancellationToken);
  }

  /// <inheritdoc/>
  protected override async Task StopCoreAsync(CancellationToken cancellationToken)
  {
    if (_generator != null)
    {
      await _playbackService.StopAsync(Id, cancellationToken);
      _generator = null;
      _logger.LogInformation("Diagnostic tone stopped");
    }
  }

  /// <inheritdoc/>
  protected override async ValueTask DisposeAsyncCore()
  {
    if (_generator != null)
    {
      try
      {
        await _playbackService.StopAsync(Id);
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Error stopping test tone during disposal");
      }
      _generator = null;
    }

    await base.DisposeAsyncCore();
  }
}

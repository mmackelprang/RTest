using System.Text.RegularExpressions;

namespace Radio.Infrastructure.Tests.Audio.SoundFlow;

/// <summary>
/// Guards the one part of the native-device serialization that cannot be enforced by
/// construction.
///
/// <para>Enumeration and device open/switch are serialized by <c>SerializedMiniAudioEngine</c>
/// overriding the virtual methods, so those call sites are safe no matter who writes them.
/// <c>Start</c>/<c>Stop</c>/<c>Dispose</c> live on SoundFlow's <c>AudioPlaybackDevice</c>, which
/// this codebase does not construct and therefore cannot subclass — so they must take
/// <c>NativeAudioDeviceGate</c> at each call site, and nothing but a test can enforce that.</para>
///
/// <para>They drive the same PulseAudio main loop as enumeration (<c>ma_device_start</c> and
/// <c>ma_device_uninit</c> wait on <c>pa_operation</c>s by iterating it), and
/// <c>SwitchPlaybackDevice</c> reaches them on a thread pool thread because
/// <c>DevicesController</c> dispatches it fire-and-forget — so an ungated one can land on top of
/// the 30-second hot-plug enumeration and abort the process.</para>
/// </summary>
public class PlaybackDeviceLifecycleGatingTests
{
  /// <summary>
  /// Matches a lifecycle call on the playback device that is NOT wrapped in the gate.
  /// The gated form is <c>NativeAudioDeviceGate.Run(someDevice.Start)</c> — a method group,
  /// no parentheses — so any occurrence WITH parentheses is a direct, ungated invocation.
  /// </summary>
  private static readonly Regex UngatedLifecycleCall =
    new(@"_playbackDevice\.(Start|Stop|Dispose)\s*\(", RegexOptions.Compiled);

  private static string FindEngineSource()
  {
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RadioConsole.sln")))
    {
      dir = dir.Parent;
    }

    Assert.True(dir is not null, "Could not locate the repository root (RadioConsole.sln) from the test output directory");

    var path = Path.Combine(
      dir!.FullName, "src", "Radio.Infrastructure", "Audio", "SoundFlow", "SoundFlowAudioEngine.cs");

    Assert.True(File.Exists(path), $"Expected SoundFlowAudioEngine.cs at {path}");
    return path;
  }

  /// <summary>
  /// Blanks out line comments while preserving line numbering, so prose like
  /// "do NOT call _playbackDevice.Start() here" is not mistaken for a call site.
  /// </summary>
  private static string StripLineComments(string source) =>
    string.Join('\n', source.Split('\n').Select(line =>
    {
      var comment = line.IndexOf("//", StringComparison.Ordinal);
      return comment >= 0 ? line[..comment] : line;
    }));

  [Fact]
  public void SoundFlowAudioEngine_TakesTheGateForEveryPlaybackDeviceLifecycleCall()
  {
    var path = FindEngineSource();
    var source = StripLineComments(File.ReadAllText(path));

    var offenders = UngatedLifecycleCall.Matches(source)
      .Select(m =>
      {
        var line = source.Take(m.Index).Count(c => c == '\n') + 1;
        return $"  line {line}: {m.Value.Trim()})";
      })
      .ToList();

    Assert.True(offenders.Count == 0,
      "Playback-device lifecycle calls drive the PulseAudio main loop and must be wrapped in "
      + "NativeAudioDeviceGate.Run(...). Use the method-group form, e.g.\n"
      + "  var retiringDevice = _playbackDevice;\n"
      + "  NativeAudioDeviceGate.Run(retiringDevice.Stop);\n"
      + "Ungated call(s) found in SoundFlowAudioEngine.cs:\n"
      + string.Join("\n", offenders));
  }

  /// <summary>
  /// The guard above is only meaningful if the gated form actually appears — otherwise
  /// deleting every lifecycle call would also make it pass.
  /// </summary>
  [Fact]
  public void SoundFlowAudioEngine_ActuallyGatesTheLifecycleCalls()
  {
    var source = File.ReadAllText(FindEngineSource());

    var gatedCalls = Regex.Matches(
      source, @"NativeAudioDeviceGate\.Run\(\w+\.(Start|Stop|Dispose)\)").Count;

    // InitializeAsync Start, TryRecoverPlaybackDevice Start, SwitchPlaybackDevice
    // Stop + Dispose + Start, DisposeAsync Stop + Dispose.
    Assert.True(gatedCalls >= 7,
      $"Expected at least 7 gated playback-device lifecycle calls, found {gatedCalls}");
  }
}

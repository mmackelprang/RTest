using System.Text.RegularExpressions;

namespace Radio.Infrastructure.Tests.Audio.SoundFlow;

/// <summary>
/// Enforces the central premise of the native-device serialization design: serialization is a
/// property of the engine <i>type</i>, so every engine in the process must be a
/// <c>SerializedMiniAudioEngine</c>.
///
/// <para>A bare <c>new MiniAudioEngine()</c> defeats that completely — its
/// <c>UpdateAudioDevicesInfo</c> is the un-overridden base method, so every enumeration on it
/// runs outside <c>NativeAudioDeviceGate</c> and can sit inside the PulseAudio main loop
/// alongside another thread's. Six of these hid in a single file, which is exactly why this is
/// a test and not a code-review convention.</para>
/// </summary>
public class NoRawMiniAudioEngineConstructionTests
{
  /// <summary>
  /// The one legitimate construction: <c>SerializedMiniAudioEngine</c>'s own gated factory.
  /// </summary>
  private const string FactoryFileName = "SerializedMiniAudioEngine.cs";

  private static DirectoryInfo FindRepositoryRoot()
  {
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RadioConsole.sln")))
    {
      dir = dir.Parent;
    }

    Assert.True(dir is not null,
      "Could not locate the repository root (RadioConsole.sln) from the test output directory");
    return dir!;
  }

  [Fact]
  public void NoProductionCode_ConstructsARawMiniAudioEngine()
  {
    var root = FindRepositoryRoot();
    var pattern = new Regex(@"new\s+MiniAudioEngine\s*\(", RegexOptions.Compiled);

    var offenders = new List<string>();

    foreach (var file in Directory.EnumerateFiles(
               Path.Combine(root.FullName, "src"), "*.cs", SearchOption.AllDirectories))
    {
      if (Path.GetFileName(file) == FactoryFileName)
      {
        continue;
      }

      var lines = File.ReadAllLines(file);
      for (var i = 0; i < lines.Length; i++)
      {
        var code = lines[i];

        // Ignore prose — several of these call sites carry explanatory comments that name
        // the type they deliberately do not construct.
        var comment = code.IndexOf("//", StringComparison.Ordinal);
        if (comment >= 0)
        {
          code = code[..comment];
        }

        if (pattern.IsMatch(code))
        {
          offenders.Add($"  {Path.GetRelativePath(root.FullName, file)}:{i + 1}");
        }
      }
    }

    Assert.True(offenders.Count == 0,
      "Every MiniAudioEngine must be created through SerializedMiniAudioEngine.Create(), which "
      + "holds NativeAudioDeviceGate across construction and returns an engine whose "
      + "UpdateAudioDevicesInfo is gated. A raw 'new MiniAudioEngine()' is an unserialized "
      + "native entry point and can abort the process.\nFound at:\n"
      + string.Join("\n", offenders));
  }
}

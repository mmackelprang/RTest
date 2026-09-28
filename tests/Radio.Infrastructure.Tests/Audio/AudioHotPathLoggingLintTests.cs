using System.Text.RegularExpressions;

namespace Radio.Infrastructure.Tests.Audio;

/// <summary>
/// LOG-6 / LOG-7 / LOG-8: the audio callbacks listed here must not log, read the wall clock, or format
/// messages. Logging from them moved to timers (the BT capture watchdog for OnProcess).
/// </summary>
/// <remarks>
/// <para>
/// A source scan rather than a behavioural test, because the thing being guarded is structural and the
/// most important of these callbacks (<c>PipeWireNativeStream.OnProcess</c>) cannot run without a
/// PipeWire daemon. ⚠ It is also the hard prerequisite of <c>LOG-10</c> (punch list O4): promoting a
/// thread to SCHED_FIFO while it logs risks a priority-inversion hang. If this test fails, the fix is
/// to record into a field and emit from a timer — not to widen the allowed patterns.
/// </para>
/// <para>
/// What it cannot see: calls <em>out</em> of these bodies into other methods. Those methods are listed
/// here too where they are part of the hot path.
/// </para>
/// </remarks>
public class AudioHotPathLoggingLintTests
{
  public static TheoryData<string, string> HotPaths => new()
  {
    { "src/Radio.Infrastructure/Platform/Bluetooth/Native/PipeWireNativeStream.cs", "private static void OnProcess(IntPtr userData)" },
    { "src/Radio.Infrastructure/Platform/Bluetooth/Native/OnProcessStatsWindow.cs", "public void RecordCallback(double intervalMs)" },
    { "src/Radio.Infrastructure/Platform/Bluetooth/Native/OnProcessStatsWindow.cs", "public void RecordExecution(double executionMs)" },
    { "src/Radio.Infrastructure/Platform/Bluetooth/Native/OnProcessStatsWindow.cs", "public void RecordRealtimeResult(bool applied, int errno, int priority)" },
  };

  // A logger call, a Serilog static, message formatting, or a wall-clock read.
  private static readonly Regex Forbidden = new(
    @"\b_?[lL]ogger\b|\bLog\.[A-Z]|\.Log(Trace|Debug|Information|Warning|Error|Critical)?\(|\bDateTime(Offset)?\.(Utc)?Now\b|string\.Format\(|\$""",
    RegexOptions.Compiled);

  [Theory]
  [MemberData(nameof(HotPaths))]
  public void HotPathBody_DoesNotLogOrReadTheWallClock(string relativePath, string signature)
  {
    var body = ExtractBody(File.ReadAllText(Path.Combine(RepoRoot(), relativePath)), signature);

    var offenders = body
      .Split('\n')
      .Select(StripComment)
      .Where(line => Forbidden.IsMatch(line))
      .ToList();

    Assert.True(offenders.Count == 0,
      $"{signature} in {relativePath} must not log or read the wall clock (LOG-6/7/8, O4):\n  " +
      string.Join("\n  ", offenders.Select(l => l.Trim())));
  }

  private static string StripComment(string line)
  {
    var i = line.IndexOf("//", StringComparison.Ordinal);
    return i >= 0 ? line[..i] : line;
  }

  private static string ExtractBody(string source, string signature)
  {
    var start = source.IndexOf(signature, StringComparison.Ordinal);
    Assert.True(start >= 0, $"Signature not found — update this lint if the method was renamed: {signature}");
    var open = source.IndexOf('{', start);
    var depth = 0;
    for (var i = open; i < source.Length; i++)
    {
      if (source[i] == '{') depth++;
      else if (source[i] == '}' && --depth == 0)
      {
        return source[open..(i + 1)];
      }
    }
    throw new InvalidOperationException("Unbalanced braces after " + signature);
  }

  private static string RepoRoot()
  {
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RadioConsole.sln")))
    {
      dir = dir.Parent;
    }
    Assert.True(dir is not null, "RadioConsole.sln not found above the test output directory");
    return dir!.FullName;
  }
}

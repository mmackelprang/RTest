using System.Text.RegularExpressions;

namespace Radio.Infrastructure.Tests.Audio;

/// <summary>
/// LOG-6: the audio-thread bodies listed here must not log, write to the console, read the wall clock,
/// format messages, or call the diagnostics emitters. Logging from them moved to timers (the BT capture
/// watchdog for OnProcess). Entries marked lock-free must also not take a lock.
/// </summary>
/// <remarks>
/// <para>
/// A source scan rather than a behavioural test, because the most important of these callbacks
/// (<c>PipeWireNativeStream.OnProcess</c>) cannot run without a PipeWire daemon. If this test fails,
/// the fix is to record into a field and emit from a timer — not to widen the allowed patterns.
/// </para>
/// <para>
/// ⚠ What it cannot see: calls <em>out</em> of these bodies. OnProcess calls
/// <c>BufferedSoundGenerator.AddSamples</c> (which takes the ring-buffer lock) and
/// <c>SrcVariableResampler.Process</c> (which logs on error, <c>LOG-8</c>); neither is listed yet. That
/// gap is one reason <c>LOG-10</c> (SCHED_FIFO, punch-list O4) is still blocked after LOG-6.
/// </para>
/// </remarks>
public class AudioHotPathLoggingLintTests
{
  // (file, method signature, whether the body may take a lock — e.g. a ring buffer's own lock)
  public static TheoryData<string, string, bool> HotPaths => new()
  {
    { "src/Radio.Infrastructure/Platform/Bluetooth/Native/PipeWireNativeStream.cs", "private static void OnProcess(IntPtr userData)", false },
    { "src/Radio.Infrastructure/Platform/Bluetooth/Native/OnProcessStatsWindow.cs", "public void RecordCallback(double intervalMs)", false },
    { "src/Radio.Infrastructure/Platform/Bluetooth/Native/OnProcessStatsWindow.cs", "public void RecordExecution(double executionMs)", false },
    { "src/Radio.Infrastructure/Platform/Bluetooth/Native/OnProcessStatsWindow.cs", "public void RecordRealtimeResult(bool applied, int errno, int priority)", false },
  };

  // A logger call, a Serilog static, console/debug/trace output, message formatting, a wall-clock or
  // tick-count read, or a call into the diagnostics emitters (which log).
  private static readonly Regex Forbidden = new(
    @"\b_?[lL]ogger\b|\bLog\.[A-Z]|\.Log(Trace|Debug|Information|Warning|Error|Critical)?\(|\bConsole\.|\bDebug\.|\bTrace\.|" +
    @"\bDateTime(Offset)?\.(Utc)?Now\b|GetUtcNow|TickCount|string\.Format\(|\$""|\$@""|@\$""|\bEmit(Diagnostics|IfDue)\b",
    RegexOptions.Compiled);

  private static readonly Regex Lock = new(@"\block\s*\(|\bMonitor\.", RegexOptions.Compiled);

  [Theory]
  [MemberData(nameof(HotPaths))]
  public void HotPathBody_DoesNotLogOrReadTheWallClock(string relativePath, string signature, bool locksAllowed)
  {
    var body = ExtractBody(File.ReadAllText(Path.Combine(RepoRoot(), relativePath)), signature);

    var offenders = body
      .Split('\n')
      .Select(StripComment)
      .Where(line => Forbidden.IsMatch(line) || (!locksAllowed && Lock.IsMatch(line)))
      .ToList();

    Assert.True(offenders.Count == 0,
      $"{signature} in {relativePath} must not log or read the wall clock (LOG-6, O4):\n  " +
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
      if (source[i] == '{')
      {
        depth++;
      }
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

using System.Text;
using System.Text.RegularExpressions;

namespace Radio.Core.Tests;

/// <summary>
/// <c>TTS-11</c> <c>T7</c>: a regression lint over <c>src/**/*.cs</c> that fails if one of the
/// twelve log statements this row fixed is written again.
/// </summary>
/// <remarks>
/// ⚠⚠ <b>WHAT THIS TEST CANNOT DO, AND IT MATTERS MORE THAN WHAT IT CAN.</b>
/// <b>This is a regression lint over twelve known shapes. It is NOT a proof of the property.</b>
/// It knows the exact identifier spellings that leaked once — <c>_text</c>, <c>request.Text</c>,
/// <c>request.Message</c>, and the rest below. A NEW leak through a differently-named variable
/// sails straight past it: <c>LogInformation("{Body}", body)</c> is invisible here, and so is any
/// user text reached through a property chain this file has never heard of. Anyone reading a green
/// run as "no user text is logged anywhere in this solution" is reading it wrong.
///
/// What it is genuinely good for is the thing no other test does: firing when somebody re-adds one
/// of these lines in six months, having never read <c>TTS-11</c>. The twelve shapes it knows are
/// the twelve that actually occurred. The real coverage of the PROPERTY lives in the tests that
/// drive the real types — <c>TTSEventSourceLogSafetyTests</c>, <c>TTSFactoryLogSafetyTests</c>,
/// <c>SoundFlowMasterMixerLogSafetyTests</c>, <c>AudioManagerDuckingLogTests</c>,
/// <c>AnnouncementServiceLogSafetyTests</c> (all in <c>Radio.Infrastructure.Tests</c>) and the
/// three controller pins in <c>Radio.API.Tests</c>.
///
/// ⚠ <b>Two shapes from the plan's list are NOT enforced globally, and one is not enforced at
/// all.</b> Every one of those decisions is recorded here rather than made silently, because a
/// lint tuned until it is green can be tuned until it is worthless.
///
/// <list type="bullet">
/// <item><b><c>source.Name</c> is scoped to the mixer, and the reason is cost, not safety.</b>
/// Twenty-nine live call sites under <c>src/</c> pass an <c>IAudioSource</c>'s <c>Name</c> to a log
/// call (counted by identifier spelling — <c>source</c>, <c>primary</c>, <c>oldSource</c>,
/// <c>_activeSource</c> and the rest — so read it as an order of magnitude, not a census). A global
/// rule would flag all of them and be deleted within a week, so the rule is enforced where the leak
/// actually happened: <c>SoundFlowMasterMixer.cs</c>, the domain-agnostic bookkeeping code that has
/// no idea whether it is holding speech.
///
/// ⚠ <b>An earlier revision of this comment said those sites were "every one of them SAFE: they
/// are on the primary-source path". That was false, and believing it cost this row a twelfth leak
/// site.</b> <c>AudioController.Next</c>'s "no primary audio source is active" warning is reached
/// precisely BECAUSE no primary source is active, and it projected <c>Name</c> over the mixer's
/// whole roster — event sources included. It is fixed and pinned (<c>AudioControllerLogSafetyTests</c>
/// in <c>Radio.API.Tests</c>) and it is what the <c>GetActiveSources()</c> rule below exists for.
/// <c>AudioFileEventSource</c> is a second counter-example to the same claim: it is an EVENT source
/// and it logs its own name at <c>:149</c>, <c>:164</c> and <c>:293</c>. Those are harmless — that
/// name is <c>"Event: " + Path.GetFileName(path)</c>, a server-side path — but harmless for a
/// reason the "primary-source path" story never covered.
///
/// <b>So this lint makes no claim about the other twenty-nine.</b> They were not audited one by one
/// here, and a green run is not a certificate that they are safe.</item>
/// <item><b>Bare <c>message</c> and bare <c>text</c> are scoped to the files that leaked them.</b>
/// They are ordinary parameter names; <c>WarnQuietly(Exception, string message)</c> in
/// <c>Radio.Web</c> logs a literal through one and is entirely correct.</item>
/// <item><b><c>phoneNumber</c> is NOT enforced, deliberately.</b> Every occurrence in the tree is a
/// real leak of a real phone number, and every one belongs to the separate, deliberately-unfixed
/// row the TTS-11 plan files at §6.1 — so a rule over it would be all exemption and no coverage.
/// An allowlist naming every file that already leaks is not a lint; it is a place for the next
/// person to add a file instead of fixing a leak. It is left out, and the sites are left visible in
/// the plan where a human owner has to decide about them.</item>
/// </list>
/// </remarks>
public class LogSafetyLintTests
{
  private static readonly string[] Levels = ["Information", "Warning", "Error", "Debug", "Trace"];

  private static readonly Regex LogCall = new(
    @"\.Log(?:" + string.Join("|", Levels) + @")\s*\(", RegexOptions.Compiled);

  /// <summary>Matches the helper whose whole job is to make a forbidden argument safe.</summary>
  private static readonly Regex SafeCall = new(@"\bLogSafeText\s*\.\s*For\s*\(", RegexOptions.Compiled);

  /// <summary>
  /// A forbidden identifier is harmless when only its SIZE is taken —
  /// <c>request.Text!.Length</c> is the character count <c>EventPlaybackService</c> logs on
  /// purpose, and the whole point of the row is that a count is fine and the content is not.
  /// </summary>
  private const string NotASizeRead = @"(?!\s*!?\s*\.\s*(?:Length|Count)\b)";

  /// <summary>One forbidden argument shape, optionally narrowed to the file that leaked it.</summary>
  /// <param name="Pattern">Matched against the argument list with strings and LogSafeText removed.</param>
  /// <param name="Shape">Human name, used in the failure message.</param>
  /// <param name="OnlyInFile">When set, the rule applies to that file alone. See the class remarks.</param>
  private sealed record Rule(Regex Pattern, string Shape, string? OnlyInFile = null);

  private static Rule Of(string pattern, string shape, string? onlyInFile = null) =>
    new(new Regex(pattern, RegexOptions.Compiled), shape, onlyInFile);

  private static readonly Rule[] Forbidden =
  [
    // L2, L3 — TTSEventSource logged the WHOLE utterance, at Information and at Debug.
    Of(@"\b_text\b" + NotASizeRead, "_text"),
    // L11 — SourcesController's TTS event route.
    Of(@"\brequest\s*\??\s*\.\s*Text\b" + NotASizeRead, "request.Text"),
    // L6 — NotificationsController's announce route.
    Of(@"\brequest\s*\??\s*\.\s*Message\b" + NotASizeRead, "request.Message"),
    // L4, L5 — both ducking arms in AudioManager.
    Of(@"\bTriggeringSource\s*\??\s*\.\s*Name\b", "TriggeringSource.Name"),
    // Never occurred, but it is the obvious next spelling of the same mistake.
    Of(@"\bttsSource\s*\??\s*\.\s*Name\b", "ttsSource.Name"),
    // L7, L8 — AnnouncementService logged the message untruncated, twice.
    Of(@"(?<![\w.])message\b" + NotASizeRead, "bare message", "AnnouncementService.cs"),
    // L1 — TTSFactory logged the first 50 characters.
    Of(@"(?<![\w.])text\b" + NotASizeRead, "bare text", "TTSFactory.cs"),
    // L9, L10 — the mixer's add/remove bookkeeping.
    Of(@"\bsource\s*\??\s*\.\s*Name\b", "source.Name", "SoundFlowMasterMixer.cs"),
    // L12 — a projection of Name over the WHOLE mixer roster, event sources included. The rule
    // above cannot see this one: it is scoped to SoundFlowMasterMixer.cs and the lambda parameter
    // at the site that leaked was `s`, not `source`. So this rule is keyed on the CALL that
    // produces the roster rather than on whatever the lambda happens to be called, which makes it
    // global and spelling-independent. It is deliberately loose about what sits between the two:
    // `.Select(s => s.Name)`, `.First().Name` and `.Where(...).Select(x => x.Name)` all match.
    Of(@"GetActiveSources\s*\(\s*\)[^;]*?\.\s*Name\b", "Name over GetActiveSources()"),
  ];

  [Fact]
  public void NoLogCallInTheSolutionPassesAKnownUserTextArgument()
  {
    var src = Path.Combine(FindRepositoryRoot(), "src");

    // The scan itself must be provably alive. A lint that quietly matches nothing — wrong root,
    // broken extractor, changed layout — is precisely a test that passes against a broken
    // implementation, which is the failure mode this whole row's test plan is built around.
    Assert.True(Directory.Exists(src), $"Expected a source tree at '{src}'.");

    var files = Directory
      .EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
      .Where(f => !IsGenerated(f))
      .ToList();

    var violations = new List<string>();
    var callsScanned = 0;

    foreach (var file in files)
    {
      var text = File.ReadAllText(file);
      var name = Path.GetFileName(file);

      foreach (var (offset, arguments) in LogCallArguments(text))
      {
        callsScanned++;
        var scrubbed = RemoveSafeCalls(RemoveStringLiterals(arguments));

        foreach (var rule in Forbidden)
        {
          if (rule.OnlyInFile is not null && !name.Equals(rule.OnlyInFile, StringComparison.Ordinal))
          {
            continue;
          }

          if (rule.Pattern.IsMatch(scrubbed))
          {
            var line = text.Take(offset).Count(c => c == '\n') + 1;
            violations.Add(
              $"{Path.GetRelativePath(src, file)}:{line} passes [{rule.Shape}] — " +
              Collapse(arguments));
          }
        }
      }
    }

    // Floors, not exact counts: they are here to catch "scanned nothing", not to be updated every
    // time a file is added. The tree held 451 files and 2,280 log calls when this was written.
    Assert.True(files.Count > 200, $"Only {files.Count} source files found under '{src}'.");
    Assert.True(callsScanned > 800, $"Only {callsScanned} log calls found — the extractor is broken.");

    Assert.True(
      violations.Count == 0,
      "TTS-11: user text must not be passed to a log call. Wrap it in LogSafeText.For(...), or " +
      "log the source's Type and Id instead of its Name.\n  " + string.Join("\n  ", violations));
  }

  private static bool IsGenerated(string path) =>
    path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
    path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

  private static string Collapse(string s) =>
    string.Join(" ", s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

  /// <summary>
  /// Walks up from the test binary looking for the solution file.
  /// </summary>
  /// <remarks>
  /// ⚠ <b>Fails loudly rather than skipping.</b> A source-scanning lint that no-ops when it cannot
  /// find the source tree is worse than no lint: it reports green forever from any unexpected
  /// working directory, and nobody notices because green is what it always says.
  /// </remarks>
  private static string FindRepositoryRoot()
  {
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    var probed = new List<string>();

    while (dir is not null)
    {
      probed.Add(dir.FullName);
      if (File.Exists(Path.Combine(dir.FullName, "RadioConsole.sln")))
      {
        return dir.FullName;
      }

      dir = dir.Parent;
    }

    Assert.Fail(
      "LogSafetyLintTests could not locate RadioConsole.sln by walking up from " +
      $"'{AppContext.BaseDirectory}'. This lint scans src/**/*.cs, so without the repository root " +
      "it cannot run — and it must FAIL rather than silently pass. Directories probed:\n  " +
      string.Join("\n  ", probed));
    return string.Empty; // Unreachable; Assert.Fail throws.
  }

  /// <summary>
  /// Yields (offset of the call, its full argument text) for every logging call in
  /// <paramref name="text"/>, with parentheses balanced so a multi-line call is returned whole.
  /// </summary>
  /// <remarks>
  /// Balancing matters: four of the eleven original sites spanned two or more lines, so a
  /// line-at-a-time scan would have missed the arguments it was looking for.
  /// </remarks>
  private static IEnumerable<(int Offset, string Arguments)> LogCallArguments(string text)
  {
    foreach (Match match in LogCall.Matches(text))
    {
      var close = IndexOfMatchingParen(text, match.Index + match.Length);
      yield return (match.Index, text[(match.Index + match.Length)..close]);
    }
  }

  /// <summary>
  /// <paramref name="start"/> is the index just past an opening parenthesis; returns the index of
  /// the one that closes it, skipping over string literals so a ')' inside a message template does
  /// not end the call early.
  /// </summary>
  private static int IndexOfMatchingParen(string text, int start)
  {
    var depth = 1;
    var i = start;

    while (i < text.Length)
    {
      var c = text[i];
      if (c == '"')
      {
        i = IndexPastStringLiteral(text, i);
        continue;
      }

      if (c == '(')
      {
        depth++;
      }
      else if (c == ')' && --depth == 0)
      {
        return i;
      }

      i++;
    }

    return text.Length;
  }

  /// <summary><paramref name="i"/> indexes an opening quote; returns the index past the close.</summary>
  private static int IndexPastStringLiteral(string text, int i)
  {
    var j = i + 1;
    while (j < text.Length)
    {
      if (text[j] == '\\')
      {
        j += 2;
        continue;
      }

      if (text[j] == '"')
      {
        return j + 1;
      }

      j++;
    }

    return text.Length;
  }

  /// <summary>
  /// Blanks the contents of string literals, so a message TEMPLATE cannot trip a rule.
  /// </summary>
  /// <remarks>
  /// ⚠ A known blind spot, and it is a real one: an INTERPOLATED string is blanked too, so
  /// <c>LogInformation($"Announcing: {message}")</c> would not be caught. That shape is already
  /// wrong for other reasons — it defeats structured logging and every site in this tree uses
  /// placeholders — but this lint is not what would catch it.
  /// </remarks>
  private static string RemoveStringLiterals(string arguments)
  {
    var sb = new StringBuilder(arguments.Length);
    var i = 0;

    while (i < arguments.Length)
    {
      if (arguments[i] == '"')
      {
        sb.Append("\"\"");
        i = IndexPastStringLiteral(arguments, i);
        continue;
      }

      sb.Append(arguments[i]);
      i++;
    }

    return sb.ToString();
  }

  /// <summary>
  /// Removes every <c>LogSafeText.For(...)</c> sub-expression, argument and all — that call is the
  /// fix, so an identifier inside one is not a violation.
  /// </summary>
  private static string RemoveSafeCalls(string arguments)
  {
    while (true)
    {
      var match = SafeCall.Match(arguments);
      if (!match.Success)
      {
        return arguments;
      }

      var close = IndexOfMatchingParen(arguments, match.Index + match.Length);
      arguments = arguments[..match.Index] + "SAFE" +
        (close < arguments.Length ? arguments[(close + 1)..] : string.Empty);
    }
  }
}

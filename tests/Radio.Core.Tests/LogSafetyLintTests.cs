using System.Text;
using System.Text.RegularExpressions;

namespace Radio.Core.Tests;

/// <summary>
/// A regression lint over <c>src/**/*.cs</c> that fails if one of the log statements
/// <c>TTS-11</c> (twelve, user text) or <c>PHN-5</c> (eleven, phone numbers) fixed is written
/// again.
/// </summary>
/// <remarks>
/// ⚠⚠ <b>WHAT THIS TEST CANNOT DO, AND IT MATTERS MORE THAN WHAT IT CAN.</b>
/// <b>This is a regression lint over known shapes. It is NOT a proof of the property.</b>
/// It knows the exact identifier spellings that leaked once — <c>_text</c>, <c>request.Text</c>,
/// <c>request.Message</c>, and the rest below. A NEW leak through a differently-named variable
/// sails straight past it: <c>LogInformation("{Body}", body)</c> is invisible here, and so is any
/// user text reached through a property chain this file has never heard of. Anyone reading a green
/// run as "no user text is logged anywhere in this solution" is reading it wrong.
///
/// It also parses C# with a regex and a paren counter rather than a lexer, and the two places
/// that costs something are documented where they are made: <see cref="RemoveStringLiterals"/>
/// (an interpolated string is blanked, so <c>LogInformation($"…{message}")</c> is invisible) and
/// <see cref="IndexPastStringLiteral"/> (backslash is treated as an escape even in a verbatim or
/// raw literal). Neither is triggered by anything in the tree today.
///
/// What it is genuinely good for is the thing no other test does: firing when somebody re-adds one
/// of these lines in six months, having never read <c>TTS-11</c>. The twelve shapes it knows are
/// the twelve that actually occurred. The real coverage of the PROPERTY lives in the tests that
/// drive the real types — <c>TTSEventSourceLogSafetyTests</c>, <c>TTSFactoryLogSafetyTests</c>,
/// <c>SoundFlowMasterMixerLogSafetyTests</c>, <c>AudioManagerDuckingLogTests</c>,
/// <c>AnnouncementServiceLogSafetyTests</c> (all in <c>Radio.Infrastructure.Tests</c>) and the
/// three controller pins in <c>Radio.API.Tests</c>.
///
/// ⚠ <b>Some shapes are NOT enforced globally, and one class of leak is not enforceable at all.</b>
/// Every one of those decisions is recorded here rather than made silently, because a lint tuned
/// until it is green can be tuned until it is worthless.
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
/// <item><b><c>phoneNumber</c> IS enforced now, globally and with no per-file exemption —
/// <c>PHN-5</c> retired the argument for leaving it out by retiring the exemptions.</b> The
/// argument itself is preserved, because it is the standing case against ever adding an allowlist
/// here: <i>"Every occurrence in the tree is a real leak of a real phone number… so a rule over it
/// would be all exemption and no coverage. An allowlist naming every file that already leaks is
/// not a lint; it is a place for the next person to add a file instead of fixing a leak."</i> That
/// was true when it was written and it stopped being true when the eleven sites were fixed: the
/// rule was turned on and the tree was clean on the first run, with nothing to exempt.
/// ⚠ <b>So if this rule ever needs an exemption, the exemption is the bug.</b> A new file matching
/// it is a new leak, not a false positive.</item>
/// <item>⚠⚠ <b>THE RULE ABOVE COVERS PHONE NUMBERS. IT DOES NOT COVER CONTACT NAMES, AND THERE IS
/// NO PLAUSIBLE RULE THAT WOULD.</b> <c>Name</c> is far too common an identifier to key on —
/// <c>source.Name</c> alone has twenty-nine live sites, per the first bullet — so
/// <c>PHN-5</c> deleted contact names from log lines rather than masking them, and nothing here
/// can tell whether one comes back. That property is pinned only by the behavioural tests that
/// drive the real types (<c>PhoneContactLookupServiceLogSafetyTests</c> and
/// <c>PhoneCallClientLogSafetyTests</c> in <c>Radio.Infrastructure.Tests</c>). <b>Anyone reading a
/// green run here as "no phone PII is logged anywhere" is reading it wrong</b>, in exactly the way
/// the top of these remarks warns about for user text.</item>
/// </list>
/// </remarks>
public class LogSafetyLintTests
{
  private static readonly string[] Levels = ["Information", "Warning", "Error", "Debug", "Trace"];

  private static readonly Regex LogCall = new(
    @"\.Log(?:" + string.Join("|", Levels) + @")\s*\(", RegexOptions.Compiled);

  /// <summary>Matches the helpers whose whole job is to make a forbidden argument safe.</summary>
  /// <remarks>
  /// ⚠ <c>For(?:Phone)?</c>, not <c>For</c>. The original required an open paren immediately after
  /// <c>For</c>, so <c>LogSafeText.ForPhone(phoneNumber)</c> was NOT stripped by
  /// <see cref="RemoveSafeCalls"/> — which would have made the <c>phoneNumber</c> rule below report
  /// a violation at every line PHN-5 fixed. The lint would have failed BECAUSE the row succeeded.
  /// </remarks>
  private static readonly Regex SafeCall = new(
    @"\bLogSafeText\s*\.\s*For(?:Phone)?\s*\(", RegexOptions.Compiled);

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
    // P1-P11 — PHN-5. Enforced GLOBALLY and with no per-file exemption, which is only possible
    // because PHN-5 fixed every occurrence in the tree. This file's own remarks explain why the
    // rule was left out before that: "a rule over it would be all exemption and no coverage… an
    // allowlist naming every file that already leaks is not a lint; it is a place for the next
    // person to add a file instead of fixing a leak." There is now nothing to exempt.
    Of(@"\bphoneNumber\b" + NotASizeRead, "phoneNumber"),
    // The other spellings the same value travels under in this subsystem. `number` is bare and
    // therefore scoped, like `message` and `text` above: it is an ordinary parameter name.
    Of(@"\bcallerNumber\b" + NotASizeRead, "callerNumber"),
    Of(@"(?<![\w.])number\b" + NotASizeRead, "bare number", "GvTrunkApiService.cs"),
    Of(@"(?<![\w.])number\b" + NotASizeRead, "bare number", "ContactResolutionService.cs"),
  ];

  [Fact]
  public void NoLogCallInTheSolutionPassesAKnownUserTextArgument()
  {
    var root = FindRepositoryRoot();
    var src = Path.Combine(root, "src");

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
    Assert.True(
      callsScanned > 800,
      $"Only {callsScanned} log calls found under '{src}' — the extractor is broken.");

    // ⚠ The resolved root goes in the failure message, always. Violations are reported as paths
    // RELATIVE to src, which are identical in every checkout of this repository — so a scan of the
    // wrong tree reads exactly like a real finding. See FindRepositoryRoot: this has happened.
    Assert.True(
      violations.Count == 0,
      $"TTS-11 / PHN-5: user text and phone numbers must not be passed to a log call. " +
      $"Scanned '{src}'. Wrap text in LogSafeText.For(...) and a number in " +
      "LogSafeText.ForPhone(...), or log the source's Type and Id instead of its Name.\n  " +
      string.Join("\n  ", violations));
  }

  private static bool IsGenerated(string path) =>
    path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
    path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

  private static string Collapse(string s) =>
    string.Join(" ", s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

  /// <summary>
  /// Walks up from the test binary looking for the solution file, and returns the OUTERMOST
  /// directory that has one.
  /// </summary>
  /// <remarks>
  /// ⚠ <b>Fails loudly rather than skipping.</b> A source-scanning lint that no-ops when it cannot
  /// find the source tree is worse than no lint: it reports green forever from any unexpected
  /// working directory, and nobody notices because green is what it always says.
  ///
  /// ⚠⚠ <b>AND IT MUST NOT RESOLVE TO A NESTED CHECKOUT. That is not hypothetical — it happened
  /// during this row's review.</b> A reviewer saw this lint fail twice reporting the PRE-FIX text
  /// of <c>SourcesController.cs:646</c>, then pass six times with no change in between.
  /// <c>.claude/worktrees/</c> holds complete checkouts of this repository at other commits, each
  /// with its own <c>RadioConsole.sln</c>. A run whose binary sits inside one of those would find
  /// that solution first and scan THAT commit's source — while printing paths relative to
  /// <c>src</c>, which are identical in every checkout. The output is indistinguishable from a
  /// real violation, which is the exact failure class this row exists to guard against: a test
  /// reporting confidently about something it did not look at.
  ///
  /// The rule is therefore "outermost, preferring a directory that is not under a worktree", not
  /// "first hit": a nested checkout's ancestor chain passes through the enclosing repository, so
  /// continuing the walk lands on the real root.
  ///
  /// ⚠ <b>The preference is a preference and NOT a filter, and an earlier revision of this comment
  /// got that wrong in a way that made the test fail.</b> It claimed "a worktree checked out
  /// somewhere else entirely is its own outermost match and is scanned normally, which is correct".
  /// That holds only while the path does not contain the segment — and
  /// <c>D:/prj/RTest/worktrees/…</c> is the convention this repository actually uses, so such a
  /// checkout was its own only candidate AND matched the exclusion, the filter emptied the list,
  /// and the assertion fired with "could not settle on a repository root". Measured on <c>main</c>
  /// at <c>6c220461</c>, and repaired in PHN-5 by falling back to the outermost candidate when
  /// every candidate looks nested. The case the guard was written for still wins: with two
  /// candidates, the non-worktree ancestor is preferred.
  /// </remarks>
  private static string FindRepositoryRoot()
  {
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    var probed = new List<string>();
    var candidates = new List<string>();

    while (dir is not null)
    {
      probed.Add(dir.FullName);
      if (File.Exists(Path.Combine(dir.FullName, "RadioConsole.sln")))
      {
        candidates.Add(dir.FullName);
      }

      dir = dir.Parent;
    }

    // Last found = outermost, because the walk goes upwards.
    // Prefer a root that is not a nested checkout; but if EVERY candidate looks like one, the
    // outermost is still the right answer and is certainly better than no root at all. Before
    // PHN-5 this was LastOrDefault(...) with no fallback, so a worktree parked under a directory
    // literally named "worktrees" — the convention this repo uses — filtered out its own only
    // candidate and the lint failed with "could not settle on a repository root". Measured.
    var root = candidates.LastOrDefault(c => !IsInsideAWorktree(c)) ?? candidates.LastOrDefault();

    Assert.True(
      root is not null,
      "LogSafetyLintTests could not settle on a repository root by walking up from " +
      $"'{AppContext.BaseDirectory}'. This lint scans src/**/*.cs, so without the root it cannot " +
      "run — and it must FAIL rather than silently pass, or scan the wrong tree. " +
      $"Solution files found: {(candidates.Count == 0 ? "(none)" : string.Join(", ", candidates))}." +
      "\nDirectories probed:\n  " + string.Join("\n  ", probed));

    return root!;
  }

  /// <summary>
  /// True when <paramref name="path"/> has a <c>.claude/worktrees</c> (or bare <c>worktrees</c>)
  /// segment, i.e. it is a checkout parked inside another repository rather than the repository.
  /// </summary>
  /// <remarks>
  /// Matched on whole path SEGMENTS rather than as a substring, so a legitimate directory whose
  /// name merely contains "worktrees" is not excluded. Case-insensitive: the paths involved are
  /// Windows paths as often as not.
  /// </remarks>
  private static bool IsInsideAWorktree(string path)
  {
    var segments = path.Split(
      [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
      StringSplitOptions.RemoveEmptyEntries);

    return segments.Any(s => s.Equals("worktrees", StringComparison.OrdinalIgnoreCase));
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
  /// <remarks>
  /// ⚠ <b>A known limitation, listed with the other one on <see cref="RemoveStringLiterals"/>.</b>
  /// This treats a backslash as an ESCAPE in every string, which is wrong inside a verbatim
  /// (<c>@"…"</c>) or raw (<c>"""…"""</c>) literal: there a backslash is an ordinary character and
  /// the escaped quote is <c>""</c>. So a template ending in a backslash inside a verbatim literal
  /// would swallow its own closing quote and unbalance the parenthesis scan for the remainder of
  /// the file — silently, and in the direction of scanning too much rather than too little.
  ///
  /// Nothing in <c>src/</c> trips it today: no logging template in the tree is a verbatim or raw
  /// literal. It is left simple rather than made correct, because owning a C# lexer is a large
  /// thing to carry for a lint over twelve known shapes. Written down so that a future failure is
  /// recognised rather than debugged from first principles.
  /// </remarks>
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

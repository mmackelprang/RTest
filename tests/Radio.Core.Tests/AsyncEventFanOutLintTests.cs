using System.Text.RegularExpressions;

namespace Radio.Core.Tests;

/// <summary>
/// A lint over <c>src/**/*.cs</c> and <c>src/**/*.razor</c> that fails if an <c>event Func&lt;…&gt;</c>
/// is raised by invoking it directly instead of walking <see cref="Delegate.GetInvocationList"/>.
/// </summary>
/// <remarks>
/// ⚠ WHAT THIS FORBIDS AND WHY. <c>await SomeEvent.Invoke()</c> on a multicast Func&lt;Task&gt; RUNS
/// every subscriber but returns only the LAST one's Task, so every earlier subscriber's continuation
/// is unobserved and a try/catch around the invoke protects exactly one of N. Worse, a subscriber
/// that throws SYNCHRONOUSLY throws out of Invoke itself and every handler registered after it never
/// runs. Queue rows <c>UI-6</c> (3 sites) and <c>UI-7</c> (17). The canonical correct form, with the
/// full argument, is <c>AudioStateStore.NotifyAsync</c>.
///
/// ⚠ THIS RULE IS DELIBERATELY NOT SCOPED TO A FILENAME, and that is a lesson from the file beside it.
/// <c>LogSafetyLintTests</c> has five rules narrowed by a literal filename compared with
/// StringComparison.Ordinal; every one of them silently disables itself on a rename and reports green
/// forever (plan UI-7 §0.6, <c>C-152</c>). This rule reads each file's OWN event declarations and
/// enforces against those names, so it applies to files nobody has written yet and survives every
/// rename.
///
/// ⚠ IT IS A REGRESSION LINT OVER A SHAPE, NOT A PROOF OF THE PROPERTY. It parses C# with regexes.
/// It knows the two forms that occurred — a direct raise and a raise through a local aliased from the
/// event — and it does not model partial classes, so an event declared in one file and raised in
/// another part of the same type would sail past. Nothing in the tree does that today.
///
/// ⚠ AND IT DOES NOT SCAN <c>tests/</c>, WHICH IS WHERE AN INSTANCE STILL LIVED WHEN THIS WAS
/// WRITTEN. UI-7 <c>C-213</c>: several test fixtures fire hub events by reflecting the compiler's
/// backing field and calling <c>await del.Invoke(dto)</c> on it — the very shape this rule forbids,
/// in the harness that would be used to test the fix. UI-7 repaired those six sites by hand; nothing
/// automated stops a seventh, because widening the scan to <c>tests/</c> would also have to model
/// the reflection that produces the delegate, which a regex cannot.
///
/// ⚠ IF THIS RULE EVER NEEDS AN EXEMPTION, THE EXEMPTION IS THE BUG. There is no allowlist and there
/// must not be one; <c>LogSafetyLintTests</c> makes the same argument about <c>phoneNumber</c> at its
/// own :64-73 and it applies verbatim. A new file matching this rule is a new instance of the defect.
///
/// ⛔ event Action&lt;T&gt; and EventHandler&lt;T&gt; are OUT OF SCOPE and the rule does not match them.
/// They are void-returning, so Invoke really does run every handler and there is no discarded Task.
/// Only the starvation half applies to them, and that is a different (open) question — see
/// PhoneUnreadState.cs:23 and plan UI-7 §6.1.
/// </remarks>
public class AsyncEventFanOutLintTests
{
  /// <summary>Matches an async event declaration and captures its name.</summary>
  /// <remarks>
  /// Covers <c>event Func&lt;Task&gt;? X;</c>, <c>event Func&lt;T, Task&gt;? X;</c> and the
  /// non-nullable spellings. The type argument list is matched loosely — anything up to the closing
  /// angle bracket — because what makes this the defect is the delegate being awaited, not which
  /// payload it carries.
  /// </remarks>
  private static readonly Regex AsyncEventDeclaration = new(
    @"\bevent\s+Func\s*<[^>]*>\s*\??\s*(?<name>\w+)\s*[;=]", RegexOptions.Compiled);

  [Fact]
  public void NoAsyncEventInTheSolutionIsRaisedByDirectInvoke()
  {
    var src = Path.Combine(RepositoryRoot.Find(), "src");

    // The scan must be provably alive before its silence means anything.
    Assert.True(Directory.Exists(src), $"Expected a source tree at '{src}'.");

    var files = Directory
      .EnumerateFiles(src, "*.*", SearchOption.AllDirectories)
      .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
        || f.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
      .Where(f => !IsGenerated(f))
      .ToList();

    var violations = new List<string>();
    var declaringFiles = 0;
    var fanOutSites = 0;

    foreach (var file in files)
    {
      var text = File.ReadAllText(file);
      fanOutSites += Regex.Matches(text, @"\bGetInvocationList\s*\(\s*\)").Count;

      var found = Violations(text).ToList();
      if (AsyncEventDeclaration.IsMatch(text))
      {
        declaringFiles++;
      }

      foreach (var (offset, shape) in found)
      {
        var line = text.Take(offset).Count(c => c == '\n') + 1;
        violations.Add($"{Path.GetRelativePath(src, file)}:{line} raises [{shape}]");
      }
    }

    // ⚠ Floors on things that must be NON-zero. A "violations == 0" assertion is satisfied just as
    // well by a regex that matches nothing, so these are what stop this test passing forever after
    // someone breaks the extractor. See also TheRuleFiresOnTheShapesItExistsToForbid below, which is
    // the layer that does not depend on the tree at all.
    Assert.True(files.Count > 200, $"Only {files.Count} source files found under '{src}'.");
    Assert.True(
      declaringFiles >= 5,
      $"Only {declaringFiles} files declare an `event Func<...>` under '{src}' — there were 6 when "
      + "this was written, so the declaration regex is probably broken.");
    Assert.True(
      fanOutSites >= 5,
      $"Only {fanOutSites} GetInvocationList() fan-out sites found under '{src}' — there were 6 "
      + "after UI-7, so either a fix was reverted or the scan is looking at the wrong tree.");

    Assert.True(
      violations.Count == 0,
      "UI-6 / UI-7: an `event Func<...>` must be raised by walking GetInvocationList() and awaiting "
      + "each subscriber inside its own try/catch — never by `await TheEvent.Invoke(...)`, which "
      + "returns only the LAST subscriber's Task and lets a synchronous throw starve the rest. "
      + $"Scanned '{src}'. Copy AudioStateStore.NotifyAsync.\n  "
      + string.Join("\n  ", violations));
  }

  /// <summary>
  /// ⭐ The positive control. Without it this class is unfalsifiable: the tree is expected to be
  /// clean, so the scan above can only ever assert zero, and a regex matching nothing asserts zero
  /// just as well. These fixtures are the VERBATIM pre-UI-7 text of two real sites.
  /// </summary>
  /// <remarks>
  /// The idiom is <c>VisualizerPanelTests.cs:233-236</c>'s — "prove the instrument before trusting
  /// its silence" — applied to a source scan instead of a reflection call.
  /// </remarks>
  [Fact]
  public void TheRuleFiresOnTheShapesItExistsToForbid()
  {
    // AudioStateHubService.cs:39 + :138-141 as they stood at 084a6bbd — the direct raise.
    const string DirectRaise = """
      public event Func<Task>? PlaybackStateChanged;
            if (PlaybackStateChanged != null)
            {
              await PlaybackStateChanged.Invoke();
            }
      """;

    // AudioStateHubService.cs:74 + :284-288 as they stood at 084a6bbd — the ALIASED raise, which no
    // direct rule reaches. A version of this lint without the alias pass would cover fourteen of the
    // row's fifteen sites while claiming fifteen; LogSafetyLintTests records exactly that mistake
    // about its own P7 site.
    const string AliasedRaise = """
      public event Func<Task>? ConfigChanged;
            var handler = ConfigChanged;
            if (handler != null)
            {
              await handler.Invoke();
            }
      """;

    // The corrected shape, which must NOT fire — the event name appears only as an ARGUMENT.
    const string Corrected = """
      public event Func<Task>? PlaybackStateChanged;
            await NotifyAsync(PlaybackStateChanged);
            foreach (var subscriber in handler.GetInvocationList())
            {
              await ((Func<Task>)subscriber).Invoke();
            }
      """;

    // ⭐ ConsolePlaybackState.cs:38-39 verbatim — a `///` line CORRECTLY DESCRIBING a different
    // class's defect, in a file that declares its own event named `Changed`. This is the eighteenth
    // hit the rule reported before BlankNonCode existed, and the reason it exists: a lint whose
    // first casualty is the documentation of the defect it forbids is worse than no lint.
    const string DescribedInAComment = """
      public event Func<Task>? Changed;
      /// ⚠ AND ITS OWN FAN-OUT IS NOT A COPY OF THE DEFECT. The design handoff says to build this "exactly
      /// like PhoneUnreadState"; PhoneUnreadState.Set is Changed?.Invoke(_count) — a plain multicast invoke
      """;

    // The same shape inside a string literal — also not code, also must not fire.
    const string QuotedInAString = """
      public event Func<Task>? Changed;
            _logger.LogWarning("do not write Changed.Invoke() here");
      """;

    Assert.Single(Violations(DirectRaise));
    Assert.Single(Violations(AliasedRaise));
    Assert.Empty(Violations(Corrected));
    Assert.Empty(Violations(DescribedInAComment));
    Assert.Empty(Violations(QuotedInAString));
  }

  /// <summary>
  /// Yields (offset, shape) for every direct raise of an async event declared in this same text.
  /// </summary>
  /// <remarks>
  /// Pass 1 collects the declared event names. Pass 2 forbids each name as the receiver of
  /// <c>.Invoke(</c>. Pass 2b resolves ONE level of aliasing — <c>var handler = SomeEvent;</c> —
  /// because that is a real shape in the tree (AudioStateHubService.cs:284-287) and it matches no
  /// direct rule. One level is enough for everything that exists; a chain of two would not be
  /// caught, and that is stated rather than implied.
  ///
  /// 📌 THE DIRECT-CALL ARM IS KEPT, AND THE PLAN EXPECTED IT TO BE DELETED. UI-7 §7.2 called it
  /// "the least tested idea in this plan" and predicted false positives, because an event and a
  /// method may share a name. Measured against src/ before Task 1 landed, with the whole tree
  /// unfixed, it produced ZERO hits — no false positives and no true ones, since no site in this
  /// repository spells the raise as bare <c>SomeEvent()</c>. It is retained rather than deleted
  /// because it costs nothing measured and covers a genuine second spelling of the same defect;
  /// the deletion the plan authorised was conditional on false positives that did not appear.
  ///
  /// ⚠ It stays quiet after the fix by construction, not by luck: the corrected form puts the event
  /// name in front of a <c>,</c> or a <c>)</c> — <c>NotifyAsync(SourceChanged)</c> — never in front
  /// of a <c>(</c>. If it ever does fire, read it before suppressing it.
  /// </remarks>
  private static IEnumerable<(int Offset, string Shape)> Violations(string rawText)
  {
    // ⚠ Comments and string literals are blanked FIRST, and that is not a nicety — it is the
    // difference between 17 findings and 18. See BlankNonCode.
    var text = BlankNonCode(rawText);

    var names = AsyncEventDeclaration.Matches(text)
      .Select(m => m.Groups["name"].Value)
      .Distinct()
      .ToList();

    if (names.Count == 0)
    {
      yield break;
    }

    // Pass 2b — locals aliased from a declared event, one level.
    var aliases = new List<string>();
    foreach (var name in names)
    {
      foreach (Match m in Regex.Matches(
        text, @"\bvar\s+(?<alias>\w+)\s*=\s*" + Regex.Escape(name) + @"\s*;"))
      {
        aliases.Add(m.Groups["alias"].Value);
      }
    }

    foreach (var receiver in names.Concat(aliases).Distinct())
    {
      // `X.Invoke(`, `X?.Invoke(`, `X!.Invoke(` — the receiver form, which is all fifteen UI-7 sites.
      foreach (Match m in Regex.Matches(
        text, @"\b" + Regex.Escape(receiver) + @"\s*[!?]?\s*\.\s*Invoke\s*\("))
      {
        yield return (m.Index, receiver + ".Invoke(...)");
      }

      foreach (Match m in Regex.Matches(
        text, @"(?<![\w.])" + Regex.Escape(receiver) + @"\s*\((?!\s*\))"))
      {
        yield return (m.Index, receiver + "(...)");
      }
    }
  }

  /// <summary>
  /// Returns <paramref name="text"/> with every comment, string literal and character literal
  /// replaced by spaces, preserving length and line breaks so reported offsets stay exact.
  /// </summary>
  /// <remarks>
  /// ⭐ THIS EXISTS BECAUSE THE LINT FOUND ITS FIRST FALSE POSITIVE IN PROSE ABOUT ITS OWN DEFECT,
  /// and in a repository that documents defects as heavily as this one that is a recurring shape
  /// rather than a one-off. Run without it against src/ at 084a6bbd, the rule reported EIGHTEEN
  /// sites: the seventeen real ones and <c>ConsolePlaybackState.cs:39</c>, a <c>///</c> line reading
  /// "PhoneUnreadState.Set is Changed?.Invoke(_count)" — a correct description of a DIFFERENT
  /// class's code, sitting in a file that happens to declare its own event named <c>Changed</c>.
  /// The plan predicted seventeen and the extra one is what the prediction was missing.
  ///
  /// ⚠ Exempting that file was the wrong repair and was not made. The rule must not fire on
  /// comments in ANY file, or the first thing it punishes is documenting the defect — and the
  /// remarks on AudioStateStore.NotifyAsync, AudioStateHubService.NotifyAsync and this very class
  /// all quote the forbidden shape on purpose.
  ///
  /// ⚠ Length and newlines are preserved rather than the regions being deleted, because the caller
  /// converts a match offset into a line number by counting '\n' before it. Deleting would silently
  /// shift every reported line.
  ///
  /// ⚠ LIMITATION, stated rather than implied: the holes of an interpolated string are blanked
  /// along with the literal, so a forbidden raise written inside <c>$"{...}"</c> would be missed.
  /// Nothing in the tree does that, and modelling interpolation would mean writing a C# parser.
  /// </remarks>
  private static string BlankNonCode(string text)
  {
    var buffer = text.ToCharArray();
    var i = 0;

    void Blank(int from, int to)
    {
      for (var k = from; k < to && k < buffer.Length; k++)
      {
        if (buffer[k] != '\n' && buffer[k] != '\r')
        {
          buffer[k] = ' ';
        }
      }
    }

    while (i < text.Length)
    {
      var c = text[i];

      if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
      {
        var end = text.IndexOf('\n', i);
        end = end < 0 ? text.Length : end;
        Blank(i, end);
        i = end;
        continue;
      }

      if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
      {
        var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
        end = end < 0 ? text.Length : end + 2;
        Blank(i, end);
        i = end;
        continue;
      }

      // Raw string literal: three or more quotes, closed by the same number.
      if (c == '"' && i + 2 < text.Length && text[i + 1] == '"' && text[i + 2] == '"')
      {
        var open = 0;
        while (i + open < text.Length && text[i + open] == '"')
        {
          open++;
        }

        var fence = new string('"', open);
        var end = text.IndexOf(fence, i + open, StringComparison.Ordinal);
        end = end < 0 ? text.Length : end + open;
        Blank(i, end);
        i = end;
        continue;
      }

      // Verbatim string: @"..." with "" as the escape for a quote.
      if (c == '@' && i + 1 < text.Length && text[i + 1] == '"')
      {
        var j = i + 2;
        while (j < text.Length)
        {
          if (text[j] == '"')
          {
            if (j + 1 < text.Length && text[j + 1] == '"')
            {
              j += 2;
              continue;
            }

            j++;
            break;
          }

          j++;
        }

        Blank(i, j);
        i = j;
        continue;
      }

      // Regular string or character literal, with backslash escapes.
      if (c == '"' || c == '\'')
      {
        var quote = c;
        var j = i + 1;
        while (j < text.Length && text[j] != quote && text[j] != '\n')
        {
          j += text[j] == '\\' ? 2 : 1;
        }

        j = Math.Min(j + 1, text.Length);
        Blank(i, j);
        i = j;
        continue;
      }

      i++;
    }

    return new string(buffer);
  }

  private static bool IsGenerated(string path) =>
    path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
    path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
}

using System.Text.RegularExpressions;

namespace Radio.Core.Tests;

/// <summary>
/// A regression lint over <c>src/Radio.Infrastructure/Audio/Sources/</c>: no <c>if</c> that encloses a
/// call to <c>_playbackService.StopAsync(...)</c> may test a private <c>bool</c> field of the same
/// class.
/// </summary>
/// <remarks>
/// <b>What it pins (PHN-10).</b> <c>AudioFileEventSource.StopCoreAsync</c> and
/// <c>DisposeAsyncCore</c> both read
/// <c>if (_playbackService != null &amp;&amp; _playbackId != null &amp;&amp; _isPlaybackActive)</c>.
/// <c>SoundFlowPlaybackService.StopAsync</c> is the only call that performs
/// <c>MasterMixer.RemoveComponent</c>, and <c>_isPlaybackActive</c> was already false every time
/// control reached either guard: <c>EventPlaybackService.TearDownAsync</c>'s first statement is
/// <c>playback.Cancel()</c>, and <c>PlayWithSoundFlowAsync</c>'s
/// <c>catch (OperationCanceledException)</c> clears the flag while stopping nothing. So the player was
/// never stopped, never detached and never disposed — by the Stop button, by preemption, by the
/// <c>MaxPlaybackSeconds</c> cap, or by anything else. <c>TTSEventSource</c> calls the same stop
/// unconditionally, which is why only one of the two event sources had the defect.
///
/// <b>Why a private bool specifically, rather than "any guard".</b> The two shipped forms of guard on
/// these call sites are a NULL check on a collaborator (<c>_playbackService != null</c>,
/// <c>_playbackId != null</c>) and a LIVE query on the service itself
/// (<c>_playbackService.IsPlaying(Id)</c>). Neither is a cached mirror of the service's own state, so
/// neither can go stale behind the caller's back. A private <c>bool</c> field is exactly the shape that
/// can — it is a second copy of a fact <c>SoundFlowPlaybackService._activePlayers</c> owns — and it is
/// the shape that shipped this bug. Forbidding all guards would forbid the null checks that keep these
/// call sites from throwing; forbidding this one shape is what the defect actually was.
///
/// ⚠⚠ <b>WHAT THIS TEST CANNOT DO.</b> It is a lint over source TEXT. It does not run any audio, does
/// not construct a source, and proves NOTHING about whether a player is detached from the mixer — that
/// needs a real device and is UAT (plan §2.2, §3 U1/U3/U5). Read a green run as "nobody re-typed the
/// shape that broke", not as "stops work".
///
/// The guards it cannot see, enumerated rather than gestured at — an incomplete list here is itself
/// the defect, because it is what a reader trusts when deciding whether green means anything:
/// <list type="bullet">
///   <item>a flag reaching the condition through a LOCAL (<c>var live = _isPlaybackActive;
///     if (live) …</c>), a PROPERTY, or a helper method — the condition text names an identifier the
///     lint does not recognise as the field;</item>
///   <item>an early <c>return</c> above the call instead of an enclosing <c>if</c>;</item>
///   <item>the brace-less body of an <c>else</c> (<c>if (a) x; else Stop();</c>) — see
///     <see cref="BracelessIfConditions"/>, which cuts at the <c>;</c>;</item>
///   <item>a stop reached through a differently-named playback-service field, or through a local
///     alias of it — <see cref="StopCall"/> matches the name <c>_playbackService</c> literally;</item>
///   <item>a bool AUTO-PROPERTY, or a bool field that is not underscore-prefixed —
///     <see cref="PrivateBoolField"/> matches the house field convention only.</item>
/// </list>
///
/// ⭐ Two gaps that WERE in this list and are now closed, recorded because both were found in
/// pre-merge review rather than by the author: a <b>brace-less <c>if</c></b> (<c>if (flag)
/// Stop();</c>) used to defeat the walk entirely — the shape most likely to reintroduce PHN-10 in a
/// one-line "quick fix", and already idiomatic in this directory — and a <b><c>this.</c>-qualified</b>
/// field reference used to read as a different object's field.
///
/// ⚠ <b>It lives in Radio.Core.Tests, not beside the code it scans</b>, for the reason every
/// source-scanning lint in this repository does: <see cref="RepositoryRoot"/> carries the
/// nested-checkout correctness this scan depends on and is <c>internal</c> to this assembly. See
/// <c>PlaybackKeyLintTests</c>' remarks — a second copy of that walker is the thing UI-7's extraction
/// existed to prevent.
///
/// ⚠ <b>Deliberately wider than the file that had the bug.</b> <c>PlaybackKeyLintTests</c> scans
/// <c>Primary/</c> only and exempts event sources on purpose (their minted id space is correct). This
/// lint scans ALL of <c>Sources/</c>, because the shape it forbids is wrong in a primary source for the
/// same reason it was wrong in an event source, and because the next source to reintroduce it is as
/// likely to be one as the other.
/// </remarks>
public class EventSourceStopIsNotActivityGuardedLintTests
{
  private const string SourcesRelativePath = "src/Radio.Infrastructure/Audio/Sources";

  /// <summary>
  /// <c>private|protected [readonly|volatile|static|internal] bool[?] _name[, _other];</c> — the
  /// field shape this lint forbids in a stop guard.
  /// </summary>
  /// <remarks>
  /// ⚠ <c>protected</c>, the nullable <c>bool?</c> and comma-separated declarator lists are all
  /// matched, and none of the three was in the first draft. A <c>protected</c> flag is if anything
  /// worse than a private one — a base class's stale activity mirror consulted by a derived
  /// <c>StopCoreAsync</c> is the same defect with a wider blast radius — and
  /// <c>private bool _a, _b;</c> would have hidden BOTH names from a per-name pattern. Widened in
  /// pre-merge review.
  ///
  /// ⚠ Still restricted to an UNDERSCORE-PREFIXED name, which is the house convention for instance
  /// fields, and to a field rather than an auto-property. Both misses are in the class remarks'
  /// cannot-do list.
  /// </remarks>
  private static readonly Regex PrivateBoolField = new(
    @"\b(?:private|protected)\s+(?:readonly\s+|volatile\s+|static\s+|protected\s+|internal\s+)*"
    + @"bool\??\s+(_\w+(?:\s*,\s*_\w+)*)\s*[;=]",
    RegexOptions.Compiled);

  /// <summary>An <c>if</c> keyword with its opening paren — the anchor for a brace-less guard.</summary>
  private static readonly Regex BracelessIf = new(@"\bif\s*\(", RegexOptions.Compiled);

  /// <summary>
  /// A call to <c>StopAsync</c> on the playback-service field, in the <c>.</c>, <c>?.</c> or
  /// <c>!.</c> form.
  /// </summary>
  /// <remarks>
  /// ⚠ The <c>!</c> arm is not decoration. <c>AudioFileEventSource.SeekCoreAsync</c> already writes
  /// <c>_playbackService!.Seek(…)</c>, so the null-forgiving form is house style in the very file
  /// that had this bug — and without it <c>_playbackService!.StopAsync(…)</c> matches NOTHING, the
  /// call site is never inspected, and the lint reports green on a guarded stop. Found by mutating
  /// the source to the brace-less shape and watching this lint pass for the wrong reason.
  /// </remarks>
  private static readonly Regex StopCall = new(
    @"_playbackService\s*[?!]?\s*\.\s*StopAsync\s*\(", RegexOptions.Compiled);

  [Fact]
  public void NoStopAsyncCallIsGuardedByAPrivateBoolField()
  {
    var (dir, files) = SourceFiles();

    var violations = new List<string>();
    var callsInspected = 0;
    var guardsExtracted = 0;
    var boolFieldsFound = 0;

    foreach (var file in files)
    {
      var raw = File.ReadAllText(file);

      // ⛔ Comments and string literals are blanked FIRST, and that is load-bearing rather than
      // tidiness. The enclosing-block walk counts braces, and this directory is full of interpolated
      // strings and format templates that contain them ($"audio-event-{Guid.NewGuid():N}",
      // "Removing {SourceName} (PlaybackId={PlaybackId})"). A stray unbalanced brace inside a comment
      // — including one inside the very comments this PR adds — would shift the depth count and make
      // the walk report a guard that is not there, or miss one that is.
      var code = BlankCommentsAndStrings(raw, file);

      // SelectMany over the declarator list, so `private bool _a, _b;` contributes both names.
      var boolFields = PrivateBoolField
        .Matches(code)
        .SelectMany(m => m.Groups[1].Value.Split(',').Select(n => n.Trim()))
        .Where(n => n.Length > 0)
        .ToHashSet(StringComparer.Ordinal);
      boolFieldsFound += boolFields.Count;

      foreach (Match call in StopCall.Matches(code))
      {
        callsInspected++;

        foreach (var (condition, conditionIndex) in EnclosingIfConditions(code, call.Index))
        {
          guardsExtracted++;

          var offending = boolFields
            .Where(f => ReferencesIdentifier(condition, f))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

          if (offending.Count == 0)
          {
            continue;
          }

          violations.Add(
            $"{Path.GetFileName(file)}:{LineOf(code, conditionIndex)} — "
            + $"if ({Collapse(condition)}) guards a StopAsync at line {LineOf(code, call.Index)}; "
            + $"tests private bool {string.Join(", ", offending)}");
        }
      }
    }

    // ⛔ THE VACUITY GUARDS, and there are three because this lint has three independent ways to go
    // blind. A regex that stops matching, a brace walk that stops finding enclosing blocks, and a
    // field pattern that stops recognising the declaration would each turn the assertion below into a
    // permanent silent pass. The floors sit under the counts MEASURED on this branch — 17 call sites,
    // 17 enclosing-if extractions, 11 private bool fields — so ordinary churn does not trip them,
    // while an instrument that has gone cold still cannot report green.
    Assert.True(
      callsInspected >= 13,
      $"Matched only {callsInspected} _playbackService.StopAsync call sites under '{dir}'. There were "
      + "17 when this lint was written. The regex has almost certainly stopped matching the code "
      + "rather than the code having shrunk — fix the pattern, do NOT lower this floor.");

    Assert.True(
      guardsExtracted >= 13,
      $"Extracted only {guardsExtracted} enclosing if-conditions around {callsInspected} StopAsync "
      + "call sites under '" + dir + "'. There were 17 when this lint was written — one per call site, "
      + "since every shipped stop is null-guarded. The brace walk has almost certainly stopped "
      + "finding enclosing blocks — fix EnclosingIfConditions, do NOT lower this floor.");

    Assert.True(
      boolFieldsFound >= 7,
      $"Found only {boolFieldsFound} private bool fields under '{dir}'. There were 11 when this lint "
      + "was written. The field pattern has almost certainly stopped matching — fix it, do NOT lower "
      + "this floor.");

    Assert.True(
      violations.Count == 0,
      "A stop must not be gated on a private bool field (PHN-10). SoundFlowPlaybackService.StopAsync "
      + "is the only call that detaches a player from the SoundFlow mixer, and a cached activity flag "
      + "is not a proxy for whether audio is reaching the speakers — EventPlaybackService.TearDownAsync "
      + "cancels the playback token before it calls stop, which is what clears such a flag. Guard on "
      + "null instead, or do not guard. "
      + $"Scanned '{dir}'. Offending guards:\n  " + string.Join("\n  ", violations));
  }

  /// <summary>
  /// Walks outward from <paramref name="index"/> through enclosing blocks, yielding the condition text
  /// of every enclosing <c>if</c> together with the index of that condition.
  /// </summary>
  /// <remarks>
  /// ⚠ <c>else if</c> is reported as <c>if</c> — the keyword scan reads the token immediately before
  /// the condition's open paren, and for <c>else if (...)</c> that token is <c>if</c>. Correct: an
  /// <c>else if</c> gating a stop is the same defect.
  ///
  /// ⚠ Bounded at eight levels. Nothing in this directory nests a stop that deeply, and an unbounded
  /// walk on a file whose braces do not balance (a truncated read, a generated file) would run to the
  /// start of the text on every call site.
  /// </remarks>
  private static IEnumerable<(string Condition, int Index)> EnclosingIfConditions(string code, int index)
  {
    // ⛔ BRACE-LESS ifs FIRST, and this is not an edge case in this directory. `if (cond) call;`
    // contributes NO opening brace, so the walk below steps straight past it to the enclosing
    // METHOD's brace and the condition is never inspected — a silent green on the exact one-line
    // shape most likely to reintroduce PHN-10. The style is already idiomatic here
    // (BluetoothAudioSource and USBAudioSourceBase both write `if (sourceId != _playbackId) return;`),
    // so nothing but this loop stops `if (_isPlaybackActive) await _playbackService.StopAsync(id);`
    // from passing. Caught in pre-merge review, not by the author.
    foreach (var found in BracelessIfConditions(code, index))
    {
      yield return found;
    }

    var position = index;

    for (var level = 0; level < 8; level++)
    {
      var open = EnclosingOpenBrace(code, position);
      if (open < 0)
      {
        yield break;
      }

      // Text immediately before the block's `{`. An `if` block looks like `…if (cond)\n{`; a method,
      // a `try`, a `lock`, a `using` and a `catch` all look like something else and are skipped
      // without being reported — the walk continues outward through them, which is how a stop inside
      // `if (…) { try { … } }` is still seen as guarded.
      var head = code[..open].TrimEnd();
      position = open;

      if (head.Length == 0 || head[^1] != ')')
      {
        continue;
      }

      var openParen = MatchingOpenParen(code, head.Length - 1);
      if (openParen < 0)
      {
        continue;
      }

      var keyword = code[..openParen].TrimEnd();
      if (!keyword.EndsWith("if", StringComparison.Ordinal))
      {
        continue;
      }

      // "if" must be a whole token, so `notif (` or `verify (` do not qualify.
      var before = keyword.Length - 2;
      if (before > 0 && (char.IsLetterOrDigit(keyword[before - 1]) || keyword[before - 1] == '_'))
      {
        continue;
      }

      yield return (code[(openParen + 1)..(head.Length - 1)], openParen);
    }
  }

  /// <summary>
  /// Conditions of every brace-less <c>if</c> whose single-statement body contains
  /// <paramref name="index"/>.
  /// </summary>
  /// <remarks>
  /// The statement containing the call starts after the nearest preceding <c>;</c>, <c>{</c> or
  /// <c>}</c>. Anything of the form <c>if (…)</c> between that point and the call, with no <c>;</c>
  /// or <c>{</c> after its closing paren, is a brace-less guard on this statement. Chained guards
  /// (<c>if (a) if (b) call;</c>) yield both.
  ///
  /// ⚠ Does NOT see an <c>else</c> branch's brace-less body (<c>if (a) x; else Stop();</c>) — the
  /// statement scan cuts at the <c>;</c> and the head is then just <c>else</c>. Listed in the class
  /// remarks rather than fixed: an <c>else</c>-only stop guard is a shape nothing in this repository
  /// writes, and a scan that walked past a <c>;</c> would start reading unrelated statements.
  /// </remarks>
  private static IEnumerable<(string Condition, int Index)> BracelessIfConditions(string code, int index)
  {
    var start = code.LastIndexOfAny([';', '{', '}'], Math.Max(index - 1, 0));
    var head = code[(start + 1)..index];

    foreach (Match candidate in BracelessIf.Matches(head))
    {
      var openParen = candidate.Index + candidate.Length - 1;
      var closeParen = MatchingCloseParen(head, openParen);
      if (closeParen < 0)
      {
        continue;
      }

      // Between the guard's `)` and the call there must be no statement terminator and no block
      // opener, or this `if` governs something else and merely happens to sit above the call.
      var between = head[(closeParen + 1)..];
      if (between.Contains(';', StringComparison.Ordinal)
        || between.Contains('{', StringComparison.Ordinal))
      {
        continue;
      }

      yield return (head[(openParen + 1)..closeParen], start + 1 + openParen);
    }
  }

  /// <summary>Index of the <c>)</c> matching the <c>(</c> at <paramref name="openIndex"/>, or -1.</summary>
  private static int MatchingCloseParen(string text, int openIndex)
  {
    var depth = 0;
    for (var i = openIndex; i < text.Length; i++)
    {
      if (text[i] == '(')
      {
        depth++;
      }
      else if (text[i] == ')')
      {
        depth--;
        if (depth == 0)
        {
          return i;
        }
      }
    }

    return -1;
  }

  /// <summary>
  /// Index of the <c>{</c> that opens the block containing <paramref name="index"/>, or -1.
  /// </summary>
  private static int EnclosingOpenBrace(string code, int index)
  {
    var depth = 0;
    for (var i = index - 1; i >= 0; i--)
    {
      if (code[i] == '}')
      {
        depth++;
      }
      else if (code[i] == '{')
      {
        if (depth == 0)
        {
          return i;
        }

        depth--;
      }
    }

    return -1;
  }

  /// <summary>Index of the <c>(</c> matching the <c>)</c> at <paramref name="closeIndex"/>, or -1.</summary>
  private static int MatchingOpenParen(string code, int closeIndex)
  {
    var depth = 0;
    for (var i = closeIndex; i >= 0; i--)
    {
      if (code[i] == ')')
      {
        depth++;
      }
      else if (code[i] == '(')
      {
        depth--;
        if (depth == 0)
        {
          return i;
        }
      }
    }

    return -1;
  }

  /// <summary>
  /// True when <paramref name="condition"/> uses <paramref name="identifier"/> as a whole token,
  /// bare or <c>this.</c>-qualified.
  /// </summary>
  /// <remarks>
  /// ⚠ The <c>.</c> in the negative lookbehind is what stops <c>other._isPlaybackActive</c> — a
  /// DIFFERENT object's field — from being read as this class's. That same exclusion also rejected
  /// <c>this._isPlaybackActive</c>, which IS this class's field, so the explicit <c>this.</c> arm is
  /// spelled out. No <c>this.</c>-qualified field access exists anywhere in
  /// <c>src/Radio.Infrastructure</c> today; the arm is here so a house-style change cannot silently
  /// blind the lint. Caught in pre-merge review.
  /// </remarks>
  private static bool ReferencesIdentifier(string condition, string identifier)
  {
    var name = Regex.Escape(identifier);
    return Regex.IsMatch(condition, $@"(?<![\w.]){name}\b")
      || Regex.IsMatch(condition, $@"(?<!\w)this\s*\.\s*{name}\b");
  }

  /// <summary>
  /// Replaces the contents of comments and string literals with spaces, preserving length and every
  /// newline so indices and line numbers stay those of the original text.
  /// </summary>
  /// <remarks>
  /// ⚠ Not a C# lexer, and it does not need to be — it needs to make brace and paren counting safe.
  /// Verbatim (<c>@"…"</c>), interpolated (<c>$"…"</c>) and raw (<c>"""…"""</c>) strings are all
  /// blanked WHOLE, holes included: an interpolated hole contains real code, so blanking it loses a
  /// theoretical call site rather than corrupting the depth count. There are no StopAsync calls
  /// inside interpolated holes in this repository, and a lint that under-reports one call site is
  /// caught by the vacuity floor above.
  ///
  /// ⛔ <b>An unterminated literal THROWS rather than blanking to the end of the file, and that is
  /// the important line in this method.</b> The quote scan is a naive toggle with no model of
  /// interpolation-hole nesting, so a hole containing an ODD number of quotes
  /// (<c>$"{x ?? "a\"b"}"</c>) desyncs it — and the failure mode without this guard is that the
  /// rest of the file is blanked to spaces, erasing real call sites and real guards from a scan that
  /// then reports green. No such construct exists under <c>Audio/Sources/</c> today; this turns a
  /// silent catastrophic false pass into a loud one if that ever changes. Caught in pre-merge review.
  /// </remarks>
  private static string BlankCommentsAndStrings(string text, string file)
  {
    var buffer = text.ToCharArray();
    var i = 0;

    while (i < buffer.Length)
    {
      var c = buffer[i];

      if (c == '/' && i + 1 < buffer.Length && buffer[i + 1] == '/')
      {
        while (i < buffer.Length && buffer[i] != '\n')
        {
          buffer[i++] = ' ';
        }
      }
      else if (c == '/' && i + 1 < buffer.Length && buffer[i + 1] == '*')
      {
        while (i < buffer.Length)
        {
          var end = buffer[i] == '*' && i + 1 < buffer.Length && buffer[i + 1] == '/';
          if (buffer[i] != '\n')
          {
            buffer[i] = ' ';
          }

          i++;
          if (end)
          {
            if (i < buffer.Length)
            {
              buffer[i++] = ' ';
            }

            break;
          }
        }
      }
      else if (c == '"')
      {
        i = Terminated(BlankString(buffer, i), file, i, "string");
      }
      else if (c == '\'')
      {
        i = Terminated(BlankChar(buffer, i), file, i, "character");
      }
      else
      {
        i++;
      }
    }

    return new string(buffer);
  }

  /// <summary>
  /// Fails loudly when a literal scan ran off the end of the file, which means the scanner desynced
  /// rather than that the source is malformed — every file it reads compiles.
  /// </summary>
  private static int Terminated(int result, string file, int start, string kind)
  {
    Assert.True(
      result >= 0,
      $"Scanner desync in '{Path.GetFileName(file)}': an unterminated {kind} literal starting at "
      + $"offset {start}. BlankCommentsAndStrings is a naive quote toggle with no model of "
      + "interpolation-hole nesting, so a hole containing an odd number of quotes desyncs it and "
      + "would otherwise blank the rest of the file — erasing real call sites from a scan that then "
      + "reports green. Teach the scanner that construct; do NOT relax this check.");

    return result;
  }

  /// <summary>
  /// Blanks the string literal starting at the quote at <paramref name="start"/>. Returns -1 when
  /// the literal is never closed.
  /// </summary>
  private static int BlankString(char[] buffer, int start)
  {
    // A raw string literal opens with three or more quotes and closes with the same run length.
    var quotes = 0;
    while (start + quotes < buffer.Length && buffer[start + quotes] == '"')
    {
      quotes++;
    }

    if (quotes >= 3)
    {
      var i = start + quotes;
      Blank(buffer, start, i);
      while (i < buffer.Length)
      {
        var run = 0;
        while (i + run < buffer.Length && buffer[i + run] == '"')
        {
          run++;
        }

        if (run >= quotes)
        {
          Blank(buffer, i, i + run);
          return i + run;
        }

        if (buffer[i] != '\n')
        {
          buffer[i] = ' ';
        }

        i++;
      }

      return -1;
    }

    // Verbatim strings escape a quote by doubling it; ordinary ones by a backslash. Blanking to the
    // first unescaped quote handles both without needing to know which kind this is, because a
    // doubled quote is "close then immediately reopen" — the second quote starts a new literal whose
    // content is blanked by the next iteration of the caller's loop.
    var j = start + 1;
    buffer[start] = ' ';
    while (j < buffer.Length)
    {
      if (buffer[j] == '\\' && j + 1 < buffer.Length)
      {
        Blank(buffer, j, j + 2);
        j += 2;
        continue;
      }

      if (buffer[j] == '"')
      {
        buffer[j] = ' ';
        return j + 1;
      }

      if (buffer[j] != '\n')
      {
        buffer[j] = ' ';
      }

      j++;
    }

    return -1;
  }

  /// <summary>
  /// Blanks the character literal starting at <paramref name="start"/>. Returns -1 when the literal
  /// is never closed.
  /// </summary>
  private static int BlankChar(char[] buffer, int start)
  {
    var j = start + 1;
    buffer[start] = ' ';
    while (j < buffer.Length)
    {
      if (buffer[j] == '\\' && j + 1 < buffer.Length)
      {
        Blank(buffer, j, j + 2);
        j += 2;
        continue;
      }

      if (buffer[j] == '\'')
      {
        buffer[j] = ' ';
        return j + 1;
      }

      if (buffer[j] != '\n')
      {
        buffer[j] = ' ';
      }

      j++;
    }

    return -1;
  }

  private static void Blank(char[] buffer, int from, int to)
  {
    for (var i = from; i < Math.Min(to, buffer.Length); i++)
    {
      if (buffer[i] != '\n')
      {
        buffer[i] = ' ';
      }
    }
  }

  private static int LineOf(string text, int index) =>
    text.Take(index).Count(c => c == '\n') + 1;

  private static string Collapse(string condition) =>
    Regex.Replace(condition, @"\s+", " ").Trim();

  /// <summary>
  /// Resolves the audio-sources directory and its <c>.cs</c> files, failing loudly if either is
  /// missing.
  /// </summary>
  /// <remarks>
  /// ⚠ The resolved directory is returned so every failure message can name it. Violations are
  /// reported by BARE FILENAME, which is identical in every checkout of this repository — so a scan
  /// of the wrong tree reads exactly like a real finding. <see cref="RepositoryRoot.Find"/> carries
  /// the worktree remarks explaining that this has actually happened here.
  /// </remarks>
  private static (string Directory, List<string> Files) SourceFiles()
  {
    var root = RepositoryRoot.Find();
    var dir = Path.Combine(root, SourcesRelativePath.Replace('/', Path.DirectorySeparatorChar));

    Assert.True(
      Directory.Exists(dir),
      $"Expected the audio sources at '{dir}'. Repository root resolved to '{root}'.");

    var files = Directory
      .EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
      .OrderBy(f => f, StringComparer.Ordinal)
      .ToList();

    Assert.True(
      files.Count >= 12,
      $"Only {files.Count} source files found under '{dir}'; there were 15 when this lint was "
      + "written. The scan is looking at the wrong place.");

    return (dir, files);
  }
}

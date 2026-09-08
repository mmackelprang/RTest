using System.Text.RegularExpressions;

namespace Radio.Core.Tests;

/// <summary>
/// A regression lint over <c>src/**/*.cs</c> asserting that every Kind-C and Kind-D test seam
/// carries the complete label required by <c>design/TESTING.md</c> § <i>Test Seams</i> (ADR-030):
/// the kind, the mechanism that makes the real path unreachable, and — the clause the convention
/// exists for — what the seam consequently does not cover.
/// </summary>
/// <remarks>
/// ⚠⚠ <b>WHAT THIS TEST CANNOT DO, AND IT MATTERS MORE THAN WHAT IT CAN.</b>
/// <b>This is a regression lint over the seams that exist. It is NOT a proof that every seam is
/// labelled.</b> It finds an <c>internal</c> member only when the member's own name ends in
/// <c>ForTest</c>/<c>ForTests</c>/<c>ForTesting</c>, or when the XML doc block immediately above it
/// mentions a test in one of four spellings. <b>A new seam with no doc comment and an ordinary name
/// matches no trigger and passes silently</b> — which is exactly the shape of the seam that motivated
/// ADR-030 in the first place (<c>ApplyDeferredCaptureState</c> carried no suffix). Catching a wholly
/// new unlabelled seam is a code-review responsibility, not this file's.
///
/// A lint that overstates its reach is the same defect class as an over-claiming comment, and that
/// defect is what <c>TEST-2</c> was filed on top of. So, precisely:
///
/// <list type="bullet">
/// <item>⚠⚠ <b>THE REAL ESCAPE HATCH IS OMITTING THE LABEL, NOT EVADING THE SCANNER.</b> A member
/// this file DOES match but whose doc block carries no kind letter is skipped outright — the
/// <c>kind is not ('C' or 'D')</c> test at the top of the loop <c>continue</c>s on <c>'?'</c> exactly
/// as it does on <c>'A'</c>. So an author who writes <i>"test seam"</i> and stops has silently opted
/// out, in one line, with no name change and no missing doc. <b>This is the common state, not a
/// corner case: 21 of the 23 members currently matched carry no kind letter</b> — every one except
/// the two labelled Cast seams. Verified by re-planting <c>ApplyDeferredCaptureState</c> with its
/// original doc comment: the scanner found it (that comment says <i>"for unit testing"</i> and
/// <i>"InternalsVisibleTo"</i>) and the lint stayed GREEN, against the very seam ADR-030 was written
/// about. Closing that would mean classifying seams rather than reading a label, which ADR-030
/// deliberately leaves to the author; the enforcement is code review.</item>
/// <item><b>It scans <c>.cs</c> only.</b> <c>src/Radio.Web/Components/Shared/NowPlayingPanel.razor</c>
/// holds a Kind-C seam this file never reads — <c>Clock</c> (<c>:414</c>), read by production at
/// <c>:934</c> and <c>:1107</c> — alongside the Kind-A <c>SourceGainDebounce</c> (<c>:421</c>).
/// Razor is not parsed here, so the one member in that file the convention says needs a FULL label
/// is the one member the lint cannot see.</item>
/// <item><b>It parses C# by line shape, not with a lexer.</b> A member declaration is recognised by
/// its leading modifiers; the doc block is whatever contiguous run of <c>///</c> lines sits above it,
/// past any attributes. A declaration split so that <c>internal</c> and the member name land on
/// different lines is invisible. Nothing in the tree does that today.</item>
/// <item><b>It does not check that a label is TRUE.</b> It checks that the two clauses are present.
/// "Why the real path is unreachable" is a claim about a mechanism and only a reviewer can falsify
/// it — see <c>CLAUDE.md</c> § <i>Pre-Merge Review</i>, whose fourth worked example is a seam
/// justification that was false for four weeks while looking entirely plausible.</item>
/// <item><b>It does not classify.</b> A Kind-C seam mislabelled <c>kind A</c> passes, because the
/// kind is read from the label rather than derived from the code. Picking the kind is the author's
/// job; the lint only holds them to what the kind they picked requires.</item>
/// <item>⚠ <b>Type declarations are skipped</b> — <c>internal class</c>, <c>record</c>, <c>struct</c>,
/// <c>interface</c>, <c>enum</c> and <c>delegate</c>. An internal test-support TYPE would not be
/// found.</item>
/// </list>
///
/// ⭐ <b>How a zero-violations lint proves it is looking.</b> Two assertions that fail in opposite
/// directions, because "no violations found" is otherwise indistinguishable from "scanned nothing":
/// numeric floors on the files and members reached, and a POSITIVE CONTROL naming real seams and the
/// kind letter each must be found with. No single breakage — wrong root, broken extractor, changed
/// doc format, renamed member — leaves this test green.
///
/// ⚠⚠ <b>THERE IS NO LIVE KIND-D SEAM IN THE TREE, AND THE CONTROL LIST DOES NOT PRETEND OTHERWISE.</b>
/// <c>TEST-2</c> retired the only one (<c>BluetoothAudioSource.ApplyDeferredCaptureState</c>) rather
/// than labelling it, so <b>the <c>kind D</c> branch of the matcher is exercised by nothing in
/// <c>src/</c></b> — it was verified by temporarily planting a Kind-D label with a missing clause and
/// confirming this test went red, and that is the only evidence that branch works. A future reader
/// must not take a green run as covering it. <c>CastStatusReadOverrideForTests</c> is likewise absent
/// because <c>AUD-5</c> has not shipped it; when it does, it must carry the kind-C label and join the
/// control list below (plan §5.3).
/// </remarks>
public class TestSeamLabelLintTests
{
  /// <summary>
  /// A member declaration whose access is <c>internal</c>, recognised by leading modifiers.
  /// Deliberately permissive about modifier order (<c>static internal</c> is legal C#) and about
  /// what follows, because the member NAME is extracted separately.
  /// </summary>
  private static readonly Regex InternalMember = new(
    @"^\s*(?:(?:public|protected|private|static|readonly|virtual|override|sealed|abstract|new|"
    + @"unsafe|extern|partial|async|volatile|required|const)\s+)*internal\s+",
    RegexOptions.Compiled);

  /// <summary>Type declarations are out of scope; see the class remarks.</summary>
  private static readonly Regex TypeDeclaration = new(
    @"\b(?:class|interface|struct|record|enum|delegate)\b", RegexOptions.Compiled);

  /// <summary>The <c>*ForTests</c> family, which is a trigger on its own with no doc comment.</summary>
  private static readonly Regex TestSuffix = new(
    @"(?:ForTest|ForTests|ForTesting)$", RegexOptions.Compiled);

  /// <summary>
  /// Reads the kind letter out of a label. Tolerant of the surrounding markup — the shipped labels
  /// wrap it in <c>&lt;b&gt;</c> — and of spacing, but the words themselves must be there.
  /// </summary>
  private static readonly Regex KindLabel = new(
    @"Test\s+seam\s*\(\s*kind\s+([A-D])\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

  private static readonly Regex GenericArguments = new(@"<[^<>]*>", RegexOptions.Compiled);

  private static readonly Regex Identifier = new(@"[A-Za-z_]\w*", RegexOptions.Compiled);

  /// <summary>
  /// The four spellings by which a doc comment admits its member exists for a test. Case-insensitive
  /// on purpose: these are prose, not identifiers.
  /// </summary>
  private static readonly string[] DocTriggers =
  [
    "internalsvisibleto",
    "test seam",
    "for unit testing",
    "test-only",
  ];

  private const string WhyClause = "why the real path is unreachable:";
  private const string NotCoveredClause = "not covered by this seam:";

  /// <summary>One test seam found in the tree.</summary>
  /// <param name="Member">The member's name, e.g. <c>ConnectRaceHookForTests</c>.</param>
  /// <param name="Kind">The letter from its label, or <c>'?'</c> when it carries none.</param>
  /// <param name="File">Path relative to <c>src</c>.</param>
  /// <param name="Line">1-based line of the declaration.</param>
  private sealed record Seam(string Member, char Kind, string File, int Line);

  [Fact]
  public void EveryKindCAndKindDSeamCarriesACompleteLabel()
  {
    var root = FindRepositoryRoot();
    var src = Path.Combine(root, "src");

    // The scan itself must be provably alive. A lint that quietly matches nothing — wrong root,
    // broken extractor, changed layout — is a test that passes against a broken implementation,
    // which is the failure mode ADR-030 exists to name.
    Assert.True(Directory.Exists(src), $"Expected a source tree at '{src}'.");

    var files = Directory
      .EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
      .Where(f => !IsGenerated(f))
      .ToList();

    var found = new List<Seam>();
    var violations = new List<string>();

    foreach (var file in files)
    {
      var lines = File.ReadAllLines(file);
      var relative = Path.GetRelativePath(src, file);

      for (var i = 0; i < lines.Length; i++)
      {
        if (!InternalMember.IsMatch(lines[i]) || TypeDeclaration.IsMatch(lines[i]))
        {
          continue;
        }

        var member = MemberName(lines[i]);
        if (member is null)
        {
          continue;
        }

        var doc = DocBlockAbove(lines, i);
        var lowered = doc.ToLowerInvariant();
        var triggered = TestSuffix.IsMatch(member) || DocTriggers.Any(lowered.Contains);
        if (!triggered)
        {
          continue;
        }

        var kindMatch = KindLabel.Match(doc);
        var kind = kindMatch.Success ? char.ToUpperInvariant(kindMatch.Groups[1].Value[0]) : '?';
        found.Add(new Seam(member, kind, relative, i + 1));

        if (kind is not ('C' or 'D'))
        {
          continue;
        }

        var missing = new List<string>();
        if (!lowered.Contains(WhyClause, StringComparison.Ordinal))
        {
          missing.Add("'Why the real path is unreachable:'");
        }

        if (!lowered.Contains(NotCoveredClause, StringComparison.Ordinal))
        {
          missing.Add("'NOT covered by this seam:'");
        }

        if (missing.Count > 0)
        {
          violations.Add(
            $"{relative}:{i + 1} '{member}' is labelled kind {kind} but its doc block is missing "
            + string.Join(" and ", missing));
        }
      }
    }

    // Floors: prove the scan ran at all. Not exact counts — they exist to catch "scanned nothing",
    // the failure mode a zero-violation lint cannot otherwise distinguish from success. Measured on
    // this branch after Task 6: 452 source files and 23 matched seam members. Both floors are set
    // comfortably below, so ordinary churn does not trip them and a broken scan does.
    Assert.True(files.Count > 200, $"Only {files.Count} source files found under '{src}'.");
    Assert.True(
      found.Count >= 12,
      $"Only {found.Count} test-seam members matched under '{src}' — the extractor is broken.");

    // POSITIVE CONTROL: prove the scan still recognises the seams this lint exists for.
    // A floor proves the scanner ran; only a named control proves it still finds THESE.
    // If a seam is renamed or its label deleted, this fails loudly instead of the lint
    // quietly guarding nothing.
    foreach (var (member, kind) in new[]
             {
               ("ConnectRaceHookForTests", 'C'),
               ("ConnectTransportOverrideForTests", 'C'),
             })
    {
      var hit = found.SingleOrDefault(f => f.Member == member);
      Assert.True(hit is not null,
        $"Positive control '{member}' was not found under '{src}'. Either it was renamed or removed "
        + "(update this control and design/TESTING.md), or the scanner no longer recognises "
        + "it — in which case every green run of this lint since the change was meaningless. "
        + $"Members matched: {(found.Count == 0 ? "(none)" : string.Join(", ", found.Select(f => f.Member)))}");
      Assert.Equal(kind, hit!.Kind);
    }

    // ⚠ The resolved root goes in the failure message, always. Violations are reported as paths
    // RELATIVE to src, which are identical in every checkout of this repository — so a scan of the
    // wrong tree reads exactly like a real finding. See FindRepositoryRoot: this has happened.
    Assert.True(
      violations.Count == 0,
      "ADR-030: a kind-C or kind-D test seam must say what it displaces. Add the missing clause(s) "
      + "to the member's XML doc, verbatim in shape — see design/TESTING.md § Test Seams. "
      + $"Scanned '{src}'.\n  " + string.Join("\n  ", violations));
  }

  /// <summary>
  /// Extracts the declared member's name from <paramref name="declaration"/>: everything up to the
  /// first <c>(</c>, <c>{</c>, <c>=</c> or <c>;</c>, with generic argument lists removed, then the
  /// last identifier in what remains.
  /// </summary>
  /// <remarks>
  /// The boundary search is what makes this work on the shapes actually in the tree — a method
  /// (<c>(</c>), an auto-property (<c>{</c>), an expression-bodied property (<c>=&gt;</c>, caught by
  /// <c>=</c>), a field-like event (<c>;</c>) and a const (<c>=</c>). Stripping generics matters for
  /// two of them in opposite directions: without it <c>internal void Foo&lt;T&gt;(…)</c> yields
  /// <c>T</c>, and <c>internal Func&lt;Task&gt;? ConnectRaceHookForTests</c> is unaffected either way.
  /// </remarks>
  private static string? MemberName(string declaration)
  {
    var boundary = declaration.Length;
    foreach (var c in new[] { '(', '{', '=', ';' })
    {
      var index = declaration.IndexOf(c);
      if (index >= 0 && index < boundary)
      {
        boundary = index;
      }
    }

    var prefix = declaration[..boundary];
    string previous;
    do
    {
      previous = prefix;
      prefix = GenericArguments.Replace(prefix, string.Empty);
    }
    while (prefix != previous);

    var identifiers = Identifier.Matches(prefix);
    return identifiers.Count == 0 ? null : identifiers[^1].Value;
  }

  /// <summary>
  /// Returns the contiguous run of <c>///</c> lines immediately above <paramref name="index"/>,
  /// joined and whitespace-normalized, skipping any attribute lines in between.
  /// </summary>
  /// <remarks>
  /// Normalizing before matching is load-bearing, not tidiness: the label's clauses are prose that
  /// wraps, so <c>NOT covered by this\n/// seam:</c> is one clause split over two lines and a
  /// line-at-a-time search would not find it.
  /// </remarks>
  private static string DocBlockAbove(string[] lines, int index)
  {
    var i = index - 1;
    while (i >= 0 && lines[i].TrimStart().StartsWith('['))
    {
      i--;
    }

    var block = new List<string>();
    while (i >= 0)
    {
      var trimmed = lines[i].TrimStart();
      if (!trimmed.StartsWith("///", StringComparison.Ordinal))
      {
        break;
      }

      block.Add(trimmed[3..]);
      i--;
    }

    block.Reverse();
    return string.Join(
      " ",
      string.Join(" ", block).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
  }

  private static bool IsGenerated(string path) =>
    path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
    path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

  /// <summary>
  /// Walks up from the test binary looking for the solution file, and returns the OUTERMOST
  /// directory that has one.
  /// </summary>
  /// <remarks>
  /// ⚠ <b>Fails loudly rather than skipping.</b> A source-scanning lint that no-ops when it cannot
  /// find the source tree is worse than no lint: it reports green forever from any unexpected
  /// working directory, and nobody notices because green is what it always says.
  ///
  /// ⚠⚠ <b>AND IT MUST NOT RESOLVE TO A NESTED CHECKOUT.</b> Copied deliberately from
  /// <see cref="LogSafetyLintTests"/>, worktree exclusion and all, rather than the simpler variant in
  /// <c>NoRawMiniAudioEngineConstructionTests</c> — a reviewer has already watched that shape
  /// silently scan a stale nested checkout and report its pre-fix source as a live violation.
  /// <c>.claude/worktrees/</c> holds complete checkouts of this repository at other commits, each with
  /// its own <c>RadioConsole.sln</c>, and paths relative to <c>src</c> are identical in every one.
  ///
  /// The rule is "outermost, preferring a directory that is not under a worktree", not "first hit".
  /// ⚠ The preference is a preference and NOT a filter: this repository parks worktrees under a
  /// directory literally named <c>worktrees</c>, so such a checkout is its own only candidate AND
  /// matches the exclusion, and a filter would empty the list and fail the run.
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
    var root = candidates.LastOrDefault(c => !IsInsideAWorktree(c)) ?? candidates.LastOrDefault();

    Assert.True(
      root is not null,
      "TestSeamLabelLintTests could not settle on a repository root by walking up from " +
      $"'{AppContext.BaseDirectory}'. This lint scans src/**/*.cs, so without the root it cannot " +
      "run — and it must FAIL rather than silently pass, or scan the wrong tree. " +
      $"Solution files found: {(candidates.Count == 0 ? "(none)" : string.Join(", ", candidates))}." +
      "\nDirectories probed:\n  " + string.Join("\n  ", probed));

    return root!;
  }

  /// <summary>
  /// True when <paramref name="path"/> has a <c>worktrees</c> segment, i.e. it is a checkout parked
  /// inside another repository rather than the repository. Matched on whole path SEGMENTS rather
  /// than as a substring, and case-insensitively — these are Windows paths as often as not.
  /// </summary>
  private static bool IsInsideAWorktree(string path)
  {
    var segments = path.Split(
      [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
      StringSplitOptions.RemoveEmptyEntries);

    return segments.Any(s => s.Equals("worktrees", StringComparison.OrdinalIgnoreCase));
  }
}

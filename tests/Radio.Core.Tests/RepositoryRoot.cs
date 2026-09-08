namespace Radio.Core.Tests;

/// <summary>
/// Resolves the repository root for the source-scanning lints in this assembly.
/// </summary>
/// <remarks>
/// ⚠ EXTRACTED IN UI-7, and the extraction is the point rather than tidiness. This method was
/// private to LogSafetyLintTests, so the second source-scanning lint in this assembly
/// (AsyncEventFanOutLintTests) could not call it and the path of least resistance was a copy. Two
/// copies is two chances to diverge, and the pre-PHN-5 version of this method — LastOrDefault with
/// no fallback — FAILED in any checkout under a directory named "worktrees", which is the
/// convention this repository uses. A copy made from memory would very likely be that version.
/// One implementation, used by every lint.
///
/// ⛔ NO BEHAVIOUR CHANGE was intended or made in the move. The one edit to the moved text is the
/// assertion message, which named LogSafetyLintTests and now names the caller-agnostic helper —
/// leaving it would have made the message assert something false as soon as the second lint used
/// it, which is the CLAUDE.md § Pre-Merge Review failure mode this repository keeps hitting.
/// </remarks>
internal static class RepositoryRoot
{
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
  /// during TTS-11/PHN-5's review.</b> A reviewer saw that lint fail twice reporting the PRE-FIX
  /// text of <c>SourcesController.cs:646</c>, then pass six times with no change in between.
  /// <c>.claude/worktrees/</c> holds complete checkouts of this repository at other commits, each
  /// with its own <c>RadioConsole.sln</c>. A run whose binary sits inside one of those would find
  /// that solution first and scan THAT commit's source — while printing paths relative to
  /// <c>src</c>, which are identical in every checkout. The output is indistinguishable from a
  /// real violation, which is the exact failure class these lints exist to guard against: a test
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
  public static string Find()
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
      "RepositoryRoot.Find() could not settle on a repository root by walking up from " +
      $"'{AppContext.BaseDirectory}'. The lints that call it scan src/, so without the root they " +
      "cannot run — and they must FAIL rather than silently pass, or scan the wrong tree. " +
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
}

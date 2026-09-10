using System.Text.RegularExpressions;

namespace Radio.Core.Tests;

/// <summary>
/// AUD-24. <c>FilePlayerAudioSource.SeekCoreAsync</c> shipped for the life of the project assigning
/// <c>_position</c> and returning — no engine call, under a log line that said
/// <c>"Seeked to {Position}"</c>. The audible half cannot be asserted in a device-free process (see
/// the plan § 0.5 C-1), so this pins the two structural facts that made the defect possible: the
/// seek path must ask the playback service to move the player, and the reported position must not
/// advance on a path the player refused.
/// </summary>
/// <remarks>
/// ⚠⚠ <b>WHAT THIS CANNOT DO.</b> It is a lint over source TEXT. It runs no audio, constructs no
/// source, and proves NOTHING about whether a player repositioned — that needs a real device and is
/// owner UAT (plan § 3 U1/U2). Both tests would stay green if the call passed the wrong position or
/// the wrong id. Read green as "nobody re-typed the shape that broke", not as "seek works".
///
/// ⚠ <b>It is LINE-based, and deliberately so.</b> The house scanner in
/// <c>EventSourceStopIsNotActivityGuardedLintTests</c> blanks comments and string literals before
/// counting braces, because its brace walk is corrupted by a <c>{</c> inside a log template. This
/// lint needs no brace walk: it locates the method body by the house 2-space indentation, drops
/// comment lines by prefix, and then matches whole statements. That avoids a second copy of a
/// ~90-line scanner whose subtleties (raw strings, interpolation-hole desync) it would not need —
/// the duplication UI-7's <see cref="RepositoryRoot"/> extraction exists to prevent.
///
/// What the line-based approach cannot see, enumerated rather than gestured at:
/// <list type="bullet">
///   <item>a <c>return</c> or an assignment written on the same line as something else
///     (<c>if (!moved) return Task.CompletedTask;</c> IS seen — the trimmed line starts with
///     <c>if</c>, not <c>return</c> — so a brace-less early return would read as absent and fail
///     LOUDLY rather than silently. That is the safe direction);</item>
///   <item>the position being moved through a helper, a property, or a differently-spelled
///     assignment (<c>_position = position.Add(TimeSpan.Zero)</c>);</item>
///   <item>a seek reached through a local alias of the service field — the pattern names
///     <c>_playbackService</c> literally;</item>
///   <item>a method body whose closing brace is not at the house two-space indent, which makes
///     <see cref="SeekCoreBody"/> fail its own assertion rather than scan the wrong region;</item>
///   <item>a branch on the returned bool that is written as a ternary, a <c>switch</c>, or a
///     <c>while</c> rather than an <c>if</c> — the guard scan matches <c>if (moved)</c> /
///     <c>if (!moved)</c> only, so a correct method written another way fails LOUDLY and must
///     update this test. That is the safe direction;</item>
///   <item>a seek whose bool is captured and then branched on in a HELPER, so the branch text
///     never appears in this body.</item>
/// </list>
///
/// ⭐ <b>The guard property was tightened in pre-merge review, and the reason is worth keeping.</b>
/// It first asserted merely that a <c>return</c> appeared between the engine call and each
/// <c>_position</c> assignment. That is disarmed by ANY unrelated early return after the call — a
/// cancellation check, a null check — after which an unconditional assignment sits below it and the
/// lint stays green through the exact defect it pins. It now requires a branch on the bool the
/// engine returned, which is the fact that actually licenses the assignment.
///
/// ⭐ <b>The second test is NOT the one the plan specified, and the difference is measured.</b> The
/// plan's version compared counts — <c>Regex.Matches(body, "_position = position;").Count</c> against
/// <c>Regex.Matches(body, @"\bif\s*\(").Count</c> — and asserted <c>decisions &gt;= assignments</c>.
/// The shipped method has four <c>if</c>s and two assignments, so hoisting an assignment above the
/// refusal guard leaves 4 &gt;= 2 and the test stays GREEN through the exact mutation the plan named
/// as its red. Replaced with an ordering property that does red on it: every assignment after the
/// engine call must have a <c>return</c> between it and that call. Same failure family as the hole
/// PHN-10's review found in its own lint.
/// </remarks>
public class FilePlayerSeekReachesTheEngineLintTests
{
  private const string SourceRelativePath =
    "src/Radio.Infrastructure/Audio/Sources/Primary/FilePlayerAudioSource.cs";

  /// <summary>A call to <c>Seek</c> on the playback-service field, in the <c>.</c>, <c>?.</c> or <c>!.</c> form.</summary>
  private static readonly Regex SeekCall = new(
    @"_playbackService\s*[?!]?\s*\.\s*Seek\s*\(", RegexOptions.Compiled);

  [Fact]
  public void SeekCoreAsync_AsksThePlaybackServiceToMoveThePlayer()
  {
    var body = SeekCoreBody();

    Assert.True(
      body.Any(line => SeekCall.IsMatch(line)),
      "FilePlayerAudioSource.SeekCoreAsync must call _playbackService.Seek(...). Without it the "
      + "method moves the reported position and no audio, which is AUD-24 exactly. If you are "
      + "deliberately replacing the mechanism (ADR-029 § 14 Q3 licenses stop-and-restart-at-offset "
      + "as a fallback), update this test and say which mechanism replaced it.\nBody scanned:\n  "
      + string.Join("\n  ", body));
  }

  [Fact]
  public void SeekCoreAsync_DoesNotAdvanceTheReportedPositionOnARefusedSeek()
  {
    var body = SeekCoreBody();

    var seekLine = body
      .Select((line, index) => (line, index))
      .Where(x => SeekCall.IsMatch(x.line))
      .Select(x => (int?)x.index)
      .FirstOrDefault();

    Assert.True(
      seekLine.HasValue,
      "No _playbackService.Seek(...) call in SeekCoreAsync, so this test cannot check what happens "
      + "after one. See SeekCoreAsync_AsksThePlaybackServiceToMoveThePlayer.");

    // Assignments BEFORE the engine call are fine and expected — the no-playback-service arm moves
    // the field because nothing can contradict it. It is the ones after that must be earned.
    var afterTheCall = Enumerable
      .Range(seekLine!.Value + 1, body.Count - seekLine.Value - 1)
      .Where(i => body[i].Trim() == "_position = position;")
      .ToList();

    // ⛔ Vacuity guard. With no assignment after the call, the seek would move the player and never
    // record where it went — the mirror image of AUD-24, and it would also make the loop below
    // pass by having nothing to check.
    Assert.True(
      afterTheCall.Count > 0,
      "SeekCoreAsync calls the playback service but never assigns _position afterwards, so a "
      + "successful seek would reposition the audio and leave the readout behind. Either this "
      + "lint has stopped recognising the assignment, or the success path stopped recording it.");

    // ⛔ The property is "a branch on the value the engine RETURNED", not "a return exists above".
    // An earlier revision asserted the latter, and pre-merge review showed it is disarmed by any
    // unrelated early return after the call —
    //     var moved = _playbackService.Seek(Id, position);
    //     if (cancellationToken.IsCancellationRequested) { return Task.CompletedTask; }
    //     _position = position;                      // unconditional. Lint would stay GREEN.
    // — which is the defect wearing one extra statement. Binding to the returned bool closes it and
    // accepts both correct shapes (an early return on !moved, or `if (moved) { … }`).
    var result = Regex.Match(body[seekLine.Value], @"\b(?:var|bool)\s+(\w+)\s*=\s*_playbackService");

    Assert.True(
      result.Success,
      "SeekCoreAsync calls _playbackService.Seek(...) without capturing its bool result, so nothing "
      + "downstream can branch on whether the player actually moved. SoundFlowPlaybackService.Seek "
      + "returns false when the position is negative, when no player is registered, or when the "
      + "data provider refuses — discarding that is how a refused seek reports success (AUD-24). "
      + "Capture it.\nSeek line: " + body[seekLine.Value].Trim());

    var resultVar = Regex.Escape(result.Groups[1].Value);
    var branchOnResult = new Regex($@"\bif\s*\(\s*!?\s*{resultVar}\b", RegexOptions.Compiled);

    foreach (var assignment in afterTheCall)
    {
      var guarded = Enumerable
        .Range(seekLine.Value + 1, assignment - seekLine.Value - 1)
        .Any(i => branchOnResult.IsMatch(body[i]));

      Assert.True(
        guarded,
        $"'_position = position;' on body line {assignment + 1} follows the engine call with no "
        + $"branch on '{result.Groups[1].Value}' between them, so it runs whether or not the player "
        + "moved. Position is what the panel and /api/audio read — advancing it on a refusal is how "
        + "a seek that repositioned nothing came to look like one that worked (AUD-24). Put it "
        + "behind an early return on the refusal, or inside an if that tests the returned bool."
        + "\nBody scanned:\n  " + string.Join("\n  ", body));
    }
  }

  /// <summary>
  /// The statement lines of <c>SeekCoreAsync</c>'s body, comment lines removed.
  /// </summary>
  /// <remarks>
  /// The body runs from the signature to the first line that is exactly <c>"  }"</c> — the house
  /// two-space indent for a method's closing brace. Every assertion here fails loudly rather than
  /// returning an empty list: a source-scanning lint that no-ops when it cannot find its target is
  /// worse than no lint, because green is what it always says.
  /// </remarks>
  private static IReadOnlyList<string> SeekCoreBody()
  {
    var root = RepositoryRoot.Find();
    var path = Path.Combine(root, SourceRelativePath.Replace('/', Path.DirectorySeparatorChar));

    Assert.True(
      File.Exists(path),
      $"Expected FilePlayerAudioSource.cs at '{path}'. Repository root resolved to '{root}'. "
      + "RepositoryRoot.Find's remarks record a run that scanned a nested checkout instead.");

    var lines = File.ReadAllLines(path);

    var start = Array.FindIndex(
      lines, l => l.Contains("protected override Task SeekCoreAsync", StringComparison.Ordinal));

    Assert.True(
      start >= 0,
      $"SeekCoreAsync was not found in '{path}'. If it was renamed or moved, update this test "
      + "rather than deleting it — the defect it pins shipped for the life of the file.");

    var end = Array.FindIndex(lines, start + 1, l => l == "  }");

    Assert.True(
      end > start,
      $"Could not find SeekCoreAsync's closing brace at the house two-space indent, starting from "
      + $"line {start + 1} of '{path}'. The body could not be isolated, so this lint would "
      + "otherwise scan the wrong region — fix the extraction, do not relax it.");

    var body = lines[(start + 1)..end]
      .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal))
      .ToList();

    Assert.True(
      body.Count >= 5,
      $"SeekCoreAsync's body came out as {body.Count} non-comment line(s), which is too few for a "
      + "method that range-checks, calls the engine and branches on the result. The extraction has "
      + "almost certainly gone wrong.");

    return body;
  }
}

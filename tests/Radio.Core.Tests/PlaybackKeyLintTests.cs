using System.Text.RegularExpressions;

namespace Radio.Core.Tests;

/// <summary>
/// A regression lint over <c>src/Radio.Infrastructure/Audio/Sources/Primary/*.cs</c>, in two rules:
/// every assignment to <c>_playbackId</c> must be <c>Id</c> or <c>null</c>, and the key argument of
/// every <c>Play*Async</c> / <c>StopAsync</c> call must be <c>Id</c> or <c>_playbackId</c>.
/// </summary>
/// <remarks>
/// <b>What it pins (AUD-2).</b> Four primary source types registered their SoundFlow component under
/// a minted key — <c>$"sdr-radio-{Guid.NewGuid():N}"</c>, <c>$"usb-capture-{Id:N}"</c>,
/// <c>$"file-player-{Guid.NewGuid():N}"</c> — while AudioManager addressed them by
/// <c>IAudioSource.Id</c>. SoundFlowPlaybackService's dictionaries carry no StringComparer
/// (<c>SoundFlowPlaybackService.cs:24-28</c>), so lookups are ordinal, the two keys were never equal,
/// and every gain and ducking lookup missed — silently, for months. Bluetooth had the identical bug
/// and was fixed in isolation by <c>2bbd0eb5</c> (2026-03-02); this file exists because that fix
/// pinned nothing, so the same shape survived in four more source types for another six months.
///
/// <b>Why TWO rules.</b> The four broken sources all went through a <c>_playbackId</c> field, so
/// rule 1 alone would have caught AUD-2. It would NOT catch a new source written in
/// TestToneAudioSource's shape, which has no such field and passes <c>Id</c> straight into the call
/// (<c>TestToneAudioSource.cs:73</c>, <c>:94</c>, <c>:107</c>). That shape is correct today and is
/// the obvious template for the next source somebody adds — so rule 2 checks the call sites
/// directly, and a source using either idiom is covered.
///
/// ⚠⚠ <b>WHAT THIS TEST CANNOT DO.</b> It is a lint over source TEXT, not a proof of the property.
/// It cannot see a key reaching the call through a local (<c>var key = Mint();
/// PlayComponentAsync(key, …)</c>), through a helper method, or through a differently-named field —
/// rule 2 would simply see an identifier it does not recognise, which is why it FAILS on anything
/// that is not <c>Id</c> or <c>_playbackId</c> rather than trying to evaluate it. That is
/// deliberately strict: a false failure here is a five-second read, and a false pass is another six
/// months of silent no-op. <b>It says nothing about whether the volume actually moves.</b> That is
/// asserted, on a registered component, by
/// <c>Radio.Infrastructure.Tests.Audio.Services.DuckingReachesTheActiveSourceTests</c>. Read a green
/// run here as "nobody re-typed the shape that broke", not as "gain and ducking work".
///
/// ⚠ <b>Event sources are deliberately out of the sweep.</b> <c>AudioFileEventSource.cs:146</c> mints
/// <c>"audio-event-…"</c> and is CORRECT to: AudioManager only ever addresses primary sources, so the
/// divergence reaches no gain or ducking path, and <c>EventPlaybackService.cs:32-41</c> documents that
/// id space on purpose. Widening this lint to <c>Sources/</c> would flag it and the rule would be
/// deleted within a week.
///
/// ⚠ <b>It lives in Radio.Core.Tests, not beside the code it scans.</b> Every source-scanning lint in
/// this repository does, because <see cref="RepositoryRoot"/> — which carries the worktree
/// correctness this scan depends on — is <c>internal</c> to this assembly. A copy of that walker made
/// to move this file next to Radio.Infrastructure would be the second copy the extraction in UI-7
/// existed to prevent. The plan (AUD-2 §4.2) named
/// <c>tests/Radio.Infrastructure.Tests/Audio/Sources/</c>; it also said to reuse the existing walker
/// rather than ship a second one, and the two instructions cannot both be honoured. The walker won.
/// </remarks>
public class PlaybackKeyLintTests
{
  private const string PrimarySourcesRelativePath =
    "src/Radio.Infrastructure/Audio/Sources/Primary";

  /// <summary>
  /// <c>_playbackId = &lt;rhs&gt;;</c> — captures the right-hand side up to the semicolon.
  /// </summary>
  /// <remarks>
  /// ⚠ The <c>(?!=)</c> is load-bearing and is not in the plan's draft. Without it the pattern also
  /// matches the four <c>_playbackId == null</c> comparisons in this directory
  /// (<c>BluetoothAudioSource.cs:808</c>, <c>:1334</c>, <c>:1415</c>), whose captured "right-hand
  /// side" is <c>= null</c> — neither <c>Id</c> nor <c>null</c>, so the lint would fail on three
  /// correct comparisons and the natural repair would be to loosen the rule.
  /// </remarks>
  private static readonly Regex Assignment = new(
    @"_playbackId\s*=(?!=)\s*([^;]+);", RegexOptions.Compiled);

  /// <summary>
  /// First argument of <c>PlayComponentAsync</c> / <c>PlayFileAsync</c> / <c>PlayDataProviderAsync</c>
  /// / <c>StopAsync</c> on <c>_playbackService</c>, taken up to the first comma or closing paren.
  /// </summary>
  private static readonly Regex PlaybackCall = new(
    @"_playbackService\s*[?]?\s*\.\s*(PlayComponentAsync|PlayFileAsync|PlayDataProviderAsync|StopAsync)"
    + @"\s*\(\s*([^,)\s]+)",
    RegexOptions.Compiled);

  [Fact]
  public void EveryPrimarySourceRegistersUnderItsOwnId()
  {
    var (dir, files) = PrimarySources();

    var violations = new List<string>();
    var inspected = 0;

    foreach (var file in files)
    {
      var lines = File.ReadAllLines(file);
      for (var i = 0; i < lines.Length; i++)
      {
        var match = Assignment.Match(lines[i]);
        if (!match.Success)
        {
          continue;
        }

        inspected++;
        var rhs = match.Groups[1].Value.Trim();
        if (rhs is "Id" or "null")
        {
          continue;
        }

        violations.Add($"{Path.GetFileName(file)}:{i + 1} — _playbackId = {rhs}");
      }
    }

    // ⛔ THE VACUITY GUARD. A regex lint that matches NOTHING passes — silently, forever, through
    // every refactor that renames the field. The floor is deliberately under the true count (16 at
    // 2f6d8ef3 — Bluetooth 7, FilePlayer 3, SDR 3, USBAudioSourceBase 3) so ordinary churn does not
    // trip it, while a pattern that has gone blind still cannot pass.
    Assert.True(
      inspected >= 12,
      $"PlaybackKeyLintTests rule 1 matched only {inspected} _playbackId assignments under '{dir}'. "
      + "It matched 16 when written. The regex has almost certainly stopped matching the code rather "
      + "than the code having shrunk — fix the pattern, do NOT lower this floor.");

    Assert.True(
      violations.Count == 0,
      "A primary audio source must register with SoundFlowPlaybackService under its own "
      + "IAudioSource.Id, so AudioManager's gain and ducking lookups can find it (AUD-2). "
      + $"Scanned '{dir}'. Offending assignments:\n  " + string.Join("\n  ", violations));
  }

  [Fact]
  public void EveryPlaybackServiceCallIsKeyedOnIdOrPlaybackId()
  {
    // Rule 2 — the call sites, for sources that carry no _playbackId field. See the class remarks.
    //
    // ⚠ WHOLE-FILE TEXT, NOT LINE BY LINE, AND THAT IS NOT A STYLE CHOICE. Five of the eighteen
    // playback-service call sites in this directory put the key argument on the line AFTER the open
    // paren (SDRRadioAudioSource.cs:963-964, FilePlayerAudioSource.cs:739-740,
    // BluetoothAudioSource.cs:717-718 and :736-737, USBAudioSourceBase.cs:326-327). A per-line match
    // finds no argument on the opening line and SKIPS those calls silently — i.e. it would pass
    // happily on three of the four sources AUD-2 is about. `\s` matches newlines in .NET by default,
    // so matching over the full text handles both shapes with no extra options.
    var (dir, files) = PrimarySources();

    var violations = new List<string>();
    var inspected = 0;

    foreach (var file in files)
    {
      var text = File.ReadAllText(file);
      foreach (Match match in PlaybackCall.Matches(text))
      {
        inspected++;
        var key = match.Groups[2].Value.Trim();
        if (key is "Id" or "_playbackId")
        {
          continue;
        }

        // 1-based line number of the call, for a message that can be jumped to.
        var line = text.Take(match.Index).Count(c => c == '\n') + 1;
        violations.Add($"{Path.GetFileName(file)}:{line} — {match.Groups[1].Value}({key}…)");
      }
    }

    // ⛔ THE VACUITY GUARD, and it is the most important assertion in this file. Without it,
    // mutations that should turn rule 2 red can all be defeated by a pattern that matches nothing.
    // The floor is under the true count (18 at 2f6d8ef3 — Bluetooth 5, FilePlayer 4, SDR 3,
    // TestTone 3, USBAudioSourceBase 3).
    Assert.True(
      inspected >= 14,
      $"PlaybackKeyLintTests rule 2 matched only {inspected} playback-service call sites under "
      + $"'{dir}'. It matched 18 when written. The regex has almost certainly stopped matching the "
      + "code rather than the code having shrunk — fix the pattern, do NOT lower this floor.");

    Assert.True(
      violations.Count == 0,
      "A primary audio source must key SoundFlowPlaybackService calls on its own IAudioSource.Id "
      + "(directly, or via a _playbackId that rule 1 pins to Id) — AUD-2. "
      + $"Scanned '{dir}'. Offending calls:\n  " + string.Join("\n  ", violations));
  }

  /// <summary>
  /// Resolves the primary-source directory and its <c>.cs</c> files, failing loudly if either is
  /// missing.
  /// </summary>
  /// <remarks>
  /// ⚠ The resolved directory is returned so every failure message can name it. Violations are
  /// reported by BARE FILENAME, which is identical in every checkout of this repository — so a scan
  /// of the wrong tree reads exactly like a real finding. <see cref="RepositoryRoot.Find"/> carries
  /// the worktree remarks explaining that this has actually happened here.
  /// </remarks>
  private static (string Directory, List<string> Files) PrimarySources()
  {
    var root = RepositoryRoot.Find();
    var dir = Path.Combine(root, PrimarySourcesRelativePath.Replace('/', Path.DirectorySeparatorChar));

    Assert.True(
      Directory.Exists(dir),
      $"Expected the primary audio sources at '{dir}'. Repository root resolved to '{root}'.");

    var files = Directory
      .EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
      .OrderBy(f => f, StringComparer.Ordinal)
      .ToList();

    Assert.True(
      files.Count >= 8,
      $"Only {files.Count} source files found under '{dir}'; there were 11 when this lint was "
      + "written. The scan is looking at the wrong place.");

    return (dir, files);
  }
}

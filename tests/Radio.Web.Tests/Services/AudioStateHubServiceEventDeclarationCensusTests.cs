using System.Reflection;
using Radio.Web.Services.Hub;

namespace Radio.Web.Tests.Services;

/// <summary>
/// Every reference-payload event on <see cref="AudioStateHubService"/> is classified by ADR-033, and
/// this census fails when a new one appears (queue row `UI-14`).
/// </summary>
/// <remarks>
/// ⚠ THIS IS A TRIPWIRE, NOT A PROOF. It asserts which events exist and how their payloads are
/// DECLARED. It cannot assert that each one behaves correctly — that is what
/// AudioStateHubServiceNullPayloadTests (reject) and AudioStateHubServicePassThroughTests
/// (pass through) are for. Its whole job is to stop an eighth payload event being added with neither.
///
/// ⭐ WHY IT EXISTS. `UI-12` gated the four events declared Func&lt;T, Task&gt; and left the three
/// declared Func&lt;T?, Task&gt; ungated. Both halves of ADR-033 are now gated — but only for the
/// seven events that existed on 2026-09-09, and nothing else would notice an eighth.
///
/// 📌 It reads the compiler-generated backing FIELD rather than the EventInfo, because that is the
/// same reflection route HubEventFire.InvocationListOf already depends on, so the two go blind
/// together rather than one of them silently. Converting any of these to explicit add/remove
/// accessors deletes the field (`UI-7` C-207) and fails this test loudly, which is correct.
/// </remarks>
public class AudioStateHubServiceEventDeclarationCensusTests
{
  private static readonly string[] NullPassesThrough =
  [
    nameof(AudioStateHubService.EventPlaybackChanged),
    nameof(AudioStateHubService.NowPlayingChanged),
    nameof(AudioStateHubService.VolumeChanged),
  ];

  private static readonly string[] NullIsRejected =
  [
    nameof(AudioStateHubService.EncoderConfigStatusChanged),
    nameof(AudioStateHubService.EncoderConnectionChanged),
    nameof(AudioStateHubService.EncoderHudChanged),
    nameof(AudioStateHubService.RadioStateChanged),
  ];

  /// <summary>
  /// ⚠ THE REMEDY, IN THE FAILURE MESSAGE RATHER THAN ONLY IN A PLAN. A tripwire whose fix lives in a
  /// document nobody opens gets silenced instead of answered — adding the new name to a list above is
  /// exactly the wrong response, because it re-creates the ungated state this census exists to catch.
  /// </summary>
  private const string Remedy =
    "⛔ DO NOT FIX THIS BY ADDING THE NAME TO THE LIST. Decide, per ADR-033, whether that event's null "
    + "is data or a contract violation, and wire it up FIRST:\n"
    + "  • Declared Func<T?, Task> — null is DATA. Add an internal On…MessageAsync seam with NO "
    + "AcceptPayload call, and a pass-through test in AudioStateHubServicePassThroughTests.\n"
    + "  • Declared Func<T, Task> — null is a VIOLATION. Add an internal On…MessageAsync seam that "
    + "calls AcceptPayload, and a rejection test in AudioStateHubServiceNullPayloadTests.\n"
    + "THEN update the list in this file. `UI-14` exists because `UI-12` gated four of seven events "
    + "and the other three went unnoticed for a day.";

  [Fact]
  public void EveryReferencePayloadEventIsClassifiedByAdr033()
  {
    var type = typeof(AudioStateHubService);
    var context = new NullabilityInfoContext();

    var events = type.GetEvents(BindingFlags.Public | BindingFlags.Instance)
      .Select(e => (e.Name, Field: type.GetField(e.Name, BindingFlags.NonPublic | BindingFlags.Instance)))
      .ToList();

    // Floors, so silence means something. There were 14 events when this was written; the class
    // remark on NotifyAsync<T> records the same number and has already been wrong once.
    Assert.True(
      events.Count >= 14,
      $"Only {events.Count} public events found on {type.Name} — there were 14 when this census was "
      + "written. Either events were deleted without updating this test, or GetEvents is looking at "
      + "the wrong type.");
    Assert.All(events, e => Assert.NotNull(e.Field));

    var payloadEvents = events
      .Select(e => (e.Name, Info: context.Create(e.Field!)))
      // Func<T, Task> has two type arguments; Func<Task> has one.
      .Where(e => e.Info.GenericTypeArguments.Length == 2)
      // SleepStateChanged is Func<bool, Task>, and a bool cannot be null.
      // ⚠ NOT "a value type cannot be null" — an earlier revision said exactly that and pre-merge
      // review falsified it: typeof(int?).IsValueType is TRUE. A bare IsValueType filter would
      // silently drop a future Func<TimeSpan?, Task>, whose null IS representable and IS in ADR-033's
      // pass-through half — the census would stay green while the eighth event landed ungated, which
      // is the one failure this file exists to prevent. None exists today; the filter is written so
      // that one would be SEEN rather than skipped.
      // 📌 ADR-033's REJECT half genuinely cannot reach a value type: AcceptPayload<T> is where T : class.
      .Where(e => !e.Info.GenericTypeArguments[0].Type.IsValueType
        || Nullable.GetUnderlyingType(e.Info.GenericTypeArguments[0].Type) is not null)
      .ToList();

    // ⭐ PROVE THE INSTRUMENT BEFORE TRUSTING ITS VERDICT. If NullabilityInfoContext reported
    // Unknown for everything, both expected sets below would come back empty and the equality
    // assertions would compare empty to non-empty — which fails, but for a reason nobody could
    // read. This says it in one line instead. (Idiom: VisualizerPanelTests.cs:233-236, and
    // AsyncEventFanOutLintTests' positive control.)
    Assert.NotEmpty(payloadEvents);
    Assert.All(payloadEvents, e => Assert.NotEqual(
      NullabilityState.Unknown, e.Info.GenericTypeArguments[0].ReadState));

    string[] Names(NullabilityState state) => payloadEvents
      .Where(e => e.Info.GenericTypeArguments[0].ReadState == state)
      .Select(e => e.Name)
      .OrderBy(n => n, StringComparer.Ordinal)
      .ToArray();

    var passesThrough = Names(NullabilityState.Nullable);
    var rejected = Names(NullabilityState.NotNull);

    // ⚠ Checked BEFORE the two equality assertions, and this ordering is the point. An eighth event
    // added tomorrow lands here — in an assertion that CAN carry a message — rather than in an
    // Assert.Equal over collections, whose overloads take no user message and would report only a
    // sequence diff. Assert.Equal still runs below and still catches a RECLASSIFIED or DELETED event,
    // where a diff is exactly the right output.
    var unclassified = passesThrough.Concat(rejected)
      .Except(NullPassesThrough.Concat(NullIsRejected), StringComparer.Ordinal)
      .OrderBy(n => n, StringComparer.Ordinal)
      .ToArray();

    Assert.True(
      unclassified.Length == 0,
      $"Unclassified reference-payload event(s) on {type.Name}: {string.Join(", ", unclassified)}.\n"
      + Remedy);

    Assert.Equal(
      NullPassesThrough.OrderBy(n => n, StringComparer.Ordinal).ToArray(),
      passesThrough);

    Assert.Equal(
      NullIsRejected.OrderBy(n => n, StringComparer.Ordinal).ToArray(),
      rejected);
  }
}

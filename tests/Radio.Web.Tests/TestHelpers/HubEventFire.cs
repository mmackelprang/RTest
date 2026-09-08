using System.Reflection;

namespace Radio.Web.Tests.TestHelpers;

/// <summary>
/// Fires a hub service's event from a test by reflecting its compiler-generated backing field and
/// awaiting EVERY subscriber, in registration order.
/// </summary>
/// <remarks>
/// ⚠⚠ THIS EXISTS BECAUSE THE TEST HARNESS CONTAINED THE DEFECT UNDER TEST (queue row `UI-7`,
/// correction `C-213`). TWELVE sites across five files fired hub events like this — nine on
/// <c>AudioStateHubService</c> and three on <c>AudioVisualizationHubService</c>, which has the same
/// shape and was not mentioned by the row at all. (`C-213` itself named six; the plan's anchor list
/// named eight. Both undercounted: two sites share one field lookup, and the visualization hub was
/// never counted.)
///
/// <code>
///   var del = (Func&lt;NowPlayingDto?, Task&gt;?)field!.GetValue(hub);
///   if (del != null)
///   {
///     await del.Invoke(dto);
///   }
/// </code>
///
/// <c>Delegate.Invoke</c> on a multicast <c>Func&lt;…,Task&gt;</c> RUNS every subscriber but returns
/// only the LAST one's Task. With one subscriber — which is what every one of those fixtures had —
/// that is correct and nobody noticed for months. Register two and it awaits only the second.
///
/// ⭐ THE CONSEQUENCE IS SHARPER THAN "A TEST IS SLIGHTLY WRONG": a UI-7 test written through the
/// old helper would report "all subscribers were awaited" WHETHER OR NOT the production fix landed.
/// It would pass against a completely unfixed implementation. That is the single most likely way
/// this row could have shipped green and broken, which is why the harness was repaired BEFORE the
/// assertion that depends on it was written.
///
/// ⛔ The <c>src/</c> lint (<c>AsyncEventFanOutLintTests</c>) CANNOT catch this shape and was not
/// extended to: it scans source text for an event NAME being invoked, and here the delegate arrives
/// through reflection with no event name in the expression at all. The guard is this helper being
/// the only way tests fire a hub event.
///
/// 📌 Not to be confused with the production fix. This walks the invocation list so the TEST
/// observes every subscriber; <c>AudioStateHubService.NotifyAsync</c> walks it so PRODUCTION does.
/// They are the same shape for the same reason and neither substitutes for the other — a test that
/// drove the production path would not need this, and §4.1's headline tests do exactly that.
/// </remarks>
public static class HubEventFire
{
  /// <summary>
  /// Awaits every subscriber of the named <c>Func&lt;T, Task&gt;</c> event on <paramref name="target"/>.
  /// </summary>
  /// <remarks>
  /// ⚠ Exceptions are NOT caught. This is the test's own fan-out, so a throwing subscriber must fail
  /// the test loudly rather than be swallowed the way production's per-subscriber catch does.
  /// </remarks>
  public static async Task FireAsync<T>(object target, string eventName, T arg)
  {
    var handlers = InvocationListOf<Func<T, Task>>(target, eventName);
    foreach (var handler in handlers)
    {
      await handler.Invoke(arg);
    }
  }

  /// <summary>
  /// Awaits every subscriber of the named parameterless <c>Func&lt;Task&gt;</c> event.
  /// </summary>
  public static async Task FireAsync(object target, string eventName)
  {
    var handlers = InvocationListOf<Func<Task>>(target, eventName);
    foreach (var handler in handlers)
    {
      await handler.Invoke();
    }
  }

  /// <summary>
  /// Returns the subscribers currently registered on the named event, or an empty array when the
  /// event has none.
  /// </summary>
  /// <remarks>
  /// ⚠ A MISSING FIELD FAILS LOUDLY, and that is deliberate rather than defensive. The field is
  /// compiler-generated for a field-like <c>event</c>; converting the event to explicit
  /// <c>add</c>/<c>remove</c> accessors deletes it (UI-7 `C-207`), and every caller of this helper
  /// would then be reflecting nothing. An unsubscribed event returning an empty list is a normal
  /// state; a NON-EXISTENT event is a broken test, and the two must not look alike.
  /// </remarks>
  public static TDelegate[] InvocationListOf<TDelegate>(object target, string eventName)
    where TDelegate : Delegate
  {
    ArgumentNullException.ThrowIfNull(target);

    var field = target.GetType().GetField(
      eventName, BindingFlags.NonPublic | BindingFlags.Instance);

    if (field is null)
    {
      throw new InvalidOperationException(
        $"No backing field named '{eventName}' on {target.GetType().Name}. A field-like `event` has "
        + "one; an event with explicit add/remove accessors does not (UI-7 C-207). If the event was "
        + "converted, this helper and its callers must change with it.");
    }

    var value = field.GetValue(target);
    if (value is null)
    {
      return [];
    }

    // ⚠ A WRONG TDelegate IS A THIRD STATE and must not be routed into the normal one. Pre-merge
    // review found this returning [] when the type test failed, which is the residual form of the
    // hazard this whole helper exists to remove: FireAsync(hub, "RadioStateChanged", aNowPlayingDto)
    // would find the field, fail the cast, fire NOTHING, and let the test pass on whatever it
    // asserted next. "No subscribers" is a normal state; "you named the wrong event, or its
    // signature changed under you" is a broken test, and silence cannot be the answer to both.
    if (value is not TDelegate handler)
    {
      throw new InvalidOperationException(
        $"Event '{eventName}' on {target.GetType().Name} is {value.GetType()}, not "
        + $"{typeof(TDelegate)}. The event name and the payload type must match the declaration.");
    }

    return handler.GetInvocationList().Cast<TDelegate>().ToArray();
  }
}

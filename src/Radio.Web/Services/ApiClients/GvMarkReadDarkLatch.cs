namespace Radio.Web.Services.ApiClients;

/// <summary>
/// Process-lifetime latch for RotaryPhone's dark mark-read feature (GV-6; ADR-024 §3.3).
/// Set the first time a mark-read route answers <c>409 markread_disabled</c>, which their
/// contract defines as their server-side <c>GVBridge:EnableMarkRead</c> being <c>false</c>.
/// Once set, <see cref="GvBridgeApiService"/> short-circuits both mark-read POSTs exactly as
/// it does when our own <c>RotaryPhone:Gv:MarkReadEnabled</c> is off.
/// <para>
/// SINGLETON BY NECESSITY. <c>AddHttpClient&lt;GvBridgeApiService&gt;</c> (Program.cs) registers a
/// TRANSIENT typed client, so every Blazor component in every circuit resolves its own service
/// instance — a field on the service could never suppress a second call. Registered
/// <c>AddSingleton</c> beside that client.
/// </para>
/// <para>
/// NOTHING CLEARS IT IN-PROCESS, deliberately. <c>GvBridgeStatusDto</c> carries no mark-read
/// capability field, so the 10s status poll cannot observe RotaryPhone re-enabling the feature,
/// and our own flag cannot change without a restart. If RotaryPhone enables mark-read while this
/// is latched, <c>radio-web</c> must be restarted to pick it up — see design/INTEGRATIONS.md
/// § "Two-flag distinction". ADR-024's rollout order (theirs first, then ours) never reaches
/// that state.
/// </para>
/// </summary>
public sealed class GvMarkReadDarkLatch
{
  private int _latched;

  /// <summary>True once <see cref="TryLatch"/> has succeeded. Cheap enough to read on every
  /// mark-read call.</summary>
  public bool IsLatched => Volatile.Read(ref _latched) != 0;

  /// <summary>
  /// Latch, and report whether THIS caller was the one that did it. Returns <c>true</c> exactly
  /// once for the lifetime of the instance, so the caller can log once and only once.
  /// <para>
  /// Interlocked rather than check-then-set because concurrent Blazor circuits each hold their
  /// own transient <see cref="GvBridgeApiService"/> over this one latch and run on independent
  /// sync-contexts: <c>if (!_b) { _b = true; Log(); }</c> races into two log lines, which is the
  /// exact property this type exists to guarantee.
  /// </para>
  /// </summary>
  public bool TryLatch() => Interlocked.Exchange(ref _latched, 1) == 0;
}

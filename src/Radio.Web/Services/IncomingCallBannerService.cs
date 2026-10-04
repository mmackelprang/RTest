using Radio.Core.Utilities;
using Radio.Web.Components.Pages;
using Radio.Web.Models;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;

namespace Radio.Web.Services;

/// <summary>Which of the three caller presentations the banner is in (PHN-11).</summary>
public enum IncomingCallCallerKind
{
  /// <summary>No usable number: none has arrived, or RotaryPhone sent the literal <c>"Unknown"</c>.</summary>
  Unknown,

  /// <summary>A number, with no contact name for it (yet, or at all).</summary>
  Number,

  /// <summary>A contact name resolved for the number.</summary>
  Contact,
}

/// <summary>What the banner's header says (PHN-11 spec §7).</summary>
public enum IncomingCallPhase
{
  /// <summary><c>INCOMING CALL</c>.</summary>
  Ringing,

  /// <summary><c>ANSWERED</c>: the exit beat after the call went to <c>InCall</c>.</summary>
  Answered,

  /// <summary><c>CALL ENDED</c>: the exit beat after the call went anywhere else.</summary>
  Ended,
}

/// <summary>Where the banner's Ignore request stands (PHN-11).</summary>
public enum IncomingCallDeclineState
{
  /// <summary>Not pressed for this call.</summary>
  None,

  /// <summary>The request is in flight, or RotaryPhone accepted it and the call has not yet ended.</summary>
  Declining,

  /// <summary>
  /// The request failed, or the call was still ringing <see cref="IncomingCallBannerService.DeclineDeadline"/>
  /// after it was sent. The banner stays up and Ignore can be pressed again.
  /// </summary>
  Failed,
}

/// <summary>Why the banner went away (PHN-11).</summary>
public enum IncomingCallCloseReason
{
  /// <summary>A touch on the banner outside the Ignore column. The call itself is unaffected.</summary>
  Touch,

  /// <summary>The call left <c>Ringing</c> for <c>InCall</c>: answered on the rotary phone or the cell.</summary>
  Answered,

  /// <summary>The call left <c>Ringing</c> for anything else: the caller hung up, a timeout, or an Ignore.</summary>
  Ended,

  /// <summary>Nothing confirmed the call was still ringing for <see cref="IncomingCallBannerService.StaleAfter"/>.</summary>
  Stale,
}

/// <summary>An immutable picture of the banner, safe to hand to a renderer on any thread.</summary>
public sealed record IncomingCallBannerSnapshot
{
  public static readonly IncomingCallBannerSnapshot Hidden = new();

  /// <summary>Whether the banner is mounted — ringing, in its exit beat, or fading out.</summary>
  public bool IsVisible { get; init; }

  /// <summary>Identifies the call on screen, so a host announces each call once. 0 when hidden.</summary>
  public long CallId { get; init; }

  public IncomingCallPhase Phase { get; init; }

  /// <summary>True for the last moments before the banner unmounts: the host plays the exit fade.</summary>
  public bool IsLeaving { get; init; }

  public IncomingCallCallerKind CallerKind { get; init; }

  /// <summary>The big line: the contact name, the formatted number, or "Unknown caller".</summary>
  public string PrimaryText { get; init; } = "";

  /// <summary>The formatted number under a contact name, "No caller ID" for an unknown caller, else null.</summary>
  public string? SecondaryText { get; init; }

  /// <summary>One or two initials for the contact case; null otherwise (the host shows a glyph).</summary>
  public string? Monogram { get; init; }

  /// <summary>True while a contact-name lookup for the current number is still running.</summary>
  public bool IsResolvingName { get; init; }

  /// <summary>
  /// True between a bare <c>Ringing</c> and the <c>IncomingCall</c> that normally follows it within
  /// milliseconds. Settles on the first number (or <c>"Unknown"</c>) or the first status read.
  /// </summary>
  public bool IsAwaitingCallerId { get; init; }

  public IncomingCallDeclineState DeclineState { get; init; }

  /// <summary>Whether Ignore can actually decline the call (<c>RotaryPhone:DeclineSupported</c>).</summary>
  public bool CanDecline { get; init; }

  /// <summary>Why the most recent banner closed; null while none has.</summary>
  public IncomingCallCloseReason? LastCloseReason { get; init; }
}

/// <summary>
/// PHN-11: the state behind the incoming-call banner. One per Blazor circuit, so a touch on the kiosk
/// closes the kiosk's banner and nobody else's. Design: <c>docs/design-handoffs/2026-10-02-incoming-call-banner.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Inputs.</b> RotaryPhone's <c>/hub</c> through <see cref="PhoneHubService"/>:
/// <c>CallStateChanged(phoneId, state)</c> (<c>Idle</c>, <c>Dialing</c>, <c>Ringing</c>, <c>InCall</c>) and
/// <c>IncomingCall(phoneId, number)</c>, which RotaryPhone sends right after <c>Ringing</c> only when it has
/// a number, and again for the same call when a caller-ID update arrives. Nothing on the hub carries a
/// call id, a contact name or a photo, so on the hub "the same call" means the same phone id with no other
/// state in between. While a call is tracked, each tracked phone's <c>GET /api/phone/status?phoneId=</c> is
/// re-read every <see cref="PollInterval"/>, so a hub event lost to a reconnect cannot leave the banner up
/// for good; that read also carries RotaryPhone's <c>CallId</c>, and a changed id splits a new call off an
/// old one whose <c>Idle</c> was lost.
/// </para>
/// <para>
/// <b>More than one call.</b> RotaryPhone refuses a second inbound call on a phone that is ringing, so a
/// second <c>IncomingCall</c> for a tracked phone is the same call with a better number: the caller line
/// only ever gains information. A ring on a different phone id is tracked alongside; the banner shows the
/// newest, and falls back to the other if the newest ends first (spec §1, case c).
/// </para>
/// <para>
/// ⚠ <b>Logging (PHN-5).</b> <c>Radio.Web</c>'s console sink has no level restriction, so an Information
/// line here reaches <c>journalctl -u radio-web</c>. This class logs nothing at Information. A number is
/// only ever logged through <see cref="LogSafeText.ForPhone"/>, and a contact name never.
/// </para>
/// <para>
/// <b>Threads.</b> Hub events arrive on SignalR threads, timers on the thread pool, and Dismiss/Ignore on
/// the circuit. Every read and write of the call state is under <see cref="_gate"/>;
/// <see cref="Changed"/> is raised outside it, and subscribers marshal onto their own circuit. The three
/// test rendezvous properties (<c>LastPoll</c>, <c>LastNameLookup</c>, <c>Seeded</c>) are plain task
/// references written outside it; production never reads them except <c>LastPoll</c>'s completion, as the
/// poll-overlap guard, where a stale read only skips or allows one tick.
/// </para>
/// </remarks>
public sealed class IncomingCallBannerService : IDisposable
{
  /// <summary>How often the phone status is re-read while a call is tracked.</summary>
  public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

  /// <summary>
  /// How long a call survives with nothing confirming it is still ringing (the status read failing, the
  /// phone service unreachable). RotaryPhone's own ring timeout is 60 s on the Bluetooth path.
  /// </summary>
  public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(90);

  /// <summary>How long after Ignore the call may keep ringing before the banner reports a failure (spec §7).</summary>
  public static readonly TimeSpan DeclineDeadline = TimeSpan.FromSeconds(5);

  /// <summary>The labelled beat after an answer or an end, before the fade (spec §7).</summary>
  public static readonly TimeSpan ExitHold = TimeSpan.FromMilliseconds(600);

  /// <summary>The exit fade; the banner unmounts when it ends (spec §8).</summary>
  public static readonly TimeSpan ExitFade = TimeSpan.FromMilliseconds(200);

  /// <summary>The config key that enables Ignore once RotaryPhone ships its decline endpoint.</summary>
  public const string DeclineSupportedKey = "RotaryPhone:DeclineSupported";

  private const string UnknownCallerText = "Unknown caller";
  private const string NoCallerIdText = "No caller ID";

  private readonly PhoneHubService _hub;
  private readonly PhoneApiService _phoneApi;
  private readonly ContactResolutionService? _contacts;
  private readonly TimeProvider _time;
  private readonly ILogger<IncomingCallBannerService> _logger;
  private readonly IConfiguration _configuration;

  private readonly object _gate = new();
  private bool _started;
  private bool _disposed;
  private long _nextCallId;
  private ITimer? _pollTimer;

  // Every call RotaryPhone reports ringing, oldest first. The banner shows the newest one not touched away.
  private readonly List<TrackedCall> _ringing = [];

  // The call whose banner is on its way out: the ANSWERED / CALL ENDED beat, or a touch's fade. Frozen
  // at the moment it left, and drawn only while no ringing call claims the banner.
  private Exit? _exit;
  private ITimer? _exitTimer;

  private IncomingCallCloseReason? _lastCloseReason;
  private long _announcedCallId;
  private long _enteredCallId;

  /// <summary>Raised after every change to <see cref="Current"/>, from whichever thread made it.</summary>
  public event Action? Changed;

  /// <summary>The completion of the most recent status read. A test rendezvous; never awaited in production.</summary>
  internal Task LastPoll { get; private set; } = Task.CompletedTask;

  /// <summary>The completion of the most recent name lookup. A test rendezvous; never awaited in production.</summary>
  internal Task LastNameLookup { get; private set; } = Task.CompletedTask;

  /// <summary>The completion of the start-up status read. A test rendezvous; never awaited in production.</summary>
  internal Task Seeded { get; private set; } = Task.CompletedTask;

  public IncomingCallBannerService(
    PhoneHubService hub,
    PhoneApiService phoneApi,
    IConfiguration configuration,
    ILogger<IncomingCallBannerService> logger,
    ContactResolutionService? contacts = null,
    TimeProvider? timeProvider = null)
  {
    _hub = hub;
    _phoneApi = phoneApi;
    _contacts = contacts;
    _logger = logger;
    _time = timeProvider ?? TimeProvider.System;
    _configuration = configuration;
  }

  /// <summary>
  /// <c>RotaryPhone:DeclineSupported</c>, read on every use rather than once per circuit (PHN-13), so turning it
  /// on through the config store (<c>POST /api/configuration/RotaryPhone</c>, which radio-web reloads on the
  /// API's <c>ConfigChanged</c> push) reaches a kiosk circuit that is already open, from its next call.
  /// </summary>
  private bool CanDecline => _configuration.GetValue(DeclineSupportedKey, false);

  /// <summary>The banner as it should be drawn now.</summary>
  public IncomingCallBannerSnapshot Current
  {
    get
    {
      lock (_gate)
      {
        return BuildSnapshotLocked();
      }
    }
  }

  /// <summary>
  /// Subscribes to the hub and reads the phone status once, so a circuit that opens mid-ring (a page
  /// reload, the kiosk relaunching) still shows the call. Idempotent; every banner host calls it.
  /// </summary>
  public void Start()
  {
    lock (_gate)
    {
      if (_started || _disposed)
      {
        return;
      }
      _started = true;
    }

    _hub.IncomingCall += OnIncomingCall;
    _hub.CallStateChanged += OnCallStateChanged;

    // A Dispose that ran between the claim above and the subscribe would have unsubscribed nothing; the
    // hub service is a singleton, so undo it here rather than leave a handler on it for good.
    bool disposed;
    lock (_gate)
    {
      disposed = _disposed;
    }
    if (disposed)
    {
      _hub.IncomingCall -= OnIncomingCall;
      _hub.CallStateChanged -= OnCallStateChanged;
      return;
    }
    Seeded = SeedAsync();
  }

  /// <summary>
  /// True the first time it is asked about a call, false after. The banner's hosts use it so a call is
  /// announced once and plays its entry once, even when the banner moves between the /sleep page and the
  /// layout mid-ring (two component instances, one circuit).
  /// </summary>
  public bool TryMarkAnnounced(long callId) => TryMark(ref _announcedCallId, callId);

  /// <inheritdoc cref="TryMarkAnnounced"/>
  public bool TryMarkEntered(long callId) => TryMark(ref _enteredCallId, callId);

  private bool TryMark(ref long field, long callId)
  {
    lock (_gate)
    {
      if (callId <= 0 || field == callId)
      {
        return false;
      }
      field = callId;
      return true;
    }
  }

  /// <summary>
  /// A touch on the banner outside the Ignore column: closes it at once (a 200 ms fade, no beat). Does not
  /// touch the call or the announcement. A caller-ID update for the same call does not bring it back; the
  /// next call does. Ignored while an Ignore is in flight, whose outcome has to be seen (spec §7).
  /// </summary>
  public void Dismiss()
  {
    bool changed = false;
    lock (_gate)
    {
      var shown = ShownLocked();
      if (shown is not null)
      {
        if (shown.Decline != IncomingCallDeclineState.Declining)
        {
          shown.Dismissed = true;
          _lastCloseReason = IncomingCallCloseReason.Touch;
          // The next ringing call, if another phone is ringing too, takes the banner over directly.
          if (ShownLocked() is null)
          {
            BeginExitLocked(shown, IncomingCallPhase.Ringing, hold: TimeSpan.Zero);
          }
          changed = true;
        }
      }
      else if (_exit is { Leaving: false })
      {
        // A touch during the ANSWERED / CALL ENDED beat skips the rest of it.
        BeginExitLocked(_exit.Call, _exit.Phase, hold: TimeSpan.Zero);
        changed = true;
      }
    }

    if (changed)
    {
      _logger.LogDebug("Incoming-call banner closed by a touch");
      RaiseChanged();
    }
  }

  /// <summary>
  /// Ignore: asks RotaryPhone to decline the ringing call on screen. The banner shows "ENDING CALL" until the
  /// call leaves <c>Ringing</c> (and closes as for any other end); a failed request, or a call still ringing
  /// <see cref="DeclineDeadline"/> after the request was sent, shows the error and Ignore works again. A
  /// no-op when declining is not supported, when no ringing call is on screen, or while one is in flight.
  /// </summary>
  public async Task DeclineAsync()
  {
    TrackedCall call;
    int attempt;
    lock (_gate)
    {
      var shown = ShownLocked();
      if (!CanDecline || shown is null || shown.Decline == IncomingCallDeclineState.Declining)
      {
        return;
      }
      call = shown;
      call.Decline = IncomingCallDeclineState.Declining;
      attempt = ++call.DeclineAttempt;
      call.DeclineTimer?.Dispose();
      call.DeclineTimer = _time.CreateTimer(_ => OnDeclineDeadline(call, attempt), null, DeclineDeadline,
        Timeout.InfiniteTimeSpan);
    }
    RaiseChanged();

    DeclineCallOutcome outcome;
    try
    {
      outcome = await _phoneApi.DeclineCallAsync(call.PhoneId);
    }
    catch (Exception ex)
    {
      // DeclineCallAsync maps its own failures to Failed; this guards the unexpected.
      _logger.LogWarning(ex, "Declining the incoming call failed");
      outcome = DeclineCallOutcome.Failed;
    }

    if (outcome.Result == DeclineCallResult.Declined)
    {
      // Accepted: the banner keeps "ENDING CALL" and waits for the call to leave Ringing, or the deadline.
      return;
    }

    bool changed;
    if (outcome.Result == DeclineCallResult.NotRinging)
    {
      // PHN-13: a 409 — the call stopped ringing before the decline landed. "InCall": the handset was lifted
      // first and the call goes on; "Idle": the caller gave up. Not a failure: the banner closes as it would on
      // the hub's own InCall / Idle (ANSWERED or CALL ENDED, then the fade), with no error. If the hub event
      // already closed it, the call is no longer tracked and this changes nothing.
      lock (_gate)
      {
        changed = call.DeclineAttempt == attempt
          && EndCallsLocked(c => ReferenceEquals(c, call),
            outcome.WasAnswered ? IncomingCallCloseReason.Answered : IncomingCallCloseReason.Ended);
      }
      if (changed)
      {
        RaiseChanged();
      }
      return;
    }

    lock (_gate)
    {
      changed = FailDeclineLocked(call, attempt);
    }
    if (changed)
    {
      _logger.LogWarning("The phone service did not accept the decline; the banner stays up");
      RaiseChanged();
    }
  }

  // ── hub ───────────────────────────────────────────────────────────

  private void OnIncomingCall(string phoneId, string phoneNumber) => OnRinging(phoneId, phoneNumber);

  private void OnCallStateChanged(string phoneId, string state)
  {
    if (string.Equals(state, "Ringing", StringComparison.OrdinalIgnoreCase))
    {
      // The number follows in IncomingCall when RotaryPhone has one.
      OnRinging(phoneId, null);
      return;
    }

    bool changed;
    lock (_gate)
    {
      changed = EndCallsLocked(c => SamePhone(c.PhoneId, phoneId), ReasonFor(state));
    }
    if (changed)
    {
      RaiseChanged();
    }
  }

  private void OnRinging(string? phoneId, string? number, string? callId = null)
  {
    TrackedCall call;
    string? lookupNumber = null;
    lock (_gate)
    {
      if (_disposed)
      {
        return;
      }

      var existing = _ringing.LastOrDefault(c => SamePhone(c.PhoneId, phoneId));
      if (existing is not null)
      {
        // The same call: the IncomingCall that follows Ringing, or a caller-ID update. The caller line
        // only ever gains information — a later "Unknown" never replaces a known number.
        call = existing;
        call.PhoneId ??= phoneId;
        call.CallId ??= callId;
        call.LastConfirmed = _time.GetUtcNow();
        call.NumberSettled |= number is not null;
        if (IsUsableNumber(number) && !SameNumber(call.Number, number))
        {
          call.Number = number;
          call.ContactName = null;
          call.ResolvingName = true;
          lookupNumber = number;
        }
      }
      else
      {
        call = NewCallLocked(phoneId, number, callId);
        lookupNumber = call.Number;
      }
    }

    if (lookupNumber is not null)
    {
      LastNameLookup = ResolveNameAsync(call, lookupNumber);
    }
    RaiseChanged();
  }

  /// <summary>Starts tracking a new ringing call, newest last, and takes the banner for it.</summary>
  private TrackedCall NewCallLocked(string? phoneId, string? number, string? callId)
  {
    var call = new TrackedCall
    {
      Id = ++_nextCallId,
      PhoneId = phoneId,
      CallId = callId,
      Number = IsUsableNumber(number) ? number : null,
      NumberSettled = number is not null,
      LastConfirmed = _time.GetUtcNow(),
    };
    call.ResolvingName = call.Number is not null;
    _ringing.Add(call);
    _lastCloseReason = null;
    // A fresh ring cancels any exit beat still on screen (spec §7).
    CancelExitLocked();
    EnsurePollTimerLocked();
    _logger.LogDebug("Incoming-call banner shown for phone {PhoneId}", phoneId ?? "(default)");
    return call;
  }

  // ── status read ───────────────────────────────────────────────────

  private async Task SeedAsync()
  {
    PhoneCallStateDto? state = await ReadStatusAsync(phoneId: null);
    if (state is not null && string.Equals(state.CallState, "Ringing", StringComparison.OrdinalIgnoreCase))
    {
      // Asked without a phone id, the status route answers for RotaryPhone's default (first) phone and does
      // not name it — hence the null id, which matches whichever phone the hub names next.
      OnRinging(null, state.IncomingNumber ?? "", state.CallId);
    }
  }

  private void OnPollTimer(object? _)
  {
    // One read at a time. With RotaryPhone hung, the client's 10 s timeout would otherwise stack about
    // four reads per circuit during a ring, on a box where load correlates with audio distortion.
    if (!LastPoll.IsCompleted)
    {
      return;
    }
    LastPoll = PollAsync();
  }

  private async Task PollAsync()
  {
    List<(TrackedCall Call, string? PhoneId)> calls;
    lock (_gate)
    {
      if (_ringing.Count == 0 || _disposed)
      {
        return;
      }
      // The phone ids are read here, under the lock, like every other field of a tracked call.
      calls = _ringing.Select(c => (c, c.PhoneId)).ToList();
    }

    // Each tracked call's own phone (null = the default phone, for a call first seen by the start-up read).
    var reads = new List<(TrackedCall Call, PhoneCallStateDto? State)>(calls.Count);
    foreach (var (call, phoneId) in calls)
    {
      reads.Add((call, await ReadStatusAsync(phoneId)));
    }

    bool changed = false;
    var lookups = new List<(TrackedCall Call, string Number)>();
    lock (_gate)
    {
      if (_disposed)
      {
        return;
      }

      foreach (var (call, state) in reads)
      {
        if (state is null || !_ringing.Contains(call))
        {
          continue;
        }

        if (!string.Equals(state.CallState, "Ringing", StringComparison.OrdinalIgnoreCase))
        {
          changed = EndCallsLocked(c => ReferenceEquals(c, call), ReasonFor(state.CallState)) || changed;
          continue;
        }

        if (call.CallId is not null && state.CallId is not null
            && !string.Equals(call.CallId, state.CallId, StringComparison.Ordinal))
        {
          // Ringing, but a DIFFERENT call: the Idle that ended the tracked one was lost, and the next ring
          // merged into it (a touch-closed one would have kept the new call off the screen). Split it off as
          // the new call it is — fresh, not touched away, no exit beat for the old one.
          // ⚠ This assumes the Idle was LOST. If it is merely LATE (a hub lagging by seconds without
          // disconnecting), it arrives after the split and ends the new call, and the late Ringing then starts
          // it again: a CALL ENDED flash and a second announcement. Accepted; RotaryPhone replays nothing on a
          // real drop, which is the case this exists for.
          _ringing.Remove(call);
          call.DeclineTimer?.Dispose();
          call.DeclineTimer = null;
          var fresh = NewCallLocked(call.PhoneId, state.IncomingNumber ?? "", state.CallId);
          if (fresh.Number is not null)
          {
            lookups.Add((fresh, fresh.Number));
          }
          changed = true;
          continue;
        }

        call.CallId ??= state.CallId;
        call.LastConfirmed = _time.GetUtcNow();
        if (!call.NumberSettled)
        {
          call.NumberSettled = true;
          changed = true;
        }
        if (IsUsableNumber(state.IncomingNumber) && !SameNumber(call.Number, state.IncomingNumber))
        {
          call.Number = state.IncomingNumber;
          call.ContactName = null;
          call.ResolvingName = true;
          lookups.Add((call, state.IncomingNumber!));
          changed = true;
        }
      }

      var now = _time.GetUtcNow();
      changed = EndCallsLocked(c => now - c.LastConfirmed >= StaleAfter, IncomingCallCloseReason.Stale) || changed;
    }

    foreach (var (call, number) in lookups)
    {
      LastNameLookup = ResolveNameAsync(call, number);
    }
    if (changed)
    {
      RaiseChanged();
    }
  }

  private async Task<PhoneCallStateDto?> ReadStatusAsync(string? phoneId)
  {
    try
    {
      return await _phoneApi.GetCallStateAsync(phoneId);
    }
    catch (Exception ex)
    {
      // GetCallStateAsync maps its own failures to null; this guards the unexpected.
      _logger.LogDebug(ex, "Reading the phone status for the incoming-call banner failed");
      return null;
    }
  }

  // ── decline deadline ──────────────────────────────────────────────

  private void OnDeclineDeadline(TrackedCall call, int attempt)
  {
    bool changed;
    lock (_gate)
    {
      changed = FailDeclineLocked(call, attempt);
    }
    if (changed)
    {
      _logger.LogWarning("The call was still ringing {Seconds} s after Ignore; the banner reports a failure",
        DeclineDeadline.TotalSeconds);
      RaiseChanged();
    }
  }

  // Only the attempt still outstanding on a call still ringing can fail: a late answer for an earlier
  // press, or for a call that has since ended, changes nothing.
  private bool FailDeclineLocked(TrackedCall call, int attempt)
  {
    if (!_ringing.Contains(call) || call.DeclineAttempt != attempt
        || call.Decline != IncomingCallDeclineState.Declining)
    {
      return false;
    }
    call.Decline = IncomingCallDeclineState.Failed;
    call.DeclineTimer?.Dispose();
    call.DeclineTimer = null;
    return true;
  }

  // ── caller name ───────────────────────────────────────────────────

  /// <summary>
  /// Resolves a contact name the way the API's announcement does (<c>PhoneContactLookupService</c>), PHN-14:
  /// the stored synced phone books first — the API's <c>/api/bluetooth/pbap/lookup</c>, which searches every
  /// phone ever synced, connected or not — then RotaryPhone's own contacts list. When both have the number,
  /// the synced phone book wins.
  /// </summary>
  private async Task ResolveNameAsync(TrackedCall call, string number)
  {
    string? name = null;
    try
    {
      if (_contacts is not null)
      {
        // A circuit-cached "no such contact" is not trusted for a call: the kiosk circuit lives for days, and a
        // phone synced since the miss (or a pre-PHN-14 404 for "no phone connected") must not hide the name.
        name = _contacts.TryResolve(number) ?? await _contacts.ResolveAsync(number, retryCachedMiss: true);
      }

      if (string.IsNullOrWhiteSpace(name))
      {
        name = FindInContacts(await _phoneApi.GetContactsAsync(), number);
      }
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Caller-name lookup failed for {Number}", LogSafeText.ForPhone(number));
      name = null;
    }

    lock (_gate)
    {
      // A result for a number the call no longer shows, or for a call that has gone, is dropped.
      if (!_ringing.Contains(call) || !SameNumber(call.Number, number))
      {
        return;
      }
      call.ResolvingName = false;
      call.ContactName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }
    RaiseChanged();
  }

  // The same matching rule as the API's PBAP lookup and its announcement (PhoneNumberNormalizer.FindMatch).
  internal static string? FindInContacts(IEnumerable<ContactDto>? contacts, string number) =>
    PhoneNumberNormalizer.FindMatch(
      contacts?.Where(c => !string.IsNullOrWhiteSpace(c.Name)), c => c.PhoneNumber, number)?.Name;

  // ── ending and the exit beat ──────────────────────────────────────

  /// <summary>
  /// Drops every tracked call that matches, and returns whether anything on screen changed. When the call
  /// on screen ends and no other ringing call takes its place, its banner plays the exit beat.
  /// </summary>
  private bool EndCallsLocked(Func<TrackedCall, bool> match, IncomingCallCloseReason reason)
  {
    var shownBefore = ShownLocked();
    var ended = _ringing.Where(match).ToList();
    if (ended.Count == 0)
    {
      return false;
    }

    foreach (var call in ended)
    {
      _ringing.Remove(call);
      call.DeclineTimer?.Dispose();
      call.DeclineTimer = null;
    }
    if (_ringing.Count == 0)
    {
      DisposePollTimerLocked();
    }
    _logger.LogDebug("Incoming-call banner: {Count} call(s) ended ({Reason})", ended.Count, reason);

    if (shownBefore is null || !ended.Contains(shownBefore))
    {
      // What was on screen is still on screen (or nothing was: a touch-closed call ending changes nothing).
      return false;
    }

    _lastCloseReason = reason;
    if (ShownLocked() is null)
    {
      BeginExitLocked(shownBefore,
        reason == IncomingCallCloseReason.Answered ? IncomingCallPhase.Answered : IncomingCallPhase.Ended,
        ExitHold);
    }
    return true;
  }

  private void BeginExitLocked(TrackedCall call, IncomingCallPhase phase, TimeSpan hold)
  {
    _exitTimer?.Dispose();
    _exit = new Exit(call, phase) { Leaving = hold <= TimeSpan.Zero };
    var exit = _exit;
    _exitTimer = _time.CreateTimer(_ => OnExitTimer(exit), null, hold <= TimeSpan.Zero ? ExitFade : hold,
      Timeout.InfiniteTimeSpan);
  }

  private void OnExitTimer(Exit exit)
  {
    lock (_gate)
    {
      if (!ReferenceEquals(_exit, exit))
      {
        return;
      }
      if (!exit.Leaving)
      {
        // The hold is over: fade, then unmount.
        exit.Leaving = true;
        _exitTimer?.Dispose();
        _exitTimer = _time.CreateTimer(_ => OnExitTimer(exit), null, ExitFade, Timeout.InfiniteTimeSpan);
      }
      else
      {
        CancelExitLocked();
      }
    }
    RaiseChanged();
  }

  private void CancelExitLocked()
  {
    _exit = null;
    _exitTimer?.Dispose();
    _exitTimer = null;
  }

  // ── helpers ───────────────────────────────────────────────────────

  /// <summary>The ringing call that owns the banner: the newest one not touched away.</summary>
  private TrackedCall? ShownLocked() => _ringing.LastOrDefault(c => !c.Dismissed);

  private static IncomingCallCloseReason ReasonFor(string? state) =>
    string.Equals(state, "InCall", StringComparison.OrdinalIgnoreCase)
      ? IncomingCallCloseReason.Answered
      : IncomingCallCloseReason.Ended;

  // A null id is the start-up read's "the default phone": it matches whichever phone speaks next.
  private static bool SamePhone(string? tracked, string? incoming) =>
    tracked is null || incoming is null || string.Equals(tracked, incoming, StringComparison.Ordinal);

  private static bool IsUsableNumber(string? number) =>
    !string.IsNullOrWhiteSpace(number)
    && !string.Equals(number.Trim(), "Unknown", StringComparison.OrdinalIgnoreCase);

  // Digits decide ("+1 555…" and "(555)…" are one caller); two digit-less values fall back to text.
  private static bool SameNumber(string? a, string? b)
  {
    string na = PhoneNumberNormalizer.Normalize(a ?? "");
    string nb = PhoneNumberNormalizer.Normalize(b ?? "");
    return na.Length > 0 || nb.Length > 0
      ? na == nb
      : string.Equals(a?.Trim(), b?.Trim(), StringComparison.Ordinal);
  }

  private void EnsurePollTimerLocked()
  {
    _pollTimer ??= _time.CreateTimer(OnPollTimer, null, PollInterval, PollInterval);
  }

  private void DisposePollTimerLocked()
  {
    _pollTimer?.Dispose();
    _pollTimer = null;
  }

  private IncomingCallBannerSnapshot BuildSnapshotLocked()
  {
    var shown = ShownLocked();
    if (shown is not null)
    {
      return Draw(shown, IncomingCallPhase.Ringing, leaving: false);
    }
    if (_exit is not null)
    {
      return Draw(_exit.Call, _exit.Phase, _exit.Leaving);
    }
    return IncomingCallBannerSnapshot.Hidden with { CanDecline = CanDecline, LastCloseReason = _lastCloseReason };
  }

  private IncomingCallBannerSnapshot Draw(TrackedCall call, IncomingCallPhase phase, bool leaving)
  {
    var kind = call.ContactName is not null ? IncomingCallCallerKind.Contact
      : call.Number is not null ? IncomingCallCallerKind.Number
      : IncomingCallCallerKind.Unknown;

    string formatted = PhoneCallFormatting.FormatPhoneNumber(call.Number);
    bool awaiting = kind == IncomingCallCallerKind.Unknown && !call.NumberSettled;
    return new IncomingCallBannerSnapshot
    {
      IsVisible = true,
      CallId = call.Id,
      Phase = phase,
      IsLeaving = leaving,
      CallerKind = kind,
      PrimaryText = kind switch
      {
        IncomingCallCallerKind.Contact => call.ContactName!,
        IncomingCallCallerKind.Number => formatted,
        _ => UnknownCallerText,
      },
      SecondaryText = kind switch
      {
        IncomingCallCallerKind.Contact => formatted.Length > 0 ? formatted : null,
        IncomingCallCallerKind.Unknown when !awaiting => NoCallerIdText,
        _ => null,
      },
      Monogram = kind == IncomingCallCallerKind.Contact ? MonogramFor(call.ContactName!) : null,
      IsResolvingName = call.ResolvingName,
      IsAwaitingCallerId = awaiting,
      DeclineState = call.Decline,
      CanDecline = CanDecline,
      LastCloseReason = _lastCloseReason,
    };
  }

  /// <summary>
  /// Initials per spec §4: <c>"Anderson, Carol"</c> → <c>CA</c>; otherwise the first letter of the first
  /// and last words; one word → one letter. Null — the host shows the glyph — when the name does not start
  /// with a letter.
  /// </summary>
  internal static string? MonogramFor(string name)
  {
    string trimmed = name.Trim();
    if (trimmed.Length == 0 || !char.IsLetter(trimmed[0]))
    {
      return null;
    }

    int comma = trimmed.IndexOf(',');
    if (comma > 0 && comma < trimmed.Length - 1)
    {
      string? given = FirstLetter(trimmed[(comma + 1)..]);
      string? family = FirstLetter(trimmed[..comma]);
      if (given is not null && family is not null)
      {
        return given + family;
      }
    }

    var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    string first = FirstLetter(words[0])!;
    string? last = words.Length > 1 ? FirstLetter(words[^1]) : null;
    return last is null ? first : first + last;
  }

  private static string? FirstLetter(string text)
  {
    foreach (char c in text)
    {
      if (char.IsLetter(c))
      {
        return char.ToUpperInvariant(c).ToString();
      }
    }
    return null;
  }

  private void RaiseChanged()
  {
    try
    {
      Changed?.Invoke();
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "An incoming-call banner subscriber threw");
    }
  }

  public void Dispose()
  {
    lock (_gate)
    {
      if (_disposed)
      {
        return;
      }
      _disposed = true;
      DisposePollTimerLocked();
      CancelExitLocked();
      foreach (var call in _ringing)
      {
        call.DeclineTimer?.Dispose();
      }
      _ringing.Clear();
    }

    _hub.IncomingCall -= OnIncomingCall;
    _hub.CallStateChanged -= OnCallStateChanged;
  }

  private sealed class TrackedCall
  {
    public long Id { get; init; }
    public string? PhoneId { get; set; }

    // RotaryPhone's call id, from the first status read that sees this call ringing (the hub never sends it).
    public string? CallId { get; set; }
    public string? Number { get; set; }
    public string? ContactName { get; set; }
    public bool ResolvingName { get; set; }

    // False until a number (or "Unknown") has arrived or a status read has confirmed there is none —
    // that is, while only the bare Ringing has been seen and the IncomingCall that follows it may be in
    // flight.
    public bool NumberSettled { get; set; }
    public bool Dismissed { get; set; }
    public IncomingCallDeclineState Decline { get; set; }
    public int DeclineAttempt { get; set; }
    public ITimer? DeclineTimer { get; set; }
    public DateTimeOffset LastConfirmed { get; set; }
  }

  private sealed class Exit(TrackedCall call, IncomingCallPhase phase)
  {
    public TrackedCall Call { get; } = call;
    public IncomingCallPhase Phase { get; } = phase;
    public bool Leaving { get; set; }
  }
}

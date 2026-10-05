# PLAN — `PHN-9` · The badge that reads a latch instead of fetching

**Row:** [`docs/queue/PHN-9.md`](../../docs/queue/PHN-9.md) · **Branch:** `fix/phn-9-unread-badge-hydration`
**Precedent:** `BellHealthService` (`src/Radio.Web/Services/BellHealthService.cs`), ADR-024, [`PHN-7`](../../docs/queue/PHN-7.md)
**Written:** 2026-09-09 (Planner) · **Scope:** `src/Radio.Web` + `tests/Radio.Web.Tests` + docs. No hardware, no audio path, no `Radio.API` change.

---

## 0. Read this before Task 1

### 0.1 The defect in one sentence, and why the option question is not part of it

`MainLayout` **seeds the topbar PHONE badge by reading an in-memory singleton** (`MainLayout.razor:402`,
`_phoneUnread = PhoneUnread.Count;`) whose only writer is `PhonePage` while mounted. A fresh process starts
that singleton at `0`, the badge is gated `@if (_phoneUnread > 0)` (`:224-227`), and `radio-kiosk-launch:11`
opens the kiosk at **root**. So after every `radio-web` restart the badge is absent for the life of the
process, and deploys restart `radio-web`.

⭐ **The fix is "`MainLayout` must fetch, not read a latch." That sentence is true whether the fetch hits one
endpoint or three**, which is why this plan does not branch on the endpoint question. §7 costs the one-endpoint
future and shows it is a **single call site**, not a redesign.

### 0.2 ⛔ The row is CONFIRMED, and I could not falsify any of its load-bearing claims

The row shipped a kill condition and survived it (owner, 2026-09-09: *"badge appears and persists"*), with
unplanned corroboration across the 12:11Z deploy: **14 → absent → 15**. Everything below was re-derived here
from source rather than inherited:

| Row claim | Verdict | Evidence |
|---|---|---|
| `PhoneUnreadState` is a process-wide singleton starting at `0` | ✅ | `PhoneUnreadState.cs:12`, `Program.cs:431` |
| **17** `Set` call sites, all in `PhonePage.razor` | ✅ **exactly 17, verified by grep** | `:282, 367, 416, 457, 509, 686, 700, 727, 746, 764, 777, 812, 857, 871, 912, 936, 997` |
| `MainLayout` seeds by reading, not fetching | ✅ | `MainLayout.razor:402` |
| `MainLayout` injects no phone hub service | ✅ | full `@inject` list `:5-28` — no `PhoneHubService`, `GvTrunkHubService`, `GvBridgeStatusService` |
| No server-side unread total exists | ✅ **twice** | `src/Radio.Web` grep; and an independent enumeration of RotaryPhone's **24 GET routes across 6 controllers** — not one returns a scalar count |
| `Dispose` never resets the count | ✅ | `PhonePage.razor:1242-1256` — nine unsubscribes, no `PhoneUnread` reset |
| The count is `Missed + Unheard + UnreadThreads` | ✅ | `PhonePage.razor:221-227` |

**I found nothing false in the row.** §8 lists four things it does not say that change the design.

### 0.3 ⭐ `C-901` — THE PUSH CHANNEL CAN ONLY EVER MAKE THE BADGE GO **UP**

This is the single most important fact in the plan and it is in neither the row nor the queue entry.

`PhoneHubService` exposes `ReadStateChanged` (`PhoneHubService.cs:36`) whose doc (`:29-36`) says it fires *"when
read-state changes from ANY source"*, with **path (b)** — externally-originated flips, i.e. the owner reading a
message on the handset — arriving *"once RotaryPhone's poller-flip fast-follow lands."*

⛔ **It has not landed, and path (a) is dark as well.** Checked directly rather than assumed:

- `GvThreadPoller.cs` contains exactly two references to the event — the declaration (`:34`) and a pass-through
  `NotifyReadStateChanged` (`:43`). **The poller never raises it from its own polling.** RotaryPhone's own
  contract doc says `GvHighWaterMark` *"does not track the read-flag of already-seen items."*
- The only broadcasters are `GvSmsController.cs:229` and `GvVoicemailController.cs:163`, both labelled *"path-a"*
  — our own mark-read writes.
- **Both mark routes ship disabled.** RotaryPhone defaults `EnableMarkRead = false` (→ `409 markread_disabled`),
  and our side defaults `RotaryPhone:Gv:MarkReadEnabled: false` (`appsettings.json:22`).

**So today no event of any kind can tell this application that an item became read.** `GvVoicemailReceived`,
`GvSmsReceived` and `CallHistoryUpdated` all signal *new* items.

⭐ **Consequence that decides §2 and §5: a re-fetch is not a safety net for the decrement case, it is the only
mechanism that exists for it.** That is precisely the 14-vs-15 observation — a latch that had been
*under*-reporting for a whole process lifetime, with nothing able to correct it.

### 0.4 ⛔ `C-902` — TWO OF THE THREE CALLS ARE LIVE GOOGLE ROUND TRIPS, NOT CACHE READS

The row says *"three REST calls on every app start … price it before choosing."* Priced, and the honest answer
is worse than "three calls":

| Call | Backing store | Cost |
|---|---|---|
| `GET /api/callhistory` | **local SQLite**, connection held open, WAL (`SqliteCallHistoryService.cs:34-41,151-168`) | cheap |
| `GET /api/gvbridge/voicemail?count=20` | ⛔ **live `PostAsync` to `clients6.google.com`** (`GvVoicemailController.cs:42` → `GvThreadClient.cs:158`). **No list cache** — `GvVoicemailCache` caches *recording bytes* only (`GvVoicemailCache.cs:8-13`) | expensive |
| `GET /api/gvbridge/sms/threads?count=20` | ⛔ **same live POST** (`GvSmsController.cs:58` → `GvThreadClient.cs:158`) | expensive |

Two amplifiers:

1. **A 401/403 doubles the upstream call** — attempt → `TryRecoverAuthAsync` → replay once
   (`GvThreadClient.cs:117-131`). A poll landing during cookie decay triggers a full auth-recovery ladder.
2. ⭐ **RotaryPhone's `GvThreadPoller` already fetches this exact data** — `ListRecentMessagesAsync(count: 50)`
   (`:89`) and `ListVoicemailsAsync(count: 50)` (`:129`) at **15 s active / 60 s idle / 120 s backoff** — **and
   throws the lists away**, keeping only a per-thread max timestamp. Our poll is therefore *duplicate* upstream
   load on data the box already had in hand seconds earlier.

⚠ **The `/api/callhistory` bound is a storage invariant, not an API contract.** The route calls
`GetCallHistory()` with the default `maxEntries: 0` = unlimited (`CallHistoryController.cs:29`,
`SqliteCallHistoryService.cs:156-158`); what caps it at ~100 rows is a **write-time ring trim**
(`MaxCallHistoryEntries = 100`). Raising that config silently uncaps every consumer of this route, including
this badge. Do not write a comment claiming the response is bounded by the API.

### 0.5 ⭐ `C-903` — RotaryPhone's own ADR already prescribes the cadence this plan uses

Their contract doc, §"polling guidance":

> *"All read endpoints are safe to poll. **Slow-changing reads** RadioConsole may poll directly (threads list at
> **30–60 s** as a backstop); **fresh-message awareness comes from the SignalR push**, not polling."*

**Push-primary, poll-as-backstop.** §2's design is that model, with the backstop deliberately slower than their
60 s ceiling because §0.3 means our backstop covers only the decrement case.

### 0.6 ⛔ `C-904` — DO NOT "FIX" THE MISSED-CALL THIRD OF THE COUNT

`MissedCallCount` (`PhonePage.razor:221-223`) counts `Incoming && NotAnswered` over the retained history.
**`CallHistoryEntry` has no read/seen/acknowledged field, and the SQLite table has no such column**
(`CallHistoryEntry.cs:43-89`, `SqliteCallHistoryService.cs:45-57`). So that third of the badge **cannot
decrement** except by a call ageing out of the 100-row ring or the owner clearing history.

⚠ Two traps follow:

- ⛔ **It is a deliberate local decision, not drift.** `PhoneUnreadState.cs:8` records *"Missed calls DO
  contribute (owner decision 2)"*. RotaryPhone's own badge handoff specifies **voicemail + texts only** — the
  missed-call term is ours. **Do not remove it to match their spec.**
- ⛔ **Do not add a seen-flag.** That is a RotaryPhone schema change, an owner decision, and out of scope. §9
  files it as a follow-up row.

**What the plan must not do is claim the fix makes the whole badge self-correcting.** It makes the *voicemail
and SMS* terms self-correcting. Say that, in the code and in the PR body.

---

## 1. Scope

### 1.1 In scope

1. **Hydration** — the badge is correct on a process that has never mounted `PhonePage`. (The row.)
2. ⭐ **Staleness** — the voicemail/SMS terms re-derive periodically, so a handset read decrements the badge.
   **§2.3 argues this is not scope creep but the same mechanism.**
3. **One definition of the total**, shared by the page and the hydrator, so they cannot drift.
4. The RED-first seam (§5) and the mutation matrix (§6).

### 1.2 Out of scope — and why, explicitly

| Excluded | Why |
|---|---|
| A server-side aggregate endpoint | RotaryPhone's, not ours. §7 shows adopting one later is **one call site**. |
| A missed-call seen-flag | Their schema + an owner decision (§0.6). Follow-up row in §9. |
| Enabling `MarkReadEnabled` | Independent flag, independent risk, unrelated to hydration. |
| Fixing `PhoneUnreadState.Set`'s plain multicast invoke | Explicitly **not** in `UI-6`'s scope per `ConsolePlaybackState.cs:44-46`; only the starvation half can bite and no new subscriber is added here. |
| Changing what the badge counts | §0.6. |

---

## 2. Design

### 2.1 Shape — `BellHealthService`, verbatim, for the reason its own class doc gives

`BellHealthService.cs:12-18` already argues this exact case about the *fault* badge:

> *"Why a background poll and not the `PhoneUnreadState` publish-from-the-page pattern: the fault badge's entire
> job is to be visible from every page **before** anyone thinks to open /phone. A value published only by
> `PhonePage` would stay dark until the user had already navigated to the surface it is meant to send them to."*

⭐ **The repo therefore contains a written, shipped argument for this row's fix, aimed at the sibling badge.**
`MainLayout.razor:406-408` states the contrast in the negative. This plan makes the PHONE unread badge the
fourth authoritative hydrator alongside Queue (`:475`), Mute (`:392`) and Encoder (`:435`).

Three pieces:

- **`PhoneUnreadRules`** (new, `src/Radio.Web/Models/`) — pure static; the **single** definition of the three
  predicates and the sum. Mirrors `BellHealthRules` / `EncoderFaultRules`, which exist for exactly this reason
  (`MainLayout.razor:1287-1289`).
- **`PhoneUnreadHydrationService`** (new, `src/Radio.Web/Services/`) — singleton + `IHostedService` +
  scope-per-fetch, `Publish`-style guards. Writes into the existing `PhoneUnreadState`.
- **`MainLayout`** — gains one awaited seed call. **`PhonePage` keeps all 17 `Set` sites**; while it is mounted
  it stays the fresher writer.

### 2.2 Three triggers, one recount

| Trigger | Fires | Purpose | Upstream cost |
|---|---|---|---|
| **Seed** — `MainLayout.OnInitializedAsync` awaits `EnsureHydratedAsync()` | once per process (cached afterwards) | ⭐ **the row's defect** | 3 calls, **once** |
| **Push** — `GvVoicemailReceived` / `GvSmsReceived` / `CallHistoryUpdated`, debounced 2 s | on real events | prompt **increments** | 3 calls per event burst |
| **Backstop** — `PeriodicTimer`, default **300 s** | while the host runs | ⭐ **the only path that can DECREMENT** (§0.3) | 3 calls / 5 min, skipped when `PhonePage` is publishing |

⚠ **`EnsureHydratedAsync` is cached, not per-circuit.** A second browser, or a kiosk reload, costs **zero**
network. This matters: `Queue`/`Mute`/`Encoder` each pay their seed *per circuit*, and copying that here would
put two live Google POSTs on every reload.

### 2.3 ⭐ Why the staleness half is IN scope

**It is not extra work. It is the same work, and excluding it would be the expensive choice.**

- The hydrator must exist regardless — that is the row.
- Once it exists, giving it a `PeriodicTimer` is **~10 lines** (`BellHealthService.cs:132-144` verbatim).
- ⛔ **Excluding it ships a hydrator that is correct exactly once per process and then rots** — which is the
  defect's own shape, moved from "starts wrong" to "goes wrong." A row whose evidence is *"the latch
  under-reported for an entire process lifetime and nobody noticed"* cannot honestly close by adding a second
  thing that goes stale silently.
- ⭐ Per §0.3 **nothing else can decrement the badge.** Deferring the timer defers the 14→15 defect indefinitely,
  because no follow-up trigger exists to catch it.

### 2.4 The failed-fetch rule — ⛔ inverted from `GvBridgeStatusService`, deliberately

**A failed fetch must retain the last known count, never publish `0`.** `BellHealthService.Publish(null)`
returns without applying (`:107-112`), and its `PollOnceAsync` catch retains health (`:156-164`).
`GvBridgeStatusService` does the opposite — `ApplyStatus(null)` on failure (`:139`) — because a *status* that
cannot be fetched is genuinely unknown.

⚠ **Follow `BellHealthService`, not `GvBridgeStatusService`.** Publishing `0` on a dropped request would make the
badge vanish and reappear, and §0.4's auth-recovery ladder makes dropped requests a normal event, not a rare one.

### 2.5 Skip the backstop while `PhonePage` is publishing

`PhonePage`'s 30 s backstop already re-fetches voicemails and threads and publishes. Running our 300 s timer on
top is pure duplicate Google load in the one case where the data is already fresh.

`PhoneUnreadState` gains `LastPublishedUtc`; the timer callback returns early when a publish landed within the
interval. **Cheap, and it makes the common "owner is on the Phone page" case cost nothing extra.**

### 2.6 Alternatives considered and rejected

| Alternative | Rejected because |
|---|---|
| **Seed once at boot, no timer** | §2.3 — leaves the decrement defect with no trigger that can ever catch it. |
| **Poll at `BellHealthService`'s 15 s** | §0.4 — 5,760 live Google POSTs/day ×2, on top of RotaryPhone's own poller. |
| **Push-only, no timer** | ⛔ §0.3 — the push channel cannot decrement. This is the trap the row's *"no hub carries an unread count"* line points at without naming the consequence. |
| **Per-circuit seed (Queue/Mute/Encoder shape)** | §2.2 — two live Google POSTs per reload. |
| **Hydrator stands down while any `PhonePage` is mounted** (mounted-count) | Real coordination state for a cosmetic race; §2.5's timestamp gets the same saving. Both writers derive from `PhoneUnreadRules`, so a disagreement window is seconds wide and both values are recent truth. |
| **Ask RotaryPhone first, block the row** | §7. |

---

## 3. Tasks

### Task 1 — `PhoneUnreadRules`: one definition of the total

**New file `src/Radio.Web/Models/PhoneUnread.cs`.**

```csharp
using Radio.Web.Models;

namespace Radio.Web.Models;

/// <summary>
/// The single definition of the topbar PHONE unread total.
///
/// <para>
/// Exists for the reason <see cref="BellHealthRules"/> and <c>EncoderFaultRules</c> exist: the count is
/// derived in two places — <c>PhonePage</c> while it is mounted, and <c>PhoneUnreadHydrationService</c>
/// when it is not — and two copies of three predicates are two chances to drift. Both callers route
/// through here so a change to what "unread" means lands in one file.
/// </para>
///
/// <para>
/// ⚠ The MISSED-CALL term cannot decrement. <c>CallHistoryEntry</c> carries no read/seen field and the
/// RotaryPhone SQLite table has no such column, so this term falls only when an entry ages out of the
/// retained history or the owner clears it. That is a deliberate local choice — PhoneUnreadState's own
/// doc records "Missed calls DO contribute (owner decision 2)", and RotaryPhone's badge handoff specifies
/// voicemail + texts only. Do NOT drop the term to match their spec, and do NOT claim anywhere that this
/// total is fully self-correcting: the voicemail and SMS terms are, and the missed-call term is not.
/// </para>
/// </summary>
public static class PhoneUnreadRules
{
  /// <summary>Incoming calls that were never answered. Null list → 0 (not yet fetched).</summary>
  public static int MissedCalls(IReadOnlyList<CallHistoryEntryDto>? callHistory) =>
    callHistory?.Count(c =>
      c.Direction == CallDirection.Incoming
      && c.AnsweredOn == CallAnsweredOn.NotAnswered) ?? 0;

  /// <summary>Voicemails not yet heard. Null list → 0.</summary>
  public static int UnheardVoicemails(IReadOnlyList<VoicemailItemDto>? voicemails) =>
    voicemails?.Count(v => !v.IsRead) ?? 0;

  /// <summary>SMS threads carrying at least one unread message. Null list → 0.</summary>
  public static int UnreadThreads(IReadOnlyList<SmsThreadDto>? threads) =>
    threads?.Count(t => t.HasUnread) ?? 0;

  /// <summary>
  /// The badge number. A null list contributes 0 rather than suppressing the whole total, which is what
  /// lets a partial fetch still publish something true about the terms that did arrive.
  /// </summary>
  public static int Total(
    IReadOnlyList<CallHistoryEntryDto>? callHistory,
    IReadOnlyList<VoicemailItemDto>? voicemails,
    IReadOnlyList<SmsThreadDto>? threads) =>
    MissedCalls(callHistory) + UnheardVoicemails(voicemails) + UnreadThreads(threads);
}
```

⚠ **Verify the three DTO member names against `src/Radio.Web/Models/ApiModels.cs` before compiling** —
`CallHistoryEntryDto.Direction` / `.AnsweredOn`, `VoicemailItemDto.IsRead` (`ApiModels.cs:1209`),
`SmsThreadDto.HasUnread` (`:1233`). If any differs, fix the rule, not the DTO.

---

### Task 2 — `PhonePage` routes through the rules (no behaviour change)

**Edit `src/Radio.Web/Components/Pages/PhonePage.razor:221-227`.** This is a pure refactor whose whole point is
that Task 1 becomes the only definition.

```csharp
  // PHN-9 — these three predicates moved to PhoneUnreadRules so PhonePage and
  // PhoneUnreadHydrationService cannot drift apart on what "unread" means. Behaviour is unchanged;
  // the expressions below are the originals, relocated.
  private int MissedCallCount => PhoneUnreadRules.MissedCalls(_callHistory);
  private int UnheardVoicemailCount => PhoneUnreadRules.UnheardVoicemails(_voicemails);
  private int UnreadThreadCount => PhoneUnreadRules.UnreadThreads(_threads);
  private int UnreadSum => PhoneUnreadRules.Total(_callHistory, _voicemails, _threads);
```

⛔ **Do not touch any of the 17 `PhoneUnread.Set(UnreadSum)` call sites.** They keep the page authoritative while
mounted, and every one of them still reads the same property.

⚠ If `_callHistory` / `_voicemails` / `_threads` are declared as `List<T>?`, they satisfy `IReadOnlyList<T>?`
without a cast. If any is a bare array or an `IEnumerable<T>`, adjust the rule signature rather than adding a
`.ToList()` at the call site — an allocation per render is not worth it.

---

### Task 3 — `PhoneUnreadState` learns when it was last written

**Edit `src/Radio.Web/Services/PhoneUnreadState.cs`.** Additive; the existing four bUnit registrations
(`PhonePageTests.cs:89`, `ConsolePlaybackChipTests.cs:73`, and the two `PhonePage*Tests`) keep working unchanged.

```csharp
using System;

namespace Radio.Web.Services;

/// <summary>
/// UI-local unread sum (unheard voicemail + unread SMS threads + missed calls)
/// surfaced to the topbar /phone pill badge in MainLayout.
/// Singleton so every writer and the layout share one source of truth.
/// </summary>
/// <remarks>
/// ⚠ The class doc used to say "v1 counts are UI-local only — a hard reload re-derives from
/// isRead/hasUnread". That was true only if the reload landed on /phone, which is what PHN-9 fixed:
/// PhoneUnreadHydrationService now derives the same total without the page, so this is no longer a
/// page-published value. It is still not authoritative on its own — it holds whatever was last
/// published, by either writer.
/// </remarks>
public sealed class PhoneUnreadState
{
  private int _count;
  private readonly TimeProvider _timeProvider;

  /// <param name="timeProvider">
  /// Defaults to <see cref="TimeProvider.System"/>. Injectable so a test can drive
  /// <see cref="LastPublishedUtc"/> without sleeping — the house idiom (CLAUDE.md § Test Timing).
  /// </param>
  public PhoneUnreadState(TimeProvider? timeProvider = null)
  {
    _timeProvider = timeProvider ?? TimeProvider.System;
  }

  public int Count => _count;

  /// <summary>
  /// When <see cref="Set"/> last ACCEPTED a value, or <c>null</c> if it never has.
  ///
  /// <para>
  /// ⚠ Read this as "when did a writer last speak", not "when was the count last correct". A suppressed
  /// no-op Set does NOT advance it — see the note in <see cref="Set"/> for why that is the behaviour the
  /// backstop wants.
  /// </para>
  /// </summary>
  public DateTimeOffset? LastPublishedUtc { get; private set; }

  public event Action<int>? Changed;

  public void Set(int count)
  {
    if (count == _count)
    {
      // ⚠ Deliberately returns BEFORE stamping LastPublishedUtc. A mounted PhonePage re-publishes the
      // same total every 5s; stamping here would let an idle page suppress the hydration backstop
      // forever, and the backstop is the only thing that can DECREMENT the voicemail/SMS terms
      // (PHN-9 §0.3). Suppressing the no-op notification is still right — MainLayout re-renders on
      // every Changed.
      return;
    }
    _count = count < 0 ? 0 : count;
    LastPublishedUtc = _timeProvider.GetUtcNow();
    Changed?.Invoke(_count);
  }
}
```

⛔ **The early return placement is load-bearing and is a mutation target (§6, `M4`).** Stamping the timestamp
before the guard inverts §2.5 into a permanent backstop suppressor.

---

### Task 4 — `PhoneUnreadHydrationService`

**New file `src/Radio.Web/Services/PhoneUnreadHydrationService.cs`.**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Radio.Web.Models;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;

namespace Radio.Web.Services;

/// <summary>
/// Derives the topbar PHONE unread total without requiring anyone to have opened /phone, and keeps it
/// from going stale afterwards. Writes into <see cref="PhoneUnreadState"/>, which MainLayout already
/// binds to (PHN-9).
///
/// <para>
/// Why this exists at all is stated for the sibling badge in <see cref="BellHealthService"/>'s own class
/// doc: a value published only by PhonePage stays dark until the user has already navigated to the
/// surface it is meant to send them to. This is that argument applied to the unread count.
/// </para>
///
/// <para>
/// ⛔ THE BACKSTOP TIMER IS NOT BELT-AND-BRACES. No SignalR event this application can receive is able to
/// report that an item became READ: RotaryPhone's poller never raises ReadStateChanged from its own
/// polling (path (b) is unimplemented — GvHighWaterMark does not track the read flag of already-seen
/// items), and both mark-read routes ship disabled on both sides, so path (a) does not fire either. The
/// push events below signal only NEW items. A periodic re-fetch is therefore the ONLY mechanism that can
/// make the voicemail and SMS terms go DOWN. Deleting the timer silently reintroduces the defect that
/// filed this row — a latch that under-reported for an entire process lifetime.
/// </para>
///
/// <para>
/// ⚠ COST, because it decides the default interval. /api/callhistory is a local SQLite read, but
/// /api/gvbridge/voicemail and /api/gvbridge/sms/threads are UNCACHED live POSTs to Google, and a 401/403
/// replays each once through an auth-recovery ladder. RotaryPhone's GvThreadPoller already fetches the
/// same data every 15-60s and discards it, so every poll here is duplicate upstream load. That is why the
/// default is 300s rather than BellHealthService's 15s, and why EnsureHydratedAsync caches rather than
/// fetching per circuit. RotaryPhone's own contract doc prescribes the shape: "fresh-message awareness
/// comes from the SignalR push, not polling", with the list poll as a 30-60s backstop.
/// </para>
///
/// <para>
/// Radio.Web is UI-only with respect to phone functionality: this reads RotaryPhone.API over REST and
/// registers no RotaryPhone services.
/// </para>
/// </summary>
public sealed class PhoneUnreadHydrationService : IHostedService, IAsyncDisposable
{
  private readonly IServiceScopeFactory _scopeFactory;
  private readonly PhoneUnreadState _state;
  private readonly PhoneHubService _phoneHub;
  private readonly ILogger<PhoneUnreadHydrationService> _logger;
  private readonly TimeProvider _timeProvider;
  private readonly int _pollSeconds;
  private readonly int _debounceMilliseconds;

  private int _started;
  private int _stopped;
  private PeriodicTimer? _timer;
  private Task? _loop;
  private CancellationTokenSource? _cts;

  // Serializes EnsureHydratedAsync so N circuits opening at once produce ONE fetch, not N.
  private readonly SemaphoreSlim _hydrationGate = new(1, 1);
  private volatile bool _hydrated;

  // Collapses a burst of pushes (a conversation arriving as five SMS) into one recount.
  private CancellationTokenSource? _debounceCts;
  private readonly object _debounceGate = new();

  public PhoneUnreadHydrationService(
    IServiceScopeFactory scopeFactory,
    PhoneUnreadState state,
    PhoneHubService phoneHub,
    ILogger<PhoneUnreadHydrationService> logger,
    int pollSeconds = 300,
    int debounceMilliseconds = 2000,
    TimeProvider? timeProvider = null)
  {
    _scopeFactory = scopeFactory;
    _state = state;
    _phoneHub = phoneHub;
    _logger = logger;
    _pollSeconds = pollSeconds <= 0 ? 300 : pollSeconds;
    _debounceMilliseconds = debounceMilliseconds < 0 ? 2000 : debounceMilliseconds;
    _timeProvider = timeProvider ?? TimeProvider.System;
  }

  Task IHostedService.StartAsync(CancellationToken cancellationToken)
  {
    Start();
    return Task.CompletedTask;
  }

  Task IHostedService.StopAsync(CancellationToken cancellationToken) => StopLoopAsync();

  /// <summary>
  /// Subscribes to the push events and starts the backstop loop. Idempotent and thread-safe.
  /// Public so tests and integration code can trigger it explicitly; the host normally drives it.
  /// </summary>
  public void Start()
  {
    if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
    {
      return;
    }

    // These three are the only push signals that exist, and every one of them means "something NEW
    // arrived". See the class remarks: nothing can push "something became read".
    _phoneHub.GvVoicemailReceived += OnItemArrived;
    _phoneHub.GvSmsReceived += OnItemArrived;
    _phoneHub.CallHistoryUpdated += OnCallHistoryUpdated;

    _cts = new CancellationTokenSource();
    _timer = new PeriodicTimer(TimeSpan.FromSeconds(_pollSeconds));
    _loop = Task.Run(() => BackstopLoopAsync(_cts.Token));
  }

  /// <summary>
  /// Fetches the total once per process and caches it. Called by MainLayout's initialization — this is
  /// the line that makes the badge correct on a circuit that has never mounted PhonePage.
  ///
  /// <para>
  /// ⚠ Cached, NOT per-circuit. A second browser or a kiosk reload costs zero network. Copying the
  /// per-circuit shape used by the Queue/Mute/Encoder seeds would put two live Google POSTs on every
  /// reload (class remarks).
  /// </para>
  /// </summary>
  /// <remarks>Never throws. MainLayout.OnInitializedAsync rethrows what reaches it, which crashes the
  /// circuit, and a badge is not worth a blank screen.</remarks>
  public async Task EnsureHydratedAsync(CancellationToken ct = default)
  {
    if (_hydrated)
    {
      return;
    }

    await _hydrationGate.WaitAsync(ct);
    try
    {
      if (_hydrated)
      {
        return;
      }
      // Marked hydrated whether or not the fetch succeeded: a failed fetch must not turn every
      // subsequent circuit into a fresh retry storm against an API that is already failing. The
      // backstop loop is what recovers from a failed first fetch.
      await RecountAsync(ct);
      _hydrated = true;
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Phone unread hydration failed; the backstop poll will retry");
      _hydrated = true;
    }
    finally
    {
      _hydrationGate.Release();
    }
  }

  private void OnCallHistoryUpdated() => ScheduleDebouncedRecount();
  private void OnItemArrived(VoicemailItemDto _) => ScheduleDebouncedRecount();
  private void OnItemArrived(SmsMessageDto _) => ScheduleDebouncedRecount();

  /// <summary>
  /// Collapses a burst of pushes into a single recount. Each push cancels the pending one and starts a
  /// new delay, so five SMS arriving together cost one fetch, not five — and each fetch is two live
  /// Google round trips.
  /// </summary>
  private void ScheduleDebouncedRecount()
  {
    CancellationTokenSource cts;
    lock (_debounceGate)
    {
      _debounceCts?.Cancel();
      _debounceCts?.Dispose();
      _debounceCts = new CancellationTokenSource();
      cts = _debounceCts;
    }

    _ = Task.Run(async () =>
    {
      try
      {
        await Task.Delay(_debounceMilliseconds, _timeProvider, cts.Token);
        await RecountAsync(cts.Token);
      }
      catch (OperationCanceledException) { /* superseded by a later push, or shutting down */ }
      catch (Exception ex)
      {
        _logger.LogDebug(ex, "Debounced unread recount failed; retaining last known count");
      }
    });
  }

  private async Task BackstopLoopAsync(CancellationToken ct)
  {
    try
    {
      while (await _timer!.WaitForNextTickAsync(ct))
      {
        // Skip while a mounted PhonePage is already publishing fresher values than we would fetch.
        // Its 30s backstop re-fetches the same two Google lists, so running on top of it is pure
        // duplicate upstream load in the one case where the data is known to be fresh.
        var lastPublished = _state.LastPublishedUtc;
        if (lastPublished is not null
            && _timeProvider.GetUtcNow() - lastPublished.Value < TimeSpan.FromSeconds(_pollSeconds))
        {
          continue;
        }
        await RecountAsync(ct);
      }
    }
    catch (OperationCanceledException) { /* shutting down */ }
  }

  /// <summary>
  /// Fetches the three lists and publishes the derived total.
  /// </summary>
  /// <remarks>
  /// ⚠ A failed fetch RETAINS the last known count rather than publishing 0 — the same posture as
  /// BellHealthService.Publish(null), and deliberately the OPPOSITE of GvBridgeStatusService, which
  /// applies null on failure. A dropped request is not evidence that the owner read everything; treating
  /// it as such would blink the badge off and back on, and §0.4's auth-recovery ladder makes dropped
  /// requests routine rather than rare.
  ///
  /// This is why the three fetches are NOT Task.WhenAll'd into a single failure: a partial result is
  /// still information about the terms that did arrive, and PhoneUnreadRules.Total treats a null list as
  /// contributing 0.
  /// </remarks>
  private async Task RecountAsync(CancellationToken ct)
  {
    using var scope = _scopeFactory.CreateScope();
    var phoneApi = scope.ServiceProvider.GetRequiredService<PhoneApiService>();
    var gvApi = scope.ServiceProvider.GetRequiredService<GvBridgeApiService>();

    // Both client methods already swallow their own exceptions and return null (PhoneApiService.cs:183,
    // GvBridgeApiService.cs:97,196), so "failed" arrives here as null rather than as a throw.
    var callHistory = await phoneApi.GetCallHistoryAsync(ct);
    var voicemails = await gvApi.GetVoicemailsAsync(ct: ct);
    var threads = await gvApi.GetSmsThreadsAsync(ct: ct);

    if (callHistory is null && voicemails is null && threads is null)
    {
      // Nothing came back at all. Publishing 0 here is the failure mode this guard exists for.
      _logger.LogDebug("Phone unread recount: all three fetches failed; retaining last known count");
      return;
    }

    var total = PhoneUnreadRules.Total(callHistory, voicemails?.Items, threads?.Threads);
    _state.Set(total);
  }

  private async Task StopLoopAsync()
  {
    if (Interlocked.Exchange(ref _stopped, 1) != 0)
    {
      return;
    }

    _phoneHub.GvVoicemailReceived -= OnItemArrived;
    _phoneHub.GvSmsReceived -= OnItemArrived;
    _phoneHub.CallHistoryUpdated -= OnCallHistoryUpdated;

    lock (_debounceGate)
    {
      try { _debounceCts?.Cancel(); } catch (ObjectDisposedException) { }
    }

    try { _cts?.Cancel(); } catch (ObjectDisposedException) { /* already torn down */ }
    if (_loop != null)
    {
      try { await _loop; } catch { /* ignore — cancellation/teardown */ }
    }
  }

  public async ValueTask DisposeAsync()
  {
    await StopLoopAsync();
    _timer?.Dispose();
    _cts?.Dispose();
    lock (_debounceGate) { _debounceCts?.Dispose(); }
    _hydrationGate.Dispose();
  }
}
```

⚠ **Two signature checks before compiling.** `GetVoicemailsAsync` returns `VoicemailListDto?` with items on
`.Items`, and `GetSmsThreadsAsync` returns `SmsThreadListDto?` with threads on `.Threads`
(`GvBridgeApiService.cs:97,196`) — they are **not** bare lists. And both take `count` first, so the
`CancellationToken` must be passed by name as above.

⚠ **`OnItemArrived` is overloaded on `VoicemailItemDto` / `SmsMessageDto`.** If overload resolution against
`Action<T>` is awkward, give them distinct names rather than casting.

---

### Task 5 — Register it

**Edit `src/Radio.Web/Program.cs`, immediately after the `BellHealthService` block (`:447-453`).**

```csharp
// PHN-9 — the single app-wide derivation of the topbar PHONE unread total, so the badge does not
// require the user to have already visited the page it points at. Same singleton + hosted-service +
// scope-per-fetch shape as BellHealthService above (ADR-022 §6.2).
//
// ⚠ 300s, not BellHealthService's 15s. Two of the three fetches are uncached live POSTs to Google via
// RotaryPhone, duplicating a poller that already runs there every 15-60s. The push events carry the
// increments; this interval only bounds how long a DECREMENT can lag. See ADR-036.
builder.Services.AddSingleton<Radio.Web.Services.PhoneUnreadHydrationService>(sp =>
  new Radio.Web.Services.PhoneUnreadHydrationService(
    sp.GetRequiredService<IServiceScopeFactory>(),
    sp.GetRequiredService<Radio.Web.Services.PhoneUnreadState>(),
    sp.GetRequiredService<Radio.Web.Services.Hub.PhoneHubService>(),
    sp.GetRequiredService<ILogger<Radio.Web.Services.PhoneUnreadHydrationService>>(),
    builder.Configuration.GetValue("RotaryPhone:UnreadPollSeconds", 300)));
builder.Services.AddHostedService(sp =>
  sp.GetRequiredService<Radio.Web.Services.PhoneUnreadHydrationService>());
```

**Also add the key to `src/Radio.Web/appsettings.json`'s `RotaryPhone` block (`:17-23`)**, beside
`Gv.StatusPollSeconds`, so the value is discoverable rather than a code-only default — `BellHealthPollSeconds`'
absence from every appsettings file is a small wart worth not repeating:

```json
    "UnreadPollSeconds": 300,
```

---

### Task 6 — `MainLayout` fetches instead of only reading

**Edit `src/Radio.Web/Components/Layout/MainLayout.razor`.** Add the inject beside the existing one at `:18`:

```razor
@inject Radio.Web.Services.PhoneUnreadHydrationService PhoneUnreadHydration
```

Replace `:400-403` with:

```csharp
      // GV Messages: mirror the shared unread count onto the topbar /phone pill badge.
      //
      // PHN-9 — subscribe first, then seed with an authoritative PULL, the same order and for the same
      // reason as SeedEncoderStateAsync below: a value arriving while the fetch is in flight must not be
      // dropped. Before this, the seed was `_phoneUnread = PhoneUnread.Count` and nothing else — a read
      // of an in-memory singleton whose only writer was PhonePage while mounted. On a fresh process
      // nobody had written it, so the badge was absent for the life of the process unless somebody
      // tapped PHONE, and radio-kiosk-launch opens the kiosk at root. Deploys restart radio-web, so that
      // was the badge's NORMAL state: observed live at 14 → absent across a deploy → 15.
      //
      // EnsureHydratedAsync caches per PROCESS, not per circuit — a reload costs no network.
      _phoneUnread = PhoneUnread.Count;
      PhoneUnread.Changed += OnPhoneUnreadChanged;
      await PhoneUnreadHydration.EnsureHydratedAsync();
      _phoneUnread = PhoneUnread.Count;
```

⚠ **The second `_phoneUnread = PhoneUnread.Count;` is not redundant.** `Set` fires `Changed` only on a *change*,
and `OnPhoneUnreadChanged` marshals through `InvokeAsync(StateHasChanged)`; re-reading after the await makes the
first render carry the hydrated value rather than depending on that round trip.

⛔ **`EnsureHydratedAsync` must never throw** — `OnInitializedAsync` rethrows what reaches it and crashes the
circuit (`:490-494`). Task 4 guarantees this; do not add a `throw` to it later.

**Also fix the stale comment at `:1261-1263`** — it says the count is published by `PhonePage`, which after this
change is only half true:

```csharp
  // Re-render the topbar /phone pill when the shared unread count changes. Two writers now: PhonePage
  // while it is mounted, and PhoneUnreadHydrationService otherwise (PHN-9). Subscribed in
  // OnInitializedAsync; unsubscribed in Dispose.
```

---

### Task 7 — ⛔ Fix a comment this work proves false

**`MainLayout.razor:1287-1289` currently reads:**

> *"Every decision is in EncoderFaultRules / EncoderFaultAnnouncer rather than here, because **MainLayoutTests
> renders nothing (Radzen + JSInterop)** and logic written in this file would ship with no automated coverage."*

⛔ **The parenthetical is false and has been since `PHN-2`.** `ConsolePlaybackChipTests` renders the **real
`MainLayout`** (`:43-91`), and its own class remarks say `PHN-2` **deleted** the `MainLayoutTests` placeholder
*"rather than leave a permanently-green test whose stated reason this one disproves."* §5's RED test depends on
that harness, so shipping this row while the comment still says the layout cannot be rendered would leave a
false claim standing next to its own counter-example.

**The conclusion survives; only the reason changes.** Rewrite:

```csharp
  // --- ENC-12: encoder fault badge + notification ---
  //
  // Every decision is in EncoderFaultRules / EncoderFaultAnnouncer rather than here. The layout IS
  // renderable in bUnit — ConsolePlaybackChipTests and PhoneUnreadBadgeHydrationTests both do it — but
  // that harness needs the layout's ~24 injected services, an OfflineHubTransport per hub and stub
  // option monitors, so logic extracted to a pure rules class is far cheaper to pin and to mutate.
  // Same reason BellHealthRules and PhoneUnreadRules exist.
  //
  // ⚠ This used to say "MainLayoutTests renders nothing (Radzen + JSInterop)". That was already false
  // when PHN-9 read it: PHN-2 deleted that placeholder and replaced it with a working harness.
```

---

## 4. Docs

| File | Change |
|---|---|
| `design/DECISION-LOG.md` | **ADR-036** (§4.1). Latest is ADR-035. |
| `design/INTEGRATIONS.md` §2 *Phone Call Integration* | Add `UnreadPollSeconds` to the config table with the cost rationale. Memory rule: **always** update this on integration-service changes. |
| `docs/queue/PHN-9.md` | Append a `PLAN` link and the §8 corrections. **Do not rewrite the row's findings** — they held. |
| `docs/BUILDER_QUEUE.md` | `PHN-9` row: `📋` → plan link. Update the last-updated banner. ⛔ **Do not reorder the queue.** |
| `design/FUTURE-WORK.md` | The §9 follow-up (missed-call seen-flag). Memory rule: never leave a known gap undocumented. |

### 4.1 ADR-036 — draft text

> **ADR-036: A status badge must hydrate from a pull; a value published only by the page it points at is not a
> hydration source**
>
> **Date:** 2026-09-09 · **Status:** Accepted
>
> **Context:** Five indicators share the topbar. Queue, Mute, Encoder and Bell each hydrate from an authoritative
> pull (`MainLayout.razor:475`, `:392`, `:435`; `BellHealthService`). The PHONE unread badge seeded by reading a
> process-wide singleton written only by `PhonePage` while mounted, so on any process where nobody had opened
> `/phone` it rendered nothing — the normal state after a deploy. Observed live: **14 → absent → 15**.
> `BellHealthService`'s own class doc had already made this argument for the fault badge, and
> `MainLayout.razor:406-408` stated the contrast in the negative, so the rule existed in prose and was not
> enforced anywhere.
>
> **Decision:** An indicator visible from routes other than its own subject page **must** derive its value from a
> source that does not require that page to be mounted. A page-published cache may make it *fresher*; it may not
> be the only writer. Where no server-side aggregate exists, a hosted singleton on the `BellHealthService` shape
> derives one.
>
> **Second half, which is what `PHN-9` actually cost:** where the push channel can only move a value in one
> direction, a periodic re-derive is **not** a safety net — it is the only mechanism for the other direction, and
> it is part of the fix rather than an enhancement. Here nothing can push "an item became read": RotaryPhone's
> poller never raises `ReadStateChanged` from its own polling, and both mark-read routes ship disabled, so
> `PhoneUnreadState` could go up and never come down. It had been under-reporting for an entire process lifetime.
>
> **Consequences:** A 300 s backstop, not 15 s — two of the three fetches are uncached live Google POSTs
> duplicating a RotaryPhone poller that already runs at 15-60 s. Increments arrive by push; the interval bounds
> only decrement lag. The missed-call term still cannot decrement at all (no seen-flag exists server-side); that
> is recorded, not fixed. If RotaryPhone ever serves an aggregate total, `RecountAsync` collapses to one call and
> nothing else changes.

---

## 5. ⭐ Verification — RED first, asserting what the fix ADDS

### 5.1 The principle, applied

The assertion is **the presence of a badge on a circuit that never mounted `PhonePage`** — not the absence of
anything. A test that asserted "the singleton is no longer only-written-by-PhonePage" would pass on a fix that
hydrates to the wrong number.

### 5.2 ⛔ `C-905` — the trap that makes a plausible RED test GREEN today

**Do not write the test by seeding `PhoneUnreadState.Set(9)` and asserting the badge shows `9`.** That passes
**today**, unfixed: `MainLayout.razor:402` already reads the singleton, so pre-seeding it exercises the one part
of the path that was never broken. The badge's absence comes from *nothing writing the singleton*, so the test
must stub **the HTTP layer** and leave the singleton at `0`.

### 5.3 New test helper — `RoutedMockHttpHandler`

`MockHttpHandler` returns **one** fixed body for every request (`MockHttpHandler.cs:19-35`). Hydration hits three
different routes needing three different payloads, so a routed variant is needed. ⭐ **Building this seam is part
of the work** — the row is right that no existing test exercises the badge seed at all.

**New file `tests/Radio.Web.Tests/TestHelpers/RoutedMockHttpHandler.cs`:**

```csharp
using System.Net;

namespace Radio.Web.Tests.TestHelpers;

/// <summary>
/// Returns a different canned body per request path, for tests that exercise a code path making several
/// calls to different routes. MockHttpHandler's single fixed body cannot express that.
///
/// <para>
/// Matching is first-substring-wins on the absolute path, so a caller registers "/api/callhistory" rather
/// than reproducing the query string. An unmatched request is a 404 with a body naming the path — a
/// silent empty 200 would let a test pass while the code called the wrong route, which is the failure
/// this helper exists to make visible.
/// </para>
/// </summary>
public sealed class RoutedMockHttpHandler : HttpMessageHandler
{
  private readonly List<(string Fragment, string Body)> _routes = new();

  /// <summary>Requests whose path matched no route, for asserting a call was NOT made.</summary>
  public List<string> Unmatched { get; } = new();

  /// <summary>Every path this handler was asked for, in order — lets a test count fetches.</summary>
  public List<string> Requested { get; } = new();

  public RoutedMockHttpHandler When(string pathFragment, string jsonBody)
  {
    _routes.Add((pathFragment, jsonBody));
    return this;
  }

  protected override Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request, CancellationToken cancellationToken)
  {
    var path = request.RequestUri?.AbsolutePath ?? "";
    Requested.Add(path);

    foreach (var (fragment, body) in _routes)
    {
      if (path.Contains(fragment, StringComparison.OrdinalIgnoreCase))
      {
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
          Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        });
      }
    }

    Unmatched.Add(path);
    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
    {
      Content = new StringContent($"{{\"error\":\"no route registered for {path}\"}}",
        System.Text.Encoding.UTF8, "application/json"),
    });
  }
}
```

### 5.4 ⭐ The RED test

**New file `tests/Radio.Web.Tests/Components/Layout/PhoneUnreadBadgeHydrationTests.cs`.** The harness is
`ConsolePlaybackChipTests.RenderLayout()` (`:43-91`) with the phone clients stubbed; **copy it rather than
sharing it**, so a change to the chip's fixture cannot silently alter this row's gate.

```csharp
  private const string CallHistoryJson = """
    [
      { "id": "11111111-1111-1111-1111-111111111111", "phoneNumber": "+15551110001",
        "direction": "Incoming", "answeredOn": "NotAnswered", "startTime": "2026-09-09T10:00:00Z" },
      { "id": "22222222-2222-2222-2222-222222222222", "phoneNumber": "+15551110002",
        "direction": "Incoming", "answeredOn": "NotAnswered", "startTime": "2026-09-09T10:05:00Z" },
      { "id": "33333333-3333-3333-3333-333333333333", "phoneNumber": "+15551110003",
        "direction": "Incoming", "answeredOn": "Handset", "startTime": "2026-09-09T10:06:00Z" }
    ]
    """;   // 2 missed — the answered one is the discriminator for M2

  private const string VoicemailJson = """
    { "items": [
        { "id": "vm-1", "threadId": "t-1", "fromNumber": "+15551110001", "receivedAt": "2026-09-09T10:00:00Z",
          "durationSeconds": 12, "isRead": false, "audioUrl": "/a/1" },
        { "id": "vm-2", "threadId": "t-2", "fromNumber": "+15551110002", "receivedAt": "2026-09-09T10:01:00Z",
          "durationSeconds": 8, "isRead": false, "audioUrl": "/a/2" },
        { "id": "vm-3", "threadId": "t-3", "fromNumber": "+15551110003", "receivedAt": "2026-09-09T10:02:00Z",
          "durationSeconds": 4, "isRead": true, "audioUrl": "/a/3" }
      ], "nextPageToken": null, "fetchedAtUtc": "2026-09-09T10:10:00Z" }
    """;   // 3 unheard

  private const string SmsThreadsJson = """
    { "threads": [
        { "threadId": "s-1", "counterpartyNumber": "+15551110001",
          "lastMessageAt": "2026-09-09T10:00:00Z", "hasUnread": true },
        { "threadId": "s-2", "counterpartyNumber": "+15551110002",
          "lastMessageAt": "2026-09-09T10:01:00Z", "hasUnread": true },
        { "threadId": "s-3", "counterpartyNumber": "+15551110003",
          "lastMessageAt": "2026-09-09T10:02:00Z", "hasUnread": false }
      ], "fetchedAtUtc": "2026-09-09T10:10:00Z" }
    """;   // 2 unread threads

  // 2 + 3 + 2 = 7. Deliberately three DIFFERENT term values, so a fix that wires up only one of the
  // three lists, or double-counts one, produces a number that is wrong rather than coincidentally right.
  private const string ExpectedBadge = "7";
```

```csharp
  [Fact]
  public void TheBadgeAppearsOnACircuitThatHasNeverMountedPhonePage()
  {
    // ⭐ THE ROW, as an assertion. PhonePage is never rendered anywhere in this test, and
    // PhoneUnreadState is left at its fresh-process value of 0 — pre-seeding it would test
    // MainLayout's singleton READ, which was never the broken part (plan §5.2).
    var cut = RenderLayout();

    // Assert PRESENCE of what the fix adds, not absence of what it removes.
    Assert.Single(cut.FindAll(".nav-badge"));
    Assert.Equal(ExpectedBadge, cut.Find(".nav-badge").TextContent.Trim());
  }

  [Fact]
  public void TheAccessibleNameCarriesTheHydratedCount()
  {
    // The badge is aria-hidden (MainLayout.razor:226), so the glyph is not the only thing that has to be
    // right — BellHealthRules.NavPillAriaLabel puts the number in the accessible name. A fix that
    // rendered the badge but left PhoneNavLabel reading a stale 0 would pass the test above.
    var cut = RenderLayout();

    Assert.Equal("Phone, 7 unread",
      cut.Find("a[href='/phone']").GetAttribute("aria-label"));
  }

  [Fact]
  public void ASecondCircuitCostsNoAdditionalFetches()
  {
    // EnsureHydratedAsync caches per PROCESS. Two of the three routes are live Google POSTs, so a
    // per-circuit seed would put two of them on every kiosk reload (plan §2.2).
    var cut1 = RenderLayout();
    Assert.Single(cut1.FindAll(".nav-badge"));
    var afterFirst = _handler.Requested.Count;

    var cut2 = RenderLayoutOnSameServices();

    Assert.Single(cut2.FindAll(".nav-badge"));
    Assert.Equal(afterFirst, _handler.Requested.Count);
  }

  [Fact]
  public void AFailedFetchLeavesTheBadgeAbsentRatherThanShowingZero()
  {
    // Not the same as "shows 0" — @if (_phoneUnread > 0) renders nothing either way. What this pins is
    // that a total failure does not THROW out of OnInitializedAsync, which would crash the circuit and
    // blank the whole panel for a badge (MainLayout.razor:490-494).
    var cut = RenderLayoutWithAllRoutesFailing();

    Assert.Empty(cut.FindAll(".nav-badge"));
    Assert.NotEmpty(cut.FindAll(".topbar"));   // the layout rendered; the circuit survived
  }
```

### 5.5 ⛔ How RED is demonstrated, stated honestly

**Run the first two tests on the branch before Tasks 4-6 land.** They compile against types that already exist —
`MainLayout`, `PhoneUnreadState`, the API services, `RoutedMockHttpHandler` — and they **fail**:

```
TheBadgeAppearsOnACircuitThatHasNeverMountedPhonePage
  Assert.Single() Failure: The collection was empty

TheAccessibleNameCarriesTheHydratedCount
  Assert.Equal() Failure
  Expected: Phone, 7 unread
  Actual:   Phone
```

⚠ **State the one thing that is not a pure red→green, rather than papering over it.** `MainLayout` gains
`@inject PhoneUnreadHydrationService` in Task 6, so the GREEN run needs **one added DI registration line** in the
harness — a type that does not exist at RED and therefore cannot be registered then. **The arrange-stub and every
assertion are byte-identical across the two runs**; only the service list grows. Record both runs' output in the
PR body. This is the same honesty the repo applied to `UI-14`'s two non-compiling mutations: report the shape of
the gate as it really is.

⛔ **Do not "solve" this by pre-registering a stub interface at RED.** That invents an abstraction the design does
not otherwise need, purely to make a commit boundary look tidier.

### 5.6 Service- and rules-level tests

These are **not** the RED gate — they are new code, green on arrival — and the plan says so rather than implying
a redness they do not have. Their value is §6.

**`tests/Radio.Web.Tests/Models/PhoneUnreadRulesTests.cs`** — each term independently; nulls contribute `0`; an
answered incoming call and a read voicemail are excluded; `Total` sums three *different* values so a transposed
term fails.

**`tests/Radio.Web.Tests/Services/PhoneUnreadHydrationServiceTests.cs`** — following
`BellHealthServiceTests`' honest precedent, which pins the state machine through `Publish` and says in its class
doc that the loop itself is not exercised:

```csharp
  [Fact]
  public async Task AllThreeFetchesFailing_RetainsTheLastKnownCount()
  {
    // The BellHealthService posture (Publish(null) → retain), deliberately OPPOSITE to
    // GvBridgeStatusService's ApplyStatus(null). A dropped request is not evidence that the owner read
    // everything, and the auth-recovery ladder makes dropped requests routine.
    var state = new PhoneUnreadState();
    state.Set(14);
    var svc = NewService(state, allRoutesFail: true);

    await svc.EnsureHydratedAsync();

    Assert.Equal(14, state.Count);
  }

  [Fact]
  public async Task APartialFetchStillPublishesTheTermsThatArrived()
  {
    // PhoneUnreadRules.Total treats a null list as 0, so a voicemail outage must not suppress the SMS
    // and missed-call terms. This is why RecountAsync does not Task.WhenAll into one failure.
    var state = new PhoneUnreadState();
    var svc = NewService(state, voicemailFails: true);   // 2 missed + 0 + 2 threads

    await svc.EnsureHydratedAsync();

    Assert.Equal(4, state.Count);
  }

  [Fact]
  public async Task EnsureHydratedAsync_IsIdempotent_AcrossConcurrentCallers()
  {
    // N circuits opening together must produce ONE fetch. Each fetch is two live Google round trips.
    var svc = NewService(new PhoneUnreadState(), out var handler);

    await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => svc.EnsureHydratedAsync()));

    Assert.Equal(3, handler.Requested.Count);
  }
```

**`tests/Radio.Web.Tests/Services/PhoneUnreadStateTests.cs`** — new; the singleton has no tests at all today:

```csharp
  [Fact]
  public void ASuppressedNoOpSetDoesNotAdvanceLastPublishedUtc()
  {
    // Load-bearing (plan §2.5 / Task 3): a mounted PhonePage re-publishes the SAME total every 5s. If a
    // no-op stamped the timestamp, an idle page would suppress the hydration backstop forever — and the
    // backstop is the only thing that can decrement the voicemail/SMS terms.
    var clock = new FakeTimeProvider();
    var state = new PhoneUnreadState(clock);
    state.Set(5);
    var first = state.LastPublishedUtc;

    clock.Advance(TimeSpan.FromMinutes(10));
    state.Set(5);

    Assert.Equal(first, state.LastPublishedUtc);
  }
```

---

## 6. Mutation matrix

Each row names the mutation, the **one** test that must fail, and what a green result would mean.

| # | Mutation | Must fail | If it stays green |
|---|---|---|---|
| `M1` | Delete `await PhoneUnreadHydration.EnsureHydratedAsync()` from `MainLayout` | `TheBadgeAppearsOnACircuitThatHasNeverMountedPhonePage` | ⛔ the row is not gated at all — this is the RED test's own mutation |
| `M2` | `PhoneUnreadRules.MissedCalls` drops the `AnsweredOn == NotAnswered` clause | badge reads `8` not `7`; `PhoneUnreadRulesTests` | the fixture's answered call is not discriminating — add one |
| `M3` | `UnheardVoicemails` → `v.IsRead` (polarity flip) | badge reads `5` not `7` | the fixture has equal read/unread counts — vary them |
| `M4` | ⭐ Move `LastPublishedUtc` stamping **above** the no-op guard in `Set` | `ASuppressedNoOpSetDoesNotAdvanceLastPublishedUtc` | §2.5 is unpinned; an idle PhonePage silently disables the backstop forever |
| `M5` | `RecountAsync` publishes `0` instead of returning when all three fail | `AllThreeFetchesFailing_RetainsTheLastKnownCount` | the badge blinks off on every dropped request |
| `M6` | Delete the `PeriodicTimer` loop, keep the seed | ⚠ **nothing** — and that is the point | ⛔ **Expected.** The staleness half is covered by §5.6's unit tests of the loop's *inputs*, not by an end-to-end decrement test. Record it as a known gap rather than inventing a wall-clock test — `CLAUDE.md` § *Test Timing* forbids racing a timer inside a `Task.Delay`. A `FakeTimeProvider`-driven loop test is the honest follow-up if the gap matters. |
| `M7` | **Control** — rename `PhoneUnreadRules.Total`'s parameters, no behaviour change | ⭐ **nothing; all green** | if anything fails, a test is pinning shape rather than behaviour |

⚠ **`M7` is a real control, unlike `UI-12`'s `M4`** (which the queue banner records as having varied behaviour
*and* shape, so it could not pass). This one varies only identifiers.

---

## 7. The one-endpoint future — costed, and why it is not a branch

RotaryPhone answered directly: **no unread total exists anywhere on their API, and none is planned as far as
they can see.** Independently confirmed here by enumerating their **24 GET routes across 6 controllers** — not
one returns a scalar count.

⚠ **Cost it as a MOVE, not a saving in kind.** Google Voice does not serve a count either; their own parser
derives a tally from per-message read flags. A server-side total would be **the same summation, moved across the
wire so it crosses once instead of three times.**

⭐ **On this box that move is still worth real money**, and for a reason bigger than payload: their
`GvThreadPoller` **already fetches this exact data every 15-60 s and discards it**
(`GvThreadPoller.cs:89,129`; `GvHighWaterMark` keeps only a per-thread max timestamp). An aggregate could be
served from state already in hand at **zero incremental Google cost**, where our poll adds two live round trips.

**If it ever ships, the change here is one method:**

```csharp
    // The three-call derivation collapses to one. PhoneUnreadRules and every test above stay as they
    // are — they pin what the number MEANS, not where it came from.
    var total = await gvApi.GetUnreadTotalAsync(ct);
    if (total is not null) { _state.Set(total.Value); }
```

⚠ **And two things would have to be settled in that ask, which is why adopting one is not automatic:**

1. **The missed-call term has no server-side representation** (§0.6). Their aggregate would cover voicemail +
   texts, matching their own badge handoff — so we would still derive the missed-call term locally, from the
   *cheap* SQLite route, and the call would become **two**, not one.
2. `/api/callhistory` has **no read-side limit** (`CallHistoryController.cs:29`); it is bounded only by a
   write-time trim. Worth flagging to them whether or not the aggregate happens.

### ⛔ Why we are not waiting

**Do not let a roadmap question you cannot control gate a defect you can.** That is the shape of this row's own
finding: the latch under-reported for an entire process lifetime and nobody noticed, because nothing was
watching. The defect is *"`MainLayout` reads a latch instead of fetching"* — true at one endpoint or three — so
the endpoint count is a **cost** question, and this plan answers it with a 300 s interval and a push-primary
design rather than by holding a live defect open.

---

## 8. ⚠ Four things the row does not say that changed this plan

1. ⭐ **The push channel cannot decrement** (§0.3). The row correctly says *"no hub carries an unread count"* and
   stops there; the consequence — that a periodic re-fetch is the **only** decrement mechanism — is what makes
   the staleness half non-deferrable.
2. ⛔ **Two of the three calls are uncached live Google POSTs** (§0.4), duplicating a RotaryPhone poller that
   already runs at 15-60 s. The row says *"three REST calls"*, which reads as three cheap local hops; they go to
   `http://radio:5004` **on the same box**, but two of them continue upstream over WiFi. This set the interval.
3. ⭐ **`MainLayoutTests renders nothing` is false** (§ Task 7) and has been since `PHN-2`. The row says no test
   exercises the badge seat — true — but the *harness* to do it already exists and is proven, which makes the
   RED test much cheaper than the row implies.
4. ⚠ **The missed-call term cannot decrement even after this fix** (§0.6), because no seen-flag exists
   server-side. Any claim that the badge is now fully self-correcting would be false.

**Nothing in the row was found false.** Its `17` `Set` sites, its line citations, its mechanism and its
`Dispose` claim all verified. ⚠ One caution for the Builder: an intermediate inventory during planning reported
**16** `Set` sites, having missed `:457` (`OnCallHistoryUpdated`). **The row's 17 is correct** — re-grep rather
than trusting either number.

---

## 9. Follow-up row to file (not this PR)

**`PHN-10` — the missed-call third of the unread badge can never decrement.** `CallHistoryEntry` carries no
read/seen field and RotaryPhone's SQLite table has no such column, so that term falls only when an entry ages out
of the ~100-row ring or the owner clears history. Needs an owner decision (is a missed call "unread" until
acknowledged?) and, if yes, a RotaryPhone schema change plus a mark-seen route — cross-repo, their lane. Record
in `design/FUTURE-WORK.md` per the standing rule.

---

## 10. Gates

```bash
# Build — the gate is EQUALITY with the baseline, not an absolute.
dotnet build RadioConsole.sln --configuration Release > /tmp/phn9-build.log 2>&1; echo "exit=$?"
grep -E "Warning\(s\)|Error\(s\)" /tmp/phn9-build.log
# Expect: 47 Warning(s), 0 Error(s) — measured on main 2026-09-06, all IDE0011.
```

⛔ **Do not use `grep -cE "warning"`** — MSBuild emits each warning once per project-graph pass and it returns
**94**. `OPS-2:536` and `UI-12`'s plan both shipped that broken recipe.

⚠ **If the count is not 47, build `main` too before believing either number.**

```bash
# Tests — NEVER pipe into tail/head/grep; the pipeline reports tail's exit code, not the suite's.
dotnet test RadioConsole.sln --configuration Release > /tmp/phn9-test.log 2>&1; echo "exit=$?"
grep -E "Passed!|Failed!|error" /tmp/phn9-test.log
```

Read the **per-project summary lines**. Known-failing on Windows and **not** regressions: four
`SrcVariableResamplerTests` (`libsamplerate.so.0`, `TEST-5`), `NwsObservationIntegrationTests.RealNwsCall_*`, and
`CoverArtPipelineIntegrationTests.CoverArtArchive_ReturnsValidUrl_ForKnownRecording` — all `Category=Integration`
and CI-excluded by `build.yml:58`.

### 10.1 UAT — ⭐ the row's own kill condition, re-run inverted

Deploy with `./deploy/Deploy-ToLinux.ps1`, then:

| # | Step | Expected |
|---|---|---|
| **U1** | ⭐ After the deploy completes, look at the topbar **without touching PHONE** | **A badge is present.** This is the whole row: before the fix it was absent here. |
| **U2** | Compare against `/phone`'s own three counts | Same number. |
| **U3** | Reload the kiosk, still never opening `/phone` | Badge still present, **and `journalctl` shows no new fetch** — `EnsureHydratedAsync` is cached per process. |
| **U4** | ⭐ Read a voicemail **on the handset**, then wait ≤5 min without opening `/phone` | Badge **decrements**. This is the 14→15 half; it could not have passed before at any delay. |
| **U5** | Send an SMS to the line | Badge increments within ~2 s of the push (debounce), not 5 min. |
| **U6** | Open `/phone`, leave it open 10 min | ⚠ **Check `journalctl -u radio-web --since '-15min'` for a backstop fetch** — §2.5 says there should be none while the page is publishing. |

⚠ **U4 is the one that cannot be faked and takes real elapsed time. Do not substitute a restart for it** — a
restart re-runs hydration and would pass regardless of whether the backstop works.

⚠ **`radio-web`'s Console sink is unrestricted** (`CLAUDE.md`), so **every `Information` line reaches
`journalctl` on a box where log volume correlates with audible audio distortion.** Log the recount at **`Debug`**,
and ⛔ **never log the count alongside a phone number** — `PHN-5` exists because
`PhoneHubService.cs:82` put a raw number in the journal.

---

## 11. Commit sequence

| # | Commit | Contents |
|---|---|---|
| 1 | `PHN-9 (1/3): the RED gate for a badge that never fetches` | `RoutedMockHttpHandler`, `PhoneUnreadBadgeHydrationTests` (first two facts). ⭐ **Record the failing output in the message.** |
| 2 | `PHN-9 (2/3): derive the unread total without the page` | Tasks 1-6 + `PhoneUnreadRulesTests`, `PhoneUnreadHydrationServiceTests`, `PhoneUnreadStateTests`, and the remaining two layout facts. |
| 3 | `PHN-9 (3/3): ADR-036, the false comment, and the queue bookkeeping` | Task 7, §4 docs, ADR-036, `FUTURE-WORK.md`, queue row + banner. |

### ⛔ Tree rules — a Builder is mid-cycle in this checkout

- **One agent commits at a time.** Reading, grepping, editing and running tests concurrently are fine; **branch
  switching and committing must be serialized.** Confirm the other session's stated intent — **a quiet
  `git status` and a `completed` notification are not "finished"** — before `git checkout -b`.
- ⛔ **Never `git add -A` / `git add .` / `git commit -a`.** Stage the named files only.
- ⛔ **Commit before any `git checkout -- <path>` / `git restore` / `git reset --hard`**, including between
  mutation runs in §6. That is how work was lost on 2026-09-08.

### Merge

⛔ **NOT auto-mergeable**, per the queue row: user-visible surface, and `U4` needs a real ≤5 min observation on
the appliance. Land on green gates **plus** the owner's `U1`/`U4` confirmation.

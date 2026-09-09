# PLAN — `GV-12` · The panels fetch once per circuit and never again. Refetch on the recovery edge.

> **Row:** `GV-12`, [`docs/queue/GV-12.md`](../../docs/queue/GV-12.md). 🟠 **P1.** Filed 2026-09-08,
> narrowed the same day by RotaryPhone.
> **Branch:** `fix/gv-12-refetch-on-recovery-edge`.
> **Estimate:** **1.0 d.** Five tasks, four of them small; Task 5 (the RED-first test) is the half-day.
> **Auto-mergeable on green gates — yes.** §0.9 derives it.
> **Planned against** `main` at **`56872b55`**. Every line number below was read out of the tree at
> that commit.
> **Nothing on the box was changed, and nothing on the box was read.** This plan is derived entirely
> from source. §7.3 separates what is verified from what is inferred.
> **Depends on:** nothing. ⛔ It does **not** depend on `UI-10` — §0.1 is why, and that is a reversal
> of the row's own instruction.

---

## 0. Read this before Task 1

### 0.1 ⚠⚠ `C-401` — THE `UI-10` VERDICT: NOT UPSTREAM. THE 30-SECOND TIMEOUTS ARE NOT THE CIRCUIT.

The row ([`GV-12.md:53-59`](../../docs/queue/GV-12.md), and again at `:93-95`) makes `UI-10` the first
question, on the theory that a half-dead Blazor circuit cannot refetch. **Establishing that was the
assignment. It is answered, and the answer is no.**

`UI-10.md:20-21` asserts:

> 30000 ms is the SignalR client's default `ServerTimeout`, so this is the **Blazor Server circuit's
> own connection**, not a GV call and not one of our visualization hub clients.

**The first half is right and the conclusion is backwards. 30000 ms is the client default precisely
because the Blazor circuit is the one connection in this system that is NOT on the default.**

| # | Check | Result |
|---|---|---|
| 1 | The circuit's configured client timeout — [`App.razor:76`](../../src/Radio.Web/Components/App.razor) | `builder.withServerTimeout(120000)` — **120 s, not 30 s.** A circuit on this config cannot emit a `30000.00ms` message |
| 2 | Who emits that string | `Microsoft.AspNetCore.SignalR.Client.HubConnection` — a **SignalR client**. For it to appear in `radio-web`'s log, `radio-web` must be the client |
| 3 | `HubConnectionBuilder` sites in `src/Radio.Web` | Exactly **four**: `AudioStateHubService.cs:155`, `AudioVisualizationHubService.cs:83`, `GvTrunkHubService.cs:54`, `PhoneHubService.cs:69` |
| 4 | How many of the four set `ServerTimeout` | **Zero.** Grep for `ServerTimeout` in `src/Radio.Web` returns two hits, both in `App.razor`. All four clients run the **30 s default** |
| 5 | The server they talk to — [`Radio.API/Program.cs:87`](../../src/Radio.API/Program.cs) | `options.KeepAliveInterval = TimeSpan.FromSeconds(30)` |
| 6 | `UI-10`'s own scope question #2 (`ServerTimeout ≥ 2 × KeepAliveInterval`) | **Violated exactly.** 30 s against 30 s — the server pings at the same instant the client gives up. Textbook, and cheap to rule out, as that row predicted |

So the ~30 s timeouts are **`radio-web`'s four outbound hub clients**, two of them pointed at our own
`radio-api` and two at RotaryPhone's `:5004`. The browser↔`radio-web` circuit is not implicated by any
evidence in either row.

**This closes `UI-10` scope question #2 with an answer, and it re-points the row.** `UI-10` remains a
real defect — it is still ours, and question #1 (which client) now has a concrete answer to check
against. What it is not is a blocker for `GV-12`.

📌 **This is not a criticism of the filing.** RotaryPhone read our logs during their own outage and
correctly identified a standing condition nobody here had noticed. The inference — "30000 is the
default, Blazor uses the default, therefore Blazor" — is the natural one, and it fails only because
this repo overrode the default in the one place a reader would not look for it: a `<script>` block in
`App.razor`.

### 0.2 ⚠⚠ `C-402` — "A CLEAN RECONNECT PRODUCES A RE-MOUNT AND THEREFORE A FETCH" IS FALSE

This is the sharper of the two corrections, because it is the premise the row calls sharp
([`GV-12.md:93-95`](../../docs/queue/GV-12.md)):

> A circuit that drops and re-establishes cleanly produces a re-mount and therefore a fetch; a circuit
> that hangs half-dead produces neither.

**Neither branch produces a fetch in Blazor Server.** A circuit that drops and successfully reconnects
resumes **the same circuit with the same component instances and the same field values**.
`OnInitializedAsync` runs once per *circuit*, not once per *connection*. That is the entire purpose of
`DisconnectedCircuitRetentionPeriod` — [`Program.cs:69`](../../src/Radio.Web/Program.cs) sets it to 10
minutes here, explicitly so that "brief network blips or deploy restarts can reconnect **without
losing circuit state**". State preserved means `_threads`, `_threadsError` and `_voicemailError`
preserved, which means the error branch renders again, unchanged.

Only a **new circuit** re-mounts, and a new circuit needs a full page load.

**This explains the 16:07 evidence better than any circuit-health theory, and it explains it without
one.** `radio-web` restarted at 16:07:29; every open kiosk/browser lost its circuit permanently,
reloaded the page (`App.razor:88-97` force-reloads on `components-reconnect-failed`), mounted fresh
components, and fetched — RotaryPhone counted 6 inbound requests. That is the mount path working
exactly as designed. During the 83 minutes the process never restarted, so no page ever reloaded, so
nothing ever re-mounted. **Same code, both days.**

⭐ **The consequence for this row is a strengthening, not a weakening.** The refetch trigger is not a
belt-and-braces addition on top of a mount path that usually copes. It is the **only** mechanism by
which a long-lived circuit can ever recover, because reconnection — however clean — will never do it.

### 0.3 ⚠ `C-403` — THREE OF THE FIVE TERMS IN THE STATUS CONTRACT HAVE NO FIELD ON OUR DTO

The row gives the predicate as ready to use ([`GV-12.md:44-47`](../../docs/queue/GV-12.md)):

```
unhealthy = !cookiesValid || !available || degraded || authBlackout
            || lastApiSuccessAt is null or older than ~2 min
```

[`ApiModels.cs:1100-1109`](../../src/Radio.Web/Models/ApiModels.cs) is the whole type:

```csharp
public class GvBridgeStatusDto
{
  public bool Available { get; set; }
  public string ActiveMode { get; set; } = "";
  public bool SipRegistered { get; set; }
  public bool CookiesValid { get; set; }
}
```

**`Degraded`, `AuthBlackout` and `LastApiSuccessAt` do not exist** — grep for any casing of the three
across `src/` returns zero hits on this type. RotaryPhone *serves* them (the row's own live capture
shows all three on the wire), we simply do not model them, so `System.Text.Json` discards them
silently. Task 1 adds them. It is additive and low-risk, and the DTO's existing comment already
documents this exact precedent for `SipRegistered`/`CookiesValid`.

✅ **Separately verified, fourth independent check: `psidts` appears nowhere in `src/`** — zero hits
for `psidts` or `Psidts`. The brief's "we hold zero references" is correct, and
`psidtsAgeSeconds`'s removal on RotaryPhone's side cannot break us.

### 0.4 ⚠⚠ `C-404` — THE ROW'S NULL RULE WOULD MAKE THIS ENTIRE FIX A SILENT NO-OP

The row says `lastApiSuccessAt` being **null** counts as unhealthy, and the brief repeats the same
shape for the incoming `psidtsMintedAtUtc` ("`null` means UNKNOWN, which is NOT healthy").

**That rule is correct for a banner and catastrophic for a recovery trigger, and this row needs a
recovery trigger.**

A banner fails *visible*: if we do not know, say something is wrong. A recovery trigger fails
*silent*: the predicate must be able to **reach healthy**, because the whole fix is an
unhealthy→healthy **edge**. Any term that is permanently true pins the state at unhealthy, the edge
never fires, and the panels stay stuck exactly as they do today — except now there is a merged PR and
a green test suite saying the problem is fixed.

Two live ways that happens, neither hypothetical:

1. **RotaryPhone does not populate `lastApiSuccessAt` on some build.** Absent → `null` → unhealthy
   forever.
2. **`CookiesValid` is a non-nullable `bool` defaulting to `false`.** On any response that omits it,
   `!CookiesValid` is true → unhealthy forever. This one is live *today* against the current DTO.

**Rule adopted for this predicate: a field we did not receive is not evidence of ill health.** Only a
field that is present and says "bad" makes the state unhealthy. `Available` keeps its non-nullable
`false` default because it is the field RotaryPhone always sends and the one the outage capture proves
goes false (`available:false` during the total outage) — so nothing is lost by trusting it.

⛔ **Do not "fix" this later by making the predicate stricter to match the row's wording.** If a
future row wants a stricter *banner*, give the banner its own predicate. These two predicates want
opposite failure directions and must not be merged.

### 0.5 ⚠ `C-405` — THE BANNER-EDGE TRIGGER ALONE DOES NOT CLOSE THE ROW

The row offers the recovery edge and a backoff loop as alternatives ("one trigger beats two clocks",
`GV-12.md:24-25`). **The edge alone leaves a reachable hole**, and it is the row's own failure shape:

[`PhonePage.razor:603-624`](../../src/Radio.Web/Components/Pages/PhonePage.razor) sets
`_threadsError = true` whenever `GetSmsThreadsAsync()` returns `null`, and
[`GvBridgeApiService.cs:196+`](../../src/Radio.Web/Services/ApiClients/GvBridgeApiService.cs) returns
`null` on **any** exception — a one-off timeout, a reset connection, a 500 on that route alone. None
of those necessarily moves `/api/gvbridge/status`. So: list fetch fails, bridge health never leaves
healthy, **no edge ever occurs**, panel is stuck until a human taps Retry. That is the row, reproduced
against the fix.

Task 4 closes it with a bounded backstop folded into the timer that already exists — **not** a second
clock, and not an unconditional retry loop. §0.6 says why the shapes differ.

### 0.6 The trigger decision: the recovery edge, plus an error-gated backstop

**Chosen: refetch on the unhealthy→healthy edge of the existing GV status poll, in the handler that
already exists.** RotaryPhone's suggestion is right, and it is even cheaper than they knew — the seam
is already wired:

- [`PhonePage.razor:253`](../../src/Radio.Web/Components/Pages/PhonePage.razor) already subscribes:
  `GvBridgeStatus.StatusChanged += OnGvStatusChanged;`
- `OnGvStatusChanged` at `:453-459` already runs on every status delivery, already updates
  `_gvBridgeAvailable`, and already re-renders. **It just never refetches.**
- `:1065` already unsubscribes in `Dispose`.
- `_gvBridgeAvailable` is what feeds `GvAvailable`, which is what gates the banner at
  [`PhoneMessagesPanel.razor:14-20`](../../src/Radio.Web/Components/Pages/PhoneMessagesPanel.razor).
  **"When the banner clears" and "when this handler sees the healthy edge" are the same instant.**

Why the edge beats a plain backoff loop, on this system specifically:

| | Recovery edge | Backoff loop |
|---|---|---|
| **Where it lives** | `GvBridgeStatusService` — a **singleton `IHostedService`** (`GvBridgeStatusService.cs:22,47-51`), one poll for the whole app, running whether or not any circuit is healthy | Per-component. N open circuits = N loops, and each one dies with its circuit |
| **Through the 83 minutes** | Polls every 10 s regardless; delivers the edge within 10 s of 15:31:17 | Would have issued ~166 failed calls per circuit into a service that was down |
| **Load on a fragile upstream** | One refresh, at the moment it can succeed | Sustained retries at exactly the time RotaryPhone is least able to serve them. Their uptime is **not settled** — a confirmed session-starvation issue — so this is not a theoretical cost |
| **Semantics** | Invalidates a *stale error*, which is what a cleared banner means | Retries a *failed request*, which is a weaker claim |

⛔ **Do NOT latch `authBlackout` with a minimum display window in this predicate.** The row is right
that a 920 ms blackout is invisible to a 10 s poll and must be latched **for a banner**. For a
recovery trigger a latch is actively wrong: it holds the state unhealthy past the moment health
returns, which delays or suppresses the very edge we are detecting. Include the field, do not latch
it, and let a banner row own latching if one is ever filed.

### 0.7 ⚠⚠ THE SILENT-FAILURE TRAP: DO NOT DERIVE THE EDGE FROM `_gvBridgeAvailable`

The obvious implementation — compare the incoming health against `_gvBridgeAvailable` before
overwriting it — **is broken, and it fails silently.**

`_gvBridgeAvailable` has **three writers**:

| Line | Writer | Cadence |
|---|---|---|
| `PhonePage.razor:221` | `OnInitializedAsync`, from its own `GvBridgeApi.GetStatusAsync()` | once |
| `PhonePage.razor:351` | `RefreshGvStatusAsync`, from its own `GvBridgeApi.GetStatusAsync()` | **every 6th 5 s poll = 30 s** (`:390-393`), and on every `RefreshAllAsync` |
| `PhonePage.razor:456` | `OnGvStatusChanged`, from the singleton's 10 s poll | 10 s |

Two independent polls of `/api/gvbridge/status` race to write one field. If `RefreshGvStatusAsync`
lands the recovery first, the field is already `true` when `OnGvStatusChanged` runs, the comparison
reads true→true, **the edge is swallowed, and the panels stay stuck.** Intermittently. On a timer
alignment nobody can reproduce on demand.

**Task 3 therefore gives the edge its own field, `_gvHealthyLast`, written by `OnGvStatusChanged` and
by nothing else.** Reviewers: if you see the edge derived from `_gvBridgeAvailable`, that is this bug.

(That two polls exist at all is redundant and is worth its own row. §8 files it. It is not fixed here —
removing a writer is a behaviour change to the banner, and this row must not touch the banner.)

### 0.8 Anchors — verified, and the one that could not be

Every line reference above was read at `56872b55`. **One thing in the row could not be verified from
this tree and is not relied on by any task:** the claim that RotaryPhone serves `degraded`,
`authBlackout` and `lastApiSuccessAt` today. The row's live capture is the only evidence, this plan
did not touch the box, and their new fields are described as merged-not-deployed. Task 1 is written so
this does not matter — all three are nullable and absence is not ill health (§0.4), so the fix behaves
correctly whether or not the fields ever arrive. **If they never arrive, the predicate degrades to
`!Available`, which the outage capture proves is sufficient to have caught this exact incident.**

⛔ **Nothing in this plan binds to `psidtsMintedAtUtc`, `browserSessionValidatedAt`,
`browserSessionAgeSeconds` or `browserSessionStale`.** Those are merged-not-deployed on RotaryPhone's
side. §8 files the follow-up.

### 0.9 Auto-merge verdict — **yes, on green gates**

Meets all four bars in the auto-merge policy. It touches no auth, no secrets, no migration, no
production config; it is additive to a DTO with defensive defaults; and the behaviour change is
confined to one handler and one timer branch in one page. **UAT stands in as the unit test**, because
the live confirmation this row actually wants cannot be manufactured — §7.2. The reviewer's specific
job is §0.7 (the edge field) and §0.4 (nullability), not general correctness.

---

## 1. Task 1 — model the health fields on `GvBridgeStatusDto`

**File:** `src/Radio.Web/Models/ApiModels.cs`. Replace lines 1100-1109 in full.

`GvBridgeApiService.JsonOptions` sets `PropertyNameCaseInsensitive = true`
([`GvBridgeApiService.cs:22-25`](../../src/Radio.Web/Services/ApiClients/GvBridgeApiService.cs)), so
`lastApiSuccessAt` → `LastApiSuccessAt` with no attributes.

```csharp
public class GvBridgeStatusDto
{
  // Server shape (post-SIP-WSS migration, March 2026): { available, activeMode }.
  // SipRegistered / CookiesValid added per ADR-022 §4.4 — defensive/optional:
  // RotaryPhone may not populate them yet, so defaults keep deserialization safe.
  public bool Available { get; set; }
  public string ActiveMode { get; set; } = "";
  public bool SipRegistered { get; set; }

  // ── GV-12 health terms ──────────────────────────────────────────────────────
  // ⚠⚠ EVERY ONE OF THESE IS NULLABLE, AND THAT IS LOAD-BEARING, NOT TIDINESS.
  // These feed GvBridgeHealth.IsHealthy, whose job is to detect RECOVERY — an
  // unhealthy→healthy EDGE. A term that can never become healthy pins the state and the
  // edge never fires, so the fix ships, goes green, and does nothing. A field we did not
  // receive must therefore read as "no signal", never as "ill". See plan GV-12 §0.4.
  //
  // Available deliberately stays non-nullable: it is the field RotaryPhone always sends,
  // and the 2026-09-08 outage capture read `available:false` for all 83 minutes, so it
  // alone is sufficient to catch that incident.

  /// <summary>
  /// Whether RotaryPhone currently holds valid GV cookies. Null = not reported.
  /// </summary>
  /// <remarks>
  /// ⚠ WIDENED bool → bool? BY GV-12, deliberately. As a non-nullable bool it defaulted to
  /// false on any response omitting the field, which made `!CookiesValid` permanently true
  /// and would have pinned the health predicate at unhealthy forever. Verified 2026-09-08:
  /// the property had ZERO readers in src/ — it was declared here and consumed nowhere — so
  /// widening it changes no call site.
  /// </remarks>
  public bool? CookiesValid { get; set; }

  /// <summary>
  /// RotaryPhone is reachable but impaired. Null = not reported.
  /// </summary>
  /// <remarks>
  /// ⚠ Do NOT treat this as the primary outage signal. It is PER-ACTIVATION and resets to
  /// false when the adapter goes inactive, which is why the live capture during the total
  /// 83-minute outage read `degraded:false` throughout. A banner bound to it would have
  /// stayed silent for the entire incident. It is one term here, never the test.
  /// </remarks>
  public bool? Degraded { get; set; }

  /// <summary>
  /// A GV auth blackout is in progress. Null = not reported.
  /// </summary>
  /// <remarks>
  /// ⚠ Measured at 920 ms, with zero true-samples across 411 polls — a 10 s poll will
  /// essentially never observe it, so this term almost never fires and that is expected.
  /// ⛔ It is deliberately NOT latched with a minimum display window here. Latching is the
  /// right answer for a BANNER and the wrong answer for a recovery trigger: it would hold
  /// the state unhealthy past the moment health returns and delay the edge this row exists
  /// to detect. Plan GV-12 §0.6.
  /// </remarks>
  public bool? AuthBlackout { get; set; }

  /// <summary>
  /// UTC timestamp of RotaryPhone's last successful upstream GV call. Null = never, or not
  /// reported.
  /// </summary>
  /// <remarks>
  /// ⚠ NULL IS NOT TREATED AS UNHEALTHY, which deliberately departs from the wording in
  /// docs/queue/GV-12.md:46. That wording is correct for a banner (fail visible) and would
  /// be fatal here (fail silent) — see the block comment above and plan GV-12 §0.4.
  /// </remarks>
  public DateTime? LastApiSuccessAt { get; set; }
}
```

---

## 2. Task 2 — the health predicate, as a pure function plus one service property

Two pieces: a pure static that can be tested without a poll or a clock, and the property that
`PhonePage` reads.

### 2a. New file `src/Radio.Web/Services/GvBridgeHealth.cs`

```csharp
using Radio.Web.Models;

namespace Radio.Web.Services;

/// <summary>
/// The GV bridge health predicate (GV-12). Pure and static so it can be exercised with a
/// literal timestamp instead of a wall clock — the house rule from CLAUDE.md § Test Timing:
/// count events, never race one clock against another.
/// </summary>
public static class GvBridgeHealth
{
  /// <summary>
  /// How stale <see cref="GvBridgeStatusDto.LastApiSuccessAt"/> may be before the bridge is
  /// considered unhealthy. ~2 min per docs/queue/GV-12.md:46.
  /// </summary>
  public static readonly TimeSpan LastSuccessStaleAfter = TimeSpan.FromMinutes(2);

  /// <summary>
  /// True when the bridge looks usable. A null status (the poll itself failed) is unhealthy.
  /// </summary>
  /// <remarks>
  /// ⚠⚠ THE ASYMMETRY IS THE DESIGN. A field that is PRESENT and says "bad" makes this false;
  /// a field that is ABSENT contributes nothing. This predicate gates an unhealthy→healthy
  /// EDGE, so any term that can never clear would pin the state and silently disable the whole
  /// of GV-12 — see plan GV-12 §0.4 for the two live ways that happens. It is NOT a hedge and
  /// NOT laziness about unknowns.
  ///
  /// ⛔ Do not reuse this for a status BANNER without re-deriving it. A banner wants the
  /// opposite failure direction: unknown should read as wrong, loudly. Two predicates, two
  /// directions; give a banner its own rather than tightening this one.
  ///
  /// ⚠ Available is checked non-defensively on purpose: it is the one field RotaryPhone always
  /// sends, and the 2026-09-08 capture read `available:false` throughout the total outage while
  /// `degraded` and `authBlackout` both read false. If every other term is absent this
  /// degrades to `!Available`, which is sufficient to have caught that incident.
  /// </remarks>
  public static bool IsHealthy(GvBridgeStatusDto? status, DateTimeOffset now)
  {
    if (status is null)
    {
      return false;                       // the poll itself failed — GvBridgeStatusService
    }                                     // maps that to a null status (GvBridgeStatusService.cs:118)

    if (!status.Available)
    {
      return false;
    }

    if (status.CookiesValid == false || status.Degraded == true || status.AuthBlackout == true)
    {
      return false;                       // present AND bad. `== ` against bool? is deliberate:
    }                                     // null must not satisfy any of these.

    if (status.LastApiSuccessAt is DateTime last)
    {
      // Reported. Treat a stale success as unhealthy; treat ABSENT as no signal (see remarks).
      // DateTimeKind is not guaranteed on a deserialized value, so compare in UTC explicitly
      // rather than trusting the kind RotaryPhone happened to serialize.
      var lastUtc = last.Kind == DateTimeKind.Unspecified
        ? DateTime.SpecifyKind(last, DateTimeKind.Utc)
        : last.ToUniversalTime();
      if (now.UtcDateTime - lastUtc > LastSuccessStaleAfter)
      {
        return false;
      }
    }

    return true;
  }
}
```

### 2b. `src/Radio.Web/Services/GvBridgeStatusService.cs`

Three edits. **`IsAvailable` is left exactly as it is** — it drives the banner and the Send gate, and
this row must not move either.

**(i)** Add the field and constructor parameter. Replace lines 24-45:

```csharp
  private readonly IServiceScopeFactory _scopeFactory;
  private readonly ILogger<GvBridgeStatusService> _logger;
  private readonly TimeProvider _timeProvider;
  private readonly int _pollSeconds;
  // Guards Start() idempotency across threads (0 = not started, 1 = started).
  private int _started;
  private PeriodicTimer? _timer;
  private Task? _loop;
  private CancellationTokenSource? _cts;

  public GvBridgeStatusDto? Current { get; private set; }

  /// <summary>
  /// Whether the bridge reported <c>available: true</c> on the last poll. Drives the
  /// reconnecting banner and the Send gate.
  /// </summary>
  /// <remarks>
  /// ⚠ This is NOT <see cref="IsHealthy"/> and GV-12 deliberately did not merge them. This one
  /// is a single field; that one is the fuller shape and is allowed to be false while this is
  /// true. Changing what this property means would move the banner, which GV-12 is not
  /// scoped to touch.
  /// </remarks>
  public bool IsAvailable { get; private set; }

  /// <summary>
  /// Whether the bridge looks usable on the fuller shape (GV-12). The unhealthy→healthy
  /// transition of THIS property is the refetch trigger; see PhonePage.OnGvStatusChanged.
  /// </summary>
  public bool IsHealthy { get; private set; }

  public event Action<GvBridgeStatusDto?>? StatusChanged;

  public GvBridgeStatusService(
    IServiceScopeFactory scopeFactory,
    ILogger<GvBridgeStatusService> logger,
    int pollSeconds = 10,
    TimeProvider? timeProvider = null)
  {
    _scopeFactory = scopeFactory;
    _logger = logger;
    _pollSeconds = pollSeconds <= 0 ? 10 : pollSeconds;
    _timeProvider = timeProvider ?? TimeProvider.System;
  }
```

⚠ `timeProvider` is added **last and optional**, so the two existing three-argument construction
sites — [`Program.cs:432-436`](../../src/Radio.Web/Program.cs) and
`PhonePageThreadLoadErrorTests.cs:93-95` — keep compiling unchanged. Do not reorder the parameters.

**(ii)** Set it in `ApplyStatus`. Replace lines 125-130:

```csharp
  private void ApplyStatus(GvBridgeStatusDto? status)
  {
    Current = status;
    IsAvailable = status is { Available: true };
    // ⚠ Assign BEFORE raising StatusChanged: PhonePage.OnGvStatusChanged reads IsHealthy off
    // this instance rather than off the DTO argument, so a subscriber invoked first would read
    // the previous poll's value and either miss the recovery edge or fire a spurious one.
    IsHealthy = GvBridgeHealth.IsHealthy(status, _timeProvider.GetUtcNow());
    StatusChanged?.Invoke(status);
  }
```

**(iii)** No change to `ApplyStatusForTest` (line 123) — it already routes through `ApplyStatus`, so
it drives `IsHealthy` too. That is the seam Task 5's test uses.

---

## 3. Task 3 — the recovery edge and the refetch, in `PhonePage`

**File:** `src/Radio.Web/Components/Pages/PhonePage.razor`.

**(i)** Add two fields next to the other GV state. After line 160 (`private bool _switchingMode;`):

```csharp
  // ── GV-12: the recovery edge ──────────────────────────────────
  // ⚠⚠ _gvHealthyLast IS SEPARATE FROM _gvBridgeAvailable ON PURPOSE, AND MERGING THEM
  // REINTRODUCES THE BUG SILENTLY. _gvBridgeAvailable has three writers — OnInitializedAsync
  // (:221), RefreshGvStatusAsync (:351, reached every 6th 5 s poll = 30 s), and
  // OnGvStatusChanged (:456) — because this page runs its OWN status poll alongside the
  // singleton's. If RefreshGvStatusAsync lands the recovery first, the field is already true
  // when OnGvStatusChanged runs, the comparison reads true→true, the edge is swallowed and the
  // panels stay stuck. Intermittently, on a timer alignment nobody can reproduce on demand.
  // This field is written by OnGvStatusChanged and by NOTHING else. Plan GV-12 §0.7.
  //
  // null = no status seen yet. The FIRST delivery must never refetch: OnInitializedAsync has
  // already fired LoadVoicemailsAsync/LoadThreadsAsync (:258-259) and a refetch here would
  // duplicate the mount fetch on every page open.
  private bool? _gvHealthyLast;

  // Re-entrancy guard. The status poll is 10 s and a refetch is two upstream list calls
  // through RotaryPhone to Google; a slow one must not stack behind the next edge.
  private bool _gvRefetching;
```

**(ii)** Replace `OnGvStatusChanged` in full (lines 451-459):

```csharp
  // GV Messages: the shared status poll drives the reconnecting banner and may
  // change the badge sum; reflect both and re-render.
  //
  // GV-12 — this is also the refetch trigger. The unhealthy→healthy transition of the shared
  // poll is precisely the instant a "Couldn't load…" panel becomes WRONG rather than merely
  // unlucky, and it is the instant the banner above the panel clears. One trigger, one clock.
  //
  // ⚠⚠ THIS IS THE ONLY MECHANISM BY WHICH A LONG-LIVED CIRCUIT EVER RECOVERS. A Blazor Server
  // circuit that drops and reconnects resumes the SAME component instances with the SAME
  // fields — OnInitializedAsync runs once per CIRCUIT, not once per connection, which is the
  // whole point of DisconnectedCircuitRetentionPeriod (Program.cs:69). So reconnection never
  // re-mounts and never re-fetches, however clean it is. Only a full page load does, which is
  // why the 2026-09-08 16:07 restart cleared every stuck panel and the preceding 83 minutes
  // cleared none. Plan GV-12 §0.2.
  private void OnGvStatusChanged(GvBridgeStatusDto? status)
  {
    if (_disposed) return;
    _gvBridgeAvailable = GvBridgeStatus.IsAvailable;
    PhoneUnread.Set(UnreadSum);

    var healthy = GvBridgeStatus.IsHealthy;
    var recovered = _gvHealthyLast == false && healthy;
    _gvHealthyLast = healthy;

    if (!recovered)
    {
      _ = InvokeAsync(StateHasChanged);
      return;
    }

    // Fired on the status service's poll thread, not the Blazor sync context. Marshal the
    // WHOLE body before touching component state — the same rule OnReadStateChanged (:473)
    // and OnGvSmsReceived (:776) already follow on this page.
    _ = InvokeAsync(RefetchAfterRecoveryAsync);
  }

  /// <summary>
  /// GV-12 — re-run the Messages fetches after the bridge came back, without user input.
  /// </summary>
  /// <remarks>
  /// ⚠ The open conversation is refetched ONLY when it is currently showing its error state.
  /// A successfully-loaded conversation is left alone: re-opening one costs 2-3 upstream Google
  /// calls (see RetryOpenThreadAsync's remarks at :727-732) and would re-run the mark-read
  /// write-through for a thread whose read state is already settled. The lists are cheap by
  /// comparison — one call each — and are refreshed unconditionally, because a list that
  /// succeeded before the outage is stale rather than wrong and the badge sum depends on it.
  ///
  /// ⚠ RetryOpenThreadAsync is reused rather than LoadOpenThreadMessagesAsync directly: on
  /// success the user is finally looking at the conversation, so it must carry the read-marking
  /// the failed open skipped. That logic already lives in RetryOpenThreadAsync and must not be
  /// duplicated here.
  ///
  /// ⚠ Failures are swallowed. This is an unattended background refresh; a toast for it would
  /// fire on a kiosk in a family room for something nobody asked for. LoadThreadsAsync and
  /// LoadVoicemailsAsync already keep their last good list and already raise their own calm
  /// "Couldn't refresh" warning when they have one to keep.
  /// </remarks>
  private async Task RefetchAfterRecoveryAsync()
  {
    if (_disposed || _gvRefetching) return;
    _gvRefetching = true;
    try
    {
      await LoadVoicemailsAsync();
      if (_disposed) return;
      await LoadThreadsAsync();
      if (_disposed) return;

      if (_openThreadError && _openThreadId != null)
      {
        await RetryOpenThreadAsync();
      }
    }
    catch { /* unattended refresh — the next edge or the Task 4 backstop retries */ }
    finally
    {
      _gvRefetching = false;
      if (!_disposed) await InvokeAsync(StateHasChanged);
    }
  }
```

---

## 4. Task 4 — the error-gated backstop for the case the edge cannot see

Closes `C-405` (§0.5): a list fetch that failed while the bridge stayed healthy produces **no edge**,
so the edge trigger alone leaves the row reproducible. This adds **no new timer** — it rides the 5 s
timer already running at `PhonePage.razor:262-264`.

**File:** `src/Radio.Web/Components/Pages/PhonePage.razor`. In `PollStatusAsync`, replace lines 389-393
(the `_pollCount` block):

```csharp
        // Refresh GV status every 6th poll (30s) as SignalR fallback
        if (++_pollCount % 6 == 0)
        {
          await RefreshGvStatusAsync();

          // GV-12 backstop. The recovery edge in OnGvStatusChanged handles the case the row
          // was filed for — the bridge went down and came back. It CANNOT handle a list fetch
          // that failed while the bridge stayed healthy: GetSmsThreadsAsync returns null on any
          // exception (a one-off timeout, a reset connection, a 500 on that route alone), the
          // shared status poll never leaves healthy, and no edge ever occurs. Same stuck panel,
          // no trigger. Plan GV-12 §0.5.
          //
          // ⚠ ERROR-GATED, NOT UNCONDITIONAL, and that is the difference between this and the
          // backoff loop §0.6 rejected. It runs only while a surface is actually showing
          // "Couldn't load…", so a healthy console adds zero calls, and a console stuck against
          // a dead upstream adds two calls per 30 s rather than per 5 s. RotaryPhone's uptime is
          // not settled — a confirmed session-starvation issue — so retry volume into them is a
          // real cost, not a rounding error.
          //
          // ⚠ The error flags are only ever set when the corresponding list is null
          // (LoadThreadsAsync :613-615, LoadVoicemailsAsync :527-530), so this cannot fire for a
          // surface that has content and is merely stale.
          if (!_gvRefetching && (_threadsError || _voicemailError))
          {
            if (_voicemailError) await LoadVoicemailsAsync();
            if (_threadsError) await LoadThreadsAsync();
          }
        }
```

⚠ The open conversation is deliberately **not** in the backstop. It stays edge-only and stays behind
the Retry button, for the cost reason in `RetryOpenThreadAsync`'s own remarks (`:727-732`): a repeated
open is 2-3 upstream Google calls, and a conversation is a thing the user is looking at and can retry
deliberately.

---

## 5. Task 5 — the tests, RED first

**⚠ The row's hard requirement: confirm RED against `main` before trusting green.** Today nothing
refetches at all, so an assertion that a refetch happened *must* fail on `56872b55`. Run each new test
against `main` first and record the failure; a test that is green on `main` is testing the wrong thing.

### 5a. New file `tests/Radio.Web.Tests/Services/GvBridgeHealthTests.cs`

Pure predicate, literal timestamps, no clock.

```csharp
using Radio.Web.Models;
using Radio.Web.Services;

namespace Radio.Web.Tests.Services;

/// <summary>
/// GV-12 § the health predicate. The cases that matter are the ABSENT ones: this predicate
/// gates a recovery EDGE, so a term that can never clear silently disables the whole row.
/// </summary>
public class GvBridgeHealthTests
{
  private static readonly DateTimeOffset Now =
    new(2026, 9, 8, 15, 31, 17, TimeSpan.Zero);

  [Fact]
  public void NullStatus_IsUnhealthy()
  {
    Assert.False(GvBridgeHealth.IsHealthy(null, Now));
  }

  [Fact]
  public void TheOutageCapture_IsUnhealthy()
  {
    // The live capture from docs/queue/GV-12.md:33-36, verbatim. degraded and authBlackout
    // both false through a TOTAL outage — this is the case the retracted morning guidance
    // would have missed, and the reason Available is a term at all.
    var status = new GvBridgeStatusDto
    {
      Available = false,
      Degraded = false,
      AuthBlackout = false,
      CookiesValid = false,
      LastApiSuccessAt = null
    };

    Assert.False(GvBridgeHealth.IsHealthy(status, Now));
  }

  [Fact]
  public void AvailableWithEveryOtherFieldAbsent_IsHealthy()
  {
    // ⚠⚠ THE LOAD-BEARING TEST. This is what RotaryPhone serves on a build that has not
    // deployed the new fields, and it is the shape the response has TODAY. If this ever
    // asserts false, the predicate can never reach healthy on that build, the recovery edge
    // never fires, and GV-12 ships as a silent no-op with a green suite. Plan §0.4.
    var status = new GvBridgeStatusDto { Available = true, ActiveMode = "GoogleVoice" };

    Assert.True(GvBridgeHealth.IsHealthy(status, Now));
  }

  [Fact]
  public void PresentAndBad_IsUnhealthy()
  {
    Assert.False(GvBridgeHealth.IsHealthy(
      new GvBridgeStatusDto { Available = true, CookiesValid = false }, Now));
    Assert.False(GvBridgeHealth.IsHealthy(
      new GvBridgeStatusDto { Available = true, Degraded = true }, Now));
    Assert.False(GvBridgeHealth.IsHealthy(
      new GvBridgeStatusDto { Available = true, AuthBlackout = true }, Now));
  }

  [Fact]
  public void StaleLastSuccess_IsUnhealthy_ButAbsentIsNot()
  {
    var stale = new GvBridgeStatusDto
    {
      Available = true,
      LastApiSuccessAt = Now.UtcDateTime.AddMinutes(-3)
    };
    Assert.False(GvBridgeHealth.IsHealthy(stale, Now));

    var fresh = new GvBridgeStatusDto
    {
      Available = true,
      LastApiSuccessAt = Now.UtcDateTime.AddSeconds(-30)
    };
    Assert.True(GvBridgeHealth.IsHealthy(fresh, Now));

    // Absent — deliberately healthy, departing from docs/queue/GV-12.md:46. Plan §0.4.
    var absent = new GvBridgeStatusDto { Available = true, LastApiSuccessAt = null };
    Assert.True(GvBridgeHealth.IsHealthy(absent, Now));
  }
}
```

### 5b. New file `tests/Radio.Web.Tests/Components/Pages/PhonePageRecoveryRefetchTests.cs`

The row's stated gate: fail N times, then succeed, and assert the panel refetches **without user
input**.

⚠ It asserts on **request counts**, not rendered copy. The error copy lives behind
`PhoneMessagesPanel`'s segment filter (`:110-117` is reachable only under `_filter == "texts"`), so a
markup assertion would couple this regression gate to which tab happens to be selected. The count is
the row's actual claim.

⚠ It drives `GvBridgeStatusService.ApplyStatusForTest` directly rather than waiting for a poll — the
service is registered-but-never-started in this suite (`PhonePageThreadLoadErrorTests.cs:91-95`), and
`ApplyStatusForTest` routes through the real `ApplyStatus`, so `IsHealthy` is set by production code.
**Events are counted, not timed** — CLAUDE.md § Test Timing.

```csharp
using System.Net;
using System.Text;
using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Radzen;
using Radio.Web.Components.Pages;
using Radio.Web.Models;
using Radio.Web.Services;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Components.Pages;

/// <summary>
/// GV-12 regression gate. On 2026-09-08 RotaryPhone's GV bridge was dead 14:08–15:31 EDT.
/// After it recovered, radio-web made ZERO further GV calls and the phone surface sat on
/// "Couldn't load…" against a fully healthy backend until a human tapped Retry — because the
/// panels fetch on mount and never again, and a Blazor circuit that survives the outage never
/// re-mounts.
///
/// ⚠ MUST FAIL AGAINST main@56872b55. Today nothing refetches at all, so
/// Recovery_RefetchesThreads_WithoutUserInput asserting a second request is the assertion that
/// was missing. A version of this test that passes on main is testing the wrong thing.
/// </summary>
public class PhonePageRecoveryRefetchTests : TestContext
{
  private const string ThreadId = "thread-1";
  private const string ContactName = "Recovery Contact";

  private readonly RecoveringGvHandler _gv = new();

  public PhonePageRecoveryRefetchTests()
  {
    Services.AddHermeticTestRig();
    JSInterop.Mode = JSRuntimeMode.Loose;
    Services.AddRadzenComponents();

    Services.AddHttpClient<PhoneApiService>(client =>
    {
      client.BaseAddress = new Uri(HermeticTestRig.PhoneApiBaseUrl);
    }).ConfigurePrimaryHttpMessageHandler(() => new QuietHandler());

    Services.AddHttpClient<PbapApiService>(client =>
    {
      client.BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl);
    }).ConfigurePrimaryHttpMessageHandler(() => new QuietHandler());

    Services.AddHttpClient<BluetoothApiService>(client =>
    {
      client.BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl);
    }).ConfigurePrimaryHttpMessageHandler(() => new QuietHandler());

    // One shared instance so the test can read its counters after the render.
    Services.AddHttpClient<GvBridgeApiService>(client =>
    {
      client.BaseAddress = new Uri(HermeticTestRig.PhoneApiBaseUrl);
    }).ConfigurePrimaryHttpMessageHandler(() => _gv);

    Services.AddSingleton<GvMarkReadDarkLatch>();

    Services.AddHttpClient<GvTrunkApiService>(client =>
    {
      client.BaseAddress = new Uri(HermeticTestRig.PhoneApiBaseUrl);
    }).ConfigurePrimaryHttpMessageHandler(() => new QuietHandler());

    var config = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        ["RotaryPhone:HubUrl"] = $"{HermeticTestRig.PhoneApiBaseUrl}/hub",
        ["RotaryPhone:ApiBaseUrl"] = HermeticTestRig.PhoneApiBaseUrl
      })
      .Build();
    Services.AddSingleton<IConfiguration>(config);
    Services.AddSingleton(new PhoneHubService(
      NullLogger<PhoneHubService>.Instance, config, new OfflineHubTransport()));
    Services.AddSingleton(new GvTrunkHubService(
      NullLogger<GvTrunkHubService>.Instance, config, new OfflineHubTransport()));

    Services.AddSingleton<PhoneUnreadState>();
    // Registered, never started — the test drives ApplyStatusForTest instead of a live poll.
    Services.AddSingleton(sp => new GvBridgeStatusService(
      sp.GetRequiredService<IServiceScopeFactory>(),
      NullLogger<GvBridgeStatusService>.Instance, 10));
    Services.AddSingleton(sp => new BellHealthService(
      sp.GetRequiredService<IServiceScopeFactory>(),
      NullLogger<BellHealthService>.Instance, 15));

    Services.AddScoped<ContactResolutionService>();
  }

  private static GvBridgeStatusDto Unhealthy() =>
    new() { Available = false, ActiveMode = "BluetoothHfp" };

  private static GvBridgeStatusDto Healthy() =>
    new() { Available = true, ActiveMode = "GoogleVoice" };

  [Fact]
  public void Recovery_RefetchesThreads_WithoutUserInput()
  {
    _gv.FailThreadList = true;

    var cut = RenderComponent<PhonePage>();
    var status = Services.GetRequiredService<GvBridgeStatusService>();

    // Mount fetch happened and failed — this is 15:31:16, the panel showing "Couldn't load…".
    cut.WaitForAssertion(
      () => Assert.Equal(1, _gv.ThreadListCalls),
      timeout: TimeSpan.FromSeconds(5));

    // Seed the unhealthy side of the edge. The FIRST delivery must never refetch: the mount
    // already fetched, and a refetch here would double every page open.
    status.ApplyStatusForTest(Unhealthy());
    cut.WaitForAssertion(
      () => Assert.Equal(1, _gv.ThreadListCalls),
      timeout: TimeSpan.FromSeconds(2));

    // 15:31:17 — the bridge comes back. Nothing else happens. No click, no keystroke.
    _gv.FailThreadList = false;
    status.ApplyStatusForTest(Healthy());

    // ⚠ THE ASSERTION THE ROW EXISTS FOR. RED on main: today this stays at 1 forever.
    cut.WaitForAssertion(
      () => Assert.True(_gv.ThreadListCalls >= 2,
        $"expected a refetch after recovery; thread-list calls = {_gv.ThreadListCalls}"),
      timeout: TimeSpan.FromSeconds(5));

    // And the recovered content actually landed, not just a request.
    cut.WaitForAssertion(
      () => Assert.Contains(ContactName, cut.Markup),
      timeout: TimeSpan.FromSeconds(5));
  }

  [Fact]
  public void RepeatedHealthyStatus_DoesNotRefetchAgain()
  {
    // Only the EDGE refetches. A healthy poll every 10 s must not become a fetch every 10 s —
    // that is the backoff loop this row deliberately did not build.
    var cut = RenderComponent<PhonePage>();
    var status = Services.GetRequiredService<GvBridgeStatusService>();

    cut.WaitForAssertion(
      () => Assert.Equal(1, _gv.ThreadListCalls),
      timeout: TimeSpan.FromSeconds(5));

    status.ApplyStatusForTest(Unhealthy());
    status.ApplyStatusForTest(Healthy());

    cut.WaitForAssertion(
      () => Assert.Equal(2, _gv.ThreadListCalls),
      timeout: TimeSpan.FromSeconds(5));

    for (var i = 0; i < 5; i++)
    {
      status.ApplyStatusForTest(Healthy());
    }

    // Still 2. No edge, no fetch.
    cut.WaitForAssertion(
      () => Assert.Equal(2, _gv.ThreadListCalls),
      timeout: TimeSpan.FromSeconds(2));
  }

  /// <summary>
  /// Serves the GV routes, counts the SMS thread-list requests, and can be flipped from
  /// failing to succeeding mid-test — the row's "fail N times, then succeed".
  /// </summary>
  private class RecoveringGvHandler : HttpMessageHandler
  {
    private int _threadListCalls;

    public volatile bool FailThreadList;
    public int ThreadListCalls => Volatile.Read(ref _threadListCalls);

    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
    {
      var path = request.RequestUri?.PathAndQuery ?? "";

      // The two SMS routes differ only by the trailing segment, so the bodies route must be
      // matched FIRST — same ordering note as PhonePageThreadLoadErrorTests.
      if (path.Contains("/sms/threads/"))
      {
        return Json(HttpStatusCode.OK,
          $$"""{"threadId":"{{ThreadId}}","messages":[],"fetchedAtUtc":"2026-09-08T19:31:17Z"}""");
      }

      if (path.Contains("/sms/threads?"))
      {
        Interlocked.Increment(ref _threadListCalls);
        if (FailThreadList)
        {
          return Json(HttpStatusCode.BadGateway, """{"error":"upstream_error"}""");
        }
        return Json(HttpStatusCode.OK, $$"""
          {"threads":[{"threadId":"{{ThreadId}}","counterpartyNumber":"+15551234567",
          "counterpartyName":"{{ContactName}}","lastMessageAt":"2026-09-08T19:00:00Z",
          "hasUnread":false,"lastMessagePreview":"Hello"}],
          "fetchedAtUtc":"2026-09-08T19:31:17Z"}
          """);
      }

      if (path.Contains("/api/gvbridge/voicemail"))
      {
        return Json(HttpStatusCode.OK,
          """{"items":[],"nextPageToken":null,"fetchedAtUtc":"2026-09-08T19:31:17Z"}""");
      }

      if (path.Contains("/api/gvbridge/status"))
      {
        return Json(HttpStatusCode.OK, """{"available":true,"activeMode":"GoogleVoice"}""");
      }

      return Json(HttpStatusCode.OK, path.Contains("/api/gvbridge/") ? "[]" : "{}");
    }

    private static Task<HttpResponseMessage> Json(HttpStatusCode code, string body) =>
      Task.FromResult(new HttpResponseMessage(code)
      {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
      });
  }

  /// <summary>Adapted from PhonePageThreadLoadErrorTests.EmptyResponseHandler.</summary>
  private class QuietHandler : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
    {
      var path = request.RequestUri?.PathAndQuery ?? "";
      string content = path switch
      {
        var p when p.Contains("system-status") =>
          """{"platform":"Linux","sipListening":false,"ht801IpAddress":"192.168.1.57","ht801Reachable":true}""",
        var p when p.Contains("/api/phone/status") => """{"callState":"Idle"}""",
        var p when p.Contains("/api/contacts") => "[]",
        var p when p.Contains("/api/callhistory") => "[]",
        var p when p.Contains("/api/bluetooth/pbap/status") => """{"devices":[]}""",
        var p when p.Contains("/api/bluetooth/pbap/contacts") => "[]",
        var p when p.Contains("/api/bluetooth/status") =>
          """{"isAvailable":true,"state":"Powered","isDiscovering":false,"pairedDevices":[],"discoveredDevices":[]}""",
        var p when p.Contains("/api/gvtrunk/status") =>
          """{"isRegistered":false,"callState":"Idle","activeCallDurationSeconds":0}""",
        var p when p.Contains("/api/gvtrunk/") => "[]",
        _ => "{}"
      };
      return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
      });
    }
  }
}
```

---

## 6. Gates

```bash
dotnet build RadioConsole.sln -c Release > /tmp/build.log 2>&1; echo "exit=$?"
grep -E "Warning\(s\)|Error\(s\)" /tmp/build.log

dotnet test RadioConsole.sln -c Release > /tmp/test.log 2>&1; echo "exit=$?"
grep -E "Passed!|Failed!|error" /tmp/test.log
```

⛔ **Never pipe `dotnet test` into `tail`** — CLAUDE.md records a run that exited `0` with five tests
failing. Redirect, then read.

- **Warning baseline is 47, 0 errors**, and the gate is **equality**, not an absolute. If the count
  looks off, build `main` and compare rather than trusting the figure.
- Known-failing on Windows and not regressions: four `SrcVariableResamplerTests`,
  `NwsObservationIntegrationTests.RealNwsCall_*`, and
  `CoverArtPipelineIntegrationTests.CoverArtArchive_ReturnsValidUrl_ForKnownRecording`.
- **Run Task 5's tests against `main` first and record them RED.**

---

## 7. Verification

### 7.1 What the unit tests establish

That the panels refetch on the recovery edge with no user input, that the first status delivery does
not double the mount fetch, that repeated healthy polls do not become a retry loop, and that the
predicate stays reachable-healthy on a response carrying only `available`.

### 7.2 ⚠⚠ What they do NOT establish, and the honest gate

**A green suite does not prove recovery on the appliance.** It proves the trigger fires when the
status service reports the edge. It does not prove RotaryPhone's `/api/gvbridge/status` actually
reports that edge on the real recovery path, which is the one link this repo cannot test.

⛔ **Do not manufacture the outage on the box.** RotaryPhone's uptime is not settled — a confirmed
session-starvation issue means a restart is a coin-flip on another 83-minute outage. Bringing their
bridge down to test our retry risks causing the incident we are fixing.

**The next real outage is the test.** What to capture when it happens:

```bash
# Ours: did the lists get re-requested after their recovery, with nobody touching the kiosk?
ssh mmack@radio 'F=$(ls -t /opt/radio-console/logs/radio-*.txt | head -1); grep -n "gvbridge" $F | tail -40'
```

⭐ **Interim mitigation, and it is real: restarting `radio-web` clears a stuck panel.** A new process
forces every client to reload, which mounts fresh components, which fetches. Any deploy does it
incidentally. Ugly, and worth knowing while this is queued.

### 7.3 Measured vs. inferred

| Claim | Standing |
|---|---|
| Circuit client timeout is 120 s, not 30 s | **Read from source**, `App.razor:76` |
| All four `HubConnection` clients use the 30 s default | **Read from source** — zero `ServerTimeout` hits outside `App.razor` |
| `radio-api` `KeepAliveInterval` is 30 s | **Read from source**, `Radio.API/Program.cs:87` |
| Therefore the 30 s timeouts are hub clients, not the circuit | **Inferred** from the three above. Strong, and cheap to confirm on the box by which logger category emits the line |
| A Blazor reconnect does not re-mount | **Framework behaviour**, corroborated in-tree by `Program.cs:66-69`'s own comment |
| `Degraded` / `AuthBlackout` / `LastApiSuccessAt` absent from our DTO | **Read from source**, `ApiModels.cs:1100-1109` |
| `psidts*` has zero references in `src/` | **Grepped**, zero hits (fourth independent check) |
| RotaryPhone serves the three fields today | **Not verified here.** Their capture only. Task 1 is built so it does not matter — §0.8 |

---

## 8. Follow-ups — not this PR

1. **`UI-10` should be re-pointed, not closed.** The defect is real and is ours; the connection is
   wrong. The fix is one line per client (`.WithServerTimeout(TimeSpan.FromMinutes(2))` on the four
   `HubConnectionBuilder` sites, matching `radio-api`'s `ClientTimeoutInterval`), and that row should
   carry §0.1's evidence and the per-day `grep -c 'Server timeout'` baseline it already asks for.
2. **`PhonePage` runs a second `/api/gvbridge/status` poll** (`RefreshGvStatusAsync`, every 30 s)
   alongside the singleton's 10 s poll, and both write `_gvBridgeAvailable`. Redundant, and the source
   of the trap in §0.7. Removing the page-local one is a behaviour change to the banner and needs its
   own row.
3. **The new RotaryPhone fields** — `psidtsMintedAtUtc`, `browserSessionValidatedAt`,
   `browserSessionAgeSeconds`, `browserSessionStale` — are **merged-not-deployed** on their side.
   Nothing here binds to them. When they deploy, they are additive terms in `GvBridgeHealth.IsHealthy`
   and must follow §0.4's rule: `psidtsMintedAtUtc` is nullable with **no upper bound**, so `null`
   must not make this predicate permanently unhealthy however it is treated in a banner.
4. **A status banner bound to the fuller shape** — with `authBlackout` latched to a minimum display
   window — is a separate row with the opposite failure direction (§0.4, §0.6). It must not reuse
   `GvBridgeHealth.IsHealthy`.

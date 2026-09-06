# PLAN — `GV-6` · A dark mark-read feature stops looking like a broken one

> **Row:** `GV-6`, [`docs/queue/GV-6.md`](../../docs/queue/GV-6.md). 📋 queued, `_plan TBD (small)_`.
> **Branch:** `fix/gv-markread-dark-409` (the row names it).
> **Depends on:** `GV-4` ✅ merged (#441). The preferred predecessor `GV-8` ✅ merged (#461) and
> left behind the exact idiom this row was told to adopt.
> **Estimate:** **0.5 d.** §0.5 says what would push it to 1 d.
> **Spec:** [ADR-024 §3.3](../design/decisions/2026-06-20-gv-mark-read-durable-readstate.md),
> amended 2026-07-31 — the amendment names this row by number as the thing that closes it.
> **Planned against** `main` at **`35e4ed5a`**. Every line number below was read out of the tree at
> that commit. Where a line is likely to move it is quoted as well as numbered.
> **`D31` status:** ✅ assessed and **unaffected**. `D31` parked SMS *sending*. Mark-read is a
> different feature behind a different flag (`RotaryPhone:Gv:MarkReadEnabled`,
> `src/Radio.Web/appsettings.json:22`) on different routes with a different `409` code. ⛔ Do not
> park this because `GV-5` was parked.

---

## 0. Read this before Task 1

### 0.1 What this row is, in one paragraph

RotaryPhone's two mark-read routes answer `409 {"error":"markread_disabled"}` when *their*
server-side `GVBridge:EnableMarkRead` is `false` — checked at step 0, before any Google call.
Our client folds that response into the same `return null` it uses for a `502`, a timeout and a
transport failure, so the caller cannot tell "the feature is switched off" from "Google is
unreachable," and the operator gets one `LogError` line per user action for as long as the skew
lasts. The fix is to recognise the one response that means *the feature is dark*, say so **once**,
and then stop asking. Everything else about the call — the return value, the optimistic flip, the
absence of any user-visible affordance — stays exactly as it is. This is diagnostic quality, not
correctness, and §0.4 records where the row overstates its own case.

### 0.2 The mechanism, traced

**Where the calls are made.** `src/Radio.Web/Services/ApiClients/GvBridgeApiService.cs`, two
methods:

| | Voicemail | SMS thread |
|---|---|---|
| Method | `MarkVoicemailReadAsync` `:139` | `MarkSmsThreadReadAsync` `:319` |
| Flag gate | `:142` | `:322` |
| `404` → `null`, silent | `:154-157` | `:333-336` |
| every other non-2xx → `LogError` + `null` | `:158-163` | `:337-341` |
| exception → `LogError` + `null` | `:166-170` | `:344-348` |

**What happens today on `409 markread_disabled`.** It falls into the generic non-2xx branch. For
voicemail that is `:161`:

```csharp
_logger.LogError("Mark-read voicemail {Id} failed: {Status}", id, (int)response.StatusCode);
```

The status *number* is logged, so `409` and `502` are already distinguishable on the line — see
`C-132`, this is the row's one overstatement. What is **not** logged is the error discriminator, so
`409 markread_disabled` is indistinguishable from any other `409` RotaryPhone might later add; and
nothing in the message tells a reader that `409` here means *"the feature is off"* rather than
*"conflicting state."* ADR-024 §3.3 calls that overload out explicitly, in the ADR, where the
operator staring at the journal is not.

**What happens on a genuine failure.** Identical: `LogError` with the status, `return null`, caller
keeps its optimistic flip, next list load reconciles. A timeout or a transport failure takes the
`catch (Exception ex)` at `:166`/`:344` and logs *"threw (non-fatal); optimistic flip kept."* None
of that changes.

**Where the repetition comes from — per user action, not per poll.** This is the part the row
leaves open, and it decides the whole shape of the fix.

- **Nothing polls mark-read.** `GvBridgeStatusService` is the only timer touching this surface and
  it calls `/api/gvbridge/status` only — `grep -n 'MarkRead' src/Radio.Web/Services/GvBridgeStatusService.cs`
  returns nothing.
- **Voicemail: once per `VoicemailPlayer` instance.** `MarkHeardOnceAsync`
  (`VoicemailPlayer.razor:469`) is guarded by `_heardSent` (`:144`, set at `:476`). But the player
  is rendered inside `@if (Expanded)` (`VoicemailRow.razor:29-32`), so **collapsing the accordion
  disposes it and re-expanding builds a fresh one with `_heardSent == false`.** Play, collapse,
  re-expand, play → two POSTs, two `LogError` lines, same voicemail.
- **SMS: once per successful thread open, unconditionally.** `OpenThreadAsync`
  (`PhonePage.razor:629`) calls `MarkOpenThreadReadAsync` at `:657`, and `RetryOpenThreadAsync`
  calls it again at `:749`. Neither checks whether the thread was already read — `wasUnread` is
  captured at `:636` but is used only to restore the marker on a *failed* load (`:653`), never to
  skip the write. So opening A, B, A, B is four POSTs and four `LogError` lines.

So the volume is bounded by how fast a person can tap, which is the basis for `C-134`: the latch's
value is one honest line instead of N misleading ones, **not** load reduction.

**What the suppressed POST actually saves.** One LAN round-trip to `radio:5004`. ADR-024 §3.3 is
explicit that the dark check runs *"first, before any validation — no GV call is made"*, so the
dark path never reaches Google. Do not confuse this with the *2–3 upstream Google calls* that a
thread **open** costs (`F-1-DIAGNOSIS.md` § Scope-affecting notes); that is a different call on a
different route (`C-135`).

### 0.3 Where each log line lands — the question this row is entirely about

`CLAUDE.md` § *Deployment* says `journalctl` carries Warning and above since `LOG-11`. **That
sentence is about `Radio.API` only, and this row's code is in `Radio.Web`.** Derived from the tree,
not from the prose:

| | `Radio.API` | `Radio.Web` |
|---|---|---|
| Console sink declared | in code, `Program.cs:48-53` | in config, `appsettings.json:58-71` |
| `restrictedToMinimumLevel` | `Warning` | **none** |
| `MinimumLevel.Default` | — | `Information` (`appsettings.json:50-51`) |
| Console formatter | `SystemdConsoleFormatter` (emits `<N>` priority prefixes) | plain `outputTemplate` — **no prefix** |
| Unit parses priorities | `radio-api.service:94-97` — `SyslogLevelPrefix=true`, `SyslogLevel=debug` | `radio-web.service` — **neither is set** |

Two consequences, both load-bearing here:

1. **In `Radio.Web`, `Information`, `Warning` and `Error` all reach `journalctl -u radio-web`.**
   Choosing Warning over Error does not keep anything out of the journal. Only the latch does
   (`C-130`). `appsettings.json:44`'s own comment says this in the file; believe the file.
2. **`journalctl -p warning -u radio-web` returns nothing, ever.** Without `SyslogLevelPrefix=true`
   the unit's stdout lands at journald's default priority (`info`) regardless of the Serilog level,
   and unlike `radio-api` there is no `<N>` prefix to parse (`C-131`). **The probe for this row's
   line must be a `grep` on a stable substring**, which is also why `GV-8`'s
   `"Failed to get GV SMS thread"` substring is documented as load-bearing at
   `GvBridgeApiService.cs:219-222`.

**Decision, stated plainly:** the one new line is `LogWarning`, in **`Radio.Web`**, and it therefore
**does reach journald on a stock box** — once per process lifetime, with no parameters. §1.4 argues
that price. The existing `LogError` failure lines stay at Error and stay per-action, because each
occurrence is a distinct failed durable write and that is information, not churn.

### 0.4 What the row claims, and what the code says

Three claims were checked. Two hold; one is stronger than the code.

- ✅ *"a different flag: `RotaryPhone:Gv:MarkReadEnabled` (`appsettings.json:22`, read at
  `GvBridgeApiService.cs:142`/`:322`), whose own doc-comment at `:131` calls it 'distinct' from
  send."* — **all four anchors are exact at `35e4ed5a`.** `appsettings.json:22` is the
  `"MarkReadEnabled": false` line; `:131` reads *"distinct from RotaryPhone's server-side
  EnableMarkRead build flag."* Unlike the sibling rows planned this week, `GV-6`'s anchors have not
  drifted.
- ✅ *"Degrades acceptably today — no crash, no wrong badge, next list fetch is authoritative."* —
  holds. `MarkOpenThreadReadAsync` (`PhonePage.razor:673-684`) and `OnVoicemailHeard` (`:569-590`)
  both act only `if (dto != null)`, and `hasUnread`/`isRead` from the list endpoints are
  authoritative on every reload (`INTEGRATIONS.md:728`).
- ⚠ *"Our client maps every non-2xx to `null`, so 'the feature is switched off' is
  indistinguishable from 'GV is unreachable'."* — **true of the return value, not of the log.**
  `:161` and `:339` already log `(int)response.StatusCode`, so an operator reading the journal
  today can already see `409` versus `502`. What is genuinely missing is the **error code**, the
  **meaning**, and the **repetition**. Recorded as `C-132` so the plan is not written against a
  claim stronger than the code, and so the PR body does not sell a smaller win as a larger one.

### 0.5 The estimate

**0.5 d.** One new 20-line type, one DI line, one new private helper plus a four-line branch in each
of two methods, two test-factory signatures, six tests, three doc edits.

What would push it to **1 d**: deciding to consolidate the four existing `CapturingLogger<T>`
copies (out of scope — §6.2), or the Builder choosing to fix the undisposed `HttpResponseMessage`
at `:150` / `:329` in the same PR (§6.1). Neither is asked for.

What will **not** push it out: on-box verification, because there is none to run — see §4.6. The
dark path is unreachable in a Debug/Development checkout and its reachability on `radio` cannot be
determined from this tree (`C-144`).

### 0.6 Constraints found while planning — numbering continues from `C-128` (`AUD-5` / `TEST-7`)

**`C-129` — the typed client is TRANSIENT, so an instance field cannot latch anything.**
`Program.cs:352` registers `AddHttpClient<GvBridgeApiService>(...)`, which is a transient
registration. `PhonePage` resolves it with `@inject` (`PhonePage.razor:5`), so **every component in
every circuit gets its own instance**. A `private bool _dark` on the service would be re-created per
component and would never suppress a second call. The latch must be a singleton.

**`C-130` — in `Radio.Web`, the log LEVEL does not decide whether a line reaches journald.**
`appsettings.json:58-71` declares a Console sink with no `restrictedToMinimumLevel` and
`MinimumLevel.Default: Information` at `:50-51`. Information, Warning and Error all reach
`journalctl -u radio-web`. The row's parenthetical *"at Warning … to avoid journald churn"*
conflates two independent things: the **level** buys readability, the **latch** buys the volume
reduction. Say it that way in the PR body.

**`C-131` — `journalctl -p` cannot filter `radio-web` at all.** `radio-api.service:94-97` sets
`StandardOutput=journal`, `SyslogLevelPrefix=true`, `SyslogLevel=debug`, and `Radio.API`'s console
sink uses `SystemdConsoleFormatter` to emit the `<N>` prefixes those settings parse.
`radio-web.service` sets **none** of them (`:54` region is the whole logging block: `SyslogIdentifier`
and nothing else) and its sink emits a plain `outputTemplate`. Every `radio-web` journal line is
therefore priority `info`. **The documented probe must be a substring `grep`, never `-p warning`.**

**`C-132` — the row overstates the current log.** See §0.4. `409` versus `502` is already visible;
the error code, the meaning and the repetition are not. Do not write a PR body claiming the fix
introduces a distinction that partly existed.

**`C-133` — the repetition is per user action.** Voicemail: one POST per `VoicemailPlayer`
instance, re-armed on every accordion re-expand (`VoicemailRow.razor:29-32` renders it inside
`@if (Expanded)`; `_heardSent` is instance state at `VoicemailPlayer.razor:144`). SMS: one POST per
successful open (`PhonePage.razor:657`) **and** per successful retry (`:749`), unconditional —
re-opening an already-read thread POSTs again. Nothing polls.

**`C-134` — the latch's value is diagnostic clarity, not load reduction.** Following from `C-133`,
the ceiling is a human tapping rows: a few lines a minute at absolute worst, which is not a
journald-volume problem on any box. **Do not sell this as an audio-distortion fix.** It replaces N
misleading `Error` lines with one accurate `Warning` line.

**`C-135` — suppressing the dark POST saves one LAN round-trip, not Google calls.** ADR-024 §3.3:
the dark check runs before any validation and *"no GV call is made."* The *"2-3 upstream Google
calls"* figure in `PhonePage.razor:733-737` and `F-1-DIAGNOSIS.md` is about **opening a thread**,
which is a different route and is unaffected by this row.

**`C-136` — the latch's lifetime is the process, and nothing in-process can clear it.**
`GvBridgeStatusDto` (`src/Radio.Web/Models/ApiModels.cs:1100-1109`) carries `Available`,
`ActiveMode`, `SipRegistered`, `CookiesValid` — **no mark-read capability field** — so the 10 s
status poll cannot observe RotaryPhone re-enabling `EnableMarkRead`. Our own flag cannot change
without a restart either (`appsettings.Production.json` is read at startup; `INTEGRATIONS.md:730`
prescribes flipping theirs first anyway). The row asks for exactly this (*"until restart"*), but
**the operational consequence must be written into `INTEGRATIONS.md`**: once latched, mark-read
stays off until `sudo systemctl restart radio-web`, even after RotaryPhone fixes their side.

**`C-137` — one latch covers both methods.** One server flag (`GVBridge:EnableMarkRead`) gates both
routes, checked at step 0 on each (ADR-024 §3.3). A per-route latch would model a state RotaryPhone
cannot be in. *Falsifier, recorded so it is revisited rather than rediscovered:* if RotaryPhone
ever splits that flag per route, a single latch over-suppresses the still-live route. The thing to
re-read is their contract, not this code.

**`C-138` — latch only on `409` WITH `markread_disabled`.** Any other `409` stays on the generic
failure path. `GvResult`'s own doc-comment (`GvResult.cs:36-43`) prescribes the exact triple —
`Outcome == HttpError && StatusCode == Conflict && ErrorCode == "markread_disabled"` — and names
`GV-6` while doing it. Follow it.

**`C-139` — the one-shot must be `Interlocked`, not check-then-set.** Multiple circuits (the kiosk,
a laptop, a tablet) each hold their own transient `GvBridgeApiService` over one shared singleton
latch, and their handlers run on independent circuit sync-contexts. `if (!_dark) { _dark = true;
Log(); }` races into two log lines, which is precisely the property the row asks for. Use
`Interlocked.Exchange(ref _latched, 1) == 0` as the "am I first" test.

**`C-140` — do NOT use a `static` field on `GvBridgeApiService`.** xUnit runs test classes in
parallel across collections; a static latch set by one test would suppress another test's expected
log and make the suite order-dependent. This repo has already paid for one order/timing-sensitive
test class (`TEST-4`; `CLAUDE.md` § *Test Timing*) and should not mint another. A singleton
resolved from DI is trivially isolated in tests by constructing a fresh one.

**`C-141` — every test gets a FRESH latch unless it is explicitly testing sharing.** The test
factory's new parameter defaults to `new GvMarkReadDarkLatch()` for exactly this reason. A shared
default would make the existing `_On502_NoRetry_` and `_On404_` cases depend on execution order.

**`C-142` — no UI change and no caller change.** The suppressed path returns `null`, byte-identical
to today's non-2xx path and to the flag-off path at `:142`/`:322`, so `OnVoicemailHeard`
(`PhonePage.razor:569`) and `MarkOpenThreadReadAsync` (`:673`) keep their optimistic flip and
reconcile on the next list load. ADR-024 §6: mark-read has no user-visible error affordance **by
design**. **Nothing in `PhonePage.razor`, `PhoneMessagesPanel.razor`, `PhoneTextsPanel.razor`,
`VoicemailRow.razor` or `VoicemailPlayer.razor` changes.** Stated as a constraint rather than left
implicit, because the row says only *"no user-visible affordance"* and a Builder could read that as
permission to add a quiet one.

**`C-143` — the new ctor parameter breaks exactly two call sites, both in one test file.**
`GvBridgeApiServiceVoicemailSmsTests.cs:16-18` (`CreateService`, target-typed `new(...)`) and `:22-29`
(`BuildSvc`). `grep -rn 'new GvBridgeApiService(' src tests` finds no production construction site —
DI builds it. Every other match is inside `docs/superpowers/plans/`, which is historical and must
not be edited.

**`C-144` — whether the dark path is live on `radio` today cannot be determined from this tree.**
The tracked `src/Radio.Web/appsettings.Production.json` has a `RotaryPhone:Gv` block with
`SendEnabled` and `AuthKey` but **no `MarkReadEnabled`** — yet `Deploy-ToLinux.ps1:292-302` records
that the box's *web overlay* carries an operator-authored `MarkReadEnabled` that is not
reconstructible from the repo (that is the state `OPS-7` exists to protect). So the flag's live
value is unknown here. The one read-only command that settles it is in §4.6.

**`C-145` — the new line must carry no parameters, and the existing ones are already clean.**
`PHN-5` §0.2 swept `{ThreadId}` and found GV thread ids opaque (`g.Group Message.<base64url>`), and
`LogSafetyLintTests.cs:143-181` enforces the phone-number rules globally. The existing `{Id}` /
`{ThreadId}` arguments are fine and must not be "fixed." The new Warning takes **no** arguments at
all, so it cannot trip the lint and cannot be argued about later.

### 0.7 Things Builder must NOT do

- ⛔ **Do not park this row because `GV-5` was parked.** Different feature, different flag, different
  routes, different `409` code. The queue row says so in bold and `D31` was assessed against it on
  2026-09-05.
- ⛔ **Do not add a user-visible affordance** — no toast, no badge, no "couldn't sync" pill
  (`C-142`, ADR-024 §6).
- ⛔ **Do not latch on a bare `409`** without the `markread_disabled` discriminator (`C-138`).
- ⛔ **Do not downgrade or throttle the existing `LogError` failure lines.** A repeated genuine
  failure is a repeated genuine failure.
- ⛔ **Do not merge this with `GV-8`.** `GV-8` is merged; the queue's [`ORDERING-NOTES.md`](../../docs/queue/ORDERING-NOTES.md)
  records the judgement that the two rows share the **idiom**, not the PR. This plan adopts
  `GvResult`'s discrimination rule rather than inventing a second mechanism, which is the whole of
  what that note asked for.
- ⛔ **Do not touch `docs/superpowers/plans/*`** — those are historical records that happen to
  contain `new GvBridgeApiService(...)` snippets (`C-143`).

---

## 1. Decision — one process-lifetime singleton latch

### 1.1 The shape, stated first

A tiny injectable singleton holding one `int`, plus a private helper on `GvBridgeApiService` that
owns both the recognition rule and the single log line. Both mark methods gain a two-line guard at
the top and a three-line branch in their existing non-2xx arm.

### 1.2 Why a singleton and not the two obvious alternatives

| Option | Why not |
|---|---|
| `private bool` on `GvBridgeApiService` | The typed client is **transient** (`C-129`). One instance per component per circuit; the latch would never see a second call. |
| `private static bool` on `GvBridgeApiService` | Correct at runtime, hostile in tests: xUnit's parallel classes would leak one test's latch into another's assertions (`C-140`). |
| `AddSingleton<GvMarkReadDarkLatch>` | Correct at runtime **and** trivially isolated — a test constructs its own. |

### 1.3 Lifetime, and what resets it

**Process lifetime. Nothing resets it in-process** (`C-136`). That is what the row specifies
(*"suppresses further calls until restart"*) and it is sufficient, because every way our flag can
change already implies a restart. The gap it leaves is the other direction: **RotaryPhone enabling
their flag while we are latched.** The console will keep mark-read suppressed until `radio-web`
restarts. That is acceptable — ADR-024's rollout order (*flip theirs first, confirm the route stops
rejecting, then flip ours*) never produces the state — but it is a fact an operator has to be able
to find, so Task 6 writes it into `INTEGRATIONS.md` next to the two-flag bullet, with the remedy.

### 1.4 The log level, decided

**`LogWarning`, in `Radio.Web`, therefore in `journalctl -u radio-web` as well as
`/opt/radio-console/logs/web-*.txt`. Once per process.**

- **Why not `Error`.** Nothing is broken. A dark upstream feature with an acceptable degradation is
  a warning about configuration, and calling it an error trains the reader to ignore errors.
- **Why not `Information`.** It would land in the same journal at the same priority (`C-130`,
  `C-131`), so it buys nothing operationally and reads as less important than it is.
- **Why not `Debug`.** `MinimumLevel.Default` is `Information` (`appsettings.json:51`), so a Debug
  line would not be emitted at all on a stock box — a line nobody can ever read is worse than none.
- **What the level costs.** Exactly one journald line per `radio-web` process lifetime. Against
  `C-133`'s per-tap `Error` lines that this replaces, the change is strictly negative volume.
- **Grep anchor.** The message must keep the literal substring **`GV mark-read is dark`**. That is
  the documented probe (`C-131` makes `-p` useless), and it follows the precedent
  `GvBridgeApiService.cs:219-222` sets for `"Failed to get GV SMS thread"`.

⚠ **One wording rule, from `CLAUDE.md` § *Pre-Merge Review*.** The message may assert what *our*
code knows and must only *report* what RotaryPhone's contract says. We know our flag is `true` —
we just passed the check at `:142`. We do **not** observe their `EnableMarkRead`; we observe a
status and a string. The text in Task 4 is phrased that way deliberately: *"…which their contract
defines as…"*, not *"their flag is false."* Do not "tighten" it into a claim the code cannot make.

---

## 2. Tasks

### Task 1 — `GvMarkReadDarkLatch`

**New file:** `src/Radio.Web/Services/ApiClients/GvMarkReadDarkLatch.cs`

```csharp
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
```

### Task 2 — register it

**File:** `src/Radio.Web/Program.cs`. Insert immediately after the `AddHttpClient<GvBridgeApiService>`
block (which ends at `:363`, before the `// There is no GV Bridge SMS *send* client` comment at
`:365`). `GvBridgeApiService` is already named unqualified at `:352`, so the namespace is in scope
and the new type needs no qualification.

```csharp
// GV-6: the dark-mark-read latch. SINGLETON on purpose — the typed client above is TRANSIENT,
// so a field on GvBridgeApiService would be re-created per component per circuit and could never
// suppress a second POST. Nothing clears it: see the type's remarks.
builder.Services.AddSingleton<GvMarkReadDarkLatch>();
```

### Task 3 — take the latch in the constructor

**File:** `src/Radio.Web/Services/ApiClients/GvBridgeApiService.cs`, `:18-32`.

```csharp
  private readonly HttpClient _httpClient;
  private readonly ILogger<GvBridgeApiService> _logger;
  private readonly IConfiguration _configuration;
  private readonly GvMarkReadDarkLatch _markReadDark;
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true
  };

  public GvBridgeApiService(HttpClient httpClient,
    ILogger<GvBridgeApiService> logger, IConfiguration configuration,
    GvMarkReadDarkLatch markReadDark)
  {
    _httpClient = httpClient;
    _logger = logger;
    _configuration = configuration;
    _markReadDark = markReadDark;
  }
```

⚠ This breaks `GvBridgeApiServiceVoicemailSmsTests.cs` until Task 5a. Those are the only two other
construction sites in the tree (`C-143`).

### Task 4 — recognise the dark `409`, in one helper and two four-line branches

**4a. The helper.** Add it directly below `ReadErrorCodeAsync` (i.e. after `:312`), so the two
private diagnostics sit together.

```csharp
  /// <summary>
  /// Was this mark-read failure RotaryPhone's dark-feature rejection rather than a real failure?
  /// If so, latch (suppressing every later mark-read POST for this process) and log ONCE.
  /// Returns <c>true</c> when it handled the response, so the caller skips its generic error log.
  /// <para>
  /// The discriminator is the triple GvResult documents for this row: 409 + an <c>error</c>/
  /// <c>code</c> of <c>markread_disabled</c>. A 409 carrying anything else is NOT latched — it
  /// falls through to the generic failure path, because ADR-024 §3.3 defines this one code and
  /// says nothing about a future second meaning for the status.
  /// </para>
  /// <para>
  /// ONE latch covers BOTH routes: a single server flag (GVBridge:EnableMarkRead) gates both and
  /// is checked at step 0 of each (ADR-024 §3.3), so a per-route latch would model a state
  /// RotaryPhone cannot be in.
  /// </para>
  /// </summary>
  private bool HandledAsMarkReadDark(HttpStatusCode statusCode, string? errorCode)
  {
    if (statusCode != HttpStatusCode.Conflict || errorCode != "markread_disabled")
    {
      return false;
    }

    if (_markReadDark.TryLatch())
    {
      // NO message parameters, deliberately: nothing here is per-item, and this line reaches
      // `journalctl -u radio-web` on a stock box — Radio.Web's Console sink carries no
      // restrictedToMinimumLevel (appsettings.json:58-71), unlike Radio.API's (LOG-11).
      // KEEP the literal "GV mark-read is dark" substring: it is the documented probe, and
      // `journalctl -p warning -u radio-web` finds NOTHING because radio-web.service sets no
      // SyslogLevelPrefix, so every line it writes is journald priority `info`.
      // The second sentence REPORTS their contract; it does not assert their config, which this
      // process cannot observe.
      _logger.LogWarning(
        "GV mark-read is dark: RotaryPhone answered 409 markread_disabled, which their contract "
        + "defines as their GVBridge:EnableMarkRead being false (ADR-024 §3.3), while our "
        + "RotaryPhone:Gv:MarkReadEnabled is true. Read-state stays UI-local and reverts on the "
        + "next list load. Suppressing further mark-read POSTs until radio-web restarts.");
    }

    return true;
  }
```

**4b. `MarkVoicemailReadAsync`** — replace `:142-163` (the flag gate through the non-2xx arm) with:

```csharp
    if (!_configuration.GetValue("RotaryPhone:Gv:MarkReadEnabled", false))
    {
      return null;  // UI-local optimistic flip already applied by the caller
    }
    // GV-6: RotaryPhone has already told us the feature is dark. Behave exactly as the flag-off
    // path above — same null, same kept optimistic flip — and spend no LAN round-trip on a POST
    // whose only possible answer is another 409.
    if (_markReadDark.IsLatched)
    {
      return null;
    }
    try
    {
      // PostAsJsonAsync defaults to JsonSerializerDefaults.Web (camelCase), so the
      // anonymous property serializes to {"isRead":...} per the ADR-024 §3 contract.
      var response = await _httpClient.PostAsJsonAsync(
        $"/api/gvbridge/voicemail/{Uri.EscapeDataString(id)}/read",
        new { isRead }, ct);

      if (response.StatusCode == HttpStatusCode.NotFound)
      {
        return null;   // item gone
      }
      if (!response.IsSuccessStatusCode)
      {
        var errorCode = await ReadErrorCodeAsync(response, ct);
        if (HandledAsMarkReadDark(response.StatusCode, errorCode))
        {
          return null;
        }
        // 502 = GV unreachable. Keep the optimistic flip; reconcile later. No retry.
        // The error code is logged too (GV-6): a 409 that is NOT markread_disabled reaches here,
        // and the status alone would not say which 409 it was.
        _logger.LogError("Mark-read voicemail {Id} failed: {Status} {ErrorCode}",
          id, (int)response.StatusCode, errorCode ?? "-");
        return null;
      }
      return await response.Content.ReadFromJsonAsync<VoicemailItemDto>(JsonOptions, ct);
    }
```

The `catch (Exception ex)` arm at `:166-170` is **unchanged**.

**4c. `MarkSmsThreadReadAsync`** — the same edit at `:322-341`:

```csharp
    if (!_configuration.GetValue("RotaryPhone:Gv:MarkReadEnabled", false))
    {
      return null;
    }
    // GV-6: same latch as the voicemail route — one server flag gates both (ADR-024 §3.3).
    if (_markReadDark.IsLatched)
    {
      return null;
    }
    try
    {
      // camelCase via PostAsJsonAsync default → {"isRead":...} (ADR-024 §3 contract).
      var response = await _httpClient.PostAsJsonAsync(
        $"/api/gvbridge/sms/threads/{Uri.EscapeDataString(threadId)}/read",
        new { isRead }, ct);

      if (response.StatusCode == HttpStatusCode.NotFound)
      {
        return null;
      }
      if (!response.IsSuccessStatusCode)
      {
        var errorCode = await ReadErrorCodeAsync(response, ct);
        if (HandledAsMarkReadDark(response.StatusCode, errorCode))
        {
          return null;
        }
        _logger.LogError("Mark-read thread {ThreadId} failed: {Status} {ErrorCode}",
          threadId, (int)response.StatusCode, errorCode ?? "-");
        return null;
      }
      return await response.Content.ReadFromJsonAsync<SmsThreadDto>(JsonOptions, ct);
    }
```

**4d. Doc-comment repairs on the two methods.** Both currently say `502/non-2xx → null` with no
mention of `409`. `CLAUDE.md` § *Pre-Merge Review* makes a stale doc-comment a review finding, so
fix them in the same diff.

On `MarkVoicemailReadAsync`, replace the sentence at `:134-135` (*"200 → DTO; 404 → null (item
gone); 502/non-2xx → null but the caller KEEPS the optimistic flip…"*) with:

```csharp
  /// 200 → DTO; 404 → null (item gone); 409 markread_disabled → null, latched: their feature is
  /// dark, so this and MarkSmsThreadReadAsync stop POSTing until radio-web restarts (GV-6,
  /// ADR-024 §3.3); any other non-2xx → null but the caller KEEPS the optimistic flip and
  /// reconciles on the next list/poll/push.
```

On `MarkSmsThreadReadAsync`, `:316-317` currently reads *"Same posture as MarkVoicemailReadAsync:
flag-gated, 200→DTO, 404→null, 502/non-2xx→null (keep optimistic flip), no auto-retry."* Extend it:

```csharp
  /// grain → hasUnread=false. Same posture as MarkVoicemailReadAsync: flag-gated, 200→DTO,
  /// 404→null, 502/non-2xx→null (keep optimistic flip), no auto-retry — and it SHARES that
  /// method's dark-feature latch, because one server flag gates both routes (GV-6).
```

### Task 5 — tests

**5a. Repair the two test factories (`C-143`, `C-141`).**
`tests/Radio.Web.Tests/Services/GvBridgeApiServiceVoicemailSmsTests.cs:16-29`:

```csharp
  private static GvBridgeApiService CreateService(HttpClient client) =>
    new(client, NullLogger<GvBridgeApiService>.Instance,
      new ConfigurationBuilder().Build(), new GvMarkReadDarkLatch());

  // GV-4: mark-read routes are gated on RotaryPhone:Gv:MarkReadEnabled; this builds
  // a service with that flag set so the flag-on/flag-off paths are both exercised.
  // GV-6: `latch` and `logger` default to FRESH instances so every existing case stays isolated
  // — a shared default would let one test's latch decide another test's outcome and make the
  // class order-dependent. Pass them explicitly only when the case is about sharing or logging.
  private static GvBridgeApiService BuildSvc(MockHttpHandler handler, bool markReadEnabled,
    GvMarkReadDarkLatch? latch = null, ILogger<GvBridgeApiService>? logger = null)
  {
    var client = new HttpClient(handler) { BaseAddress = new Uri("http://radio:5004") };
    var config = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
        { ["RotaryPhone:Gv:MarkReadEnabled"] = markReadEnabled.ToString() })
      .Build();
    return new GvBridgeApiService(client, logger ?? NullLogger<GvBridgeApiService>.Instance,
      config, latch ?? new GvMarkReadDarkLatch());
  }
```

Add `using Microsoft.Extensions.Logging;` to that file's usings (it currently imports only
`Microsoft.Extensions.Logging.Abstractions`).

**5b. A shared capturing logger.** New file
`tests/Radio.Web.Tests/TestHelpers/CapturingLogger.cs`:

```csharp
using Microsoft.Extensions.Logging;

namespace Radio.Web.Tests.TestHelpers;

/// <summary>
/// Records every entry as (level, formatted message) so a test can assert what was logged and
/// how many times. Lives in TestHelpers for the same reason <see cref="MockHttpHandler"/> does:
/// this assembly keeps one copy rather than a private nested one per test class.
/// <para>
/// <c>PhonePiiLogSafetyTests</c> keeps its own private copy on purpose — it also records
/// <c>exception?.ToString()</c>, which is load-bearing for that row and irrelevant here.
/// Consolidating the two is not GV-6's business.
/// </para>
/// </summary>
public sealed class CapturingLogger<T>(List<(LogLevel Level, string Message)> sink) : ILogger<T>
{
  public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

  public bool IsEnabled(LogLevel logLevel) => true;

  public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
    Exception? exception, Func<TState, Exception?, string> formatter) =>
    sink.Add((logLevel, formatter(state, exception)));
}
```

**5c. Latch unit tests.** New file `tests/Radio.Web.Tests/Services/GvMarkReadDarkLatchTests.cs`:

```csharp
using Radio.Web.Services.ApiClients;

namespace Radio.Web.Tests.Services;

public class GvMarkReadDarkLatchTests
{
  [Fact]
  public void StartsUnlatched()
  {
    Assert.False(new GvMarkReadDarkLatch().IsLatched);
  }

  [Fact]
  public void TryLatch_ReturnsTrueOnce_ThenFalse_AndStaysLatched()
  {
    var latch = new GvMarkReadDarkLatch();

    Assert.True(latch.TryLatch());
    Assert.False(latch.TryLatch());
    Assert.False(latch.TryLatch());
    Assert.True(latch.IsLatched);
  }

  // ⚠ This is NOT a timing test and cannot be weakened by a slow or saturated runner
  // (CLAUDE.md § Test Timing). There is no clock and no sleep: Interlocked.Exchange guarantees
  // exactly one caller observes the 0→1 transition under EVERY interleaving, so starvation can
  // only reorder the winners, never produce two of them. It exists because the property the row
  // asks for — "log once" — is a concurrency claim, and a check-then-set implementation would
  // pass every test above while failing this one.
  [Fact]
  public void TryLatch_GrantsExactlyOneWinner_UnderParallelCallers()
  {
    var latch = new GvMarkReadDarkLatch();
    var winners = 0;

    Parallel.For(0, 256, _ =>
    {
      if (latch.TryLatch())
      {
        Interlocked.Increment(ref winners);
      }
    });

    Assert.Equal(1, winners);
  }
}
```

**5d. Service-level tests.** Append to `GvBridgeApiServiceVoicemailSmsTests.cs`:

```csharp
  // ── GV-6: a dark mark-read feature is not a failure ──────────────

  private const string DarkBody = """{"error":"markread_disabled"}""";

  [Fact]
  public async Task MarkVoicemailReadAsync_Dark409_ReturnsNull_AndSuppressesTheSecondPost()
  {
    var handler = new MockHttpHandler(DarkBody, HttpStatusCode.Conflict);
    var latch = new GvMarkReadDarkLatch();
    var svc = BuildSvc(handler, markReadEnabled: true, latch);

    Assert.Null(await svc.MarkVoicemailReadAsync("vm1"));
    Assert.Null(await svc.MarkVoicemailReadAsync("vm2"));

    Assert.True(latch.IsLatched);
    Assert.Equal(1, handler.RequestCount);   // the second call never reached the network
  }

  [Fact]
  public async Task MarkSmsThreadReadAsync_SharesTheLatch_WithVoicemail()
  {
    // One server flag gates both routes (ADR-024 §3.3), so a 409 on either must silence both.
    var handler = new MockHttpHandler(DarkBody, HttpStatusCode.Conflict);
    var latch = new GvMarkReadDarkLatch();
    var svc = BuildSvc(handler, markReadEnabled: true, latch);

    Assert.Null(await svc.MarkVoicemailReadAsync("vm1"));   // latches
    Assert.Null(await svc.MarkSmsThreadReadAsync("t1"));    // must not POST

    Assert.Equal(1, handler.RequestCount);
  }

  [Fact]
  public async Task Dark409_LogsExactlyOnce_AtWarning_AcrossBothMethods()
  {
    var entries = new List<(LogLevel Level, string Message)>();
    var handler = new MockHttpHandler(DarkBody, HttpStatusCode.Conflict);
    var latch = new GvMarkReadDarkLatch();
    var svc = BuildSvc(handler, markReadEnabled: true, latch,
      new CapturingLogger<GvBridgeApiService>(entries));

    await svc.MarkVoicemailReadAsync("vm1");
    await svc.MarkVoicemailReadAsync("vm2");
    await svc.MarkSmsThreadReadAsync("t1");

    // The documented grep anchor — if this substring changes, INTEGRATIONS.md's probe breaks.
    var dark = entries.Where(e => e.Message.Contains("GV mark-read is dark",
      StringComparison.Ordinal)).ToList();
    Assert.Single(dark);
    Assert.Equal(LogLevel.Warning, dark[0].Level);
    // No per-call Error noise once the feature is known to be dark.
    Assert.DoesNotContain(entries, e => e.Level == LogLevel.Error);
  }

  [Fact]
  public async Task Conflict_WithADifferentErrorCode_IsAGenuineFailure_AndDoesNotLatch()
  {
    // ADR-024 §3.3 defines ONE meaning for 409 on these routes. Anything else is unknown, and an
    // unknown failure must not silence a feature the operator asked for.
    var entries = new List<(LogLevel Level, string Message)>();
    var handler = new MockHttpHandler("""{"error":"something_else"}""", HttpStatusCode.Conflict);
    var latch = new GvMarkReadDarkLatch();
    var svc = BuildSvc(handler, markReadEnabled: true, latch,
      new CapturingLogger<GvBridgeApiService>(entries));

    Assert.Null(await svc.MarkVoicemailReadAsync("vm1"));
    Assert.Null(await svc.MarkVoicemailReadAsync("vm2"));

    Assert.False(latch.IsLatched);
    Assert.Equal(2, handler.RequestCount);           // still trying, correctly
    Assert.Equal(2, entries.Count(e => e.Level == LogLevel.Error));
    Assert.Contains(entries, e => e.Message.Contains("something_else", StringComparison.Ordinal));
  }

  [Fact]
  public async Task BadGateway_DoesNotLatch_AndKeepsReportingEachFailure()
  {
    var entries = new List<(LogLevel Level, string Message)>();
    var handler = new MockHttpHandler(statusCode: HttpStatusCode.BadGateway);
    var latch = new GvMarkReadDarkLatch();
    var svc = BuildSvc(handler, markReadEnabled: true, latch,
      new CapturingLogger<GvBridgeApiService>(entries));

    Assert.Null(await svc.MarkSmsThreadReadAsync("t1"));
    Assert.Null(await svc.MarkSmsThreadReadAsync("t2"));

    Assert.False(latch.IsLatched);
    Assert.Equal(2, handler.RequestCount);
    Assert.Equal(2, entries.Count(e => e.Level == LogLevel.Error));
    Assert.DoesNotContain(entries,
      e => e.Message.Contains("GV mark-read is dark", StringComparison.Ordinal));
  }

  [Fact]
  public async Task FlagOff_NeverLatches_EvenIfTheServerWouldReject()
  {
    // The two guards are independent: our flag is the reason we do not call, and their 409 is
    // the reason we stop calling. With ours off, theirs is never observed.
    var handler = new MockHttpHandler(DarkBody, HttpStatusCode.Conflict);
    var latch = new GvMarkReadDarkLatch();
    var svc = BuildSvc(handler, markReadEnabled: false, latch);

    Assert.Null(await svc.MarkVoicemailReadAsync("vm1"));

    Assert.False(latch.IsLatched);
    Assert.Equal(0, handler.RequestCount);
  }
```

Add `using Radio.Web.Tests.TestHelpers;` — already present for `MockHttpHandler` — and
`using Microsoft.Extensions.Logging;` from 5a.

### Task 6 — docs

**6a. `design/INTEGRATIONS.md:690`** — the REST mark-read bullet lists `200`/`404`/`502` and no
`409`. Extend its status list:

> `200`→DTO, `404`→gone (null), **`409 markread_disabled`→null and LATCHED** (their
> `GVBridge:EnableMarkRead` is off — GV-6 logs one Warning and stops POSTing until `radio-web`
> restarts), `502`/other non-2xx→null but the UI keeps its optimistic flip and reconciles on the
> next list/poll/push; **no client-side auto-retry**.

**6b. `design/INTEGRATIONS.md:730`** — append to the *"Two-flag distinction"* bullet, because this
is where an operator looking for the skew will land:

> **GV-6: the skew is now self-announcing, and un-latching needs a restart.** If ours is on while
> theirs is off, the first mark-read logs one Warning containing **`GV mark-read is dark`** and
> then suppresses every further mark-read POST for the life of the process. Probe with
> `ssh mmack@radio "journalctl -u radio-web --since '-2h' --no-pager | grep 'GV mark-read is dark'"` —
> **bounded, never tailed**, and **not** `journalctl -p warning`: `radio-web.service` sets no
> `SyslogLevelPrefix`, so every line it writes is journald priority `info` regardless of Serilog
> level. **After RotaryPhone enables their flag, `sudo systemctl restart radio-web`** — nothing
> in-process can clear the latch, because the status payload carries no mark-read capability field.

**6c. `design/decisions/2026-06-20-gv-mark-read-durable-readstate.md`** — two edits in §3.3.
The table cell at `:105` currently reads *"Currently indistinguishable from `502` — see the
consequence note below."* Replace with:

> Recognised and latched (GV-6): one Warning, then mark-read is suppressed until `radio-web`
> restarts. Optimistic flip kept; no user-visible change.

And close the amendment's consequence note at `:112` by appending to that paragraph:

> **✅ Closed by GV-6.** The client now branches on `409` + `markread_disabled` and says so once.
> The rollout order below is still the right one — it avoids the skew rather than diagnosing it.

**6d. ⚠ `CLAUDE.md` § *Deployment* — an adjacent correction, to be NAMED in the PR body.** The
section already records that `Radio.Web`'s Console sink is unrestricted. It does **not** record the
consequence that makes that fact actionable, and this row's probe depends on it. Append one
sentence to the `radio-web` warning paragraph:

> `radio-web.service` also sets no `SyslogLevelPrefix`/`SyslogLevel` (unlike `radio-api.service`,
> which sets both and pairs them with `SystemdConsoleFormatter`), so **every `radio-web` journal
> line is priority `info` whatever its Serilog level** — `journalctl -p warning -u radio-web`
> returns nothing. Grep for a substring instead.

⚠ **Say in the PR body that 6d is an unrelated documentation correction**, with the two file
citations. A silent edit to `CLAUDE.md` inside a small diagnostic row reads as scope creep; a named
one reads as a Builder who checked the probe before writing it down.

**Not needed:** `design/FUTURE-WORK.md` (nothing is stubbed or deferred to a platform API here) and
`design/DECISION-LOG.md` (no decision is being made that ADR-024 §3.3 did not already make; this row
implements the handling that section prescribes).

---

## 3. Ordering

Tasks 1 → 2 → 3 → 5a in one pass: Task 3 breaks the build until the test factories are repaired, so
do not stop between them. Then Task 4 (the behaviour), then 5b–5d (the tests), then Task 6.

Task 4b and 4c are independent of each other and both depend on 4a.

---

## 4. Test plan

### 4.1 `T1` — the latch itself

`tests/Radio.Web.Tests/Services/GvMarkReadDarkLatchTests.cs`, Task 5c. Three cases: starts
unlatched; `TryLatch` grants exactly one winner then stays latched; and one parallel case proving
the `Interlocked` property under 256 concurrent callers. That last case is the reason the type
exists as a type — a `bool` implementation passes the first two and fails the third.

### 4.2 `T2` — the service, driven through `MockHttpHandler`

Task 5d, six cases, all in the existing mark-read test file so they sit beside the `404` / `502` /
flag-off cases they extend:

| Case | Asserts |
|---|---|
| `Dark409_ReturnsNull_AndSuppressesTheSecondPost` | `null`; `RequestCount == 1` after two calls |
| `SharesTheLatch_WithVoicemail` | a voicemail `409` silences the SMS route (`C-137`) |
| `LogsExactlyOnce_AtWarning_AcrossBothMethods` | one `Warning` carrying the grep anchor; **zero** `Error` |
| `Conflict_WithADifferentErrorCode_…DoesNotLatch` | not latched; `RequestCount == 2`; two `Error`s carrying the code (`C-138`) |
| `BadGateway_DoesNotLatch_…` | not latched; two `Error`s; no dark line |
| `FlagOff_NeverLatches_…` | `RequestCount == 0`; not latched — the two guards are independent |

`MockHttpHandler` already exposes `RequestCount` for exactly this
(`tests/Radio.Web.Tests/TestHelpers/MockHttpHandler.cs:16-18`), and returns a fixed body + status
for every request, which is all these cases need.

### 4.3 `T3` — what is deliberately NOT tested

No component test. `C-142` says the UI does not change, and a bUnit test asserting that a component
still does what it already did would be a test of the plan's promise rather than of the code.
The behavioural guarantee that matters — the suppressed path returns `null`, identical to the
flag-off path — is asserted directly at the service level in every case above.

### 4.4 Gates

```bash
dotnet build RadioConsole.sln -c Release          # 0 warnings; Release treats them as errors
dotnet test RadioConsole.sln -c Release > /tmp/test.log 2>&1; echo "exit=$?"
grep -E "Passed!|Failed!|error" /tmp/test.log
```

⚠ **Never pipe `dotnet test` into `tail`** (`CLAUDE.md` § Build & Test) — the pipeline reports
`tail`'s exit code, not the suite's. Read the **per-project summary lines**, one per test project.

Known-failing on Windows and **not** a regression: four `SrcVariableResamplerTests`
(`libsamplerate.so.0`, `TEST-5`) and `NwsObservationIntegrationTests.RealNwsCall_*` (live network,
`Category=Integration`, CI-excluded).

Also expect `LogSafetyLintTests` to stay green: the new Warning takes no arguments and its message
contains none of the forbidden identifiers (`LogSafetyLintTests.cs:143-181`; `C-145`).

### 4.5 UAT

**None is possible, and that is a statement about the feature, not a gap in the plan.** The dark
path requires `RotaryPhone:Gv:MarkReadEnabled=true` **and** RotaryPhone's `GVBridge:EnableMarkRead=false`
simultaneously — a state ADR-024's rollout order exists to prevent. Manufacturing it means editing
RotaryPhone's config on the shared box, which is a cross-service change this row has no mandate for.
Per the auto-merge policy, the unit suite plus the code review stands in for UAT here: nothing
user-facing changes (`C-142`), and the whole deliverable is a log line.

### 4.6 The one on-box check, for a human, and what it answers

Not a gate. It answers `C-144` — *is the dark path even reachable on `radio` today?* — which this
checkout cannot answer, because the box's `appsettings.Production.json` web overlay carries an
operator-authored `MarkReadEnabled` that is not in the repo (`Deploy-ToLinux.ps1:292-302`):

```bash
ssh mmack@radio "grep -n MarkReadEnabled /opt/radio-console/web/appsettings.Production.json"
```

One read-only command, no journald load. **If it prints `true`**, the skew is live today and the new
Warning should appear in the journal within one voicemail play or thread open after the deploy —
check with the bounded grep in 6b. **If it prints nothing or `false`**, our flag is off, the guard at
`:142`/`:322` short-circuits before any POST, and the dark path is unreachable until the rollout in
ADR-024 §3.3 happens; the fix is correct-by-construction and waits.

---

## 5. Docs and queue

Per PR: Task 6 covers `design/INTEGRATIONS.md` (×2), `design/decisions/2026-06-20-gv-mark-read-durable-readstate.md`
(×2), and the named `CLAUDE.md` correction (6d). The queue row moves 📋 → ✅ with the plan link;
wording is in §8 — ⛔ **the Planner did not edit `docs/BUILDER_QUEUE.md`**, a Builder was writing to
it concurrently.

**PR body must contain:**
- A **Docs Impact** section naming the five doc edits, with 6d flagged as the unrelated one.
- The `C-132` correction, stated plainly: the status code was already in the log; what this adds is
  the error code, the meaning, and the once-ness.
- The `C-134` honesty note: this is not an audio-distortion fix.

---

## 6. Deliberately not done

### 6.1 The undisposed `HttpResponseMessage` at `:150` and `:329`

Both mark methods do `var response = await _httpClient.PostAsJsonAsync(...)` with no `using`, unlike
`GetSmsThreadMessagesAsync` at `:214` which does `using var response`. It is a real (small) leak and
the fix is one keyword on each. **Not done here**: it is a pre-existing defect on lines this row
happens to sit near, and folding it in makes a two-concern diff out of a one-concern row. If a
Builder does it anyway, **say so in the PR body** — the repo's rule is that an unrelated fix is fine
when it is named and invisible when it is not.

### 6.2 Consolidating the four `CapturingLogger<T>` copies

`Radio.API.Tests/TestSupport/CapturingLoggerProvider.cs:37`,
`Radio.Web.Tests/Services/AttendedPlaybackCircuitHandlerTests.cs:218`,
`Radio.Web.Tests/Services/PhonePiiLogSafetyTests.cs:207`,
`Radio.Infrastructure.Tests/.../ListLogger`. This plan adds a fifth, in `TestHelpers/`, and edits
none of the others. `PHN-5`'s own remarks argue the cross-assembly duplication is correct; the
within-assembly one is arguable, but arguing it is not this row's job.

### 6.3 Skipping the mark-read POST for an already-read thread

`MarkOpenThreadReadAsync` is called unconditionally on every open and every retry (`C-133`), so
re-opening a read thread spends a POST to tell Google something it already knows. ADR-024 §3.3
guarantees that is a `200` idempotent no-op, so it is waste, not a bug — and removing it changes
behaviour on the **healthy** path, which a diagnostic row should not do. Worth its own row if
anyone wants the round-trips back.

### 6.4 Surfacing the latch on a diagnostics endpoint

`DiagnosticsApiService` exists and could expose `IsLatched`. Skipped: the log line is the
deliverable the row asked for, and an endpoint nobody polls is a second thing to keep true.

### 6.5 Clearing the latch from the status poll

Would need RotaryPhone to add a mark-read capability field to `/api/gvbridge/status` — a cross-repo
contract change (`C-136`). A restart is the documented remedy and the rollout order makes the state
rare. If the skew ever happens twice, this becomes worth a handoff.

---

## 7. Self-review

### 7.1 Verified first-hand at `35e4ed5a`

- Both mark methods, their flag gates, their `404` and non-2xx arms, and the exact `LogError` text
  (`GvBridgeApiService.cs:139-171`, `:319-349`).
- `GvResult`'s doc-comment naming `GV-6` and prescribing the `409 + markread_disabled` triple
  (`GvResult.cs:36-43`), and `ReadErrorCodeAsync`'s comment doing the same (`:281`).
- `AddHttpClient<GvBridgeApiService>` → transient (`Program.cs:352-363`); `PhonePage.razor:5` injects
  it.
- The two callers and their repetition shape (`PhonePage.razor:629`, `:657`, `:673`, `:749`;
  `VoicemailPlayer.razor:144`, `:469-480`; `VoicemailRow.razor:29-32`).
- `GvBridgeStatusService` contains no mark-read reference; `GvBridgeStatusDto` has no capability
  field (`ApiModels.cs:1100-1109`).
- The full sink derivation in §0.3, from `Radio.Web/appsettings.json:43-71`,
  `Radio.API/Program.cs:40-56`, `deploy/common/radio-api.service:85-97` and
  `deploy/common/radio-web.service` (whose logging block is `SyslogIdentifier` alone).
- The row's four line anchors, all exact (§0.4).
- `MockHttpHandler`'s `RequestCount`; the two test factories; `LogSafetyLintTests`' forbidden list.

### 7.2 Not verified, and what it costs

- **RotaryPhone's actual `409` body.** Taken from ADR-024 §3.3's live-verified amendment
  (2026-07-31), not observed by this session — `D:/prj/RotaryPhone` was not read. If they emit
  `{"code":"markread_disabled"}` instead of `{"error":...}`, `ReadErrorCodeAsync` already tries
  both spellings and both casings (`:298`), so the plan survives; if the string itself differs, one
  constant changes and every test moves with it.
- **The live flag state on `radio`** — `C-144`, unresolvable from this tree. §4.6 has the command.
- **That `Parallel.For(0, 256, …)` is cheap enough not to annoy the suite.** It is loop-bound, not
  clock-bound, so its cost is microseconds; if it ever looks slow, lower the count — the property
  does not depend on it.

### 7.3 What would falsify this plan's central decision

The decision is *"one process-lifetime singleton latch shared by both routes."* It is wrong if
RotaryPhone ever gates the two routes on separate flags (`C-137`), or if they add a second meaning
for `409` on these routes (`C-138` mitigates: an unrecognised `409` does not latch). It is
*insufficient*, but not wrong, if the skew turns out to be common enough that operators are
restarting `radio-web` to clear it — that is the trigger for §6.5, not for reopening this.

---

## 8. Queue row wording

⛔ The Planner did **not** edit `docs/BUILDER_QUEUE.md` — a Builder was writing to it concurrently.
Apply by hand.

**Plan column** (currently `_plan TBD (small)_`) →

```
[`design/plans/GV-6-distinguish-markread-disabled-from-a-real-failure.md`](../design/plans/GV-6-distinguish-markread-disabled-from-a-real-failure.md)
```

**Item column** — append to the existing text, leaving the `D31` assessment and the ⛔ warning
intact:

> _**Planned 2026-09-06 against `35e4ed5a`. Estimate 0.5 d.** Shape: an `AddSingleton`
> `GvMarkReadDarkLatch` (the typed client is **transient**, so a field on the service could never
> latch), one `Interlocked` one-shot **`LogWarning`** in `Radio.Web` carrying the grep anchor
> **`GV mark-read is dark`**, and both mark methods short-circuiting afterwards exactly as they do
> when our own flag is off. **No UI change, no caller change** (ADR-024 §6). Three of the plan's
> findings change how the row should be read: (1) **the status code is ALREADY logged** at `:161`/
> `:339`, so `409` vs `502` was never wholly indistinguishable — what is missing is the error code,
> the meaning and the once-ness; (2) **in `Radio.Web` the log LEVEL does not decide journald
> exposure** — the Console sink is unrestricted, so Warning, Error and Information all land there,
> and `journalctl -p warning -u radio-web` finds nothing at all because the unit sets no
> `SyslogLevelPrefix`; the **latch**, not the level, is what removes volume; (3) the repetition is
> **per user action, not per poll** — one POST per accordion re-expand and one per thread open
> *and* per retry, unconditionally — so the ceiling is human tapping speed and this is **not** an
> audio-distortion fix. **UAT is not possible** (the skew state is what ADR-024's rollout order
> exists to prevent); unit suite + review stand in. One read-only box command in §4.6 answers
> whether the path is even reachable on `radio` today._

**Status column** on completion: 📋 → ✅ with the PR number, in the shape the file's other completed
rows use.

**Branch** — unchanged: `fix/gv-markread-dark-409`.

**Depends on** — unchanged: `GV-4` (✅ merged, #441).

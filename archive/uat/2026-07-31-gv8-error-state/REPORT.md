# UAT — PR #461 / GV-8: distinguish a failed conversation load from an empty one

**Date:** 2026-07-31 evening EDT (timestamps below in **UTC** = 2026-08-01)
**Branch:** `fix/gv-texts-load-error-state` @ `4b55dbc12719126f6dbbcf72a175e6ceba92ee1c`
**Plan:** `docs/superpowers/plans/2026-07-31-gv-texts-load-error-state.md` § Test Plan C1–C9
**Predecessor:** `../2026-07-31-gv-live-data/REPORT.md` — F-1 (HIGH), F-2 (MEDIUM)
**Surface:** `http://192.168.86.50:5002/phone` → Texts, viewport **1920×720**

## Verdict

**PR #461 closes F-1, and closes the state half of F-2. Recommend merge.**

All nine plan cases were executed against live data on the real box — none inferred, none simulated.
During a confirmed **HTTP 502**, the conversation pane now renders `cloud_off` + "Couldn't load
messages." + a working `Retry`, where it previously rendered "Start the conversation below." The
opposite failure mode was checked too: a genuinely empty group thread still reads as **empty**.

| | Result |
|---|---|
| Passed | **10 / 10** (C1–C9 + added C2b) |
| Failed | 0 |
| HIGH / MEDIUM | **0 / 0** |
| LOW | 2 (`L-1`, `L-2`) |
| Observations (not this PR) | 2 (`O-1`, `O-2`) |
| Could not observe | 1 (M-1 — **not** a pass) |

## Box state after this UAT

| Component | State |
|---|---|
| Radio.API | `4b55dbc`, built `2026-08-01T00:58:41Z`, restarted `20:58:57 EDT` |
| Radio.Web | branch binary, mtime `2026-07-31 20:58:47.425 EDT`, restarted `20:59:04 EDT` |
| rotary-phone | **untouched** — `ActiveEnterTimestamp` `2026-07-31 10:43:32 EDT` throughout |
| Rollback point | `d2321a46343995f89fb32ddfa52e8e2812263192` |

```powershell
git checkout d2321a4
& 'D:\prj\RTest\RTest\deploy\Deploy-ToLinux.ps1' -TargetHost 192.168.86.50 -Runtime linux-x64
```

Post-UAT: `radio-api`, `radio-web`, `rotary-phone` all `active`; `:5002/phone` → HTTP 200.

## Deploy verification (OPS-1 gap closed out-of-band)

`Radio.Web` has no version endpoint, so exit code 0 proves nothing. Verified three ways:

1. **API** — `GET :5000/api/health/version` → `gitShaShort: 4b55dbc`, `buildTimestampUtc: 2026-08-01T00:58:41Z`.
2. **mtime + restart** — web binary `08:11:09` → **`20:58:47 EDT`**; `radio-web` ActiveEnter `08:12:43` →
   **`20:59:04 EDT`**; `wwwroot/css/design-system.css` also refreshed to `20:58:47 EDT`.
3. **Branch-only symbols inside the deployed binary** — decisive:

```console
$ grep -ac OpenThreadLoading    /opt/radio-console/web/Radio.Web   → 1
$ grep -ac RetryOpenThreadAsync /opt/radio-console/web/Radio.Web   → 2
$ grep -ac GvCallOutcome        /opt/radio-console/web/Radio.Web   → 1
```

None exists on `d2321a4`. The web bits under test are positively the branch bits.

> **Recommendation for OPS-1:** adopt this `grep -ac <branch-only-symbol>` check as the interim
> web-freshness gate until `Radio.Web` gets a real version endpoint.

## The blackout clock

Confirmed against the source of truth:

```
Jul 31 20:40:02 EDT  CDP cookie refresh: 20 cookies extracted and activated
Jul 31 21:00:02 EDT  CDP cookie refresh: 20 cookies extracted and activated   ← exactly 20 min
Jul 31 21:20:02 EDT  (next boundary — used for C6)
```

Healthy `age < ~660`; blackout `~660–1200`; refresh at `~1200`. Every observation carries wall clock
**and** `psidtsAgeSeconds`. `available`/`degraded`/`cookiesValid` were confirmed liars again — see `O-1`.

## Results

| Case | Result | Wall clock (UTC) | `psidtsAgeSeconds` | Phase |
|---|---|---|---|---|
| **C1** Baseline open | **PASS** | `01:06:17` → `01:06:21` | 375 → 379 | HEALTHY |
| **C2** Skeleton shimmers | **PASS** (see `L-1`) | `01:04:48` | 287 | HEALTHY |
| **C2b** No stale bubbles cross-thread | **PASS** | `01:28:19` | 497 | HEALTHY |
| **C3** Note unread thread | **PASS** | `01:04:00` | 238 | HEALTHY |
| **C4** Error state on real failure | **PASS** | `01:11:48.723` | **707** | **BLACKOUT** |
| **C5** Unread survives failed open | **PASS** | `01:11:48` → `01:11:54` | 707 → 713 | BLACKOUT |
| **C6** Retry recovers | **PASS** | `01:20:05.089` | **3** | HEALTHY |
| **C7** Group thread reads EMPTY | **PASS** | `01:05:54.8` → `01:05:58.9` | 353 → 357 | HEALTHY |
| **C8** Blackout reproduction | **PASS** (executed *as* C4) | `01:11:48.723` | 707 | BLACKOUT |
| **C9** Server-side probe | **PASS** | `01:11:48` | 707 | BLACKOUT |

### C1 — Baseline open, healthy window · PASS

`01:06:17Z`, age **375**. Opened feed row 2 = `t.51789` (non-group, real preview).
Observed **1 bubble**, 1 day separator (`DEC 17, 2025`), body `19199010112 deleted from Microsoft
account je**a@ma**.com. Not you? aka.ms/alca`. `emptyState: null` — no `cloud_off`, no
"Couldn't load messages.", no "Start the conversation below." Viewport asserted in-page: `1920×720`.

Screenshot: `screenshots/01-c1-baseline-thread-51789.png`

### C2 — The skeleton visibly shimmers · PASS (with `L-1`)

`01:04:48Z`, age **287**. The case the plan singled out, because the branch was inert before this PR
and a class-name unit assertion passed on it anyway. A screenshot of one frame cannot prove
animation, so three independent kinds of evidence were collected.

**1 — The markup is the new composition** (branch-only; `d2321a4` emitted a bare `.skeleton`):

```html
<div class="skeleton-list-row">
  <div class="skeleton-loading skeleton-feed-chip"></div>
  <div class="skeleton-list-row-text">
    <div class="skeleton-loading skeleton-text" style="width: 42%"></div>
    <div class="skeleton-loading skeleton-text" style="width: 26%"></div>
  </div>
</div>
```

5 skeleton rows × 3 shimmer nodes = **15 `.skeleton-loading` elements**.

**2 — The animation clock advances.** A `MutationObserver` armed *before* the click sampled the live
node the instant it appeared:

| Sample | `animationName` | `animationPlayState` | `Animation.currentTime` | `backgroundPosition` |
|---|---|---|---|---|
| t+0 ms | `shimmer` | `running` | **0 ms** | `-200% 0px` |
| t+120 ms | `shimmer` | `running` | **117 ms** | `-174.531% 0px` |

`prefers-reduced-motion: reduce` was **false**, so the `animation: none` override at
`design-system.css:1683` was not in play.

**3 — Rendered pixels moved.** A CDP screencast captured 17 real frames; diffing the skeleton region
between two frames 116 ms apart, against a static control region in the same frame pair:

| Region | Frames | Pixels changed | Max channel delta |
|---|---|---|---|
| Skeleton pane `(1420,320)-(1910,560)` | `+108ms` vs `+224ms` | **14.5 %** | 3 |
| Control — static thread list `(300,430)-(1300,560)` | same pair | **0 %** | 0 |

The control proves the 14.5 % is the shimmer, not compression noise or a global repaint.

Screenshots: `screenshots/02-c2-skeleton-visible.png`, `screenshots/03-c2-frame-a-108ms.png`,
`screenshots/04-c2-frame-b-224ms.png`

### C2b — No stale bubbles while a different thread loads · PASS

Added because the plan names this, not the skeleton's brevity, as the real C2 failure:
*"What IS a failure: seeing the previous thread's bubbles while a different thread is loading."*

`01:28:19Z`, age **497**. Thread A = `t.+19193718044` (**15 bubbles, 11 day separators**) fully
loaded, then Back, then thread B = `t.+13362039432`. At the instant B's skeleton appeared:

```json
{ "skeletonRows": 5, "shimmerNodes": 15,
  "bubblesStillShown": 0, "daySeps": 0,
  "headerName": "+13362039432", "text": "" }
```

Zero stale bubbles; the header had already switched. This confirms `LoadOpenThreadMessagesAsync`
nulls `_openThreadMessages` *before* awaiting, which is also what makes the skeleton branch reachable.

Screenshot: `screenshots/12-c2b-cross-thread-no-stale-bubbles.png`

### C3 — Unread threads noted · PASS

`01:04:00Z`, age **238**. Two rows carried `.unread-dot`; `TEXTS` badge read **2**:

| Feed row | Thread id | Display |
|---|---|---|
| 0 | `t.+18019208129` | "Don't worry about the sealing for Andrews…" |
| **1** | **`t.+19193718044`** | **Mark Mackelprang — "teest"** ← carried into C5 |

Row 1 was not opened until C4/C5, per the plan.

### C4 / C8 — The error state on a real 502 · PASS — this is the F-1 fix

Executed during the **natural blackout**, not by stopping `rotary-phone` (owner declined). So C4 and
C8 are one observation, and it is a genuine upstream failure rather than a synthesised one.

**`01:11:48.723Z`, `psidtsAgeSeconds` = 707 → BLACKOUT.** Opened row 1 (`t.+19193718044`):

```
cloud_off
Couldn't load messages.
[ Retry ]
```

DOM: `emptyState.icons = ["cloud_off"]`, `emptyState.text = "Couldn't load messages."`,
`emptyState.buttons = ["Retry"]`, `bubbles = 0`, `skeletonRows = 0`. Header still correctly
identified the thread: `Mark Mackelprang / +19193718044`.

**"Start the conversation below." did not appear.** That string *was* F-1; its absence here under a
confirmed 502 is the whole point of the PR.

Screenshots: `screenshots/06-c4-list-before-failed-open.png`, `screenshots/07-c4-error-state-blackout.png`

### C5 — The unread marker survives a failed open · PASS (Task 6 shipped)

Same blackout, `01:11:48Z` → `01:11:54Z` (age 707 → 713):

| | `.unread-dot` on row 1 | `TEXTS` badge |
|---|---|---|
| Before the failed open | `true` | 2 |
| After the failed open + Back | **`true`** | **2** |

We did not mark read a conversation the user was never shown. Visible in
`screenshots/07-c4-error-state-blackout.png` — the dot is still on the "Mark Mackelprang" row
*behind* the error pane.

Screenshot: `screenshots/08-c5-unread-survives-failed-open.png`

### C6 — Retry recovers · PASS

Run by **holding the error state across a window boundary** rather than restarting the bridge.

- `01:12:28Z` (age ~747, BLACKOUT) — re-opened row 1, error state rendered.
- Sat untouched for **~7.5 minutes / 31 poll samples**. Pane state at the end was byte-identical to
  the start: still `cloud_off` + "Couldn't load messages." + `Retry`. It neither self-healed nor decayed.
- `01:20:05.089Z`, age **3** (refresh fired at `21:20:02 EDT`) — pressed `Retry`.

Result: **15 bubbles, 11 day separators**, full history from `NOV 19, 2013` to `MAR 31`, rendered
**in place**. `emptyState: null`. **No page reload.**

A successful retry also carried the read-marking the failed open had skipped:

| | `.unread-dot` row 1 | `TEXTS` badge | `MESSAGES` badge |
|---|---|---|---|
| After successful Retry | **`false`** | **1** (was 2) | 12 (was 13) |

Screenshots: `screenshots/09-c6-error-before-retry.png`, `screenshots/10-c6-recovered-after-retry.png`,
`screenshots/11-c6-list-after-retry.png`

### C7 — A genuine empty still reads as empty · PASS

The boundary with RotaryPhone's Defect B, where over-reporting errors would itself be a new defect.
`01:05:54.8Z`, age **353**. Opened feed row 11 = `g.Group Message.d5Mri/NrDUQgXNXNQehOfw`, resolved
as **"Mary Carmen Wiser"**.

Rendered: `forum` + **"Start the conversation below."**, `buttons: []` — the **empty** state.
**No `cloud_off`. No `Retry`.** Correct: the server said zero messages.

Corroborated server-side at `01:06:47Z` (age ~405, healthy) with the exact escaping the client uses:

```
t.51789                                       HTTP=200  messages=1
g.Group%20Message.d5Mri%2FNrDUQgXNXNQehOfw    HTTP=200  messages=0
g.Group%20Message.yL8g8JjuyR7Z57d9BxRW%2FQ    HTTP=200  messages=0
```

A real `200` with an empty array — "empty" is the honest rendering. Group threads becoming *readable*
remains RotaryPhone's `%2F` decode bug, explicitly out of scope.

Screenshot: `screenshots/05-c7-group-thread-empty-state.png`

### C9 — Server-side probe · PASS

Blazor Server fetches server-side over SignalR, so the browser sees nothing. Confirmed: across every
run in this session, **0 console errors and 0 failed network requests** — while the underlying 502 was
actively firing. A clean console is not a pass, exactly as the plan warns.

The one piece of monitoring this surface has did fire, in the same second as the click
(`21:11:48 EDT` = `01:11:48 UTC`):

```
[21:11:48 ERR] Radio.Web.Services.ApiClients.GvBridgeApiService:
    Failed to get GV SMS thread t.+19193718044: HTTP 502 Failed to fetch SMS messages from Google
```

Upstream, same second:

```
[21:11:48 WRN] api2thread/list returned Unauthorized for folder Sms
```

Two things worth noting:

1. The documented substring **`Failed to get GV SMS thread`** survived the refactor across all 7 log paths.
2. The line now **carries the outcome** (`HTTP 502` + upstream reason). On `d2321a4` this was a bare
   `HttpRequestException` stack dump. A real diagnosability gain that directly serves the F-1 workflow.

## Findings

### `L-1` · LOW · The shimmer runs, but is close to imperceptible on the dark kiosk theme

Not a regression, and not a blocker — this PR strictly improves the previous state (five static grey
bands). But "visibly shimmer" deserves an honest reading.

Read from the **deployed** stylesheet: `--surface-raised: #141416` → `--surface-overlay: #1A1A1D`,
i.e. `rgb(20,20,22)` → `rgb(26,26,29)` — a **6/255** stop-to-stop delta. Measured peak frame-to-frame
change across the whole moving band was **3/255**. The animation is unambiguously running
(`currentTime` 0 → 117 ms); it is the *amplitude* that is marginal. Side by side the two captured
frames read as identical to the eye.

Worth a Designer opinion on whether the shimmer delta should widen. LOW, not fixed here — it is a
design-token question spanning every skeleton in the app, not a GV-8 bug.

**Repro:** open any text thread on the kiosk and watch the pane during load.
**Evidence:** `screenshots/03-c2-frame-a-108ms.png` vs `screenshots/04-c2-frame-b-224ms.png`.

### `L-2` · LOW · `Deploy-ToLinux.ps1` defaults to `linux-arm64`, but the box is `x86_64`

Ops, not code — flagged because it nearly cost this UAT its validity. The literal command
`deploy/Deploy-ToLinux.ps1 -TargetHost 192.168.86.50` uses the script's default
`-Runtime linux-arm64` (`Deploy-ToLinux.ps1:51`), while `uname -m` on the box is **`x86_64`**.
Correct invocation:

```powershell
& '...\Deploy-ToLinux.ps1' -TargetHost 192.168.86.50 -Runtime linux-x64
```

Suggest defaulting by target, or probing `uname -m` and refusing on mismatch.

## Observations (not defects in this PR)

### `O-1` · The reconnect banner still cannot fire during a blackout — RotaryPhone Defect A re-confirmed

At `01:11:48Z`, with `psidtsAgeSeconds = 707` and the thread fetch returning a hard 502:

```json
{"available": true, "degraded": false, "cookiesValid": true, "psidtsAgeSeconds": 707}
```

The pane's `banner` was `null` — "Google Voice is reconnecting" did **not** appear, in precisely the
window it exists for. Reproduces the F-1 diagnosis' bonus finding exactly; belongs to the cross-repo
cookie-refresh item, not GV-8. **`psidtsAgeSeconds` remains the only trustworthy field.**

### `O-2` · The composer stays mounted behind the error state

In the C4 error state the compose bar is still rendered (`Message` field + `Send`). Consistent with
prior finding **F-3** (`SendEnabled=false` is honoured, so it is inert) and F-2's wording half, both
routed to **GV-7**. No action for this PR; noted so it is not mistaken for new behaviour.

## Could not observe

### M-1 · Inbound message arriving on an open-but-failed thread — **NOT OBSERVED**

Polisher's M-1 fix (`Error && Messages == null`, so content outranks a stale error flag) requires a
**real inbound SMS to land while a failed thread is open**. No inbound traffic arrived during the
session, and inducing it would have meant injecting a synthetic message — explicitly ruled out.

**This is not a pass.** What *is* established: the branch condition is `else if (Error && Messages == null)`
in `PhoneTextsPanel.razor`, ordering is skeleton → error → empty → list, and `PhoneTextsPanelTests`
covers it at the unit level. Live confirmation remains open — it needs someone to text the GV number
during a blackout with the thread open.

## Deviations from the plan

1. **C4 ran via the natural blackout instead of `systemctl stop rotary-phone`.** Owner declined the
   service stop; `rotary-phone`'s `ActiveEnterTimestamp` stayed `10:43:32 EDT` throughout, confirming
   it was never touched. Consequence: C4's failure mode is `HTTP 502` rather than a transport failure
   — arguably the better test, since 502 is the real-world case F-1 was reported against.
   **Transport-failure rendering is therefore unverified live** (covered by unit tests).
2. **C6 ran by holding the error state across a window boundary** rather than restarting the bridge —
   same reason. This added a free bonus assertion: the error state is stable for 7.5 min unattended.
3. **Playwright MCP could not be used; a self-managed browser was substituted.** The MCP is configured
   for the `chrome` channel and resolves it at `/opt/google/chrome/chrome`, absent in this WSL instance;
   installing needs root (no passwordless sudo). Substitute:
   - Chromium **151.0.7922.10** from the Playwright bundle (`npx playwright install chromium`, no root);
   - `libnss3` / `libnspr4` extracted from Ubuntu `.deb`s into a scratch dir via `LD_LIBRARY_PATH`
     (nothing installed system-wide, nothing outside the scratchpad modified);
   - driven over **CDP** from `playwright-core`, which additionally enabled the `MutationObserver`
     animation probe and the frame-level screencast that C2 needed.

   Viewport was asserted **in-page** (`window.innerWidth/innerHeight = 1920×720`) on every step rather
   than assumed. **Fix before the next session** — install Chrome on the WSL box, or point the plugin at
   `--browser chromium`. The plugin config was not changed; that is the user's environment.
4. **Plan erratum confirmed.** § Non-goals 1 cites the group-thread empty-state check as "step C6";
   the Test Plan numbers it **C7**. C7 is correct and is what was executed.

## Artifacts

All at 1920×720 unless noted.

| File | What it shows |
|---|---|
| `screenshots/01-c1-baseline-thread-51789.png` | C1 — healthy open, real bubble |
| `screenshots/02-c2-skeleton-visible.png` | C2 — shimmer skeleton caught mid-load *(1920×577, screencast run)* |
| `screenshots/03-c2-frame-a-108ms.png` | C2 — rendered frame at +108 ms *(1920×577)* |
| `screenshots/04-c2-frame-b-224ms.png` | C2 — rendered frame at +224 ms *(1920×577)* |
| `screenshots/05-c7-group-thread-empty-state.png` | C7 — group thread renders EMPTY, not error |
| `screenshots/06-c4-list-before-failed-open.png` | C4 — thread list, unread dot present |
| `screenshots/07-c4-error-state-blackout.png` | **C4 — the F-1 fix under a live 502** |
| `screenshots/08-c5-unread-survives-failed-open.png` | C5 — unread dot survived |
| `screenshots/09-c6-error-before-retry.png` | C6 — error state held while waiting for the window |
| `screenshots/10-c6-recovered-after-retry.png` | **C6 — conversation recovered in place after Retry** |
| `screenshots/11-c6-list-after-retry.png` | C6 — read state after a successful retry |
| `screenshots/12-c2b-cross-thread-no-stale-bubbles.png` | C2b — thread B loaded, no stale bubbles from A |

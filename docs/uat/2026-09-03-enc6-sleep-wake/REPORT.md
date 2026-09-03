# ENC-6 — sleep, wake, and the three-state model: UAT report

**Date:** 2026-09-03 (00:08–00:12 EDT)
**Box:** `radio` (`radio.lan` → `192.168.86.50`), Intel N100, x86_64, Ubuntu + GNOME 46, Wayland
**Deployed SHA:** **`121a8ca`** — API and Web both verified by `/api/health/version`, and **re-checked
unchanged at the end of the run**, so nothing was deployed underneath the measurements.
**Row:** `ENC-6` (P0) · **PR:** [#539](https://github.com/mmackelprang/RTest/pull/539)
**Encoder:** `cafe:4005` present, `isConnected: true`, `status: Configured`, flash `MatchesCurrentDesign`
**Audio during the run:** SDR Radio (RTL-SDR) actively playing — which is what makes the Ambient
result meaningful rather than vacuous.

---

## Verdict: PASS on everything reachable without hands on the knobs

**5 of 10 scenarios executed and passed. 5 could not be executed at all** — they require physically
turning the knobs, and there is no software path to inject encoder input on this box. That gap is
stated plainly in section 3 rather than papered over; it is the subject of `ENC-17`, filed the same
night.

| # | Scenario | Result |
|---|---|---|
| **A** | **Both `/sleep` entry paths, separately** | ✅ **PASS** — the headline criterion |
| B | Ambient: VOLUME acts in place | ⛔ **NOT RUN** — needs a physical knob |
| C | Ambient: SOURCE / PRESETS / TUNING wake and are consumed | ⛔ **NOT RUN** — needs a physical knob |
| D | Standby: D22 (turn does not resume, press does) | ⛔ **NOT RUN** — needs a physical knob |
| E | The wake latch — count the detents | ⛔ **NOT RUN** — needs a physical knob |
| **F** | **The Standby hint** | ✅ **PASS** — and it is the proof of the HTTP round trip |
| **G** | The tap works from both states | ✅ **PASS** |
| **H** | Nothing blanks | ✅ **PASS** |
| I | Awake is untouched | ⛔ **NOT RUN** — needs a physical knob |
| **J** | No new log noise | ✅ **PASS** |

---

## 1. Scenario A — the two `/sleep` entry paths, exercised separately

This is the criterion #533 added to section 15, and the reason it exists: **idle-at-30-minutes and the
Sleep pill produced different server-side states, so verifying one proved nothing about the other.**
Each path was driven independently and **the API was read to prove the state rather than trust it.**

### Path 1 — the idle mechanism

The 30-minute timer was **not** shortened (the plan forbids it). Instead the exact statement
`idle-dimmer.js`'s `navigateToSleep()` executes was issued in the page:

    window.location.href = '/sleep';

`navigateToSleep` is not exported from the module's IIFE, so it cannot be called by name — but its
entire server-visible behaviour *is* that one statement. The rest of the function (`undim()`, two
`clearTimeout`s, the on-route guard) touches nothing the server can observe. So this is not a
convenient stand-in for the idle path; it is the idle path's mechanism exactly, minus the wait: the
same **hard** navigation, the same brand-new circuit, and critically the same **absence** of any
`SetSleepAsync` call.

    GET /api/system/sleep  ->  {"isSleeping":false,"wakeState":"Ambient"}

✅ **`Ambient`, with `isSleeping` false** — audio still playing, and the server now *knows* the clock
is up. **Before this row the same action left the server reading `Awake`**, which is the whole defect:
the router saw an awake console and dispatched every knob against a screen showing a clock.

Repeated a second time later in the run and reproduced identically.

### Path 2 — the topbar Sleep pill

Clicked `button[aria-label="Enter sleep mode"]`.

    GET /api/system/sleep  ->  {"isSleeping":true,"wakeState":"Standby"}

✅ **`Standby`, with `isSleeping` true** — audio parked.

### What convergence actually means here

The two paths correctly produce **different** states — that was never the bug. The bug was that only
one of them was *visible to the server at all*. Both are now reported through the same
`POST /api/system/sleep-screen`, from the same place (`Sleep.razor` reporting about itself), so the
router gates on a fact that exists no matter how the route was reached.

⚠ **If step 1 had reported `Awake`, every scenario below would have been invalid.** It did not.

---

## 2. The scenarios that passed, and what each one actually proves

### F — the Standby hint, and the only live proof of the HTTP round trip

| State | Hint rendered on the real page |
|---|---|
| Ambient | `tap anywhere to wake` |
| Standby | `tap anywhere, or press any knob, to turn on` |

Both correct, including deviation **D-1** (the Standby line names the tap, which handoff section 8.6's
own copy omits).

**Why this is the load-bearing result and not a cosmetic one.** The plan (section 0.7) states that
`dotnet test` structurally *cannot* cover the new HTTP round trip: `AddHermeticTestRig` fails every
outbound call by design, so a `null` result is the expected bUnit outcome and a dead surface is
indistinguishable from a healthy one. `ENC-8` shipped a page that could not deserialize its own API
response for exactly this reason, with a fully green suite.

The Standby hint closes that, and the timing is what makes it airtight: the pill calls the API, the
server broadcasts `SleepStateChanged(true)`, and *only then* does the browser navigate. The `/sleep`
page therefore subscribes to the hub **after** that broadcast has already fired and never receives
it. The only way the page could know it was in Standby is the **`POST /api/system/sleep-screen`
response body** — parsed, deserialized into `SleepStateDto`, and rendered. It rendered.

### G — the tap resumes from both states

- Tap from **Ambient** -> navigated to `/`, state returned to `Awake`.
- Tap from **Standby** -> navigated to `/`, state returned to `Awake`, and **audio resumed**.

### H — nothing blanks

`/sys/class/drm/card1-DP-1/dpms` read **`On`** throughout, including while parked on `/sleep`.
`card1-DP-1` is the live connector (the other three read `disabled`). ⚠ `ENC-15` failed its gate; a
blank here would be a regression, not a feature. There was none — this row ships no DPMS call.

### J — no new log noise

6 warnings in the journal since the service started, **none from `SleepService` or
`RotaryEncoderActionRouter`**: a missing saved input device, no connected Bluetooth device, two
ASP.NET binding/HTTPS boilerplate lines, and two `Missed callback deadline ... with GC activity`
entries. The second GC line lands about 1 s after the Standby resume, which is the SDR source
restarting — pre-existing behaviour on an unchanged code path. **0 browser console errors** across
the run.

### Bonus — `WakeAsync`'s Ambient/Standby split, confirmed on real hardware

Not a listed scenario, but the file sink recorded it cleanly and it is worth having, because it was
previously covered only by unit tests:

    00:09:57 [INF] Waking from sleep mode (source: api)
    00:09:57 [INF] Sleep mode exited            <- Ambient wake: NO "Resumed source" line
    00:10:13 [INF] Entering sleep mode
    00:10:13 [INF] Paused active source SDR Radio (RTL-SDR) for sleep
    00:10:40 [INF] Waking from sleep mode (source: api)
    00:10:40 [INF] Resumed source SDR Radio (RTL-SDR) after wake   <- Standby wake: audio restored

✅ The Ambient wake broadcast without touching audio; the Standby wake restored playback. That is
Task 2's rewritten guard (`wasSleeping`) behaving correctly against a live audio source — a wake from
Ambient must never "restore" a mute state that was never saved.

### Bonus — the stale-flag correction actually works

`Sleep.razor`'s dispose cannot run when a **hard** navigation kills the circuit, so the code relies on
`MainLayout` reporting the opposite on its own first render. Driven directly: hard-navigated to
`/sleep` (-> `Ambient`), then hard-navigated away to `/`.

    GET /api/system/sleep  ->  {"isSleeping":false,"wakeState":"Awake"}

✅ The flag was corrected by `MainLayout`, on the one path where `Sleep.razor` cannot correct it.

---

## 3. ⛔ What was NOT tested, and why — read this before trusting the table

**Scenarios B, C, D, E and I were not executed.** They all require physically turning or pressing the
knobs, and **there is no software path to inject encoder input on this box:**

- `cafe:4005` exposes **only `/dev/hidraw3` and zero evdev nodes**, so nothing can be synthesised
  through the input subsystem.
- `hidraw` output reports travel host to device; a host cannot fabricate the *input* reports
  `HidRotaryEncoderService` reads.
- There is **no simulation endpoint** — `IntegrationsController` exposes status, provisioning,
  re-apply, save, reset-counters and mapping, and none of them inject an event.

This is a gap in the **whole encoder arc**, not something this row introduced, and it was filed the
same night as **`ENC-17`** ([#540](https://github.com/mmackelprang/RTest/pull/540)) — *"the encoder arc
has been shipping with half its UAT unrunnable."* `ENC-17` establishes that **`/dev/uhid` is present
on the box**, so a virtual-HID injection harness is buildable; it was not built tonight.

**What stands in for these five, and what does not:**

- The gate policy itself is covered by **12 automated facts** driving the **real**
  `RotaryEncoderActionRouter` through every cell of handoff section 8.3's two surviving columns,
  including D22 (a Standby turn does not resume, a press does) and the latch
  (`Ambient_ASecondTurnDuringTheWake_Acts`, which is scenario E's *"one detent, not twelve"* expressed
  deterministically).
- ⚠ **That is not equivalent to scenario E.** The unit test proves the latch's *logic*; it cannot
  measure detents lost across a real browser round trip on real hardware, which is the number
  section 15 asks for. **No measured detent count exists.** It should be recorded the first time a
  human turns the knob, or when `ENC-17` lands.
- ⚠ The router tests drive a `FakeSleepService` that **mirrors** the real derivation rather than using
  it. The pre-merge pass made the fake edge-triggered to match `SleepService` exactly and left a
  comment saying that if the two ever disagree, every gate test is vacuous — but the mirroring itself
  remains an assumption these five scenarios would have broken.

**One consequence worth stating plainly:** the consumed-value readout (`PublishCurrentValue`) has
**never been seen rendering on the real panel**. Its phase is `Value`, which `IsKnownPhase` already
accepts, and `EncoderHud` renders `card.Label` unconditionally on that branch — both verified by
reading the markup rather than by trusting a test, which is how `ENC-5`'s and `ENC-7`'s near-misses
were caught. But *verified by reading* is weaker than *seen*, and this report should not imply
otherwise.

---

## 4. Known follow-up, already recorded

`IsSleepScreenVisible` is an in-memory field on `SleepService`. **An API restart while the kiosk sits
on `/sleep` silently reinstates this row's own headline defect** until something re-renders, and
nothing re-reports on its own. Every deploy restarts `radio-api` — though the deploy also relaunches
the kiosk onto Home, so the common case self-corrects. Recorded in `design/FUTURE-WORK.md` section 7
with the mechanism that closes it; `SetSleepScreenVisible` was made edge-triggered on this row
specifically so that heartbeat cannot wipe an in-flight wake claim. **Not exercised in this run**
(restarting the API mid-UAT would have invalidated the SHA check).

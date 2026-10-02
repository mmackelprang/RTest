# `PHN-11` — an incoming call shows a large banner overlay with the caller

[← Builder Queue index](../BUILDER_QUEUE.md)

🚧 **BUILT 2026-10-02, HELD for owner panel UAT** — branch `feat/phn-11-incoming-call-banner`, one PR with
[`PHN-12`](PHN-12.md). **Not deployed, not merged.** `origin/main` `4ab61b2` (`AUD-87`) merged in first.

🟡 **Owner request, 2026-10-02**, filed and built the same day by the Builder.

> *"Note that I would like to have a large aesthetically pleasing banner overlay when a call comes in along
> with the announcement that shows the caller on the radio console.  It can cover most of the screen, and be
> closed by a touch, phone answer, or call cancelled."*

Addition, same day, relayed by the coordinator:

> *"for PHN-11 - there should be an 'Ignore' button on the overlay that will allow the user to cancell the call
> from the touchscreen."*

Design: [`design-handoffs/2026-10-02-incoming-call-banner.md`](../design-handoffs/2026-10-02-incoming-call-banner.md)
(Designer, session model). Screenshots: [`uat/screenshots/2026-10-02-phn-11-incoming-call-banner/`](../uat/screenshots/2026-10-02-phn-11-incoming-call-banner/).

## What the banner can know (read from RotaryPhone, read-only)

| Event on `/hub` | Sent when | Carries |
|---|---|---|
| `CallStateChanged(phoneId, state)` | every state change; `Idle`, `Dialing`, `Ringing`, `InCall` | no number |
| `IncomingCall(phoneId, number)` | right after `Ringing`, when the number is known — which RotaryPhone always makes it, using the literal `"Unknown"` when there is no caller ID | the number |
| (both again) | a caller-ID update mid-ring (`"Unknown"` → the real number) | |

`InCall` = answered (on the rotary phone or on the cell). `Idle` = the caller hung up, the 60 s ring timeout,
or a hang-up from here. **No call id, no contact name, no photo** travel on the hub; `GET /api/phone/status`
does return a `CallId`. Contact names come from the Web's own lookup: the API's PBAP lookup, then RotaryPhone's
`/api/contacts` — the same order the API's announcement uses.

## Decisions (the design spec's, confirmed in the build)

| Question | Decision | Cost / what the other option gives up |
|---|---|---|
| A call while asleep, or with the panel dark (`ENC-22` timer, `ENC-23` deep sleep) | **Light the panel and show the banner; do not wake the console.** The `/sleep` page reports the sleep screen hidden while the banner covers it (that is what lights a dark panel), and visible again when it closes. Parked music stays parked. | ⚠ After a call in **deep** sleep the panel stays lit on the sleep clock: `ENC-22`'s timer restarts, and the box runs `PanelOffAfterMinutes = 0`. Restoring deep-dark needs an API change (spec Q3). Waking the console instead would start the music under the announcement, at night, for a call that may be ignored. |
| Does a touch also silence the announcement? | **No.** A touch closes only the banner; the call and the announcement are untouched. The hint line says so: *"Touch anywhere to close. The call keeps ringing."* | A one-tap "quiet" is lost; it would be a different action with its own endpoint. |
| A second call while the banner is up | A caller-ID update upgrades the caller line in place. A new call after the first ended gets a fresh banner, even if the last one was touched away. A ring on another phone shows the newest, then falls back. | Stacked banners for two simultaneous rings — a layout nobody will see. |
| Ignore, while RotaryPhone has no decline route | **Shown, disabled, with a reason.** | Hiding it reads as "not built" at UAT. |
| Does Ignore confirm? | **No.** One tap; the whole Ignore column ignores touch-to-close, so a near-miss cannot read as "it worked". | A confirm step protects against a mis-tap at the cost of the ring window. |
| Non-Home pages | Every route, and `/sleep`. On `/phone` the banner covers the page's own Ringing hero. | — |

## Ignore — needs RotaryPhone

RotaryPhone has **no** decline/reject route (checked read-only: `PhoneController` has status, bell-failure
ack, three `simulate/*` routes, system-status and HT801 validate; `RotaryHub` and `GVBridgeController` have
nothing either). The request — `POST /api/phone/decline?phoneId=`, `200 {"declined": true}` from `Ringing`
only, decided atomically against a handset lift — is at
`D:\prj\RotaryPhone\docs\prompts\2026-10-02-radioconsole-decline-ringing-call-request.md`. It is the only file
written in that repo. **The boundary doc's Change Log was not edited** (the coordinator limited this session to
the one file); the request asks RotaryPhone to add the entry.

The banner side is built and tested: `RotaryPhone:DeclineSupported` (Radio.Web, default `false`) enables it;
after a `200` JSON `declined: true` it shows **ENDING CALL** and closes on the `Idle` that follows; a failure, or
a call still ringing 5 s after the request, shows *"Couldn't end the call. Try again."*. Stub recorded in
`design/FUTURE-WORK.md`.

`simulate/hook?offHook=false` would reach the same `CallManager.HangUp()` today. **Deliberately not used**: it
is unconditional, so a tap a moment after the handset is lifted would hang up the answered call.

What the caller experiences (from RotaryPhone's code, not observed): **Bluetooth/HFP** — the cell rejects the
call; the carrier normally sends the caller to voicemail. **Google Voice** — the GV leg was already answered
before the rotary rang, so the caller hears the call drop. RotaryPhone was asked to confirm both.

## Deviations from the design spec, recorded

- **Ambient knobs (pre-merge review M2).** While the banner covers the sleep screen in **Ambient**,
  `SleepService.WakeState` reads Awake, so a knob acts as on the awake console (a TUNING turn retunes) instead of
  being spent waking it. Standby is unaffected. Spec §6 says knobs behave exactly as without the banner; avoiding
  this needs an API seam that lights the panel without touching the wake state. **Owner question.**
- **The spec's Q4 premise is wrong** (found by the reviewer): ENC-25's VOLUME-turn wake is Standby-only; in
  Ambient a VOLUME turn already acts without waking.
- **A ring counts as activity** for the 5-minute dim and the 30-minute idle-to-sleep timer
  (`radioSleepManager.wake('call')` resets both). Not in the spec; chosen because a lit, ringing panel should not
  dim or slide to the sleep screen under the banner.
- The ANSWERED / CALL ENDED beat also recolours the card's hairline and drops its amber glow (instantly). Not in
  the spec's §7; flagged for the Designer.
- Ignore's spoken name while declining is **"Ending call"** (label-in-name); the spec's §10 had no in-flight
  label. The polite-region strings **"Call answered"** / **"Call ended"** are likewise the Builder's.
- Caller upgrades fade in (200 ms) rather than strictly crossfade.
- On `/sleep` the page's own Sleep-variant HUD cannot rise above the banner (it lives in `.sleep-screen`'s
  stacking context), so while the banner covers the screen the page also hosts the fixed-position HUD, which
  does.

## What changed

- `Radio.Web/Services/IncomingCallBannerService.cs` (new, scoped per circuit): the call state machine, the 3 s
  per-phone status read (with `CallId` splitting and one read at a time), the 90 s stale close, name resolution,
  the exit beat and touch fade, Ignore and its 5 s deadline. Logs nothing at Information.
- `Radio.Web/Components/Shared/IncomingCallBanner.razor` (new) and `design-system.css` § PHN-11.
- `MainLayout.razor` and `Sleep.razor` host it; `Sleep.razor`'s visibility reports go through one serialized
  method, the dispose report included; keys on the covered sleep screen never wake the console.
- `PhoneHubService`: the live `CallStateChanged` handler runs through `RaiseCallStateChangedForTest`.
- `PhoneApiService`: `DeclineCallAsync`; `GetCallStateAsync(phoneId)`. `PhoneCallStateDto.CallId`.
- `Radio.Web/appsettings.json`: `RotaryPhone:DeclineSupported: false`.
- Docs: `design/INTEGRATIONS.md` (hub contract, banner section, key files), `design/FUTURE-WORK.md` (Ignore).

## Tests

`IncomingCallBannerServiceTests` (service), `IncomingCallBannerTests` (component, bUnit),
`SleepIncomingCallBannerTests` (the `/sleep` host); shared harness `TestHelpers/IncomingCallBannerHarness.cs`
(the real hub service, API clients and contact resolution over a scripted handler, and a `FakeTimeProvider`).
No wall clock: every async effect is awaited through the service's completion tasks or the handler's gates.
Covered: showing on an incoming call; the three caller cases and the awaiting-caller-ID state; closing on a
touch (and not in the Ignore column), on Escape, on answer, on cancel/end, on a missed hang-up caught by the
status read, and on the stale window (and not before it); a caller-ID update; a later "Unknown"; a ring on
another phone and the fallback; a `CallId` change splitting a touch-closed merge; Ignore once, in flight, the
deadline, every failure shape, a late answer for an earlier call; announce-once across hosts and re-announce for
the same caller calling back; the `/sleep` reports, no wake on touch or key, the HUD over the banner; and no
phone number or name in any log line, nothing at Information.

## Mutation checks

**31 mutants, 31 killed** (each a literal edit to committed code, the targeted tests run, the file restored from
git). Round 1, before review: M1–M20 + M12b — PHN-12's argument order, the `IncomingCall` subscription, the
bare-Ringing raise, the dedupe; touch while declining; the Ignore column's `stopPropagation`; `/sleep`'s first
report; the banner inside the tap-to-wake surface; Answered read as Ended; the stale window; the decline deadline;
decline judged by status or by a non-JSON 2xx; the number logged at Information; "Unknown" replacing a number;
Escape; the undim; announce-once; a touch-closed banner reopened; no fallback; decline without the capability.
**M12b first SURVIVED** — a content-type check was redundant with JSON parsing; the check was removed, and the
mutant re-formed against the parse failure was then killed. M4 and M16 first failed to build (`if (false)` is a
warnings-as-errors failure) and were re-formed. Round 2, after review: N1–N10 — the Ringing reset, "Unknown" as no
number, the per-phone read, overlapping reads, `CallId` splitting, emptied live regions, a taking-over host
staying quiet, the in-flight aria-label, keys on the covered sleep screen, the HUD over the banner. M1–M4, M10,
M14, M18 and M19 were re-run against the refactored code: all killed.

**Not covered by a test:** the dispose report waiting on the report gate; `Start` re-checking `Dispose`; that a
live region is empty at its first render (bUnit cannot observe the first render separately —
`ACallAlreadyUpWhenTheBannerMounts_…` pins the announcement, not the empty first paint).

**Known, left as is (re-review LOWs):** a `CallId` split can misfire if the hub is *late* rather than lost (a
status read sees the new call before a delayed `Idle`): a CALL ENDED flash and a second announcement — only with a
hub lagging by seconds without disconnecting. On a host that took a call over mid-ring, later caller-ID upgrades
swap without the 200 ms fade. An unknown phone id in the status read logs at Error on each 3 s read until the 90 s
stale close (ids come from RotaryPhone's own hub, so only a phone set changed mid-call reaches it).

## Owner checks (needs a real call)

1. **Call the rotary phone.** The banner appears over whatever page is up, with the caller's name (if they are a
   contact), number, or *Unknown caller*, **and the console announces the caller** — the announcement never ran
   on the box before `PHN-12`.
2. **Touch the banner anywhere except the Ignore column.** It closes; the phone keeps ringing; the announcement
   is not cut off.
3. **Call again and pick up the rotary handset.** The banner shows **ANSWERED** for a moment and goes.
4. **Call again and hang up from the calling phone while it rings.** The banner shows **CALL ENDED** and goes.
   ⚠ On the **Google Voice** path the caller's hang-up may never reach the box (RotaryPhone prompt
   `2026-09-11-radioconsole-inbound-hangup-never-arrives.md`): then the phone and the banner keep ringing until
   the handset is lifted, and the banner only closes when RotaryPhone stops reporting the ring. Note the
   wall-clock time.
5. **Sleep the console (tap SLEEP), then call.** The banner shows over the sleep screen, the music stays parked,
   and touching the banner does not wake the console.
6. **Deep sleep (hold SLEEP until the panel goes dark), then call.** The panel lights and shows the banner. After
   the call it stays lit on the sleep clock (expected; see Decisions).
7. **Ignore** is visible and disabled with *"Not available yet…"*. It cannot end the call until RotaryPhone ships
   the decline route and `RotaryPhone:DeclineSupported` is turned on — then: **tap Ignore on a real call; the
   ringing stops, and note what the caller hears** (voicemail on the Bluetooth path, a dropped call on Google
   Voice, per the code).

## Open questions for the owner

1. Ambient knobs while the banner is up (see Deviations) — acceptable, or file the API seam?
2. Deep sleep after a call leaves the panel lit — acceptable, or file an API row (spec Q3)?
3. Answer from the banner? (Spec Q1 — recommended: not in this row.)
4. When the decline route lands, rename the Phone page hero's disabled **Reject** to **Ignore**? (Spec Q2.)

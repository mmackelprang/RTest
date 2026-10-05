# `ENC-25` — in normal sleep (Standby), turning the VOLUME knob wakes the console

[← Builder Queue index](../../docs/BUILDER_QUEUE.md)

✅ **SHIPPED 2026-10-02.** Owner: *"All of these items pass."* on `7583e06` — after a first round that ran on `1daecde` (without this fix) and failed exactly as reported, a pre-fix baseline. Owner also noted: *"pressing sleep currently always mutes.  I'm ok with that behavior."* Merged via [#774](https://github.com/mmackelprang/RTest/pull/774). The status lines below were true until then.

🚧 **BUILT 2026-10-02, HELD for owner panel UAT with the knob** — branch `fix/ui-36-enc-25-wake`, one PR
with [`UI-36`](UI-36.md). **Not deployed, not merged.**

🟡 **P2 — owner report 2026-10-02**, from the UAT of `ENC-23`/`ENC-24` ([#772](https://github.com/mmackelprang/RTest/pull/772)).
ID reserved by the coordinator; taken by the Builder.

> *"UAT - these all pass, but visualizaitons sometimes (but not always) "pause" sometimes after a deep sleep
> for ~15 seconds.  In normal sleep, I expect a volume change to wake the console, but it only shows the new
> volume without waking."*

## Which sleep "normal sleep" is

**Standby** — the topbar Sleep pill's tap, or a VOLUME long-press (`ENC-24`). Confirmed from the API file sink
for the UAT: `Standby entered by a volume knob long-press` at 10:29:18, 10:31:28 and 10:32:55, each followed by
a wake (`encoder-button`, `api`). In Standby a VOLUME turn was **consumed by D22** ("a turn is what a passing
sleeve does; a press is what a person does") and published a card showing the *current* volume — the HUD the
owner saw. (Ambient, the 30-minute idle clock, already lets VOLUME act in place without waking, by design;
it is unchanged.)

## Decided semantics

| Input in Standby | Before | Now |
|---|---|---|
| **VOLUME turn** | consumed; shows the current volume | **wakes the console, then applies the turn** (unmutes if muted, moves the volume, shows the new value) |
| VOLUME turn while VOLUME is **held**, or within **500 ms** of its last press/release | consumed; shows the current volume | **unchanged** — consumed; shows the current volume |
| SOURCE / PRESETS / TUNING turn | consumed (D22) | **unchanged** |
| Any press, a screen tap | wakes | **unchanged** |

- **The turn both wakes and changes the volume** — the owner's words were *"a volume change to wake"*, and a
  knob that wakes the console but drops the movement would feel like a lost detent. The change is applied
  **after** the wake completes: `SleepService.WakeAsync` restores the pre-sleep mute state, and the volume
  handler clears mute on its first detent (`ENC-4b`), so the other order would leave a console that was muted
  before it slept still muted. A fast spin wakes once (the `ENC-6` claim latch); its later detents act directly.
- **Only VOLUME.** The owner asked about the volume knob. The other three keep D22: a SOURCE or PRESETS turn
  in Standby would otherwise open a selector on a console that is still waking, and the sleeve argument still
  holds for them. **Recommendation:** keep it that way; say so if every knob's turn should wake.
- **Guards, so `ENC-24`'s hold is not undone by its own hand.** The VOLUME hold fires Standby at 600 ms with the
  finger still down: a turn while the button is still held does not wake, and neither does one within
  500 ms of the button's last edge (the HID parse raises a report's button edges before its turn, so a stray
  detent can arrive with the release). `ENC-24`'s rule is unchanged: a hold cancelled by turning never enters
  Standby at all.
- **Deep sleep (`ENC-23`) — unchanged, and recommended to stay so.** On a deep-dark panel a turn still only
  lights it onto the Standby screen, and every input inside the 2 s wake grace is still spent. What is new is
  what happens *after* that: a further VOLUME turn on the lit Standby screen now wakes, like the press always
  did. Making the first turn on a dark panel wake as well was **not** done: in the dark a turn is the likeliest
  accidental input, `ENC-23`'s owner spec names the *press* as the deep-sleep wake, and the turn that lights the
  panel already shows the console responding.
- **The Standby hint line** (`tap anywhere, or press any knob, to turn on`) is designed copy and was left as is;
  it does not mention the turn and says nothing false.

## What changed

- `RotaryEncoderActionRouter.GateInput`: a new `SleepGateOutcome.WakeThenDispatch` for a VOLUME turn in Standby
  that passes `VolumeTurnMayWake()`; `WakeThenTurnAsync` awaits `WakeAsync("encoder-volume-turn")` and then runs
  the turn through the same handler table. VOLUME button edges are timestamped (`TimeProvider`).
- `EncoderLongPressGesture.IsHeld(index)`.
- Comments: `ISleepService.ConsoleWakeState.Standby`, `Sleep.razor`'s hint comment, the router's panel-gate note.
- `design/INTEGRATIONS.md` § the input table and a new `ENC-25` paragraph; § Deep sleep's wake bullet.

## Tests

`RotaryEncoderRouterMappingTests` (FakeTimeProvider; the wake's completion is a `TaskCompletionSource` the test
releases, and the router exposes `WakeTurnIdle` as the rendezvous — no wall clock):
`Standby_AVolumeTurn_WakesTheConsole_AndAppliesTheTurn`, `…_IsAppliedOnlyAfterTheWakeHasRestoredMute`,
`Standby_AFastVolumeSpin_WakesOnce_AndEveryDetentMovesTheVolume`, `Standby_AVolumeTurnWhileVolumeIsStillHeld_DoesNotWake`,
`Standby_AVolumeTurnInsideTheSettleWindowAfterTheRelease_DoesNotWake_ButOneAfterItDoes`,
`Awake_AHoldCancelledByTurningVolume_StillDoesNotEnterStandby_OrWake`,
`DeepDark_AVolumeTurn_StillOnlyLights_ThenAVolumeTurnOnTheLitStandbyScreenWakes`,
`Standby_AConsumedVolumeTurn_StillShowsTheCurrentVolume`, `Standby_ATurnOfAnyOtherKnob_DoesNotResumeAudio`;
`EncoderLongPressGestureTests.IsHeld_*`. Three existing tests that drove VOLUME turns in Standby to reach the
consumed readout now use another knob, or a press.

## Owner check (needs the knob)

1. Tap SLEEP (Standby). Turn VOLUME one click. **The console wakes to the full UI and the volume moves one step.**
2. Mute, tap SLEEP, turn VOLUME. **It wakes unmuted** (the turn clears mute, as when awake).
3. Hold VOLUME until Standby; keep holding and turn. **It stays asleep.** Let go, wait a second, turn: **it wakes.**
4. In Standby, turn SOURCE, PRESETS, TUNING. **No wake**; each shows its value.
5. `ENC-23`/`ENC-24` again: deep sleep + VOLUME press wakes in one press; a hold cancelled by turning does not sleep.

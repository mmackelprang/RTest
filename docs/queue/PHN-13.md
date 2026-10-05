# `PHN-13` — enable the banner's Ignore; rename the Phone page's Reject to Ignore

[← Builder Queue index](../BUILDER_QUEUE.md)

✅🔬 **SHIPPED** in [#779](https://github.com/mmackelprang/RTest/pull/779) (squash `57bc371`, with [`PHN-14`](PHN-14.md)) and [#780](https://github.com/mmackelprang/RTest/pull/780) (squash `22363aa`,
`RotaryPhone:DeclineSupported` on by default). **Deployed and verified at `22363aa` on 2026-10-04.** The owner's real-call
test stopped the rotary ringer, **but the cell kept ringing**. That is open in RotaryPhone; see § Owner real-call test,
2026-10-04. *(Was: built 2026-10-03, held for the owner's real-call UAT.)*

🟡 **P2, owner request.** Filed 2026-10-03 by the coordinator.

Owner, 2026-10-03: *"The 'Ignore' API is done and you have a handoff for it. … if you could do any prep or start working on it, that would be great."* Earlier ruling (2026-10-02, `PHN-11`): *"d - yes - ignore is the label I want."*

## The route (RotaryPhone reply, `archive/handoffs/radioconsole-decline-endpoint-reply.md` on their `main`)

- `POST /api/phone/decline?phoneId=default` → `200 {"declined": true}` only from `Ringing`; `409 {"declined": false, "state": "<Idle|Dialing|InCall>"}` otherwise; `404` for an unknown phone id.
- Atomic against a handset lift (same lock; raced 200× in their tests). After a `200` the `Idle` broadcast follows synchronously.
- Log line `Incoming call declined from Radio Console`, no number. Call history: `NotAnswered` + `EndTime`.
- ⚠ **Not verified on a real call yet.** Their reply mentions an owner console test planned for 2026-10-04; do not switch `DeclineSupported` on until it passes.

## What our side must change

1. **`409` is benign, not a failure.** `PHN-11` treats anything but `200 {"declined":true}` as "Couldn't end the call. Try again." Per the reply: `409` + `"state":"InCall"` = the handset was lifted first → close quietly (or show ANSWERED); `409` + `"state":"Idle"` = the caller gave up → close quietly. Keep the error only for transport failures, 5xx, 404, or a `200` without `declined:true`.
2. **Enable it:** set `"RotaryPhone": { "DeclineSupported": true }` (Radio.Web `appsettings.Production.json` seed or the config store) once the real-call test passes; then run `PHN-11`'s Ignore owner check.
3. **Phone page:** rename the hero's disabled **Reject** to **Ignore** and wire it to the same decline (`docs/known-issues-and-future-work.md`, `PHN-11` entry).
4. **Update `docs/known-issues-and-future-work.md`** (the `PHN-11` Ignore entry becomes done) and `docs/integrations.md`.

## What the caller experiences (RotaryPhone, from code — untested)

| Path | Result |
|---|---|
| Bluetooth / HFP | The cell rejects the ringing call; the carrier usually sends the caller to voicemail. |
| Google Voice | **The call drops; no voicemail** (the GV leg is answered at INVITE). Sending to voicemail needs RotaryPhone to hold `200 OK` until a lift — owner-deferred on their side; it would likely also fix the 2026-09-11 GV hang-up defect. |
| SIP trunk | ⚠ Probably **keeps ringing on the caller's end** — the box goes Idle but nothing is sent to the trunk. Worth raising with RotaryPhone if that path is used. |

## Owner checks (once built and the flag is on)

- A real call → tap **Ignore** → ringing stops, the banner closes, the announcement stops; note what the caller heard (path).
- Lift the handset, then tap Ignore quickly → the call continues and the banner closes with no error.
- Phone page: **Ignore** is shown in place of Reject and behaves the same.

## What was built (2026-10-03)

- **`409` is benign.** `PhoneApiService.DeclineCallAsync` now returns `Declined` / `NotRinging` (any `409`, with its
  `state`) / `Failed`. The banner closes on a `409` as it would on the hub's own event — `InCall` → the **ANSWERED**
  beat, `Idle` or anything else → **CALL ENDED** — with no error, and no late deadline error either. The error
  stays only for transport failure, 5xx, `404` and a 2xx without `"declined": true`.
- **The Phone page hero's Reject is Ignore**, wired to the same decline (`PhonePage` →
  `PhoneApi.DeclineCallAsync(phoneId: null)`, i.e. RotaryPhone's `default` phone — the one the page shows), with the
  banner's states: *Ending call…* after a `200` until the call leaves Ringing, the error if it is still ringing 5 s
  later or on a failure, and a quiet reset on a `409`. Disabled with *"Not available yet…"* while the flag is off.
- **The flag is read on every use** (it was read once per circuit), so the coordinator's config-store flip reaches
  the kiosk's open circuit from its next call. Shipped default unchanged: `false`.

## The enable step (owner check #1; the coordinator runs it with the owner's go-ahead)

```bash
curl -s -X POST http://radio:5000/api/configuration/RotaryPhone \
  -H 'Content-Type: application/json' -d '{"DeclineSupported": true}'
```

Writes `rotaryphone:DeclineSupported = true` to the shared config store, which radio-web reads over the shipped
`false`; radio-web reloads on the API's `ConfigChanged` push (if missed: `sudo systemctl restart radio-web`). The
same POST with `false` switches it off. `RotaryPhoneDeclineEnableStepTests` pins the round trip.

## Tests

`PhoneApiServiceTests.DeclineCallAsync_*` (every answer shape), `IncomingCallBannerServiceTests`
(`A409_ClosesTheBannerQuietly_WithNoError` ×4, a `409` after the hub already closed the banner, the live flag, and
the failure theory — 5xx, 502, both `404`s, a 2xx without `declined:true`, a non-JSON 2xx), `PhoneStatusHeroIgnoreTests`
(the hero's states, double tap, deadline, reset), `PhonePageTests.Dashboard_Ringing_*` (the page sends the decline;
a `409` shows no error; a 5xx shows it; disabled while the flag is off).

## Pre-merge review (session model) and what was done

No HIGH. **M1** (the banner consulted the circuit's local contact index first — one phone book's first numbers plus
every RotaryPhone contact — so it could show a RotaryPhone name where the announcement spoke the synced one): fixed,
the banner asks the API's lookup directly (`ResolveAsync(skipLocalIndex: true)`), pinned by
`ThePhonePagesLocalIndex_DoesNotOutrankTheApisSyncedPhoneBookAnswer`. **M2** (the hero's 5 s deadline started at
the `200`, the banner's at the tap): fixed, armed at the tap; pinned. **M3** (precedence is source first, then
match tier, so a last-seven synced match beats an exact RotaryPhone match): **not changed — owner question**; it
meets the stated "synced phone contacts first", and the alternative (synced-exact → RotaryPhone-exact →
synced-last-7 → RotaryPhone-last-7) changes that rule. LOWs fixed: `DeclineCallResult.Failed` is 0 (a default
outcome fails closed); no timer after the hero is disposed; the `409` close drops an attempt check that guarded
nothing; the stale `Program.cs` cross-process reload comment and the hero's phone-id comment corrected; a remark
narrowed ("does not parse"); a PhonePage test now waits for the `200` to be handled. Left: the Debug line naming the
device MAC (acceptable — MACs are already logged at Information by `PbapSyncService`).

## Mutation checks

**51 mutants over two rounds, 51 killed** (literal edits in a separate worktree outside the repo, targeted tests
run, file restored from git). Round 1, 30: every ranking step in the repository, the controller's old no-device
404 and connected-phone argument, the lookup service's PBAP skip / connected phone / old `/lookup` route, both
`FindMatch` tiers and the country-code strip, the `409` read as failure / its state dropped / InCall closing as
Ended, the cached miss, the RotaryPhone source, the flag read once, PBAP-first priming, `retryCachedMiss`, and the
hero's 409 / failure / deadline / reset / double-tap / enable, the page and panel wiring. Round 2, after review, 21:
the two new mutants (local index consulted; `skipLocalIndex` ignored) and the Web round re-run against the
refactored code (M24 re-formed: its anchor moved with the tap-armed timer). Not covered by a test: the
`Dispose` attempt bump in the hero, and the enum order.

## Owner real-call test, 2026-10-04 — the console half passes; the cell keeps ringing (OPEN, RotaryPhone)

This ran on `22363aa`, deployed and verified on both services, with `DeclineSupported` on by #780. A real incoming
call reached the paired cell, and the owner tapped **Ignore** on the console:

- ✅ the rotary phone stopped ringing (the HT801/handset side);
- ⛔ **the cell kept ringing.** Decline does not reject the call on the cell. This is a **RotaryPhone-side gap**,
  reported in `D:\prj\RotaryPhone\docs\prompts\2026-10-04-radioconsole-decline-does-not-reject-on-cell.md`
  (it asks for the HFP reject on `hci1`). **Not fixed.** It is tracked in
  [`CROSS-REPO-HANDOFFS.md`](CROSS-REPO-HANDOFFS.md) and in the punch list's 2026-10-04 status.

**Not yet recorded:** owner checks 2 (lift the handset, then tap Ignore) and 3 (the Phone page's Ignore), and M3
(the lookup precedence question, an owner call). The row stays ✅🔬 in the queue until they are recorded and
RotaryPhone replies.

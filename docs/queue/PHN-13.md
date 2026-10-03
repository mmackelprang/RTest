# `PHN-13` — enable the banner's Ignore; rename the Phone page's Reject to Ignore

[← Builder Queue index](../BUILDER_QUEUE.md)

🟡 **P2, owner request.** Filed 2026-10-03 by the coordinator (prep only; nothing built).

Owner, 2026-10-03: *"The 'Ignore' API is done and you have a handoff for it. … if you could do any prep or start working on it, that would be great."* Earlier ruling (2026-10-02, `PHN-11`): *"d - yes - ignore is the label I want."*

## The route (RotaryPhone reply, `docs/handoffs/radioconsole-decline-endpoint-reply.md` on their `main`)

- `POST /api/phone/decline?phoneId=default` → `200 {"declined": true}` only from `Ringing`; `409 {"declined": false, "state": "<Idle|Dialing|InCall>"}` otherwise; `404` for an unknown phone id.
- Atomic against a handset lift (same lock; raced 200× in their tests). After a `200` the `Idle` broadcast follows synchronously.
- Log line `Incoming call declined from Radio Console`, no number. Call history: `NotAnswered` + `EndTime`.
- ⚠ **Not verified on a real call yet.** Their reply mentions an owner console test planned for 2026-10-04; do not switch `DeclineSupported` on until it passes.

## What our side must change

1. **`409` is benign, not a failure.** `PHN-11` treats anything but `200 {"declined":true}` as "Couldn't end the call. Try again." Per the reply: `409` + `"state":"InCall"` = the handset was lifted first → close quietly (or show ANSWERED); `409` + `"state":"Idle"` = the caller gave up → close quietly. Keep the error only for transport failures, 5xx, 404, or a `200` without `declined:true`.
2. **Enable it:** set `"RotaryPhone": { "DeclineSupported": true }` (Radio.Web `appsettings.Production.json` seed or the config store) once the real-call test passes; then run `PHN-11`'s Ignore owner check.
3. **Phone page:** rename the hero's disabled **Reject** to **Ignore** and wire it to the same decline (`design/FUTURE-WORK.md`, `PHN-11` entry).
4. **Update `design/FUTURE-WORK.md`** (the `PHN-11` Ignore entry becomes done) and `design/INTEGRATIONS.md`.

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

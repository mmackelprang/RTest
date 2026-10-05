# `PHN-12` — the API's phone client misread RotaryPhone's hub contract, so the call announcement never ran

[← Builder Queue index](../BUILDER_QUEUE.md)

✅ **SHIPPED 2026-10-02.** Owner: *"For 777 all pased."* — the same call now produced the console's first-ever call announcement. `PhoneCallClient` binds `CallStateChanged(phoneId, state)` and `IncomingCall(phoneId, number)` in RotaryPhone's order, pinned by a contract test over real SignalR. Merged via [#777](https://github.com/mmackelprang/RTest/pull/777). The status lines below were true until then.

🚧 **BUILT 2026-10-02, in the same PR as [`PHN-11`](PHN-11.md)** — branch `feat/phn-11-incoming-call-banner`.
**HELD for owner panel UAT** with `PHN-11`: not deployed, not merged.

🔴 **Found by the `AUD-87` Builder ([#775](https://github.com/mmackelprang/RTest/pull/775)), relayed by the
coordinator 2026-10-02, and re-verified here by reading both sides' code before anything was built.** The
coordinator routed it into `PHN-11`'s PR because the owner wants the announcement and the banner together.

## The defect

RotaryPhone's contract (`RotaryPhoneController.Server/Services/SignalRNotifierService.cs`, `OnStateChanged`,
read-only 2026-10-02):

```
SendAsync("CallStateChanged", phoneId, manager.CurrentState.ToString());   // Idle | Dialing | Ringing | InCall
if (state == Ringing && IncomingPhoneNumber != null)
  SendAsync("IncomingCall", phoneId, manager.IncomingPhoneNumber);
```

`Radio.Infrastructure/External/PhoneCallClient.cs` (before this row):

- `:65` `On<string, string>("CallStateChanged", OnCallStateChanged)` with `OnCallStateChanged(string state,
  string phoneNumber)` (`:117`) — the **phone id was parsed as the state**. No arm of `ParseCallState` matches a
  phone id, so every event read as `Idle`; the API never saw `Ringing`, and never ran the announcement.
- `:66` a second, three-argument `CallStateChanged(state, phoneNumber, callerName)` — nothing sends it.
- No `IncomingCall` subscription at all, though it is the only event that carries the number.

**Evidence on the box (the `AUD-87` Builder's, not re-read here — the box is read-only for this row):** the 7
retained API logs hold three `Phone call state:` lines, all `Idle`, never `Ringing`. `phoneintegration:enabled`
is `true` on the box, so the old note that the integration was off is wrong.

**`Radio.Web` was never affected.** `PhoneHubService` binds `CallStateChanged` as `(phoneId, state)` and
`IncomingCall` as `(phoneId, phoneNumber)` — the server's order — which is what the `PHN-11` banner and the Phone
page consume.

## The fix (kept to the minimum)

- `CallStateChanged` bound as `(phoneId, state)`; `IncomingCall` bound as `(phoneId, phoneNumber)`; the
  three-argument registration removed.
- **Ringing is raised from `IncomingCall`**, with the number. The bare `CallStateChanged(…, "Ringing")` raises
  nothing: it carries no number, every inbound path in RotaryPhone sets the number first (`"Unknown"` when there
  is none), and raising both would announce "Unknown caller" and then cut it off milliseconds later (`AUD-87`'s
  `PhoneCallIntegrationService` treats every `Ringing` as a new call). A caller-ID update (the real number
  replacing `"Unknown"`) raises again, which restarts the announcement with the better name — intended.
- Every `CallStateChanged(…, "Ringing")` resets the ring — RotaryPhone sends one before every `IncomingCall` — so
  one missed `Idle` cannot silence the same caller's next call (pre-merge review M3). The same number arriving
  twice with no `CallStateChanged` between is a duplicate delivery and is not raised. (A reconnect-time reset was
  tried and removed on re-review: redundant with the per-`Ringing` reset, and it set the reported state to `Idle`
  silently mid-ring.)
- **RotaryPhone's `"Unknown"` is raised as no number** (review M4): passed through, the API would have said
  *"Incoming call from Unknown"*, looked it up as a contact and reported it back to RotaryPhone as a resolved
  name. As null, the announcement says *"Unknown caller"* and does neither.
- Every other state is raised as it arrives and clears the caller. `"Dialing"` still maps to `Idle`, as before.
- Logging: the ringing line keeps the masked number (`LogSafeText.ForPhone`); the other lines carry none.
- An internal constructor takes a connection-options callback (the test seam), and `Radio.Infrastructure`
  grants `InternalsVisibleTo` to `Radio.API.Tests`.

`PhoneCallIntegrationService` is **unchanged** — merged from `origin/main` (`4ab61b2`, `AUD-87`) first.

## Tests

- `Radio.API.Tests/Services/PhoneCallClientContractTests` — **the contract pin.** An in-process `TestServer`
  hosts a hub; the test sends exactly RotaryPhone's two calls through `IHubContext`, and the real
  `PhoneCallClient` connects over SignalR (long polling). Ringing arrives as `Ringing` with the number and exactly
  one event; `InCall` arrives as `InCall`; `Idle` as `Ended`. Waits are on the events (a TCS each); the 15 s
  timeout only bounds a failure.
- `Radio.Infrastructure.Tests/External/PhoneCallClientHubEventTests` — the semantics: the ring is raised once from
  `IncomingCall`; a re-send of the same number is not raised; a caller-ID update is; leaving the ring clears the
  caller; the same caller calling back after an `Idle` is a new ring; a phone id that spells a state is still a
  phone id.
- `PhoneCallClientLogSafetyTests` (`PHN-5`) rewritten for the new handlers; the name arms went with the
  three-argument overload that carried a name.
- `PhoneCallIntegrationLogSafetyTests.PHN12_ACallWithNoCallerId_…` — a ring with no number is announced as
  "Unknown caller", with no lookup and no report back.

Mutation checks: see [`PHN-11`](PHN-11.md) § Mutation checks (M1–M4, N1–N2).

`SystemConfigPage`'s phone status row shows **Unknown caller** for a ring with no number (it showed a blank once
`"Unknown"` stopped arriving as a number; found on re-review).

## Owner check

1. Call the rotary phone. **The console announces the caller** ("Incoming call from …") — this has never worked
   on the box before. A withheld number is announced as "Unknown caller".
2. Hang up from the calling phone while it rings. **The announcement stops.** ⚠ On the Google Voice path the
   caller's hang-up may never reach the box (RotaryPhone prompt `2026-09-11-radioconsole-inbound-hangup-never-arrives.md`);
   then it stops when the handset is lifted.
3. Call again and pick up. The announcement stops at the pick-up.

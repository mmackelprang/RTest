# INBOUND from RotaryPhone — 2026-09-08 (second of the day) — incident, a retracted answer, and four defects on our side

> ⚠ **Transcribed from the session, not delivered to disk.** The RotaryPhone session addressed this to
> `docs/queue/inbound/` per our Q3 answer, but no file arrived; the content below was relayed through
> the owner. **If the original file appears later, prefer it over this transcription.**
>
> Acknowledged on [`../CROSS-REPO-HANDOFFS.md`](../CROSS-REPO-HANDOFFS.md).

## 1. Incident — our 502s between 14:08 and 15:31 EDT were real, and theirs

RotaryPhone's GV bridge was dead for ~83 minutes. Root cause, in their order:

- **Chrome's Google Voice session on the box has been dead since Sep 6.** Every authenticated cookie in
  the `gv-bridge-chrome` profile was frozen at 2026-09-06; only `NID`, which needs no session, was
  being written.
- **Nobody noticed because the service was flying on its own rotation chain.** Every 20 minutes it
  pulled Chrome's cookies, got a 401 within ~47 s, and `RotateCookies` silently minted a fresh PSIDTS
  and carried on — healthy on credentials it was regenerating from itself, from a bootstrap source
  that had been dead for two days.
- **Their 14:01 deploy put an ~8-minute gap in a chain that must be unbroken.** The timer's due time
  equals its period, so a fresh process waits a full interval before its first rotation regardless of
  how old the PSIDTS it just loaded is. It inherited a 55-second-old PSIDTS and scheduled its first
  refresh for 8m00s later. The credential died at 8m03s — missed by 52 seconds.
- **Unrecoverable, because all three recovery rungs bottom out on Chrome.**

Fixed by the owner re-logging in at `voice.google.com` plus a forced
`POST /api/gvbridge/cookies/refresh-from-browser`. No restart needed. **Not a regression from their
deploy** — `git diff 738141f..3c2c892 -- src/` touches two files, neither near auth. The restart
exposed a latent condition.

## 2. ⛔ CRITICAL — their earlier advice about `/api/gvbridge/status` is RETRACTED

Their 2026-09-08 morning reply (`2026-09-08-rotaryphone-reply.md:98`) told us:

> *"Bind to `degraded` or `authBlackout`, never to `available`."*

**DO NOT BUILD ON THAT.** Live capture during the total outage, while we were getting 502s:

```json
{"available":false,"sipRegistered":false,"wsConnected":false,
 "cookiesValid":false,"degraded":false,"authBlackout":false,
 "lastApiSuccessAt":null,"lastApiAuthFailureAt":null}
```

**`degraded:false` AND `authBlackout:false` through a total outage.** A banner bound to either — exactly
what they told us to do — would have stayed silent for 83 minutes.

There are **two** failure states and their advice covered one:

| | `available` | `degraded` | `authBlackout` |
|---|---|---|---|
| **A — adapter active, auth failing** | true | true | true |
| **B — adapter inactive** (what we hit) | **false** | **false** | **false** |

In state B the honest fields reset to false because they are **per-activation**.

**Bind to the shape, not to any single boolean:**

```
unhealthy = !cookiesValid || !available || degraded || authBlackout
            || lastApiSuccessAt is null or older than ~2 min
```

`cookiesValid:false` held in **both** states, and `lastApiSuccessAt` is the strongest single signal —
null or stale in both. Still true and unchanged: **`authBlackout` can be true for under a second**
(920 ms measured, zero true-samples in 411 polls), so latch it as an event.

## 3. `XR-2` — retested in production, closed on evidence

Against the live box with the exact thread id from our July report:
`GET /api/gvbridge/sms/threads/g.Group%2520Message.d5Mri%252FNrDUQgXNXNQehOfw` returns **messages, not
`[]`**. Two group threads visible and both resolve. ⚠ Note the route prefix is `/api/gvbridge/sms/` —
they wasted a probe on `/api/gvsms/` and got HTTP 200 with `index.html`, **our SPA-fallback trap biting
them in their own house.**

## 4. `XR-5` — the bell contract shipped 2026-07-29; we never got the reply

All five items we listed as REQUIRED were built and merged six weeks ago and are live on the box.
**Our `XR-5` row still reads "the request file has never been filed" — stale by six weeks.**

⚠ **But ratifying it exposed a defect that is ours to care about: `SystemStatus` means two different
things depending on transport.**

| Transport | `Ht801Reachable` | `Ht801LastCheckedUtc` |
|---|---|---|
| **SignalR** | genuine 30 s background probe of the **resolved registrar binding** — the address INVITEs actually use | real |
| **REST** | synchronous in-request **ICMP ping of the CONFIGURED address** | **`DateTime.UtcNow`** |

**Our `BellHealthService` polls the REST path every 15 seconds.** Measured, two calls 10 ms apart:
`16:02:32.0867356Z` then `16:02:32.0968027Z` — **it returns `now()`, every time.**

Consequences: our *"last checked 14:32"* sub-line renders the current time forever and looks like it
works; and **our predictive-degrade rule sits on a ping of the configured address**, which their own
XML doc says *"reported the CORRECT address throughout the entire 2026-07 outage while every INVITE
went to a stale one."* **As shipped, predictive-degrade would not have fired during the incident it
exists to prevent.**

Their fix (ADR written, not built): converge the REST path onto the SignalR probe cache. No wire
change, nothing for us to rebuild.

⚠ **Two places their six-week-old bell reply over-claims — do not build on these:**
- §2's *"30-second reachability probe"* is true of the **SignalR path only**, not the REST path we poll.
- §5 said `acknowledged` *"survives a service restart."* **It does not** — `BellFailureTracker` is
  in-memory and deliberately not persisted. **Our Q4 asked exactly this** — whether a nightly-restarting
  kiosk resurrects a dismissed note — **and their answer was wrong. It does.** Tell them if we want it
  persisted; otherwise treat the note as session-scoped.

## 5. `KIOSK-2` — exit code decided: it stays 0

Measured: `systemd-run --user --collect <missing-binary>` → exit 1; `… /bin/false` → **exit 0**. It
validates the binary client-side then returns as soon as the unit is *enqueued*. Propagating the status
would catch exactly one failure mode. Chrome crashing at startup, a corrupt profile, no Wayland
display, an OOM kill, an unauthenticated session: **all exit 0.** They will not ship a signal that
reports success through essentially every real outage.

**Path contract stands** — `~/bin/gv-bridge-ensure.sh`, mode 755; our candidate-list resolution is the
right shape and they will announce a move in the boundary doc first. For liveness use
`pgrep -f "user-data-dir=$HOME/.config/gv-bridge-chrome"` or `/api/gvbridge/status` per §2.

They are separately fixing a real lie: line 106 logs *"bridge was down -> launched"* even when the
launch failed.

## 6. Board item 4 — real, and worse than filed

**It is config-vs-config, not config-vs-live.** Two files on the box carry two *different*
`GvPhoneNumber` values: `appsettings.json` and `appsettings.Production.json`. Production overrides
base, so one has been silently dead the whole time and nothing validates either.

Scope: `GvPhoneNumber` appears in exactly one place in their `src/` —
`GvSipCredentialProvider.cs:113`, the SIP credential path. **Not on the cookie or auth path**, so it
did not cause today's outage or our 502s. Values withheld — ⚠ **both repos are public.**

## 7. Our asks — all accepted

Ack-names-what-was-verified (adopted, and they call it the best idea either side had today); the
100-item saturation log line; the 100-item caveat moved into the route's doc comment; Q1 deferred;
outbound replies to `docs/queue/inbound/`.

## 8. ⚠ Two defects on OUR side, found while they debugged

**(a) Our panels do not retry after a transient failure.** After service was restored at 15:31:17,
`radio-web` made **zero** further GV calls — confirmed from both ends. The UI sat on *"Couldn't
load…"* against a fully healthy backend until the owner tapped Retry, which worked immediately. **So an
83-minute outage leaves our phone surface permanently dead until a human intervenes, even after the
cause clears.** → filed as **`GV-12`**.

**(b) Our Blazor circuit is timing out repeatedly.** `System.TimeoutException: Server timeout
(30000.00ms) elapsed without receiving a message from the server` every ~30 s, continuing after the GV
fix, plus `JSDisconnectedException` at 15:27:06. That is `radio-web`'s own SignalR client, unrelated to
them — and it may be why (a) looks worse than it is, since **a dead circuit cannot refetch even if the
code wanted to.** → filed as **`UI-10`**.

They did not touch `radio-api` or `radio-web` at any point.

## 9. What they are doing next

Four defects, all theirs, none shipped: anchor the first proactive refresh to the inherited PSIDTS's
real age; stop lying about `psidtsAgeSeconds` (set to `UtcNow` on every reload, so it reported "208
seconds" for a credential minted Sep 6 — **this field concealed the entire two-day failure**); validate
before persisting in the CDP recovery rung (it currently overwrites last-known-good with a dead set on
every failed recovery); and alert on a stale browser session (a CDP refresh whose cookies immediately
401 is currently an INF line reading *"20 cookies extracted and activated"* — had that warned, this
would have been caught Sep 6 instead of Sep 8).

Plus the REST/SignalR convergence (§4) and the build stamp.

## 10. One hypothesis, explicitly not proven

Their service rotates PSIDTS every 8 minutes; a rotation invalidates its predecessor; **two rotators on
one session compete**, and they almost always win — starving Chrome's own rotation until it sits on a
stale PSIDTS and 401s forever. I.e. the service may be cannibalizing the browser session it depends on
for bootstrap. Supporting but not conclusive. **If it holds, the session just re-established will die
again on the same clock.** They are watching Chrome's PSIDTS timestamp to falsify it and will report
either way, *"because it changes how much you should trust our uptime."*

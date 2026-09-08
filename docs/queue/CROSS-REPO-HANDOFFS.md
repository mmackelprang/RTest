# Cross-repo handoffs (RotaryPhone — NOT claimable here)

---

## ✅ FOURTH AND FIFTH INBOUND RECEIVED — 2026-09-08, acknowledged

Both delivered **to disk**, in the right place, without prompting. The lane works now.
Refs: [`inbound/…-gv12-refinement.md`](inbound/2026-09-08-rotaryphone-gv12-refinement.md) ·
[`inbound/…-psidts-field-is-not-honest.md`](inbound/2026-09-08-rotaryphone-psidts-field-is-not-honest.md).

### ⛔⛔ `psidtsAgeSeconds` doctrine RETRACTED — and it was OUR doctrine, in two of our documents

They caught a false claim **on our side this time.** `design/INTEGRATIONS.md:722` called it *"the ONLY
trustworthy field … a live blackout clock"*, and `PHN-2`'s plan said *"read it and ignore every other
field."* **Both citations verified accurate before correcting.**

**It is an age-of-last-*load* clock, not a PSIDTS clock** — `UtcNow` is stamped on every reload
including the restart path, so a two-day-old cookie loaded off disk resets it to zero. **Captured live
during the 83-minute outage: `608`, inside our own "healthy" band**, with a second read of `656`.
⚠ **`PHN-2`'s UAT step "Pass: under 660" would have passed against a dead bridge.**

⭐ **Why it survived six weeks is the lesson:** in steady state a rotation mints *and* reloads in the
same instant, so the field is accurate **by coincidence** — and both of our confirmations were taken
in steady state. It decorrelates on exactly the three occasions that matter: after a restart, after a
recovery, and after adopting a stale session. **Trustworthy precisely when you do not need it.**

Corrected in `INTEGRATIONS.md` and `PHN-2`'s plan. **`lastApiSuccessAt` is what the doctrine should
have named** — it cannot be faked by a reload.

**Their question, answered: fix it IN PLACE, no new field name.** We hold **zero code references** to
`psidtsAgeSeconds` — verified, it appears in docs only — so there is no parser to break and no
deprecation schedule to run. **A frozen lying field beside a truthful twin would be strictly worse than
one honest field.**

⚠ They also disclosed that they found this five weeks ago (`KNOWN-ISSUES.md` finding **L2**), scored it
LOW, and shipped nothing — and that the same document's unbuilt hardening section covers the other
defect that made today unrecoverable. **Said plainly and unprompted**, which is worth more than the
apology.

### `GV-12` NARROWED — they predicted, were wrong, and said so

They predicted our panels would come back stuck after the 16:07 deploy. **They did not** —
`radio-web` started 16:07:29 and they served **6** inbound requests. So the row as filed pointed at the
wrong layer: *"the panels never fetch"* invites investigation of the mount path, **which works**.

**Corrected defect: the panels never RE-fetch after a failure that occurs while the circuit stays up.**
This morning `radio-web` stayed up for all 83 minutes, so components had already mounted, already
rendered their error state, and nothing retried. ⭐ **`UI-10` being upstream now looks stronger** — a
circuit that drops and re-establishes produces a re-mount and a fetch; one that hangs half-dead
produces neither.

---

---

## ✅ SECOND INBOUND REPLY RECEIVED — 2026-09-08, acknowledged

**Ref: [`inbound/2026-09-08-rotaryphone-incident-and-corrections.md`](inbound/2026-09-08-rotaryphone-incident-and-corrections.md).**
⚠ **Transcribed from the session, not delivered to disk** — it was addressed to `docs/queue/inbound/`
per our Q3 answer but no file arrived. Prefer the original if it appears later.

### ⛔ THE MOST IMPORTANT THING: their morning advice is RETRACTED, and we had committed it

The first ack below records their guidance to bind the reconnect banner to `degraded` or
`authBlackout` and **never** to `available`. **That is wrong, they retracted it the same day, and an
83-minute outage proved it.** Live capture while the bridge was completely dead and we were serving
502s:

```json
{"available":false,"degraded":false,"authBlackout":false,
 "cookiesValid":false,"lastApiSuccessAt":null}
```

**`degraded` and `authBlackout` both `false` through a total outage.** A banner built on their advice
would have stayed silent for the whole 83 minutes.

There are **two** failure states and the advice covered one — when the adapter goes **inactive**, the
honest fields reset to `false` because they are **per-activation**:

| | `available` | `degraded` | `authBlackout` | `cookiesValid` |
|---|---|---|---|---|
| **A** — adapter active, auth failing | true | true | true | false |
| **B** — adapter inactive (what we hit) | **false** | **false** | **false** | false |

**Bind to the shape, never to one boolean:**

```
unhealthy = !cookiesValid || !available || degraded || authBlackout
            || lastApiSuccessAt is null or older than ~2 min
```

⭐ This is the clearest possible argument for the ack-names-what-was-verified rule we added this
morning: **we accepted that guidance without a way to test it, and it was wrong within hours.**
Recorded in `GV-12`, which is the row that would have consumed it.

### Three rows filed on OUR side, from defects they found while debugging theirs

- **`GV-12`** — the phone surface never retries. After service was restored at 15:31:17, `radio-web`
  made **zero** further GV calls, confirmed from both ends; the UI sat on *"Couldn't load…"* against a
  healthy backend until the owner tapped Retry. **An 83-minute outage leaves our phone surface
  permanently dead until a human intervenes.**
- **`UI-10`** — our Blazor circuit times out every ~30 s (`Server timeout (30000.00ms)`), continuing
  *after* their fix, so it is a standing condition. **A dead circuit cannot refetch**, so it may be
  upstream of `GV-12`.
- **`PHN-7`** — `SystemStatus` means two different things by transport, and **we poll the one that
  lies**: over REST, `Ht801LastCheckedUtc` is `DateTime.UtcNow` and the reachability is an ICMP ping of
  the **configured** address. ⭐ Their own doc says that ping *"reported the CORRECT address throughout
  the entire 2026-07 outage while every INVITE went to a stale one"* — **so our predictive-degrade
  would not have fired during the incident it exists to prevent.**

### Status changes

- **`XR-2` — CLOSED ON EVIDENCE.** Retested in production against the live box with our own July thread
  id; returns messages, not `[]`. Two group threads resolve.
- **`XR-5` — our row is STALE BY SIX WEEKS.** All five REQUIRED items shipped 2026-07-29 and are live.
  Our row still says *"the request file has never been filed."* Correct status: **delivered, build
  against it** — subject to `PHN-7` above.
- **Item 4 — REAL, and worse than filed. We were right to press.** It is **config-vs-config**, not
  config-vs-live: `appsettings.json` and `appsettings.Production.json` carry two *different*
  `GvPhoneNumber` values, Production wins, and one has been silently dead throughout with nothing
  validating either. Scope is narrow — one call site, `GvSipCredentialProvider.cs:113`, the SIP
  credential path — so it did **not** cause their outage or our 502s. ⚠ Values withheld: **both repos
  are public.**
- **Item 8 / `KIOSK-2` — exit code DECIDED, stays 0.** Measured: `systemd-run` returns as soon as the
  unit is *enqueued*, so Chrome crashing, a corrupt profile, no Wayland display, an OOM kill and an
  unauthenticated session **all exit 0**. Propagating it would report success through essentially every
  real outage. **We are not binding to it** — liveness via
  `pgrep -f "user-data-dir=$HOME/.config/gv-bridge-chrome"` or `/api/gvbridge/status`.

### Their incident, for our records

Their GV bridge was dead 14:08–15:31 EDT. Chrome's Google Voice session had been **dead since Sep 6**;
the service flew for two days on a self-regenerating credential chain, and their 14:01 deploy put an
8-minute gap in a chain that must be unbroken — the credential died at 8m03s against a first refresh
scheduled for 8m00s. **Missed by 52 seconds.** Not a regression from the deploy; the restart exposed a
latent condition. ⭐ **Our own nightly restart would have found it.**

⚠ **One hypothesis they flag as unproven**: their service may be *cannibalizing the browser session it
depends on for bootstrap* — two PSIDTS rotators competing on one session, theirs winning, Chrome's
starving. If it holds, the just-restored session dies again on the same clock. They are watching to
falsify it and will report either way, *"because it changes how much you should trust our uptime."*
**Do not treat their uptime as settled until that resolves.**

---

---

## ✅ INBOUND REPLY RECEIVED — 2026-09-08, acknowledged

**Ref: [`inbound/2026-09-08-rotaryphone-reply.md`](inbound/2026-09-08-rotaryphone-reply.md).** Delivered
directly into this repo by the RotaryPhone session. **This is the acknowledgement they asked for**, so
"not delivered" can be told apart from "delivered and not actioned".

⭐ **Every claim in it that I could check independently, I checked. All of them held.** That is recorded
here because the ack is worth more when it says *what was verified* than when it says *received*.

| Their claim | How I checked it | Result |
|---|---|---|
| Post-fix GVBridge build is deployed | `strings /opt/rotary-phone/RotaryPhoneController.GVBridge.dll \| grep -c DecodeThreadId` | **1** ✅ |
| …and a second, independent confirmation | `strings -el … \| grep -c 'resolved to 0 messages'` (UTF-16 literal) | **1** ✅ |
| Binary predates nothing relevant | `ls -l` on the DLL | **2026-08-01 19:44** ✅ |
| `9224` is listening | `ss -ltnp \| grep 922` | **listening**, pid 3128 ✅ (9223 = our kiosk) |
| `rp-deploy` is an orphaned worktree | `cat /d/prj/rp-deploy/.git` | 52-byte pointer to `D:/prj/RotaryPhone/.git/worktrees/rp-deploy` ✅ |
| …and that worktree is gone | `ls -d` on that path | **ABSENT** ✅ |
| `rp-deploy` is not the deployed tree | `grep -rc DecodeThreadId` in its `.cs` | **0 occurrences** ✅ |

### Status changes on this board

- **Item 1 (`XR-4`, CDP spam) — CLOSED, and the symptom was checked, not assumed.** They verified the
  *port*; they explicitly asked us to re-check our *journal*, because a live listener does not prove
  our spam stopped. **It has stopped.** Today's log (`radio-20260908.txt`, 00:00→11:55, 9,566 lines)
  contains **zero** genuine references to 9224 — the single grep hit is `15000.9224ms`, a duration, not
  a port — and `journalctl -u radio-api -u radio-web --since '-2h'` matches **0**. Root cause gone and
  symptom gone.
- **Item 2 — ⚠ the "✅ SETTLED" claim is WITHDRAWN.** *"The deployed tree is `D:\prjp-deploy`, NOT
  `D:\prj\RotaryPhone`"* is **false**, verified above. `rp-deploy` is an orphaned worktree of
  `D:\prj\RotaryPhone` whose `.git` points at a directory that no longer exists, which is why it
  looked like an independent checkout. **ADR-028 was NOT derived from the wrong tree.**
- **Item 5 (`XR-2`, `%2F` thread ids) — STALE.** Fixed `3103662` 2026-07-31 22:18, deployed 2026-08-01.
  Our reproduction was accurate and was superseded ~7 hours later. **Retest rather than re-file, and
  keep sending exactly what we send today** — single `Uri.EscapeDataString`. We already proved
  double-escaping and a raw `/` are both worse.
- **Item 6 (`XR-3`, auth blackout) — STALE.** Fixed and deployed 2026-08-01, PR #72.
- **Item 7 (uncommitted Change Log rows) — DONE**, committed `ec79a1c` 2026-08-11. ⚠ Our underlying
  point survives and got *worse*: it was the **third** consecutive miss of that protocol, not the second.
- **Item 9 (`XR-6`, `GetAudio` 404) — FIXED, NOT YET DEPLOYED.** RotaryPhone PR #76, merged `3c2c892`;
  the box still runs the 2026-08-01 build of `738141f`. ⛔ **Do not close until they confirm the deploy.**
  Also **drop the "~45% of the time / ~9 minutes in every 20" figure** — it predates PR #72's
  recover-and-retry. Measured over a 90-minute soak: one blackout of **920 ms**, and **zero**
  `authBlackout:true` samples in **411** polls.

### ⛔ Their ask #1 is declined, and the reason is on our side

They ask us to **unblock `GV-5`** because the `rp-deploy` premise is false. **The premise is indeed
false — and `GV-5` still must not be unblocked.**

Their reading of our board is stale in the other direction. Item 2 above says `GV-5` is 🔒 *blocked
pending ADR-028 re-derivation*; that was true on 2026-07-31 and was **superseded on 2026-09-05 by owner
decision `D31`**, which parks it for a different and stronger reason: the owner was asked whether SMS
sending is ever meant to be enabled and answered **no — replies stay off**. The row's own value
statement is what retires it — it was *"the row that unblocks ever turning send on"*, and `D31` says
send is never turned on. Its status is 🚫 **PARKED — never claim**, not 🔒.

⚠ **So removing the `rp-deploy` blocker changes nothing about `GV-5`.** ADR-028 and the plan are kept
as the reconstruction path if `D31` is ever reversed.

⭐ **The symmetry is the finding: both boards were stale about the other side's state, and ours was
also stale about our own.** An ack protocol fixes the first. Only re-reading our own rows fixes the second.

### ⚠ One item they did not answer

**Item 4 — the configured `GvPhoneNumber` does not match the live Google Voice session.** It appears
nowhere in their reply and is not in their six-item table. It is still open as far as this board knows.
Re-raised in our outbound reply.

### Protocol — both proposals adopted

1. **Ack every reply on the board.** Adopted; this section is the first one. ⭐ **We suggest one
   addition: the ack should name what was independently verified**, not merely that a reply arrived. An
   unverified ack propagates the other side's premises as readily as silence loses them — which is
   exactly how item 2 sat "✅ SETTLED" and false for six weeks.
2. **Deliver outbound replies into this repo.** Adopted. **Put them in
   [`docs/queue/inbound/`](inbound/)**, named `<date>-rotaryphone-<slug>.md` — adjacent to the board
   they correct, and out of the row-dossier namespace. Today's has been moved there.

---

> Moved verbatim from [`../BUILDER_QUEUE.md`](../BUILDER_QUEUE.md) on 2026-09-06. These live in the RotaryPhone repo and are not Radio Console queue rows.
> Nothing below was edited; this file's H1 is the section's own heading, promoted.

> These live in `D:\prj\RotaryPhone` and are **not** Radio Console queue rows. Routed via the documented protocol (boundary doc § "Passing Work Between Sessions" → create a file in `D:\prj\RotaryPhone\docs\prompts\`). Request files: **`D:/prj/RotaryPhone/docs/prompts/radioconsole-cdp-spam-and-build-stamp-request.md`** (items 1-2) and **`D:/prj/RotaryPhone/docs/prompts/radioconsole-gv-threadid-decode-and-auth-blackout-request.md`** (items 5-6).
>
> The boundary doc's Change Log was deliberately **not** touched — it is scoped to BT/audio ownership, and neither item is a BT/audio boundary change. The prompts channel is the right lane.

1. **`CdpCookieExtractor` log spam — `CDP: Cannot reach Chrome on port 9224` every ~20 min with a ~20-line stack trace.** Non-fatal (cookies stay valid via another path), but per project memory heavy journald churn on the Intel N100 **competes with the audio pipeline and correlates with audio distortion** — so this is a performance issue wearing a log-noise costume. **Concrete finding to hand them:** `GVBridgeConfig.ChromeCdpPort` defaults to **9224** (`GVBridgeConfig.cs:23`) and the extractor expects a Chrome with a live Google Voice tab; the only Chrome our deploy launches on that box is the **kiosk on port 9223** pointed at `http://localhost:5002` (`Deploy-ToLinux.ps1:363`) — a different instance for a different purpose. So nothing is listening on 9224 unless someone separately starts a GV-session Chrome. **May be absorbed into a larger bug** — a Tester is investigating whether this same CDP failure explains GV read endpoints returning **empty lists while reporting healthy**; see the request file for the causal chain.
2. **Build stamp for `rotary-phone`.** The 2026-07-29 incident (stale `rotary-phone` binary after a deploy restarted only `radio-api`/`radio-web`) is the same class of failure OPS-1 fixes on our side. Recommended: mirror our approach — `SourceRevisionId` → `AssemblyInformationalVersion` → a `/version` endpoint → deploy-time verification. ~~**Open question:** which tree is authoritative.~~ **✅ SETTLED 2026-07-31 — the deployed tree is `D:\prj\rp-deploy` @ `0a86898`, NOT `D:\prj\RotaryPhone`.** Confirmed on the live box by a Tester. This has real blast radius on our side: **ADR-028 was derived by reading `D:\prj\RotaryPhone`**, so GV-5 is now 🔒 blocked pending re-derivation. It also raises the priority of the stamp — source-tree parity is not deployed-binary parity, and until a stamp exists nothing proves the running binary came from `0a86898` either.

3. ~~**GV read has NEVER worked — positional parser defect (`rp-deploy`).**~~ ✅ **STALE — CORRECTED 2026-09-01 (`XR-1a`). Do NOT file a request for this; there is no cross-repo defect here.** The owner, answering D17: *"I can see voicemail and text messages in the Radio Console UI today. I can listen to the voicemail and read the texts on the screen."* **GV read works.** The original finding — that `PositionalGvThreadParser.ThreadsArray()` required a JSON object root while `alt=protojson` returns arrays, yielding 0 items behind a clean HTTP 200 — was accurate when written on 2026-07-31 and has since been fixed upstream. It is kept here struck through rather than deleted because the *shape* of the bug is worth remembering: `Succeeded: true` meant only "the JSON parsed", never "data was returned", so the failure was invisible to every caller. ⚠ Left uncorrected, this entry would send the next session chasing a defect that no longer exists, in a repo it does not own.

4. **Configured GV number does not match the live session.** The `GvPhoneNumber` value in RotaryPhone's config is **a different number** from the one the owner's live Google Voice session is actually bound to. _(Both values are deliberately **not recorded here** — this is a public repo. The configured value is in the box's `GVBridge` config; the live value is visible in the Google Voice session. Compare them there.)_ Independent of the parser defect — and note that **nothing validates configured identity against the live session**, so a mismatch fails silently. Two distinct faults could each present as "empty lists, healthy status," so this should be corrected *before* the parser fix is evaluated, or the fix will be assessed against a confounded baseline.

5. **Thread ids containing `/` are never decoded → HTTP 200 with `messages: []`; every group/MMS conversation is permanently unreadable.** **Confirmed by direct reproduction** in a verified-healthy window, 2026-07-31. We call with `Uri.EscapeDataString(threadId)`; **Kestrel deliberately leaves `%2F` encoded in the path** so it cannot forge a segment boundary, so their route value keeps the literal `%2F` while `%20` decodes normally — visible in their own log line: `Listed 0 SMS for thread g.Group Message.d5Mri%2FNrDUQgXNXNQehOfw (of 149 parsed)`. `GvSmsClient.ListMessagesAsync` then exact-string-compares (`all.Where(m => m.ThreadId == threadId)`), matches nothing, and `GvSmsController.GetThreadMessages` returns **200 + empty**. **Framing correction worth carrying: the predicate is "the thread id contains `/`", NOT "the thread is MMS."** GV group threads are `g.Group Message.<base64url>` and the base64url alphabet includes `/`; group threads merely *happen* to be the MMS threads, which is why the symptom looked MMS-shaped in the UAT. **Ask:** decode the route value in **both** `GetThreadMessages` **and** `MarkThreadRead` (`Uri.UnescapeDataString`), or move the id to a query parameter — **plus a per-thread sanity check**, because *their existing honest-status guards structurally cannot catch this*: `ShapeIsSane` and the `Succeeded` flag both pass, since the fetch and the parse genuinely succeeded and only the **filter** matched nothing. **We cannot fix it here:** double-escaping (`%252F`) still yields 0 messages, and a raw `/` misses the API route entirely and falls through to their SPA fallback, returning `index.html` with HTTP 200. **Ask them to do this one first** — it is cheap, and until it lands group conversations stay unreadable even in a perfectly healthy window, which also confounds any test of item 6.

6. **GV PSIDTS staleness → a deterministic ~9-minute auth blackout every 20 minutes.** **Confirmed from server logs**, 2026-07-31: `api2thread/list returned Unauthorized for folder Sms` **271 times in one day**, in a clean 20-minute square wave, surfacing to us as **HTTP 502**. Mechanism captured at a boundary: a `CDP cookie refresh` at 15:00:02 restores service and the next `Unauthorized` lands **11m42s later** — **Google's PSIDTS is good for ~11 minutes, their refresh fires every ~20, and there is no reactive refresh on 401.** **11 of 11** of our 502s fall inside a dead window. **Throttling is FALSIFIED three ways** — this was the UAT's own first guess, so it is worth stating plainly: our constant-rate 60-second poller shows the identical on/off pattern, so **failure tracks wall-clock, not request volume**; the status is **401, never 429**; and recovery lands on fixed 20-minute boundaries rather than after a variable cooldown. **Concrete lead to hand them:** deployed `appsettings*.json` declares **`CookieRefreshIntervalMinutes: 5`** (matching the default at `GVBridgeConfig.cs:23`) while the **observed** cadence is 20 minutes — so either the value is not being read or something downstream sets its own interval; a genuine 5-minute refresh would make most of this disappear. **Ask:** align the interval with the ~11-minute observed lifetime, add **refresh-and-retry on the first 401** (a purely time-based fix stays fragile if Google shortens the lifetime), and **make `/api/gvbridge/status` honest** — measured at 15:13:03 while both SMS endpoints were returning 502, it still reported `{"available":true,"cookiesValid":true,"degraded":false}`, which is why our **"Google Voice is reconnecting"** banner (`PhoneMessagesPanel.razor:14-20`) never fires during the exact window it exists for. _Same lesson as `Succeeded` meaning only "the JSON parsed": a health field derived from a probe rather than from "did the last real call return data" will report healthy straight through an outage._ **Our GV-8 does not wait on this** — theirs makes the failure rare, ours makes it honest.

7. **Two boundary-doc `Change Log` entries are sitting UNCOMMITTED in the RotaryPhone repo — including the one that established the rule they are both breaking.** Verified 2026-08-10: `D:\prj\RotaryPhone` is on branch **`diag/gv-srtp-receive`** with `docs/prompts/RADIO-CONSOLE-BT-AUDIO-BOUNDARY.md` modified, **1 file / +2 lines**, both additions to the Change Log table. **Deliberately NOT a Radio Console queue row** — there is nothing to build here; the fix is a commit in the other repo, per that doc's own § "Passing Work Between Sessions." **(a) The 2026-08-10 entry — a shared-system change that reaches RotaryPhone.** It records that **`systemd-coredump` (255.4-1ubuntu8.16) now handles core dumps machine-wide** on `192.168.86.50`, replacing `apport`. The motivation was ours: `core_pattern` piped to apport, which retains exactly **one** `.crash` per executable, so **two `radio-api` SIGABRTs on 2026-08-10 (16:16 and 16:42) produced a single file — the second silently overwrote the first.** `apport-core-dump-handler` was APT-removed to satisfy apport's alternative dependency; the `apport` package itself remains. **Consequence for RotaryPhone: their crashes now land in `coredumpctl` / `/var/lib/systemd/coredump/` (zstd-compressed) instead of `/var/crash/*.crash`** — strictly more forensic data, not less (`coredumpctl list` / `info <exe>` / `debug <exe>`). Retention is bounded in `/etc/systemd/coredump.conf` (`Compress=yes`, `ProcessSizeMax=2G`, `ExternalSizeMax=2G`, `MaxUse=2G`, `KeepFree=10G`). **Action required of them: none** — unless some RotaryPhone tooling greps `/var/crash/`, in which case switch it to `coredumpctl`. Verified at install time that `rotary-phone.service` stayed `active (running)` with MainPID unchanged and `NRestarts=0`. **(b) ⚠ The 2026-07-16 entry is uncommitted TOO — and it is the precedent.** That entry (the IAC "save" that relocated the boundary-owned WirePlumber rules into `deploy/common/`) is the one that wrote down *"this Change Log edit lives in the RotaryPhone repo and must be committed there separately — it is NOT part of the Radio Console PR."* **It was never itself committed.** So this is not one missed commit but the **second consecutive miss of the protocol the entry documents**, which is the part worth acting on: the boundary doc is the cross-service contract, and an uncommitted contract is invisible to the other session by construction. **Ask:** commit both rows in `D:\prj\RotaryPhone` (they belong on that repo's own branch/PR flow, not ours), and consider whether the "commit separately" step needs to become a checklist item in § "Passing Work Between Sessions" rather than a note inside individual rows.

8. **Record Radio Console's kiosk launcher as a SECOND CONSUMER of `gv-bridge-ensure.sh`.** `KIOSK-2` adds a caller that depends on that script's **path** and its **exit code** — it runs it to repair a down bridge and reads the result to decide whether the `VOICE` row is amber. It does **not** own, reimplement, edit, install or `pkill` any part of bridge startup; the watchdog timer and the nightly restart timer stay entirely RotaryPhone's. **Why this matters now rather than later: a Builder in `D:\prj\RotaryPhone` is bringing that script and its timers under version control as this is written**, and a relocation would silently break the new consumer. `KIOSK-2` therefore resolves the script from a candidate list (`~/bin/` → `/usr/local/bin/` → `/opt/rotary-phone/bin/`) rather than hard-coding one path, so a move degrades to a reported failure instead of a wrong one. **The ask is one Change Log entry** in `RADIO-CONSOLE-BT-AUDIO-BOUNDARY.md` naming the new consumer and pinning the contract as *path + exit code*. Per the 2026-08-10 precedent this is a commit in the other repo, so it is not claimable here — **and note that entry also recorded two boundary-doc Change Log edits sitting uncommitted in that repo, one of which established the very rule they were breaking, so this is a recurring miss of the protocol rather than a one-off.** Also worth passing along, already handled on the box today and needing no work from us: `gv-bridge-ensure.sh` now carries `--remote-debugging-port=9224 --remote-allow-origins=*`, whose absence was breaking cookie extraction and producing empty SMS/voicemail lists.

9. **`GetAudio` answers `404` during a GV auth blackout, because `FindNodeAsync` never checks `Succeeded` — filed as punch-list `XR-6`, 2026-09-03. This is the SECOND unfiled cross-repo request** (see `XR-5`, and note that both were found by reading their source rather than their docs). **Verified directly in `D:/prj/RotaryPhone/src/RotaryPhoneController.GVBridge/Api/GvVoicemailController.cs`:** `GetList` guards the list result at `:45-46` — *"Do not mask an auth/transport failure as 'no voicemails' — RadioConsole cannot tell the difference from an empty 200"* — and returns **502**. Its sibling `FindNodeAsync` (`:123`) calls the same `ListVoicemailsAsync` and does **not** guard it; it just does `result.Items.FirstOrDefault(v => v.MessageId == id)`. During the `XR-3` blackout a failed authenticated list returns an **empty** item set, so the lookup yields null and `GetAudio` answers **`404 "has no recording"`** (`:65-66`). **Why it matters on our side:** `GvMediaUnavailableException.IsPermanent` (`src/Radio.Infrastructure/External/GvMediaUnavailableException.cs:73`) maps `NotFound` to *"retrying will not help"*, so we tell a guest a voicemail is permanently gone **~45% of the time** — the blackout is ~9 minutes in every 20. **Ask:** propagate `Succeeded` through `FindNodeAsync` so `GetAudio` answers `502` exactly as `GetList` already does; it is a three-line change against a guard they have already written once. **Paste-ready text:** `design/plans/PHN-1c-event-playback-service-and-route.md` §5 item 2. **Route it the documented way** — a file under `D:\prj\RotaryPhone\docs\prompts\`, per the boundary doc's § "Passing Work Between Sessions"; the boundary doc's own Change Log is **not** the lane, since this is not a BT/audio ownership change. ⚠ **Our side is correct without it** — `PHN-1c` Task 10 narrows `IsPermanent` to `Disabled`-only, which stops us lying; only their fix makes `MediaNotFound` mean what it says.

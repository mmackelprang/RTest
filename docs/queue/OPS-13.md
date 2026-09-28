# `OPS-13` — a failed SHA verification exits the deploy before the kiosk relaunch, so a dev-host DNS miss turns a SUCCESSFUL deploy into a dark panel

[← Builder Queue index](../BUILDER_QUEUE.md)

🟠 **P1.** Filed 2026-09-28 from the first deploy run from a Linux dev host (`appserver`), the
one that shipped [PR #677](https://github.com/mmackelprang/RTest/pull/677) → `f409bb9`. Recovery
is one command and took about a minute, which is why this is not P0 — but the panel was dark
after a deploy that had, in fact, succeeded.

## What happened

Steps 1–3 completed: build, rsync transport, remote move, Production configs left alone, five
WirePlumber rules installed. Step 4 started both services. Then:

```
[4/4] Starting services...
  Verifying deployed commit via /api/health/version...
=== DEPLOY VERIFICATION FAILED ===
  Could not reach http://radio:5000/api/health/version after 10 attempts.
deploy exit=1
```

Read by IP from the same host, thirty seconds later:

| | |
|---|---|
| `http://192.168.86.50:5000/api/health/version` | `f409bb9` — the commit just built |
| `http://192.168.86.50:5002/api/health/version` | `f409bb9` |
| `systemctl is-active radio-api radio-web` | `active active` |
| `systemctl --user is-active radio-kiosk.service` | **`inactive`** |
| established connections to `:5002` | **0** |

**The deploy was correct and the verification was wrong.** The panel stayed dark until
`/usr/local/bin/radio-kiosk-launch` was run by hand.

## Two defects, and they compound

### 1. The verification resolves the target differently from every other step

Every `ssh`/`scp`/`rsync` call in the script goes through OpenSSH, which consults `~/.ssh/config`.
On `appserver` the `Host radio` block pins `HostName 192.168.86.50` because the bare name `radio`
does not resolve there (the router answers `radio.local` over mDNS only). So ssh worked all the way
through steps 2–4.

The verification does not go through ssh. `Deploy-ToLinux.ps1:472` and `:508` build
`http://${TargetHost}:${ApiPort}/api/health/version` and hand it to `Invoke-RestMethod`, which asks
the OS resolver — and on this host the OS resolver has never heard of `radio`. Ten attempts, ten
`NameResolutionFailure`s, `exit 1`.

The script already has a form of this check that does **not** depend on the dev host's DNS:
`:461` polls the negotiate endpoint *on the box* via `ssh $SshTarget "... curl -sf ... http://localhost:5000/..."`.
The SHA verification could ask the same way and the question would have the same answer on every
dev host.

### 2. The kiosk relaunch is gated behind the verification

Step 2 (`:179`) stops the kiosk via `radio-kiosk-exit` **unconditionally** (unless `-NoRestart`).
The relaunch (`:561`) lives inside the success branch of step 4, *after* both SHA checks. Any
`exit 1` between `:489` and `:536` — a verification that could not be reached, or one that reached
the box and disagreed — leaves the kiosk stopped.

That is a defensible choice when the SHA genuinely mismatches: nobody wants the panel showing a
binary that is not the one just built. It is the wrong outcome when the *instrument* failed, which
is what happened here. The script cannot currently tell the two apart, so it treats both as "do not
relaunch".

⭐ **This is a false-negative gate, which is worse than a false-positive one for this workflow.**
CLAUDE.md § *Read the deployed SHA BEFORE a human runs UAT* is about a deploy that *claims* success
and did not happen. This is the inverse: a deploy that *claims* failure and did happen — and the
claim is believed at exactly the moment an operator is deciding whether to trust the box.

## Recommended shape

Decide before building; the two halves are independent.

1. **Verify from the box, not from the dev host.** Replace the two `Invoke-RestMethod` polls with an
   `ssh $SshTarget "curl -s http://localhost:5000/api/health/version"` (and `:5002`) and parse the
   JSON locally. That removes the DNS dependency, matches how `:461` already works, and keeps the
   check honest — the SHA is still read from the running process. The `$TargetHost` URLs printed at
   the end (`:605-606`) are informational and can stay.
2. **Relaunch the kiosk on every path that stopped it, unless the SHA was reached AND mismatched.**
   Concretely: an unreachable endpoint after ten attempts should still relaunch (and still `exit 1`,
   loudly); only a reached-and-wrong SHA should leave the kiosk down. If that distinction is judged
   too subtle, the simpler rule "always relaunch what step 2 stopped" is also acceptable — a wrong
   SHA on the panel is visible and recoverable; a dark panel is neither.

⚠ **Do not "fix" this by pinning an IP in the docs.** `ssh mmack@radio` is the documented form for
a reason (CLAUDE.md § *Reaching the box*, and the identity is hostname-bound). The per-host
workaround is an `/etc/hosts` entry on the dev machine, and it belongs in the dev-box notes, not the
script.

## Relationship to other rows

- **`OPS-12`** — the transport ordering defect (services stop before the transport is proven) is
  the same shape one step earlier: an outage produced by *sequence*, not by a broken binary. Fixing
  either without the other leaves a dark-panel path in the script.
- **`OPS-10`** — readiness. Not a dependency; a verification that asks the box over ssh works with
  or without a readiness signal.

⚠ **Not auto-mergeable.** Production deploy path. Validate the verification change with
`-NoRestart` against `radio` (it skips step 2 and step 4 entirely, so it cannot darken the panel),
then one supervised full deploy.

## BUILT 2026-09-28 — `fix/ops-13-ops-12-deploy-safety` (with `OPS-12`)

Both halves of the recommended shape, plus the instrument on its own:

1. **Verify from the box.** `Get-DeployedSha` runs `curl -s -m 3 http://localhost:<port>/api/health/version`
   over the same ssh every other step uses and parses the JSON locally; ten attempts, two seconds
   apart. `Invoke-RestMethod` and the `http://$TargetHost:…` URLs are gone from the verification.
2. **Relaunch on every path that stopped the kiosk, except reached-and-mismatched.** Step 4 now
   produces one of three verdicts — `Verified` (relaunch, exit 0), `Unreachable` (relaunch, exit 1),
   `Mismatch` (kiosk left down, exit 1) — and the "services not active" branch relaunches before its
   exit 1 too. `Invoke-KioskRelaunch` / `Get-KioskLiveness` carry the 2026-08-18 Wayland and
   2026-08-02 liveness reasoning verbatim.
3. **`-VerifyOnly`.** No build, no stop, no sync: both SHAs read on the box against local HEAD, kiosk
   liveness, exit 0 only on a match.

**Measured on the Linux dev box, `main` at `89b5b12` with the box running `f409bb9`:**

| Run | Result |
|---|---|
| `-VerifyOnly` | `API (:5000): running f409bb9 - MISMATCH`, same for Web, `Kiosk: 4 established connections … radio-kiosk.service=active`, **exit 1** — correct, `main` was four docs/test commits ahead of the box |
| `-NoRestart` (auto → rsync) | `[1.5/4] Pre-flight (rsync - auto: non-Windows host with rsync on PATH)… Pre-flight OK`, both syncs, `WirePlumber rules up to date`, exit 0, services untouched |

⚠ The full path — a real deploy exercising the `Verified` verdict and the relaunch — is the
supervised deploy at merge time, per the row's own instruction. The `Unreachable` and `Mismatch`
branches were not driven live; they are read-verified and the row should say so until one occurs.

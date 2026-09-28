# `OPS-12` — the rsync branch of `Deploy-ToLinux.ps1` has never executed, and the first attempt took the appliance down

[← Builder Queue index](../BUILDER_QUEUE.md)

🔴 **P0.** Filed 2026-09-09 after the first-ever execution of the rsync path failed and left
`radio-api` and `radio-web` stopped.

## What happened

`Deploy-ToLinux.ps1` chooses its transport at `:178-185`:

```powershell
$useRsync = $false
try { $null = Get-Command rsync -ErrorAction Stop; $useRsync = $true } catch { }
```

**rsync had never been on PATH on this workstation**, so every successful deploy in this project's
history has taken the **scp fallback**. An `rsync.cmd` shim was added to `C:\Users\mark\bin\` on
2026-09-09 (⛔ **NOT "for an unrelated purpose" — see the CORRECTION at the foot of this file; it was an OWNER DECISION for RotaryPhone deploy safety**). The next deploy silently selected a branch that had **never
run once**, and it failed:

```
[2/4] Stopping services and kiosk browser...
[3/4] Syncing files...
  Syncing API...
The source and destination cannot both be remote.
rsync error: syntax or usage error (code 1) at main.c(1471) [Receiver=3.5.0]
API sync failed!
```

⛔ **Step 2 stops the services. Step 3 failed. The console was dark until restarted by hand.**

## ⚠ THREE stacked defects, not one — fixing only the first causes a SECOND outage

Measured directly with `/c/msys64/usr/bin/rsync.exe`, dry-run, against the real target:

| # | Form | Result |
|---|---|---|
| 1 | `'D:\...\api/'` → `mmack@radio:/tmp/...` | ⛔ `The source and destination cannot both be remote` — MSYS2 rsync parses `D:` as a **hostname** (`host:path`). **The script does NO path conversion**; `:220` and `:250` pass `${ApiPublishDir}/` raw. |
| 2 | `'/d/.../api/'` → same | ⛔ `dup() in/out/err failed` (code 12) — MSYS2 rsync **cannot drive the native Windows OpenSSH** `ssh.exe`. |
| 3 | same, plus `-e /usr/bin/ssh` | ⛔ `Host key verification failed` (code 12) — MSYS2 ssh has its **own** `known_hosts`/`config`/identity, and the `Host radio` block with `IdentityFile ~/.ssh/id_ed25519_radio` is not visible to it. |

⭐ **Each fix reveals the next.** A Builder who fixes only the path conversion will re-run the deploy,
stop the services, and fail at `dup()`. **Do not ship a partial fix — the failure path takes the
appliance down every time.**

## ⚠ Verify the transport WITHOUT stopping services

⛔ **Never validate this row by running the deploy.** Step 2 stops `radio-api`/`radio-web` before
step 3 is reached, so every failed attempt is an outage. Use `rsync -n` (dry run) against a scratch
remote path, exactly as the table above does, until the transfer parses and connects.

## Recommended shape

Two candidates; **decide before building**:

1. **Fix the branch** — convert `$ApiPublishDir`/`$WebPublishDir` to MSYS form, force `-e` to an ssh
   MSYS rsync can drive, and give that ssh the host key and identity. Three moving parts, all
   workstation-local state that no other machine will have.
2. **⭐ Remove the branch.** scp is the only transport that has ever worked here, and the rsync path
   has zero successful executions. Deleting it removes a trap rather than arming it better.

⚠ **Whatever is chosen, the transport must be verified before step 2 stops anything.** A pre-flight
check that proves the chosen transport can reach the target belongs *ahead* of the service stop —
that is the defect that turned a sync error into an outage, and it is independent of rsync.

## ⭐ The general lesson

**Installing a tool armed an untested code path.** The shim was added for an unrelated task; nothing
announced that it also changed how the appliance is deployed. A capability probe (`Get-Command`) that
silently switches transports is a landmine on any box where the alternative has never been exercised.

## Current mitigation (in place, not a fix)

`C:\Users\mark\bin\rsync.cmd` renamed to `rsync.cmd.disabled-broken-transport`, restoring the scp
path. ⚠ **This is workstation-local and invisible to the repo** — anyone who installs rsync by any
route re-arms the failure.

## ⛔ CORRECTION 2026-09-09 — "installed for an unrelated purpose" is FALSE, and the mitigation is CROSS-REPO

**Two claims above are wrong and are corrected here rather than edited away.**

**1. The shim was not unrelated.** Installing rsync and adding `C:\Users\mark\bin` to the persistent
user PATH was an **owner decision**, taken so deploys would use rsync **instead of the tar-pipe
fallback** — **for the RotaryPhone repo**, on the reasoning that rsync excludes
`appsettings.Production.json` at the sync level rather than depending on a backup/restore wrapped
around a failing `tar`. It was a deploy-safety decision for the *other* service on the same box.

**2. The mitigation is workstation-GLOBAL, not workstation-local.** `C:\Users\mark\bin` is on the
persistent user PATH (verified: `C:\msys64\usr\bin` is **not** on it, and `mingw64\bin` has no
`rsync.exe`), so renaming the shim disables rsync discovery for **both repos in a fresh shell** —
**reversing an owner decision on RotaryPhone's behalf without their involvement.**

### ⭐ But the decision was never achievable, in either repo

**Defects 2 and 3 are transport-level, not RTest-specific.** ⚠ The defect-2 measurement used **no
`-e` flag, and rsync's default remote shell IS `ssh`** — so it is the exact equivalent of
RotaryPhone's bare `-e ssh`. **MSYS2 rsync cannot reach `radio` from this workstation from either
repo.**

| Repo | What the shim actually changed |
|---|---|
| RotaryPhone | `tar` path → **failed rsync, then `tar` path** (it falls back). Same outcome, plus noise. |
| RTest | scp path → **outage** (no fallback). |

⛔ **So the rsync install bought nobody the safe path it was installed for**, and nobody knew until an
outage forced the measurement. A recorded belief — *"`Deploy-ToLinux.ps1` will take the safe rsync
sync path on next execution"* — was **true of the PATH and false of the transport**, and nothing
revisited it.

### ⚠ Consequence that must not be lost

**RotaryPhone's next deploy takes the `tar` path**, which carries a **silent stale-deploy bug**: the
remote chain is `;`-separated and ends in `chmod`, so `ssh` returns chmod's status and a failed `tar`
reports success. Their PR **#84** fixes it with `set -e`. **They have been told.** ⭐ **Their failure
is quieter than ours and therefore worse to have** — ours took the appliance down and said so.

### The decision this leaves open

**Restoring the shim re-arms this row's outage; leaving it disabled reverses an owner decision.**
That is the owner's call, not a Builder's. Recommendation: **leave it disabled** — it delivers
nothing to either repo as things stand — and treat "make MSYS2 rsync actually reach the box
(identity + `known_hosts` + a drivable ssh)" as a **separate, explicit** piece of work if the safe
sync path is still wanted.

## Update 2026-09-28 — the rsync branch has now executed successfully, from a Linux host

Filed by the session that shipped [PR #677](https://github.com/mmackelprang/RTest/pull/677)
(`Deploy-ToLinux.ps1` made runnable from a Linux `pwsh`). Measured on `appserver` (Ubuntu 24.04,
native rsync 3.5.0 / OpenSSH, one `~/.ssh` for both):

1. **Dry run first, per this row's instruction:** `rsync -n -avz --delete deploy/common/
   mmack@radio:/tmp/rsync-preflight-scratch/` → exit 0, `(DRY RUN)`, scratch path never created,
   services untouched.
2. **Then a full deploy took the rsync branch** — `Get-Command rsync` found `/usr/bin/rsync` — and
   steps 1–3 completed: both publish dirs transferred, remote move OK, `f409bb9` verified on both
   services by IP. ⭐ **This is the branch's first successful execution in project history.**
   (Step 4's verification then failed for an unrelated reason — see `OPS-13`.)

**What that changes about this row:**

- The three stacked defects in the table above are **all artifacts of the Windows/MSYS2 toolchain**
  (`D:` as hostname; MSYS rsync vs. native Windows OpenSSH; MSYS ssh's separate `known_hosts`).
  None exists on a Linux host. So the branch is not *broken*; it is **broken on Windows** and has
  never had a working transport there.
- ⭐ **"Strongly consider deleting the branch" should be re-weighed.** On a Linux dev host rsync is
  the transport that works and is faster; deleting it would push Linux deploys onto the scp
  fallback whose `mv` exit codes are the ones the comment block at `:213-217` says are still masked.
  A per-host answer — scp on Windows, rsync on Linux — is now a defensible shape, **provided the
  choice is made explicit** rather than by `Get-Command` silently arming whichever is installed.
- ⛔ **The independent defect stands and is now joined by a sibling.** Services stop at step 2
  before step 3 proves the transport (this row); the kiosk stops at step 2 and is only relaunched
  after step 4's verification succeeds (`OPS-13`). Both convert a *sequencing* failure into a dark
  console. Fix them together or the script keeps one outage path.

## BUILT 2026-09-28 — `fix/ops-13-ops-12-deploy-safety` (with `OPS-13`), per D-C: keep rsync, make it explicit

New **step 1.5**, between the build and the service stop:

- `-Transport rsync|scp|auto` (default `auto` = **scp on a Windows host**, **rsync on any other host
  with it on PATH**). The choice and its reason are printed. `Get-Command rsync` no longer selects a
  branch by itself; it only answers whether `-Transport rsync` / `auto` *can* use rsync.
- Pre-flight, in order: `ssh -o ConnectTimeout=10 $SshTarget true`; box prerequisites
  (`command -v rsync && command -v curl && sudo -n true` — the move into place uses `sudo rsync`
  whichever transport carried the bytes); then the transport itself — `rsync -n -az` of the real API
  publish dir to the real staging path, or a one-byte `scp` probe copied and removed. **Any failure
  exits 1 with "Nothing was stopped" and names the other transport to try.**
- Step 3 no longer probes; it reads the step-1.5 decision.

Measured on the Linux dev box (`-NoRestart`, so build + pre-flight + sync with services untouched):
auto → rsync, pre-flight OK, both syncs complete. **`-Transport scp -NoRestart`: `[1.5/4] Pre-flight
(scp - requested)… Pre-flight OK`, then the scp fallback synced both services end to end, exit 0,
probe file and `-tmp` staging dirs gone from the box afterwards.** ⭐ That is the first time the scp
branch has been driven against a real target from a script run, which `OPS-9` recorded as never
having happened (`design/FUTURE-WORK.md` §27). Its masked `mv` exit codes are unchanged by this row. ⚠ The Windows half of `auto` (scp) is by construction from this row's own
measurements, not re-measured on a Windows host today.

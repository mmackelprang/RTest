# PLAN — `OPS-7` · A deploy that can silently destroy a secret

> **Row:** `OPS-7`, P2, [`BUILDER_QUEUE_ARCHIVE.md`](../../docs/BUILDER_QUEUE_ARCHIVE.md).
> **Branch:** `fix/deploy-production-config-seed-guard`
> **Estimate:** **0.5 d**, as filed. The fix is nine lines; the estimate is verification.
> **Planned against** `main` at `603207df`. Every line number read out of the tree at that commit.

---

## 0. Why this is a file rather than a plan-in-the-row

The row says *"small enough for a plan-in-the-row if the owner prefers"*. **The fix is. The
verification is not.**

The obvious way to verify a deploy bug is to run the deploy and look — and that is precisely the
action that can destroy the secret being protected. The safe procedure is a scratch install path, a
sentinel file, a deliberate mutation, and a cleanup, with one trap in it that a careless run walks
straight into (§3.2). That is fifteen steps and a warning, and it does not survive being folded into
a table cell.

The fix itself is §2 and is nine lines.

---

## 1. What is actually wrong

### 1.1 The filed defect — confirmed exactly as written

`deploy/Deploy-ToLinux.ps1:219-228`, quoted verbatim:

```powershell
# Deploy target-specific Production config if not already present
$targetConfigPath = Join-Path $RepoRoot "deploy\$configDir\appsettings.Production.json"
if (Test-Path $targetConfigPath) {
  ssh $SshTarget "test -f $TargetPath/api/appsettings.Production.json" 2>$null
  if ($LASTEXITCODE -ne 0) {
    Write-Host "  Deploying Production config from deploy/$configDir/..." -ForegroundColor DarkGray
    scp $targetConfigPath "${SshTarget}:/tmp/appsettings.Production.json"
    ssh $SshTarget "sudo cp /tmp/appsettings.Production.json $TargetPath/api/ && sudo cp /tmp/appsettings.Production.json $TargetPath/web/ && sudo chown ${TargetUser}:${TargetUser} $TargetPath/api/appsettings.Production.json $TargetPath/web/appsettings.Production.json && rm /tmp/appsettings.Production.json"
  }
}
```

`:222` tests **one** file. `:226` writes **two**. The state that loses data is: `api/`'s overlay
absent, `web/`'s present. The guard sees the missing api file, decides "seed needed", and the copy
overwrites the web file with the api seed.

`src/Radio.Web`'s Production overlay is where `RotaryPhone:Gv:AuthKey` **belongs**
(`docs/HANDOFF-rotaryphone-gv-send-markread-auth.md:102`; the handler is
`src/Radio.Web/Services/Http/RotaryPhoneAuthHandler.cs`). The tracked seed at
`deploy/debian-x64/appsettings.Production.json` contains **no `RotaryPhone` section at all** —
verified by reading it — so the overwrite does not replace what is there with a different value. It
**deletes it**.

> ⚠ **CORRECTED AFTER THE FACT — this paragraph originally said the overlay is where that key
> "lives", and that `PHN-2` "made that key load-bearing". Both overstate the stakes.** Measured on
> `radio` 2026-09-02 (`design/INTEGRATIONS.md:994-997`), the web overlay is **75 bytes holding only
> `RotaryPhone:Gv:MarkReadEnabled`**, and *"Neither carries an auth key today"*;
> `docs/HANDOFF-...:100-106` records all three `Gv` flags as wired off, and `grep -c AuthKey`
> returns **0** on both deployed overlays. The key is not on any box, so nothing can delete it yet.
>
> **What the overwrite destroys today** is operator-authored config that the repo cannot
> reconstruct — `MarkReadEnabled` on `radio`, and whatever a given box's operator hand-placed. The
> `AuthKey` loss is **prospective**: real from the moment `PHN-2`'s gate is set, on a box
> mis-provisioned before then.

**`C-87` — WITHDRAWN. It said "the row's description is accurate in every particular"; it was not,
and this plan is the reason the error spread.** `:222`, `:226`, both destinations and the
dormant/live framing do check out. **The key did not.** The row asserted it, this plan ratified it
without measuring the deployed files, the dispatch brief repeated the plan, and `OPS-7`'s Builder
then transcribed it into a source comment — where a pre-merge reviewer finally caught it against
`INTEGRATIONS.md`. Fixing that comment corrected the **leaf**; this note is the **root**.

The lesson is specific and worth more than the correction: **`C-87` was a claim of verification that
performed none.** Every other bullet in §5.1 names a file that was opened; this one named the row.
A plan may not certify a row's factual claims by agreeing with them — checking the tree for `:222`
and `:226` says nothing about what a *deployed box* holds, and only the deployed box could have
falsified this. **The defect is real and the fix is unchanged; only the stakes were wrong.**

### 1.2 ⚠ `C-88` — a second defect in the same fourteen lines, not previously filed

`:230-233`:

```powershell
if ($LASTEXITCODE -ne 0) {
  Write-Host "Remote file move failed!" -ForegroundColor Red
  exit 1
}
```

That check is *positioned* to guard the remote file move at `:217`. It cannot. Trace `$LASTEXITCODE`
between `:217` and `:230`:

- `:221` `Test-Path` is a PowerShell cmdlet and does not set `$LASTEXITCODE`.
- `:222` `ssh … test -f` **does** — 0 when the file exists, 1 when it does not.
- `:226` `ssh … sudo cp …` **does**, when the inner branch runs.

`$configDir` resolves to `raspberry-pi` or `debian-x64` (`:64-67`), and **both directories contain an
`appsettings.Production.json`** — verified. So `Test-Path` at `:221` is **always** true for every
shipping runtime, `:222` always runs, and `$LASTEXITCODE` at `:230` is **never** the one `:217` left.

**A genuine failure of the remote file move is therefore always masked**, and the error message it
would print names the wrong step. It reports *"Remote file move failed!"* for what is now a
config-copy exit code. `CLAUDE.md` § *Pre-Merge Review* is specifically about log and comment text
asserting something stronger than the code does; this is that, in an error path, in a deploy script.

Fixed in §2 because it is inside the block being rewritten and leaving it would mean touching these
same lines twice.

### 1.3 `C-89` — is any other seed/copy pair the same shape? No. One instance.

Swept, with results stated so the negative is auditable:

| Candidate | Verdict |
|---|---|
| `Deploy-ToLinux.ps1:299-305` — the WirePlumber rule sync | **Correct already, and it is the exemplar.** `Sync-WpRule` (`:268-297`) is called once per file, and each call does its own `cmp` against its own destination before installing. One guard, one destination — exactly the shape §2 gives the config seed. **The fix's model is forty lines below the bug, in the same file.** |
| `Deploy-ToLinux.ps1:216-217` — the rsync move | Correct. `--exclude='appsettings.Production.json'` is applied to **both** the api and web rsync invocations, so the move preserves both overlays. The bug is only in the seed. |
| `deploy/debian-x64/setup.sh` | No Production seed at all. Its only `api/`+`web/` pairs are systemd unit paths and printed instructions. |
| `deploy/Deploy-ToPi.ps1` | A pure wrapper — no `param()` block, forwards `@args` to `Deploy-ToLinux.ps1:47`. It **inherits** the bug and needs no separate fix. |

One instance. §5.3 records what would falsify that.

### 1.4 Why it is dormant on `radio` and live on the next box

`radio` has both overlays present, so `:222` short-circuits and `:226` never runs. The divergent state
arises on a box provisioned by copying an api overlay in by hand and not a web one, or the reverse —
which is what a first provisioning looks like, and what the next box will be.

---

## 2. The fix

**File:** `deploy/Deploy-ToLinux.ps1`, replacing `:219-233`.

```powershell
# Deploy target-specific Production config into each service directory that does not
# already have one.
#
# ⚠ GUARDED PER DESTINATION, NOT ONCE FOR BOTH. Before OPS-7 a single `test -f` on api/
# gated a copy into BOTH api/ and web/, so a box with a web overlay and no api overlay had
# its web file OVERWRITTEN by the seed. That file holds RotaryPhone:Gv:AuthKey, and the
# tracked seed has no RotaryPhone section — so the overwrite deleted the key rather than
# replacing it, and the service came up with inter-service auth silently off.
#
# The per-file shape here matches Sync-WpRule below, which has always guarded each
# destination independently.

⚠ **DO NOT TRANSCRIBE THE COMMENT ABOVE — it is preserved as filed, and two of its claims are
false.** It is what `OPS-7`'s Builder copied into the source, and both errors reached review:

1. *"That file holds `RotaryPhone:Gv:AuthKey`"* — it does not; see the correction in §1.1. The
   shipped comment says what is lost **today** (operator-authored `Gv` config) and what is lost
   **later** (the key, once `PHN-2`'s gate is on).
2. *"matches Sync-WpRule … which has **always** guarded each destination independently"* — the
   present-tense half is true, the historical half was never checked, and the pointer is
   misleading anyway: `Sync-WpRule`'s guard is compare-and-**overwrite**, the opposite policy to
   this block. A reader following it to confirm "is it safe not to clobber?" lands on a function
   that clobbers on every deploy.

The shipped wording is in `deploy/Deploy-ToLinux.ps1`; prefer it over this draft.
$targetConfigPath = Join-Path $RepoRoot "deploy\$configDir\appsettings.Production.json"
if (Test-Path $targetConfigPath) {
  $seedStaged = $false
  foreach ($dest in @('api', 'web')) {
    ssh $SshTarget "test -f $TargetPath/$dest/appsettings.Production.json" 2>$null
    if ($LASTEXITCODE -eq 0) {
      Write-Host "    $dest/appsettings.Production.json present — left alone" -ForegroundColor DarkGray
      continue
    }

    if (-not $seedStaged) {
      Write-Host "  Deploying Production config from deploy/$configDir/..." -ForegroundColor DarkGray
      scp $targetConfigPath "${SshTarget}:/tmp/appsettings.Production.json"
      if ($LASTEXITCODE -ne 0) {
        Write-Host "Production config upload failed!" -ForegroundColor Red
        exit 1
      }
      $seedStaged = $true
    }

    ssh $SshTarget "sudo cp /tmp/appsettings.Production.json $TargetPath/$dest/ && sudo chown ${TargetUser}:${TargetUser} $TargetPath/$dest/appsettings.Production.json"
    if ($LASTEXITCODE -ne 0) {
      Write-Host "Production config seed into $dest/ failed!" -ForegroundColor Red
      exit 1
    }
  }

  if ($seedStaged) {
    ssh $SshTarget "rm -f /tmp/appsettings.Production.json"
  }
}
```

And the file-move check, which must now be **captured at `:217` rather than read at `:230`**:

```powershell
# :217 — unchanged command, but capture its exit code immediately.
ssh $SshTarget "sudo mkdir -p ... && rm -rf /tmp/radio-deploy-api /tmp/radio-deploy-web"
$moveExit = $LASTEXITCODE
if ($moveExit -ne 0) {
  Write-Host "Remote file move failed!" -ForegroundColor Red
  exit 1
}
```

…and the old `if ($LASTEXITCODE -ne 0) { "Remote file move failed!" }` at `:230-233` is **deleted**,
because the check now lives immediately after the thing it checks and the seed block does its own
error handling inline.

### 2.1 Three decisions in that code, with reasons

- **`scp` once, `cp` per destination.** The seed is staged to `/tmp` at most once even when both
  directories need it. Uploading twice would be harmless but slower and would make the log read as
  though two different files were involved.
- **`rm -f` rather than `rm`.** The old code's `rm` was chained with `&&` inside the same `ssh` as
  the copies. Now it is a separate call guarded by `$seedStaged`, and `-f` means a re-run after a
  partial failure does not exit non-zero on an already-removed temp file.
- **A "present — left alone" line at DarkGray.** The old code was silent when it skipped, so the
  common case produced no evidence either way. One line per destination makes the *decision* visible
  in the deploy output, which is what an operator needs to confirm the guard did what they expected —
  and it is exactly what §3 reads to check the fix.

---

## 3. ⚠ Verification — how to prove this without risking a real secret

**The obvious test destroys the thing under protection.** Everything below exists to avoid that.

### 3.1 The isolation that makes it safe

`Deploy-ToLinux.ps1:50` declares `[Alias("PiPath")] [string]$TargetPath = … "/opt/radio-console"`.
`$TargetPath` is threaded through the whole file-placement path — `:217`, `:222`, `:226` — so a
scratch value puts every byte this row touches somewhere the live install is not.

Combined with `-NoRestart` (`:145`, `:323`), which skips both the service stop and the service start,
the live `radio-api` and `radio-web` keep running against `/opt/radio-console` throughout and are
never restarted.

### 3.2 ⚠ The trap: `-TargetPath` does **not** isolate everything

**The WirePlumber block — `:301-384` after `OPS-7` shifted it; `:299-318` as planned — is not
parameterised by `$TargetPath` and is not guarded by `-NoRestart`.** It runs on every invocation, writes to `/etc/wireplumber/main.lua.d` and
`/etc/wireplumber/bluetooth.lua.d`, and can issue
`systemctl --user restart wireplumber` — **which cycles BT and audio on a box that may be playing
music in someone's living room.**

In practice it is a no-op on a box already in sync: `Sync-WpRule` compares with `cmp` and only
installs on difference, and `$wpRulesChanged` stays false, so the restart does not fire. That is a
statement about the box's current state, **not** a property of the script.

**So, before the first scratch run, confirm the rules are in sync** — run the deploy once normally,
or check that the previous deploy printed `WirePlumber rules up to date`. If any rule differs, the
scratch run will restart WirePlumber. **Do this at a quiet time regardless**, and never while nobody
is physically present at the box (`CLAUDE.md` § *What the box actually is* — WiFi is the only link).

This is the single most important paragraph in the plan. A Builder who reads §3.1 and skips §3.2 will
believe `-TargetPath` bought total isolation and cycle the household's audio to test a config guard.

### 3.3 The procedure

Runs against `radio`. `$S` is `mmack@radio`.

**Set up the state that loses data — web overlay present, api overlay absent.**

```bash
ssh mmack@radio 'sudo mkdir -p /opt/radio-console-ops7/api /opt/radio-console-ops7/web'
ssh mmack@radio 'echo "{\"RotaryPhone\":{\"Gv\":{\"AuthKey\":\"OPS7-SENTINEL-DO-NOT-CLOBBER\"}}}" | sudo tee /opt/radio-console-ops7/web/appsettings.Production.json'
ssh mmack@radio 'ls -l /opt/radio-console-ops7/api/ /opt/radio-console-ops7/web/'
```

The api directory must be empty of an overlay. That asymmetry **is** the bug's precondition.

**Prove the OLD code destroys it — run the mutation first, on the scratch path.**

```powershell
git stash                     # or check out main into a second worktree
./deploy/Deploy-ToLinux.ps1 -TargetHost radio -Runtime linux-x64 `
  -TargetPath /opt/radio-console-ops7 -NoRestart
```

```bash
ssh mmack@radio 'grep -c OPS7-SENTINEL /opt/radio-console-ops7/web/appsettings.Production.json || echo DESTROYED'
```

**This must print `DESTROYED` (or `0`).** ⚠ **If it does not, stop.** Either the reproduction is wrong
or the defect is not what §1.1 says, and the fix would then be a change with no demonstrated
behaviour. **This step is the whole verification** — without it, a green run after the fix proves
only that the fix did not crash. It is safe to run precisely because it destroys a sentinel in a
scratch directory rather than a key in the live one.

**Re-arm and run the FIXED code.**

```bash
ssh mmack@radio 'echo "{\"RotaryPhone\":{\"Gv\":{\"AuthKey\":\"OPS7-SENTINEL-DO-NOT-CLOBBER\"}}}" | sudo tee /opt/radio-console-ops7/web/appsettings.Production.json'
ssh mmack@radio 'sudo rm -f /opt/radio-console-ops7/api/appsettings.Production.json'
```

```powershell
git stash pop
./deploy/Deploy-ToLinux.ps1 -TargetHost radio -Runtime linux-x64 `
  -TargetPath /opt/radio-console-ops7 -NoRestart
```

Assert all four:

```bash
# 1. the sentinel survived
ssh mmack@radio 'grep -c OPS7-SENTINEL /opt/radio-console-ops7/web/appsettings.Production.json'   # 1
# 2. the api overlay WAS seeded — the guard must not have become "never seed"
ssh mmack@radio 'test -f /opt/radio-console-ops7/api/appsettings.Production.json && echo SEEDED'   # SEEDED
# 3. the api seed is the tracked file, not something else
ssh mmack@radio 'grep -c HiddenDevicePatterns /opt/radio-console-ops7/api/appsettings.Production.json'  # 1
# 4. no temp file left behind
ssh mmack@radio 'test -f /tmp/appsettings.Production.json && echo LEAKED || echo clean'            # clean
```

Assertion 2 is not decoration. **The trivially wrong fix is to delete the copy into `web/` and call
it done** — that passes assertion 1 and leaves a freshly provisioned box with no web overlay at all.
Assertion 2 is what separates "guarded correctly" from "stopped seeding".

**Then the mirror case**, which the original bug's symmetry demands: api present, web absent → the
web file must be seeded and the api file left byte-identical (`sha256sum` before and after).

**Clean up, always.**

```bash
ssh mmack@radio 'sudo rm -rf /opt/radio-console-ops7'
```

### 3.4 Confirm the live install was untouched

```bash
ssh mmack@radio 'ls -l /opt/radio-console/api/appsettings.Production.json /opt/radio-console/web/appsettings.Production.json'
ssh mmack@radio 'systemctl is-active radio-api radio-web'
```

Timestamps unchanged from before the exercise; both services `active`. Record the `ls -l` output
**before** starting so there is something to compare against.

### 3.5 Considered and rejected: a Pester harness

A `Deploy-ToLinux.Tests.ps1` stubbing `ssh`/`scp` and asserting the issued commands would be a real
CI-able pin, and it is the only route that catches a future regression automatically.

**Rejected for this row.** The repo has no PowerShell test infrastructure at all — verified: no
`*.Tests.ps1` anywhere, no Pester reference in any `.ps1` or workflow. Standing up a test framework,
a CI job and a stubbing layer for `ssh` is a larger and more interesting piece of work than the
nine-line fix it would guard, and it belongs to whoever decides deploy scripts should be tested at
all. **It is the honest answer to "how does this get pinned permanently", and this row does not
answer it.** §5.2 says what that leaves exposed.

---

## 4. Tasks

| # | Task |
|---|---|
| 1 | Rewrite `deploy/Deploy-ToLinux.ps1:219-233` per §2 — per-destination guard, inline error handling, `:230-233` deleted. |
| 2 | Capture `$moveExit` immediately after `:217` per §2 (`C-88`). |
| 3 | Run §3.3 in full, **including the pre-fix mutation**. Paste all four assertions plus the mutation result into the PR body. |
| 4 | Run §3.3's mirror case (api present, web absent). |
| 5 | Clean up the scratch path (§3.3) and confirm the live install untouched (§3.4). |
| 6 | `deploy/DEPLOYMENT.md` — one line under the Production-config section: each service directory is seeded independently, and an existing overlay is never overwritten. |
| 7 | Builder marks `docs/BUILDER_QUEUE.md`'s `OPS-7` row ✅ at merge. |

**No unit tests.** There is nothing in this change a `dotnet test` can reach; the whole diff is
PowerShell. `dotnet build` and the existing suite must still be green, because a red suite would mean
something unrelated broke — but they are not evidence about this row, and the PR body should say so
rather than listing them as if they were.

---

## 5. Self-review

### 5.1 What was verified, first-hand, at `603207df`

- `:222`/`:226` guard/copy asymmetry — read verbatim (§1.1).
- The tracked seed `deploy/debian-x64/appsettings.Production.json` contains **no `RotaryPhone`
  section** — read in full. This is what makes the failure a deletion rather than a substitution.
- `RotaryPhone:Gv:AuthKey` is a `Radio.Web` key — `src/Radio.Web/Services/Http/RotaryPhoneAuthHandler.cs`,
  `docs/HANDOFF-rotaryphone-gv-send-markread-auth.md:102`.
- `$LASTEXITCODE` masking at `:230` (`C-88`), including that both `$configDir` values have a seed
  file, which is what makes the masking unconditional.
- `-TargetPath` is a real parameter (`:50`) used at `:217`/`:222`/`:226`.
- `-NoRestart` guards `:145` and `:323` (now `:389`), and **not** the WirePlumber block (§3.2).
  Re-verified post-fix by `OPS-7`'s Builder: the block runs `:301-384`, `systemctl --user restart
  wireplumber` is at `:377`, and a sweep of that range finds neither `$NoRestart` nor `$TargetPath`.
- `Sync-WpRule` guards per destination (`:268-297`) — the exemplar.
- No Pester or `*.Tests.ps1` anywhere in the repo.

### 5.2 What could not be verified, and what it costs

1. **Nothing here was run.** This is a plan; §3.3's mutation is the first time the defect will be
   observed rather than read. **If the mutation does not destroy the sentinel, §1.1 is wrong** and
   the fix must not land on a reading alone.
2. **The exact shell semantics of `2>$null` on `:222` under PowerShell 7 vs 5.1** were not tested.
   It suppresses `ssh`'s stderr; `$LASTEXITCODE` is set either way. The rewrite keeps the same
   construct, so behaviour is unchanged — but if a future edit drops it, `test -f`'s failure noise
   starts appearing in deploy output.
3. **`raspberry-pi` was not exercised.** The fix is runtime-independent (`$configDir` only selects a
   source path), and `piradio` was not available. The mirror-case run covers the logic; the ARM64
   path is unexercised.
4. **No permanent regression guard exists after this merges** (§3.5). Someone can reintroduce the
   single-guard shape and nothing will catch it. That is a real residual, stated rather than
   papered over.

### 5.3 What would falsify `C-89` ("one instance")

Another `test -f`/`cp` pair in the deploy path where the tested path and the written path differ.
The sweep covered `Deploy-ToLinux.ps1` end to end, `Deploy-ToPi.ps1`, and `deploy/debian-x64/setup.sh`.
**Not covered:** `deploy/deploy-to-pi.sh`, `deploy/provision/`, `deploy/scripts/` and
`deploy/raspberry-pi/`. Those are provisioning-time rather than deploy-time, which is why they were
deprioritised — but *"one instance"* is a claim about the four files listed, **not about the whole
`deploy/` tree**, and it should not be quoted as the latter.

---

## 6. Deliberately not done

- **Making the seed merge rather than overwrite.** A box whose overlay is missing *some* keys still
  gets nothing new. Merging JSON overlays in a bash one-liner is a genuinely different piece of work
  and a different failure mode; "never overwrite what exists" is the whole of this row.
- **Giving `api/` and `web/` separate seed files.** Today one file is copied to both, so the api
  overlay contains web keys and vice versa. Arguably wrong, definitely out of scope, and it would
  change what a correctly-provisioned box receives.
- **A Pester harness.** §3.5, with the cost named.
- **Anything about `GvMedia:AuthKey`.** A different key in a different service's config, and
  `PHN-2`'s queue entry already records that it is empty on `radio` and its gate is off.

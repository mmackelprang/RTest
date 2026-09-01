# Rotary-Phone Arc Revival Survey

**Generated:** 2026-05-24
**Purpose:** Read-only inventory of the 7 branches preserved in `branch-cleanup-manifest.md`, with revival-effort guidance.

---

## 1. Executive Summary

Of the 7 "preserved" branches, **only one carries genuinely shippable, un-merged work**: `fix/sdr-gc-pressure-allocation` (and its parent `feature/rotaryphone-sip-integration`, which adds two doc files on top of it). Four other branches in the "rotary-phone arc" — `feature/bt-audio-reliability-infrastructure`, `feature/gvbridge-kiosk-integration`, `feature/capture-lifecycle-fix-and-instrumentation`, and `feature/bt-management-pbap-sync-ui` — were already merged to `main` via squash-merge PRs in March 2026 (#347, #350, #349, #344 respectively). They are "preserved" only as historical refs; their content is in `main` today. `claude/check-repo-status-hAc1F` is unrelated to the rotary arc — it is the source branch for the already-merged version-endpoint PR #385 (with one DisplayNames fix that landed via #392).

**Recommended sequencing:**
1. Ship the SDR GC fix immediately (small, foundational, production-critical, will conflict-resolve cleanly).
2. Triage the RotaryPhone SIP integration plan (`docs/rotaryphone-sip-integration-prompt.md` + `docs/plans/2026-03-30-rotaryphone-sip-ui-update.md`) — the plan is sound but its execution will require updates because (a) `GvBridgeHubService` and `GvTrunkHubService` already exist on main (registered via PR #350) and need to be either deleted or repurposed, and (b) the RotaryPhone-side architectural change happened weeks ago and may have evolved further since.
3. Delete the four already-merged branches and the obsolete `claude/check-repo-status-hAc1F` branch in the next cleanup pass (they are confusing the picture).

---

## 2. Per-Branch Summary

### 2.1 `feature/rotaryphone-sip-integration`

- **Last commit:** `95d1e5a` — Mark Mackelprang — 2026-03-30 21:06 EDT — *"docs: add RotaryPhone SIP integration prompt and UI update plan"*
- **State vs main:** 2 ahead / 62 behind
- **Diff:** 3 files (`docs/plans/2026-03-30-rotaryphone-sip-ui-update.md`, `docs/rotaryphone-sip-integration-prompt.md`, `src/RTLSDRCore/RadioReceiver.cs`). 381 insertions, 4 deletions.
- **Intent:** Two distinct concerns:
  1. The SDR GC-pressure fix (inherited from `fix/sdr-gc-pressure-allocation`).
  2. **A planning document only** describing the work needed in `Radio.Web` after RotaryPhone replaced its Chrome-extension-based Google Voice integration with native SIP-over-WebSocket + DTLS-SRTP. The plan covers `ApiModels.cs` DTO updates, deleting `GvBridgeHubService.cs`, repurposing `GvBridgeApiService.cs`, replacing the "GV Browser" mode button with "GV API (SIP)", switching from SignalR to a 5-second REST poll, and adding cookie management UI to `SystemConfigPage.razor`. The plan also calls for one RotaryPhone-side API change (`GetStatus` endpoint returning `SipRegistered` / `CookiesValid` instead of `ExtensionConnected`).
- **Completeness:** **Plan-only.** No implementation commits exist on this branch beyond the SDR fix it inherits. The plan was committed but never executed.
- **Overtaken-by-main:** The SDR fix is **not** in main (verified — `src/RTLSDRCore/RadioReceiver.cs:1143` still allocates `new float[maxDecimOut]` per batch). The SIP UI update plan is **not** implemented and is **partially obsolete** because `GvBridgeHubService.cs` and `GvTrunkHubService.cs` (which the plan says to delete) **already exist on main** — they were added by PR #350 (`feature/gvbridge-kiosk-integration` squash-merged 2026-03-22). So step 1 of the plan can be executed today; the "delete the hub service" part is still applicable.
- **Conflict hotspots if rebased:**
  - `src/RTLSDRCore/RadioReceiver.cs` will conflict heavily — main has added ~30 lines of new RDS infrastructure (`RdsStationNameChanged` event, PI code decoding for NRSC-4-B Annex D call-sign decode) in the same file. The fix-relevant lines (1143–1155 area) are still pristine, so the per-batch allocation fix can be cherry-picked manually.
- **Recommended action:** **SPLIT and REWORK.** Cherry-pick the SDR fix as its own focused PR (S). Treat the SIP-integration plan as a fresh spec that needs a quick refresh-and-execute pass (M).

### 2.2 `feature/gvbridge-kiosk-integration`

- **Last commit:** `681754c` — Mark Mackelprang — 2026-03-22 12:47 EDT — *"fix: add GV Bridge/Trunk status to poll loop as SignalR fallback"*
- **State vs main:** 4 ahead / 63 behind
- **Reality:** All 4 unique commits (`c10451e`, `38e9ba2`, `dc74bf2`, `681754c`) were **squash-merged into main as commit `fd9019c` (PR #350) on 2026-03-22**. Verified — current `main` already has `src/Radio.Web/Services/Hub/GvBridgeHubService.cs`, `GvTrunkHubService.cs`, `GvBridgeApiService.cs`, `GvTrunkApiService.cs`, and the PhonePage integration. The branch tip is a pre-squash artifact.
- **Completeness:** **Done — already in main.**
- **Overtaken-by-main:** Fully — the content IS main.
- **Conflict hotspots:** N/A; no work to land.
- **Recommended action:** **ARCHIVE.** Move to "safe to delete" list. There is nothing to do; the branch can be deleted (`git branch -D feature/gvbridge-kiosk-integration` and remote delete) once the user authorizes cleanup.

### 2.3 `feature/capture-lifecycle-fix-and-instrumentation`

- **Last commit:** `f921d9a` — Mark Mackelprang — 2026-03-21 21:22 EDT — *"fix: complete BT stall cleanup and add reentrancy guards to recovery handlers"*
- **State vs main:** **0 ahead / 64 behind**
- **Reality:** The branch tip IS the merge commit on main: `333e7da Merge pull request #349 from mmackelprang/feature/capture-lifecycle-fix-and-instrumentation`. This was a true merge (not squash), so the entire branch history is preserved in main's commit graph. Zero unique commits.
- **Completeness:** **Done — already in main as PR #349.** This shipped `GeneratorStalled` event, `BufferedSoundGenerator` lifecycle tracking, audio flow health monitor (10s), and BT source self-healing.
- **Overtaken-by-main:** Yes, by its own merge.
- **Conflict hotspots:** N/A.
- **Recommended action:** **ARCHIVE.** Move to "safe to delete" list. Note that PR #390 (`feature/bt-capture-watchdog`, commit `f82660a`) and PR #411 (`fix/bt-fingerprint-reattach-on-recovery`, commit `72d0a04`) extended this work further on main; the capture-lifecycle story has moved on without this branch.

### 2.4 `feature/bt-audio-reliability-infrastructure`

- **Last commit:** `93d12f1` — Mark Mackelprang — 2026-03-14 17:44 EDT — *"fix: address code review feedback — pipeline monitor leak, cancellation, adapter state"*
- **State vs main:** 8 ahead / 75 behind
- **Reality:** The 8 "unique" commits were **squash-merged into main as commit `ba94a17` (PR #347) on 2026-03-14**. Tree comparison confirms `feature/bt-audio-reliability-infrastructure` and `ba94a17^{tree}` are byte-identical — verified by checking that `radio-bt-setup.sh`, `BluetoothHealthCheck.cs`, `BluetoothPipelineStatus` enum, the 30-second pipeline self-healing monitor, `CaptureStreamRecovered` event, and the APT hook for bluez.lua are all present in main.
- **Completeness:** **Done — already in main as PR #347.**
- **Overtaken-by-main:** Yes, by its own merge. Plus main has shipped many follow-on BT fixes that build on this foundation (#390 watchdog, #391 autoswitch gate, #394 pw-event subscription, #402 drift compensation, #404 variable-rate resampler, #407 WP dual-routing fix, #410 album art cache, #411 fingerprint re-enable).
- **Conflict hotspots:** N/A; the branch IS in main.
- **Recommended action:** **ARCHIVE.** Move to "safe to delete" list.

### 2.5 `feature/bt-management-pbap-sync-ui`

- **Last commit:** `60c2c54` — Mark Mackelprang — 2026-03-12 14:28 EDT — *"fix: cache MergedContacts list instead of recomputing on every access"*
- **State vs main:** **0 ahead / 87 behind**
- **Reality:** This branch was merged to main as PR #344 / commit `7c9f463` (true merge, not squash). The branch tip itself appears in main's history as `60c2c54` directly. All commits including PBAP integration, `PbapApiService`, contact merging, BluetoothPage service badges — all already in main.
- **Completeness:** **Done — already in main as PR #344.**
- **Overtaken-by-main:** Yes, by its own merge. Plus main has shipped further PBAP-related fixes (commit `baa544d` "show PBAP contacts even when phone is disconnected", commit `74b3d6c` "enable virtual scrolling on contacts DataGrid", etc.).
- **Conflict hotspots:** N/A.
- **Recommended action:** **ARCHIVE.** Local-only branch with no remote; safe to `git branch -D` after user authorization.

### 2.6 `fix/sdr-gc-pressure-allocation`

- **Last commit:** `55717b3` — Mark Mackelprang — 2026-03-24 15:44 EDT — *"fix: eliminate per-batch allocation in SDR stereo decimation path"*
- **State vs main:** 1 ahead / 62 behind
- **Diff:** 1 file (`src/RTLSDRCore/RadioReceiver.cs`), +7/-4 lines.
- **Intent:** Fix a production-critical memory issue. The stereo FM decimation path allocates `new float[maxDecimOut]` per IQ batch (~200 Hz). Over multi-day uptime this produced ~131 GB of garbage (14M Gen0, 195K Gen1, 32K Gen2 collections per observation interval), starving MiniAudio's audio callback and causing PipeWire to drop the stream. The fix reuses the already-existing `_decimBufferRight` field (currently used only for the de-emphasis path) for the decimation output as well. Commit message documents a clean root-cause trace: 77% CPU on RadioReceiver-DSP thread after 2 days uptime → missed callback deadline → MiniAudio stream dropped → no audio despite API reporting Playing.
- **Completeness:** **Substantially complete.** Single-file fix with thorough commit message. No tests added — adding a microbenchmark / allocation-tracking test would strengthen the PR but is optional given the fix is "remove allocation, reuse existing field."
- **Overtaken-by-main:** **No.** Verified — `src/RTLSDRCore/RadioReceiver.cs:1143` still has `var rightDecimBuf = new float[maxDecimOut]`. This is real, unmerged, production-relevant work.
- **Conflict hotspots if rebased:** Substantial. Main has added ~30 lines of new RDS infrastructure to `RadioReceiver.cs` since March (`RdsStationNameChanged` event with documentation block, `RdsPiCodeReceived` event, NRSC-4-B Annex D call-sign decoding hook, Task #80 work). The fix-relevant lines (1142–1155 area) are themselves pristine — the conflicts will be in surrounding context around the file's event declarations and DSP buffer fields. A manual cherry-pick of just the 11-line change against current main will be cleaner than `git rebase`.
- **Recommended action:** **REBASE+SHIP.** Cherry-pick the fix against current main into a fresh branch (e.g. `fix/sdr-gc-pressure-allocation-rebased`), add an allocation-budget assertion or microbenchmark test, open a PR. Effort: **S**.

### 2.7 `claude/check-repo-status-hAc1F`

- **Last commit:** `1167968` — Mark Mackelprang — 2026-05-21 20:03 EDT — *"fix(web,test): normalize Windows paths in DisplayNames + await async clicks in GainControlPopover tests"*
- **State vs main:** 3 ahead / 36 behind
- **Diff:** 9 files (Directory.Build.props, DEPLOYMENT.md, Deploy-ToLinux.ps1, deploy-to-pi.sh, HealthController.cs, VersionInfoDto.cs, DisplayNames.cs, HealthControllerTests.cs, GainControlPopoverTests.cs). 345 insertions, 8 deletions.
- **Intent:** Stamp git SHA into AssemblyInformationalVersion at build time, expose via `/api/health/version`, have deploy scripts verify the running SHA matches the locally-built HEAD after deploy. Plus a Linux-CI fix for `DisplayNames.DeriveTitleFromFilePath` (Windows path normalization) and bUnit async-await fixes for `GainControlPopover` tests.
- **Completeness:** Looked complete at the time. Was the source branch for **PR #385** ("feat(api): /api/health/version endpoint + deploy SHA verification") which merged to main as commit `9998376`.
- **Overtaken-by-main:** **Yes, almost entirely.**
  - Directory.Build.props version stamping: in main via #385 (zero diff between branch and main for this file).
  - HealthController + VersionInfoDto + tests: in main via #385.
  - DisplayNames Windows path normalization: in main via PR #392 (`55515a8 fix(web): normalize Windows path separators in DisplayNames.DeriveTitleFromFilePath`) — the branch's version differs only in comment wording and variable name (`normalized` vs `normalizedPath`).
  - `Deploy-ToLinux.ps1` and `deploy-to-pi.sh`: branch is **substantially older** than main (missing Chrome cache cleanup, missing WirePlumber Lua rule sync, missing many deploy-script hardening fixes). Reverting any of this would be a regression.
  - `GainControlPopover` async-await test fixes: in main via #386 (`c1fba27`, mentioned in commit message: *"Same fix that PR #386 lands; mirrored here so #385 can go green independently"*).
- **Conflict hotspots:** N/A — the branch is essentially dead weight. Re-applying its deploy-script changes would actively regress deploy infrastructure.
- **Recommended action:** **ARCHIVE.** This branch is NOT part of the rotary-phone arc — its inclusion in the manifest was a mis-flag. Safe to delete after user confirms. No further investigation warranted.

---

## 3. Dependency Graph

```
                          [main]
                            │
              ┌─────────────┴─────────────┐
              │                           │
   fix/sdr-gc-pressure-allocation         │      (independent — pure DSP fix)
              │                           │
              └─►  feature/rotaryphone-sip-integration
                      │
                      └─► docs/plans/2026-03-30-rotaryphone-sip-ui-update.md
                          docs/rotaryphone-sip-integration-prompt.md
                          (planning only — NOT YET IMPLEMENTED in code)


   [main]  ←─ already contains all of:
            ├─ feature/bt-audio-reliability-infrastructure (PR #347, ba94a17)
            ├─ feature/capture-lifecycle-fix-and-instrumentation (PR #349, 333e7da)
            ├─ feature/gvbridge-kiosk-integration (PR #350, fd9019c)
            ├─ feature/bt-management-pbap-sync-ui (PR #344, 7c9f463)
            └─ claude/check-repo-status-hAc1F (PR #385+#392, 9998376+55515a8)
```

### Real dependency chain to be aware of

The rotary-phone "arc" in main builds in layers:

```
PR #341 (rotary-phone-integration UI — base) ──► PR #344 (PBAP sync) ──►
   PR #345 (BT adapter isolation + RotaryPhone hub URL fix) ──►
      PR #347 (BT audio reliability infra) ──►
         PR #349 (capture lifecycle + GeneratorStalled self-heal) ──►
            PR #350 (GV Bridge / GV Trunk UI integration) ──►
               (rotary arc paused here)
                   ⋮
               (2 months of unrelated audio/UX work in main: #382–#420)
                   ⋮
               PR-to-be: "RotaryPhone SIP UI refresh" — execute the plan
               from feature/rotaryphone-sip-integration, but updated to
               account for current main state.
```

The SDR GC fix is **independent** of the rotary chain.

---

## 4. Recommended Completion Sequence

| # | Action | Branch / Source | Rationale | Effort |
|---|--------|------------------|-----------|--------|
| 1 | **Cherry-pick SDR GC fix onto fresh branch off main**; add allocation-budget test or microbenchmark; open PR. | `fix/sdr-gc-pressure-allocation` → new branch | Production bug. Affects long-uptime stability. Independent of rotary work. Smallest possible PR. | **S** (~1–2 h) |
| 2 | **Refresh the RotaryPhone SIP UI plan**: compare `docs/plans/2026-03-30-rotaryphone-sip-ui-update.md` against current state of `GvBridgeApiService.cs`, `GvBridgeHubService.cs`, `GvTrunkHubService.cs`, `PhonePage.razor` in main. Mark steps that are still needed, already done, or partially done. Write a fresh execution plan. | `feature/rotaryphone-sip-integration` docs | Don't blindly execute a 2-month-old plan. Architecture has drifted. | **S** (~1 h) |
| 3 | **Confirm RotaryPhone-side state**: read `D:\prj\RotaryPhone\docs\prompts\RADIO-CONSOLE-BT-AUDIO-BOUNDARY.md`, check the current state of the RotaryPhone service's `/api/gvbridge/status` endpoint, confirm whether it still returns `ExtensionConnected` or already returns `SipRegistered`. May require launching the RotaryPhone service and inspecting the API surface. | Cross-repo coordination | Plan's RotaryPhone-side updates (Task 5) may already be in flight or done. | **S** (~30 min) |
| 4 | **Execute the refreshed SIP UI plan**: update `GvBridgeStatusDto`, repurpose or delete `GvBridgeHubService.cs`, replace "GV Browser" with "GV API (SIP)" in `PhonePage.razor`, switch SignalR subscription to 5-second REST poll, add SIP/Cookie status chips, optionally add cookie-management UI to `SystemConfigPage.razor`. | New branch off main | Implements the actual user-facing fix. | **M** (~3–5 h, mostly UI) |
| 5 | **Archive obsolete branches**: in a follow-up housekeeping PR/cleanup pass, delete `feature/bt-audio-reliability-infrastructure`, `feature/gvbridge-kiosk-integration`, `feature/capture-lifecycle-fix-and-instrumentation`, `feature/bt-management-pbap-sync-ui`, `claude/check-repo-status-hAc1F`, and the original `feature/rotaryphone-sip-integration` and `fix/sdr-gc-pressure-allocation` once items 1 and 4 are merged. Update `design/research/branch-cleanup-manifest.md` to reflect the new "DO NOT DELETE" emptiness. | All preserved branches | Reduces confusion in future surveys. | **S** (~15 min) |

---

## 5. Questions for the User

1. **SDR fix scope.** Do you want the SDR GC fix shipped as a stand-alone PR (recommended), or bundled with a broader RTLSDR observability sweep (e.g. allocation-budget metric)? Stand-alone is faster.
2. **Allocation regression guard.** For the SDR fix, do you want a new microbenchmark test asserting "no allocations per batch in the hot path" (a real safety net but adds a benchmark project dependency), or just the fix with a comment? Either is defensible.
3. **RotaryPhone-side coordination.** The SIP UI plan calls for updating `/api/gvbridge/status` on the RotaryPhone service to return `SipRegistered` + `CookiesValid` instead of `ExtensionConnected`. Has that change already shipped on the RotaryPhone side? If not, do you want to coordinate the change (RotaryPhone API update + RTest UI update in lock-step) or land RTest with a defensive fallback that tolerates both old and new DTO shapes?
4. **GvBridgeHubService disposition.** The original plan said "delete entirely; use REST polling." But `GvBridgeHubService.cs` is now well-established in main with its 4 ahead commits already in. Do you want to truly delete it (cleaner) or keep it stubbed/disabled with a feature flag (safer rollback)? Recommend delete — the new architecture has no hub to connect to.
5. **GV Browser mode value.** The plan says replace `"GVBrowser"` with `"GVApi"` in mode string comparisons. Are there any persisted user preferences using the old string value that need migration, or is this a "next start of the app, defaults take over" situation?
6. **Cookie management UI priority.** The plan marks SystemConfigPage cookie-management UI as "nice-to-have, not a blocker." Should that go in the same PR or be deferred to a follow-up?
7. **Branch cleanup authorization.** Are you ready to authorize deletion of the five obsolete branches (BT reliability, GV Bridge, capture lifecycle, PBAP, claude/check-repo-status) once items 1 and 4 are merged?

---

## 6. Effort Estimates Summary

| Branch | Action | Effort |
|--------|--------|--------|
| `fix/sdr-gc-pressure-allocation` | Cherry-pick + test + PR | **S** (1–2 h) |
| `feature/rotaryphone-sip-integration` (plan portion) | Refresh plan + execute UI changes | **M** (4–6 h total) |
| `feature/bt-audio-reliability-infrastructure` | Archive | — |
| `feature/gvbridge-kiosk-integration` | Archive | — |
| `feature/capture-lifecycle-fix-and-instrumentation` | Archive | — |
| `feature/bt-management-pbap-sync-ui` | Archive | — |
| `claude/check-repo-status-hAc1F` | Archive | — |

**Total revival effort:** ~5–8 hours of actual code work, plus a 1-hour plan refresh and ~30 minutes of cross-repo verification.

---

## 7. Risk Notes

- **Risk:** Re-applying the SDR GC fix could mask a separate underlying issue (e.g. if main has added different allocation sites in the same hot path since March). **Mitigation:** Run `dotnet-counters monitor` on the radio-api process after deploy for one long-uptime cycle (24+ h) to confirm Gen0/Gen1 collection rates have dropped. The original observation was 14M Gen0 in one interval; post-fix expectation is "low, stable, no growth over hours."

- **Risk:** The SIP UI plan was written against a March 2026 snapshot of both repos. Even with a refresh (item 2 above), there may be subtler integration issues — e.g. the call-state flow in `PhoneCallIntegrationService.cs` may have been refactored on either side. **Mitigation:** Item 3 — actually launch both services in dev mode and confirm the API surface before writing code.

- **Risk:** Per the project's "RotaryPhone is UI-only" rule (from user MEMORY.md), the SIP UI work must NOT register any RotaryPhone backend services in `Radio.Web`'s DI container. The existing pattern (`GvBridgeApiService` as a REST client, `GvBridgeHubService` as a SignalR client) is correct. The plan's Task 6 "switch to REST polling" stays inside this rule. **Mitigation:** Code review focus — confirm no `services.AddSingleton<IGv*Service, *>` (where the impl is a backend type) sneaks in.

- **Risk:** The 2-month gap means the user (and any agent that picks this up) has stale context about RotaryPhone's current architecture. The branch's `docs/rotaryphone-sip-integration-prompt.md` references "PR mmackelprang/RotaryPhone#19 (`feature/sip-wss-audio`)" — that PR may have been merged, abandoned, or evolved further. **Mitigation:** Read the current state of `D:\prj\RotaryPhone\docs\prompts\RADIO-CONSOLE-BT-AUDIO-BOUNDARY.md` and `D:\prj\RotaryPhone\` recent commits before starting item 4.

- **Risk (low):** Two of the "preserved" branches (`feature/bt-management-pbap-sync-ui` and `claude/check-repo-status-hAc1F`) are technically still present in the local repo and could be accidentally checked out or pushed. Both are in a state where pushing/merging would be confusing (PBAP branch is local-only with stale content; claude-check would attempt to revert deploy-script improvements). **Mitigation:** Item 5 cleanup pass eliminates this risk.

- **Risk:** The "preserved" branch manifest itself is now misleading — five of the seven entries are actually merged or obsolete. Future agents reading the manifest will waste cycles re-investigating the same finding. **Mitigation:** Update `design/research/branch-cleanup-manifest.md` to reflect this survey's findings (move the four merged + one obsolete branches into a "merged via PR — safe to delete" subsection with PR references). This can happen during item 5.

---

## Appendix A: Verification Commands Used

```bash
# Branch ahead/behind counts
git rev-list --count main..<branch>
git rev-list --count <branch>..main

# Unique commits per branch (excludes merge-base ancestry)
git log <branch> --not main --oneline

# Squash-merge detection: compare branch tree to PR merge commit tree
git diff <pr-merge-sha>..<branch> --stat

# File-level diff inventory
git diff main...<branch> --stat
git diff main..<branch> -- <file>

# Search main for "did this work land?" patterns
git log main --oneline --grep='<keyword>' -i
```

## Appendix B: Key Source Files Referenced

- `D:\prj\RTest\RTest\src\RTLSDRCore\RadioReceiver.cs` (line 1143: still-unfixed per-batch allocation)
- `D:\prj\RTest\RTest\src\Radio.Web\Services\Hub\GvBridgeHubService.cs` (already in main, slated for deletion by SIP plan)
- `D:\prj\RTest\RTest\src\Radio.Web\Services\Hub\GvTrunkHubService.cs` (already in main)
- `D:\prj\RTest\RTest\src\Radio.Web\Services\ApiClients\GvBridgeApiService.cs` (already in main, slated for DTO update)
- `D:\prj\RTest\RTest\src\Radio.Web\Components\Pages\PhonePage.razor` (mode selector + status chips to update)
- `D:\prj\RTest\RTest\src\Radio.Web\Models\ApiModels.cs` (`GvBridgeStatusDto` to update)
- `D:\prj\RTest\RTest\src\Radio.API\Health\BluetoothHealthCheck.cs` (already in main via #347)
- `D:\prj\RTest\RTest\src\Radio.Infrastructure\Audio\Services\BluetoothCaptureWatchdog.cs` (already in main via #390 — newer than the preserved BT reliability branch)
- `D:\prj\RTest\RTest\deploy\common\radio-bt-setup.sh` (already in main via #347)
- `D:\prj\RTest\RTest\docs\rotaryphone-sip-integration-prompt.md` (planning doc on `feature/rotaryphone-sip-integration` branch only — not on main)
- `D:\prj\RTest\RTest\docs\plans\2026-03-30-rotaryphone-sip-ui-update.md` (planning doc on `feature/rotaryphone-sip-integration` branch only)
- `D:\prj\RTest\RTest\design\research\branch-cleanup-manifest.md` (the manifest being surveyed)

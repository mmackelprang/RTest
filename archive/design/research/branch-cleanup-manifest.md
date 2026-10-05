# Branch Cleanup Manifest

Generated during 2026-05-23 multi-PR session. Use this as the authoritative list when executing Item C (branch cleanup) after PR #410, #411, and any Item B follow-up PRs merge.

## DO NOT DELETE — preserve per user direction (2026-05-23)

These branches contain WIP we want to pick back up. The user explicitly flagged them on 2026-05-23. Leave both local and remote refs intact.

| Branch | Status | Reason |
|---|---|---|
| `feature/rotaryphone-sip-integration` | Old (March 2026), in sync with origin | WIP — pick back up to complete rotary-phone integration |
| `feature/gvbridge-kiosk-integration` | Old, in sync | WIP — same arc as above |
| `feature/capture-lifecycle-fix-and-instrumentation` | Old, in sync | WIP — same arc |
| `feature/bt-audio-reliability-infrastructure` | Old, in sync | WIP — same arc |
| `fix/sdr-gc-pressure-allocation` | Old, in sync | WIP — same arc |
| `claude/check-repo-status-hAc1F` | Old, in sync | WIP — same arc |
| `feature/bt-management-pbap-sync-ui` | Old (2026-03-12), local-only (no remote) — last commit *"cache MergedContacts list instead of recomputing on every access"* | Added to preserved list 2026-05-23 — adjacent to the rotary-phone integration arc (PBAP/contact UI work) |

## Safe to delete (already merged or stale agent-worktree refs)

These were verified merged via PR or are throwaway agent worktrees.

### Local + remote branches whose work is merged

| Branch | Merged via | Action |
|---|---|---|
| `feat/bt-input-resampler` | PR #404 | `git branch -d` + `git push origin --delete` |
| `feat/bt-drift-compensation-refinement` | PR #402 | `git branch -d` + `git push origin --delete` |
| `feat/buffer-drift-observability` | PR #400 | Verify merged; then delete |
| `feat/bt-autoswitch-gate` | Earlier PR | Verify; delete if merged |
| `feat/bt-capture-watchdog` | Earlier PR | Verify; delete if merged |
| `feat/bt-codec-observability` | Earlier PR | Verify; delete if merged |
| `chore/rtest-ci-appserver-migration` | PR #395 | `git branch -d` + remote delete |
| `fix/pw-registry-listener-symbol-resolution` | PR #396 | `git branch -d` + remote delete |
| `fix/deploy-units-and-sysload-script` | Verify | Delete if merged |
| `fix/wp-bt-route-exclusivity` | Verify merge state | Delete if merged; otherwise PR or preserve |
| `fix/device-display-config-reload` | **PR #408** (merged 2026-05-22) — local commit `9461796` duplicates merged `7f0e0ec` on main | `git branch -D` (force, since not on main) + `git push origin --delete` |
| `fix/output-exclusive-startup` | **PR #409** (merged 2026-05-23) | `git branch -d` + remote delete |
| `fix/bt-album-art` | **PR #410** (open as of cleanup time — verify merged first) | Delete only after merge |
| `fix/bt-fingerprint-reattach-on-recovery` | **PR #411** (open as of cleanup time — verify merged first) | Delete only after merge |

### Throwaway agent-worktree branches

All `worktree-agent-*` branches are residual from agent work, no longer needed:
```
worktree-agent-a499dbd5b007ba852
worktree-agent-ab11363353a75db6c
worktree-agent-ab8962a6aece8ac6b
worktree-agent-a6c8894dbfa749bbf
worktree-agent-a2874a1c16dff488b
worktree-agent-a8ee81d07f6329846
worktree-agent-a0ab87da78d28c398
worktree-agent-a0b7ce3fad0f1bf9e
worktree-agent-a0d9338a3656808ca
worktree-agent-aa337fdcb7b7da788
worktree-agent-add8164cebb6e21dd
worktree-agent-a8ee81d07f6329846
worktree-agent-af5267832c28c2d8c
```

All local-only, no upstream. Delete with `git branch -D <name>` (force, since not on main).

## Cleanup procedure (when authorized)

1. Confirm PR #410, #411, and any "Stop Casting" UI PR are MERGED first.
2. `git checkout main && git fetch origin --prune && git pull`
3. For each branch in "Safe to delete" → verify merge state via `gh pr list --state merged --head <branch>` OR check the merge status manually.
4. Run local + remote deletes per the action column.
5. Run `git remote prune origin` to clean dangling remote refs.
6. **DO NOT TOUCH** any branch in the "DO NOT DELETE" table above.

Recommended: pause for user confirmation before any `git push origin --delete <branch>` operation.

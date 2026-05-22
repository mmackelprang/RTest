# RTest CI — Migrate to self-hosted `appserver` runner

> **For Claude:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Move RTest's 4 Linux-targeted GitHub Actions workflows from `runs-on: ubuntu-latest` (GH-hosted, consumes Mark's monthly Actions minutes pool) to the self-hosted `appserver` runner (`192.168.86.167`, labels `[self-hosted, linux, x64, appserver]`) already provisioned for FamilyWorkspace. Eliminates the monthly Actions-minutes ceiling that bit during the cast/BT research arc's Phase 1 rollout.

**Architecture:** The `appserver` runner is **already online** — installed and verified for FamilyWorkspace in FW PR #618 / branch `chore/appserver-setup` (2026-05-17). RTest's migration is purely a `runs-on` flip in 4 workflow files plus a runner-readiness audit for the .NET 10 SDK + Playwright system deps + pwsh that RTest's workflows depend on. No application code changes. No infrastructure provisioning. The runner is ephemeral (Docker-isolated per job), so RTest CI cannot interfere with FW CI state.

**Tech Stack:** GitHub Actions workflow YAML, the existing `appserver` runner Docker image, `actions/setup-dotnet@v4` action for SDK install (already present in the workflows).

**Source research / model:** [`D:\prj\FamilyWorkspace\docs\plans\2026-05-17-phase-5b-fw-appserver-deploy.md`](../../../../FamilyWorkspace/docs/plans/2026-05-17-phase-5b-fw-appserver-deploy.md) is the FW deploy plan whose phase-4 sister-PR provisioned the runner. The migration pattern in FW's [`ci.yml` header comment](../../../../FamilyWorkspace/.github/workflows/ci.yml) is the canonical reference for RTest to mirror.

---

## What's ALREADY in place (do NOT redo)

Background from FW's `chore/appserver-setup` (merged 2026-05-17):

- Ubuntu 24.04 LTS host at `192.168.86.167` ("appserver.lan"); 16 CPU / 31 GB RAM / 824 GB free.
- Docker Engine CE 29.2.0 on default context; `mmack` in `docker` group.
- Self-hosted GitHub Actions runner online with labels `[self-hosted, linux, x64, appserver]`. **Ephemeral container** per job — each job gets a fresh container; apt-installed packages do NOT persist across jobs unless baked into the runner Docker image.
- Runner orchestration: `D:\prj\FamilyWorkspace\infra\runner\compose.runner.yml` + `/srv/gha-runners/.env` on the box (mode 600, PAT).
- Caddy + TLS for `*.appserver.lan` (not used by RTest — RTest CI doesn't serve HTTP from this host).

What does **NOT** yet exist for RTest:

- No `appserver` `runs-on` in any RTest workflow.
- No documented confirmation that .NET 10 SDK installs cleanly inside the runner's ephemeral container.
- No confirmation that the `pwsh` + Playwright-browser system deps that `build.yml` step 27 requires resolve correctly inside the runner.

---

## Locked decisions (do not re-litigate)

1. **Share the generic `appserver` label, do NOT add an `appserver-rtest` label.** Two reasons: (a) the runner is ephemeral so per-job isolation is structural; (b) adding a project-specific label requires re-registering the runner with the new label set, which adds operational surface for marginal benefit. If runner queue contention becomes a real problem (e.g. RTest + FW jobs serialize behind each other and slow each other down), revisit later.
2. **Migrate only the 4 Linux workflows.** `audio-uat.yml` uses `runs-on: windows-latest` and stays untouched — the appserver runner is Linux-only. Audio UAT workflow continues to consume Windows GH-hosted minutes (a smaller monthly quota; not the constraint that bit us).
3. **Use `actions/setup-dotnet@v4` to install .NET in the ephemeral container** — same action the workflows already use against `ubuntu-latest`. No "bake .NET into the runner Docker image" optimization in this plan; that's a deferred future improvement.
4. **Advisory-mode policy** (per FW's pattern) — CI on the appserver runner is a second-opinion signal; the Builder's local-gate output remains the merge-blocking truth, since `main` branch protection is not enabled. Document this in the workflow header comments.
5. **Canary period not required** for RTest. FW kept a `self_hosted_smoke` job for ~1 week after migration as a canary; RTest's migration is a much smaller delta (single `runs-on` flip per workflow, no compose/Caddy/DB concerns) and the FW canary period has already elapsed in production. If the very first RTest CI run on the appserver runner fails for runner-specific reasons (network, disk, container state), revert per the rollback path below.

---

## Audit findings the plan incorporates

From the grounding read of `.github/workflows/build.yml`, `audit-configuration.yml`, `pages-docs.yml`, `todo-to-issues.yml`, `audio-uat.yml`:

- **`build.yml`** runs `dotnet build` + `dotnet test` + NuGet pack of 5 packages, on `push: main, develop` and `pull_request: main`. Uses `dotnet-version: 10.0.x`, **and step 27 invokes `pwsh tests/Radio.Web.E2ETests/bin/Release/net10.0/playwright.ps1 install --with-deps`** — this requires `pwsh` to be available in the runner's container, plus the Playwright `--with-deps` install needs `apt` and system libraries. Both are reasonable to expect on a Linux runner; verify in Task 1.
- **`audit-configuration.yml`** runs weekly on a cron (Mondays 09:00 UTC) plus `workflow_dispatch`. Uses Python 3.x via `actions/setup-python@v4`. Lightweight.
- **`pages-docs.yml`** runs on `push` and `workflow_dispatch`. Builds the docs site for GitHub Pages. Lightweight.
- **`todo-to-issues.yml`** runs weekly on a cron (Mondays 09:00 UTC) plus `workflow_dispatch`. Uses a third-party `alstr/todo-to-issue-action@v5`. Lightweight.
- **`audio-uat.yml`** runs on `push`/`pull_request` to `main`. **`runs-on: windows-latest`.** Out of scope for this plan.
- **Branch protection** is off on `main` (verified via `gh api repos/mmackelprang/RTest/branches/main/protection` during the cast/BT arc). So CI is advisory-only; merges proceed without CI gating. The migration does not change this — it only changes which runner executes the second-opinion signal.

---

## Success criteria

- All 4 migrated workflows complete a green run on the `appserver` runner within their normal time budget (within +50% of prior `ubuntu-latest` durations is fine; significantly slower indicates a runner-image issue worth addressing).
- The next PR opened against `main` after this plan merges triggers CI on the `appserver` runner (verifiable via the run page showing the new label set).
- Mark's GitHub Actions monthly minutes consumption drops to roughly just `audio-uat.yml` × (push + PR cycle count) — the Linux 4 no longer charge.
- Rollback path (revert one PR) is documented and demonstrably reversible.

---

## Task 0: Runner-readiness verification (operator action — Mark, not Builder)

**This task is NOT executed by the Builder.** It's Mark's pre-flight check. The Builder consumes Mark's findings to gate the YAML edits.

**Step 1:** Mark SSHes to `mmack@appserver` (the FW host) and confirms the runner container has — or can `apt`-install at job time — the tools the workflows need:

```bash
ssh mmack@appserver "docker ps --format '{{.Names}}' | grep gha-runner"
# Expected: a running ephemeral runner container (e.g. gha-runner-fw-1 or similar)
```

For each tool the workflows need, run the equivalent installability check **inside a fresh runner container** (use `compose.runner.yml`-style spawn or attach to a job's transient container — operator's call):

```bash
# .NET 10 SDK (build.yml + audio-uat.yml)
dotnet --version || (apt-get update && apt-get install -y dotnet-sdk-10.0)

# pwsh (build.yml step 27 — Playwright install)
which pwsh || (apt-get update && apt-get install -y powershell)

# Playwright system deps (build.yml step 27 with --with-deps)
# The action self-installs via apt-get; just confirm apt-get works as root
sudo apt-get update

# Python 3.x (audit-configuration.yml)
python3 --version
```

**Step 2:** Mark reports back to the Builder which of these need a runner-image rebake vs which `actions/setup-*` already handles inline. The cleanest split is:

- `dotnet`: `actions/setup-dotnet@v4` handles install per-job — **no image change needed**.
- `python`: `actions/setup-python@v4` handles install per-job — **no image change needed**.
- `pwsh` + Playwright system deps: per-job apt-install is acceptable for short-term, ~30s overhead per build run. **Image bake is a deferred optimization** — not in scope for this plan.

**Step 3:** Mark confirms the runner has outbound HTTPS access to `nuget.org`, `api.nuget.org`, `playwright.azureedge.net` (for browser download), `pypi.org`, and `dotnetcli.azureedge.net`. The FW migration already validated `nuget.org` and `dotnet`-equivalent endpoints; remaining ones can be confirmed empirically by Task 3's smoke run.

**Exit condition of Task 0:** Mark reports "go" — Builder proceeds to Task 1. Until then, the migration is paused.

---

## Task 1: Add runner-placement header comments to each migrated workflow

**Files:**
- Modify: `.github/workflows/build.yml`
- Modify: `.github/workflows/audit-configuration.yml`
- Modify: `.github/workflows/pages-docs.yml`
- Modify: `.github/workflows/todo-to-issues.yml`

**Step 1:** Prepend the following header comment block to each of the 4 workflow files (insert immediately after the `name:` line):

```yaml
# Runner placement (since 2026-05-22, RTest CI appserver migration):
#   - Jobs run on the self-hosted appserver runner (labels:
#     [self-hosted, linux, x64, appserver]) — eliminates the GH Actions
#     monthly-quota dependency that surfaced during the cast/BT research
#     arc Phase 1 rollout.
#   - CI is ADVISORY: main branch protection is off; the Builder's local
#     gates (dotnet build / dotnet test) are the merge-blocking truth.
#     CI here is a second-opinion signal.
#   - Rollback: revert this PR — runs-on flips back to ubuntu-latest.
#
# audio-uat.yml stays on windows-latest (intentional — Windows-only
# feature surface; appserver runner is Linux).
```

(adapt the wording slightly per file — `audit-configuration.yml` runs weekly so emphasize the cron-vs-PR distinction; `pages-docs.yml` references the static-site build; the exact phrasing isn't critical, but the rollback note and "advisory" framing IS critical.)

**Step 2:** Build verification — none for this task (comment-only change).

**Step 3:** Commit:

```bash
git add .github/workflows/build.yml \
        .github/workflows/audit-configuration.yml \
        .github/workflows/pages-docs.yml \
        .github/workflows/todo-to-issues.yml
git commit -m "docs(ci): add appserver runner-placement header comments to 4 Linux workflows"
```

---

## Task 2: Flip `runs-on` from `ubuntu-latest` to the appserver label set

**Files:**
- Modify: `.github/workflows/build.yml` (1 occurrence at `jobs.build.runs-on`)
- Modify: `.github/workflows/audit-configuration.yml` (1 occurrence)
- Modify: `.github/workflows/pages-docs.yml` (1 occurrence)
- Modify: `.github/workflows/todo-to-issues.yml` (1 occurrence)

**Step 1:** In each of the 4 files, replace exactly:

```yaml
    runs-on: ubuntu-latest
```

with:

```yaml
    runs-on: [self-hosted, linux, x64, appserver]
```

(Note YAML list syntax. The runner needs ALL four labels to match — the runner is registered with exactly this label set; any subset will fail to schedule.)

**Step 2:** No build verification possible from the dev host (CI execution is the verification).

**Step 3:** Commit:

```bash
git add .github/workflows/build.yml \
        .github/workflows/audit-configuration.yml \
        .github/workflows/pages-docs.yml \
        .github/workflows/todo-to-issues.yml
git commit -m "ci: migrate 4 Linux workflows to self-hosted appserver runner"
```

---

## Task 3: Smoke-test by opening the PR and observing the first CI run

**Step 1:** Push the branch and open the PR:

```bash
git push -u origin chore/rtest-ci-appserver-migration

gh pr create --title "ci: migrate RTest Linux workflows to self-hosted appserver runner" --body "..."
```

Use this template for the PR body:

```markdown
## Summary

Flips 4 of RTest's 5 GitHub Actions workflows from `runs-on: ubuntu-latest` (GH-hosted) to `runs-on: [self-hosted, linux, x64, appserver]` — the same runner FamilyWorkspace migrated to in PR #618 / `chore/appserver-setup` (2026-05-17).

`audio-uat.yml` stays on `windows-latest` (intentional — Windows-only feature surface).

## Why

The cast/BT research arc's Phase 1 rollout (PRs #389/#390/#391) ran into the monthly GH Actions minutes ceiling. Self-hosted runner sidesteps the quota constraint while preserving the second-opinion CI signal.

## What's NOT in this PR

- No runner provisioning (already done by FW PR #618).
- No image bake (per-job `apt-install` of pwsh + Playwright deps is acceptable short-term overhead).
- No branch-protection change (CI is advisory; Builder local gates remain merge-blocking truth).
- No Windows-runner change.

## Test plan

- [x] Per-workflow header comments added explaining the placement + rollback path
- [ ] First CI run on this PR completes (green or red) on the appserver runner — visible via `gh run view` showing the `[self-hosted, linux, x64, appserver]` label set
- [ ] If green: merge and watch the next several PRs to confirm reliability
- [ ] If red with a runner-specific failure (network, disk, container state): revert this PR per the rollback path documented in the workflow headers

## Rollback

`git revert <merge-commit>` — both `runs-on` lines flip back to `ubuntu-latest`. The runner stays online (it's shared with FW); no infrastructure changes.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
```

**Step 2:** Watch the CI runs. **Do NOT auto-merge** on this PR — Mark verifies the first appserver-runner CI cycle himself, because:

- The build runs full `dotnet test` across 11 projects + Playwright install + NuGet pack. ~3-4 minutes typical.
- The audit-configuration workflow won't naturally trigger from this PR's `pull_request` event (it's `cron`-only). Verify via `gh workflow run audit-configuration.yml --ref chore/rtest-ci-appserver-migration` after the PR is open.
- pages-docs.yml runs on `push` — will trigger when the branch is pushed. Watch it.
- todo-to-issues.yml is `cron`-only too. Skip for this PR's verification; will execute on its next scheduled Monday or via manual dispatch post-merge.

**Expected results:**

- `build` on `appserver` runner: passes within +50% of the prior `ubuntu-latest` duration (baseline ~3-5 min → expect 3-7 min).
- `Run AudioUAT (--all)` (Windows): unchanged — still on `windows-latest`.
- `GitGuardian Security Checks`: unchanged — runs externally to the workflow YAMLs.

**Step 3:** If green across the board: Mark approves and merges via `gh pr merge <PR#> --squash --delete-branch`. If red with a runner-specific signal: Mark reverts and we open a follow-up issue describing what blew up.

---

## Task 4: Post-merge verification (operator action — Mark)

Once the PR merges to `main`:

**Step 1:** Mark watches the next few PRs (from any source — could be Phase 3 ideas, new feature work, or just a small follow-up) for runner-related reliability issues. Specifically:

- Does the runner accept the job promptly (<30 s queue time)?
- Does `apt-install pwsh` succeed in the ephemeral container?
- Does Playwright browser install (`--with-deps`) complete without missing-package errors?
- Does the GitHub Pages publish still work for `pages-docs.yml`?

**Step 2:** If any of the above fail for runner-image reasons, file an issue against this plan and consider the deferred optimization "bake pwsh + Playwright system deps into the runner Docker image" — that's a follow-up plan.

**Step 3:** Once the next Monday's `audit-configuration.yml` and `todo-to-issues.yml` crons fire and complete green, the migration is fully validated.

---

## Out of scope

- **Image bake (pwsh + Playwright deps into the runner Docker image).** Per-job apt-install adds ~30 s overhead per build run. Worth optimizing later if RTest CI volume picks up; not in scope here. A separate plan can add a multi-stage Dockerfile to the FW `infra/runner/` tree if Mark wants to do this.
- **Migrating `audio-uat.yml` off `windows-latest`.** Windows runner availability is its own discussion. If Windows GH-hosted minutes become a constraint, options are: (a) buy a Windows Actions plan increment, (b) acquire a Windows self-hosted runner.
- **Branch protection on `main`.** This plan does NOT enable required CI checks. CI remains advisory while the project's appetite for blocking merges on a flaky-or-recently-migrated runner is still being calibrated. Future plan can flip this when confidence is established.
- **The `audit-configuration.yml` schedule moving off cron.** That workflow runs weekly; the cron remains independent of the runner change.
- **NuGet package consumer impact.** The migrated `build.yml` still publishes NuGet artifacts via `actions/upload-artifact@v4`. Storage stays in GitHub Actions artifact storage (not the appserver) — no consumer change.
- **Anything affecting FW's CI.** The runner is shared via labels; FW's workflows are unchanged.

---

## References

- FW migration commit: see `D:\prj\FamilyWorkspace\.github\workflows\ci.yml` header comments (lines 1-25 of the version on FW's `master`).
- FW deploy plan: `D:\prj\FamilyWorkspace\docs\plans\2026-05-17-phase-5b-fw-appserver-deploy.md`.
- FW operator setup log: `D:\prj\FamilyWorkspace\docs\operator\appserver-setup-2026-05-16.md` (referenced by FW's plan; provides the runner-registration commands).
- RTest CI run history that motivated this plan: `gh run list --branch main --workflow build.yml` showing the run-failure pattern across the cast/BT arc.

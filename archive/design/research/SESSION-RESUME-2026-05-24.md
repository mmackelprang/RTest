# Session Resume State — 2026-05-24

**Saved:** end of 2026-05-24 evening session, before user-initiated Claude restart.

**Purpose:** Next Claude session reads this to pick up where the previous session left off without re-doing context discovery.

---

## TL;DR — pick up here

1. **All shipped work is MERGED + deployed.** Nothing in flight.
2. **Phase B (RotaryPhone repo work) is blocked on 3 small user questions** — see "Pending user decisions" below.
3. **Playwright MCP disconnected** — visual UATs require either the user driving the browser or MCP reconnection.
4. **One open visual UAT** (PR #423) waiting on the above.
5. **One open long-uptime UAT** (PR #421 SDR fix) — user verifies via `dotnet-counters` after 24h+.

---

## Workflow contract in force

The user established this contract earlier in the session for the autonomous multi-PR workflow:

> "merge after tests and code reviews pass, deploy after merging, and I'll UAT this tomorrow"

Adapted further to:
- Builder ships PR (no UAT pause)
- Coordinator dispatches `feature-dev:code-reviewer` on the PR
- Coordinator applies any BLOCKER + MAJOR findings via Builder follow-up message (skip NITs unless explicitly authorized)
- Coordinator merges via `gh pr merge <n> --squash --delete-branch`
- Coordinator deploys via `pwsh -NoProfile -Command "./deploy/Deploy-ToLinux.ps1 -TargetHost radio -Runtime linux-x64"`
- Coordinator drives visual UAT via Playwright (when available) and surfaces screenshots
- All from `main` after merge — use temporary worktree if main checkout is blocked by another Builder's uncommitted edits

**Important caveat:** Multiple parallel Builders sharing the same working directory caused contamination during this session. Future multi-Builder dispatches should explicitly request isolated worktrees in the Builder prompt.

---

## Today's complete delivery (PRs shipped this session)

| PR | Title | State | Notes |
|---|---|---|---|
| **#421** | `fix(sdr): eliminate per-batch allocation in FM stereo decimation path` | MERGED `67de0f6` + deployed | Long-uptime UAT pending — user verifies via `dotnet-counters` Gen0/Gen1 over 24h+ |
| **#422** | `fix(web): wire SQLite config bridge into Radio.Web (TimeFormat/RdsScroll user saves now take effect)` | MERGED `b315c2b` + deployed + visual UAT PASSED ✓ | Topbar showed `7:36:45 PM` post-fix vs pre-fix `19:36`. Screenshot delivered to user. |
| **#423** | `refactor(web): rotary-phone API baseline cleanup (Phase A — no RotaryPhone-side changes)` | MERGED `d208cdd` + deployed | Visual UAT pending Playwright reconnection. Builder confirmed clean post-deploy startup (no GvBridgeHub references in logs). |

Earlier in the session: PRs #409-#420 (audio output exclusivity, BT album art, BT fingerprint flag, Stop Casting UI, time format feature, RDS scroll feature + revision, sleep weather forecast + redesign + icon fix + current conditions) — all MERGED + deployed + UAT verified.

Branch cleanup also completed: 43 obsolete branches deleted (15 from manifest + 28 from audit), 12 worktree-agent refs removed, 2 dead remote refs pruned.

---

## Pending user decisions (blocks next dispatch)

### For Phase B (RotaryPhone repo work — see `design/research/rotaryphone-phase-b-server-additions-plan.md`)

Three small questions from §10 of the plan. My recommended defaults:

| Q | Question | My default |
|---|---|---|
| **Q-C** | Cookie endpoints — no auth (LAN trust model) vs add per-endpoint auth? | **No auth** — document in boundary doc. Matches RotaryPhone's existing trust model. |
| **Q-D** | POST body shape — `{ rawCookieHeader }` vs individual fields vs both? | **Both, raw preferred** — Google requires extras (SIDCC, NID, __Secure-1PSIDCC); raw header is safest passthrough. |
| **Q-F** | SignalR push for GV status changes vs continue 10s polling? | **Skip push** — 10s poll is fine, less complexity. |

If user answers (or says "use defaults"), dispatch the RotaryPhone-side Builder for Phase B (see plan doc for two-PR sequencing: RP-PR1 = Q5+Q3+Q4 additive, ~3h; RP-PR2 = Q6 cookie management, ~4h).

After Phase B PRs deploy → dispatch RTest **Phase C** Builder consuming the new APIs (two-badge UI, cookie management UI in SystemConfigPage, HT801 dashboard chip, audio-bridge stats, diagnostics consumption).

---

## Standing queue (not blocking, user prioritizes)

| Item | Notes |
|---|---|
| **Visual UAT of PR #423** | "GV Available/Unavailable" badge + "GV API" button on PhonePage. Needs Playwright OR user-driven browser test. |
| **Long-uptime UAT of PR #421** | Wait 24h+; SSH to `radio`, `dotnet-counters monitor -p $(pgrep -f radio-api) System.Runtime --refresh-interval 5`, verify Gen0/Gen1 collection rates dropped vs pre-fix baseline (was 14M Gen0 + 195K Gen1 per observation interval). |
| **Cross-process hot-reload for SQLite bridge** | Deferred from PR #422 — when user saves a Display/RDS setting via API's System Config page, Web's `IOptionsMonitor` doesn't see it until next circuit init (page reload). Low priority for kiosk. |
| **Cast garbled audio** | Investigation doc at `design/research/cast-garbled-audio-investigation.md` identifies H9 (Cast lifecycle churn) as top hypothesis, plus instrumentation needs (receiver telemetry). 5 user questions in §6 needed before next step. Awaiting user answers when ready. |
| **Cast latency reduction** | Research at `design/research/cast-latency-comparison.md` (33 sources, 5 ranked recommendations). #1 is "tighten DirectChannel buffer params (~1h, drops latency 2-3s → 400-700ms)" — BUT debugger explicitly warned not to ship this without instrumentation first (could turn intermittent dropouts into constant ones). Sequencing: instrument receiver telemetry → measure → tune. |
| **Audit `docs/post-extraction-cleanup` remote branch** | Old branch (March 2026), not merged, never PR'd. User said "leave for now" earlier. |

---

## Key reference docs (read these to ramp up)

### Architecture
- `design/adr/ADR-weather-data-source.md` — NWS weather data source ADR

### Active investigations / plans
- `design/research/rotaryphone-api-state-2026-05-24.md` — what the RotaryPhone server currently exposes vs RTest expects (drift analysis)
- `design/research/rotaryphone-phase-b-server-additions-plan.md` — Phase B server-side additions plan (Q2-Q6 features the user authorized)
- `design/research/rotary-phone-arc-revival-survey.md` — original survey of preserved branches (most were already merged; only SDR + planning docs were real)
- `design/research/cast-garbled-audio-investigation.md` — Cast garbled audio root cause analysis (H1-H9 hypotheses)
- `design/research/cast-latency-comparison.md` — Cast latency reduction research (33 sources)
- `design/research/branch-cleanup-manifest.md` — branch cleanup manifest (updated; preserved-list now just `feature/bt-management-pbap-sync-ui` and `feature/rotaryphone-sip-integration`)

### Design handoffs (from this session — for context if iterating)
- `docs/design-handoffs/HANDOFF-configurable-time-format.md`
- `docs/design-handoffs/HANDOFF-rds-accumulating-scroll.md` + `HANDOFF-rds-inline-scroll-revision.md`
- `docs/design-handoffs/HANDOFF-sleep-mode-weather-forecast.md` + `HANDOFF-sleep-weather-visual-redesign.md` + `HANDOFF-sleep-weather-current-conditions.md`
- `docs/design-handoffs/HANDOFF-stop-casting-menu-item.md`

---

## Workspace state at save time

### Git
- On branch `fix/web-sqlite-config-bridge` locally (PR already merged; branch should be cleaned up)
- `main` ahead of where local main was last seen — fast-forwardable via `git fetch && git pull`
- Local branches still present:
  - `feat/rotary-phone-baseline-cleanup` (PR #423 merged — safe to delete)
  - `feature/rotaryphone-sip-integration` (preserved per user — has planning docs only)
  - `main`
- Worktrees: only main repo at `D:/prj/RTest/RTest`. All previous .claude/worktrees have been cleaned up except as listed.

### Deployed on `radio` host (Ubuntu N100)
- API + Web running commit `d208cdd` (PR #423, deployed 23:42:51 UTC)
- Both services active, healthy
- No new errors in startup journal post-PR-#423 deploy

### Caveats
- **Playwright MCP disconnected** — error `-32000` on reconnect. User will restart Claude.
- **Cross-agent worktree contamination occurred earlier** — resolved by using temp worktrees for deploys. Future dispatches should request isolated worktrees in Builder prompts.

---

## How to resume (cheat sheet for next session)

1. **First action:** read this file (`design/research/SESSION-RESUME-2026-05-24.md`)
2. **Verify state:**
   - `git fetch origin && git log --oneline origin/main -5` — should see `d208cdd refactor(web): rotary-phone API baseline cleanup (Phase A) (#423)` at top
   - `ssh mmack@radio "curl -s http://localhost:5000/api/health/version | head -1"` — should show `d208cdd`
3. **Ask user:** "Ready to proceed? Reminders: (a) Phase B blocked on Q-C/Q-D/Q-F answers, (b) PR #423 visual UAT pending Playwright reconnect, (c) PR #421 long-uptime UAT pending."
4. **Default workflow contract:** Builder → code review → fix BLOCKER/MAJOR → merge → deploy → visual UAT. NITs skipped unless authorized. Use isolated worktrees for parallel Builders.

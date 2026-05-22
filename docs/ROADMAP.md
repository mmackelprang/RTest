# Roadmap

Forward-looking catalog of major upcoming work, organized by maturity stage. New items typically enter as **research arcs**, get **scoped into plans**, then **implemented as PRs**.

Companion docs:
- [`design/WORK-LOG.md`](../design/WORK-LOG.md) — chronological record of what's been done
- [`design/FUTURE-WORK.md`](../design/FUTURE-WORK.md) — stubbed features pending implementation
- [`design/DECISION-LOG.md`](../design/DECISION-LOG.md) — architectural decisions
- [`docs/plans/`](plans/) — scoped plans ready for implementation
- [`docs/research/`](research/) — research output (not yet committed work)

---

## Research arcs (research output; not yet planned implementation)

Research arcs explore a problem space deeply enough that *if* the team decides to act, the work is informed by data rather than guesswork. Each arc carries an explicit **non-goal of producing implementation work** — the research output is consumed later by a separate plan, with the team choosing which ideas (if any) to act on.

### Cast stutter comparison + BT audio stabilization (2026-05-21 → 2026-05-22)

**Branch**: `research/cast-bt-comparison` → merged to `main`
**Docs**:
- [`docs/research/2026-05-21-cast-stutter-comparison.md`](research/2026-05-21-cast-stutter-comparison.md) — Cast (HttpMp3 + DirectChannel) vs SoundCloud + Plex reference systems
- [`docs/research/2026-05-22-bt-audio-stabilization.md`](research/2026-05-22-bt-audio-stabilization.md) — RTest BT-as-source vs PW-stock + bluez-alsa + AOSP-BT

**Output**:
- **2 docs**, ~700 lines combined
- **8 failure modes for Cast** × 4 systems = 32-cell matrix (filled)
- **11 failure modes for BT** × 4 systems = 44-cell matrix (filled)
- **10 + 11 pipeline rows** (filled across all systems)
- **6 + 6 synthesis patterns** identifying where RTest differs from the reference cluster
- **11 + 5 speculative ideas** (8 Cast-specific + 3 system-isolation shared + 5 BT-specific), each with full 5-block measurement methodology so a debug agent can demonstrably show a change is an *actual* improvement (Evidence → Baseline probe → Post-change probe → Success criterion → Verification steps)
- **Concurrent-load discipline** baked in: every probe captures `PROBE-SYS-LOAD` (vmstat / iostat / pidstat / log-rate / SSH-session-count) alongside the audio probe; success criteria must hold under both light-load and heavy-load scenarios

**Status**: research complete. No implementation commitments. Live-inspection probes pending LAN access (Plex receiver source walk, MSE buffered-range readouts, `pw-top` + `btmon` captures, 72 h soak runs). Probe scripts in `scripts/research/` declared as research deliverables, not yet authored.

**How this becomes work**: when the team decides to act on any idea, the next step is a scoped plan in [`docs/plans/`](plans/) that consumes that idea's 5-block measurement structure as its acceptance criteria. The plan owns implementation; the research doc retains the rationale + measurement contract.

---

## Planning queue (scoped, ready for implementation)

### Cast/BT research — Phase 1 + Phase 2 (selected 2026-05-22)

Consumed from the cast/BT research arc per embedded-audio-engineering prioritization (transcript record). Sequenced and scoped in [`docs/plans/2026-05-22-cast-bt-phase-1-2-arc.md`](plans/2026-05-22-cast-bt-phase-1-2-arc.md).

**Phase 1 — stop the bleeding** (3 plans, ~280 LOC; can run in any order or parallel):

| Plan | Idea | Addresses | Branch |
|---|---|---|---|
| [`2026-05-22-bt-capture-watchdog.md`](plans/2026-05-22-bt-capture-watchdog.md) | #12 Watchdog on `OnProcess` interval | FM-BT-3 (known long-uptime quiescence bug) | `feat/bt-capture-watchdog` |
| [`2026-05-22-bt-autoswitch-gate.md`](plans/2026-05-22-bt-autoswitch-gate.md) | #13 Gate `autoSwitchOnConnect` on PW node existence | FM-BT-1 (autoSwitch retries on missing PW node) | `feat/bt-autoswitch-gate` |
| [`2026-05-22-bt-codec-observability.md`](plans/2026-05-22-bt-codec-observability.md) | #15 Surface negotiated codec + bitpool as observable metric | FM-BT-6 (codec quality degradation currently invisible) | `feat/bt-codec-observability` |

**Phase 2 — isolate the audio path** (2 plans, ~165 LOC; ships immediately after Phase 1 completes; CPU affinity before PW event subscription):

| Order | Plan | Idea | Addresses | Branch |
|---|---|---|---|---|
| 1 | [`2026-05-22-audio-thread-isolation.md`](plans/2026-05-22-audio-thread-isolation.md) | #9 Pin radio-api to dedicated CPU cores + SCHED_FIFO | FM2 + FM8 + FM-BT-11 (load contention) | `feat/audio-thread-isolation` |
| 2 | [`2026-05-22-pw-event-subscription.md`](plans/2026-05-22-pw-event-subscription.md) | #14 Replace pw-cli scraping with PipeWire event subscription | FM-BT-1 + FM-BT-2 (eliminates scrape race) | `feat/pw-event-subscription` |

**Shared scaffolding** (research-tier code in `scripts/research/`, declared per-plan in Task 0): `sysload_capture.sh`, `sysload_correlate.py`, `bt_stall_detect.py`, `bt_autoswitch_audit.py`, `bt_codec_observability_probe.sh`, `heavy_load_harness.sh`, `bt_pair_cycle_harness.sh`, plus the per-plan `*_compare.py` PASS/FAIL gates.

**Phase 3+ ideas remain in research** until Phase 1+2 measurement points the finger.

### Active follow-up arc — BT clock-skew "underwater" audio (selected 2026-05-22)

PR #400's observability surfaced 217–391 ppm clock skew between the BT phone clock and the local speaker clock during BT → Cast playback — ~10–20× over BT A2DP spec. The skew is unfixable in software (two crystals cannot be sync'd); the goal is to make the compensation that masks it less audible.

- **Research**: [`docs/research/2026-05-22-bt-clock-skew-measurement.md`](research/2026-05-22-bt-clock-skew-measurement.md) — measurement methodology, architectural analysis, mitigation menu
- **Plan in flight**: [`docs/plans/2026-05-22-bt-drift-compensation-refinement.md`](plans/2026-05-22-bt-drift-compensation-refinement.md) — Path C: smaller, more-frequent compensation events with cosine-ramp crossfade smoothing. Branch `feat/bt-drift-compensation-refinement`.
- **Path D shipped** (PR #404 / commit `781a455`). UAT showed objective metrics REGRESSED across all 3 criteria — but diagnosis revealed this was a measurement artifact from a previously-undiagnosed dual-audio-path issue, not a Path D failure.

### Active follow-up arc — BT dual-routing fix + output picker UI (selected 2026-05-22)

`pactl list sink-inputs` on radio revealed **two independent audio paths** feeding the local soundbar simultaneously:
1. **Path A (designed)**: BT → Radio.API → audio engine → outputs
2. **Path B (rogue)**: BT → PipeWire stream-restore → default sink directly (bypassing Radio.API entirely)

The dual-path explains both Mark's "audio from both soundbar AND Cast" complaint AND the Path D regression (the resampler added latency to Path A while Path B remained delay-free, creating comb-filter "underwater" artifacts).

- **Research**: [`docs/research/2026-05-22-bt-dual-routing-investigation.md`](research/2026-05-22-bt-dual-routing-investigation.md)
- **Plan: WP rule (Part 1, critical-path)**: [`docs/plans/2026-05-22-wp-bt-route-exclusivity.md`](plans/2026-05-22-wp-bt-route-exclusivity.md) — WirePlumber config that prevents BT A2DP from auto-routing to default sink. Without this, Path D can't be measured cleanly. ~30 LOC infra config.
- **Plan: Output picker UI (Part 2, UX)**: [`docs/plans/2026-05-22-output-picker-ui.md`](plans/2026-05-22-output-picker-ui.md) — replaces the `MainLayout.razor:636-641` stub (currently `NavigationManager.NavigateTo("/devices")`) with a real popover. ~150-200 LOC mirroring `CastDeviceDropdown.razor`.

### CI infrastructure — RTest appserver runner migration

Selected as a follow-up after the cast/BT Phase 1+2 tranche revealed that the project's monthly GH Actions minutes pool can be a real blocker. Plan modeled on FamilyWorkspace's 2026-05-17 migration which already provisioned the runner.

| Plan | Branch | Scope |
|---|---|---|
| [`2026-05-22-rtest-ci-appserver-migration.md`](plans/2026-05-22-rtest-ci-appserver-migration.md) | `chore/rtest-ci-appserver-migration` | Flip 4 Linux workflows from `runs-on: ubuntu-latest` to `runs-on: [self-hosted, linux, x64, appserver]`. `audio-uat.yml` (Windows) stays. Advisory-mode CI; no branch-protection change. Mark verifies first run before merge. |

---

## Active in-progress

*Empty.*

When work is in active development on a feature branch, reference it here with the branch name, current state, and outstanding gates.

---

## Workflow notes

- **Research → Plan → Implementation**: research arcs don't directly produce code. A separate plan document in `docs/plans/` scopes the actual work, with acceptance criteria derived from the research's measurement-discipline blocks.
- **Branch hygiene**: every implementation lives on a short-lived branch (`feat/`, `fix/`, `docs/`) and merges via PR to `main`.
- **Measurement-driven implementation**: ideas with full 5-block measurement methodology (per the Cast + BT research arc) require the debug-agent verification step to actually run as part of PR acceptance — a change that doesn't measurably move its target metric is not merged.

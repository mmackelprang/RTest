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

---

## Active in-progress

*Empty.*

When work is in active development on a feature branch, reference it here with the branch name, current state, and outstanding gates.

---

## Workflow notes

- **Research → Plan → Implementation**: research arcs don't directly produce code. A separate plan document in `docs/plans/` scopes the actual work, with acceptance criteria derived from the research's measurement-discipline blocks.
- **Branch hygiene**: every implementation lives on a short-lived branch (`feat/`, `fix/`, `docs/`) and merges via PR to `main`.
- **Measurement-driven implementation**: ideas with full 5-block measurement methodology (per the Cast + BT research arc) require the debug-agent verification step to actually run as part of PR acceptance — a change that doesn't measurably move its target metric is not merged.

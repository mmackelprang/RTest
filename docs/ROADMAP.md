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

*Empty.*

When a plan lands in [`docs/plans/`](plans/), reference it here with its target acceptance criteria, dependencies, and rough scope.

---

## Active in-progress

*Empty.*

When work is in active development on a feature branch, reference it here with the branch name, current state, and outstanding gates.

---

## Workflow notes

- **Research → Plan → Implementation**: research arcs don't directly produce code. A separate plan document in `docs/plans/` scopes the actual work, with acceptance criteria derived from the research's measurement-discipline blocks.
- **Branch hygiene**: every implementation lives on a short-lived branch (`feat/`, `fix/`, `docs/`) and merges via PR to `main`.
- **Measurement-driven implementation**: ideas with full 5-block measurement methodology (per the Cast + BT research arc) require the debug-agent verification step to actually run as part of PR acceptance — a change that doesn't measurably move its target metric is not merged.

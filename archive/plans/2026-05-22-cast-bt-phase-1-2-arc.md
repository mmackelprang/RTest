# Cast/BT Research — Phase 1+2 Implementation Arc

> **For Claude:** This is an arc-overview document. It does not contain executable tasks. The plans for each idea live in the per-idea files referenced below. Use `superpowers:executing-plans` against each individual plan, in the sequence specified here.

**Source research**:
- [`docs/research/2026-05-21-cast-stutter-comparison.md`](../research/2026-05-21-cast-stutter-comparison.md)
- [`docs/research/2026-05-22-bt-audio-stabilization.md`](../research/2026-05-22-bt-audio-stabilization.md)

**Selection rationale**: The 16 ideas across both research docs were prioritized using embedded-audio-engineering judgment (transcript record). Phase 1 (3 ideas) addresses known production bugs and adds the observability needed to diagnose every subsequent BT issue. Phase 2 (2 ideas) is "standard embedded-audio practice debt" — things every real-time audio system should already have. Phases 3+ are deferred until Phase 1+2 data points the finger.

---

## Phase 1 — Stop the bleeding (3 plans, ~280 LOC)

Sequence within Phase 1 is **flexible** — the three plans touch disjoint code. They can be implemented in any order or in parallel.

| Plan | Idea | Addresses | Branch | Est. LOC |
|---|---|---|---|---|
| [`2026-05-22-bt-capture-watchdog.md`](2026-05-22-bt-capture-watchdog.md) | #12 Watchdog on `OnProcess` interval | FM-BT-3 (known long-uptime quiescence bug) | `feat/bt-capture-watchdog` | ~80 |
| [`2026-05-22-bt-autoswitch-gate.md`](2026-05-22-bt-autoswitch-gate.md) | #13 Gate `autoSwitchOnConnect` on PW node existence | FM-BT-1 (autoSwitch retries on missing PW node) | `feat/bt-autoswitch-gate` | ~120 |
| [`2026-05-22-bt-codec-observability.md`](2026-05-22-bt-codec-observability.md) | #15 Surface negotiated codec + bitpool as observable metric | FM-BT-6 (codec quality degradation, currently invisible) | `feat/bt-codec-observability` | ~80 |

**Phase 1 exit criteria**: all three PRs merged to `main`. Each PR's acceptance criteria (from the research's 5-block measurement scaffolding) have been verified per the plan's debug-agent verification steps.

---

## Phase 2 — Isolate the audio path (2 plans, ~165 LOC)

Sequence within Phase 2: **#9 before #14**. CPU affinity is purely systemd-level + thin P/Invoke; doesn't depend on any other work and provides immediate measurable benefit. The PW event subscription is more involved and benefits from Phase 1 being landed (so the watchdog + gate built in Phase 1 can be optionally simplified once the event API replaces `pw-cli` scraping).

| Order | Plan | Idea | Addresses | Branch | Est. LOC |
|---|---|---|---|---|---|
| 1 | [`2026-05-22-audio-thread-isolation.md`](2026-05-22-audio-thread-isolation.md) | #9 Pin `radio-api` to dedicated CPU cores + SCHED_FIFO | FM2 + FM8 + FM-BT-11 (host resource contention; documented "audio distortion correlates with SSH activity") | `feat/audio-thread-isolation` | ~55 |
| 2 | [`2026-05-22-pw-event-subscription.md`](2026-05-22-pw-event-subscription.md) | #14 Replace `pw-cli` scraping with PipeWire event subscription | FM-BT-1 + FM-BT-2 (eliminates pw-cli scrape race window) | `feat/pw-event-subscription` | ~150 |

**Phase 2 exit criteria**: both PRs merged. The shared `PROBE-SYS-LOAD` infrastructure (declared in both research docs' §3) is exercised by the audio-thread-isolation verification under the two-scenario protocol (light vs heavy load), confirming the load-correlation gap from MEMORY has measurably narrowed.

---

## Shared scaffolding — author this once before Phase 1 starts

The research docs declare several probe scaffolds as part of the research deliverable. They are *not* shipped to production — they live in `scripts/research/` and are used by the debug-agent verification steps in each plan.

| Script | Used by | Status |
|---|---|---|
| `scripts/research/sysload_capture.sh` | All Phase 1+2 plans (concurrent-load discipline) | Author before any plan begins |
| `scripts/research/sysload_correlate.py` | All plans (post-process audio events vs load) | Author before any plan begins |
| `scripts/research/bt_stall_detect.py` | Plan A — watchdog | Author as Task 0 of Plan A |
| `scripts/research/bt_autoswitch_audit.py` | Plan B — autoswitch gate | Author as Task 0 of Plan B |
| `scripts/research/bt_codec_observability_probe.sh` | Plan C — codec observability | Author as Task 0 of Plan C |
| `scripts/research/heavy_load_harness.sh` | Plan D — CPU affinity (two-scenario protocol) | Author as Task 0 of Plan D |
| `scripts/research/bt_pair_cycle_harness.sh` | Plan E — PW event subscription | Author as Task 0 of Plan E |

Each plan begins with a **Task 0: Author the probe scripts** step so the baseline run can be performed *before* the implementation lands.

---

## Workflow notes

- **One PR per plan**. Each plan ends with a `gh pr create` step. Plans do not merge each other.
- **Branches off `main`**, not stacked. The 5 plans are independent enough that stacking adds coordination overhead.
- **Measurement-driven acceptance**: every plan ends with a "Task N: Verify acceptance criteria" step that runs the baseline + post-change probes per the research's 5-block measurement structure and produces a single PASS/FAIL artifact. Do not merge a plan whose verification artifact is FAIL.
- **Phase 3 ideas (#1, #10, #11 + remaining cast-side) remain in research** until Phase 1+2 measurement data identifies which gaps remain. Do not pre-commit them.

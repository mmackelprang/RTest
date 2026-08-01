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

### GV Messages — Voicemail + SMS UI on `/phone` (selected 2026-06-20)

Consumes the Designer handoff (`docs/design-handoffs/HANDOFF-phone-messages-voicemail-sms.md`) and Architect ADR-022 (`design/decisions/2026-06-20-gvbridge-voicemail-sms-integration.md`). Restructures `/phone` into a unified Messages feed (voicemail + texts + recent calls, segmented filter, "More ▸" rail) consuming RotaryPhone's `gvbridge` API at `radio:5004`. **Status corrected 2026-07-31 — this section previously claimed "read experience ships fully." It does not.** The **client** work (GV-1→GV-4) is merged and correct, but **the server has never returned read data**: RotaryPhone's `PositionalGvThreadParser.ThreadsArray()` expects a JSON object root with a `"threads"` property while the `alt=protojson` request returns arrays, so every real response yields 0 items behind a clean HTTP 200. Voicemail and SMS read have **never** worked against live Google Voice. Defect is in the deployed tree (`D:\prj\rp-deploy`); a capture-then-fix cycle is underway there, and **no Radio Console change is required**. Honest status: **read UI complete, read data blocked upstream.** SMS send is built behind `RotaryPhone:Gv:SendEnabled=false`; **GV-5 📋** reconciles the client against the as-built contract ([ADR-028](../design/decisions/2026-07-30-gv-sms-send-contract.md)) and gates thread reply-ability. _A "wrong tree" concern briefly blocked it and was **disproven** — `D:\prj\rp-deploy` is a git **worktree** of `D:\prj\RotaryPhone`, sharing one object store, so the ADR was derived from the deployed objects all along._ **GV-7 📋** covers rendering non-dialable senders. **Update 2026-07-31 (later the same day) — the upstream parser fix (`627b928`) landed and read data IS now flowing**, superseding the "read data blocked upstream" status recorded above: a [live UAT](uat/2026-07-31-gv-live-data/REPORT.md) rendered 20 real SMS threads and 20 voicemails with real transcripts. That pass then found the *read experience* carries three defects on top of working data, and a [root-cause pass](uat/2026-07-31-gv-live-data/F-1-DIAGNOSIS.md) split them across two repos. **Ours is GV-8 📋** — the client collapses every non-2xx to `null` and renders it as an empty conversation, so a 502 is byte-identical to a genuinely empty thread; it ships **independently of the two RotaryPhone items and is deliberately not blocked on them** (ours makes the failure honest, theirs makes it rare). **Theirs** are a `%2F` thread-id decode — every group/MMS conversation is currently unreadable — and a ~9-minute GV auth blackout every 20 minutes; both are routed via `D:/prj/RotaryPhone/docs/prompts/`. **GV-9 📋** and **GV-10 📋** carry the LOW findings. **GV-7's "do not design blind" warning is retired** — the observations exist. _Standing note for anyone testing this surface: until the blackout is fixed, **record wall-clock time**. The 2026-07-31 pass hypothesised throttling precisely because it did not, and the logs falsified that hypothesis — 401 never 429, wall-clock not request volume._ Queued for Builder in [`docs/BUILDER_QUEUE.md`](BUILDER_QUEUE.md).

| # | Plan | Scope | Depends on | Branch |
|---|---|---|---|---|
| GV-1 | [`pr1-foundation-ia-shell.md`](superpowers/plans/2026-06-20-gv-messages-pr1-foundation-ia-shell.md) | DTOs, `GvBridgeApiService` reads + absolute audio URL, `PhoneHubService` GV events, `GvBridgeStatusService`, auth-handler seam (OFF), config/DI, Messages-feed IA shell (calls folded in, missed-call badge). | — | `feat/gv-messages-pr1-foundation` |
| GV-2 | [`pr2-voicemail-surface.md`](superpowers/plans/2026-06-20-gv-messages-pr2-voicemail-surface.md) | Voicemail rows + inline seekable player + transcript states + new-arrival + UI-local/flagged mark-read seam. | GV-1 | `feat/gv-messages-pr2-voicemail` |
| GV-3 | [`pr3-texts-surface.md`](superpowers/plans/2026-06-20-gv-messages-pr3-texts-surface.md) | Thread list + conversation bubbles + `GvSmsReceived` + compose/keyboard (all flag-gated send) + open-thread-to-RotaryPhone deliverable. | GV-1 | `feat/gv-messages-pr3-texts` |
| GV-4 | [`pr4-mark-read.md`](superpowers/plans/2026-06-20-gv-messages-pr4-mark-read.md) | Durable GV read-state (GV write-through, [ADR-024](../design/decisions/2026-06-20-gv-mark-read-durable-readstate.md)): `MarkVoicemailReadAsync` + SMS-thread mark-read wired to RotaryPhone's `read` routes, `ReadStateChanged` SignalR subscription, idempotent reconcile keyed by `(id-or-threadId + isRead)`. **✅ SHIPPED — PR #441**, behind `RotaryPhone:Gv:MarkReadEnabled=false`. No longer on hold: RotaryPhone's routes are shipped and dark by config only, so lighting up is a two-side config flip. | GV-2, GV-3 | `feat/gv-messages-pr4-mark-read` |
| GV-5 | [`pr5-send-contract.md`](superpowers/plans/2026-07-30-gv-messages-pr5-send-contract.md) | Reconcile the SMS **send** contract against RotaryPhone's as-built endpoint ([ADR-028](../design/decisions/2026-07-30-gv-sms-send-contract.md)): fix the request shape (our missing `ToNumber` makes **every** send fail `400 invalid_number` today), adopt the nine-code `Code` taxonomy with per-code UI treatment, subscribe to the `SmsSent` outbound echo, and add the idempotent `OutboundSmsReconciler` (exact `Id`, then `(Outbound, counterparty, text, ≤120s)`). Also gates **thread reply-ability** (ADR-028 §8) — ~a third of inbound SMS is from non-dialable senders (short codes, opaque IDs), so compose is gated client-side before the POST rather than surfacing their `400 invalid_number` as a failed send. Ships with `SendEnabled` still `false`. **📋 Queued** — a "wrong tree" concern was raised and **disproven** (`rp-deploy` is a git *worktree* of `D:\prj\RotaryPhone`, same object store). | GV-3 | `feat/gv-messages-pr5-send-contract` |
| GV-7 | _plan TBD (design-led)_ | Render non-dialable SMS senders: thread-list row, conversation header, and contact resolution for **numeric short codes** and **opaque 36-char sender IDs**. There is **no `fromName` in the GV payload**, so names can only come from local contact resolution — which cannot resolve these senders. Display counterpart to GV-5's send-side gate. **📋 Queued — live observations now EXIST** ([UAT § Findings for GV-7](uat/2026-07-31-gv-live-data/REPORT.md), G-1…G-9); the "do not design blind" warning is retired. Design against **G-1** (the `Texts 2` badge is an *unread count* — there are **20** threads) and **G-3** (no opaque 36-char ID is reachable from this surface, so that case is *untested against real data*, not proven absent). **G-4 de-risked the layout concern:** a 36-char ID measured **safe at 1920×720** — no truncation strategy needed. Also folds in **G-6** (header duplicates the identifier when no name resolves), **G-8** (MMS preview sender prefix) and **F-3** (composer rendered-but-disabled with no stated reason — a design decision for this row, not a prescription). | GV-3 | `feat/gv-messages-pr7-nondialable-senders` |

| GV-8 | [`plan`](superpowers/plans/2026-07-31-gv-texts-load-error-state.md) | **Distinguish a failed conversation load from an empty one.** UAT F-1 (HIGH): `GvBridgeApiService.GetSmsThreadMessagesAsync` collapses every non-2xx/timeout/deserialization error to `null`, then `PhonePage.razor:632` does `?? new()` — so a 502 renders as **"Start the conversation below."** with no error, no spinner, no retry. Adds outcome-aware client results, `_openThreadError`/`_openThreadLoading` page state threaded through `PhoneMessagesPanel` (whose `Loading`/`Error` parameters already exist but are never passed), and the missing error branch in `PhoneTextsPanel`, reusing the thread list's existing `cloud_off` + "Couldn't load messages." + `Retry` pattern one level down. Folds in F-2 (empty copy must mean *genuinely empty*). **Not blocked on RotaryPhone's two defects.** **✅ Shipped — PR #461.** [UAT 10/10, 0 HIGH/MEDIUM](uat/2026-07-31-gv8-error-state/REPORT.md), verified under a genuine upstream 502 during a natural auth blackout. C7 confirmed a genuinely empty group thread still reads as **empty**, so the opposite lie was not introduced. One user-visible delta by design: **a failed open no longer marks the thread read**, and a successful `Retry` performs the read-marking the failed open skipped. | GV-3 | `fix/gv-texts-load-error-state` |
| GV-9 | _plan TBD (small; CSS only)_ | **Texts-surface polish.** F-4 — `.texts-conv-number` lacks `text-overflow: ellipsis` and clips mid-character past ~60 chars (*hardening only; no live data triggers it, and a 36-char ID measured safe*). F-7 — unread rows sit 20px out of alignment because the unread dot displaces the text instead of occupying a reserved gutter, so every row jumps when marked read. **📋 Queued.** | GV-3 | `fix/gv-texts-polish-overflow-unread-align` |
| GV-10 | _plan TBD (investigate first)_ | **Confirm-or-close: bubbles may render the list snippet rather than the full body** (UAT F-5). **Unproven** — the plausible mechanism is upstream (per-thread messages are derived by filtering the whole SMS folder list, whose entries carry snippets), in which case this closes with no Radio Console change. First task is one `curl`, run inside a healthy auth window. **📋 Queued, lowest priority.** | GV-3 | `fix/gv-texts-bubble-full-body` |

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

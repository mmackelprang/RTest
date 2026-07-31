# Session state — 2026-07-31 · GV live-data UAT + F-1 triage

**Written for session restart.** `claude-mem` was failing throughout this session (300+ consecutive
"worker unreachable" hook errors, and it degraded to blocking the `Read` tool for one subagent), so
**none of this session's work was captured to memory.** This file plus the git history is the whole
record. Read this first on resume.

Next session runs in **WSL**, so Windows paths below become `/mnt/d/...`
(`D:\prj\RTest\RTest` → `/mnt/d/prj/RTest/RTest`).

---

## Where things stand

All work is **committed and pushed** to `origin/main` (`441ce02..7b0e351`). Working tree is clean
except for pre-existing untracked debris unrelated to this session.

| Commit | What |
|---|---|
| `a2e14c5` | GV-5 unblocked (worktree disproof), ADR-028 §8 reply-ability, GV-7 queued |
| `12fdfb3` | gitignore Playwright scratch + root-level UAT screenshots |
| `44e9936` | UAT report + 8 screenshots (`docs/uat/2026-07-31-gv-live-data/`) |
| `7b0e351` | F-1 three-defect split, GV-8/9/10 queued, GV-7 discharged |

**Key artifacts to read on resume:**
- `docs/uat/2026-07-31-gv-live-data/REPORT.md` — the UAT pass (F-1…F-8, G-1…G-9)
- `docs/uat/2026-07-31-gv-live-data/F-1-DIAGNOSIS.md` — confirmed root cause, log evidence, ownership split
- `docs/BUILDER_QUEUE.md` — GV-5, GV-6, GV-7, GV-8, GV-9, GV-10, OPS-1 all 📋

---

## What this session established

### 1. The parser fix works — live GV data is flowing

RotaryPhone's `PositionalGvThreadParser` fix (`627b928`) is deployed. Voicemail and SMS return real
data for the first time. Before this, every response yielded 0 items behind a clean HTTP 200.

### 2. GV-5 is unblocked — the "wrong tree" concern was disproven

`D:\prj\rp-deploy\.git` is a **file** reading `gitdir: D:/prj/RotaryPhone/.git/worktrees/rp-deploy`.
It is a detached-HEAD **git worktree** — same object store, same remote. **One repository.** ADR-028
was derived from the deployed objects all along, which also explains why all four source files were
byte-identical across both trees. Depends-on restored to GV-3 alone.

### 3. GV-7 is unblocked, and two of our assumptions were wrong

- **`Texts 2` is an unread count, not a thread count.** There are **20 threads**. An earlier session
  recorded "the filter showed Texts 2" and this session's Coordinator passed that to the Tester as
  "2 threads" — the Tester caught it. GV-7 must design against a 20-row list.
- **The layout risk did not materialise.** A 36-char opaque identifier measures **safe at 1920×720**
  (no overflow at 12/36/60/120 chars). No truncation strategy needed for layout-safety reasons.
- **Zero opaque 36-char sender IDs are reachable** from this surface — it caps at 20 threads with no
  pagination. Recorded as **"could not observe," not "does not occur."** Do not treat as absent.
- **70% (14/20) of threads render an identifier rather than a name** — well above the ~⅓ predicted
  from wire-data analysis. The fallback is the *common* case here, not an edge case.

### 4. F-1 is three defects across two repos, and the throttling theory was falsified

The UAT guessed Google Voice throttling. **Wrong**, falsified three ways: upstream status is 401
`Unauthorized` and never 429; the constant-rate 60s poller shows the identical on/off pattern
(failure tracks wall-clock, not request volume); recovery lands on fixed 20-minute boundaries.

| Defect | Owner | Confidence |
|---|---|---|
| **A** — PSIDTS stale ~11 min, CDP refresh every ~20 min, no reactive refresh on 401 → deterministic ~9-min auth blackout every 20 min → HTTP 502 | RotaryPhone | Confirmed |
| **B** — thread ids containing `/` arrive as literal `%2F`, fail exact string compare → HTTP 200 with `messages: []` | RotaryPhone | Confirmed |
| **C** — our client collapses every failure to `null` → `?? new()` → indistinguishable from empty; pane has no error branch | Radio Console | Confirmed |

11 of 11 observed 502s fall inside a predicted dead window.

**The "MMS defect" framing was wrong.** The predicate is **"thread id contains `/`"** — GV group
thread ids are `g.Group Message.<base64url>` and base64url includes `/`. Group threads happen to be
the MMS threads. They are **permanently unreadable**, which is why exactly those two never rendered:
they were hit by A *and* B simultaneously, while every other thread only had to dodge a 9-minute
window.

**Not fixable client-side** — double-escaping (`%252F`) and raw `/` were both tested and both fail.

---

## Open items, in priority order

### Needs your decision first

**The RotaryPhone cross-repo handoff is written but UNCOMMITTED.**
`D:\prj\RotaryPhone\docs\prompts\radioconsole-gv-threadid-decode-and-auth-blackout-request.md` (14KB,
carries the full log evidence so they need not re-derive it).

It was **deliberately not committed**: that repo is on branch `diag/gv-srtp-receive` — an in-flight
diagnostic session — with an uncommitted edit to `docs/prompts/RADIO-CONSOLE-BT-AUDIO-BOUNDARY.md`
(the sensitive cross-service boundary doc). Committing onto an active diagnostic branch, or switching
branches under someone else's dirty tree, is not a call to make unilaterally.

Note a pattern: `docs/prompts/radioconsole-cdp-spam-and-build-stamp-request.md` from an **earlier**
session is *also* still untracked there. Cross-repo prompt files are being written and left
uncommitted. Worth a deliberate fix rather than repeated one-offs.

### Ready to build (all 📋, no blockers)

- **GV-8** — Defect C, the error-state gap. HIGH, on a **shipped** surface. Fully specified with line
  citations (`GvBridgeApiService.cs:204-218` → `PhonePage.razor:632` →
  `PhoneMessagesPanel.razor:184-191` → `PhoneTextsPanel.razor:36-68`). The thread list already does
  this correctly (`PhonePage.razor:595-616` + `PhoneMessagesPanel.razor:110-117`) — the fix is that
  same pattern one level down. **Ships independently of RotaryPhone's fixes; do not block on them.**
  Recommended next.
- **GV-5** — SMS send contract + reply-ability gate. Ready longest; ADR-028 + Chunk 6b + §D2 tests.
  Merges as a user-visible no-op (`SendEnabled=false`).
- **GV-7** — non-dialable sender display. Design-led; now has its live observations. Consumes
  `GvCounterparty` from GV-5, so coordinate if both are in flight.
- **GV-6, GV-9, GV-10, OPS-1** — smaller; see queue.

### Judgement call recorded for review

Planner kept **GV-8 and GV-6 separate** rather than merging, despite a shared root shape (both map
non-2xx to `null`). Reason: merging couples a HIGH user-facing correctness bug to a low-priority
operator nicety and gives GV-8 a needless GV-4 dependency. They should share **the idiom, not the
PR** — whoever ships first introduces outcome discrimination in `GvBridgeApiService` in reusable
shape; GV-8 first. Door left open: if a Builder finds the shared shape does all the work, merging is
defensible — but say so rather than doing it quietly.

---

## Disciplines to carry forward

- **UAT of the texts surface must record wall-clock time against the 20-minute blackout cycle**, or
  results look random. Until Defect A is fixed, test within ~10 minutes of a `CDP cookie refresh`
  log line. This bit the original UAT — its "75s cooldown that worked" had simply crossed a
  20-minute boundary.
- **Monitoring for F-1 is server-side only.** Blazor Server fetches over SignalR, so failures never
  reach the browser — the UAT saw 0 console errors and 0 failed requests throughout. Probes:
  `journalctl -u radio-web | grep 'Failed to get GV SMS thread'` and
  `journalctl -u rotary-phone | grep 'api2thread/list returned'`. Bound them with `--since`; the box
  is an Intel N100 and heavy log tailing competes with the audio pipeline.
- **`/api/gvbridge/status` lies during a blackout** (`available:true, degraded:false` while endpoints
  return 502), so our "Google Voice is reconnecting" banner never fires in the window it exists for.
  Don't trust that endpoint as a health signal until Defect A's B2 lands.
- **Each thread open costs 2–3 upstream Google calls**, not 1 — `MarkThreadRead` re-lists twice on
  top of `GetThreadMessages`. Relevant if rate pressure is ever suspected.

---

## Environment notes for the WSL restart

- **`claude-mem` must be fixed or disabled before resuming.** It blocked `Read` for a subagent and
  captured nothing all session. If it is still failing, prefer committing work incrementally (as this
  session did) over relying on memory.
- Repo paths shift to `/mnt/d/prj/RTest/RTest`, `/mnt/d/prj/RotaryPhone`, `/mnt/d/prj/rp-deploy`.
- Project memory says **use the SSH MCP tools** for `radio` / `piradio`, not bash `ssh`. The debugger
  subagent this session used bash `ssh` because no SSH MCP tool was exposed in its toolset — worth
  checking that subagents inherit it after the restart.
- Deploy target for all hardware is **Ubuntu x64 `radio`** (`linux-x64`), screen **1920×720**.

# HANDOFF — the 2026-09-27 Bluetooth / Google Cast / FM-SDR code review

**Status:** `[FILED 2026-09-27 — NOTHING BUILT, NOTHING QUEUED]`. Thirty-six punch-list rows,
`AUD-37` … `AUD-72`, filed in [`HANDOFF-GA-PUNCH-LIST.md`](HANDOFF-GA-PUNCH-LIST.md) §4.2 (P1) and §5
(P2) from a read of the code, not from the box. This file is the pick-up point for whoever works them.

---

## What happened, in one paragraph

The owner asked for a thorough review of the BT, Cast and FM-SDR code for performance and bugs, with
findings added to the GA punch list. The review read `LinuxBluetoothService`, `BluetoothAudioSource`,
the PipeWire native stream, `GoogleCastOutput`, `DirectCastStreamingService`, `HttpStreamOutput`,
`TappedOutputStream`, all of `RTLSDRCore` and `SDRRadioAudioSource`, plus the DI wiring and the
controllers that reach them. Three parallel reviewers covered the same files; **every citation they
produced was re-read against the tree at `e46fa68` before it was filed, and several were dropped.**
The rows say what was read. **None claims a symptom was observed on `radio`.**

---

## Read this before touching anything

1. **Nothing here was measured.** Each row is a code read with `file:line` anchors. Where a row's
   conclusion needs the box (`AUD-37`, `AUD-38`, `AUD-45`, `AUD-47`), it names the check. Run the
   check before building — a plausible mechanism is not an observed defect, and this repo has been
   burned by that (see `AUD-9`'s memory note in the punch list).
2. **The box runs Cast in `DirectChannel` mode** (`deploy/debian-x64/appsettings.Production.json:20`,
   custom receiver `567E3DBA`). `AUD-56` and `AUD-57` only reach the shipped `HttpMp3` default and say
   so. `AUD-37`, `AUD-38`, `AUD-54` and `AUD-58` are the production-mode rows.
3. **`design/AUDIO-PIPELINE-REVIEW.md` (2026-07-26) had already recorded seven of these mechanisms**
   — A1, A3, B5, B9, B10, B11, C4 — and had never been carried onto the punch list. The rows cite it.
   Do not refile them a third time; do correct its stale numbers where a row says they are stale
   (B5's LOH rate is 10× high: the receiver runs 240 kS/s, not 2.4 MS/s).
4. **Three SharpCaster claims are unverified locally.** The package is not restored in a fresh
   container, so the heartbeat-disconnect event (`AUD-37`), the discovery-timeout semantics
   (`AUD-53`) and the inbound channel routing (`AUD-54` item 2) come from a reviewer's reading of the
   3.0.0 source. Each row names the box check. Verify before building on them.
5. **The ID lines were stale before this pass.** The punch list said the next free `AUD` number was
   `AUD-32` while the queue had taken `AUD-32` … `AUD-36`. Both documents now say `AUD-63`. Re-derive
   from both files before minting, exactly as the header tells you to.
6. **The usual gates apply and were not exercised here** — this pass changed only documents. A Builder
   picking up any row runs `dotnet build -c Release --no-incremental` (47 warnings, 0 errors baseline)
   and the test suite without piping into `tail`, per `CLAUDE.md`.

---

## The rows, and a suggested order

The punch list's §2 ordering constraints still govern. Within that, this is the order the review
would take, cheapest evidence first:

### Confirm on the box (an afternoon, no code)

| Row | The check | What it settles |
|---|---|---|
| `AUD-40` | BT playing; press Pause on the panel; does the audio stop? | P1 if it keeps playing. One-line fix. |
| `AUD-41` | Press Stop Scan; disconnect and reconnect the phone; does AVRCP volume/codec still attach? Try pairing a new phone. | P1 if either fails; a restart is the only recovery. |
| `AUD-38` | Set the Cast speaker to 20 %; restart `radio-api` with Cast as the persisted output; read the speaker level and `/api/audio` master volume. Then turn the knob. | Whether the 70 % push and the knob dead-end are real. |
| `AUD-37` | While casting, power-cycle the Chromecast; wait 60 s; is the cabinet silent with local still muted? Check the journal for the DirectChannel send-error Warning every ~5 s. | P1 if silent; also confirms the SharpCaster `Disconnected` claim. |
| `AUD-47` | `wpctl get-volume <bluez_input id>` after a phone-side resume, then after a phone volume press. | Which of the two contradictory policies the box actually has. |
| `AUD-39` | Correlate `bluetooth.metadata_updates` (or `AVRCP metadata:` debug lines) with `⚠️ Buffer underrun` lines on a BT session. | Whether the flush-per-Track-event is a visible part of `AUD-15`. |
| `AUD-42` | `dotnet-counters` Gen2 count and LOH size for 10 min of SDR play; log `GCSettings.LatencyMode` after the set. | The baseline miss mechanism `AUD-20` lacks. |

### Build, in this order

1. **`AUD-40`, `AUD-41`, `AUD-50`, `AUD-51`, `AUD-53`** — each under an hour, each with a static
   test seam, each independently mergeable. Good first PRs for a session that wants to get the
   feel of the BT service before `AUD-48`.
2. **`AUD-39`** — two lines in `BluetoothAudioSource.OnMetadataChanged`, one test. Do it before or
   with `AUD-15` so the underrun counts it changes are attributed correctly.
3. **`AUD-42`** — pool the `IqSample[]`; measure Gen2 before and after. Then decide `AUD-45` from
   the `top -H` number, not from the estimate.
4. **`AUD-37` + `AUD-54`** together — same client-lifetime code in `GoogleCastOutput`, and
   `AUD-5` is queued against the same lock discipline; plan the three as one arc.
5. **`AUD-38`** — after `AUD-37`, because the reconnect path decides where the volume push lives.
6. **`AUD-43`, `AUD-44`, `AUD-59`, `AUD-60`** — the SDR arc. `AUD-43` first: it removes the restart
   that makes the others harder to observe.
7. **`AUD-47`, `AUD-48`, `AUD-49`** — the BT service arc, after the box check for `AUD-47`.
8. **`AUD-58`** — the pooling half is a half-day and safe now; the transport half is a receiver
   change and belongs with `AUD-37`'s reconnect work.
9. The hygiene rows (`AUD-46`, `AUD-52`, `AUD-55`, `AUD-61`, `AUD-62`) ride along with whichever
   PR next touches their files. `AUD-46` is a plain delete and can go any time.

### Not filed, deliberately

- `PipeWireNativeStream.OnProcess` allocations and marshalling — `design/plans/AUD-15-…` §6.1
  already holds them as a follow-up gated on `AUD-15`'s Task 1 numbers.
- `SrcVariableResampler` ignoring `InputFramesUsed` — `AUD-15` Task 2.
- The `BluetoothMgmtMonitor` poll loop pinning a thread-pool thread and the 300 ms `Thread.Sleep`
  in the disconnect handler — real but P3, and `AUD-21`/`AUD-23` are the rows that reshape that code.
- `AudioSourceBase`/`USBAudioSourceBase` volume plumbing beyond `AUD-62` item 4 — out of scope.

---

## The FM RDS follow-up (later the same day)

The owner reported that *"the display often has jerky visual artifacts when showing RDS data"* and asked
for the same kind of review of the RDS code. Ten more rows: `AUD-63` … `AUD-72`. **The symptom has one
display-side root cause and two decoder-side fragment sources, and they are separable.**

### The answer, in one paragraph

The RDS ticker's JavaScript engine keeps its scroll position across renders only when
`MarqueeTextDiff.Compute` can align the new track text as *front-trim + tail-append* of the old. That diff
was designed for a RadioText-only track. The inline-scroll revision (option (c)) then made the track
`"{PS} • {RT}"`, and a fixed head defeats the alignment: once the 256-char buffer is full, **every**
RadioText update classifies as an in-place swap (the whole text shifts a chunk under a fixed offset) or a
reset (snap to home); and because the decoder trims the station name, consecutive rolling-PS pages differ
in length and three of four page changes also reset. A line-for-line Python port of the diff reproduces
both (`AUD-63`). Two tests pin the wrong assumption that PS is always eight characters. Separately, the
decoder's PS confirmation confirms old/new hybrids on a lost segment (`AUD-69`) and the RadioText
assembler confirms complete hybrids on an in-place change with one lost group (`AUD-70`) — the
"fragments that change back and forth".

### Rows

| Row | Tier | One line |
|---|---|---|
| ~~`AUD-63`~~ | P1 | Marquee diff defeated by the PS head; rolling PS resets; Blazor paints the text a round-trip before the engine compensates. ✅ **Shipped 2026-09-27 — [#674](https://github.com/mmackelprang/RTest/pull/674).** |
| ~~`AUD-69`~~ | P1 | PS confirmation confirms hybrids on a lost segment; accented chars freeze a slot. ✅ **Shipped with `AUD-60` — [#674](https://github.com/mmackelprang/RTest/pull/674).** |
| ~~`AUD-70`~~ | P1 | In-place RT change + one lost group confirms a complete hybrid; ticker keeps both. ✅ **Shipped — [#674](https://github.com/mmackelprang/RTest/pull/674).** |
| `AUD-64` | P2 | Speed policy steps 40 ↔ 60 px/s on alternate updates; no hysteresis. |
| `AUD-65` | P2 | Decoder/ticker comments, tests and logs that describe a pipeline that does not exist; dead PS event. |
| `AUD-66` | P2 | The card vanishes on every tune and grows 8 px when RT arrives — layout shift under the frequency well. |
| `AUD-67` | P2 | A tap on the touchscreen freezes the ticker (sticky `:hover` + focus); static-fit → scroll snaps from centred to left. |
| `AUD-68` | P2 | Engine cancels/recreates the animation on every update; `OnAfterRenderAsync` re-entrancy; server-global `RdsRelevantChanged`. |
| `AUD-71` | P2 | PI and PTY have no confirmation; one aliased block flips the call sign and empties the ticker. |
| `AUD-72` | P2 | Decoder reset races the DSP thread; Costas/pilot loops are first-order by a units error. |

### Box checks before building (CDP is on `:9223`)

1. PS flip cadence on the station the owner listens to: count `RDS: Station name` lines in the file
   sink over a bounded window. Every one is a candidate reset. (⚠ Since `LOG-12`, raise `RTLSDRCore`
   to Debug for the window first — otherwise the count is 1 per tune.)
2. Snap detection: via CDP, `const m = await import('/js/rds-marquee.js')` and poll `m._debugState(id)`
   at 200 ms; `offset` dropping to 0 on an RT update is `AUD-63`(1), on a PS change `AUD-63`(2).
3. `matchMedia('(prefers-reduced-motion: reduce)').matches` once — GNOME's `enable-animations` can
   set it, which would give a static strip rather than jerk.
4. Look for a mangled station name in the same log window (`AUD-69`) and an RT line that is a mix of
   two titles (`AUD-70`).

### What shipped, and what the next session does with it

> ⏸ **Paused 2026-09-27: the owner is doing the deploy, acceptance and follow-up on the dev box.** The
> step-by-step for that is [`HANDOFF-RDS-DEV-BOX.md`](HANDOFF-RDS-DEV-BOX.md); this section stays as the record.

✅ **`AUD-63`, `AUD-69`, `AUD-70` and `AUD-60` shipped 2026-09-27 in [#674](https://github.com/mmackelprang/RTest/pull/674)** (three commits, one per
row set). What landed, in one line each:

- `AUD-63` — the track is two spans; `RdsCard` passes `Head` (`"{PS}{Separator}"`) and `Text` (RT)
  separately, only the RT is diffed, and `rds-marquee.js` `update()` now takes the head and body strings,
  writes them, re-measures and restarts the leg in one JS task, absorbing the head's width change into the
  offset. Once the engine is attached Blazor never rewrites the span text. Driven in a headless Chromium
  against the real stylesheet: a body glyph stays within 0.01 px across every kind of update.
- `AUD-69` + `AUD-60` — a PS candidate comes only from one complete in-order cycle (segment 0 → 3, nothing
  missing), a group with a bad block is dropped whole, and 0x80–0xFF map through the new `RdsCharset`.
- `AUD-70` — `RadioTextAssembler` tracks a change wave per slot; only slots re-sighted since the change
  began count towards a complete assembly. 2B is complete at 32; 0x0A/0x0B are spaces.

⚠ **Verified in unit tests and a browser harness only — not deployed, not watched on the panel.** The box
checks above are now the acceptance step: deploy, read the deployed SHA (`/api/health/version` on both
services) *before* looking, then poll `_debugState(id)` over CDP — `offset` must no longer drop to 0 on
an RT update or a PS page, and the file sink must show no mangled `RDS: Station name` lines (with
`RTLSDRCore` raised to Debug — `LOG-12`). Two things
to know when reading the result: the head is still the live rolling PS (it now flips in place without
moving the body; anchoring it on the PI call sign instead is an open product choice), and a rolling-PS
page now needs two clean consecutive cycles to show, so under heavy block loss the head goes *stale*
rather than wrong. `_debugState` reports `headWidth`, `bodyCharWidth`, `headText` and `bodyText` in place
of the old `charWidth`.

One thing seen while gating, not filed: `BluetoothAutoSwitchServiceTests.NodeArrivesAfterProbe_SwitchesViaEvent`
sleeps 500 ms on a wall clock (`:261`) and failed once under load, then passed 3/3 alone — the shape
`TEST-4` was written about. Worth a `TEST` row if it shows up in CI.

### Build order for what is left

`AUD-64`, `AUD-66`, `AUD-67` are an afternoon each and independent; `AUD-64` is the one still visible
now that the jump is gone (the 40 ↔ 60 px/s speed step). `AUD-71`, `AUD-72` and `AUD-68` ride with
whichever PR next touches their files — `AUD-68`(1) (cancel/recreate on every update) and (2)
(`OnAfterRenderAsync` re-entrancy) are partly closed by `AUD-63`'s serialised interop; (1) still applies
to `'swap'` and `'speed'`.

---

## State of the tree

| | |
|---|---|
| **Branch** | `claude/bt-cast-fm-sdr-review-flm1x1`, squash-merged to `main` (see the PR this file landed in) |
| **Files changed** | `docs/HANDOFF-GA-PUNCH-LIST.md` (+26 rows, then +10 RDS rows, two §9 cells, the header ID line), `docs/BUILDER_QUEUE.md` (the two "next free" lines only), `docs/HANDOFF-NEXT-SESSION.md` (pointer), this file |
| **Code** | untouched |
| **Box** | untouched; nothing to deploy |
| **Queue** | no row claimed, no row added — the punch list is the review artifact, the queue is the dispatch artifact |

The commit that filed the first 26 rows is `2fc13de` (merged in #672); the RDS follow-up is the PR this revision landed in.

# HANDOFF — the 2026-09-27 Bluetooth / Google Cast / FM-SDR code review

**Status:** `[FILED 2026-09-27 — NOTHING BUILT, NOTHING QUEUED]`. Twenty-six punch-list rows,
`AUD-37` … `AUD-62`, filed in [`HANDOFF-GA-PUNCH-LIST.md`](HANDOFF-GA-PUNCH-LIST.md) §4.2 (P1) and §5
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

## State of the tree

| | |
|---|---|
| **Branch** | `claude/bt-cast-fm-sdr-review-flm1x1`, squash-merged to `main` (see the PR this file landed in) |
| **Files changed** | `docs/HANDOFF-GA-PUNCH-LIST.md` (+26 rows, two §9 cells, the header ID line), `docs/BUILDER_QUEUE.md` (the two "next free" lines only), `docs/HANDOFF-NEXT-SESSION.md` (pointer), this file |
| **Code** | untouched |
| **Box** | untouched; nothing to deploy |
| **Queue** | no row claimed, no row added — the punch list is the review artifact, the queue is the dispatch artifact |

The commit that filed the rows is `2fc13de`; its message lists the rows by tier.

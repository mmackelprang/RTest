# `AUD-20` — the SDR source misses its callback deadline 2–4× more often since 2026-09-08, and five mechanisms have been ruled out

[← Builder Queue index](../BUILDER_QUEUE.md)

🔵 **P3.** Filed 2026-09-09 by the coordinator from a live-box observation. ⛔ **No cause established.**
This row exists to record what is measured and, more importantly, **the five hypotheses already
falsified**, so the next investigator does not re-run them.

## What is measured

`SDRRadioAudioSource` logs `🔬 Missed callback deadline (NNms) with GC activity: …` at `WRN`. Counts
from the file sink, `/opt/radio-console/logs/radio-YYYYMMDD.txt`:

| Day | misses | note |
|---|---|---|
| 09-03 | 264 | |
| 09-04 | 196 | |
| 09-05 | 264 | |
| 09-06 | 266 | |
| 09-07 | 192 | |
| **09-08** | **519** | ⭐ regime change begins **~13:00** |
| **09-09** | **531** | measured at ~11:47 EDT, i.e. **half a day** |

**Hourly is where it is stark.** Baseline was **1–11/hour**. Since 09-08 13:00 it has run **20–70/hour
continuously**, ⭐ **including 42–65/hour overnight with nobody touching the console** — so it is not
user-driven load. Today projects to roughly **4× baseline** against *lower* total log volume (33k lines
vs 55k on 09-05), so it is not an artefact of more logging either.

**Two further facts that constrain any explanation:**

- ⭐ **504 of 504 sampled lines are `SDRRadioAudioSource`.** Not a general audio-engine condition.
- ⭐ **It survived a `radio-api` restart** (today 11:14, a fresh process on new code) **and it predates
  the 09-08 deploy**, so it is neither accumulated process state nor introduced code.

## ⛔ Five hypotheses, all falsified by measurement. Do not re-run these.

1. **SongRec process churn.** No correlation. 09-05 had **18** SongRec timeouts and 264 misses;
   09-09 had **2** and 531. Overnight ran 42–65 misses/hour with **zero** SongRec activity.
2. **The 09-08 47-PR deploy.** `radio-api` started **16:07:28**; the sustained run began **13:00:51**,
   three hours earlier. Also still elevated on two later builds.
3. **RF / signal quality.** Anticorrelated. RDS `Block sync lost` per day: 09-05 **13,680** with
   baseline misses; 09-07 **845**; 09-09 **10,060**. ⚠ **And the RDS "station-name flapping" is a red
   herring** — `Limeligh`/`imelight`/`Rock 92`/`Rush` is a station scrolling its name and current
   track through the 8-character PS field. That is normal, not instability.
4. **A changed threshold or log format** (i.e. more *reporting* rather than more *misses*). The
   reported minimum is exactly **`40.0ms` on every day**, and the line format is byte-identical between
   09-07 and 09-09. **The increase is real.**
5. **A GC regime change.** ⭐ **This one inverted on measurement.** `Gen2/Gen0` ratio per day: 09-05
   **0.970**, 09-06 **0.875**, 09-07 **0.008**, 09-08 **0.618**, 09-09 **0.922**. **09-07 is the
   outlier, not the elevated days** — the near-1.0 ratio is the normal state here and does not track
   the miss count at all.

## ⚠ The station changed, and it does NOT explain it either

Tuned frequency by day: 09-07 **97.7 MHz**; 09-08 **92.3 MHz**; 09-09 back to **97.7/97.8 MHz**. The
owner confirms *"the radio may have changed stations, but no physical changes for weeks."*

**The elevation began on the day of the change and persisted through the change back.** So a station
swap is temporally adjacent and causally insufficient on its own. ⚠ It has not been ruled out as a
*contributor* — only as a complete explanation.

## What has NOT been examined

Stated so the next investigator knows where the unexplored ground is, rather than inferring it from
what is above:

- Whether the miss rate correlates with **SDR stream uptime** rather than wall-clock date. `MEMORY.md`
  carries a long-standing *"after days of uptime + source switches, SoundFlow capture stops delivering
  audio"* item, and `AUD-18` recovered on an **RTL-SDR stream restart**. **Nobody has checked whether
  the SDR stream has run continuously since 09-08.** ⭐ This is the most promising untested lead.
- Whether anything else on the box changed load around 09-08 13:00 — `rotary-phone`, the GV bridge's
  20-minute cookie cron, PipeWire, or thermal/CPU state.
- Whether 40 ms is even the right threshold. **Nobody has established the audible consequence.** Audio
  was playing normally throughout, and a 43 ms miss on a buffered path may be inaudible.

## ⚠ Why this is P3 and not higher

**No user-visible symptom has been demonstrated.** Audio played correctly throughout the elevated
period, including all night. The row is filed because a 4× sustained change in a warning nobody
understands is worth a name — **not because anything is known to be broken.**

⛔ **Do not "fix" this by raising the threshold or suppressing the warning.** That converts an
unexplained signal into no signal, which is the failure this repo has spent the week removing.

## Verification

⚠ **There is no unit test for this and there should not be one yet** — the phenomenon is not
understood well enough to pin. The instrument is the file sink, and the measurements above are
reproducible with `grep -c` per daily log.

⭐ **The first honest deliverable is an ANSWER, not a fix**: establish what changed at 09-08 13:00, or
establish that the question is unanswerable from retained logs and say so. **Closing this row with
"cause not determined, here is what was excluded" is a legitimate and useful outcome.**

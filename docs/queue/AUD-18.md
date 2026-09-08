# `AUD-18` — the fingerprint tap returned zero bytes for 11½ hours while the radio played

[← Builder Queue index](../BUILDER_QUEUE.md)

🟠 **P1.** Filed 2026-09-08. Found incidentally while grepping the log for the `XR-4` CDP spam — the
single `9224` hit turned out to be `15000.9224ms`, a *duration* inside this warning. **Nobody was
looking for this.**

⭐ **This is the first repo row for the capture-lifecycle failure.** `MEMORY.md` has carried it as
*"long-running capture device lifecycle bug — HIGH PRIORITY for production stability"* for months and
**no queue row existed** — `SoundFlowAudioTap` appears in no row and no dossier. That is why it has
never been worked.

## The evidence

`SoundFlowAudioTap.cs:232-235` warns when a 15-second capture window yields `bytesRead == 0`.

| Date | Occurrences | Log lines |
|---|---|---|
| 2026-09-02 → 09-05 | **0** | 232,285 |
| 2026-09-06 | 48 | 29,149 |
| 2026-09-07 | 112 | 22,309 |
| 2026-09-08 (to 12:00) | **2,490** | 13,407 |

**Today it is 18.6% of every line in the log**, on a box where `CLAUDE.md` records that log volume
correlates with audible audio distortion. Each failure logs **twice** — `SoundFlowAudioTap` and its
twin in `BackgroundIdentificationService` — so ~5,200 lines came from this alone.

### It is one continuous outage, not scattered noise

Hourly on 2026-09-08: **240, 239, 240, 240, 240, 240, 239, 240, 240, 240, 92** for hours 00–10, then
nothing. 240/hour is **one every 15 seconds — the full capture window, failing back to back.** Each
carries ~1,420 read attempts, so the loop polls ~95×/s and gets nothing every time.

2026-09-07's 112 are **all in hour 23**. So:

- **Onset 2026-09-07 ~23:00 EDT**
- **Recovery 2026-09-08 10:22:57 EDT**
- **Duration 11 h 23 m**

### Fingerprinting was completely dead for the whole window — confirmed independently

`TrackMetadata` rows created (UTC, so 03Z = 23:00 EDT):

```
09-07T22Z 13   09-08T00Z 13   09-08T02Z 13   [04Z–13Z: NOTHING]   09-08T15Z 11
09-07T23Z 19   09-08T01Z 15   09-08T03Z  7                        09-08T16Z 13
```

**Zero rows for eleven hours, resuming the moment the warnings stop.** The DB gap and the log window
are the same window, derived from different systems.

### Audio was flowing, and fingerprinting worked, right up to the onset

`SDRRadioAudioSource` was active and identifying tracks normally until **22:57:12** — *'3Am'*,
*'The Seed (2.0)'*, *'White Rabbit'* — each setting album art. So this is not "nothing was playing".

⚠ **Ten minutes before the onset, 22:50:09:**

```
[WRN] SDRRadioAudioSource: 🔬 Missed callback deadline (41.6ms) with GC activity
```

**Suggestive, not causal.** It is the only anomaly in the window and it names the mechanism the
memory note describes, but one correlated warning is not a diagnosis. **Do not start the plan from it.**

### It recovered without a service restart

At **10:23:03** the RTL-SDR stream was restarted — `Switching to FM Broadcast (pausing stream)` →
`Stopping RTL-SDR sample streaming` → `Starting RTL-SDR sample streaming` — and capture resumed.

⚠ **`radio-api` uptime is unbroken since 2026-09-06 10:04:45.** So the recovery was a *stream*
restart, not a process restart. `MEMORY.md` says *"Restart fixes"*; this refines it — **restarting the
source is enough, and the service does not need to be bounced.** That is a materially cheaper
mitigation than the memory note implies, and it is a lead: whatever is stale lives at or below the
SDR stream, not in the whole engine.

**Uptime at onset was ~37 hours**, which matches the memory note's *"after days of uptime + source
switches."*

## Deliberately not assumed

- **Not proven that audio stopped being audible.** The tap is a separate path from playback; the
  memory note's shape is *"generator in mixer but output=0"*, i.e. the tap dies while sound continues.
  **Nobody was awake to hear it.** Ask the owner before claiming a user-visible audio outage — what is
  *proven* user-visible is that **album art and play history stopped for 11½ hours.**
- **Not proven the 2026-09-06 occurrences are the same phenomenon.** `radio-api` restarted at 10:04:45
  that day and the first warning was 09:53:20 — a *different process*. Treat 09-06 as a separate
  sample.
- **Not proven this is the memory note's bug.** Strongly suggestive (long uptime, capture returns
  zero, restart clears it) but that note predates this evidence and describes *"affects all capture
  sources"*, which this sample cannot show — only SDR was active.
- **Not `AUD-15`.** That is the BT ring buffer running *empty during playback* (`buffer: 0/384000`).
  This is the fingerprint tap reading *zero bytes* from the mixer. Different path, different symptom.
- **Not `AUD-12`.** That is a BT source stalling at `Ready`. Here the source was `Playing` throughout.

## Scope questions for the plan

1. **What does the tap read from, and what goes stale?** Start at `SoundFlowAudioTap.cs` and the
   generator's attachment to `MasterMixer`. The recovery evidence narrows it: a source-stream restart
   was sufficient, so the stale object is plausibly the generator or its buffer, not the engine.
2. **Why does nothing detect it?** Eleven hours of a 15-second loop returning zero is a condition the
   system could notice in under a minute. A watchdog that restarts the source after N consecutive
   empty captures may be a cheaper and more reliable fix than finding the root cause — **and can ship
   independently of it.** Consider proposing both, sequenced.
3. **Is the double-logging wanted?** Two WARN lines per failure, at 4/minute during an outage, on a
   box where log volume is itself a problem. Rate-limit or collapse to one.
4. **Does it affect other capture sources?** The memory note claims all; this sample only shows SDR.
   Say what the code implies and mark it unverified rather than repeating the claim.

## Verification

⚠ **The hard part is that the failure needs ~37 hours of uptime to appear**, so a test cannot
reproduce it directly and a fix cannot be confirmed quickly. Be honest about that in the plan rather
than asserting a green run proves anything.

What *is* checkable cheaply: the detection/recovery half. A watchdog that counts consecutive empty
captures is unit-testable with a fake tap, and **must fail first** — assert it restarts after N, then
confirm the test is red without the watchdog.

The durable field signal is the one that found this: **`grep -c 'No audio data captured'` per day
should be 0.** It was 0 for four consecutive days before 09-06, so zero is the established baseline,
not an aspiration.

⚠ Live audio path. **Not auto-mergeable.**

# `PHN-10` — two voicemails can play at once, and `PHN-1f` shipped the queue that was supposed to prevent it

[← Builder Queue index](../BUILDER_QUEUE.md)

🟠 **P1.** Filed 2026-09-09 from the owner's `PHN-2` UAT at the cabinet
([record](../uat/2026-09-09-phn2-sound-uat/RESULT.md), check #11 / §3 U8).

## What was observed

Owner, verbatim: *"Two voicemails **can** play simultaneously - when TTSing a text the second text
interrupts the first one - but ducking doesn't work for this either."*

Check #11 exists to verify *"the **wait** is not mistaken for a broken button, and the room never
carries **two voices**."* **The room carried two voices.**

## ⛔ Why this is worse than a new defect

**`PHN-1f` — "the wait-then-play queue" — has already SHIPPED** (archived). Serialising event
playback is what that row was for. So this is not "a queue we never built"; it is **a queue that
exists and did not cover this path**, which is a different and more serious thing to diagnose.

⚠ **Establish which before designing anything:** does the queue not cover the voicemail path, does it
cover it and fail, or does it serialise *requests* while two *sources* can still reach the mixer?
**Three different fixes.**

## ⚠ TWO BEHAVIOURS WERE REPORTED AND ONLY ONE IS CERTAINLY WRONG

⛔ **Do not treat these as one symptom.**

| Observation | Verdict |
|---|---|
| Two **voicemails** play **simultaneously** | ⛔ **Wrong.** Check #11 exists to forbid exactly this. |
| A second **TTS text interrupts** the first | ⚠ **Unknown.** Interruption may be *intended preemption* — `PHN-1d` §2.2 covers doorbell-preempts-voicemail as designed behaviour. |

**Find the intended contract for each path before calling either a defect.** Simultaneity and
preemption are opposite failures and a fix for one can create the other.

## ⚠ The ducking half is `AUD-2`, not this row

The owner adds *"ducking doesn't work for this either"* — that is
[`AUD-2`](AUD-2.md) (key mismatch at `SoundFlowPlaybackService.cs:686`), confirmed by ear in the same
sitting on two independent paths. ⛔ **Do not fix ducking here.** This row owns *how many things play
at once*; `AUD-2` owns *whether the radio ducks under them*.

⭐ But note the interaction, because it is why the owner heard it so clearly: **with ducking broken,
two simultaneous voices arrive at full level over an un-ducked radio.** Fixing `AUD-2` will make this
row's defect quieter without making it less real — ⚠ **so do not let a post-`AUD-2` re-listen be read
as evidence this is fixed.**

## Verification

⚠ **Not closable by a green suite.** Reproduce at the cabinet: start one voicemail, start a second
before the first ends, and listen. ⭐ **Assert the PRESENCE of serialisation** — the second waits and
then plays — not the absence of an error.

⚠ **Beware the `AUD-2` interaction above**, and beware testing only the TTS path: the owner saw
simultaneity on voicemails and interruption on texts, so a single-path test can return either answer.

## Depends on

None to build. ⚠ **Read `PHN-1f`'s archived row first** — it shipped the queue, and its scope is the
fastest route to which of the three diagnoses applies.

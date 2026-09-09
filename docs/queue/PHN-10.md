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

---

## ✅ OWNER RULING 2026-09-09 (post-`AUD-2` deploy) — **the TTS half is CORRECT BEHAVIOUR. This row HALVES.**

The owner re-ran the check at the cabinet after `AUD-2` was deployed (`f4d71b28`). Verbatim:

> *"Ducking now works, but I'm still able to play two voicemails simultaneously. TTS also ducks and
> the second TTS cancels the first one. **I think this behavior is the correct one.**"*

| Observation | Filed as | Now |
|---|---|---|
| Two **voicemails** play simultaneously | ⛔ Wrong (check #11) | ⛔ **STILL LIVE — reproduced after `AUD-2`** |
| A second **TTS** interrupts the first | ⚠ **Unknown** — possible intended preemption | ✅ **CORRECT BEHAVIOUR — owner ruled. NOT a defect.** |

### ⛔ What this means for the fix

**This row is now ONLY about voicemail serialisation.** ⛔ **The fix MUST PRESERVE TTS preemption.**
A change that serialises *all* event audio would suppress behaviour the owner has explicitly endorsed
— and before this ruling, a Builder had a live chance of "fixing" the interruption and breaking it.
**Do not re-open the interruption question.**

⭐ **The three-way diagnosis narrows with it.** The queue demonstrably *does* something on the TTS
path (it preempts, correctly). So *"the queue does not exist / does not cover event audio"* is much
less likely than *"it covers TTS and not voicemail"* or *"it serialises REQUESTS while two voicemail
SOURCES still reach the mixer."* **Start there.**

### ⭐ The row's own trap warning HELD — record this

This dossier warned: *"do not let a post-`AUD-2` re-listen be read as evidence this is fixed."* The
owner listened after `AUD-2` landed, with ducking audibly working on both paths, **and the
simultaneity was still there.** The row predicted its own most likely false-clear and the check
walked past it. ⚠ **Keep that warning in force** — it applies equally to any future partial fix.

### Status of the interacting row

✅ **`AUD-2` is CLOSED and CONFIRMED BY EAR** on both paths (voicemail and TTS ducking), deployed as
`f4d71b28`. **`PHN-2` check #6 flips FAIL → PASS.** The ducking half of the original report is done;
what remains here is *how many voicemails play at once*, and nothing else.

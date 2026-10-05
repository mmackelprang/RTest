# `PHN-10` — two voicemails can play at once, and `PHN-1f` shipped the queue that was supposed to prevent it

[← Builder Queue index](../BUILDER_QUEUE.md)

🔴 **P0 — RE-TIERED 2026-09-09 by the owner**, up from P1, once the plan established the scope. ⛔ **Every voicemail played leaks a `SoundPlayer` plus a mixer component PERMANENTLY**; the Stop button, doorbell preemption, ADR §7.1's `MaxPlaybackSeconds` "THE guarantee", the `/sleep` edges and the last-circuit backstop all funnel through one disarmed guard. **"Two voicemails at once" was only the cheapest way to hear it.** Originally filed P1 from the owner's `PHN-2` UAT at the cabinet
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

---

## ✅ DIAGNOSIS 2026-09-09 (Builder) — **three of this row's own premises were wrong, and the severity was understated**

Shipped on `fix/phn-10-two-voices-at-once`. Plan:
[`PHN-10-nothing-can-stop-a-voicemail.md`](../../design/plans/PHN-10-nothing-can-stop-a-voicemail.md).

⛔ **The wrong framing below is CORRECTED, not deleted.** The `PHN-1f` premise sent the investigation
at the wrong file, and the record of that is worth more than a tidy page.

### ⛔ Correction 1 — `PHN-1f` was never in scope, and structurally could not have been

This row's headline says *"`PHN-1f` SHIPPED the queue that was supposed to prevent it"*, and its body
calls that *"a queue that exists and did not cover this path, which is a different and more serious
thing to diagnose."* **Two independent facts falsify it.**

1. **The queue's predicate can never see a voicemail as a blocker.**
   `EventPlaybackService.IsBlockedByAHigherPrioritySource` tests
   `_duckingService.GetPriority(s) >= threshold`, where the threshold is `GvMedia:PreemptAtPriority`
   = **8**. `EventPlaybackRequest.Priority` defaults to **6** and the Web client never overrides it.
   **6 < 8**, for both arms, always — and deliberately: ADR-029 §6.1 sets voicemail and text-TTS at
   the same 6 because *"They are the same class of thing."*
2. **Even at equal priorities the wait could not run.** `StartAsync`'s replacement arm tears the first
   playback down **inside `_gate`, before the second playback's acquisition task is started**;
   `WaitForClearAirAsync` runs later, after acquisition, by which time the first voicemail has already
   left the ducking set.

⭐ **`PHN-1f` shipped the mirror of ADR-029 §6.2 rule 2** (a playback starting under an *announcement*
at ≥ 8). Attended-vs-attended is **rule 1**, and rule 1 shipped with the single-slot replacement arm.
**The replacement arm works.** The failure was two layers below it, in the source.

### ⛔ Correction 2 — the "Verification" section above asks for the WRONG assertion

This row says *"⭐ **Assert the PRESENCE of serialisation** — the second waits and then plays."*
**For this path that is the opposite of the contract.** ADR-029 §6.2 rule 1 says attended-vs-attended
→ **replace**, and rejects queueing in as many words: *"queueing behind 40 seconds of voicemail would
be baffling."*

⛔ **A build in which the second voicemail waits satisfies this row's stated assertion and violates
the ADR.** The correct assertion is: **the second voicemail starts promptly and the first goes
silent.** ("Waiting, then playing" remains right for `PHN-1f`'s case — a voicemail behind a
*doorbell*. The two are different rules and this row conflated them.)

### ⛔ Correction 3 — the two reported behaviours are ONE behaviour

This row splits the report into two table rows and warns that *"simultaneity and preemption are
opposite failures and a fix for one can create the other."* Sound in general; **not applicable here.**
Both arms enter the same method (`EventPlaybackService.StartAsync`), take the same single slot, carry
the same priority, and are governed by the same ADR rule. ⭐ **So the owner's TTS ruling is the
SPECIFICATION, not a constraint to design around** — *"make voicemail do what TTS already does"* is
the whole fix.

### ⭐ The mechanism

`TearDownAsync`'s **first** statement disarms the guard its **last** statements depend on.

| # | What happens |
|---|---|
| 1 | The replacement arm claims voicemail A terminal and calls `TearDownAsync(A)` |
| 2 | **`TearDownAsync`'s FIRST line is `playback.Cancel()`** |
| 3 | `AudioFileEventSource._playbackCts` is linked over that token, so it is now cancelled |
| 4 | `AwaitCompletionAsync`'s filter is `when (!cancellationToken.IsCancellationRequested)` — **false** — so the exception propagates |
| 5 | ⛔ **`PlayWithSoundFlowAsync`'s `catch (OperationCanceledException)` sets `_isPlaybackActive = false` and does nothing else**, under a comment reading `// Playback was stopped`. **Nothing stopped it.** |
| 6 | `ReleaseSourceAsync` awaits `StopDuckingAsync` — a **500 ms** `Audio:DuckingReleaseMs` fade |
| 7 | `StopCoreAsync` reads `_isPlaybackActive` — **false** — and skips `SoundFlowPlaybackService.StopAsync` |
| 8 | `DisposeAsyncCore` — **identical guard, identical skip** |
| 9 | The `SoundPlayer` is never stopped, never `MasterMixer.RemoveComponent`ed, never disposed |

⭐ **Step 6 makes this deterministic, not a race.** ⭐ **And step 8 was provably dead even without it**,
because `StopCoreAsync` awaits `_playbackTask` to completion, by which point step 5 has definitively
run. **The voicemail arm had no working backstop at any layer.**

### ⛔ THE SEVERITY WAS UNDERSTATED — this is not "two voicemails"

| Stop path | Consequence before the fix |
|---|---|
| A second attended playback | **The observed defect.** Two voices. |
| The **user's own Stop button** | ⛔ The voicemail keeps playing; `Stopped` is published and the room disagrees. |
| **Doorbell preemption** (§6.2 rule 2) | ⛔ The announcement talks over it. **`PHN-2` check #10, DEFERRED — predicted to FAIL.** |
| **`GvMedia:MaxPlaybackSeconds`** | ⛔ ADR-029 §7.1 calls this *"THE guarantee"*. It did not stop voicemail audio. |
| **The `/sleep` edges** (§16.5) | ⛔ Audio continues on a dark panel with no stop control on screen. |
| **The last-circuit backstop** (`D30`) | ⛔ A browser reload does not silence it. |
| **Natural end of content** | Audio ends (the file ran out), but **the player and its mixer component are never removed** — one leak per voicemail, for the life of the process. |

⚠ The leak is a standing one on an appliance with weeks of uptime. Whether it interacts with the known
long-running capture-device degradation is a **hypothesis only** — nothing here measures it.

### ⭐ Why every test in the suite was blind to it

`AudioFileEventSourceTests`' 27 tests all construct the source **with no playback service**, so
`PlayCoreAsync` takes the silent-simulation branch and `_isPlaybackActive` is never true in any test.
And `EventPlaybackServiceTests.ASecondStartReplacesTheFirst_AndTheFirstIsTornDown` — **the only
replacement test in the tree** — asserts `first.StopCalls == 1` / `first.DisposeCalls == 1`, **both
true today, with the bug live.** The service half was always correct; the fake then does what the fake
does. ⛔ **Do not cite it as evidence.**

### ⛔ Not closable by a green suite

The cabinet gate is: start one voicemail, start a second, confirm **the first goes silent while the
second plays** — **plus a TTS control in the same sitting**, because the whole risk of the fix is
buying the first by breaking the preemption the owner ruled correct.

# `PHN-8` — speak a text message from the phone dashboard, without opening the thread

[← Builder Queue index](../BUILDER_QUEUE.md)

🟡 **P2.** Filed 2026-09-08 on owner request:

> "I want to make sure we have an item on the punchlist to TTS the text messages from the phone
> dashboard."

## ⚠ Read this first — the capability EXISTS, one level down

`PHN-3` shipped 2026-09-07 ([#598](https://github.com/mmackelprang/RTest/pull/598)) and **already
speaks text messages through the console.** This row is **not** a duplicate and **must not re-implement
it.**

**What `PHN-3` built:** a 44 px gutter play button on `MessageBubble.razor` (`:32`,
`class="msg-speak-btn"`), a `Mine`-gated state machine, `GvSpeechText.ForMessage` with its eight
content rules, and `EventPlaybackApiService.StartSpeechAsync`. **Per message, inside an open
conversation.**

**What is missing:** the **thread list** has no speak affordance. Verified 2026-09-08 —
`PhoneTextsPanel.razor`'s only `speak`/`speech` matches are doc comments *about* the message-level
feature; there is no button on a conversation row. **So from the dashboard you cannot hear a message
without first opening its thread.**

⭐ **This row is therefore a surface/affordance question, not an audio one.** The speech path,
the content rules, the rejection reasons and the state machine are all built and shipped. Reuse them.

## The design question, which comes before any plan

**What does "speak" mean at thread-list level?** The options are genuinely different and the answer
changes the whole row:

1. **Speak the latest message in that thread** — cheapest, and matches how a dashboard glance works.
   ⚠ But "latest" is ambiguous on a thread whose last message is `Mine`, which `PHN-3`'s state machine
   already treats specially.
2. **Speak all unread messages in that thread**, oldest first. More useful, and closer to what someone
   walking past the console actually wants. Needs a queueing decision and a stop affordance.
3. **Speak unread across all threads** — a single "read me my messages" action on the dashboard. Most
   valuable and most work; also the only one that needs an ordering rule across conversations.

⛔ **Needs a Designer answer before planning.** `PHN-3`'s handoff already established the visual
language for the per-message button and the topbar chip — the dashboard affordance should extend that
rather than invent a second idiom.

## Constraints inherited from `PHN-3` — do not re-derive these

- **`GvSpeechText.ForMessage`'s eight content rules** already handle what is and is not speakable.
- **Rejection reasons** come back from `StartSpeechAsync` as tokens; ⚠ the transport failure token is
  **`Transport`**, not `Unreachable` (`PhoneTextsPanel.razor:361` records this — an earlier draft had
  it wrong).
- **`TextTooLong` should be unreachable** — `GvSpeechText` caps at the same number — and exists as a
  backstop.
- ⚠ **Never log message bodies or phone numbers.** `PHN-5` exists because a raw phone number reached
  `journalctl` on a stock box, and `Radio.Web`'s Console sink is **unrestricted**, so every
  `Information` line there is a journald line.

## ⚠ Interactions worth knowing before estimating

- **`PHN-4` deleted the composer** from this surface, so the thread list's layout changed recently.
  Re-derive anchors; do not trust any pre-`PHN-4` citation.
- **`GV-9` shipped alignment and overflow work on the conversation rows** ([#608], 2026-09-08) —
  including a 20 px shift and four structural tests that a new affordance in that row must not break.
  ⛔ **Do not delete those tests as "odd assertions"**; they exist as a tripwire.
- **`GV-7`** re-scopes the same surface and **must not run concurrently** with work here.
- **`GV-12`** (the surface never retries after an outage) touches the same panel's failure handling.

## Verification

Component-testable: assert the affordance appears on a conversation row, that activating it calls
`StartSpeechAsync` with the right message id, and that a rejection renders the mapped reason.
**Must fail first** — there is no such affordance today, so a test asserting its presence is RED
against `main` by construction. Say so, and confirm it rather than assuming.

⚠ **Speech itself needs the box** — it goes through the real event playback path, and `D26` removed
eSpeak, so there is **no offline TTS**. A component test proves the wiring, not that anything was
heard. **Do not claim otherwise in the PR body.**

⚠ **And check `AUD-2` before judging it by ear**: the owner reported 2026-09-08 that **ducking does not
work when playing a voicemail**, which likely means music does not duck under event playback either.
A speak test judged by listening, over un-ducked music, will read as broken when it is not.

---

## ⭐ OWNER DECISIONS 2026-09-08 — the design gate is DISCHARGED, both questions answered

The owner saw the deployed `PHN-3` button and said:

> "I can see the button now, but I agree that the play button would be much better to be on the
> 'main' tab rather than in the individual text / message."

Two follow-up questions were put and both answered.

### 1. **ADD, do not move — the per-message button stays.**

Despite the word *"rather than"*, the decision is that **both** affordances exist: the main tab gains
one, and `PHN-3`'s per-message gutter button survives for when you are already reading a thread.

⛔ **Nothing shipped in `PHN-3` gets reverted.** No bubble markup removed, no `Mine`-gating removed, no
tests deleted.

⚠ **The cost of this choice is stated so the plan handles it rather than discovers it: two places can
now start speech.** The stop/state handling must be coherent across both — pressing play on a row while
a bubble is already speaking, and vice versa, are real cases. `PHN-3`'s state machine already models
"speaking" for a single origin; **it now needs to model which origin.**

⚠ **Two speak affordances on one surface is exactly the drift a Polisher pass exists to catch.** Run
one before merge, and make the two read as one idiom rather than two.

### 2. **Speak ALL UNREAD in that thread, oldest first.**

Not just the latest, and not across all threads.

- **Falls back to the latest message when nothing is unread** — a row with no unread should still do
  something sensible rather than nothing.
- **Needs a queue and a stop control.** Speaking three messages back-to-back is the common case (they
  arrive together), and there must be a way to stop partway.
- ⚠ **"Unread" must mean the same thing here as it does to the unread dot.** `GV-4`/`GV-6` already own
  mark-read semantics and a `409 markread_disabled` path; **do not mint a second definition of unread.**
  Check what the dot binds to and bind to that.
- ⚠ **Does speaking mark them read?** Not decided, and the plan must ask rather than assume. There is a
  real argument each way, and mark-read is gated by `RotaryPhone:Gv:MarkReadEnabled` on a flag that can
  be dark — so the answer interacts with `GV-6`.

**The Designer gate is discharged for scope.** A Designer is still worth consulting on the affordance's
*appearance* on a conversation row — `PHN-3`'s handoff set the visual language and this should extend
it, not invent a second idiom — but the row is no longer blocked on a decision.

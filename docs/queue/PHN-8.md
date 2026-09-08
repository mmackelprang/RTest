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

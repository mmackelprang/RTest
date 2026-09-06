# PHN-3 — NEW ROW 2026-09-05 — Feature B, the eighth and last PR of the ADR-029 arc, and it never had a row in this file.

> Queue dossier for row **`PHN-3`** of [`BUILDER_QUEUE.md`](../BUILDER_QUEUE.md).
> The detail below was moved verbatim out of that row's Item cell on 2026-09-06; only
> whitespace, the table's `\|` escapes and docs-relative link prefixes changed.
>
> ⚠ **Directional words in the prose were written when every row shared one file.**
> *above*, *below* and *this file* may now point across files — most often at
> [`BUILDER_QUEUE_ARCHIVE.md`](../BUILDER_QUEUE_ARCHIVE.md) or a sibling in this
> directory. They were left verbatim rather than reworded, which would be a content edit.

| Field | Value |
|---|---|
| Status | 📋 |
| Plan | [`PHN-3-the-sms-speak-button.md`](../../design/plans/PHN-3-the-sms-speak-button.md) |
| Spec / handoff | [handoff §B `:297-430` + §Cross-1…5 `:79-181`](../design-handoffs/HANDOFF-phone-console-audio-and-canned-replies.md) · [ADR-029](../../design/decisions/2026-08-03-gv-audio-through-engine.md) §4.2 / §9 (the amendment) · [`PHN-2` plan](../../design/plans/PHN-2-retire-the-audio-element.md) §0.5 · punch list [§4.4](../HANDOFF-GA-PUNCH-LIST.md) |
| Depends on | ✅ **MET.** `O6` (*`PHN-1` before or with `PHN-2` / `PHN-3`*) is satisfied: `PHN-1a`…`PHN-1f` ✅ and `PHN-2` ✅ [#566](https://github.com/mmackelprang/RTest/pull/566) have all merged. ✅ **`PHN-4` has MERGED** ([#578](https://github.com/mmackelprang/RTest/pull/578)), so the sequencing instruction that used to sit here is **retired** — no other row is queued against `PhoneTextsPanel.razor`, and this row can be claimed whenever it reaches the front. ⚠ **The file is now 258 lines, not 442, and `PHN-4` moved essentially every line in it.** The plan was repaired against `656f58e6` — **read [§0.0](../../design/plans/PHN-3-the-sms-speak-button.md) before editing that file**, because three of the plan's original edits referenced members `PHN-4` deleted (`StatusFor`, `RetrySend`, and a zero-arg `ResolveThreadName`) and would not have compiled. ✅ **The `GV-10` ordering preference is DISCHARGED 2026-09-06 — `GV-10` was FALSIFIED** ([`F-5-DIAGNOSIS.md`](../uat/2026-07-31-gv-live-data/F-5-DIAGNOSIS.md)): conversation bubbles bind `SmsMessageDto.Text` and render it verbatim, and this row reads that **same field**, so it speaks exactly what is displayed and there is no ellipsis to sequence around. ⛔ **The instruction that clause carried still stands, unchanged: do not strip a trailing ellipsis, and do not otherwise work around one here** — if Google ever does hand us snippets, this feature must expose that upstream defect audibly rather than conceal it (plan `:377`). |
| Branch | `feat/phn-3-speak-a-text` |

## Detail

⭐ **NEW ROW 2026-09-05 — Feature B, the eighth and last PR of the ADR-029 arc, and it never had a row in this file.**

🟠 **P1, `O6`, punch list [§4.4 `:1066`](../HANDOFF-GA-PUNCH-LIST.md)**, where it has always been listed with **`Queued? No`** — corrected to `Yes` by this pass.

**An inbound SMS bubble gets a 44px gutter button that speaks the message through the console**: same `IEventPlaybackService` seam, same ducking, same topbar chip as `PHN-2`'s voicemail, on the Speech arm instead of the RemoteMedia one.

⭐ **`D31` does NOT touch this row and arguably strengthens it** — reading a text aloud is the read surface working as intended, not a reply path; the handoff says so independently at `:420-424` (*“a short-code thread cannot be replied to; it can absolutely be read aloud”*).

⛔ **The button is not gated by `SendEnabled`, not gated by repliability, and must not be tidied away when `PHN-4` deletes the composer.**

⭐ **Most of it is wiring**: the seam, the route, the one-voice-at-a-time replacement arm, server-owned state, and `ConsolePlaybackState` all shipped — and `ConsolePlaybackState.cs:73-78` **already** maps `"Speech" → "Message"` for the chip. `EventPlaybackApiService.cs:110-111` names this row in its own doc comment as the caller its Speech sibling is waiting for.

**Genuinely new:** `GvSpeechText.ForMessage` (a pure static helper, eight content rules, **confirmed absent from `src/`**), `StartSpeechAsync`, the button, and the handoff's `§Ph` CSS.

⚠ **SCOPE, corrected 2026-09-06 — still true about the region, wrong about the shape.** The row used to say it *"edits only the `<MessageBubble>` call in the list branch"*. That remains the only **markup** edit, but it is no longer the only edit to the file: **Task 4c now injects `NotificationService` into the panel**, because `PHN-4` deleted `RetrySend` and with it the only failure-surfacing path the plan was told to copy. **In `PhoneTextsPanel.razor` this row edits the `<MessageBubble>` call in the list branch (`:99` — one line, one parameter, since `PHN-4`), adds one computed property, injects `NotificationService` for the §Cross-5 failure toast, and touches nothing else.**

⚠⚠ **FOUR PLACES THE HANDOFF AND THE SHIPPED TREE DISAGREE, all settled in the plan rather than left to Builder.**

**(1) The handoff's engine story is STALE**: it says ADR-029 §9 pins message speech to local `espeak-ng`, but the owner **reversed** that (ADR `:491-511`, *“use the selected engine”*), `GvMedia:SpeechEngine` was **deleted**, and `TTS-9` removed eSpeak from the codebase — so the deployed engine is **Google**, synthesis is a cloud round-trip, and **this is the row where *‘private SMS bodies reach Google's TTS API’* (ADR `:509`) stops being a recorded trade and starts happening.** Its open **Q5** is therefore closed, not open.

**(2) `GvMedia:MaxSpeechChars` REJECTS rather than truncates** (`GvMediaOptions.cs:79-88`), so **client-side truncation is this row's job** — without it a long MMS yields `400 TextTooLong` and the button fails silently, which is the opposite of the handoff's *“no UI indication”*.

**(3) `EventPlaybackState.Waiting` is real for a Speech playback** (`PHN-1f`'s `D28`; `EventPlaybackService.cs:493`) and is absent from the handoff's §B4 state table — the plan renders it as `Preparing`.

**(4) `SmsMessageDto` carries NO sender name**, so the `Message from {Name}.` lead-in needs a parameter threaded down from `PhoneTextsPanel` — ⚠ **and it must be null rather than an identifier** — `PhoneMessagesPanel.OpenThreadName` falls back **twice**, to the bare number and then to the raw thread id, and the handoff forbids reading either aloud (14 of 20 live threads have no resolvable name, so this is the common case). **Two guard clauses, not one** — plan `C-108`. _(Corrected 2026-09-06: this point previously cited `ResolveThreadName`, which after `PHN-4` takes a `SmsThreadDto` and serves the thread list only; the conversation's name comes from `HeaderName` ← `PhoneMessagesPanel.OpenThreadName`.)_

⚠⚠ **THE SINGLE MOST IMPORTANT TEST IN THE ROW:** the MMS sender-prefix strip must require a literal `" - "` separator, because a looser `^\d+\s+` eats the handoff's own headline example — `"77971 is your Facebook confirmation code"` — and *“verification codes are the single most valuable thing this feature reads”*.

⭐ **`§A4b`'s single-selection group is free**: the server owns the one attended playback, so a `Mine` gate returns every other button to rest silently with no client bookkeeping — **do not build any**.

⚠ **Copy `VoicemailPlayer`'s `_starting` re-entrancy guard**; a double-tap is the default gesture on a wall panel.

⚠ **Every bubble subscribes to `ConsolePlaybackState.Changed` and must dispose** — a 40-message thread mounts 40 subscribers.

**Est. 1.5–2 d** (punch list says 2–3 d, quoted when *“nearly all its cost is `PHN-1`”* — that cost is now paid).

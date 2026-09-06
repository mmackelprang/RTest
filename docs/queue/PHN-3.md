# PHN-3 — NEW ROW 2026-09-05 — Feature B, the eighth and last PR of the ADR-029 arc, and it never had a row in this file.

> Queue dossier for row **`PHN-3`** of [`BUILDER_QUEUE.md`](../BUILDER_QUEUE.md).
> The detail below was moved verbatim out of that row's Item cell on 2026-09-06; only
> whitespace, the table's `\|` escapes and docs-relative link prefixes changed.

| Field | Value |
|---|---|
| Status | 📋 |
| Plan | [`PHN-3-the-sms-speak-button.md`](../../design/plans/PHN-3-the-sms-speak-button.md) |
| Spec / handoff | [handoff §B `:297-430` + §Cross-1…5 `:79-181`](../design-handoffs/HANDOFF-phone-console-audio-and-canned-replies.md) · [ADR-029](../../design/decisions/2026-08-03-gv-audio-through-engine.md) §4.2 / §9 (the amendment) · [`PHN-2` plan](../../design/plans/PHN-2-retire-the-audio-element.md) §0.5 · punch list [§4.4](../HANDOFF-GA-PUNCH-LIST.md) |
| Depends on | ✅ **MET.** `O6` (*`PHN-1` before or with `PHN-2` / `PHN-3`*) is satisfied: `PHN-1a`…`PHN-1f` ✅ and `PHN-2` ✅ [#566](https://github.com/mmackelprang/RTest/pull/566) have all merged. **Not blocked by `PHN-4`** — they touch different regions of `PhoneTextsPanel.razor` (this row edits only the `<MessageBubble>` call in the list branch), but **both edit that one 442-line file, so claim them SEQUENTIALLY, never concurrently** — the same instruction already given for `GV-9` vs `PHN-4`. ⚠ **`GV-10` is an ordering PREFERENCE, not a dependency:** if the bubbles carry snippets rather than full bodies, this feature reads an ellipsis aloud — that is `GV-10`'s defect and **must not be worked around here**, since a workaround would hide it in the one feature where the difference is audible. |
| Branch | `feat/phn-3-speak-a-text` |

## Detail

⭐ **NEW ROW 2026-09-05 — Feature B, the eighth and last PR of the ADR-029 arc, and it never had a row in this file.**

🟠 **P1, `O6`, punch list [§4.4 `:1066`](../HANDOFF-GA-PUNCH-LIST.md)**, where it has always been listed with **`Queued? No`** — corrected to `Yes` by this pass.

**An inbound SMS bubble gets a 44px gutter button that speaks the message through the console**: same `IEventPlaybackService` seam, same ducking, same topbar chip as `PHN-2`'s voicemail, on the Speech arm instead of the RemoteMedia one.

⭐ **`D31` does NOT touch this row and arguably strengthens it** — reading a text aloud is the read surface working as intended, not a reply path; the handoff says so independently at `:420-424` (*“a short-code thread cannot be replied to; it can absolutely be read aloud”*).

⛔ **The button is not gated by `SendEnabled`, not gated by repliability, and must not be tidied away when `PHN-4` deletes the composer.**

⭐ **Most of it is wiring**: the seam, the route, the one-voice-at-a-time replacement arm, server-owned state, and `ConsolePlaybackState` all shipped — and `ConsolePlaybackState.cs:73-78` **already** maps `"Speech" → "Message"` for the chip. `EventPlaybackApiService.cs:110-111` names this row in its own doc comment as the caller its Speech sibling is waiting for.

**Genuinely new:** `GvSpeechText.ForMessage` (a pure static helper, eight content rules, **confirmed absent from `src/`**), `StartSpeechAsync`, the button, and the handoff's `§Ph` CSS.

⚠⚠ **FOUR PLACES THE HANDOFF AND THE SHIPPED TREE DISAGREE, all settled in the plan rather than left to Builder.**

**(1) The handoff's engine story is STALE**: it says ADR-029 §9 pins message speech to local `espeak-ng`, but the owner **reversed** that (ADR `:491-511`, *“use the selected engine”*), `GvMedia:SpeechEngine` was **deleted**, and `TTS-9` removed eSpeak from the codebase — so the deployed engine is **Google**, synthesis is a cloud round-trip, and **this is the row where *‘private SMS bodies reach Google's TTS API’* (ADR `:509`) stops being a recorded trade and starts happening.** Its open **Q5** is therefore closed, not open.

**(2) `GvMedia:MaxSpeechChars` REJECTS rather than truncates** (`GvMediaOptions.cs:79-88`), so **client-side truncation is this row's job** — without it a long MMS yields `400 TextTooLong` and the button fails silently, which is the opposite of the handoff's *“no UI indication”*.

**(3) `EventPlaybackState.Waiting` is real for a Speech playback** (`PHN-1f`'s `D28`; `EventPlaybackService.cs:493`) and is absent from the handoff's §B4 state table — the plan renders it as `Preparing`.

**(4) `SmsMessageDto` carries NO sender name**, so the `Message from {Name}.` lead-in needs a parameter threaded down from `PhoneTextsPanel` — **and it must be null rather than the raw number**, since `ResolveThreadName` falls back to one and the handoff forbids reading the identifier aloud (14 of 20 live threads have no resolvable name, so this is the common case).

⚠⚠ **THE SINGLE MOST IMPORTANT TEST IN THE ROW:** the MMS sender-prefix strip must require a literal `" - "` separator, because a looser `^\d+\s+` eats the handoff's own headline example — `"77971 is your Facebook confirmation code"` — and *“verification codes are the single most valuable thing this feature reads”*.

⭐ **`§A4b`'s single-selection group is free**: the server owns the one attended playback, so a `Mine` gate returns every other button to rest silently with no client bookkeeping — **do not build any**.

⚠ **Copy `VoicemailPlayer`'s `_starting` re-entrancy guard**; a double-tap is the default gesture on a wall panel.

⚠ **Every bubble subscribes to `ConsolePlaybackState.Changed` and must dispose** — a 40-message thread mounts 40 subscribers.

**Est. 1.5–2 d** (punch list says 2–3 d, quoted when *“nearly all its cost is `PHN-1`”* — that cost is now paid).

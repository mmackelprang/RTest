# F-5 diagnosis — conversation bubbles render the full body, not the list snippet

**Verdict: FALSIFIED as a Radio Console defect.** Queue row `GV-10` should close with no code
change in this repo. One residual question is genuinely upstream and is **not** answerable from
source; see § *What is left, and where it lives*.

Investigated 2026-09-06 by a read-only pass over both repos. Nothing was modified. Confidence
**~90%** on the falsification, **~60%** that `GV-10` closes outright rather than converting to a
cross-repo note.

---

## The claim under test

`GV-10`, from UAT finding **F-5** (LOW) in [`REPORT.md`](REPORT.md) `:291-298`, echoed at
`:188-189`:

> do conversation bubbles render the list snippet instead of the full message body?

The row was filed **UNPROVEN** and explicitly allowed to close with no code change. It does.

---

## Why the original inference does not hold

This is the part worth carrying forward, because the reasoning failed in a way that would repeat.

F-5 reasoned: *the bubble matched the truncated list preview, therefore the conversation view is
rendering the snippet.* That inference is invalid given how the upstream parser builds the preview.

`RotaryPhoneController.GVBridge/Clients/PositionalGvThreadParser.cs:148-154` derives
`LastMessagePreview` by reading **`SmsTextIdx` on the last message node** — the same wire slot, on
the same node, that `ParseSmsMessages` (`:92-115`) reads to build the bubble's `Text`.

So the newest bubble and the thread-list preview are **the same string by construction**. They
would match identically even if both carried complete, untruncated bodies. The observed match was
guaranteed by the data path, not evidence about truncation.

---

## Evidence chain

### Two distinct fields, never cross-wired

`src/Radio.Web/Models/ApiModels.cs:1138-1153` — `SmsMessageDto.Text` (per message) and
`SmsThreadDto.LastMessagePreview` (per thread) are separate members of separate records.

| Surface | Binds | Site |
|---|---|---|
| Conversation bubble | `SmsMessageDto.Text` | `MessageBubble.razor:10` → `DisplayText` at `:49-50` |
| Thread-list row | `SmsThreadDto.LastMessagePreview` | `PhoneTextsPanel.razor:170` → `PreviewText` at `:248-249` |

`MessageBubble`'s `DisplayText` is a verbatim pass-through — no cap, no substring, no ellipsis.

No code path feeds `LastMessagePreview` into a bubble. `grep "new SmsMessageDto"` across `src/`
returns **zero** production hits, so no `SmsMessageDto` is ever synthesized from a thread row. The
one cross-field assignment in the repo runs the *other* direction: `PhonePage.razor:826` writes a
full body into the preview slot (`LastMessagePreview = msg.Text`).

### The bubble's data arrives untruncated

`PhonePage.razor:701` → `GetSmsThreadMessagesAsync(threadId)`; `:715` stores
`result.Value!.Messages.ToList()` — a straight `ToList()`, no projection.
`GvBridgeApiService.cs:229-230` deserializes only. `Radio.API` is not in this path at all.

Upstream is verbatim too: `GvSmsController.cs:74-82` maps `Text: m.Text`;
`PositionalGvThreadParser.cs:110` reads `GvProtobuf.GetString(msg, SmsTextIdx)`; `GvProtobuf`
`:7-13` is a bare `el.GetString()`. A `Substring`/`[..n]` sweep of the whole GVBridge project hits
only cookie-prefix logging and correlation-id hashing.

### Neither data truncation nor display truncation

- **Not display.** `.msg-bubble` (`design-system.css:5852-5858`) sets `max-width: 72%;
  word-break: break-word;` with **no** `text-overflow`, `-webkit-line-clamp`, `max-height`, or
  `white-space: nowrap`. `.msg-text` has **no CSS rule at all**. Bubbles wrap and grow.
- **Not data.** Every hop from Google's JSON to `@DisplayText` is verbatim, in both repos.

For contrast, the thread-list row *does* truncate visually — `.list-item-subtitle`
(`design-system.css:658-664`) is `nowrap` + `overflow: hidden` + `text-overflow: ellipsis`. That is
display-only, renders **U+2026**, and touches the list alone.

### The one truncation in this repo is not on this path

`PhonePage.razor:856-857` `Truncate(s, n = 80)` has a single caller at `:804` — the toast body for
a message arriving into a **closed** thread. It never touches `_openThreadMessages` (the append at
`:795` stores the whole DTO), it emits `…` (U+2026) where F-5 observed a literal `...`, and its
80-char cap does not match F-5's ~155-char string.

**Therefore any ellipsis F-5 saw was inside the string** — it arrived that way, or the message
genuinely ended in `...`.

---

## Consequence for PHN-3

**PHN-3 is not gated by this row and can be built now.**

PHN-3 reads `SmsMessageDto.Text` — the same field the bubble already renders verbatim — so it will
speak exactly what is displayed. Its plan's `C-114` correctly forbids reading `DisplayText`, which
would speak the literal `"(no text)"` placeholder.

The plan's ⛔ *"do not strip a trailing ellipsis"* is right and needs no amendment: if Google ever
does hand us snippets, PHN-3 audibly exposes an upstream defect rather than concealing it.

---

## What is left, and where it lives

Collapsed to one narrower question that source cannot answer: **is the wire value at `SmsTextIdx` a
full body or a snippet?**

If it is a snippet, that is a **RotaryPhone** item, not a Radio Console one — verify `SmsTextIdx`
against a live capture and find whether a full-body slot exists elsewhere in the message node.
PHN-3 is unaffected either way.

Note `PositionalGvThreadParser.cs:8-9` and `:32-39` still declare *"EVERY index below is
UNVERIFIED"*. Index 4 demonstrably yields real message text in production, so that comment is stale
in the ways that matter — but "yields real text" is not "yields the *full* text", and a snippet slot
adjacent to a body slot is exactly the shape that would have produced F-5.

### The one command that closes it

Against a known-long **non-group** thread:

```bash
curl -s "http://radio:5004/api/gvbridge/sms/threads/t.32665?count=50"
```

Two disciplines carried over from [`F-1-DIAGNOSIS.md`](F-1-DIAGNOSIS.md): run it inside a healthy
auth window (`:267-269`) or a 502 masquerades as a result, and keep to a non-group thread, since
`%2F` ids return 0 messages for an unrelated reason (`GvBridgeApiService.cs:191-201`).

- **Full body returned** → close `GV-10`, no change in either repo, and correct F-5's inference in
  the record.
- **Snippet returned** → open a RotaryPhone item. `GV-10` still closes here.

---

## A coverage gap this investigation exposed

`MessageBubbleTests.cs` is the **only** test source referencing `MessageBubble` / `msg-text` /
`msg-bubble` (an exhaustive Grep over `tests/` returned 14 matches under a 25-entry limit). Its one
content assertion is `NullText_RendersPlaceholder` (`:33-38`, asserting `"(no text)"`); the other
four tests assert CSS classes only.

**So nothing pins what a bubble renders for a non-null `Text`.** The verbatim pass-through at
`MessageBubble.razor:49-50` — the single fact this diagnosis rests on, and the one PHN-3 depends on
for what it speaks aloud — is guarded by reading, not by a test. A future edit that introduced a cap
or a clamp there would pass the suite green.

Worth a test when PHN-3 is built, since PHN-3 gives that pass-through a second consumer whose
failure mode is audible rather than visual.

---

## Out of scope, flagged not fixed

- `PositionalGvThreadParser.cs:8-9`'s "EVERY index is UNVERIFIED" is stale against production
  behaviour — a comment-accuracy issue in RotaryPhone.
- `PhoneTextsPanel.razor:119-135`'s thread-list skeleton branch is dead in production, per its own
  comment at `:121-124`.

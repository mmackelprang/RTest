# PLAN — `PHN-3` · Feature B: a text message gets a play affordance that speaks it through the console

> **Row:** `PHN-3` — ⭐ **NEW ROW, created by this pass.** It had none: the token appears in
> `docs/BUILDER_QUEUE.md` four times in prose and never as a table row. Punch list `:1066` (§4.4),
> 🟠 **P1**, `O6`.
> **Branch:** `feat/phn-3-speak-a-text`
> **Estimate:** **1.5–2 d**. The punch list says 2–3 d *"nearly all its cost is `PHN-1`"* — and
> `PHN-1a`…`PHN-1f` and `PHN-2` have all shipped, so that cost is already paid. §0.6 says what is
> left.
> **Planned against** `main` at **`6c220461`**. Every line number read out of the tree at that
> commit.
> **Design input:** [`docs/design-handoffs/HANDOFF-phone-console-audio-and-canned-replies.md`](../../docs/design-handoffs/HANDOFF-phone-console-audio-and-canned-replies.md)
> §B (`:297-430`) and §Cross-1…5 (`:79-181`). **Relationship: `follows`, with three recorded
> deviations and one section of the handoff that is factually stale** — §0.3.

---

## 0. Read this before Task 1

### 0.1 What this row is, in one paragraph

`PHN-2` retired the `<audio>` element and made a voicemail play *through the console* — the speakers
in the cabinet, ducking the music, stoppable from a topbar chip anywhere in the app. Feature B is the
same mechanism pointed at a different payload: a text message gets a play button that speaks it. The
handoff's own framing is that this *"is the feature that makes the phone surface make sense in
furniture"* — a console you can ask to read you a text from across the room. **Almost all of the
machinery already exists**: the seam (`IEventPlaybackService`), its Speech arm, the REST route, the
server-owned state, the topbar chip, and the client's `ConsolePlaybackState` all shipped in
`PHN-1a`…`PHN-2`. §0.5 is the honest split between *wiring* and *new*. The genuinely new work is two
things: a **pure static text helper** that turns an SMS body into an utterance under eight content
rules, and a **44px button in the bubble gutter** that joins the one-voice-at-a-time group.

### 0.2 ⚠ `D31` does not touch this row, and it is worth saying why in the plan itself

Owner decision `D31` (2026-09-05) says **SMS sending stays off, permanently**. It parked `GV-5` 🚫
and converted Feature C from a feature into a deletion (`PHN-4`). It would be an easy and expensive
mistake to read Feature B as part of that retreat.

**It is the opposite.** `D31` removes the *write* surface and leaves the *read* surface as the whole
point of the phone console — and reading a text aloud is the read surface working as intended, not a
reply path in disguise. The handoff says so independently and before `D31` existed, at `:420-424`:

> A short-code thread cannot be replied to; it can absolutely be **read aloud**. Feature C's
> four-state gate applies **only** to the reply affordance. The play button is present and live on
> every inbound bubble in **every one of the four states**… *"for the 70% of threads you can't reply
> to, the console can still read them to you."*

⛔ **Therefore: the speak button is NOT gated by `RotaryPhone:Gv:SendEnabled`, not gated by
repliability, and not gated by `GvBridgeSendService`.** A future reader finding a play button on a
surface whose composer was deleted must not "tidy" it away. The queue banner records `D31` as
strengthening the three read-surface rows; this is the fourth.

### 0.3 ⚠⚠ The handoff's §B4/§B3 engine story is STALE, and the ADR that supersedes it is the one to follow

**`C-105`.** The handoff (2026-08-01) was written against a version of ADR-029 §9 that has since been
**reversed by the owner**, and it still asserts the old position in four places.

`design/decisions/2026-08-03-gv-audio-through-engine.md:491-511`:

> **This section previously pinned message speech to local `espeak-ng` and introduced
> `GvMedia:SpeechEngine` as the escape hatch. The owner's instruction supersedes it:** *"the TTS
> engine in the radio console supports both Google and Azure TTS, so make sure the text messaging
> uses the currently selected TTS engine."*
> `:507` — **The answer is: use the selected engine.**
> `:602` — `GvMedia:SpeechEngine` is **deleted, not redefined.**
> `:509` — accepted trade: *"private SMS bodies reach Google's TTS API on each play."*

The handoff still says the reverse — `:416` (*"ADR-029 §9 pins message speech to the local
`espeak-ng` engine, so synthesis is on-box and there is no network round trip"*), `:418`, `:742-748`,
and open question **Q5** (*"Robotic-but-private, or better-but-cloud"*).

**The shipped code follows the ADR, not the handoff.** `EventPlaybackService.AcquireSpeechAsync:714`
resolves `ResolveEngine(request.Engine, tts.DefaultEngine)`, and `TTSOptions.DefaultEngine` is
deployed as **`"Google"`** (`src/Radio.API/appsettings.json:176`). `eSpeak` was removed from the
codebase entirely by `TTS-9` (#548), so the handoff's local option **does not exist to choose**.

**Three consequences this plan has to carry, rather than leave to be discovered:**

1. **Latency is a cloud round-trip, not on-box synthesis.** `Preparing` is a real state with a real
   duration and the handoff's *"if it persists past ~2s"* copy (`:416`) is load-bearing rather than
   defensive.
2. **Offline is a failure mode.** With no network there is no speech, and the error toast (§Cross-5)
   is the whole of the UX. The handoff assumed on-box synthesis could not fail this way.
3. ⭐ **This is the row where "SMS bodies reach Google" stops being a recorded trade and starts
   happening.** ADR `:509` records the owner accepting it in the abstract. **§8 puts it back to the
   owner as the one thing worth a yes before this ships** — not to re-litigate a decision, but
   because a row that first realises a privacy trade should say so out loud.

⛔ **Q5 is not an open question and must not be treated as one.** It was answered by the owner and
recorded in the ADR. Task 10 corrects the handoff so the next reader does not re-open it.

### 0.4 ⚠ Three more places the handoff and the shipped tree disagree

**`C-106` — `GvMedia:MaxSpeechChars` REJECTS; it does not truncate. Client-side truncation is this
row's job and the handoff's *"no UI indication"* is only achievable because of it.**

`src/Radio.Core/Configuration/GvMediaOptions.cs:79-88`, verbatim:

> ⚠ **The behaviour is REJECTION, not truncation**: over-length text is refused as
> `EventPlaybackRejection.TextTooLong` and mapped to a 400 with that reason. ADR-029 §4.2 says
> *"truncated with a spoken tail"*; **that is overridden**… **`Radio.Web` truncates visibly before
> sending (`PHN-3`).**

The queue's `PHN-1c` entry says the same in fewer words: *"`MaxSpeechChars` ships as **rejection**
deliberately (`PHN-3` owns visible client-side truncation)"*.

Handoff §B3 rule 7 (`:398`) calls the cap *"a safety valve rather than a routine truncation"* needing
**no UI indication**. **Both can be true, and only if this row truncates**: the client caps the string
so the user simply hears the first 1000 characters and the Stop button remains the real control —
exactly the handoff's intent. Without the truncation the same message yields **`400 TextTooLong`**
and the button fails silently, which is the opposite of no-indication. Task 2 rule 7.

**`C-107` — `EventPlaybackState.Waiting` is a real state for a Speech playback and the handoff's §B4
state table does not have it.**

`PHN-1f` (`D28`) added `Waiting = 6`, and `EventPlaybackService.cs:493` applies
`WaitForClearAirAsync` on **both** arms — so tapping a speak button while an announcement is sounding
parks the playback for up to `GvMedia:MaxQueuedWaitSeconds` (30 s). The handoff's four-row table
(`:406-414`) predates it.

**Resolution, and it needs no new visual:** `Waiting` renders **exactly as `Preparing`** — the
spinner — and differs only in the `title` / `aria-label`, which take `VoicemailPlayer`'s already
shipped copy (`Waiting for the announcement to finish…`). A bubble gutter has no room for a sub-line
and the handoff is right that it should not grow one. `EventPlaybackSnapshotDto.IsLive` is a
**deny-list** (`ApiModels.cs:1517-1521`), so `Waiting` already counts as live and the chip and Stop
affordance come for free.

**`C-108` — `SmsMessageDto` carries no sender name, so the `Message from {Name}.` lead-in needs a
parameter this component does not have.**

`ApiModels.cs:1138-1145` gives `SmsMessageDto` an `Id`, `ThreadId`, `Direction`,
`CounterpartyNumber`, `Text`, `SentAt`, `IsRead` — **and no name**. The resolved name lives on
`SmsThreadDto.CounterpartyName` (`:1150`) and is computed by
`PhoneTextsPanel.ResolveThreadName` (`:413-430`), which walks `CounterpartyName` → a normalised
contact match → the raw number.

⛔ **Do not resolve the name inside `MessageBubble`**, and do not inject a contact service into it.
It is a presentational component with three parameters and no services, and the name is a
**per-thread** fact already computed once by the panel. Task 4 threads it down as one nullable
parameter. ⚠ **And it must be the resolved *name*, never the raw number** — handoff `:386` says *"Do
**not** read the identifier aloud"*, and `ResolveThreadName` falls back to the number, so the panel
must pass `null` rather than that fallback (Task 5).

### 0.5 What is wiring and what is new — the split the estimate rests on

| Piece | Status | Where |
|---|---|---|
| `IEventPlaybackService`, the Speech arm, validation | ✅ **shipped** | `PHN-1a`, `PHN-1c` |
| `POST /api/audio/events` → 202 + snapshot; `DELETE /{id}` → 204 | ✅ **shipped** | `EventPlaybackController.cs:64`, `:139` |
| One-voice-at-a-time (§Cross-1) | ✅ **shipped, server-side** | `EventPlaybackService.cs:237-244` replacement arm |
| Server-owned state, re-attach, broadcast (§Cross-4) | ✅ **shipped** | `PHN-1e` |
| `ConsolePlaybackState` + `Changed` fan-out | ✅ **shipped** | `PHN-2` |
| ⭐ Topbar chip renders `Speech` as **`Message`** | ✅ **shipped and already B-aware** | `ConsolePlaybackState.cs:73-78` |
| `.spinner`, `.transport-btn-*`, `.visually-hidden` CSS gaps | ✅ **fixed** | `PHN-2` G-1/G-2 |
| Transport component pattern (`Mine`, `_starting`, dispose) | ✅ **shipped, to be copied** | `VoicemailPlayer.razor` |
| `EventPlaybackApiService.StartSpeechAsync` | 🆕 **new** — one method | Task 3 |
| `GvSpeechText.ForMessage` — the eight content rules | 🆕 **new, and the bulk of the row** | Task 2 |
| `.msg-row-inbound` / `.msg-speak-btn` / `.msg-bubble.speaking` CSS | 🆕 **new** — specified verbatim by the handoff | Task 6 |
| The button + its state machine in `MessageBubble` | 🆕 **new** | Task 4 |
| Chip copy for the Speech arm | 🆕 **4 lines** | Task 7 |

⭐ **`EventPlaybackApiService.cs:110-111` names this row in its own doc comment:** *"⚠ **Voicemail
only. The Speech arm has no caller until `PHN-3`**, and this file's own history is the argument for
not adding one before it does."* Task 3 is the sibling that comment is waiting for.

⭐ **And Feature B does NOT need `GvMedia:Enabled`.** `EventPlaybackService.cs:222`'s 409 gate is the
`RemoteMedia` arm only, so a Speech playback runs with the flag off — unlike Feature A, which is
still dark on a stock box. ⚠ **Re-verify that line before relying on it for UAT**, because it is what
makes this row testable end-to-end without a config change, and that is a large claim to inherit.

### 0.6 The estimate

**1.5–2 d.** The punch list's 2–3 d was quoted when *"nearly all its cost is `PHN-1`"* — and `PHN-1`
is eight merged PRs behind us. What remains:

- **`GvSpeechText.ForMessage` + its tests: ~0.75 d.** Eight ordered rules, one of which (emoji) has
  no tidy .NET primitive and one of which (the MMS prefix) has a **hazard that can eat a verification
  code** (`C-109`). This is the part to slow down on.
- **`MessageBubble` + CSS + the panel wiring: ~0.5 d.** The component pattern is a copy of
  `VoicemailPlayer`'s and the CSS is specified verbatim by the handoff.
- **`StartSpeechAsync`, chip copy, docs: ~0.25 d.**
- **UAT: ~0.25 d**, and it needs the box (§4.5).

### 0.7 ⚠ Constraints — numbering continues from `C-104` (`PHN-5`)

`C-105`–`C-108` are in §0.3–§0.4 above because they change what gets built.

---

**`C-109` — ⚠⚠ THE MMS PREFIX STRIP CAN EAT A VERIFICATION CODE, WHICH IS THE ONE THING THE HANDOFF
SAYS THIS FEATURE MUST NEVER DO.**

Handoff §B3 rule 3 (`:390`, UAT G-8) asks for the MMS sender prefix to be dropped: *"if the body
begins with an E.164-shaped token followed by ` - `, drop the token and the separator."* Rule 5
(`:394`) says the opposite about digits generally: *"**Keep digit runs verbatim** — verification codes
are the single most valuable thing this feature reads. Do not summarise, truncate, or skip numbers."*

A loose implementation of rule 3 — `^\d+\s+` — destroys rule 5's headline example:

```
"77971 is your Facebook confirmation code"   →   "is your Facebook confirmation code"
```

⛔ **The ` - ` separator is the entire guard and it is not optional.** The pattern must require a
literal space-hyphen-space after the digit run, and must not match a digit run followed by anything
else. `ForMessage_DoesNotStripALeadingVerificationCode` (§4.1) pins it and is the most important
single test in this row.

---

**`C-110` — the single-selection group (§A4b) is enforced by the SERVER, and the client needs no
bookkeeping for it. Do not build any.**

Handoff `:248-256`: *"Every play button on `/phone` — every voicemail row and every inbound message
bubble — belongs to one single-selection group. They are not independent toggles."*

That is already true and free. There is **one** attended playback by construction
(`EventPlaybackService.cs:237-244` tears down whatever is in the slot), and every component gates its
rendering on `Mine` — *"is the ambient snapshot's `Id` the one I started?"*
(`VoicemailPlayer.razor:178-181`). When bubble B starts, the ambient snapshot's `Id` becomes B's, so
A's `Mine` answers `null` and A returns to rest **silently**, which is precisely §B4's `Replaced`
row.

⛔ **Do not add a shared "currently speaking bubble id" to `ConsolePlaybackState` or to the panel.**
It would be a second source of truth for something the server already owns, and it is the exact shape
`PHN-1e` replaced.

---

**`C-111` — subscribe to `ConsolePlaybackState.Changed`, NEVER to `AudioStateStore.EventPlaybackChanged`.**

`ConsolePlaybackState.cs:80-101` fans out over `GetInvocationList()` with a per-handler `try/catch`,
deliberately unlike `PhoneUnreadState`'s plain multicast invoke. `PHN-2` §0.6 is the argument: there
is exactly **one** subscriber to the store, for the life of the process, and one throwing handler must
not silence the others. **A conversation with forty messages mounts forty subscribers** — the largest
fan-out this event has ever had — so this is not a style preference. `UI-6` (P2) is the open row about
the multicast defect and this row must not add a consumer on the wrong side of it.

⚠ **And every bubble must unsubscribe.** `@implements IDisposable`, `Changed -= …`. Forty leaked
handlers per opened thread, on a kiosk that runs for weeks, is a real leak.

---

**`C-112` — the shipped chip copy cannot say "Reading", and this plan changes `MainLayout` rather
than deviating from the handoff.**

`MainLayout.razor:1389-1395` builds one kind-agnostic string:

```csharp
private string ConsolePlaybackTitle => ConsolePlayback.Snapshot?.Label is { Length: > 0 } label
  ? $"Playing {label} on the console. Tap to stop." : "Playing on the console. Tap to stop.";
```

Handoff §Cross-3 (`:145`, `:147`) specifies `Reading a message from {sender} on the console. Tap to
stop.` / `Stop reading message from {sender}`.

Two options were considered. **Choosing a `Label` of `"a message from Jane"` and accepting *"Playing
a message from Jane…"* was rejected**: it is a copy deviation from an approved handoff bought for
four lines of saved work, which is a bad trade. Task 7 makes the two format strings kind-aware
instead. ⚠ **That touches a Feature A surface**, so its existing test must still pass and the
`RemoteMedia` arm must be byte-identical.

⚠ **The chip's VISIBLE label is `KindLabel`, not `Label`** (`MainLayout.razor:142`, `:149`), and
`ConsolePlaybackState.cs:73-78` already maps `"Speech" => "Message"` — matching §Cross-3's *"the
kind, not the sender"*. **Do not touch it**; the queue records a `PHN-2` review defect about exactly
that label being guarded by a test.

---

**`C-113` — `GV-10` may be feeding the bubbles snippets rather than full bodies, and if so this
feature reads an ellipsis aloud. It is NOT a blocker and must NOT be worked around.**

`GV-10` (queue `:112`) records UAT **F-5**: a bubble ending in a literal `...`, plausibly because
RotaryPhone derives per-thread messages by filtering the SMS folder list, whose entries carry
**snippets**. `D31` raised `GV-10` to the highest priority within the GV set *precisely because
reading is now the whole feature*.

Handoff §B3 rule 8 (`:402`) already calls this *"Dependency noted, not owned here."* **This row reads
`SmsMessageDto.Text`, which is the correct field.** If that field holds a snippet, the defect is
`GV-10`'s and fixing it fixes Feature B for free.

⛔ **Do not strip a trailing ellipsis to make the demo nicer.** That masks `GV-10` — it would make a
truncated body indistinguishable from a complete one, in the one feature where the difference is
audible. **Ordering preference, not a dependency:** `GV-10` before `PHN-3` gives a better UAT; either
order ships.

---

**`C-114` — read `Message.Text`, never `DisplayText`.**

`MessageBubble.razor:49-50`: `DisplayText => string.IsNullOrEmpty(Message.Text) ? "(no text)" : Message.Text!`.
A speak helper fed `DisplayText` would say the literal words *"no text"* aloud. The button must be
**absent** on a bubble with no text, not present and speaking a placeholder (Task 4).

---

**`C-115` — `PHN-4` edits the same 442-line file. Sequential, not concurrent.**

`PhoneTextsPanel.razor` is touched by both rows, and the overlap of *regions* is near-zero:

| Region | `PHN-3` | `PHN-4` |
|---|---|---|
| `.msg-list` list branch, the `<MessageBubble …>` call — `:82-98` | ✅ **its only edit here** | ❌ |
| `ComposeBar()` `:259-279` + call sites `:101`, `:138` | ❌ | ✅ deleted |
| New-recipient mode `:104-140`; draft path; `New message` `:175-178` | ❌ | ✅ deleted |

**So they do not fight over content — they fight over line numbers.** `PHN-4`'s own queue cell warns
its numbers need re-verifying before editing; the same applies here in reverse. ⚠ **Claim them in
either order, never concurrently** — the same instruction the queue already gives for `GV-9` vs
`PHN-4`. Neither is a dependency of the other.

⚠ **And `design-system.css:5842-5859` is LIVE** — `.msg-list`, `.msg-bubble`, `.skeleton-feed-chip`.
`PHN-4`'s brief once cited it as dead. This row **adds** rules near it and must delete none.

### 0.8 What this row is NOT

1. ⛔ **Not a reply path.** §0.2. No send, no composer, no `GvBridgeSendService`.
2. ⛔ **Not the optional hairline progress bar.** Handoff `:311` makes it conditional on Q2 and
   recommends skipping otherwise. **Skipped** — §1.3 gives the reason, and it is what keeps this row
   free of any clock.
3. ⛔ **Not a change to `NowPlayingDock`.** Handoff §Cross-2 (`:99-105`): *"This is an instruction,
   not an omission."*
4. ⛔ **Not seek, pause or restart.** `TTSEventSource` is `IsSeekable => false` (ADR §8.3) and the
   handoff removes all three by name.
5. ⛔ **Not a speak button on outbound bubbles.** Handoff `:323`. Inbound only, which leaves the
   entire outbound render path untouched.
6. ⛔ **Not `GV-10`.** `C-113`.
7. ⛔ **Not a fix for `UI-6`.** `C-111` says which side of it to stand on; the row itself is P2 and
   someone else's.
8. ⛔ **Not an ADR edit.** ADR-029 is merged and correct (`C-105`). The **handoff** is what Task 10
   corrects.

---

## 1. Three decisions this plan settles

### 1.1 Where the content rules live: a pure static helper in `Radio.Web`

ADR-029 §4.2 (`:222`) and the handoff's component table (`:703`) both say the composition happens in
`Radio.Web` and names it `GvSpeechText.ForMessage`. **Verified absent:**
`grep -rn "GvSpeechText|ForMessage|StartSpeechAsync" src/` returns nothing.

`Radio.API` speaks a finished string; it must not learn about MMS prefixes or emoji. The in-tree
precedent for a pure static text helper in the Web models is `GvCounterparty` and `GvDirection` in
`ApiModels.cs`, and this follows it — **a static class with no services, no state and no I/O**, which
is what makes the eight rules unit-testable without a browser.

### 1.2 `Waiting` renders as `Preparing`

`C-107`. One spinner, two `title` strings. No new visual, no sub-line, no table row that the gutter
cannot hold.

### 1.3 No progress bar, and therefore no clock

The handoff makes the 3px hairline conditional (`:311`, Q2 at `:760`) and recommends skipping it.
**Skipped**, and the reason is worth more than the pixels: `VoicemailPlayer.razor:196-205` records
that it deliberately has **no timer** — its position moves because `MainLayout`'s 1 Hz tick
re-renders through `@Body` — and that *"⛔ Do NOT answer that by adding a timer here… every expanded
row would carry one."* A conversation renders **forty bubbles**. A per-bubble clock is forty timers
on an N100 where churn is audible. Skipping the bar means this row introduces no clock at all.

⚠ It is also the honest answer: a synthesized utterance has **no duration known before it starts**,
which is the same reason the handoff drops the `0:14 / 0:42` readout (`:302-307`).

---

## 2. Tasks

### Task 1 — `EventPlaybackApiService.StartSpeechAsync`

**File:** `src/Radio.Web/Services/ApiClients/EventPlaybackApiService.cs`

Mirror `StartVoicemailAsync` (`:113-152`) exactly. ⚠ **`:21-27` is a standing instruction on this
file:** *"Every one of the four now builds its path INSIDE the try that catches. **Do not hoist a
path expression back into an argument list.**"*

```csharp
  /// <summary>
  /// Starts a Speech (TTS) attended playback and returns its first snapshot, or a rejection reason.
  /// </summary>
  /// <remarks>
  /// ⚠ The sibling of <see cref="StartVoicemailAsync"/>, and the caller PHN-3 was named for at
  /// that method's own doc comment. The two arms are mutually exclusive by validation
  /// (EventPlaybackRequest.Validate rejects ArmMismatch if a Speech request carries MediaKind,
  /// MediaId or DurationSeconds), so this body sends NEITHER — an anonymous object with a Speech
  /// kind, the text and a label, and nothing else.
  ///
  /// ⚠ Radio.Web has no copy of EventPlaybackRequestDto and must not grow one. The body is
  /// anonymous for the same reason StartVoicemailAsync's is.
  ///
  /// ⚠ TEXT MUST ALREADY BE CAPPED by the caller. The server REJECTS over-length text as
  /// TextTooLong rather than truncating it (GvMediaOptions.cs:79-88), so GvSpeechText.ForMessage
  /// does the capping and this method does not second-guess it. The TextTooLong branch in the
  /// caller's failure mapping is a backstop for a server configured below the client's cap, not
  /// the normal path. See plan PHN-3 C-106.
  ///
  /// ⚠ No engine and no voice are sent. ADR-029 §9 Amendment: message speech uses the CURRENTLY
  /// SELECTED engine (TTSOptions.DefaultEngine, deployed "Google"), resolved server-side. Sending
  /// one here would reintroduce the per-feature engine override the amendment deleted.
  /// </remarks>
  public async Task<(EventPlaybackSnapshotDto? Snapshot, string? Reason)> StartSpeechAsync(
    string text, string? label, CancellationToken ct = default)
  {
    try
    {
      var path = "api/audio/events";
      var response = await _http.PostAsJsonAsync(
        path,
        new
        {
          kind = "Speech",
          text,
          label
        },
        ct);

      if (!response.IsSuccessStatusCode)
      {
        return (null, await ReadReasonAsync(response, ct));
      }

      var snapshot = await response.Content.ReadFromJsonAsync<EventPlaybackSnapshotDto>(ct);
      return (snapshot, null);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to start speech playback");
      return (null, "Unreachable");
    }
  }
```

⚠ **Match the surrounding file's exact idioms rather than this sketch where they differ** — the
`HttpClient` field name, the `ReadReasonAsync` signature (`:203-221`), the catch shape and the
logging convention are all established at `StartVoicemailAsync` and must be copied from it.

⛔ **Never log `text`.** It is an SMS body. `EventPlaybackService.cs:720-721` carries the same
instruction server-side, and `TTS-11` is the row that established it.

---

### Task 2 — `GvSpeechText`, the eight content rules

**New file:** `src/Radio.Web/Models/GvSpeechText.cs` (beside `ApiModels.cs`, matching
`GvCounterparty` / `GvDirection`).

⚠ **Rule order is load-bearing** and is not the handoff's presentation order. Strip the prefix
**first** (it is anchored to the start), transform the body, then prepend the lead-in, then cap the
**whole** result — because the server counts the whole `Text`, lead-in included.

```csharp
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Radio.Web.Models;

/// <summary>
/// Turns an SMS body into the utterance the console speaks (handoff §B3, feature B).
/// </summary>
/// <remarks>
/// Pure, static, no services and no I/O — deliberately, so all eight rules are unit-testable
/// without a browser. ADR-029 §4.2: composition happens in Radio.Web; Radio.API speaks a finished
/// string and must not learn about MMS prefixes or emoji.
///
/// ⚠ THE ORDER OF THE RULES IS PART OF THE SPEC. The prefix strip is anchored to the start of the
/// RAW body, so it runs before anything else can shift it; the cap runs LAST and over the whole
/// string including the lead-in, because GvMedia:MaxSpeechChars is measured on the text the server
/// receives and it REJECTS rather than truncates (plan PHN-3 C-106).
/// </remarks>
public static class GvSpeechText
{
  /// <summary>
  /// Mirrors <c>GvMediaOptions.MaxSpeechChars</c>'s shipped default.
  /// </summary>
  /// <remarks>
  /// ⚠ Duplicated deliberately rather than fetched: Radio.Web has no route that exposes the
  /// server's value, and one round-trip per bubble to learn a constant would be worse than this
  /// coupling. GvSpeechTextTests pins the two together so the pair cannot drift silently. If a box
  /// configures the server BELOW this number, the caller's TextTooLong branch is what catches it.
  /// </remarks>
  public const int MaxChars = 1000;

  /// <summary>Rule 3 — the MMS sender prefix, e.g. <c>+15551234567 - Body…</c>.</summary>
  /// <remarks>
  /// ⚠⚠ THE " - " SEPARATOR IS THE ENTIRE GUARD AND IS NOT OPTIONAL. Rule 5 says digit runs are
  /// kept verbatim because "verification codes are the single most valuable thing this feature
  /// reads", and the handoff's own example body BEGINS with one: "77971 is your Facebook
  /// confirmation code". A looser pattern — ^\d+\s+ — would eat it. Requiring a literal
  /// space-hyphen-space after an E.164-shaped run is what separates a sender prefix from a code.
  /// Pinned by ForMessage_DoesNotStripALeadingVerificationCode. See plan PHN-3 C-109.
  /// </remarks>
  private static readonly Regex MmsSenderPrefix = new(
    @"^\+?\d{7,15} - ", RegexOptions.Compiled | RegexOptions.CultureInvariant);

  /// <summary>Rule 4 — any URL becomes the words "a link".</summary>
  private static readonly Regex Url = new(
    @"\b(?:https?://|www\.)\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

  private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

  /// <summary>
  /// The utterance for one inbound message, or null when there is nothing to speak.
  /// </summary>
  /// <param name="body">The message body — <c>SmsMessageDto.Text</c>, never <c>DisplayText</c>.</param>
  /// <param name="senderName">
  /// A RESOLVED contact name, or null. ⚠ Never a phone number: handoff :386 says "Do not read the
  /// identifier aloud", and the panel's ResolveThreadName falls back to one — so the caller passes
  /// null rather than that fallback. See plan PHN-3 C-108.
  /// </param>
  public static string? ForMessage(string? body, string? senderName)
  {
    if (string.IsNullOrWhiteSpace(body))
    {
      return null;
    }

    // Rule 3 — strip the MMS sender prefix, before anything can move it off the start.
    var text = MmsSenderPrefix.Replace(body, string.Empty);

    // Rule 4 — a URL is unspeakable; the words are more useful than the characters.
    text = Url.Replace(text, "a link");

    // Rule 6 — emoji go. "❤️Love you too! ❤️" must not become "red heart Love you too".
    text = StripEmoji(text);

    // Tidy up after 3/4/6, which can leave doubled or leading spaces. Rule 2 needs no code: the
    // timestamp is never part of the body and is simply never added.
    text = Whitespace.Replace(text, " ").Trim();

    if (text.Length == 0)
    {
      // A body that was ONLY emoji, or only a stripped prefix. Nothing to say.
      return null;
    }

    // Rule 1 — the lead-in, and only when a name actually resolved.
    if (!string.IsNullOrWhiteSpace(senderName))
    {
      text = string.Concat("Message from ", senderName.Trim(), ". ", text);
    }

    // Rule 7 — the safety valve, LAST and over the whole string. No UI indication, per handoff
    // :398: the Stop button is the real control for a long read.
    return text.Length <= MaxChars ? text : text[..MaxChars];
  }

  /// <summary>
  /// Removes emoji, pictographs, variation selectors and zero-width joiners.
  /// </summary>
  /// <remarks>
  /// ⚠ AN APPROXIMATION BY CODE-POINT RANGE, not a Unicode-property query, and it is stated as one
  /// rather than implied. .NET has no built-in Emoji_Presentation predicate, and the ranges below
  /// are the ones that carry emoji in practice. Two known consequences, both accepted:
  /// a legitimate arrow or a dingbat in a message body is dropped, and a novel emoji outside these
  /// ranges survives. Both are strictly better than speaking "red heart".
  ///
  /// ⚠ Enumerates RUNES, not chars. Most emoji are surrogate pairs, and a char-wise filter would
  /// leave half of one behind — which renders as a replacement character and may well be SPOKEN.
  /// </remarks>
  private static string StripEmoji(string text)
  {
    var sb = new StringBuilder(text.Length);

    foreach (var rune in text.EnumerateRunes())
    {
      if (IsEmojiLike(rune.Value))
      {
        continue;
      }

      sb.Append(rune.ToString());
    }

    return sb.ToString();
  }

  private static bool IsEmojiLike(int v) =>
    v is 0x200D or 0xFE0E or 0xFE0F        // ZWJ and the two variation selectors
    || (v >= 0x2600 && v <= 0x27BF)        // misc symbols + dingbats
    || (v >= 0x2B00 && v <= 0x2BFF)        // misc symbols and arrows
    || (v >= 0x1F000 && v <= 0x1FAFF);     // emoticons, pictographs, flags, supplemental
}
```

⚠ **Rule 5 needs no code and that is the point.** *"Keep digit runs verbatim"* is a constraint **on
the other rules**, not a transformation. It is enforced by `C-109`'s separator guard and by three
tests, not by a line in this file.

---

### Task 3 — `MessageBubble` gains the button

**File:** `src/Radio.Web/Components/Pages/MessageBubble.razor` (56 lines today; **no `.razor.css`
sidecar** — the styles go in `design-system.css`, Task 6).

**Verified starting point:** the file has no wrapper element, no gutter, no button, no injected
service and no `IDisposable`. `.msg-bubble` is the outermost element and the only branch is
`@if (IsOutbound)`.

Wrap the **inbound** path only, exactly as the handoff specifies (`:316-329`):

```razor
@using Radio.Web.Models
@using Radio.Web.Services
@using Radio.Web.Services.ApiClients
@implements IDisposable
@inject ConsolePlaybackState ConsolePlayback
@inject EventPlaybackApiService EventPlayback

@if (CanSpeak)
{
  <div class="msg-row-inbound">
    @BubbleMarkup
    <button type="button"
            class="msg-speak-btn @(IsMineAndLive ? "speaking" : null)"
            title="@SpeakTitle"
            aria-label="@SpeakAriaLabel"
            @onclick="ToggleSpeakAsync">
      @if (IsMinePreparing)
      {
        <span class="spinner"></span>
      }
      else
      {
        <RadzenIcon Icon="@(IsMineAndLive ? "stop" : "play_arrow")" />
      }
    </button>
  </div>
}
else
{
  @BubbleMarkup
}
```

with the existing markup extracted verbatim into a `RenderFragment` so the outbound path is
**byte-identical** to today:

```razor
@code {
  /// <summary>
  /// The bubble exactly as it rendered before PHN-3. ⚠ Extracted UNCHANGED — the outbound render
  /// path must not move, per handoff :323 ("leaves the entire outbound render path untouched").
  /// The only edit inside it is the `speaking` class, which can only ever be true on an inbound
  /// bubble because CanSpeak gates the wrapper.
  /// </summary>
  private RenderFragment BubbleMarkup => @<div class="msg-bubble @DirectionClass @StatusClass @(IsMineAndLive ? "speaking" : null)"
       @onclick="OnFailedClick" role="@(IsFailed ? "button" : null)">
    @* …the existing :9-30 content, unchanged… *@
  </div>;
}
```

The state machine, copied from `VoicemailPlayer.razor`:

```csharp
  /// <summary>A resolved contact name for the thread, or null. Never a phone number (C-108).</summary>
  [Parameter] public string? SenderName { get; set; }

  private string? _playbackId;
  private bool _starting;
  private string? _startReason;

  /// <summary>
  /// ⚠ Inbound only, and only when there is something to say. C-114: this reads Message.Text, not
  /// DisplayText — a null-text bubble renders "(no text)" and must not offer to speak those words.
  /// </summary>
  private bool CanSpeak => !IsOutbound && Utterance is not null;

  private string? Utterance => GvSpeechText.ForMessage(Message.Text, SenderName);

  // ⚠ Mine, not the ambient snapshot. VoicemailPlayer.razor:178-181 is the original; the reason is
  // that every state this button renders must first be gated on the playback THIS bubble started.
  // It is also what gives §A4b's single-selection group for free: when another item starts, the
  // ambient Id changes, Mine goes null here, and this button returns to rest silently — which is
  // §B4's "Replaced" row, with no client bookkeeping at all. See plan PHN-3 C-110.
  private EventPlaybackSnapshotDto? Mine =>
    _playbackId is not null && ConsolePlayback.Snapshot?.Id == _playbackId
      ? ConsolePlayback.Snapshot : null;

  private bool IsMineAndLive => Mine?.IsLive == true;

  // ⚠ Waiting renders exactly as Preparing — one spinner, two titles. PHN-1f's D28 queue means a
  // Speech playback can legitimately sit in Waiting for up to GvMedia:MaxQueuedWaitSeconds while
  // an announcement finishes, and the handoff's §B4 table predates that state. See C-107.
  private bool IsMinePreparing => Mine?.State is "Preparing" or "Waiting";

  private string SpeakTitle => Mine?.State switch
  {
    "Waiting" => "Waiting for the announcement to finish…",
    "Preparing" => "Preparing…",
    not null when IsMineAndLive => "Stop reading this message.",
    _ => "Read this message aloud."
  };

  private string SpeakAriaLabel =>
    IsMineAndLive ? "Stop reading this message" : "Read this message aloud";

  protected override void OnInitialized() => ConsolePlayback.Changed += OnPlaybackChangedAsync;

  // ⚠ C-111. A forty-message conversation mounts forty subscribers; every one must come off.
  public void Dispose() => ConsolePlayback.Changed -= OnPlaybackChangedAsync;

  private Task OnPlaybackChangedAsync() => InvokeAsync(StateHasChanged);

  private async Task ToggleSpeakAsync()
  {
    // ⚠ THE RE-ENTRANCY GUARD IS MANDATORY, and VoicemailPlayer.razor:323-345 explains why in its
    // own words: _playbackId is cleared before the awaited POST, so a second tap arriving while
    // the first is in flight sees a null id and starts a SECOND playback. A double-tap is the
    // default gesture on a wall panel.
    if (_starting)
    {
      return;
    }

    if (IsMineAndLive)
    {
      var id = _playbackId;
      _playbackId = null;
      if (id is not null)
      {
        await EventPlayback.StopAsync(id);
      }

      return;
    }

    _starting = true;
    _startReason = null;
    try
    {
      _playbackId = null;
      var (snapshot, reason) = await EventPlayback.StartSpeechAsync(Utterance!, SpeakLabel);
      if (snapshot is null)
      {
        _startReason = reason;
        await OnSpeakFailed.InvokeAsync(reason);
        return;
      }

      // ⚠ 202 means ACCEPTED, not playing (EventPlaybackController.cs:17-22). Do not render
      // Playing here — the state arrives on the next broadcast.
      _playbackId = snapshot.Id;
    }
    finally
    {
      _starting = false;
    }
  }

  /// <summary>
  /// The chip's title text, via EventPlaybackSnapshot.Label. ⚠ Shaped so MainLayout's Speech arm
  /// reads "Reading a message from Jane on the console." — see Task 7 and plan PHN-3 C-112.
  /// EventPlaybackRequest.MaxLabelChars is 128; a resolved contact name cannot approach it, but
  /// the server rejects LabelTooLong rather than truncating, so it is clamped here.
  /// </summary>
  private string SpeakLabel
  {
    get
    {
      var label = string.IsNullOrWhiteSpace(SenderName)
        ? "a message"
        : $"a message from {SenderName.Trim()}";

      return label.Length <= 128 ? label : label[..128];
    }
  }
```

⚠ **`OnSpeakFailed` is an `EventCallback<string?>` parameter**, not a toast raised here. Handoff
§Cross-5 (`:174`) puts the failure in a **toast** — *"there is no room beside a bubble"* — and a
presentational bubble should not own a toast host. The panel raises it (Task 5).

⛔ **The button sits OUTSIDE `.msg-bubble`, in the wrapper.** That is the handoff's placement rule
(`:325`) and it also sidesteps a live hazard: `MessageBubble.razor:9` binds `@onclick="OnFailedClick"`
on the bubble root **unconditionally**. A button nested inside it would bubble its click into that
handler and would need `@onclick:stopPropagation="true"` (the in-repo precedent is
`CastDeviceDropdown.razor`, per `CLAUDE.md` § *UI/UX Patterns*). **Placing it outside means the
question does not arise** — do not "simplify" by moving it in.

---

### Task 4 — `PhoneTextsPanel` passes the name down and owns the toast

**File:** `src/Radio.Web/Components/Pages/PhoneTextsPanel.razor`

⚠ **Re-verify every line number before editing** — the file is 442 lines and `PHN-4` and `GV-9` are
both queued against it (`C-115`).

The **only** markup edit is the `<MessageBubble>` invocation in the list branch (`:94-96` at
`6c220461`):

```razor
    <MessageBubble Message="captured"
                   Status="StatusFor(captured)"
                   SenderName="@SpeakableSenderName"
                   OnSpeakFailed="OnSpeakFailedAsync"
                   OnRetry="@(() => RetrySend(captured))" />
```

and one computed property plus the toast handler:

```csharp
  /// <summary>
  /// The resolved contact name for this thread, or null when only an identifier is known.
  /// </summary>
  /// <remarks>
  /// ⚠ NULL RATHER THAN THE NUMBER, and that is the whole reason this is not just HeaderName.
  /// ResolveThreadName falls back to the raw CounterpartyNumber, and handoff :386 says "Do not read
  /// the identifier aloud" — 14 of the 20 live threads have no resolvable name, so this is the
  /// COMMON case, not an edge. A number leaking through here would be read digit by digit to the
  /// room. See plan PHN-3 C-108.
  /// </remarks>
  private string? SpeakableSenderName
  {
    get
    {
      var name = ResolveThreadName();
      return string.IsNullOrWhiteSpace(name) || name == HeaderNumber ? null : name;
    }
  }
```

⚠ **`HeaderNumber` is a placeholder for whatever this file actually calls the raw identifier** —
read `ResolveThreadName` (`:413-430`) and the header parameters (`:229`) and compare against the real
member. The rule is *"if the resolved name is just the identifier, pass null"*; the spelling is the
file's.

The toast handler uses the panel's existing notification mechanism — **read how `RetrySend`'s failure
path surfaces its message and copy it**, rather than introducing a second toast route:

```csharp
  /// <summary>Handoff §Cross-5 :174 — a synthesis failure is a TOAST, not an inline state.</summary>
  private Task OnSpeakFailedAsync(string? reason) => ShowErrorToast(
    "Couldn't read that message.",
    reason switch
    {
      "TextTooLong" => "That message is too long for the console to read.",
      "Unreachable" => "The console isn't responding. Try again.",
      _ => "The console couldn't read this one. Try again."
    });
```

The first two strings are copy the handoff does not specify. `TextTooLong` should be unreachable
(Task 2 caps at the same number) and is a backstop for a server configured below the client's cap
(`C-106`). ⚠ **Both are new copy and §8 flags them for the Designer.**

---

### Task 5 — the CSS, verbatim from the handoff

**File:** `src/Radio.Web/wwwroot/css/design-system.css`

Add the handoff's §Ph block (`:331-371`) **unchanged** — it is quoted in full in the handoff and
every token it uses was verified to exist: `--surface-separator` (`:68`), `--accent-primary` (`:72`),
`--accent-dim` (`:76`), `--text-low` (`:96`), `--touch-compact` 44px (`:130`), `--sp-2` (`:134`).

⛔ **No `:root` changes.** Handoff `:25`, `:707`. Zero new custom properties are required.

Place it **after** the existing `.msg-*` rules (which end at `design-system.css:5914`), not inside
them.

⚠ **Three things about that block worth not re-deriving:**

1. `max-width: calc(72% + var(--touch-compact) + var(--sp-2))` on the row **preserves the bubble at
   exactly its current 72%** (`design-system.css:5853`) by widening the row by precisely the button
   and its gap. Changing either term silently reflows every conversation.
2. `.msg-speak-btn:focus-visible` uses an **outset** ring (`box-shadow: 0 0 0 2px`), where the
   transport buttons use `inset`. That is deliberate — this button sits in empty gutter space, so an
   inset ring would be invisible against the background.
3. `--touch-compact` (44px) rather than `--touch-min` (48px), matching the established chip spine
   (`.vm-chip`, `.feed-chip`) and the global `button { min-height: 44px }` floor at `:1292`.

⚠ **`.msg-bubble.speaking` sets `border-color` only**, so it composes with `.msg-bubble.inbound`'s
own `1px solid var(--surface-separator)` rather than replacing it. And it cannot collide with
`.failed`: `StatusFor` reads `_statusById`, populated only on the outbound optimistic-send path, so
an inbound bubble's status is always `None`.

---

### Task 6 — the chip says "Reading" for a Speech playback

**File:** `src/Radio.Web/Components/Layout/MainLayout.razor:1389-1395`

`C-112`. Make both strings kind-aware; the `RemoteMedia` arm must be **byte-identical** to today.

```csharp
  // ⚠ The RemoteMedia arm is UNCHANGED, byte for byte — PHN-2 shipped it and it has a test.
  // The Speech arm is new (PHN-3) and exists because handoff §Cross-3 :145/:147 specifies
  // "Reading a message from {sender} on the console." and one kind-agnostic "Playing {label}"
  // string cannot produce it. The sender arrives inside Label, shaped by MessageBubble.SpeakLabel
  // as "a message from Jane" so this reads as a sentence.
  private string ConsolePlaybackTitle => ConsolePlayback.Snapshot switch
  {
    { Kind: "Speech", Label: { Length: > 0 } label } =>
      $"Reading {label} on the console. Tap to stop.",
    { Kind: "Speech" } => "Reading a message on the console. Tap to stop.",
    { Label: { Length: > 0 } label } => $"Playing {label} on the console. Tap to stop.",
    _ => "Playing on the console. Tap to stop."
  };

  private string ConsolePlaybackAriaLabel => ConsolePlayback.Snapshot switch
  {
    { Kind: "Speech", Label: { Length: > 0 } label } => $"Stop reading {label}",
    { Kind: "Speech" } => "Stop reading message",
    { Label: { Length: > 0 } label } => $"Stop playing {label}",
    _ => "Stop playing on the console"
  };
```

⛔ **Do not touch the chip's visible label** (`:142`, `:149`). It renders `KindLabel`, which
`ConsolePlaybackState.cs:73-78` already maps `"Speech" => "Message"` — §Cross-3's *"the kind, not the
sender"*. The queue records a `PHN-2` review defect about that label; it is guarded by a test.

---

### Task 7 — docs and the stale handoff

1. **`docs/design-handoffs/HANDOFF-phone-console-audio-and-canned-replies.md`** — correct the four
   stale engine claims (`C-105`): `:416`, `:418`, `:742-748` and **Q5 at `:766`**. Q5 is **answered**,
   by the owner, in ADR-029 §9's amendment; mark it closed with the pointer rather than leaving an
   open question whose premise (`espeak-ng`, `GvMedia:SpeechEngine`) no longer exists in the tree.
   Add `Waiting` to the §B4 state table (`C-107`) and note that the 1000-char cap requires client
   truncation (`C-106`).
   ⚠ **Corrections only.** Do not restructure the handoff or re-open a settled question.
2. **`design/INTEGRATIONS.md`** — the phone-integration section gains the speak path: what it calls,
   which engine resolves, and that SMS bodies reach the configured TTS provider. Project memory
   requires this file to be updated when the phone integration changes.
3. **`design/FUTURE-WORK.md`** — record the two items §7 defers.
4. **`docs/BUILDER_QUEUE.md`** — Builder marks the row ✅ at merge.
5. **`docs/HANDOFF-GA-PUNCH-LIST.md`** — flip `PHN-3`'s **`Queued?`** cell from **`No`** to a link to
   the row. ⚠ **The §9 tier counts do NOT move** — see §5.

---

## 3. Ordering

Task 2 (`GvSpeechText`) first: it is the bulk of the row, it is pure, and it can be finished and
fully tested before any markup exists. Then Task 1 (the API method), then Tasks 3–5 (component, panel,
CSS) which are one unit and not usefully separable. Task 6 any time. Task 7 last.

**One PR.** The row is one user-visible affordance and the tests span its helper, its component and
its client method; splitting the helper from the button would ship a tested helper with no caller,
which is the shape `EventPlaybackApiService.cs:110-111` explicitly refused for `StartSpeechAsync`.

---

## 4. Test plan

> ⚠ **The standing warning for this repository:** cycles here have repeatedly found tests that passed
> against a deliberately broken implementation. **Every pin below names the mutation that must make
> it fail, and Builder must run each mutation and record the result in the PR body.**

### 4.1 `T1` — `GvSpeechText`, the row's most valuable tests

**File:** `tests/Radio.Web.Tests/Models/GvSpeechTextTests.cs`

One test per rule, plus the interactions. The handoff's own examples are the fixtures — use them
verbatim so a reviewer can diff the test against `:376-402`.

| Test | Fixture → expected | Mutation that must fail it |
|---|---|---|
| ⭐ `ForMessage_DoesNotStripALeadingVerificationCode` | `"77971 is your Facebook confirmation code"` → **unchanged** | loosen `MmsSenderPrefix` to `^\+?\d{7,15}\s+` |
| `ForMessage_StripsTheMmsSenderPrefix` | `"+15551234567 - Dinner at 7?"` → `"Dinner at 7?"` | delete the `Replace` |
| `ForMessage_LeavesABodyWithNoPrefixAlone` | `"555 - is not a prefix"` (too short) → unchanged | widen the digit floor below 7 |
| `ForMessage_ReplacesUrlsWithTheWordsALink` | `"See https://ex.com/a?b=1 now"` → `"See a link now"` | delete the URL rule |
| `ForMessage_StripsEmojiAndTheSpacesTheyLeave` | `"❤️Love you too! ❤️"` → `"Love you too!"` | delete `StripEmoji`; **and** change `EnumerateRunes` to a `char` loop |
| `ForMessage_AddsTheLeadInOnlyWhenANameResolved` | `(body, "Jane")` → `"Message from Jane. …"`; `(body, null)` → body alone | make the lead-in unconditional |
| `ForMessage_NeverIncludesATimestamp` | any fixture → contains no `":"`-shaped time | — (a guard, not a transformation) |
| `ForMessage_CapsAtMaxChars` | 1500 `'a'` → length exactly 1000 | remove the cap |
| ⭐ `ForMessage_CapsTheWholeStringIncludingTheLeadIn` | 995 chars + `"Jane"` → **≤ 1000** | move the cap before the lead-in |
| `ForMessage_ReturnsNullForNullEmptyOrEmojiOnly` | `null`, `""`, `"  "`, `"❤️"` → `null` | return `""` instead |
| ⭐ `MaxChars_MatchesTheServersDefault` | `GvSpeechText.MaxChars == new GvMediaOptions().MaxSpeechChars` | change either constant |

⚠ **`ForMessage_CapsTheWholeStringIncludingTheLeadIn` is not a formality.** Capping the body and then
prepending a lead-in produces a string over the limit, and the server **rejects** rather than
truncates (`C-106`) — so the bug's symptom is a 400 on exactly the messages that have a resolved
sender, which is the 30% of threads a demo would use.

⚠ **`MaxChars_MatchesTheServersDefault` requires `Radio.Web.Tests` to reference `Radio.Core`.**
Verify that it does before writing it; if it does not, assert against the literal `1000` **and say in
the test's comment that the coupling is unpinned**, rather than adding a project reference for one
assertion.

### 4.2 `T2` — `MessageBubble`

**File:** `tests/Radio.Web.Tests/Components/MessageBubbleTests.cs` (extend if it exists).

bUnit, with `JSInterop.Mode = JSRuntimeMode.Loose` (project memory: required for components using JS
interop) and a fake `EventPlaybackApiService`.

| Test | Mutation |
|---|---|
| `InboundBubble_RendersASpeakButton` | remove the wrapper |
| ⭐ `OutboundBubble_RendersNoSpeakButtonAndNoWrapper` | drop `!IsOutbound` from `CanSpeak` |
| `BubbleWithNullText_RendersNoSpeakButton` (`C-114`) | feed `DisplayText` to `ForMessage` |
| `TappingSpeak_PostsTheComposedUtteranceNotTheRawBody` | pass `Message.Text` straight through |
| ⭐ `ASecondTapWhileTheFirstIsInFlight_StartsOnlyOnePlayback` | delete the `_starting` guard |
| `WhenTheAmbientSnapshotIsAnotherId_TheButtonIsAtRest` (`C-110`) | drop the `Mine` gate, use the ambient snapshot |
| `WaitingRendersTheSpinner_AndSaysWhy` (`C-107`) | remove `"Waiting"` from `IsMinePreparing` |
| `Dispose_UnsubscribesFromConsolePlaybackState` (`C-111`) | delete `Dispose` |

⚠ **`ASecondTapWhileTheFirstIsInFlight…` needs a deliberately-slow fake, not a `Task.Delay` in the
test.** `CLAUDE.md` § *Test Timing*: do not race a wall clock against a wall clock. Gate the fake's
`StartSpeechAsync` on a `TaskCompletionSource` the test controls, tap twice, then release — so the
assertion runs while the component is frozen rather than after a hopeful sleep.

> **What `T2` cannot falsify:** anything about audio. bUnit renders markup and drives callbacks; that
> a real `TTSEventSource` is created, ducks the music and comes out of the cabinet speakers is
> **only** provable by §4.5. Say so in the test file rather than letting a green component suite read
> as a working feature.

### 4.3 `T3` — `StartSpeechAsync`

**File:** `tests/Radio.Web.Tests/Services/EventPlaybackApiServiceTests.cs` (extend).

Stub handler asserting the posted body: `kind == "Speech"`, `text` present, `label` present, and
⭐ **`mediaKind`, `mediaId` and `durationSeconds` all absent** — any one of them present is
`ArmMismatch` and a 400. Plus: a 400 returns `(null, reason)`; a 202 returns the snapshot; a throw
returns `(null, "Unreachable")`.

> **Mutation:** add `mediaId = (string?)null` to the anonymous body. ⚠ **Check whether that actually
> serialises** — if `null` members are omitted by the configured `JsonSerializerOptions` the arm
> check still passes, and the test proves less than it appears to. Report which.

### 4.4 Gates

- `dotnet build --configuration Release` — 0 warnings (warnings are errors in Release).
- `dotnet test --configuration Release` — full suite green. ⛔ **Never pipe to `tail`**
  (`CLAUDE.md`): redirect, echo `$?`, grep the file, read the **per-project** summaries.
  Known-failing on Windows and not regressions: four `SrcVariableResamplerTests`
  (`libsamplerate.so.0`) and `NwsObservationIntegrationTests.RealNwsCall_*`.
- ⚠ **`Radio.Web.Tests` has known flaky `AudioApiService` timeout tests**
  (`_WhenServerNotAvailable`) unrelated to this row (project memory). Do not chase them.
- Every mutation in §4.1–§4.3 run, with results in the PR body.

### 4.5 ⭐ On-box UAT — this row is not done without it

Deploy first (`./deploy/Deploy-ToLinux.ps1`; project memory: **always deploy before testing**), then
work the handoff's own verification items 15–21 (`:806-813`). **This is the only evidence the feature
works**; §4.2 cannot reach audio.

| # | Check |
|---|---|
| 15 | A play button on **every inbound** bubble and **no outbound** bubble. |
| 16 | A short-code thread (`32665`): the button is present and **works**, while the reply slot shows the `PHN-4` pill. **This is `D31`'s proof** (§0.2). |
| 17 | Listen end to end. **Report an opinion on the voice, not a pass/fail** — the handoff asks for judgement here. |
| 18 | Resolved-name thread opens `Message from {Name}.`; an unresolved one starts with the body and **reads no number aloud** (`C-108`). |
| 19 | MMS prefix stripped; a URL says *"a link"*; an emoji body does not say *"red heart"*. |
| 20 | ⭐ A digit run is spoken **verbatim and intelligibly at kiosk distance** (`C-109`). Use `77971 is your Facebook confirmation code`. |
| 21 | The speaking bubble carries the cyan border; **only one bubble is ever marked, anywhere on the surface**; starting a voicemail mid-speech returns the button to rest **silently** (`C-110`). |
| + | Tap the same button twice fast — one playback, not two (`_starting`). |
| + | Start a speak while an announcement is sounding → spinner + *"Waiting for the announcement to finish…"*, then it speaks (`C-107`). |
| + | Navigate away mid-speech and back → the button re-attaches to the live playback (§Cross-4). |
| + | The topbar chip reads **`Message`**, and its tooltip reads *"Reading a message from…"* (`C-112`). |

⚠ **Kiosk cache.** `OPS-5` set `Cache-Control: no-cache`, so a CSS change should land without
ceremony — but if the new bubble styles do not appear, that is the first thing to check, not the last.

⚠ **Screen is 1920×720.** Verify the 44px targets and that a six-line marketing SMS keeps the button
**top-aligned** (handoff `:329`), not floating at the vertical centre.

---

## 5. Punch-list bookkeeping — and why the counts do not move

`docs/HANDOFF-GA-PUNCH-LIST.md` §9 is explicit that counts are corrected **visibly**, so the
no-change is stated rather than left to inference.

- **`PHN-3`'s `Queued?` cell (`:1066`) changes `No` → a link to the new row.** That is the one cell
  this pass falsifies, and it is corrected in the same pass.
- ⛔ **The §9 P1 count does NOT change.** It reads *"38 listed, 34 open"*, and `PHN-3` is **already in
  that list** (`:1346`). Queueing a row is not shipping it: `listed` is unchanged because the item was
  always listed, and `open` is unchanged because it is still open. The count moves when the row
  merges, and it is **Builder's** to move then.
- ⛔ **`PHN-5` touches no count at all** — it has no punch-list row (punch list `:1441` records that
  it *"was minted past"* the `PHN-1…PHN-4` mapping and lives only in the queue).

---

## 6. Self-review

### 6.1 What was verified first-hand at `6c220461`

- The handoff read in full (826 lines): §B1–§B5, §Cross-1–5, §A4b, the component/file impact table,
  and verification items 15–21. Every CSS custom property the §Ph block uses was checked to exist in
  `design-system.css`.
- **`MessageBubble.razor` in full (56 lines)** — ✅ **the claim that it has no speak-button
  placeholder is CONFIRMED**, and stronger than filed: no wrapper, no gutter, no injected service, no
  `IDisposable`, and `@onclick` bound unconditionally on the bubble root.
- `grep msg-row-inbound|msg-speak-btn|msg-bubble\.speaking` over `design-system.css` → **zero
  matches**. `.msg-bubble`'s `max-width: 72%` at `:5853` — the number the handoff's `calc()`
  preserves.
- `grep -rn "GvSpeechText|ForMessage|StartSpeechAsync" src/` → **zero matches.** Both new artifacts
  confirmed absent.
- `IEventPlaybackService.cs` in full: every member, all four enums, the request record and its
  `Validate`. `EventPlaybackController.cs` in full: six routes, their DTOs and their status codes.
- `EventPlaybackService.AcquireSpeechAsync` (`:698-747`) — the engine resolution `C-105` turns on, and
  the `WaitForClearAirAsync` call at `:493` that `C-107` turns on.
- ADR-029 §9's amendment (`:491-511`, `:602`) — the reversal, in the owner's own quoted words.
- `GvMediaOptions.cs:79-88` — *"the behaviour is REJECTION, not truncation"*, verbatim.
- `PHN-2`'s plan in full, and the code it shipped: `ConsolePlaybackState.cs`,
  `EventPlaybackApiService.cs` (including `:110-111`'s named handoff to this row),
  `VoicemailPlayer.razor`'s `Mine` / `_starting` / dispose pattern, `MainLayout.razor:1389-1395`.
- `PhoneTextsPanel.razor`'s list branch and `ResolveThreadName`; `SmsMessageDto` (`:1138-1145`) —
  **no name field**, which is `C-108`.
- The `PHN-4` and `GV-9` queue cells, for `C-115`'s ordering claim.

### 6.2 What could not be verified, and what it costs

1. **Nothing here was built or run.** Every code block is written against read source.
2. **`EventPlaybackService.cs:222`'s 409 gate being `RemoteMedia`-only** was read from a report of the
   code, not from the line itself. §0.5 marks it ⚠ **re-verify** because the whole UAT plan assumes
   Feature B works with `GvMedia:Enabled` false. **If it is wrong, §4.5 needs a config change first.**
3. **The panel's toast mechanism** (Task 4). `ShowErrorToast` is a placeholder for whatever
   `PhoneTextsPanel` actually uses; the existing failure path of `RetrySend` was not traced. Builder
   copies the real one.
4. **`HeaderNumber`** (Task 4) is likewise a placeholder for the file's real member name.
5. **Whether `Radio.Web.Tests` references `Radio.Core`** (§4.1's last test). Named rather than
   assumed.
6. **The emoji ranges are an approximation** and are documented as one in the code. No test can prove
   "all emoji"; the tests prove the handoff's example and the surrogate-pair property.
7. **Nothing about how the resolved name reaches a thread whose contact resolution is async.**
   `SpeakableSenderName` reads `ResolveThreadName()` at render time; whether that can transiently
   return the number and later the name — producing a lead-in that changes between two taps — was
   **not traced.** It degrades gracefully (worst case: no lead-in) and is worth a UAT glance.

### 6.3 Handoff relationship: `follows`, with three deviations and one correction

| | |
|---|---|
| **follows** | §B1 (button + states), §B2 (gutter placement, inbound-only, always visible, top-aligned), §B3 (all eight content rules), §B5 (never gated by repliability), §Cross-1/2/3/4/5, §A4b, the §Ph CSS **verbatim**, and verification items 15–21. |
| **deviates 1** | **No hairline progress bar.** §1.3 — permitted by `:311` ("recommended only if Q2…") and it keeps the row free of any clock. |
| **deviates 2** | **`Waiting` added to §B4's state table**, rendering as `Preparing`. `C-107` — the handoff predates `PHN-1f`. |
| **deviates 3** | **Two new error strings** (`TextTooLong`, `Unreachable`) beyond §Cross-5's one. Flagged to the Designer in §8. |
| **corrects** | §B4/§B3/Q5's **engine story** — `C-105`. The handoff describes an `espeak-ng` pin the owner reversed and `TTS-9` deleted. Task 7 fixes the document. |

---

## 7. Deliberately not done

- **`GV-10`.** `C-113`. Ordering preference, not a dependency, and working around it would hide it.
- **Gating the button on anything.** §0.2. `D31` strengthens this row; it does not touch it.
- **A per-bubble progress bar or clock.** §1.3.
- **Making `MainLayout`'s chip fully kind-driven**, e.g. a `KindVerb` on `ConsolePlaybackState`.
  Task 6 does the minimum that satisfies §Cross-3. A third kind would be the moment to generalise;
  two arms in a switch is not.
- **Speaking a voicemail transcript.** Not in the handoff, and it would need its own content rules.
- **A `Restart` affordance.** Handoff `:302-307`: *"replaying an eight-word text is just tapping play
  again."*
- **Fixing `UI-6`.** `C-111` stands on the safe side of it; the row is P2 and separate.

---

## 8. ⚠ For the owner and the Designer

**Owner — one thing, and it is a yes/no rather than a design question.** `C-105`: ADR-029 §9's
amendment records the owner choosing the currently-selected TTS engine for message speech, and
records the consequence at `:509` — *"private SMS bodies reach Google's TTS API on each play."* That
was accepted in the abstract, before anything sent one. **`PHN-3` is the row where it starts actually
happening**, on a box in a family home, for every text the console reads. The decision looks settled
and this plan does not re-open it; it is surfaced because a row that first realises a privacy trade
should say so rather than let it arrive as a side effect. *(Note the shape of the alternative, so the
choice is real: `TTS-9` removed eSpeak entirely, so "keep it local" is no longer one config key — it
is a new offline engine.)*

**Designer — three, all small:**

1. **Two new error strings** (Task 4): `That message is too long for the console to read.` and
   `The console isn't responding. Try again.` §Cross-5 specifies only the generic pair.
2. **Q2 is being answered "no hairline bar"** by this plan, on the handoff's own recommendation
   (`:311`). Say if that is wrong.
3. **Q5 is closed, not open** (`C-105`), and Task 7 marks it so. Confirm.

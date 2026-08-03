# Handoff: Phone — console-routed voicemail, spoken text messages, canned replies

- **Status:** Draft for owner review (Designer phase)
- **Date:** 2026-08-01
- **Author:** Designer (Claude)
- **Surface:** `/phone` in `src/Radio.Web` (Blazor Server + Radzen `material-dark` + `wwwroot/css/design-system.css`), plus one additive element in the topbar (`MainLayout.razor`).
- **Form factor:** wall-mounted kiosk, **1920×720**, touch-only, dark room, glanceable across a room. 120px topbar → 600px content area → **536px** usable once the 64px `NowPlayingDock` is subtracted.
- **Read-only handoff.** No source was changed. This is the artifact **Planner** consumes.
- **Companion:** **ADR-029** — `design/decisions/2026-08-03-gv-audio-through-engine.md`. The Architect ran in parallel and has **resolved every contingency this doc originally carried**; §12 of that ADR is written as a handoff to this one. There are **no open contingencies left** — the transport below is a single design, not a branching one. Where ADR-029 asked Designer a question back, the answer is in **§Answers to ADR-029 §12** at the end.

Covers a three-part arc:

- **A.** Voicemail playback moves from the browser `<audio>` element into the console's audio engine (with ducking).
- **B.** A text message gets a play affordance that speaks it through the TTS flow.
- **C.** Freeform SMS compose is replaced by a fixed set of canned responses.

---

## follows / extends / deviates

### FOLLOWS (reused verbatim — do not reinvent)

- **`HANDOFF-phone-messages-voicemail-sms.md`** — the IA (unified feed + 4-segment filter + 520px detail pane), the voicemail accordion-under-the-row placement, the bubble primitives, the badge model, the toast rules, the timestamp rule, and the copy voice ("plain, calm, sentence case, no exclamation marks, never blame the user; errors say *what happened + what to do*").
- **`HANDOFF-phone-dark-theme-and-scrollbars.md`** — `color-scheme: dark`, the `.list-item-touch` button reset, the global scrollbar, the 44px `.feed-chip` spine, the `:focus-visible` ring convention (`box-shadow: inset 0 0 0 2px var(--accent-primary)`), and the canonical error copy at `:310`.
- **Every `:root` token** in `design-system.css:49–151`. **Zero new colours. Zero new fonts. Zero new `:root` entries.**
- **The existing send contract, unchanged.** ADR-028 §2 (nine-code taxonomy), §4.4 (the de-dupe invariant), §5 (code → UI state), §8 (reply-ability). Feature C changes *what produces the text*; it changes nothing about how a send is issued, reconciled, or failed.
- **The four-state composer precedence** from `docs/superpowers/plans/2026-07-30-gv-messages-pr5-send-contract.md:1894`. Owner-approved; carried forward intact (see §C4 for the one refinement, which does not reorder it).
- **The exact strings** `You can't reply to this sender.` · `Reply once this loads.` · `Texting unavailable` · `Couldn't load messages.` · `Couldn't load conversations.` · `Couldn't load this recording.` · `Fetching recording…` · `Retry` · `Transcript` · `No transcript available.`
- **`PhoneUnreadState`** (`src/Radio.Web/Services/PhoneUnreadState.cs`) as the precedent shape for a small singleton surfaced from `/phone` to `MainLayout`. §Cross-1 asks for a second one built the same way.
- **`.phone-mode-btn.active`'s** exact active recipe — `background: var(--accent-dim)` + `box-shadow: inset 0 0 0 1px rgba(92,212,232,0.22)` + `color: var(--accent-primary)`. Every new "this is live" state in this doc reuses it verbatim so the surface has one idea of "active."

### EXTENDS (new, built FROM the patterns above)

- A **console-playback chip** in `.topbar-primary`, styled as a `.nav-pill` with the `.phone-mode-btn.active` recipe. The global "the console is playing something" indicator + stop.
- **`.transport-btn-secondary`** (skip-back / skip-forward / stop) beside the existing `.transport-btn-primary`, and an explicit visual treatment for both (they currently have *none* — see §Gaps).
- **`.msg-row-inbound`** — a wrapper that puts a speak button in the gutter beside an inbound bubble. Deliberately wraps **inbound only**, so `.msg-bubble` and the whole outbound path are untouched.
- **`.msg-speak-btn`** + **`.msg-bubble.speaking`** — the per-message play affordance and its active state, both from existing tokens.
- **`.reply-tray` / `.reply-tray-grid` / `.reply-option`** — the canned-response tray, assembled from existing tokens exactly the way `.feed-chip` and `.gv-reconnect-banner` were.
- A **real `.spinner`** (§Gaps G-1 — it currently renders nothing at all).

### DEVIATES (flagged — owner direction required, not Designer initiative)

**D-1. Feature A supersedes two shipped architecture decisions.** This is the big one and it must not be papered over.

> `design/decisions/2026-06-20-gvbridge-voicemail-sms-integration.md:63` (**ADR-022 D1**):
> *"Radio.API adds nothing here. Radio.API owns the audio engine/hardware. GV voicemail/SMS is pure RotaryPhone state with **no audio-engine involvement** (the voicemail recording plays in the browser's `<audio>`, not through the SoundFlow pipeline). A proxy would only add latency and a second failure point."*
>
> `…:180` (**ADR-022 D4**, "the critical one"):
> *"the browser's `<audio src>` points at the absolute RotaryPhone URL … a direct hit, **NOT proxied** through Radio.Web or Radio.API."*

Feature A reverses both. ADR-022 is still marked **Proposed**, which makes it cheaper to amend than an Accepted one, and D4 already names the sanctioned path back ("a Range-forwarding proxy on Web/API would be required — explicitly out of scope today and noted as a **future constraint**"). **Feature A is that future arriving.** The Architect must formally supersede D1/D4; this handoff assumes they will and specifies the UX either way. It is owner-directed, not Designer initiative.

*Three things get better as a side effect, worth recording as arguments in favour:* (1) `docs/BUILDER_QUEUE.md:72` carries an open risk — the voicemail audio endpoint must stay unauthenticated because *"native `<audio>` can't send the header."* A server-side fetch **can** send `X-RotaryPhone-Auth`, so feature A **closes that risk** rather than deferring it again. (2) The recording plays wherever the console's output is pointed, including the soundbar, instead of out of a browser the user is not standing at. (3) The ingest shape already exists: `AudioFileEventSourceFactory.CreateFromStream(name, stream, duration)` (`:74–85`) already turns arbitrary bytes into a duckable `IEventAudioSource`, and Radio.API already reaches `radio:5004` over plain `HttpClient` (`PhoneContactLookupService.cs:20–27`). The server-side path is short.

**D-2. The seekable scrubber survives — the same gesture, a different mechanism.** `HANDOFF-phone-messages-voicemail-sms.md:203` specced the progress bar as *"made seekable."* ADR-022 D4 chose the browser `<audio>` element specifically to preserve HTTP Range for that. ADR-029 §8.3 keeps the capability by materialising the recording to a local file first, so seeking becomes a local file operation and Range is irrelevant. **Net effect on the user: none — tap-to-seek still works, and pause is gained.** Two smaller notes: the transport adds ±15s skip buttons, and drag-scrub is explicitly deferred rather than left ambiguous (§A3).

**D-3. `＋ New` and the whole new-recipient composer (Screen D) are removed.** `HANDOFF-phone-messages-voicemail-sms.md` §Screen D is retired in full. This follows directly from the owner's framing but the *consequence* needs an explicit nod: **after C, the console cannot start a text conversation with anyone who has not texted it first** — unless `Text back` ships (open question #5 in the original handoff, `:542`, still unanswered). See §C6.

**D-4. Feature B's transport is not literally identical to voicemail's.** The owner asked for identical. The affordance, the states, the ducking behaviour, the stop, and the one-at-a-time rule *are* identical. A scrubber and a `0:14 / 0:42` readout are not, because a synthesized utterance has no duration the user knows before it starts and a message bubble has no room for a transport row. §B1 states exactly what "identical" is honoured as, and open question **Q2** puts it back to the owner rather than deciding it silently.

Everything else maps to an existing token, class, or already-approved decision.

---

## Who uses this & success criteria

One person, one fixed 1920×720 wall panel, touch-only, often dark, often across the room. "Grandpa's radio." The device is a **radio console that also does phone things** — not a phone.

1. Tapping play on a voicemail makes the room play it, over the music, at a sane volume, **without the user wondering whether it worked**.
2. The user can stop it from **anywhere in the app**, not only from the row they started it on.
3. The music is obviously still there — ducked, not stopped — and comes back on its own.
4. A text can be **listened to** instead of read. This matters most for the 70% of threads that are a bare identifier: you may not be able to reply to `32665`, but the console can still read you the code.
5. Replying is **two taps, no typing, no keyboard**, and never sends something the user didn't choose.
6. Every "you can't do this here" state says *why*, in one short sentence. No silently-disabled controls (UAT **F-3**).
7. Fits 536px with no shell scrollbar. **Zero new tokens, zero drift.** Polisher finds nothing.

---

## Cross-cutting: the "playing on the console" model

Features A and B share one model. Specify it once; both consume it.

### Cross-1 — One voice at a time

> **The console plays exactly one message at a time.** Starting anything — a voicemail, or a spoken text — **stops** whatever the console was already playing or speaking. The stopped item returns to its resting state, silently: **no error, no toast, no dialog.**

This single rule answers all four collision cases: voicemail→voicemail, text→text, text→voicemail, voicemail→text. It is the reason there is no queue.

**No queue, deliberately.** A backlog of pending utterances is invisible, has no UI to inspect or cancel, and on a kiosk becomes a trap ("why is it still talking?"). A user who taps three messages in a row is almost always correcting themselves; last tap wins is what they meant.

**ADR-029 §6.2 rule 1 makes this an architectural guarantee**, so the UI can rely on it rather than police it. The ADR builds the mechanism deliberately: the documented 1–10 priority was **metadata only** before this arc (it did not affect duck depth) and ducking is reference-counted rather than preemptive, so exclusivity had to be added *above* the ducking layer. See §A4b for the IA consequence — every play button on `/phone` is one single-selection group.

**Tapping the active item's own button stops it** (play → stop → play). Tapping a *different* item's button replaces. On a message bubble this is unambiguous because there is no pause: the active button shows `stop`. On the voicemail transport, where pause *does* exist, the primary button pauses and the separate `■` button stops — which is why §A2 keeps a dedicated Stop control rather than overloading the primary.

**An incoming call outranks everything.** ADR-029 §6.2 rule 2: a real inbound call or caller announcement **stops** playback (it does not pause it), the chip disappears, and nothing is queued to resume. The UI must land in a clean replayable Idle state.

**One wart, recorded so it is not mistaken for an oversight.** ADR-029 §6.2 rule 3 leaves sub-priority-8 announcements *mixing over* attended playback, so a low-priority system announcement can still talk across a voicemail. Pre-existing, explicitly out of scope for that ADR, and not something the phone UI should try to paper over.

### Cross-2 — The NowPlayingDock does NOT change. This is an instruction, not an omission.

While a voicemail or a spoken message is playing, the dock keeps showing **the music** — same title, same artist, same progress, same transport.

Rationale: the music genuinely *is* still the primary source; it is only ducked. The dock continuing to show `Crazy Little Thing Called Love · Queen` is the reassurance that nothing was lost, and it is free. Repurposing the dock would (a) lie about what the primary source is, (b) turn a global transport into a mode-switching control, so the user reaching for "pause my music" would hit "stop the voicemail," and (c) do it for a ≤60s transient.

**Planner: do not let anyone "helpfully" take over the dock.** The 64px height and the `.content-area.has-dock .page-transition { height: calc(100% - 64px) }` coupling (`design-system.css:2343`) also mean any dock growth silently breaks the phone panel's height — which is exactly the bug that rule was added to fix.

### Cross-3 — The console-playback chip (the global remote)

A single transient element in `.topbar-primary`, inserted as the **last child before `.topbar-nav`** (which carries `margin-left: auto`, so a new sibling lands in the empty span between the Out picker and the nav pills — roughly x≈700 at 1920, confirmed against the live capture).

```
┌ .topbar-primary ─────────────────────────────────────────────────────────────┐
│ TIME  │ IN ▪FM/AM Radio → OUT ▪Soundbar  [Cast] [Out]   ((•)) Voicemail ⏹    │
│                                                          ▲ appears only while  │
│                                                            the console plays   │
│                                                       …then HOME QUEUE … PHONE │
└──────────────────────────────────────────────────────────────────────────────┘
```

- **Why the topbar and not the dock.** Besides Cross-2, there is a decisive practical reason: **the dock is not global.** `MainLayout.razor:878` gates it on `IsDockVisible => !_isOnHome` — it is absent on Home, where `NowPlayingPanel` owns the surface. A stop control that vanishes on the app's landing page is not a global remote. The topbar is on every route.
- **Markup:** a `<button class="nav-pill nav-pill-playing">` — a real button, because it is the stop control. It is *not* a marker inside the `/phone` pill: that pill's bottom-right corner is already occupied by the bell-fault glyph (`.phone-nav-fault`, `design-system.css:5316`), and that glyph is `pointer-events: none` while this must be tappable.
- **Contents:** `[volume_up icon] [label] [stop_circle icon]`. Label is `Voicemail` or `Message` — the *kind*, not the sender. The sender goes in `title` and `aria-label`, which have room.
- **Visible only while something is playing.** Appears on start, disappears on end/stop. It never occupies space at rest.
- **Tap = stop.** One tap, no confirm. Stopping playback is not destructive.
- **No pulse, no blink.** The cyan-on-`--accent-dim` state change is enough, and a pulsing element on a wall panel in a dark room is what the sleep/dim work exists to prevent.

```css
/* §Ph — console playback chip. Reuses .nav-pill geometry + the .phone-mode-btn.active
   recipe verbatim so "live" looks the same everywhere on this surface. */
.nav-pill.nav-pill-playing {
  background: var(--accent-dim);
  box-shadow: inset 0 0 0 1px rgba(92, 212, 232, 0.22);
}
.nav-pill.nav-pill-playing .rzi,
.nav-pill.nav-pill-playing .nav-pill-label { color: var(--accent-primary); }
```

**Copy:**

| Element | String |
|---|---|
| Chip label (voicemail) | `Voicemail` |
| Chip label (spoken text) | `Message` |
| `title` (voicemail) | `Playing voicemail from {caller} on the console. Tap to stop.` |
| `title` (spoken text) | `Reading a message from {sender} on the console. Tap to stop.` |
| `aria-label` (voicemail) | `Stop playing voicemail from {caller}` |
| `aria-label` (spoken text) | `Stop reading message from {sender}` |

`{caller}` / `{sender}` use the **same resolution chain the rows already use** — `ResolvedName → CounterpartyName/FromName → raw identifier` (`PhoneTextsPanel.ResolveThreadName:413`, `VoicemailRow.DisplayName`). For the 70% unresolved case this is a bare number or short code, which is correct and expected.

### Cross-4 — Playback state is server-owned; the UI is a view of it

Every transport in this doc renders **server state**, never local state. The concrete consequences are what Planner needs:

- **Navigating away does not stop playback.** The current `VoicemailPlayer.DisposeAsync` (`:179`) tears down the `<audio>` element, and ADR-029 §7.2's default rule likewise stops audio on navigate-away. **This design overrides that** — see §Answers, item 6. Component disposal must not stop the console; the topbar chip is what earns that.
- **Returning to `/phone` re-attaches.** Re-opening the same voicemail row shows the correct live position and the right glyph, because the transport is a view of a global server snapshot (ADR-029 §8.1), not per-circuit state. This currently does *not* hold — every `VoicemailPlayer` holds private transport state at `:76`, which is why two browsers would play independently today.
- **Every tap is optimistic, then reconciled.** Tap → the button changes on the next frame → the server confirms → the button settles. Same shape as the send path (ADR-028 §4.4) and read-state (ADR-024 §9). A tap that produces no visible change reads as a broken button on a touch panel — and per §A5, this matters most precisely because the fast path is *very* fast.
- **Position is anchored, not ticked.** ADR-029 §8.2 broadcasts `PositionAtBroadcast` + `BroadcastAtUtc` + `State` on every transition and **nothing in between**; the client advances the bar from that anchor using its own clock. Do not request, design around, or assume a periodic position push — the ADR rejects it because steady-state churn is audible on the N100, and local interpolation is smoother anyway (60 fps local vs any wire rate). Re-anchor on every state change. Under `prefers-reduced-motion`, step at each anchor rather than animating.

### Cross-5 — Failure is a common path, not an edge case

**This is the most important thing ADR-029 §12 item 3 tells the design, and it inverts a normal assumption.** GV auth is dead for roughly **9 minutes in every 20**. A first-time voicemail fetch therefore has about a **45% chance of failing** — and it fails **before any sound**, so the user's mental model is "I pressed play and nothing happened."

Worse, **the existing "Google Voice is reconnecting" banner is known not to fire in exactly that window**, because the status endpoint lies (`design/INTEGRATIONS.md`; `PhoneMessagesPanel.razor:14–20`). So there is no ambient explanation on screen.

> **Design rule: the player owns its own error state and must never lean on the reconnect banner to explain a failed fetch.**

Because a coin-flip failure is the norm rather than an incident, the copy has to set the right expectation — that this is transient and worth another tap — without blaming anything the user did:

| Condition | Where | Primary copy | Sub-line | Action |
|---|---|---|---|---|
| **GV fetch failed** (502 / auth blackout) — *the common one* | Replaces the transport, existing error-row shape (`VoicemailPlayer.razor:12–16`) | `Couldn't load this recording.` *(existing string)* | **`This usually clears up in a minute. Try again.`** ⚠ *new* | `Retry` |
| **Audio engine refused** (device gone, output in a bad state) | Same row shape | `The console can't play this right now.` ⚠ *new* | — | `Retry` |
| **TTS synthesis failed** (feature B) | Toast only — there is no room beside a bubble | `Couldn't read that message.` ⚠ *new* | `The console couldn't read this one. Try again.` | button returns to rest |
| **Console muted / volume 0** | `.phone-pill.amber`, inline | `The console is muted.` ⚠ *new* | — | `Unmute` (`.phone-btn-sm`) |

Four distinct failures, four distinct sentences. **Do not collapse them** — "couldn't load the recording" and "the console can't play it" are different problems with different fixes, and after this change both are reachable.

**Two things make repeated failure survivable, and the copy should be true to them.** ADR-029 §5.3 caches a recording once fetched, so (a) a successful play is permanently reliable afterwards even during a later blackout, and (b) `Retry` genuinely does have good odds. `This usually clears up in a minute.` is an accurate promise, not a soothing one.

`AudioStateHubService` is already injected into `MainLayout`, so mute/volume state is readable in the web layer today. The muted-state check is **recommended, not required** — it is the difference between "this feature is broken" and "oh, I'm muted."

---

## Feature A — Voicemail through the console audio engine

### A1 — What changes, conceptually

The transport stops being a media player and becomes **a remote control for something happening in the room.** Three consequences drive every decision below: there is now **latency** on every command; the sound is **not coming from the thing you are touching**; and the thing keeps happening **after you navigate away**.

Placement does **not** change: the player stays the accordion under the tapped row in the feed pane (`VoicemailRow.razor` renders `<VoicemailPlayer>` as a sibling inside `.vm-row-wrap`). The detail pane is untouched.

### A2 — Transport layout

```
│ 🎙 ⊙ Jane Appleseed                    vmail   9:41a   "Hey, calling about…"   ▾ │
│ ┌───────────────────────────────────────────────────────────────────────────┐  │
│ │  (⟲15)  ( ▶ )  (15⟳)  ──────●──────────────────────  0:14 / 0:42   ( ■ )  │  │
│ │   48px   56px   48px   ← tap-to-seek, ≥44px hit area →  mono         48px  │  │
│ │                                                                            │  │
│ │  Transcript                                                                │  │
│ │  Hey, calling about the thing on Saturday — give me a ring back when you   │  │
│ │  get a sec. Thanks, bye.                                                  │  │
│ └───────────────────────────────────────────────────────────────────────────┘  │
```

| Control | Class | Size | Glyph | Note |
|---|---|---|---|---|
| Back 15s | `.transport-btn.transport-btn-secondary` | 48px | `replay_10`* | Disabled while Idle/Preparing |
| Play / Pause | `.transport-btn.transport-btn-primary` | 56px | `play_arrow` / `pause` | Gains the missing `.transport-btn` class — §Gaps G-2 |
| Forward 15s | `.transport-btn.transport-btn-secondary` | 48px | `forward_10`* | Disabled while Idle/Preparing |
| Progress | `.vm-scrubber` > `.now-playing-dock-progress` | ≥44px hit area | — | Tap-to-seek. §A3 |
| Time | `.vm-time` | — | — | `0:14 / 0:42`; indeterminate when duration unknown |
| Stop | `.transport-btn.transport-btn-secondary` | 48px | `stop` | Always present; distinct from Pause — §A4 |

\* Material Icons ships `replay_10` / `replay_30` and `forward_10` / `forward_30`, but no `_15`. ADR-029 §12 specifies **15 seconds**. **Use the `replay_10` / `forward_10` glyphs and accept the numeral mismatch, or use the numeral-free `fast_rewind` / `fast_forward`.** Recommendation: **`fast_rewind` / `fast_forward`** — a glyph that says "10" while the button does 15 is a small, permanent lie, and at kiosk viewing distance the numeral inside the glyph is barely legible anyway. The `aria-label` carries the real number. Flagged as **Q11**.

Gap between controls: `var(--sp-3)` (12px), matching the existing `.vm-player-transport`. The feed pane is ≈1240px wide at 1920, so there is ample room.

### A3 — The scrubber: tap-to-seek ships, drag is deferred

**ADR-029 §8.3 resolved this.** Seek is a **local file** operation (the recording is materialised to disk before playing), so HTTP Range — the thing ADR-022 D4 chose the browser `<audio>` element to preserve — is no longer needed to preserve it. Measured round trip: **~30–85 ms**, comfortably under the ~100 ms that reads as instant for a discrete action.

**What ships: tap-to-seek, unchanged from the user's point of view.** The current player is *already* tap-to-seek — `VoicemailPlayer.razor:36` binds `@onclick` and `:135` takes a `MouseEventArgs`, computing a fraction from a single click's `clientX`. There is no pointermove handler and never was. Only the plumbing moves server-side.

**A `Seeking` state is still needed** despite the low latency: on tap, the bar jumps to the tapped position **immediately** (optimistic), and the time readout dims to `--text-medium` until the server re-anchors. 85 ms is fast, but it is not zero, and an optimistic jump costs nothing.

**Drag-scrub is deferred, not forbidden.** ADR-029 clears a specific safe pattern — *the thumb moves locally with no audio response; audio repositions once, on release* — because continuous scrubbing would need a ~16 ms budget we do not have. That pattern is sound. It is deferred anyway, for design reasons rather than technical ones:

- It requires a **visible, grabbable thumb**, which `.now-playing-dock-progress` (a 3px bar with no thumb) does not have. That is a new visual primitive on a shared class.
- The need is already met. On a ~700px bar over a 42-second voicemail, a tap resolves to ~0.06 s/px — far finer than anyone needs — and `⟲15` covers the actual need, which is *"say that again."*
- Pointer-capture drag on a touch panel competes with the feed's own vertical scroll gesture.

**If it is ever added, ADR-029's commit-on-release pattern is the only acceptable implementation.** Recorded so nobody later ships audio-follows-thumb and discovers the 16 ms budget the hard way.

**The progress bar is client-interpolated, never server-ticked** (ADR-029 §8.2). The server broadcasts an *anchor* — `PositionAtBroadcast` + `BroadcastAtUtc` + `State` — and the client advances the bar locally from that anchor while `State == Playing`, re-anchoring on every state transition. **Do not design or request a position tick**; the ADR rejects it explicitly because steady-state churn is audible on the N100. The existing `.now-playing-dock-progress-bar { transition: width 200ms linear }` is the wrong tool for this — interpolation wants a local rAF loop or a long-duration linear transition re-armed at each anchor. Under `prefers-reduced-motion`, step the bar at each anchor instead of animating.

### A4 — Stop, Pause, and what an incoming call does

All three now exist and all three differ:

- **Pause** holds the position *and* the duck claim — the music stays quiet. Correct for "hang on, someone's talking to me." **Voicemail only**; ADR-029 §8.3 gives `TTSEventSource` `IsSeekable => false` and no pause, because pausing mid-utterance has no user value.
- **Stop** ends playback, releases the claim, and **the music comes back up**. Position resets to 0. Present for both features.
- **Preempted by a call** — a real inbound call or caller announcement **stops** playback; it does not pause it (ADR-029 §6.2 rule 2). The UI must land in a clean **Idle/replayable** state, not a paused-mid-track one, because there is nothing to resume *to*. Replaying is expected and fine.

Stop is also what the topbar chip does, so the two are one action reached from two places. **The chip is present for Preparing, Playing *and* Paused** — a paused voicemail is still holding the room ducked, so it must stay globally visible and stoppable.

### A4b — At most one thing plays, anywhere on the surface

ADR-029 §6.2 rule 1 makes this an architectural guarantee, not just a convention: **starting one attended playback stops the other.** The design consequence is worth stating in IA terms because it is easy to build wrong:

> **Every play button on `/phone` — every voicemail row and every inbound message bubble — belongs to one single-selection group.** They are not independent toggles.

This *extends* an existing behaviour rather than inventing one: the voicemail list is already one-at-a-time via `_openVoicemailId` (`PhoneMessagesPanel.razor:285`). Feature B joins the same group.

**Playback state is global, not per-browser** (ADR-029 §8.1) — there is one set of speakers, so two browsers on `/phone` see the same transport and either can stop it. This is also what makes "navigate away and come back" work for free (§Cross-4).

### A5 — States

| State | Trigger | What renders |
|---|---|---|
State names map 1:1 onto ADR-029 §8.2's `EventPlaybackState` — `Preparing | Playing | Paused | Completed | Stopped | Failed` — so the UI never invents a state the snapshot cannot express.

| State | `EventPlaybackState` | Trigger | What renders |
|---|---|---|---|
| **Idle / ready** | *(no active event)* | Player opened, nothing requested | ▶ enabled; ⟲15 / 15⟳ / ■ **disabled** (`opacity: 0.35`); progress at 0; `0:00 / {duration}`, or the indeterminate treatment when duration is unknown. |
| **Preparing** | `Preparing` | ▶ tapped — a network fetch (first play) or TTS synthesis is running | ▶ shows `.spinner`; other controls inert; voicemail shows the sub-line **`Fetching recording…`** (existing string, `.vm-buffering-note`). **A multi-second stall is expected and must read as intentional.** |
| **Playing** | `Playing` | Server confirms | Primary shows `pause`; progress interpolates from the anchor; **topbar chip appears**. |
| **Paused** | `Paused` | Pause confirmed (voicemail only) | ▶ returns; position held; **chip stays** (still ducking). |
| **Seeking** | *(client-only)* | Tap on the bar | Bar jumps optimistically; time readout dims to `--text-medium` until the next anchor. ~30–85 ms. |
| **Ended** | `Completed` | Playback finished | Resets to ▶ at 0; chip disappears; music un-ducks. **No toast.** |
| **Replaced** | `Stopped` | User started something else (Cross-1 / §A4b) | Silently returns to Idle. **No error, no toast.** |
| **Preempted** | `Stopped` | An inbound call or caller announcement took the audio | Returns to **Idle**, replayable — *not* paused-mid-track. **No error toast**; this is the system behaving correctly. |
| **Recording error** | `Failed` | GV fetch failed / 502 | The error row — see the blackout note below. |
| **Engine error** | `Failed` | Audio engine refused / device gone | Same row shape: **`The console can't play this right now.`** + `Retry`. |
| **Muted** | any | Console muted or volume 0 at play time | `.phone-pill.amber` **`The console is muted.`** + `Unmute`. Playback still proceeds. |

**⚠ The `Preparing` state has two wildly different durations, and the design must survive both.** ADR-029 §5.3 caches each recording after its first fetch, so **a replay is effectively instant while a first play is a network round trip.** The same button therefore resolves in ~0 ms or in seconds depending on history the user cannot see. Consequence: **never suppress the spinner behind a "it's probably fast" delay.** Render `Preparing` on the very next frame after the tap, unconditionally. If it resolves in 40 ms the user perceives a press flash, which is correct feedback; if it is deferred by 150 ms "to avoid flicker," the slow case looks like a dead button — and the slow case is the one that matters.

**⚠ A duration the UI can't trust makes the whole transport lie.** `DurationSeconds == 0` means **"unknown"** in the GV contract (ADR-029 §4.1), it is a live and expected case, and both the interpolated bar and the completion timer are derived from that same number — `AudioFileEventSource` detects completion with `await Task.Delay(_duration)` (`:205`) rather than a real end-of-stream event. At zero, that reports "ended" instantly while audio is still playing.

**Design rule:** when duration is unknown, render the bar as **indeterminate** — no thumb, no percentage, total shows `--:--`, elapsed still ticks from the anchor — and never render `0:00` as if it were a real total. Do not fake a percentage against an unknown denominator; a bar that crawls to 100% and stops while sound continues is worse than no bar. **Seek is also meaningless in this state** — the bar takes no taps and the ±15s buttons stay live, since a relative skip needs no total. Raised as **Q10**; ADR-029 §14 Q4 separately assigns Planner the job of re-arming that timer after a seek.

Transcript states are **unchanged** — `Transcript` / `Transcript pending — Google is still transcribing this voicemail.` / `No transcript available.` (`VoicemailPlayer.razor:51–67`).

**Mark-heard is unchanged.** Opening/playing still bubbles `OnHeard` → `PhonePage.OnVoicemailHeard` → the single durable write path (ADR-024). The player still must not call the mark-read route directly.

### A6 — Accessibility

- Primary keeps `aria-label="Play voicemail from {caller}"`, becoming `"Pause"` while playing. Add `"Back 15 seconds"` / `"Forward 15 seconds"` (the real interval, whatever glyph Q11 picks) and `"Stop playing"`.
- Progress keeps `role="slider"` + `aria-valuemin/max/now` **because it is genuinely interactive** — that was the open risk and it resolved in the bar's favour. **But when duration is unknown it must switch to `role="progressbar"`** with `aria-valuetext="Unknown length"`, because there is nothing to seek within. An unchangeable `slider` is an accessibility lie in exactly that state.
- Add a visually-hidden `aria-live="polite"` line in the player announcing `Playing on the console.` / `Stopped.` on state change — the sound is elsewhere, so a non-sighted user gets no other confirmation.
- All transport buttons take the house focus ring: `box-shadow: inset 0 0 0 2px var(--accent-primary)` on `:focus-visible`.

---

## Feature B — Speak a text message

### B1 — What "identical to voicemail's" is honoured as

| Identical | Not identical, and why |
|---|---|
| The circular transport button and its `play_arrow` glyph | No `0:14 / 0:42` readout — a synthesized utterance has no duration the user knows before it starts |
| The `Preparing` spinner state | No ±15s, **no seek, no pause** — ADR-029 §8.3 gives `TTSEventSource` `IsSeekable => false` and no pause, because there is no meaningful position inside an utterance |
| Playing through the console, ducking the music | No progress bar by default ⟨see below⟩ |
| **Stop**, and the topbar chip that stops it from anywhere | No `Restart` — replaying an eight-word text is just tapping play again |
| The one-voice-at-a-time rule (Cross-1 / §A4b) | — |

**These asymmetries are now the Architect's decision, not a Designer preference**, which changes the shape of the owner question: it is no longer "should B have a scrubber" but "is the identical *button and behaviour* what you meant." **Q2.**

**⟨Optional⟩** The one gap that *could* still close: `TTSEventSource` is constructed with a known `TimeSpan` duration, so once synthesis finishes the length is known. A hairline progress line — `.now-playing-dock-progress` at 3px, full bubble width, directly under the bubble — is therefore renderable, just not until `Preparing` completes. Cheap, and it makes the two read as one family. Recommended only if Q2 comes back wanting maximum fidelity; otherwise skip it, because a bar that appears *after* the spinner is its own small jolt.

### B2 — Placement: the gutter, inbound only

```
  ┌───────────────────────────────────┐
  │ 77971 is your Facebook            │  ( ▶ )   ← 44px, gutter, top-aligned
  │ confirmation code                 │
  │ 3:29 PM                           │
  └───────────────────────────────────┘
```

**Inbound messages only.** You do not need the console to read back something you sent — and after feature C, every outbound message is a canned string the user picked from a list two seconds earlier. This also removes the side-switching problem (the gutter is on the right for inbound, the left for outbound), halves the visual noise, and leaves the entire outbound render path untouched.

**Outside the bubble, not inside it.** Inside `.msg-meta` a 44px control would nearly double a one-line bubble's height and put a touch target in an 11px text row. Outside, it sits in space that is already empty.

**Always visible, never hover-revealed.** There is no hover on this device. A tap-to-reveal-then-tap-to-play flow is two taps and an invented "selected bubble" state.

**Top-aligned, not centred.** A long marketing SMS can be six lines; a vertically-centred button floats away from the message's start. Top alignment reads as "the control for this message."

```css
/* §Ph — spoken-message row (feature B). Wraps INBOUND bubbles ONLY, so .msg-bubble
   and the entire outbound path are untouched. .msg-bubble.inbound already carries
   align-self: flex-start, which agrees with the row's align-items — no conflict. */
.msg-row-inbound {
  align-self: flex-start;
  display: flex;
  align-items: flex-start;
  gap: var(--sp-2);
  min-width: 0;
  /* Preserves the bubble at EXACTLY its current 72% of the list width. */
  max-width: calc(72% + var(--touch-compact) + var(--sp-2));
}
.msg-row-inbound > .msg-bubble { max-width: none; min-width: 0; }

.msg-speak-btn {
  width: var(--touch-compact); height: var(--touch-compact);   /* 44px */
  border-radius: 50%;
  display: inline-flex; align-items: center; justify-content: center;
  appearance: none; -webkit-appearance: none;
  background: transparent;
  border: 1px solid var(--surface-separator);
  color: var(--text-low);
  flex-shrink: 0;
  margin-top: 2px;                     /* optically centres on the first text line */
  cursor: pointer;
  transition: color 100ms ease, border-color 100ms ease, transform 80ms ease;
}
.msg-speak-btn:hover  { color: var(--text-medium); border-color: rgba(255, 255, 255, 0.10); }
.msg-speak-btn:active { transform: scale(0.92); }
.msg-speak-btn:focus-visible { outline: none; box-shadow: 0 0 0 2px var(--accent-primary); }

/* Active — the .phone-mode-btn.active recipe again. */
.msg-speak-btn.speaking {
  background: var(--accent-dim);
  border-color: rgba(92, 212, 232, 0.22);
  color: var(--accent-primary);
}
/* Which message the room is reading, at a glance. */
.msg-bubble.speaking { border-color: rgba(92, 212, 232, 0.45); }
```

`--touch-compact` (44px) is used rather than `--touch-min` (48px) to match the established 44px chip spine (`.vm-chip`, `.feed-chip`) and because the global `button { min-height: 44px }` at `:1292` already sets that floor.

### B3 — What actually gets spoken

This is where the live-data constraints bite hardest, and it is a **content spec**, not an implementation note. Order of operations:

**1. Lead-in — only when a name actually resolved.**

| Case | Spoken |
|---|---|
| Name resolved (6 of 20 live threads) | `Message from {Name}. {body}` |
| No name — E.164, short code, or opaque ID (**14 of 20 live threads**) | **`{body}` — nothing else.** |

Do **not** read the identifier aloud. `"Message from plus one nine one nine five five five oh one two three"` costs ~6 seconds and conveys nothing the user isn't already looking at. `"Message from three two six six five"` is worse than nothing. Since the unresolved case is the **common** case here (70%), silence is the right default, not the fallback.

**2. Never speak the timestamp.** It is on screen and it is not why the user tapped play.

**3. Strip the MMS sender prefix (UAT finding G-8).** Two live threads carry a preview of the form `+1XXXXXXXXXX - <text>` — a second phone number embedded in the body of a row that already shows a counterparty. Left alone, TTS opens by reading a ten-digit number. **Rule:** if the body begins with an E.164-shaped token followed by ` - `, drop the token and the separator before speaking. (Note: those same two threads are the two that never rendered a body in UAT **F-1** — so this rule may be untestable until F-1 is resolved. Specify it anyway.)

**4. Replace each URL with the words `a link`.** The live corpus is full of `https://www.crunchlabs.com/63415353575/orders/e691521a…`. Read character-by-character this is unbearable and is the single most likely cause of "I hit play and it read gibberish for forty seconds." Exact replacement string per ADR-029 §12 item 7.

**5. Keep digit runs verbatim.** `77971 is your Facebook confirmation code` is the single most useful thing this feature does for a short-code thread. Do not summarise, truncate, or skip numbers.

**6. Strip emoji.** `❤️Love you too! ❤️` should be spoken as `Love you too!`, not `red heart Love you too! red heart`. Required by ADR-029 §12 item 7; per-engine behaviour still worth a live listen (**Q4**).

**7. Hard cap at 1000 characters** (ADR-029 §12 item 7). Typical SMS is 160, so this is a safety valve rather than a routine truncation, and it needs **no UI indication** — the Stop button is the real control for a long read. Recorded because it is a contract constraint, not a preference.

**Composition happens in `Radio.Web`, not the audio layer** (ADR-029 §4.2): Radio.API speaks a finished string. So every rule above is a `Radio.Web` concern and belongs in the pure static helper named in §Component/file impact.

**8. Speak the literal body, not the list preview.** UAT **F-5** observed a bubble ending in a literal `...`, suggesting the conversation may render the truncated list snippet for some messages. If GV-10 confirms that, feature B would read an ellipsis aloud. **Dependency noted, not owned here.**

### B4 — States

| State | Button | Bubble | Chip |
|---|---|---|---|
| **Rest** | `play_arrow`, `--text-low` outline | normal | absent |
| **Pending / preparing** | `.spinner` | normal | absent |
| **Speaking** | `stop`, `.speaking` (cyan) | `.msg-bubble.speaking` (cyan border) | `Message` |
| **Ended** | back to Rest, silently | normal | absent |
| **Replaced** (Cross-1) | back to Rest, silently | normal | moves to the new item |
| **Engine error** | back to Rest | normal | absent | + toast `Error` / `Couldn't read that message.` / `The console couldn't read this one. Try again.` |
| **Muted** | proceeds | normal | shown | + `.phone-pill.amber` `The console is muted.` + `Unmute`, beside the compose area |

**On synthesis latency.** ADR-029 §9 pins message speech to the **local `espeak-ng`** engine, so synthesis is on-box and there is no network round trip — but it is still not instant, and unlike voicemail there is **no cache hit path**: every play re-synthesizes. So `Preparing` is the normal opening state for feature B, every time. If it persists past ~2s the button's `title` and `aria-label` become `Preparing…`.

**On voice quality — raised because ADR-029 §12 item 8 invites it.** The local engine sounds noticeably more robotic than the Google voice used for announcements. **The design's position: keep the local default.** Three reasons, plus one caveat, in §Answers item 8.

### B5 — The speak button is NOT gated by reply-ability

**Stated explicitly because it is exactly the kind of thing that gets wrongly conflated.** A short-code thread cannot be replied to; it can absolutely be read aloud. Feature C's four-state gate applies **only** to the reply affordance. The play button is present and live on every inbound bubble in every one of the four states.

This is the strongest argument for feature B on this device: **for the 70% of threads you can't reply to, the console can still read them to you.**

---

## Feature C — Canned replies

> *"Since this is primarily a radio console, and not a full-fledged communication device, responding to a text message should provide a few 'canned' responses, but not allow the whole 'create response from scratch' flow."* — owner, verbatim

### C1 — The recommended set: six, fixed, in fixed positions

| | Left column | Right column |
|---|---|---|
| **Row 1** | `Yes` | `No` |
| **Row 2** | `OK` | `Thanks.` |
| **Row 3** | `Call me when you can.` | `Love you.` |

**Why these.** The repliable population on this device is *people you know* — the six live threads that resolve to a contact, plus dialable E.164s. Everything short-code and automated is structurally un-repliable (ADR-028 §8). So the set is not general-purpose SMS; it is **acknowledgements and phone logistics from a wall panel at home**:

- Rows 1–2 are the acknowledgements that close most short exchanges. `Yes`/`No` answer a question; `OK`/`Thanks.` close one.
- `Call me when you can.` exploits what this device actually *is*: a phone. It converts a text thread into the modality the console is genuinely good at, and it puts the action on the other person, which is the easier ask from a wall panel. Likely the single most-used button here.
- `Love you.` is not sentiment for its own sake — the live corpus literally contains `❤️Love you too! ❤️` from a family contact. It is data-supported.

**Why six and not more — this is a geometry constraint, not a taste one.** See §C3; the arithmetic only leaves room for three rows. The two strongest runners-up, in order, if a live measurement turns out to allow a fourth row: **`I'll call you back.`** then **`Can't talk right now.`**.

**Punctuation** is deliberate: bare words for the one-word answers, full stops on the sentences. They render as literal message text in a bubble, so they must read like something a person wrote.

**Fixed, never reordered, never context-aware.** On a wall panel used daily by one person, **positional constancy is the entire usability story** — after a week the user taps `Love you.` at bottom-right without reading. A most-recently-used reorder destroys that and, worse, creates a mis-send: you tap where `Yes` was and send `No`. Any context-awareness that *reorders* is rejected on those grounds. (Context-awareness that *filters* is rejected too — a set that is sometimes four items and sometimes six breaks the same muscle memory.)

**Hardcode v1 in one `static readonly string[]`**, so changing the set is a one-line edit. An owner-editable set in Settings is a reasonable fast-follow, and is called out as **Q7** rather than built.

### C2 — Zero edit affordance. None.

No "edit before sending." No "+ Custom." No long-press-to-modify. The owner's framing forbids the from-scratch flow, and a half-editable canned response is the worst of both worlds — it resurrects the keyboard dependency that removing freeform compose exists to eliminate, for a fraction of the flexibility.

**Consequence worth putting in front of the owner (a point in this feature's favour):** the global virtual keyboard (`wwwroot/js/virtual-keyboard.js`, auto-shows on *any* focused text input) currently has, across the entire repo, **exactly one `data-keyboard` consumer** — `PhoneTextsPanel.razor:122`, the new-recipient field. After feature C, `/phone` contains **no text inputs at all**, and that consumer count drops to **zero**. The keyboard never appears on this surface again. It still serves its other consumers and is being evaluated separately; this handoff does not design around keeping or dropping it, it just records the delta.

### C3 — The interaction: Reply → tray → send

```
┌──────────── conversation pane (520px wide) ───────────────┐
│ ◂  Lynne Marley                                            │  ~44px
│    +1919***8129                                            │
├────────────────────────────────────────────────────────────┤
│  ┌──────────────────────────────┐   ( ▶ )                  │  .msg-list
│  │ Mark may I have the link for │                          │  (shrinks while
│  │ Alexis. Blessing today       │                          │   the tray is open)
│  │ 5:01 PM                      │                          │
│  └──────────────────────────────┘                          │
├────────────────────────────────────────────────────────────┤
│  ┌──────────────────┐  ┌──────────────────┐               │  ← .reply-tray
│  │ Yes              │  │ No               │               │    (only when open)
│  ├──────────────────┤  ├──────────────────┤               │
│  │ OK               │  │ Thanks.          │               │
│  ├──────────────────┤  ├──────────────────┤               │
│  │ Call me when you │  │ Love you.        │               │
│  │ can.             │  │                  │               │
│  └──────────────────┘  └──────────────────┘               │
├────────────────────────────────────────────────────────────┤
│  [            ✕  Close                                   ] │  ← the SAME button,
└────────────────────────────────────────────────────────────┘     56px, always present

  …when the tray is CLOSED:

├────────────────────────────────────────────────────────────┤
│  [            ↩  Reply                                   ] │
└────────────────────────────────────────────────────────────┘
```

1. **Closed:** the compose bar's slot holds a single full-width `Reply` button — `.phone-btn` (56px, the house's primary action shape), icon `reply`.
2. **Tap Reply** → the tray expands **above the button**, `max-height 200ms ease` (the same transition the "More" rail group and the voicemail accordion use, = `--anim-duration-normal`). The button stays put and becomes `✕ Close`. `.msg-list` shrinks; **scroll it to the bottom on open** so the message being replied to stays visible.
3. **Tap a response** → the tray closes immediately and the message sends.
4. **`Close` or `Escape`** closes with nothing sent.

**The button never moves, and there is no separate tray header.** One control in one fixed position that toggles — rather than a `Reply` button that is replaced by a tray with its own `✕` in a different place. Saves ~24px of a very tight budget and, more importantly, gives the muscle-memory story a fixed anchor: the bottom-most control is always "the reply control."

**Two taps to send, and the second tap is the safety.** One-tap-to-send would be dangerous: a mis-tap while scrolling a conversation would fire an irreversible SMS to a family member. There is **no undo** — Google Voice offers no unsend through this bridge, and this handoff must not imply one anywhere in copy. The tray's existence *is* the intent gate. That is a feature, not friction.

**Geometry — shown so it can be checked rather than trusted, because it is the constraint that sets the set size.**

*Widths* are solid: pane 520px − `.reply-tray` padding (12px × 2) = 496px → two 244px columns with an 8px gap. `Call me when you can.` at 15px Inter measures ≈165px, so every option fits on one or two lines.

*Heights* are an estimate with a real error bar, and the chain matters:

| | px | note |
|---|---|---|
| `.page-transition` with the dock | 536 | `calc(100% - 64px)` of the 600px content area |
| − `.panel-header` | ~42 | 10px padding ×2 + ~20px line + 1px border |
| − `.phone-mode-selector` + its wrapper | ~46 | `min-height: 40px` plus wrapper padding — **the least certain number here** |
| = `.phone-messages-detail` | **~448** | |
| − `.texts-conv-header` | ~45 | 8px padding ×2 + a 16px and a 12px line + 1px border |
| = available for list + reply block | **~403** | |
| − reply block, 6 options (3×56 + 2×8 + 24 pad + 56 button + ~16) | ~280 | |
| = **`.msg-list` while the tray is open** | **~123** | roughly one to two bubbles |

At **eight** options the same chain leaves ~**60px** — less than one bubble, which is too little; the user loses sight of the message they are answering. At **four** it leaves ~**188px**, comfortable. **Six is the recommendation and four is the safe fallback.**

> **⚠ Builder/Tester: measure `.panel-header` + `.phone-mode-selector` in the live app before the count is fixed.** Everything above is derived from CSS, and the mode-selector wrapper is the one value not pinned by a rule I could read. If the real figure differs by more than ~20px, re-run the table rather than trusting it. Flagged as **Q6**.

```css
/* §Ph — canned reply tray (feature C). Occupies the compose bar's slot; the
   pane's flex column already sizes .msg-list around it (design-system.css:5839). */
.reply-tray {
  border-top: 1px solid var(--surface-separator);
  background: var(--surface-raised);
  padding: var(--sp-3) var(--sp-3) var(--sp-4);
  display: flex; flex-direction: column; gap: var(--sp-2);
  flex-shrink: 0;
}
.reply-tray-header {
  display: flex; align-items: center; justify-content: space-between;
  font-family: var(--font-mono); font-size: 11px;
  text-transform: uppercase; letter-spacing: 0.08em;
  color: var(--text-low);
}
.reply-tray-grid { display: grid; grid-template-columns: 1fr 1fr; gap: var(--sp-2); }

/* NOT .phone-btn: that is uppercase, letter-spaced, pill-shaped command chrome.
   These render the LITERAL MESSAGE TEXT the user is choosing to send, so they must
   be sentence case, body font, left-aligned — a sentence, not a command. */
.reply-option {
  min-height: var(--touch-preferred);      /* 56px */
  padding: var(--sp-2) var(--sp-3);
  border-radius: 8px;
  appearance: none; -webkit-appearance: none;
  background: var(--surface-elevated);
  border: 1px solid var(--surface-separator);
  color: var(--text-high);
  font-family: var(--font-body); font-size: 15px; line-height: 1.3;
  text-align: left;
  display: flex; align-items: center;
  cursor: pointer;
  transition: background 100ms ease, border-color 100ms ease, transform 80ms ease;
  -webkit-tap-highlight-color: transparent;
}
.reply-option:hover  { background: var(--surface-overlay); border-color: rgba(255, 255, 255, 0.10); }
.reply-option:active { transform: scale(0.97); }
.reply-option:focus-visible { outline: none; box-shadow: inset 0 0 0 2px var(--accent-primary); }
```

### C4 — The four-state gate, extended (not reordered)

The owner-approved precedence from `2026-07-30-gv-messages-pr5-send-contract.md:1894` is carried forward **intact**. The Reply affordance is the thing being gated; the pill shapes and strings are the existing ones.

| Order | Condition | What renders in the compose slot | Resolves when |
|---|---|---|---|
| **1** | `!ThreadIsRepliable` | `.phone-pill` — **`You can't reply to this sender.`**<br>`title="This sender is a short code or automated ID."` | never (structural) |
| **2** | `ConversationFailedToLoad` | `.phone-pill.amber` — **`Reply once this loads.`**<br>`title="This conversation didn't load. Retry above."` | on `Retry` |
| **3a** | `!SendService.SendEnabled` | `.phone-pill.amber` — **`Replies are turned off.`** ⚠ *new string* | on a config flip |
| **3b** | `_serverSendDark` (409 `send_disabled`) | `.phone-pill.amber` — **`Replies are turned off.`** | on a server config flip |
| **3c** | `!GvStatus.IsAvailable` | `.phone-pill.amber` — **`Texting unavailable`**<br>`title="Google Voice is reconnecting."` | on reconnect |
| **4** | — | the `Reply` button | — |

**What changed and what did not.** Tiers 1, 2, and 4 are untouched. Tier 3 is **refined into three sub-cases in permanence order** — the plan's own stated principle ("most permanent first") applied one level down: a config flag is more permanent than a transient reconnect. Nothing is reordered relative to anything the owner already blessed.

**The one genuinely new thing is 3a**, and it exists to close UAT **F-3**:

> *"the composer is rendered but disabled, not hidden, and carries no explanation of why it is disabled — no tooltip, no helper text, no `title`. A user sees an input they cannot type into with no stated reason."*

Since `RotaryPhone:Gv:SendEnabled` is `false` today, **3a is the state this feature actually ships in.** It must be the best-looking of the five, not an afterthought — it is what the owner will see on day one. A disabled `Reply` button with no reason would reproduce F-3 exactly; the pill replaces the button and says why, in the same shape as its four siblings. Marked ⚠ because it needs the owner's nod (**Q3**).

**In tiers 1, 2, 3a–3c there is no `Reply` button at all** — the pill *is* the compose slot's whole content, matching the shipped `ComposeBar()` structure at `PhoneTextsPanel.razor:274–278`. Never hide the slot: *"an absent composer reads as a rendering bug, a disabled one reads as an answer."*

### C5 — Send states: reused verbatim, with one column that evaporates

The moment a canned response is tapped, **everything is the existing send path.** ADR-028 §4.4's reconciliation invariant, §5's nine-code mapping, `MessageBubble`'s `{None, Sending, Sent, Failed}`, `.msg-bubble.sending` / `.failed`, the 10s rate-limit cooldown, the no-auto-retry rule — all unchanged.

**But ADR-028 §5's "Compose text" column is load-bearing and half of it no longer applies.** Nothing was typed, so "text is restored" means nothing. Restate per code:

| `Code` | Bubble | Was "restore compose text" → now | Retry |
|---|---|---|---|
| `queued` | → `Sent` | n/a | — |
| `send_disabled` | **removed** (nothing was attempted) | tray stays available; slot falls to tier **3b** | none |
| `rate_limited` | → `Failed` | n/a | Retry after the 10s cooldown |
| `invalid_number` | → `Failed` | n/a — **and there is no recipient field to show an inline error in.** Toast only. | **no Retry** |
| `invalid_text` | → `Failed` | n/a — unreachable: canned strings are never empty | **no Retry** |
| `auth_unavailable` | → `Failed` | n/a | Retry |
| `upstream_error` | → `Failed` | n/a | Retry |
| `timeout` | → `Failed` | n/a | Retry (still ambiguous, still never auto) |
| `error` / unknown | → `Failed` | n/a | Retry |

**Retry gets simpler and safer.** `RetrySend` (`PhoneTextsPanel.razor:396`) currently re-arms `_draft` from the failed bubble's text and re-enters `SendDraftAsync`, and carries a `TODO(send-ship)` about an orphaned temp bubble. With canned strings the retry payload is just the same fixed string — there is no draft to preserve, restore, or lose. The failed bubble stays the ≥48px tap target it already is (`.msg-bubble.failed { min-height: var(--touch-min) }`).

**No success toast.** The bubble is the feedback. Unchanged from ADR-028.

### C6 — What is removed, and the consequence

**Removed from `PhoneTextsPanel.razor`:**

| What | Where |
|---|---|
| Recipient field + `.texts-compose-new` block | `:118–129` |
| `_composingNew` mode branch (Screen D) | `:104–140` |
| `StartNew()` / `CancelNew()` / `_recipient` / `_recipientError` / `OnRecipientInput` | `:294–306`, `:318–323` |
| `New message` button in the empty thread list | `:175–178` |
| `.texts-compose-input`, `_draft`, `OnDraftInput`, the segment counter | `:263–270`, `:312–316` |
| `＋ New` in the Texts header | per `HANDOFF-phone-messages-voicemail-sms.md:246` |

**Dead CSS to remove:** `.texts-compose-new`, `.texts-recipient-error`, `.texts-compose-input`, `.compose-send-enabled` (`design-system.css:5842–5859`; the last two were already markup-only).

**Retired copy strings:** `Phone number` · `Enter a valid phone number.` · `Message` (placeholder) · `＋ New` · `New message` · `{chars} · {n} SMS`.

**Changed copy (fixes UAT F-2).** `Start the conversation below.` currently renders for a genuinely-empty-but-successfully-loaded thread while pointing at a dead composer. It becomes state-neutral:

> **`No messages in this conversation.`**

The Reply affordance below it now states what is and isn't possible on its own — which is the whole point of the tiered gate.

**⚠ The consequence the owner must confirm.** With `＋ New` gone, **the console can only text people who have texted it first.** The `Text back` quick action — specced in the original handoff as owner question #5 (`:542`), still unanswered, still deferred in `FUTURE-WORK.md:593` and `BUILDER_QUEUE.md:94` — becomes the *only* remaining way to start a thread with someone in your call history or voicemail. Feature C therefore **forces** that long-deferred question. It is raised as **Q1**, not decided here.

Coherent post-C entry points into composition:
- Reply inside an existing thread → the tray.
- `Text back` from a call detail or a voicemail → opens/creates that thread → the tray.

Both are "someone contacted you, respond to them," which is exactly the posture the owner's framing describes.

### C7 — Accessibility

- `Reply` button: `aria-label="Reply"`, `aria-expanded` reflecting tray state, `aria-controls` pointing at the tray.
- Tray: `role="group"` + `aria-label="Canned replies"`. Each option is a real `<button>` whose accessible name **is its literal text** — no `aria-label` override, because the visible text is precisely what will be sent.
- On open, move focus to the first option. `Escape` closes and returns focus to `Reply`.
- Gate pills carry their reason in visible text, not only in `title` — the strings above are the accessible name (this is the F-3 fix).
- `.reply-option` takes the house focus ring: `inset 0 0 0 2px var(--accent-primary)`.

---

## Primitive gaps Builder must fix (found while specifying; all pre-existing)

**G-1 — `.spinner` renders nothing.** `design-system.css:1209` is `.spinner { animation: spin 1s linear infinite; }` and **nothing else** — no width, height, border, or radius, anywhere in any stylesheet. It is used as a bare `<span class="spinner"></span>` in `VoicemailPlayer.razor:25` and `MessageBubble.razor:18`, so **every buffering and sending state in the phone surface is currently invisible.** This handoff adds several more consumers, so it must be fixed first:

```css
.spinner {
  display: inline-block;
  width: 14px; height: 14px;
  border: 2px solid var(--surface-separator);
  border-top-color: var(--accent-primary);
  border-radius: 50%;
  animation: spin 1s linear infinite;
}
.transport-btn .spinner, .transport-btn-primary .spinner { width: 20px; height: 20px; }
```
The existing reduced-motion rule (`:1688`, `animation: none !important; opacity: 0.7`) then leaves a static ring, which still reads as "busy."

**G-2 — `.transport-btn-primary` has no shape and no surface.** It is a *separate* class, not a modifier of `.transport-btn` (`:683` vs `:695`), so `VoicemailPlayer.razor:21`'s bare `class="transport-btn-primary"` gets no `border-radius: 50%`, no background, no border, and no colour — the round accent button in the spec is not what renders. Fix in two parts: markup takes **both** classes (`class="transport-btn transport-btn-primary"`), and both get an explicit treatment, reusing the `.phone-mode-btn.active` recipe for the primary:

```css
.transport-btn-primary {
  appearance: none; -webkit-appearance: none;
  background: var(--accent-dim);
  border: 1px solid rgba(92, 212, 232, 0.22);
  color: var(--accent-primary);
}
.transport-btn-secondary {
  appearance: none; -webkit-appearance: none;
  background: transparent;
  border: 1px solid var(--surface-separator);
  color: var(--text-medium);
}
.transport-btn-secondary:hover { color: var(--text-high); border-color: rgba(255, 255, 255, 0.10); }
.transport-btn:disabled, .transport-btn-primary:disabled { opacity: 0.35; cursor: not-allowed; }
.transport-btn:focus-visible,
.transport-btn-primary:focus-visible { outline: none; box-shadow: inset 0 0 0 2px var(--accent-primary); }
```
This is the same class of bug as the near-white `.list-item-touch` rows: a `<button>` with no chrome reset.

**G-3 — `.vm-scrubber` hit area is 25px, and the bar stays interactive, so it must grow.** `:5685` is `padding: 11px 0` around a 3px bar — 25px total. That met the original handoff's stated ≥24px, but 25px is a poor seek target for a fingertip in a dark room, and §A3 confirms the bar remains tap-to-seek. **Change to `padding: 20px 0`** → 43 + 3 = **46px**, clearing `--touch-min`. Purely a padding change; the visible 3px bar is unaffected.

**G-4 — undefined tokens.** `--signal-green-glow` and `--signal-red-glow` are consumed at `:5148`, `:5357`, `:5364` but never declared in `:root`, so those `box-shadow`s resolve to nothing. **Do not add new consumers.** Nothing in this handoff uses them; recorded so nobody reaches for them.

**G-5 — `.phone-btn-sm`'s declared 36px never renders.** The global `button { min-height: var(--touch-compact) }` at `:1292` wins, so it paints at 44px. Fine for touch; noted so nobody "fixes" the discrepancy and shrinks every small button to 36px.

---

## Component / file impact (what, not how)

| Concern | File | Note |
|---|---|---|
| Voicemail transport rewrite (A) | `src/Radio.Web/Components/Pages/VoicemailPlayer.razor` | Transport becomes a view of server state. **`DisposeAsync` must stop disposing playback** (`:179`). |
| Browser audio teardown (A) | `src/Radio.Web/wwwroot/js/voicemail-player.js` | Retire, or reduce to nothing, per the Architect's mechanism. |
| Speak button + inbound row wrapper (B) | `src/Radio.Web/Components/Pages/MessageBubble.razor` | Inbound path only; outbound render path untouched. |
| Spoken-text content rules (B) | new pure static helper in `Radio.Web` | Prefix-strip / URL→`link` / emoji-strip / lead-in. **In-tree precedent: `GvCounterparty` and `GvDirection` in `ApiModels.cs`** — same shape, same file neighbourhood (ADR-028 §8.3). |
| Canned tray, compose removal, gate tiers (C) | `src/Radio.Web/Components/Pages/PhoneTextsPanel.razor` | See §C6 for the exact removals; §C4 for the branch order. |
| Console-playback chip (cross) | `src/Radio.Web/Components/Layout/MainLayout.razor` | Additive `<button>` before `.topbar-nav`. |
| Shared playback state (cross) | new singleton in `src/Radio.Web/Services/` | **Build it exactly like `PhoneUnreadState`** — same shape, same `Changed` event, same singleton registration. |
| CSS | `src/Radio.Web/wwwroot/css/design-system.css` §Ph | All new classes above + the four §Gaps fixes. **No `:root` changes.** |
| Unchanged | `PhoneMessagesPanel.razor` layout, `VoicemailRow.razor`, the segmented filter, `PhoneUnreadState`, badge model, `NowPlayingDock` | Only `VoicemailRow`'s child player changes behaviour. |

**Owned by ADR-029, not this doc** — read it, don't re-derive it: the `IEventPlaybackService` seam and request shape (§3–§4), server-side fetch + cache (§5), the priority/exclusivity mechanism and the one required `DuckingService` change (§6), lifecycle and the three stop paths (§7), the `EventPlaybackSnapshot` broadcast and the anchor model (§8), the speech-engine/privacy default (§9), config and the auth-seam closure (§10).

**Still open on the Architect's side:** ADR-029 §14 Q3 (does `SeekAsync` behave through SoundFlow for a short MP3 — if not, seek degrades to stop-and-restart-at-offset, which this design tolerates) and §14 Q4 (`AudioFileEventSource`'s completion timer must be re-armed after a seek). Both are Planner verification tasks per that ADR, and neither changes anything above.

---

## Answers to ADR-029 §12 (Designer → Architect)

ADR-029 asked this handoff four questions and flagged five constraints. Consumed and answered:

**§12.1 Seek — consumed.** Tap-to-seek ships (§A3). Drag-scrub is **deferred for design reasons**, not technical ones — it needs a grabbable thumb that `.now-playing-dock-progress` does not have, and `⟲15` already covers the real need. If it is ever added, the commit-on-release pattern is recorded as the only acceptable implementation. **No architectural change requested.** One nit back: Material Icons has no `_15` glyph (§A2 footnote, **Q13**).

**§12.2 `Preparing` — consumed, and the two-latency point drove a real rule.** §A5 requires the spinner on the next frame after the tap, unconditionally, precisely because the cached path is instant and the uncached one is seconds.

**§12.3 Blackout failures — consumed, and promoted.** This reframed the error state from an edge case into a design centrepiece (§Cross-5). The player owns its own error state; the reconnect banner is explicitly not relied on. New sub-line copy `This usually clears up in a minute. Try again.` sets the expectation a 45% failure rate demands.

**§12.4 / §12.5 Call-stops and single-selection — consumed.** §A4 and §A4b. The single-selection point was the more useful of the two; it is now stated as an IA rule rather than left implicit.

**§12.6 — ⚠ YES. Design the persistent transport; flip the navigate-away rule.**

> **This handoff already contains the affordance your rule was waiting for: §Cross-3, the console-playback chip.** Please treat the navigate-away rule as **overridden** — playback survives navigation.

Rationale, since this flips an architectural decision:

1. The rule's stated justification is *"sound with no visible way to stop it."* The chip removes exactly that: a persistent, one-tap Stop, present on every route, labelled with what is playing.
2. Stopping audio on navigate-away is the **wrong behaviour for this device**. It is a wall panel in a room; the sound is in the room, not in the page. A user who taps play and then glances at the weather has not asked for silence — and on a touch kiosk, incidental navigation is common.
3. **One correction to your suggestion:** you propose `NowPlayingDock` could host it. It should not. The dock is gated on `IsDockVisible => !_isOnHome` (`MainLayout.razor:878`) — **it is absent on Home**, so a dock-hosted stop control would vanish on the app's landing page, which is the worst possible place to lose it. It would also mode-switch a global music transport, so a user reaching for "pause my music" would hit "stop the voicemail." §Cross-2 asks that the dock be left strictly alone; the chip goes in `.topbar-primary`, which is on every route.

The three stop paths in your §7 all remain: explicit stop (now reachable from anywhere), the max-duration cap, and the `CircuitHandler` backstop. Only the navigate-away trigger is removed.

**§12.7 Utterance copy — owned, delivered in §B3.** Your two constraints are honoured verbatim (1000-char cap, URLs → `a link`, emoji dropped). §B3 adds four rules from the live UAT data that the ADR could not have known: **no lead-in when no name resolves** (70% of threads), **strip the MMS `+1XXXXXXXXXX - ` prefix** (finding G-8), **never speak the timestamp**, and **keep digit runs verbatim** (verification codes are the single most valuable thing this feature reads).

**§12.8 Robotic voice — raised, and the design's answer is: keep the local default.**

1. **A different voice is a feature, not a defect.** A system announcement and "the console is reading you a text" are different kinds of event; a different voice makes that legible with zero UI. The mismatch is doing useful work.
2. **The dominant real use case is digit strings.** Per live data the repliable-adjacent traffic is heavy with verification codes (`77971 is your Facebook confirmation code`). A flat, over-articulated engine is *better* at that than a naturalistic one.
3. **Privacy should not be something the owner has to trade away for polish**, especially not silently by inheriting a config default.

**Caveat, and it is a real one:** none of that is worth much against an actual listening test with an older listener in a room with music ducked underneath. Recommend a **30-second UAT step — play one real message on the box and listen** — rather than settling it on argument. Folded into the verification list (item 16b) and left as owner **Q5**.

**§12.9 Reply-ability — consumed.** §C4 tier 1 gates the reply affordance with the existing `.phone-pill` + `You can't reply to this sender.` treatment, not hidden, per ADR-028 §8.5. **One clarification back:** feature B's play button is deliberately **not** gated by reply-ability (§B5) — a short-code thread cannot be replied to but can absolutely be read aloud, and that is the strongest argument for feature B on this device.

**§12.10 Does new-recipient mode survive C? — No. It goes.** §C6 removes `＋ New`, the recipient field, and Screen D entirely. The numeric-keyboard dependency goes with it — `data-keyboard="numeric"` at `PhoneTextsPanel.razor:122` is the repo's **only** consumer, so that count drops to zero. The consequence (the console can no longer start a thread with someone who has not texted it) is real and is escalated to the owner as **Q1**, with `Text back` as the recommended replacement entry point.

---

## Open questions for the owner

**Q1 — `Text back` is now load-bearing (§C6).** Removing `＋ New` means the console can only text people who text it first, unless the long-deferred `Text back` quick action ships. Confirm: **(a)** ship `Text back` alongside C [recommended], **(b)** accept reply-only and revisit later, or **(c)** keep `＋ New` after all, which keeps the numeric keyboard on this surface.

**Q2 — "Identical to voicemail's" (§B1, D-4).** Confirm that "identical" means the **button, the states, and the console behaviour** — not a scrubber, a time readout, or pause inside a message bubble. Note this is now largely settled *above* the design: ADR-029 §8.3 gives spoken text `IsSeekable => false` and no pause because there is no position inside an utterance. The one remaining choice is the optional hairline progress line (§B1).

**Q3 — The new string `Replies are turned off.` (§C4, tier 3a).** This is a new user-visible string and a fifth reason on an owner-approved four-tier gate. It exists to close UAT F-3, and since `SendEnabled=false` today it is **the state the feature ships in**. Confirm the string and its placement at 3a.

**Q4 — Emoji in spoken messages (§B3.6).** Stripping is now required by ADR-029 §12 item 7, so this is no longer a design choice — but the *result* is still worth one live listen, since per-engine emoji handling varies. Rolls into Q5's listening test.

**Q5 — Robotic-but-private, or better-but-cloud (§B4, ADR-029 §14 Q2).** Message speech defaults to local `espeak-ng` so SMS bodies never leave the box; the alternative is one config key (`GvMedia:SpeechEngine = "Google"`) and sending private message bodies to Google. **The design's recommendation is to keep the local default** — reasoning in §Answers item 8 — but settle it by **playing one real message on the box and listening**, not by argument. Verification item 16b.

**Q6 — Six canned replies, or four, or eight (§C3).** Six is the recommendation and the arithmetic is shown so it can be checked rather than trusted. The chain (`.panel-header` + `.phone-mode-selector` heights) **must be measured in the live app** before the count is fixed. Four is the comfortable fallback; eight only if the measurement comes back kinder than estimated.

**Q7 — Owner-editable canned set.** v1 hardcodes the six strings in one array. A Settings surface for editing them is a reasonable fast-follow but is **not** designed here.

**Q8 — Transport feel over Google Cast.** ADR-029 confirms playback *works* on Cast and exclusive-mode outputs — a genuine improvement over browser `<audio>`, which may be silent there. The open bit is narrower: Cast typically buffers seconds, so the **~30–85 ms seek budget does not hold** and the transport may feel disconnected while casting. Allowed as-is, or warned about? **Architect / Tester;** if it needs a UI state, the shape is the §Cross-5 amber pill.

**Q9 — Sleep mode.** Entering Sleep explicitly (the topbar `SLEEP` button) while a voicemail is playing: stop it, or let it finish? Not decided here — the sleep-mode arc has its own handoffs and I will not unilaterally set its semantics. Note ADR-029 §7.1's max-duration cap bounds the worst case regardless.

**Q10 — Unknown voicemail duration (§A5).** `DurationSeconds == 0` means "unknown" in the GV contract and ADR-029 §12 item 1 confirms it must be designed for. Recommendation: **indeterminate bar, `--:--` for the total, elapsed still ticks**, and never render `0:00` as if it were real. Confirm the treatment. *(ADR-029 §14 Q4 separately notes `AudioFileEventSource`'s completion is a wall-clock timer that must be re-armed after a seek — Planner's, not the owner's.)*

**Q11 — There is no `_15` skip glyph (§A2).** Material Icons ships `replay_10`/`replay_30` and `forward_10`/`forward_30`; ADR-029 specifies 15 seconds. Recommendation: use the numeral-free **`fast_rewind` / `fast_forward`** rather than a glyph that says "10" while the button does 15. Alternative: change the interval to 10s and keep the numbered glyphs — also fine, and arguably better for a 42-second voicemail. Owner's pick.

**Q12 — A resolved-name lead-in for voicemail toasts, for symmetry.** Feature B speaks `Message from {Name}.` only when a name resolves. The existing voicemail toast (`New voicemail` / `{caller} · {duration}`) already shows the raw identifier in the 70% case. No change proposed; raised only so the two are consciously consistent rather than accidentally different.

> **Resolved by ADR-029, no longer open:** the stop-speaking route (§3.3/§7.2 give the seam an explicit stop path); whether an incoming call preempts (§6.2 rule 2 — it stops playback); whether seek is achievable (§8.3 — yes, ~30–85 ms); whether playback survives navigation (**flipped by this handoff** — see §Answers item 6).

---

## Verification (for Tester, at 1920×720 on the kiosk)

**Cross-cutting**
1. Start a voicemail; the topbar chip appears, cyan, labelled `Voicemail`. Navigate to `/` — **the chip is still there and playback continues.** Tap it: audio stops, chip disappears.
2. Throughout, the `NowPlayingDock` keeps showing the **music**, unchanged. The music ducks and comes back on its own.
3. Start voicemail A, then tap play on voicemail B: A stops silently, B plays, no toast, no error. Repeat text→voicemail and text→text.
4. Tap the active item's own button: it stops. (Play → stop → play, not a stutter.)
5. Mute the console, then tap play: the `The console is muted.` pill + `Unmute` appears.

**A — voicemail**
6. Skeleton → row → accordion → ▶ → `Fetching recording…` → Playing. The spinner appears **on the next frame** (G-1 must be fixed for this to be observable at all).
7. **Play the same voicemail a second time.** It should be near-instant (cached, ADR-029 §5.3) — and the spinner must *still* flash rather than being suppressed. Two very different latencies, one button.
8. `⟲15` / `15⟳` move playback audibly and the bar follows. Both are disabled in Idle and Preparing.
9. **Tap the progress bar** — it seeks, within ~85 ms, and the bar jumps optimistically on tap. **Drag it** — it should *not* be draggable (deferred; see §A3).
10. **Stop** un-ducks the music and resets to 0. **Pause** holds position and keeps the music ducked; the chip stays.
11. A voicemail whose `DurationSeconds` is 0 shows an **indeterminate** bar and `--:--`, keeps ticking elapsed, takes **no** seek taps, carries `role="progressbar"`, and does **not** report "Ended" while audio is still playing (Q10).
12. **Trigger an inbound call mid-playback.** Voicemail **stops** (does not pause), the transport returns to a clean replayable Idle, the chip disappears, and **no error toast** fires.
13. **The blackout path — run this repeatedly, it is a coin flip.** Force / wait for a 502 → `Couldn't load this recording.` + `This usually clears up in a minute. Try again.` + `Retry`. Confirm the reconnect banner does **not** fire in that window and that the player explains itself anyway. Force an engine failure → `The console can't play this right now.` The two must be **distinguishable**.
14. Start a voicemail, **navigate to `/`, and come back.** Playback continued; re-opening the row shows the **live position**, not 0:00. Open a **second browser** on `/phone` — it shows the same transport, and stopping from either stops the audio.

**B — spoken messages**
15. A play button appears on every **inbound** bubble and on **no outbound** bubble.
16. On a **short-code thread** (e.g. `32665`) the play button is present and works even though the reply slot shows `You can't reply to this sender.`
17. **Listen to one real message end-to-end and judge the voice (Q5).** This is a listening test, not a checkbox: play a real SMS on the box, with music ducked underneath, from across the room. Is the local `espeak-ng` voice acceptable, or is the Google flip worth the privacy trade? Report an opinion, not a pass/fail.
18. Resolved-name thread → speech opens `Message from {Name}.` Unresolved thread → **speech starts with the body; no number is read aloud.**
19. An MMS-prefixed body (`+1919***7670 - …`) is spoken **without** the leading number (G-8). A body with a URL says `a link`, not the URL. An emoji body (`❤️Love you too! ❤️`) does not say "red heart."
20. A digit run (`77971 is your Facebook confirmation code`) is spoken **verbatim** — and is *intelligible* at kiosk distance.
21. The speaking bubble carries the cyan border; **only one bubble is ever marked, anywhere on the surface.** Start a voicemail while a message is speaking: the message stops and its button returns to rest silently (§A4b).

**C — canned replies**
22. With `SendEnabled=false` (today's shipped state): the compose slot shows **`Replies are turned off.`** — a stated reason, not a mystery disabled input (F-3).
23. Flip send on: `Reply` → tray opens (200ms) → the message list scrolls to bottom → tap `OK` → tray closes, `Sending` bubble at 0.6 opacity with a **visible** spinner → `Sent` check. The button stays in one place throughout and reads `Close` while open.
24. **Measure the live pane before fixing the option count (Q6).** Record actual `.panel-header` and `.phone-mode-selector` heights and the resulting `.msg-list` height with the tray open. Confirm the message being replied to is still visible.
25. All four gate tiers, in a thread that qualifies for several at once: reply-ability beats failed-load beats turned-off beats reconnecting. Order per §C4.
26. **A failed load never leaks a bubble** — with the tray reachable, confirm there is no path that appends an optimistic bubble into a `cloud_off` pane (the defect the tier-2 gate exists to prevent).
27. Force a send failure → `.msg-bubble.failed` + `Retry` → retry re-sends the **same canned string**.
28. Confirm `＋ New` is gone, the empty thread list offers no `New message`, and an empty-but-loaded conversation reads **`No messages in this conversation.`**
29. **The virtual keyboard never appears anywhere on `/phone`.** Tap everything.
30. Every touch target measures ≥44px; `Reply` and each `.reply-option` ≥56px. No horizontal scroll; no shell scrollbar; the tray does not push content under the dock.
31. `dotnet build` clean; `dotnet test tests/Radio.Web.Tests` green.

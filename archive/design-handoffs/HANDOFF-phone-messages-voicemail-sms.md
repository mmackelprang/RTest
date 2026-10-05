# Handoff: Phone Messages — Voicemail + SMS + Recent Calls as the main Phone view

- **Status:** Draft for owner review (Designer phase)
- **Date:** 2026-06-20
- **Author:** Designer (Claude)
- **Surface:** `/phone` in `src/Radio.Web` — Blazor Server + Radzen + `design-system.css`
- **Form factor:** wall-mounted kiosk, **1920×720**, touch-first, no guaranteed physical keyboard, glanceable from across a room. 120px topbar → **600px content area**, lists scroll internally, no shell-level vertical scrollbar.
- **Consumes:** RotaryPhone GV bridge REST + SignalR at `http://radio:5004` (contract in `D:/prj/RotaryPhone/docs/handoffs/radioconsole-gv-voicemail-sms-ui-handoff.md`).

This doc is the artifact Planner consumes. It covers the **IA restructure** (Messages becomes the main Phone view; the old panels go on-demand) and fully specs the two new surfaces — **Voicemail** and **Texts** — plus the folded-in **Recent Calls**.

---

## follows / extends / deviates

**FOLLOWS (reused verbatim — do not reinvent):**

- The **Command Surface design system** at `src/Radio.Web/wwwroot/css/design-system.css`. All colour/type/spacing tokens come from its `:root`. **Zero new colours, zero new fonts.**
- The **Phone-surface visual language** in `docs/design-handoffs/design_handoff_phone_page/README.md` and shipped in `design-system.css` §Ph:
  - Left-rail shell — `.phone-shell` / `.phone-tab-rail` / `.phone-rail-heading` / `.phone-rail-tab` / `.phone-rail-label` (design-system.css 4972–5014), as used in `Components/Pages/PhonePage.razor`.
  - `.phone-pill.{green|red|amber|blue|cyan|gray}` (5265–5288) for status/unread markers.
  - `.phone-btn` / `.phone-btn-sm` + variants (`.btn-answer|.btn-hangup|.btn-ghost|.btn-success|.btn-danger|.btn-warn`).
  - `.phone-input` for compose/recipient fields.
  - `.phone-card` / `.phone-card-title` / `.phone-card.accent-{green|cyan|amber|blue}`.
- The **list-row + state primitives**:
  - `.list-item-touch` (582), `.list-item-touch:hover/:active`, `.list-item-touch.list-item-active` (602, cyan left border + accent bg) for the selected row.
  - `.empty-state` / `.empty-state-icon` / `.empty-state-text` (design-system.css §15).
  - `.skeleton` / `.skeleton-list-row` / `.skeleton-list-row-text` (1120–1174) for initial-load.
  - `.list-item-add` animation (1182, `listItemAdd` keyframes) for new-arrival rows.
  - `.transport-btn-primary` (662) for the play/pause control.
  - `.now-playing-dock-progress` + `-bar` (2432–2448, 3px cyan bar) as the basis for the voicemail scrubber.
- The **count badge** — `.nav-badge` (503), the exact class on the topbar Queue pill (`MainLayout.razor:121`).
- The **plumbing** already on `/phone`: typed `*ApiService` REST clients, `*HubService` SignalR with C# events on the existing hub, page-level lifecycle (`PhonePage.razor` OnInitializedAsync 114–164 / Dispose 477–487), Radzen `NotificationService.Notify(...)` toasts mounted in `MainLayout`, and the `/api/gvbridge/status` poll the page already runs.

**EXTENDS (new, built FROM the patterns above):**

- A **Messages landing surface** (new component `PhoneMessagesPanel.razor`) that becomes the **default** content of `/phone`. Assembled entirely from `.list-item-touch` rows + `.phone-pill` + existing skeleton/empty/add primitives.
- A **Voicemail inline player** (in `PhoneVoicemailPanel.razor` or a shared `VoicemailPlayer.razor`) — extends `.now-playing-dock-progress` from display-only into a **seekable** control (new behaviour, same visual).
- A **Texts thread list + conversation** (`PhoneTextsPanel.razor`) with **message bubbles** — a genuinely new primitive, fully specced below from existing tokens (`§Ph bubbles`).
- New `.nav-badge` consumer locations: the **Messages rail tab**, the **topbar `/phone` pill**, and per-section sub-counts.
- A **"More" affordance** on the rail that reveals the demoted panels (Dashboard · Contacts · Dialer · Diagnostics) on demand.
- A **`.gv-reconnect-banner`** (new selector, amber, existing-token-derived alphas) for auth-decay degradation.

**DEVIATES (requires owner direction — flagged, not done unilaterally):**

1. **IA inversion vs. the RotaryPhone-side exploration.** That exploration (`D:/prj/RotaryPhone/docs/design-handoffs/gv-voicemail-sms-radioconsole/overview.md`, "Navigation model") recommended **keeping Dashboard as the landing tab** and adding Voicemail/Texts as two *additional* sub-tabs (rail order `Dashboard · Voicemail · Texts · Contacts · Call History · Diagnostics`). **The owner's directive overrides this**: Messages (voicemail + SMS + recent calls) is the **new main view**, and Dashboard/Contacts/Dialer/Diagnostics are **demoted to on-demand**. This doc follows the owner. The exploration remains the authority on *visual* treatment of voicemail/texts; only the *landing/IA* decision is inverted. **This is the one true deviation and it is owner-directed, not Designer initiative.**
2. **On-screen keyboard for compose** — unresolved platform dependency (no Radzen touch-keyboard exists today; `wwwroot/css/virtual-keyboard.css` is MudBlazor-selectored and unconsumed). Escalated in "Open decisions for the owner" — not a visual deviation.
3. **Heard/read persistence** — v1 is UI-local only (GV mark-read is out of scope on the RotaryPhone side). Escalated as an owner decision, not decided here.

Everything else maps to an existing token or class.

---

## Who uses this & success criteria

One person, fixed 1920×720 kiosk, touch-first, glanceable from across a room, always on. "Grandpa's radio/phone." High bar for legibility and tap-target size; low tolerance for fiddly interaction.

1. Opening the Phone icon lands directly on **what's new** — voicemail, texts, recent calls — in one glance, newest first, with unread clearly marked. No drilling.
2. A new voicemail/text **announces itself** (toast + persistent rail/topbar badge) **without stealing the screen or pausing music**.
3. Listen to a voicemail + read its transcript in **≤2 taps** (row → inline player), scrubber works on first play (proxied/cached audio → brief fetch spinner).
4. Read a thread and (when send ships) reply with large tap targets and honest send feedback.
5. The old panels (Dashboard/Contacts/Dialer/Diagnostics) are **still reachable in one tap** via a clear "More" affordance — demoted, not buried.
6. Fits the 600px content area, no shell scrollbar. **Zero new tokens; zero drift.** Polisher finds nothing.

---

## IA decision (the core job)

### Decision 1 — Messages is a **unified, sectioned feed**, not three sub-tabs and not a fully-interleaved stream

**Recommendation: a single Messages surface organized as a `[All · Voicemail · Texts · Calls]` segmented filter over one chronologically-sorted feed**, defaulting to **All** (interleaved by time). It is one scroll region, one mental model, glanceable.

Three candidates were weighed:

| Option | What it is | Verdict |
|---|---|---|
| **A. Fully-interleaved single feed, no filter** | Voicemail, SMS, and call rows merged strictly by timestamp, one list. | Best glanceability; but no way to say "just show me my texts," and a chatty SMS thread buries voicemail. **Rejected as the sole mode.** |
| **B. Three hard sub-tabs** (Voicemail / Texts / Calls) | Pick one stream at a time. | Loses the "what's new across everything" glance the owner is asking for; reintroduces drilling. This is essentially the rejected old IA with new labels. **Rejected.** |
| **C. Unified feed + segmented filter** ✅ | One feed, default **All** (interleaved newest-first), with a top segmented control `All · Voicemail · Texts · Calls` to narrow. Unread counts live on the segments. | Keeps the single-glance "what's new" **and** lets the user focus one stream. Matches the kiosk's "everything on one screen" ethos. **Recommended.** |

**Why C for this kiosk + glanceability:**
- The **default All view** answers "what happened on my phone?" in one look — exactly the owner's ask — without forcing a tab choice.
- The **segmented filter** uses the established Phone segmented-selector pattern (the Dashboard's Active-Mode selector, `.mode-selector`/`.mode-btn`), so it's a *reused* interaction, not a new one.
- Each row type is **visually self-identifying** by its leading icon-chip colour + a tiny type label, so an interleaved list never confuses (spec below). This is what makes the merged feed legible rather than soupy.
- Filtering is **client-side** over already-loaded data — no extra round trips, instant on a touch tap.

> **Texts caveat — feed row vs. conversation.** In the feed, a text appears as a **thread row** (one row per conversation, newest activity, last-message preview + unread dot), *not* one row per message — otherwise a single chatty thread floods the feed. Tapping a text row opens the **conversation view** (master-detail, see Screen C). Voicemail and call rows are atomic (one event = one row).

### Decision 2 — On-demand access to the demoted panels: a **"More" rail tab that expands a secondary list**

The old panels stay on the **same left rail**, but collapsed under one entry so Messages owns the primary real estate.

**New rail (top→bottom):**

```
Phone                  ← .phone-rail-heading
● Messages   [badge]   ← NEW, default-active; .nav-badge sums unheard VM + unread texts
─────────────          ← hairline divider (--surface-separator)
  More ▸               ← .phone-rail-tab; expands the secondary group below
    Dashboard          ← shown only when "More" is expanded
    Contacts
    Dialer*            ← was "Call History"; the dialer/history panel
    Diagnostics
```

- **Messages** is the default active tab (`_activeTab = "messages"`), replacing today's `_activeTab = "dashboard"` default in `PhonePage.razor:88`.
- **More** is a `.phone-rail-tab` that toggles a `bool _moreExpanded`. Collapsed by default. When expanded it reveals the four legacy tabs *inset/indented* under it (same `.phone-rail-tab` style, smaller label, a left indent to read as a sub-group). Tapping any of them sets `_activeTab` to that panel and renders the **existing, unchanged** `PhoneDashboardPanel` / `PhoneContactsPanel` / `PhoneHistoryPanel` / `PhoneDiagnosticsPanel`. No rewrite of those panels — only their entry point moves.
- **Recent Calls is NOT under More.** Call history is *folded into the Messages feed* (the owner's explicit ask). The legacy `PhoneHistoryPanel` (which also contains the dialer/dev-dial path) stays reachable under More as **"Dialer"** for placing calls / clearing history / the full table view — but the *recent-calls glance* now lives in Messages.
- **Why a "More" group, not a hamburger or a back-to-classic route:** the rail already supports arbitrary `.phone-rail-tab` buttons (zero new layout). A hamburger/overflow menu would add an opaque popover with no glance value; a separate `/phone/classic` route would split page lifecycle and SignalR. The expand-in-place "More" keeps one page, one hub, one mental model, and the demoted panels are always one tap (expand) + one tap (panel) away — call it "two taps, always visible."

**Alternative (NOT chosen, owner override to adopt):** keep the four legacy tabs flat on the rail *below* Messages (no "More" wrapper). Simpler, but the rail then reads as six co-equal destinations and dilutes "Messages is the main view." Choose this only if the owner finds the expand interaction fussy. Noted in Open Decisions.

### Decision 3 — Unread badge model (see full spec in "Badge model" below)

Badges live in three places: the **topbar `/phone` pill** (global awareness), the **Messages rail tab** (on-page awareness), and the **segmented-filter sections** (per-stream). All reuse `.nav-badge`.

---

## Layout — Messages main view at 1920×720

`.phone-shell` = rail (156px) + content. Content = 600px tall. Messages defaults to the **All** feed. Because tapping a text row needs the conversation, Messages uses a **master-detail split** sized so the feed is primary and the detail pane is contextual.

```
┌─ rail 156px ─┬──────────────── feed (1fr, min 560px) ───────────────┬──── detail (520px) ─────┐
│ Phone        │ ┌ panel-header ───────────────────────────────────┐ │ ┌ detail pane ────────┐ │
│              │ │ MESSAGES                                  [↻]   │ │ │                     │ │
│ ● Messages 5 │ │ ┌ segmented filter ───────────────────────────┐ │ │ │   (contextual)      │ │
│ ──────────   │ │ │[ All ][Voicemail 3][Texts 2][Calls]         │ │ │ │                     │ │
│   More ▸     │ │ └─────────────────────────────────────────────┘ │ │ │  • empty: hint      │ │
│              │ ├─────────────────────────────────────────────────┤ │ │  • text row → conv  │ │
│              │ │ 🎙●  Jane Appleseed     vmail  9:41a  "Hey…"  ▸ │ │ │  • vmail row →      │ │
│              │ │ 💬●  Mom                text   9:12a  Did you…▸ │ │ │    player can also  │ │
│              │ │ 📞   Dr. Smith (missed) call   8:55a          ▸ │ │ │    expand inline    │ │
│              │ │ 🎙   +1 919 555 0123   vmail   Mon    0:08    ▸ │ │ │    (see Screen B)   │ │
│              │ │ 💬   +1 800 555 4471   text   Mon    Your co…▸ │ │ │                     │ │
│              │ │ 📞↗  Aunt Carol        call   Sun    4:12     ▸ │ │ │                     │ │
│              │ │ … (scrolls internally)                          │ │ │                     │ │
│              │ └─────────────────────────────────────────────────┘ │ └─────────────────────┘ │
└──────────────┴─────────────────────────────────────────────────────┴─────────────────────────┘
```

- **Panel header:** `.panel-header` title "MESSAGES" + a refresh `.phone-btn-sm` ghost icon-button (`aria-label="Refresh messages"`).
- **Segmented filter:** the `.mode-selector`/`.mode-btn` pattern (the same control the Dashboard uses for Active Mode). Buttons: `All · Voicemail · Texts · Calls`. Active segment = `--accent-dim` bg + `--accent-primary` text + 1px inset accent border. Voicemail and Texts segments carry an inline unheard/unread `.phone-pill.cyan` count when >0; All and Calls do not.
- **Feed rows:** each is a `.list-item-touch` (min-height 56px). Universal row grid:
  `[type icon-chip 44px][unread dot 8px (if unread)][1fr: title (caller/contact, --text-high 600 when unread else --text-medium) + preview/subtitle (1 line, ellipsis, --text-medium)][type label mono --text-low][timestamp/duration mono right][chevron ▸]`.
  - **Type icon-chip colour-codes the stream** (reuses the hero icon-chip treatment, `color-mix` tint of the accent):
    - Voicemail → `voicemail` icon, cyan tint (`--accent-primary`).
    - Text → `chat_bubble` / `sms` icon, cyan tint.
    - Call → direction icon (`call_received` answered = `--signal-green`, `call_missed` = `--signal-red`, `call_made` = `--signal-blue`), reusing `PhoneHistoryPanel`'s exact `GetCallDirectionIcon` / `GetCallDirectionColor` logic (lines 89–103).
  - **Type label** (`vmail` / `text` / `call`) in `--font-mono` `--text-low` 10px uppercase — the tiebreaker that keeps the interleaved list unambiguous. (Hidden in single-stream filter views, redundant there.)
  - **Caller/contact name resolution** mirrors the existing client-side suffix match used in `PhoneStatusHero` (last-10-digits) and `PhoneHistoryPanel` (`PhoneNumberNormalizer.Normalize`): resolved contact → DTO name (`FromName`/`counterpartyName`/`CallerName`) → formatted E.164.
- **Detail pane (520px):** contextual, reused across stream types.
  - **Empty (nothing selected):** `.empty-state` hint — icon `forum`, "Pick a message to open it here."
  - **Text row tapped →** conversation view (Screen C, right pane content) renders here.
  - **Voicemail row tapped →** the inline player (Screen B) can render **either** as an accordion under the row **or** in this detail pane. **Decision: accordion under the row** (transcript wants the feed's full reading width and keeps the list visible) — the detail pane stays the *texts conversation* host. See Screen B note.
  - **Call row tapped →** a lightweight call-detail card in the pane: caller, number (mono), direction, when, duration, answered-on pill, and a `[📞 Call back]` action (hands E.164 to the existing dial path) + `[💬 Text back]` (opens the Texts conversation for that number).

> **Width rationale:** at 1920 the feed gets the lion's share (≈1fr ≈ 1240px after the rail) and the detail pane is a fixed 520px — wide enough for a readable conversation, narrow enough that the feed stays the star. When the **Texts** filter is active and a thread is open, this *is* the canonical SMS master-detail (Screen C). When **All/Voicemail/Calls** is active, the pane holds the call-detail or empty hint, and voicemail uses the accordion.

---

## Screen A — Voicemail (list + states)

Filter = **Voicemail**, or voicemail rows within **All**. Single-column feed rows, newest first (`receivedAt` desc). Unread = leading 8px cyan `.unread-dot` + bold name; heard = no dot, dimmer.

**Row:** `[🎙 cyan chip 44px][unread dot][1fr: caller (title) + transcript first-line preview (subtitle, 1 line ellipsis)][0:42 duration mono][▸]`.

| State | Trigger | What renders |
|---|---|---|
| **Loading (initial)** | First fetch, no cache | `.skeleton` block of ~5 `.skeleton-list-row` (thumb + two text lines). NOT a centered spinner. |
| **Loaded** | ≥1 item | Row list, `receivedAt` desc. Unread marked by dot, not by reorder. |
| **Empty** | 0 items | `.empty-state` — icon `voicemail`, text **"No voicemails."** |
| **Error (initial)** | Fetch threw / non-200 | `.empty-state` error variant — icon `cloud_off`, **"Couldn't load voicemail."** + `Retry` (`.phone-btn-sm`). |
| **Refresh error (list already loaded)** | Transient refresh fails | Keep last good list; **no blank, no banner** — toast `Warning` "Couldn't refresh. Showing the last update." |
| **Refreshing (manual)** | Header refresh tapped | Icon spins (`.spinner`); list stays interactive; replace on success. |
| **New arrival (push)** | `VoicemailReceived` | New row animates in at top (`.list-item-add`, slide from left); unread badge ++; toast (see Notifications). |

**Defensive rendering:**
- `transcript == null` → preview shows **"Transcript pending…"** (recent) or **"No transcript available."** (not recent / disabled) per the player rule, not a blank.
- `durationSeconds == 0` → render **"—"** (or omit), never "0:00".
- `receivedAt` UTC ISO-8601 → format local, relative-smart (see Timestamp rule).

---

## Screen B — Voicemail player (inline accordion)

Expands **under the tapped row** inside the feed (not a route, not the detail rail — transcript wants full reading width and keeps the list visible). Opening any row collapses any other open player.

```
│ 🎙 ⊙ Jane Appleseed          vmail  9:41a   "Hey, calling about…"        ▾ │
│ ┌──────────────────────────────────────────────────────────────────────┐ │
│ │  (▶)  ──────●─────────────────────────────  0:14 / 0:42               │ │  ← transport + seekable scrubber
│ │                                                                        │ │
│ │  Transcript                                                            │ │
│ │  Hey, calling about the thing on Saturday — give me a ring back when  │ │
│ │  you get a sec. Thanks, bye.                                          │ │
│ │                                                                        │ │
│ │  [📞 Call back]   [💬 Text back]                                       │ │  ← optional quick actions (owner Q)
│ └──────────────────────────────────────────────────────────────────────┘ │
```

- **Transport:** circular `.transport-btn-primary` play/pause on the left.
- **Scrubber:** `.now-playing-dock-progress` + `-bar`, **made seekable** — pointer/touch sets `<audio>.currentTime` from the tap x-fraction. Range support (server `Accept-Ranges: bytes`) makes seek work without re-download. **Hit area ≥24px tall** even though the visual bar is 3px — pad the hit region. Time readout right of the bar in `--font-mono` tabular-nums: `0:14 / 0:42` (mirror `.now-playing-dock-elapsed` / `-total`).
- **Audio element:** `<audio src="http://radio:5004/api/gvbridge/voicemail/{id}/audio">`.

| State | Trigger | What renders |
|---|---|---|
| **Idle / ready** | Player opened, audio not yet requested | Play ▶ enabled, scrubber at 0, time `0:00 / {duration}` (duration from `durationSeconds`, no fetch needed). If `durationSeconds == 0`, show `0:00 / --:--` and don't pretend the total is known. |
| **Buffering (first play)** | ▶ tapped, server fetching from Google | Play button shows a small `.spinner` in place of the glyph; scrubber inert; optional sub-line **"Fetching recording…"**. A 0.5–few-sec stall is expected — must read as intentional. |
| **Playing** | Audio playing | Pause ❚❚; scrubber advances (`<audio>` `timeupdate`); time ticks. |
| **Paused** | Pause tapped | Play glyph returns; position held. |
| **Ended** | Reached end | Reset to ▶ at position 0 (re-listen ease). |
| **Audio error** | `<audio>` `error` / proxy **502** | Replace transport with an inline error row: icon `error_outline` + **"Couldn't load this recording."** + `Retry` (`.phone-btn-sm`). Toast `Error` "Playback failed — Couldn't load this recording. Try again." |
| **Transcript present** | `transcript` non-null/non-empty | Body text, `--text-high`, wraps, scrolls with panel. Heading **"Transcript"**. |
| **Transcript pending** | `transcript == null` AND recent | Italic `--text-medium`: **"Transcript pending — Google is still transcribing this voicemail."** Mark `aria-live="polite"` so a later push announces it. |
| **Transcript absent** | `transcript == null` AND not recent / disabled | `--text-low`: **"No transcript available."** |

- **Mark-heard:** opening the player (or play start) flips the row to **heard locally** and decrements the unread badge. **GV-side mark-read is out of scope v1 → heard is UI-local only.** (Owner decision below.)
- **Quick actions (optional):** `Call back` hands the E.164 to the existing dial path; `Text back` opens the Texts conversation pre-targeted to that number. Owner decision whether to ship in v1.

**Keyboard / SR:** play/pause is a real `<button>` (`aria-label="Play voicemail from {caller}"` / `"Pause"`). Scrubber `role="slider"`, `aria-valuemin=0`, `aria-valuemax={durationSeconds}`, `aria-valuenow`, `aria-label="Playback position"`. Transcript is plain DOM text.

---

## Screen C — Texts (thread list + conversation)

Filter = **Texts** → feed becomes the **thread list**; tapping a thread opens the **conversation** in the 520px detail pane. This is the SMS master-detail.

```
┌─ rail ──┬──────── threads (1fr) ────────┬─────────── conversation (520px) ───────────┐
│ …       │ TEXTS              [2]  [＋ New]│ ◂  Jane Appleseed                          │
│ ●Messages│ ─────────────────────────────│    +1 919 555 0123                         │
│   More ▸ │ ● Jane Appleseed       9:41a  │ ─────────────────────────────────────────│
│         │   Sounds good, see you then ▸ │                            ┌────────────┐  │ ← outbound (right, cyan)
│         │   Mom               Yesterday │                            │ On my way  │  │
│         │   You: Did you eat?         ▸ │                            └────────────┘  │
│         │   +1 800 …          Mon        │ ┌────────────┐                            │ ← inbound (left, raised)
│         │   Your code is 4471…        ▸ │ │ Sounds good │                            │
│         │                               │ └────────────┘                            │
│         │                               │ ─────────────────────────────────────────│
│         │                               │ [ Message…                    ] [ Send ]  │ ← compose bar (flagged)
└─────────┴───────────────────────────────┴───────────────────────────────────────────┘
```

**Thread list (feed pane):**
- `.panel-header` "TEXTS" + unread-thread-count `.phone-pill.cyan` + **"＋ New"** (`.phone-btn-sm`, opens Screen D).
- Each thread row: `.list-item-touch` → `[unread dot][💬 chip 44px][1fr: name (title) + last-message preview (subtitle, 1 line ellipsis; prefix outbound previews with "You: ")][timestamp mono right][▸]`. Selected thread → `.list-item-touch.list-item-active`.
- Newest activity first (`lastMessageAt` desc). Unread = dot + `--text-high` 600 name + `--text-medium` preview. Read = dimmer, no dot.

| Thread-list state | Trigger | Renders |
|---|---|---|
| **Loading** | First fetch | `.skeleton` list ~6 rows. |
| **Loaded** | ≥1 thread | Thread rows. |
| **Empty** | 0 threads | `.empty-state` — icon `forum`, **"No conversations yet."** + (if compose ships) **"New message"** `.phone-btn-sm`. |
| **Error** | Fetch failed | `.empty-state` error + `Retry`; keep last good list on transient refresh (toast warning). |
| **New inbound (push), thread NOT open** | `SmsReceived` | Thread bumps to top (`.list-item-add`); unread dot; badge ++; toast. |
| **New inbound (push), thread OPEN** | `SmsReceived` for open thread | **Append bubble in place**; do NOT mark unread; **do NOT toast**; bump thread to top, keep selected. |

**Conversation (detail pane):**
- **Header:** back chevron (`aria-label="Back to conversations"`; at 1920 both panes always visible, back is for completeness) + contact name (title) + E.164 sub-line (mono, `--text-medium`).
- **Message region:** `.msg-list`, bubbles newest at bottom, auto-scrolls to bottom on open and on new message. Day separators (`.msg-day-sep`) between dates. `aria-live="polite"` so inbound announces.
- **Bubbles** (`§Ph bubbles` spec below): inbound left on `.surface-raised`; outbound right on `--accent-surface` cyan tint. Outbound carries a status glyph (sending spinner / sent check / failed ⚠).
- **Compose bar (FEATURE-FLAGGED OFF):** `.phone-input` (grows) + `.phone-btn-sm` Send (disabled empty/sending). Pinned to pane bottom. See Compose spec.

**Outbound write-path bubble states (flagged):**

| State | Trigger | Bubble |
|---|---|---|
| **Sending** | Send tapped; `POST /sms/send` in flight | Optimistic bubble appears immediately, right-aligned, `.msg-bubble.sending` (opacity 0.6), `.spinner` in the meta slot. Input clears; Send disabled until response. |
| **Sent (queued)** | 200 (`Queued == true`) | Full opacity; status glyph = single check (`done`, `--text-low`). 200 = accepted by GV, confirmed when poll/push re-surfaces it. |
| **Confirmed** | Same message returns via `SmsReceived`/poll | De-dupe against the optimistic bubble (match text + recency, or returned id); collapse to one. No visual jump. |
| **Failed** | non-200 / `Queued == false` / timeout | `.msg-bubble.failed` (red left edge), `error_outline` glyph + **"Failed to send"** sub-line + Retry (whole bubble is a ≥48px tap target). Toast `Error` per error matrix. **Text preserved. Never auto-retry.** |
| **Inbound** | `SmsReceived` / history, `direction=="Inbound"` (and **unknown direction → treat as Inbound**) | Left `.surface-raised` bubble, no status glyph, timestamp below; animates in `.list-item-add` / `slideInUp`. |

**Defensive:** `text == null` → render an empty-but-present bubble with a `--text-low` placeholder "(no text)" rather than crashing; `direction` not exactly `"Outbound"` → inbound; `sentAt` UTC → local.

---

## Screen D — New-recipient composer

Triggered by **"＋ New"** in the Texts header. Replaces the conversation pane.

```
│ NEW MESSAGE                                                     │
│ ┌────────────────────────────────────────────────────────────┐ │
│ │ To:  [ Phone number                          ] [Pick…]     │ │ ← recipient field (+ optional contact pick)
│ ├────────────────────────────────────────────────────────────┤ │
│ │  Start the conversation below.                             │ │ ← empty message region
│ ├────────────────────────────────────────────────────────────┤ │
│ │ [ Message…                                   ] [ Send ]    │ │ ← same compose bar (flagged)
│ └────────────────────────────────────────────────────────────┘ │
```

- **Recipient field:** `.phone-input`, placeholder **"Phone number"**, `aria-label="Recipient phone number"`. Optional **"Pick contact"** affordance reusing the existing contacts list (nice-to-have; number-entry is the floor).
- **Empty message region:** `.empty-state`-style hint **"Start the conversation below."**
- **Validation:** don't pre-validate aggressively client-side — block only obviously-empty/non-numeric; let RotaryPhone normalize to E.164. On a normalization failure, inline under the field: **"Enter a valid phone number."**
- **On success:** the composer transitions into the normal conversation view for the resolved thread (RotaryPhone returns/poller surfaces the thread).

| State | Renders |
|---|---|
| **Recipient empty** | Recipient placeholder; Send disabled until both recipient and message non-empty. |
| **Recipient invalid** | Inline error under field; keep typed text. |
| **Sending → sent** | Same as conversation send states; on success → conversation view. |

---

## Screen E — Recent Calls (folded into the feed)

Per the owner's directive, the recent-calls *glance* lives **inside the Messages feed** (filter **Calls**, or interleaved in **All**) — it is not a separate landing tab. The full call-history table + clear-history + dialer remain reachable under **More → Dialer** (the existing `PhoneHistoryPanel`, unchanged).

**Call row (in feed):** `[direction chip 44px][1fr: caller (title) + "Incoming/Outgoing/Missed" sub (mono 10px)][call type label][when mono][duration mono][▸]`.
- Direction chip icon + colour reuse `PhoneHistoryPanel.GetCallDirectionIcon` / `GetCallDirectionColor` **verbatim** (answered-incoming `call_received` green, missed `call_missed` red, outgoing `call_made` blue).
- **Tap a call row →** the call-detail card in the detail pane: caller, number (mono), direction, when (local), duration (`PhoneHistoryPanel.FormatDuration` logic), answered-on `.phone-pill` (`Rotary` amber / `GV` cyan / missed `—` gray), and actions **`[📞 Call back]`** (existing dial path) + **`[💬 Text back]`** (opens Texts conversation for that number).
- **No unread concept for calls** in v1 (missed-call badging is a possible fast-follow — noted in Open Decisions). Missed calls are visually flagged by the red `call_missed` chip, not a dot.

States mirror the list pattern: loading (skeleton), loaded, empty (`.empty-state` icon `call`, "No recent calls."), error + Retry. Call rows arrive from the existing `PhoneApi.GetCallHistoryAsync()` + the `CallHistoryUpdated` SignalR event already wired in `PhonePage.razor` (252–261) — no new data path.

---

## New-arrival notifications (cross-cutting)

**HARD RULE: a new arrival must NEVER steal the screen or pause audio.** No modal, no full-screen, no audio chime from the web layer (the box already does call-ring TTS in the Radio.API audio layer; the text/voicemail UI stays silent). Three coordinated non-modal signals:

1. **Toast (transient).** On `SmsReceived` / `VoicemailReceived`, fire `NotificationService.Notify(...)`:
   - SMS: `NotificationSeverity.Info`, title `{contact or formatted number}`, body `{message preview, truncated}`.
   - Voicemail: `NotificationSeverity.Info`, title **"New voicemail"**, body `{caller} · {duration}`.
   - Severity **Info** (blue) — good news, low urgency. Auto-dismiss (Radzen default ~5s). Stacks at the existing `<RadzenComponents />` mount, never blocks touch.
   - **Suppress the toast** if the user is currently viewing that exact thread/voicemail (append in place instead).
2. **Persistent unread badge (until addressed).** See Badge model.
3. **Inbox bump + in-place append.** The feed reorders (new item to top, `.list-item-add`); if the conversation/voicemail is open, content appears in place (`aria-live`). No navigation forced.

| Condition | Behaviour |
|---|---|
| Push arrives, foreground, not on relevant item | Toast + badge + bump. |
| Push arrives, user on the exact thread/voicemail | Append in place; badge NOT incremented; toast suppressed. |
| Push during a SignalR reconnect gap | Backstop poll reconciles on reconnect; badge/list catch up; **no toast for the catch-up batch** (avoid a toast storm) — only live pushes toast. |
| Burst of pushes | Toasts stack but cap (Radzen handles); badge reflects true count. Collapsing >3 simultaneous into one "{n} new messages" toast is a nice-to-have. |

> An *audible* new-text chime, if ever wanted, belongs in the Radio.API audio layer (ducking-aware), not Blazor. Flagged as future work, not v1.

---

## Badge model

All badges reuse `.nav-badge` (amber pill, `--font-mono`, `--text-inverse`) — the exact class on the topbar Queue pill (`MainLayout.razor:121`).

| Location | Shows | Source | Clears when |
|---|---|---|---|
| **Topbar `/phone` pill** (`MainLayout.razor:140–143`) | `unheardVoicemail + unreadThreads` (single sum) — global awareness off the `/phone` page | live counts pushed/polled; needs the count surfaced to `MainLayout` (a small shared state/service — Architect's call on mechanism) | as items are heard/read on `/phone` |
| **Messages rail tab** | same sum as topbar (on-page mirror) | same | same |
| **Segmented filter — Voicemail** | unheard voicemail count (`.phone-pill.cyan` inline on the segment) | voicemail list `isRead==false` count, live-adjusted | per voicemail opened/played |
| **Segmented filter — Texts** | unread *thread* count (`.phone-pill.cyan`) | threads `hasUnread==true` count | per thread opened |
| **Segmented filter — All / Calls** | no badge | — | — |
| **Per-row unread dot** | `.unread-dot` (8px cyan) on unheard VM / unread text rows | per-item flag | item opened |

Rules:
- **Counts are UI-local truth in v1** (heard/read does not persist to GV). A reload re-derives from `isRead`/`hasUnread`, so an item the user "heard" locally but didn't actually mark on GV may reappear unread after a hard reload — acceptable per the v1 scope, flagged in Open Decisions.
- **Calls do not contribute to badges in v1** (no missed-call unread concept yet). If the owner wants missed-call badging, it's a fast-follow.
- Badge is `aria-hidden="true"` (matching the Queue pill); the accessible count goes on the pill's `aria-label`, e.g. `aria-label="Phone, 5 unread"`.

---

## Auth-decay degradation (GV reconnecting)

Driven by the **already-polled** `/api/gvbridge/status` (`Available` / `CookiesValid`) that `/phone` consumes today (`PhonePage.razor` GvBridge status, ~10s effective cadence). No new awareness mechanism.

- **Calm banner** at the top of the Messages content (above the segmented filter), spanning the feed: `.gv-reconnect-banner` — **amber, non-blocking, NOT red**. Text: **"Google Voice is reconnecting — voicemail and texts may be delayed."**
- While shown: compose **Send disabled** with a **"Texting unavailable"** `.phone-pill` + tooltip **"Google Voice is reconnecting."** Don't let the user type into a dead send path.
- **Auto-clears** on recovery (`Available==true`); Send re-enables. RotaryPhone does the actual cookie recovery; the UI only reflects state.

```css
/* §Ph — GV reconnecting banner (existing-token-derived alphas, no new :root entry) */
.gv-reconnect-banner {
  display: flex; align-items: center; gap: var(--sp-2);
  padding: var(--sp-2) var(--sp-4);
  background: rgba(240,168,48,0.10);          /* matches .phone-pill.amber bg */
  border-bottom: 1px solid var(--surface-separator);
  color: var(--signal-amber);
  font-family: var(--font-body);
  font-size: 13px;
}
```

**Send-failure copy matrix** (user copy non-technical; log the raw error):

| Condition | Failed-bubble sub-line | Toast body |
|---|---|---|
| Generic non-200 | `Failed to send` | `Couldn't send your message. Try again.` |
| Auth decay (GV reconnecting) | `Failed to send` | `Couldn't send — Google Voice needs to reconnect. Try again shortly.` |
| Invalid number (INVALID_ARGUMENT) | `Invalid number` | `That number doesn't look right. Check it and try again.` |
| Rate-limited (429) | `Failed to send` | `Sending too fast — wait a moment.` |
| Timeout / network | `Failed to send` | `No response — check the connection and try again.` |

---

## Compose / on-screen keyboard spec (FEATURE-FLAGGED OFF)

Built in full, shipped behind a flag, network call stubbed (no `POST /api/gvbridge/sms/send` exists yet; when it ships, request ≈ `{ "threadId": "...", "text": "..." }` → returns the created outbound message).

**Compose bar states:**

| State | Appearance |
|---|---|
| **Empty** | `.phone-input` placeholder **"Message"**; Send `.phone-btn-sm` disabled (`:disabled` opacity 0.35). |
| **Has text** | Send enabled (cyan). Segment counter appears once long. |
| **Sending** | Input cleared + briefly disabled; Send shows spinner; re-enabled on response. |
| **Rate-limited (429)** | Toast "Sending too fast — wait a moment."; re-enable after a short cooldown; **keep the text**. |
| **GV unavailable** | Send disabled + "Texting unavailable" pill + tooltip; don't let the user type into a dead path. |

**Character / SMS-segment counter:** hidden until ~120 chars; then `{chars} · {n} SMS` in `--font-mono` `--text-low` (e.g. `161 · 2 SMS`). Simple GSM-7 vs UCS-2 heuristic (any non-ASCII → 70-char boundary). Informational only; nice-to-have if it complicates the send PR.

**On-screen keyboard — recommendation:** the kiosk is touch-first with no guaranteed physical keyboard, and tapping `.phone-input` must summon text entry. **Build a touch keyboard component** (the handoff's recommended option):
- A Radzen-native on-screen keyboard mounted as a bottom sheet within the conversation/composer pane, wired to the focused `.phone-input`. Re-skin the existing `wwwroot/css/virtual-keyboard.css` off its MudBlazor selectors (`.mud-overlay .mud-paper`) onto a `.virtual-keyboard-container` + Radzen markup (the file exists but is unconsumed and MudBlazor-selectored today).
- Keys ≥48px (`--touch-min`); QWERTY + a numeric/symbol layer (numeric layer is what the new-recipient field needs); `space`, `backspace`, `return`/send.
- It is the **only** thing blocking compose on the kiosk; the entire **read** experience (voicemail listen + transcript, thread read, recent calls) has **no keyboard dependency** and ships first.
- **This is an owner/Architect decision to confirm** (build vs. assume-physical-keyboard vs. defer-compose). See Open Decisions.

**Send guardrails (when send ships):** disable Send while in flight; 429 → "Sending too fast — wait a moment," keep text; **never auto-retry**; retry is user-initiated only.

**Keyboard/touch/SR for compose:** Send target ≥48px. On a hardware keyboard (if present), `Enter` sends, `Shift+Enter` newline, focus returns to input after send (matches the dev-tray dial input's Enter-to-act convention). On pure touch, **do not auto-focus** the input on thread-open (it would pop the on-screen keyboard unbidden). Compose input `aria-label="Type a message"`.

---

## §Ph — message bubble spec (NEW primitive, existing tokens only)

To be added to `design-system.css` §Ph (where `.phone-card`/`.phone-pill` live), following the `phone-`/`§Ph` naming discipline. **No new colour/font/spacing tokens.**

```css
/* -- §Ph  Text message bubbles --------------------------------------------- */
.msg-list {
  display: flex; flex-direction: column;
  gap: var(--sp-2);                 /* 8px */
  padding: var(--sp-3) var(--sp-4); /* 12 / 16 */
  overflow-y: auto; min-height: 0;
}
.msg-bubble {
  max-width: 72%;                    /* layout dim — no :root equiv, see note */
  padding: var(--sp-2) var(--sp-3);  /* 8 / 12 */
  border-radius: 14px;
  font-family: var(--font-body); font-size: 15px; line-height: 1.4;
  color: var(--text-high); word-break: break-word;
}
.msg-bubble.inbound {
  align-self: flex-start;
  background: var(--surface-raised);
  border: 1px solid var(--surface-separator);
  border-bottom-left-radius: 4px;
}
.msg-bubble.outbound {
  align-self: flex-end;
  background: var(--accent-surface);           /* rgba(92,212,232,0.08) */
  border: 1px solid rgba(92,212,232,0.20);     /* derived from --accent-primary */
  border-bottom-right-radius: 4px;
}
.msg-bubble.sending { opacity: 0.6; }
.msg-bubble.failed {
  border-color: var(--signal-red);
  border-left: 3px solid var(--signal-red);
  cursor: pointer; min-height: var(--touch-min);   /* whole bubble = 48px retry target */
}
.msg-meta {
  display: flex; align-items: center; gap: 6px; margin-top: 4px;
  font-family: var(--font-mono); font-size: 11px; color: var(--text-low);
}
.msg-meta .msg-status-sent { color: var(--text-low); }     /* done glyph */
.msg-meta .msg-status-fail { color: var(--signal-red); }   /* error_outline + "Failed to send" */
.msg-day-sep {
  align-self: center; margin: var(--sp-2) 0;
  font-family: var(--font-mono); font-size: 11px;
  letter-spacing: 0.08em; text-transform: uppercase; color: var(--text-low);
}
.unread-dot {
  width: 8px; height: 8px; border-radius: 50%;
  background: var(--accent-primary);
  box-shadow: 0 0 6px var(--accent-glow);   /* existing token */
  flex-shrink: 0;
}
```

New bubbles animate in via existing `.list-item-add` (`listItemAdd` keyframes) or `slideInUp`. Reduced-motion handled globally. **The one non-token value:** `max-width: 72%` + the `14px`/`4px` radii are layout dimensions (no `:root` equivalent; existing `.phone-card` hardcodes its radius too) — consistent with house style, no owner sign-off needed.

**Rail-tab badge & topbar pill badge** reuse `.nav-badge` **verbatim** — no new CSS; mount absolute top-right inside the `.phone-rail-tab` and inside the topbar `.nav-pill` exactly as the Queue pill does.

---

## Copy strings (consolidated)

**Voicemail:** tab/header `Voicemail` / `VOICEMAIL`; unheard pill `{n} unheard`; empty `No voicemails.`; load error `Couldn't load voicemail.` + `Retry`; audio error `Couldn't load this recording.` + `Retry`; buffering `Fetching recording…`; transcript heading `Transcript`; pending `Transcript pending — Google is still transcribing this voicemail.`; absent `No transcript available.`; quick actions `Call back` · `Text back`; new-VM toast title `New voicemail`, body `{caller} · {duration}`.

**Texts:** header `TEXTS`; new button `＋ New`; empty `No conversations yet.`; new-composer empty `Start the conversation below.`; load error `Couldn't load conversations.` + `Retry`; compose placeholder `Message`; send `Send`; segment counter `{chars} · {n} SMS`; texting-unavailable pill `Texting unavailable`; recipient placeholder `Phone number`; invalid recipient `Enter a valid phone number.`; new-text toast title `{contact or number}`, body `{preview}`; send-failed toast `Message not sent` / `Couldn't send your message. Try again.`; rate-limit toast `Slow down` / `Sending too fast — wait a moment.`

**Calls (in feed):** empty `No recent calls.`; detail actions `Call back` · `Text back`; answered-on pills `Rotary` / `GV` / `—`.

**Shared:** Retry buttons everywhere read `Retry`; transient refresh `Couldn't refresh. Showing the last update.`; reconnecting banner `Google Voice is reconnecting — voicemail and texts may be delayed.`; tooltip `Google Voice is reconnecting.` **Never** show raw HTTP codes / stack traces / `INVALID_ARGUMENT` — logs only.

**Voice:** plain, calm, sentence case, no exclamation marks, never blame the user; errors say *what happened + what to do*. Matches the existing register ("Awaiting Call", "Lift the handset to place a call").

**Timestamp rule (shared):** relative-smart local — today `9:41 AM` (`9:41a` in compact thread rows), `Yesterday`, within 7 days weekday `Monday`, older same year `Jun 3`, prior year `Jun 3, 2025`. Durations `m:ss` `--font-mono` tabular-nums; `durationSeconds == 0` → `—`/`--:--`, never `0:00`.

---

## Interaction / animation rules

- All animations reuse `design-system.css` §16 keyframes and respect `@media (prefers-reduced-motion: reduce)` (§26) automatically (token-driven).
- New-arrival row: `.list-item-add` (slide from left). New inbound bubble while thread open: `.list-item-add` / `slideInUp`.
- Segmented-filter switch: instant client-side filter (no animation needed); active segment styling per `.mode-btn.active`.
- "More" expand/collapse: `max-height` transition `200ms ease` (matches Dev Tray collapse).
- Voicemail accordion open/close: `max-height` transition, collapse any other open player first.
- Button press: `transform: scale(0.96)` on `:active`, `80ms` (existing house convention).
- Touch targets: rows ≥56px (`.list-item-touch`), Send/keys ≥48px (`--touch-min`), scrubber hit area ≥24px tall.

---

## Data lifecycle (mirror the existing Phone page)

- **OnInitializedAsync:** fetch voicemail list + thread list (and reuse the already-fetched call history) in parallel; subscribe to hub events. Do NOT block tab/filter switching on these.
- **SignalR (primary freshness):** subscribe to `VoicemailReceived(VoicemailItemDto)` and `SmsReceived(SmsMessageDto)` on the **existing hub** (alongside the call events already wired). On receipt: update list/badge; if the affected item is open, append in place.
- **Poll (backstop only):** slow thread-list poll (30–60s) catches missed pushes — same belt-and-suspenders as the existing `PollStatusAsync` (call history is already poll-backstopped at lines 219–222). Never faster than the read endpoints expect.
- **Dispose:** unsubscribe all hub handlers + stop timers (mirror lines 477–487). Switching filter/tab must NOT tear down the page-level subscription.
- **Defensive everywhere:** `transcript`/`text` may be null; `direction` unknown → inbound; timestamps UTC → local; `durationSeconds 0` → unknown; audio endpoint may 502.

---

## Component / file impact (for Planner — what, not how)

- **`PhonePage.razor`** — change default `_activeTab` to `"messages"`; add the `"messages"` and `"more"` rail entries + `_moreExpanded`; render the legacy panels only under "More". Existing SignalR/lifecycle/poll stays; add the two new hub subscriptions + the new lists. *(Architect owns the DTO/service/hub additions and the topbar-badge shared-count mechanism — out of this spec.)*
- **`PhoneMessagesPanel.razor`** (new) — the unified feed + segmented filter + detail pane host.
- **`PhoneVoicemailPanel.razor`** / `VoicemailPlayer.razor` (new) — voicemail rows + inline seekable player.
- **`PhoneTextsPanel.razor`** (new) — thread list + conversation + bubbles + compose (flagged).
- **`design-system.css` §Ph** — add `.msg-*`, `.unread-dot`, `.gv-reconnect-banner` (above). No `:root` changes.
- **`MainLayout.razor`** — add a `.nav-badge` to the `/phone` pill (mirror the Queue pill) + an accessible count in its `aria-label`.
- **Unchanged:** `PhoneDashboardPanel`, `PhoneContactsPanel`, `PhoneHistoryPanel`, `PhoneDiagnosticsPanel`, `PhoneStatusHero`, `PhoneDevTray` — only their rail entry point moves under "More".

---

## Open decisions for the owner

1. **On-screen keyboard for compose (BLOCKING for send on a keyboardless kiosk).** Recommendation: **build a Radzen-native touch keyboard** (re-skin `virtual-keyboard.css`), and **ship the full read experience first** (no keyboard dependency) with compose behind the flag. Confirm: (a) build the keyboard [recommended], (b) assume a physical USB/BT keyboard, or (c) ship read-only, defer compose entirely.
2. **Heard/read persistence.** v1 is **UI-local only** — "heard"/"read" does not persist to Google, so a hard reload re-derives unread from `isRead`/`hasUnread` and a locally-heard item may reappear unread. Confirm that's acceptable for v1, or request GV mark-read be pulled forward on the RotaryPhone side.
3. **"More" wrapper vs. flat legacy tabs.** Recommendation: **"More" expand-in-place group** so Messages clearly owns the rail. Alternative: keep Dashboard/Contacts/Dialer/Diagnostics flat below Messages (simpler, but reads as six co-equal destinations). Confirm the "More" interaction is acceptable.
4. **Unified-feed default + segmented filter** (Decision 1, Option C) vs. a fully-interleaved no-filter feed (A) or hard sub-tabs (B). Recommendation: **C, default All**. Confirm.
5. **Voicemail quick actions (`Call back` / `Text back`) in v1?** They reuse existing dial + the Texts conversation, so low cost — but confirm they're wanted in this pass vs. a fast-follow.
6. **Missed-call badging.** v1 does **not** badge missed calls (only voicemail + texts contribute to unread counts); missed calls are flagged by the red chip only. Confirm, or pull missed-call unread into v1.
7. **Topbar global badge mechanism.** The `/phone` pill badge needs the unread sum surfaced to `MainLayout` (a small shared state/service). Flagging that this is an **Architect** decision (mechanism), not a visual one — included here only so it isn't lost.

---

## Verification (for Tester downstream)

At 1920×720: (1) Phone icon lands on **Messages / All**, no shell scrollbar. (2) Segmented filter narrows to Voicemail/Texts/Calls with correct per-section counts. (3) "More" reveals the four legacy panels, each opens unchanged. (4) Voicemail: skeleton → list → tap row → inline player → first-play buffering spinner → scrubber seeks → transcript present/pending/absent → 502 → audio-error row. (5) Texts: thread list → conversation → bubbles render inbound/outbound → (flagged) sending/sent/failed-with-preserved-text. (6) New-arrival push → toast + badge ++ + row bump, and **no toast / no audio interruption** when the item is already open. (7) `/api/gvbridge/status` unavailable → calm amber banner + Send disabled + "Texting unavailable"; recovery auto-clears. (8) Topbar `/phone` pill badge reflects unread off-page.

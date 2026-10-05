# HANDOFF — Bell-failure surfacing ("the phone won't ring")

**Component:** `src/Radio.Web/Components/Shared/PhoneStatusHero.razor` (primary), `src/Radio.Web/Components/Pages/PhoneDashboardPanel.razor` (System Status row), `src/Radio.Web/Components/Pages/PhoneDiagnosticsPanel.razor` (new Bell card), `src/Radio.Web/Components/Layout/MainLayout.razor` (nav-pill fault badge), `src/Radio.Web/Services/Hub/PhoneHubService.cs` (new hub subscription).
**Surface:** `/phone` → Dashboard tab → Phone Status Hero; plus a persistent cross-page signal on the topbar `/phone` nav pill.
**Status:** `[PENDING REVIEW]` — ready for Planner / Builder once §6 (backend contract) is ratified with RotaryPhone.
**Relationship to existing handoffs:**
- **Follows** `docs/design-handoffs/design_handoff_phone_page/` — hero composition, `.phone-card` / `.phone-pill` / `.phone-btn` chrome, mono-uppercase label typography, `--signal-*` semantics.
- **Follows** `docs/design-handoffs/HANDOFF-phone-dark-theme-and-scrollbars.md` — single dark token set, no light-mode variants (see §8.4).
- **Extends** `docs/design-handoffs/HANDOFF-phone-messages-voicemail-sms.md` — reuses its degraded-banner voice and chrome (`.gv-reconnect-banner`, `.vm-player-error`) for a new hero-level alert strip. That handoff established the phone page's two-tier severity rule — *hard failure → red error block with an action; transient/degraded condition → calm amber banner, explicitly **not** red* — and its copy voice: *"plain, calm, sentence case, no exclamation marks, never blame the user; errors say what happened + what to do."* This spec applies both to the hero, which had no degraded pattern at all.
- **Note on stack:** this app is **Radzen** (`material-dark-base.css`, `RadzenIcon`), **not MudBlazor**. There is no `MudTheme` / `PaletteDark`. Icon names below are Material Symbols names as consumed by `RadzenIcon`.
- **Deviates** from nothing. No new colour tokens, no new spacing values, no new animation curves. Every value in §10 is an existing token or an existing `rgba()` recipe already present in `design-system.css`.

---

## 1. Problem + context

A call came in. `/phone` showed **RINGING** in 96px amber with the `ring-pulse` animation running. The physical rotary phone bell never made a sound. Nothing on screen indicated anything was wrong, and the call sat in that state for the full 60-second timeout.

The cause is a split-brain between two independent paths:

| | Path | Timing |
|---|---|---|
| UI truth | `CallManager.cs:363` sets `Ringing` → SignalR `CallStateChanged` → hero renders | t=0 |
| Physical truth | `CallManager.cs:389` sends the SIP INVITE to the HT801, which is what actually rings the bell | t≈6ms, fails at t≈5s |

The UI commits to "ringing" ~6 ms **before** anything is attempted against the hardware, and never hears about the failure. So the screen was not merely unhelpful — it was **confidently wrong**.

Two design consequences fall out of that timing, and they shape everything below:

1. **The failure signal will always arrive late.** `BellInviteFailed` cannot precede `Ringing`; it lands ~5 s after it. So this is never an alternative initial state — it is always a *late-arriving qualifier* applied to a hero that is already rendered. Design for retrofit, not for a branch at render time.
2. **The call state genuinely is `Ringing`.** The inbound leg is live on the network. The correct UX is not "replace Ringing with an error." It is **degraded-but-live**: keep the true state, and attach an honest caveat about the part that broke.

### Why this needs more than a live-call treatment

A failure visible only inside a 60-second ringing window is easy to miss — nobody was looking at the screen, which is exactly why the phone has a bell. The user's stated goal is that *this class of problem should be obvious and easy to diagnose next time*. That splits into two different jobs with two different audiences:

- **Obvious** — a household member walking past needs to learn "the phone won't ring, look at the screen." Plain language, no jargon, visible before a call ever arrives and still visible after one has gone.
- **Easy to diagnose** — whoever debugs it needs the reason code, the target address, and a timestamp. Jargon welcome, but kept off the hero.

This spec deliberately separates those. §3.2–3.4 and §3.7 serve *obvious*. §3.6 and §3.8 serve *diagnose*.

---

## 2. State model — what "degraded ring" means

Two orthogonal axes. The hero renders the product of them; it does not collapse them into one enum.

**Axis A — call state** (unchanged, comes from `CallStateChanged`):
`Idle` · `Ringing` · `InCall` · `Dialing`

**Axis B — bell health** (new, client-derived):

| Value | Meaning | Source |
|---|---|---|
| `Ok` | No reason to think the bell is broken | default |
| `Suspect` | ATA is known-unreachable, but no ring has actually been attempted yet | `Ht801Reachable == false` from `/api/phone/status` |
| `Failed` | A specific ring attempt was confirmed to have failed | `BellInviteFailed` hub event |
| `Unknown` | We have not been able to check | `Ht801Reachable == null` |

The hero shows the alert strip when bell health is `Suspect` **or** `Failed`. It never shows it for `Ok` or `Unknown`.

### The predictive-degrade rule (important)

Because the failure event is ~5 s late, there is a five-second window during which the hero would still be lying. Close it: **if bell health is already `Suspect` when the call transitions to `Ringing`, render the alert strip immediately** — do not wait for `BellInviteFailed` to confirm.

This can occasionally be wrong (ATA marked unreachable but the bell actually rings). That error is in the safe direction: the worst case is that a user glances at the screen for a call that was also audible. The opposite error — silence with a confident screen — is the bug we are fixing. Copy is identical in both cases; the user does not need to know our confidence level. The distinction is recorded only in Diagnostics (§3.8).

### Bell health is sticky

`Failed` does not clear when the call ends. It persists as a historical note (§3.4) until dismissed, or until a later call rings successfully. This is the single most important behavioural decision in this spec: **the 60-second window is not long enough to be the only chance to notice.**

---

## 3. Visual mockups (ASCII)

Hero interior is ~1030 px wide at the target viewport (§9). Mockups are drawn at proportional scale.

### 3.1 State A — Ringing, bell OK (baseline, unchanged)

```
┌──────────────────────────────────────────────────────────────────────────────────────┐
│  INCOMING CALL                                                  ▪ via ROTARY PHONE   │
│                                                                                      │
│                                                                                      │
│    R I N G I N G                                    ← --signal-amber, .ring-pulse    │
│                                                       96px --font-led, glow 20px     │
│                                                                                      │
│    ┌─────┐   +1 801 555 0134                                                         │
│    │  ↙  │   Karen Anderson                                                          │
│    └─────┘                                                                           │
│                                                                                      │
│    ( ANSWER )      ( REJECT )      ( SILENCE )                                       │
└──────────────────────────────────────────────────────────────────────────────────────┘
```

### 3.2 State B — Ringing, bell failed (NEW — the core state)

```
┌──────────────────────────────────────────────────────────────────────────────────────┐
│  INCOMING CALL · BELL SILENT                                    ▪ via ROTARY PHONE   │
│  ╰──── --text-low ────╯ ╰─ --signal-red ─╯                                           │
│                                                                                      │
│    R I N G I N G                                    ← --signal-amber, NO PULSE       │
│                                                       same size, same glow, static   │
│                                                                                      │
│  ┌────────────────────────────────────────────────────────────────────────────────┐  │
│  │  🔕   The phone won't ring — answer here on the screen.                        │  │
│  └────────────────────────────────────────────────────────────────────────────────┘  │
│     ╰── .phone-hero-alert · red 12% fill · red 30% hairline · --signal-red text      │
│                                                                                      │
│    ┌─────┐   +1 801 555 0134                                                         │
│    │  ↙  │   Karen Anderson                                                          │
│    └─────┘                                                                           │
│                                                                                      │
│    ( ANSWER )      ( REJECT )      ( SILENCE )                                       │
│      ╰ primary       ╰ disabled      ╰ disabled, retitled (§5.4)                     │
└──────────────────────────────────────────────────────────────────────────────────────┘
```

Three things changed, on three independent perceptual channels:

1. **Motion is withdrawn.** `ring-pulse` is removed. The pulse is a visual metaphor for the bell ringing — a thing pulsing in time is *making noise*. If the bell is silent, the pulse is a false claim. Removing it makes the LED read as "lit but not sounding," which is precisely true. This is the most elegant part of the treatment: the honest thing to do is *subtract*, not add.
2. **Colour is added, not replaced.** The word stays `--signal-amber` because the call really is ringing at full strength on the network leg. Red appears *alongside* it in the strip. Amber and red coexisting is not a conflict — it is the exact message: *this is live (amber) and this part is broken (red)*.
3. **Text states it outright**, in the label suffix and in the strip.

**Why the strip sits below the LED word, not above it.** The LED word is the hero's anchor and the first thing the eye lands on; the strip's job is to qualify that claim, so it must be read second. Placing it above would push the LED down, compete with the state-label row, and make the caveat arrive before the thing it qualifies. Below = claim, then correction — which is how the sentence actually reads. (Alternative considered and rejected; see §11 Q3.)

### 3.3 State C — Idle, bell unreachable (NEW — the persistent signal)

This replaces the existing `.phone-hero-empty` copy, which is now an active lie:

> `Lift the handset to place a call, or wait for an incoming ring.` ← wrong when the bell is dead

```
┌──────────────────────────────────────────────────────────────────────────────────────┐
│  AWAITING CALL                                                  ▪ via ROTARY PHONE   │
│                                                                                      │
│                                                                                      │
│    I D L E                                          ← --text-medium, no glow         │
│                                                                                      │
│  ┌────────────────────────────────────────────────────────────────────────────────┐  │
│  │  🔕   The phone can't ring right now. Calls will still appear on this screen.  │  │
│  │       Rotary phone unreachable · last checked 14:32          [ Check again ]   │  │
│  └────────────────────────────────────────────────────────────────────────────────┘  │
│         ╰── sub-line: --font-mono 11px --text-low ──╯      ╰─ .phone-btn-sm ─╯       │
│                                                                                      │
│    ( NEW CALL )    ( PICK CONTACT )                                                  │
└──────────────────────────────────────────────────────────────────────────────────────┘
```

Note the deliberate vocabulary split: the hero says **"rotary phone"** (a thing a household member recognises); the model number lives one card over in System Status, where technical rows already live.

### 3.4 State D — Idle, a recent call failed to ring (NEW — the sticky note)

Amber, not red. The distinction is meaningful and reuses the design system's existing semantics: **red = broken right now, act on it** (matching `.phone-pill.red` = Offline/Unregistered); **amber = a past or degraded condition, be aware** (matching `.gv-reconnect-banner`).

```
┌──────────────────────────────────────────────────────────────────────────────────────┐
│  AWAITING CALL                                                  ▪ via ROTARY PHONE   │
│                                                                                      │
│    I D L E                                                                           │
│                                                                                      │
│  ┌────────────────────────────────────────────────────────────────────────────────┐  │
│  │  🔕   A call at 2:14 PM didn't ring the phone.                     [ Dismiss ] │  │
│  └────────────────────────────────────────────────────────────────────────────────┘  │
│     ╰── .phone-hero-alert.is-warn · amber 12% fill · amber 30% hairline              │
│                                                                                      │
│    ( NEW CALL )    ( PICK CONTACT )                                                  │
└──────────────────────────────────────────────────────────────────────────────────────┘
```

If both C and D apply (ATA still unreachable *and* a recent call failed), render **C only** — the live fault supersedes the historical note, and its copy already covers the consequence. Do not stack two strips; the hero has room but two alerts dilute each other.

Multiple failures collapse into one note:
`2 calls didn't ring the phone. Most recent 2:14 PM.`

### 3.5 State E — InCall, after a failed bell

The strip **clears completely** on the transition to `InCall`. Once the call is up, the bell is no longer relevant to the user's immediate task, and leaving a red alert on an active call reads as "this call is broken," which is false. Bell health stays `Failed` internally; the note re-appears at `Idle` (State D).

### 3.6 System Status card — HT801 row, relabelled and made tri-state

Current row is technically accurate and humanly opaque, and it has a real bug: `Ht801Reachable` is `bool?`, and `== true ? green : red` paints an unknown value as a red **Offline** — a false alarm every time the page loads before the first probe returns.

```
BEFORE                                          AFTER
┌──────────────────────────────────────┐        ┌──────────────────────────────────────┐
│ HT801 ATA   192.168.1.57   [Offline] │        │ BELL   HT801 · 192.168.1.57 [Offline]│
└──────────────────────────────────────┘        └──────────────────────────────────────┘
  ╰ 90px ╯   ╰─ 1fr ─╯     ╰─ auto ─╯             ╰ 90px ╯ ╰──── 1fr ────╯  ╰─ auto ─╯
```

- Label `HT801 ATA` → **`BELL`**. Fits the existing 90px label column, and is the only word in that card a non-technical reader will understand. The model number moves into the value column, where it belongs as a diagnostic detail.
- Pill becomes genuinely tri-state:

| `Ht801Reachable` | Pill class | Text |
|---|---|---|
| `true` | `.phone-pill.green` | `Online` |
| `false` | `.phone-pill.red` | `Offline` |
| `null` | `.phone-pill.gray` | `Unknown` |

### 3.7 Topbar `/phone` nav pill — cross-page fault badge (NEW)

This is what makes the failure findable without being on `/phone`. The pill is visible from every page in the app.

```
   normal              unread only            bell fault           both
 ┌──────────┐        ┌──────────┐          ┌──────────┐        ┌──────────┐
 │ ☎ PHONE  │        │ ☎ PHONE ③│          │ ☎ PHONE  │        │ ☎ PHONE ③│
 └──────────┘        └──────────┘          └───────🔕─┘        └───────🔕─┘
                       ╰ .nav-badge          ╰ .phone-nav-fault (bottom-right)
                         top-right             12px, --signal-red
```

The fault badge takes the **bottom-right** corner so it can coexist with the existing unread count without moving it. It is a glyph (crossed bell), not a coloured dot — so the signal survives for a user who cannot distinguish red, satisfying §8.3. The `aria-label` carries it in text regardless.

The nav pill is a *wayfinding* cue, not the authoritative statement. Its job is only to get someone to `/phone`, where the full non-colour, non-glyph explanation lives.

### 3.8 Diagnostics tab — new "Bell" card (where jargon is allowed)

Serves the second audience. Follows the existing `.phone-diag-card` pattern verbatim.

```
┌── BELL (HT801) ──────────────────────────────────────────────────────────┐
│  Reachable      [ Offline ]                                              │
│  Address        192.168.1.57:5060                                        │
│  Last checked   2026-07-29 14:32:11                                      │
│  Last ring      2026-07-29 14:14:07 — FAILED (Timeout)                   │
│  Detail         no response to INVITE after 5000 ms                      │
└──────────────────────────────────────────────────────────────────────────┘
```

`reason`, `target`, and `detail` from the hub payload (§6) appear **here and nowhere else**. They must never reach the hero.

Even here, the corpus rule holds — *"Never show raw HTTP codes / stack traces / `INVALID_ARGUMENT` — logs only."* `reason` is a closed enum and `target` is an address, so both are safe to render. **`detail` is free-text from the backend and must be treated as untrusted**: render it truncated (single line, ~120 chars, `text-overflow: ellipsis`) and never in the hero or any toast. If `detail` would ever carry a stack trace, it belongs in the log and the field should be dropped from the UI entirely.

---

## 4. Motion — what happens to `ring-pulse`

Existing, unchanged:

```css
@keyframes ringPulse { 0%,100% { opacity:.45; transform:scale(1);} 50% { opacity:1; transform:scale(1.04);} }
.ring-pulse { animation: ringPulse 1.2s ease-in-out infinite; }
```

**Rule:** apply `.ring-pulse` when `CallState == "Ringing"` **AND** bell health ∈ { `Ok`, `Unknown` }. Withdraw it for `Suspect` / `Failed`.

Do **not** substitute a different animation (slower pulse, fade-out, shake). Withdrawal is the message. Adding a second motion vocabulary for "broken ringing" invents a pattern the design system does not have and that nothing else would reuse.

The alert strip itself does **not** animate in. It appears. A slide/fade entrance on a failure notice reads as decorative, and during an incoming call any motion competes with the Answer button for attention.

**Reduced motion.** `design-system.css:1675` already zeroes all animation globally under `prefers-reduced-motion: reduce`. That means a reduced-motion user never saw the pulse in the first place, so *withdrawing* it communicates nothing to them. This is not a nice-to-have caveat — it is why the crossed-bell glyph, the `· BELL SILENT` label suffix, and the strip text are **mandatory, load-bearing channels**, not redundant decoration. Motion is the weakest of the four channels and is the only one allowed to be absent.

---

## 5. Copy deck

House voice, inferred from the two canonical degraded surfaces already in the codebase:

- `PhoneMessagesPanel.razor:18` — *"Google Voice is reconnecting — voicemail and texts may be delayed."*
- `VoicemailPlayer.razor:14` — *"Couldn't load this recording."*

Sentence case. Contractions. Em-dash introduces the consequence. One sentence. Never names a protocol.

### 5.1 Live failure, during Ringing — `--signal-red`, `role="alert"`

> **The phone won't ring — answer here on the screen.**

Every word is doing work. *"The phone"* — the object in the hallway, not "the ATA" or "the bell circuit." *"won't ring"* — present/future, the thing the user cares about, not a past-tense report of an internal event. *"answer here on the screen"* — the action, and the location of the action, in four words. A household member who reads only this sentence knows everything they need.

Rejected alternatives, and why:
- ~~"Bell failure"~~ — names a component, states no consequence, prompts no action.
- ~~"Could not reach the phone's ringer (SIP timeout)"~~ — jargon; also front-loads our internal model.
- ~~"Ringing failed"~~ — contradicts the 96px word directly above it saying RINGING. Actively confusing.
- ~~"The phone isn't ringing"~~ — ambiguous; could be read as "no call is coming in."

### 5.2 State-label suffix (top row)

> `INCOMING CALL` · `BELL SILENT`

`INCOMING CALL` stays `--text-low` (existing). ` · ` separator inherits. `BELL SILENT` is `--signal-red`, same 11px mono uppercase — no size or weight change, colour and adjacency carry it. Two words, because the row is narrow and shares space with the source tag.

### 5.3 Idle, ATA unreachable — `--signal-red`, `role="status"`

> **The phone can't ring right now. Calls will still appear on this screen.**
> `Rotary phone unreachable · last checked 14:32` `[ Check again ]`

Second sentence is essential: without it, the reasonable inference is "the phone is completely dead," and someone stops watching for calls entirely. The whole point is to redirect attention to the screen, not to withdraw it.

### 5.4 Sticky historical note — `--signal-amber`, `role="status"`

> **A call at 2:14 PM didn't ring the phone.** `[ Dismiss ]`

Multiple: **`2 calls didn't ring the phone. Most recent 2:14 PM.`**

Past tense, because it is past. Timestamps use the project's configurable clock format (`Clocks.FormatWallClock`, per `HANDOFF-configurable-time-format.md`) — **not** a hard-coded `HH:mm`.

### 5.5 Button `title` corrections in the degraded state

Two existing tooltips become misleading and must be conditioned:

| Button | Today | When bell health is `Failed` / `Suspect` |
|---|---|---|
| Silence | `Not yet implemented` | `The phone isn't ringing` |
| Reject | `Physical handset only` | `Answer or reject on this screen` |

The Reject one matters more than it looks. If the INVITE to the ATA never succeeded, the ATA has no call, so **lifting the physical handset very likely cannot answer it either** — making the on-screen Answer button not merely convenient but the only path. That assumption needs backend confirmation (§11 Q5), but the copy in §5.1 is correct either way, which is why it says *"answer here on the screen"* rather than *"you can also answer on the screen."*

### 5.6 Accessible names

| Surface | Text |
|---|---|
| Nav pill, fault only | `Phone — the phone won't ring` |
| Nav pill, fault + unread | `Phone, 3 unread — the phone won't ring` |
| Hero LED, sr-only mirror | `Ringing. Bell silent.` |
| Alert strip icon | `aria-hidden="true"` |
| Dismiss button | `Dismiss the missed ring notice` |
| Check again button | `Check whether the phone can ring` |

---

## 6. Backend contract required from RotaryPhone (PR2)

None of this exists yet. Radio.Web is **UI-only** with respect to phone functionality — it consumes RotaryPhone.API over REST/SignalR and registers no RotaryPhone services. Everything below is a request to the other service, and should be filed at `D:\prj\RotaryPhone\docs\prompts\radioconsole-bell-failure-request.md` following the convention set by `radioconsole-gv-markread-readstate-request.md`.

### 6.1 New hub event — REQUIRED

On the existing `RotaryHub` (`/hub`), consumed in `PhoneHubService.cs` alongside the current handlers. Use the **single-DTO** shape (matching `SmsReceived` / `VoicemailReceived`), not the `(string, string)` tuple shape of `CallStateChanged` — this payload has too many fields to stay a tuple, and will grow.

```jsonc
// event name: "BellInviteFailed"
{
  "phoneId":       "rotary-1",
  "callId":        "9f2c1a...",                 // REQUIRED — see 6.5
  "direction":     "Inbound",
  "callerNumber":  "+18015550134",
  "occurredAtUtc": "2026-07-29T20:14:07.812Z",
  "reason":        "Timeout",                    // enum, see below
  "target":        "192.168.1.57:5060",          // diagnostics only, never user-facing
  "detail":        "no response to INVITE after 5000ms"  // diagnostics only, nullable
}
```

`reason` ∈ `Timeout` · `Unreachable` · `Rejected` · `NotRegistered` · `NotConfigured` · `Unknown`

Radio.Web treats an unrecognised `reason` as `Unknown` and still shows the strip — the user-facing copy does not vary by reason, so an unknown code must never suppress the alert.

### 6.2 Recovery signal — REQUIRED (one of two forms)

Without this, a fault badge can only clear on the next successful call, which could be days. Either:

- **(a) preferred)** a `BellRecovered` event with `{ phoneId, occurredAtUtc }`; or
- **(b) minimum)** a hard guarantee that `SystemStatusChanged` fires whenever `Ht801Reachable` changes value in either direction.

(b) is acceptable and requires no new event, but only if the guarantee is real — Radio.Web currently re-fetches `/api/phone/status` on `SystemStatusChanged`, so the plumbing already exists.

### 6.3 `PhoneSystemStatusDto` additions — REQUIRED

```csharp
public bool? Ht801Reachable { get; set; }         // exists — semantics must be honoured
public DateTime? Ht801LastCheckedUtc { get; set; } // NEW
```

`Ht801Reachable == null` must mean *"not yet probed / cannot determine,"* never *"false."* Today the UI collapses null into a red **Offline**, which is a false alarm at boot; §3.6 fixes the UI side, but only if the server keeps null meaningful. `Ht801LastCheckedUtc` powers the `last checked 14:32` sub-line and lets the UI mark a stale probe.

### 6.4 Reload survivability — REQUIRED

This one is the difference between the spec working and not working. A browser refresh, a kiosk restart, or a Blazor circuit drop currently erases the only record that anything went wrong — reintroducing the exact bug we are fixing, just on a longer fuse. `GET /api/phone/status` must include:

```jsonc
"lastBellFailure": {
  "occurredAtUtc": "2026-07-29T20:14:07.812Z",
  "reason":        "Timeout",
  "callerNumber":  "+18015550134",
  "failureCount":  2,          // consecutive failed rings since last success
  "acknowledged":  false
} // or null
```

### 6.5 `PhoneCallStateDto.CallId` — STRONGLY REQUESTED

`PhoneCallStateDto` has no call identifier, so Radio.Web cannot correlate `BellInviteFailed` to a specific call — only to "whatever is ringing right now." Given that the failure arrives ~5 s after `Ringing`, a fast hang-up-and-redial inside that window would mis-attribute the alert. Adding `CallId` to the status DTO and the `CallStateChanged` payload removes the ambiguity. If it cannot be added, Radio.Web will fall back to "apply to the current ringing call, ignore if not ringing" and accept the rare mis-attribution.

### 6.6 Acknowledge endpoint — REQUESTED

`POST /api/phone/bell-failure/ack` so `[ Dismiss ]` is durable across reloads. If declined, Radio.Web holds dismissal in a client-side singleton (same pattern as `PhoneUnreadState`) and the note reappears after an app restart. See §11 Q4.

### 6.7 Probe endpoint — REQUESTED

`POST /api/phone/bell/probe` to back `[ Check again ]`. If declined, the button re-fetches `GET /api/phone/status` and is relabelled `Refresh`.

### 6.8 Ordering contract — MUST BE STATED, NOT ASSUMED

RotaryPhone must confirm in writing that `BellInviteFailed` may arrive **after** the call has already left `Ringing` (answered, rejected, or timed out). Radio.Web is built to handle this (§7), but the contract should say so explicitly so a future refactor does not quietly start assuming ordering.

---

## 7. Transitions + edge cases

| # | Scenario | Behaviour |
|---|---|---|
| a | `Ringing` → `BellInviteFailed` arrives | Strip appears in place; `ring-pulse` withdrawn; label suffix added. No layout jump — see §9. `role="alert"` fires **once**. |
| b | ATA already `Suspect`, call → `Ringing` | Strip renders immediately at `Ringing` onset (predictive-degrade, §2). No 5-second blind window. |
| c | Answered on screen → `InCall` | Strip clears entirely. Bell health stays `Failed` internally. |
| d | Caller hangs up before timeout → `Idle` | Strip → sticky amber note (State D). |
| e | 60-second timeout → `Idle` | Same as (d). |
| f | `BellInviteFailed` arrives for a call that already ended | **No** live strip. Still record the sticky note. Never retro-apply an alert to a hero showing a different call. |
| g | Two failures in a row | One note, `failureCount` phrasing (§5.4). Do not stack strips. |
| h | Failure, then a later call rings successfully | Bell health → `Ok`. Note auto-clears. A successful ring is the strongest possible recovery evidence — stronger than a reachability probe. |
| i | `[ Dismiss ]` pressed | Note clears. Does **not** clear `Suspect` — if the ATA is still unreachable, State C persists. Dismissing a historical note must not silence a live fault. |
| j | Both `Suspect` and a recent failure | State C only (§3.4). |
| k | Hub disconnects mid-ring | Out of scope, but note: with the hub down the hero cannot learn anything, and must not be read as asserting the bell is fine. The existing `.gv-reconnect-banner` pattern covers hub-down messaging on the Messages panel; extending it to the Dashboard tab is a candidate follow-up. |
| l | Page loaded fresh with a prior unacknowledged failure | `lastBellFailure` from `/api/phone/status` (§6.4) rehydrates State D on first render. |
| m | `Ht801Reachable == null` | Bell health `Unknown`. **No strip.** Gray `Unknown` pill in System Status only. Never alarm on absence of evidence. |
| n | Failure event arrives during `Dialing` or `InCall` | Record it (sticky note for later); render nothing live. The bell is an inbound-ring concern only. |

**Non-obvious ones worth calling out to Builder:** (f), (h), (i), and (m). Each is a place where the naive implementation produces a wrong or alarming result.

---

## 8. Accessibility

### 8.1 Announcement

| Surface | Role | Rationale |
|---|---|---|
| Live failure strip (Ringing) | `role="alert"` | Assertive. A call is ringing right now and the user must act within seconds. This is the textbook case for interrupting. |
| Idle unreachable strip (State C) | `role="status"` | Polite. Ambient condition, no deadline. |
| Sticky note (State D) | `role="status"` | Polite. Historical. |

**A documented tension, flagged rather than silently resolved.** The handoff corpus contains **zero** uses of `role="alert"` — its stated convention is `aria-live="polite"`, never assertive (`HANDOFF-phone-messages-voicemail-sms.md`, and the discipline in `HANDOFF-sleep-mode-weather-forecast.md:340`: *"live-announcing every 1-Hz tick would be hostile"*). The shipped **code**, however, already uses `role="alert"` for genuine failures — `VoicemailPlayer.razor:12` ("Couldn't load this recording") and `PhoneTextsPanel.razor:96`.

This spec sides with the code precedent for the live strip only, on the grounds that a bell failure during an active inbound ring is strictly more urgent than a voicemail that won't load — it has a hard deadline measured in seconds — and that the corpus's aversion to assertive announcements is aimed at *periodic ambient data*, which this is not. States C and D follow the corpus and stay polite. See §11 Q8; the owner may overrule this to polite everywhere at the cost of a blind screen-reader user possibly missing the ring window.

The strip must be **keyed stably** so Blazor does not remount it on unrelated re-renders — `role="alert"` re-announces on DOM insertion, and the hero re-renders on every `InCall` duration tick and every `StateHasChanged` from the hub. A strip that remounts would announce repeatedly, which is worse than not announcing at all. Builder: verify with a screen reader across at least one full 60-second ring cycle.

`role="alert"` announces **without moving focus** — correct here. The Answer button must remain the primary focus target; stealing focus to an alert during an incoming call would actively impede the user's task.

### 8.2 The LED word is not accessible on its own

`.phone-hero-state` renders the bare string `RINGING`. A screen reader hears "RINGING" and nothing else — the degraded qualifier is invisible to it, since it is carried by the *absence* of an animation.

Add an sr-only mirror carrying the qualified state (the pattern already exists in `RdsScrollMarquee.razor:41`):

```
visual:   RINGING          (amber, static)
sr-only:  "Ringing. Bell silent."
```

### 8.3 Not colour alone (WCAG 1.4.1)

Four independent channels carry "the bell failed." At least three are always present:

| Channel | Present when |
|---|---|
| **Text** — `· BELL SILENT` + full sentence in strip | always |
| **Glyph** — `notifications_off` (a bell with a slash through it) | always |
| **Colour** — `--signal-red` | always |
| **Motion** — pulse withdrawn | only without `prefers-reduced-motion` |

`notifications_off` is chosen deliberately over `error_outline` or `warning`: it is literally a crossed-out bell, so it carries the *specific* meaning ("no ringing") rather than the generic one ("something is wrong"), and it does so without language.

The nav-pill badge (§3.7) is a glyph rather than a coloured dot for exactly this reason.

### 8.4 Contrast

The app is **dark-only** — `color-scheme: dark` at `design-system.css:57`, a single `:root` token set, no light-theme selector, no `dark:` variants anywhere. "Dark-mode parity" for this spec therefore means: **use tokens, add no hard-coded hex, introduce no light-mode branch.** Every value in §10 satisfies that. There is no second theme to check.

Measured against `--surface-raised` `#141416`:

| Pair | Ratio | Verdict |
|---|---|---|
| `--signal-red #F87171` | ≈ 6.8 : 1 | Passes AA normal text |
| `--signal-amber #F0A830` | ≈ 9.4 : 1 | Passes AAA normal text |
| `--text-low #4B5563` (sub-line) | ≈ 2.6 : 1 | **Fails.** Acceptable only for the non-essential `last checked` timestamp; never for the primary sentence. |

Builder: the 12% coloured fill lightens the strip background slightly. Re-measure red-on-tint; if it lands below 4.5:1, raise the text to `--text-high` and keep red on the icon and hairline only.

### 8.5 Touch targets and focus

`[ Dismiss ]` and `[ Check again ]` use `.phone-btn-sm`, which is 32px tall — below the project's `--touch-min` of **48px** for buttons (the corpus bar: rows ≥56px, Send/keys ≥48px, scrubber hit area ≥24px). On a touch kiosk they need vertical padding to reach a 48px hit area. Use the pattern already at `.vm-scrubber` (*"3px bar + pad ≥24px hit area"*) — **do not enlarge the visual button; pad the hit region**, so the strip's 44px min-height is not blown out.

Both buttons need the project's canonical focus ring, which is required app-wide for keyboard and kiosk-remote operation:

```css
.phone-hero-alert .phone-btn-sm:focus-visible {
  outline: none;
  box-shadow: inset 0 0 0 2px var(--accent-primary);
}
```

---

## 9. Layout budget at 1920 × 720

The hero must absorb the strip **without reflowing anything else** — a 44px layout jump five seconds into an incoming call would move the Answer button under the user's descending finger.

Measured chain:

```
viewport height                                     720
− topbar (--topbar-height)                         −120
= .content-area                                     600
− NowPlayingDock (.content-area.has-dock)           − 64
= .phone-shell / .phone-dashboard                   536
− .phone-dashboard padding (14 × 2)                 − 28
− PhoneDevTray row (collapsed) + 12px gap           − 56
= hero outer height                               ≈ 452
− .phone-hero padding (24 × 2)                     − 48
= hero content budget                             ≈ 404
```

Content consumed in the ringing state:

| Element | Height |
|---|---|
| `.phone-hero-top` | 16 |
| `.phone-hero-state` (96px + 16px margin-top) | 112 |
| `.phone-hero-meta` (20px margin + 44px row) | 64 |
| `.phone-hero-actions` (`.phone-btn` 56px) | 56 |
| **Subtotal** | **248** |
| **Slack** | **≈ 156** |

The alert strip needs **44px + 12px gap = 56px**, comfortably inside the 156px slack. **No font-size reduction is required** and the LED word stays at 96px.

Guard rail for Builder: if the meta row later grows (a second line, an avatar), the first thing to give is the LED word — drop `.phone-hero-state` from 96px to 72px **only** while the strip is present. Do not shrink the action buttons; they are touch targets on a kiosk.

Horizontally the hero is `1.5fr` of a `1.5fr 1fr` grid inside `1920 − 156 (rail) − 28 (padding) = 1736px`, minus the 12px gap → hero ≈ **1030px** outer, ≈ **974px** interior. Every copy string in §5 fits on one line at 13px with large margin. The strip is `width: 100%`; text does not wrap at this viewport, but must be allowed to wrap rather than clip if it ever does.

---

## 10. Tokens + CSS additions

**No new design tokens.** Every colour is an existing `--signal-*`; every rgba is a recipe already present in `design-system.css`. This is a deliberate constraint — a failure state is exactly the place where a one-off "alert red" tends to get invented and then drift.

New classes, to be added in the `§Ph Phone Page Surface` block:

```css
/* §Ph — Hero alert strip (bell failure).
   Chrome is the .phone-btn-sm.btn-danger / .btn-warn recipe verbatim
   (12% fill / 30% hairline) so the strip reads as the same family as the
   existing degraded surfaces. No new tokens. */
.phone-hero-alert { /* flex row, gap --sp-3, padding --sp-3 --sp-4,
                       border-radius 8px, margin-top --sp-4, min-height 44px */ }
.phone-hero-alert            { background: rgba(248,113,113,0.12);
                               border: 1px solid rgba(248,113,113,0.30);
                               color: var(--signal-red); }
.phone-hero-alert.is-warn    { background: rgba(240,168,48,0.12);
                               border: 1px solid rgba(240,168,48,0.30);
                               color: var(--signal-amber); }
.phone-hero-alert-text       { font-family: var(--font-body); font-size: 13px; }
.phone-hero-alert-sub        { font-family: var(--font-mono); font-size: 11px;
                               color: var(--text-low); }

/* §Ph — "· BELL SILENT" suffix on the hero state label */
.phone-hero-state-label-fault { color: var(--signal-red); }

/* §Ph — Topbar nav-pill bell-fault badge (bottom-right; coexists with .nav-badge) */
.phone-nav-fault { position: absolute; bottom: 6px; right: 6px;
                   font-size: 12px; color: var(--signal-red); }
```

Existing classes reused unchanged: `.phone-btn-sm`, `.phone-pill.{green,red,amber,gray}`, `.phone-card`, `.phone-status-row`, `.phone-diag-card`, `.ring-pulse`, `.nav-badge`.

Existing classes **modified**: none structurally. `.phone-hero-empty`'s *content* is conditioned (§3.3); its styling is untouched.

---

## 11. Open questions for the user

**Q1 — Event name.** The brief says `BellInviteFailed`. That leaks the protocol into the wire contract. **Designer recommendation: `BellRingFailed`** — same meaning, survives a future move off SIP, and matches the user-facing vocabulary in §5. Low stakes; RotaryPhone's call either way.

**Q2 — Sticky-note lifetime.** Currently: until dismissed, or until a later call rings successfully. Alternative: auto-expire after N hours. **Designer recommendation: no auto-expiry.** The failure mode being fixed is *nobody noticed*; a note that quietly deletes itself overnight reintroduces exactly that. If the note becomes annoying, that is evidence the bell is still broken.

**Q3 — Strip placement.** Chosen: below the LED word. Alternative: at the very top of the hero, matching `.gv-reconnect-banner`'s placement at the top of its panel. **Designer recommendation: keep it below** (rationale in §3.2), but this is the single most reversible decision in the spec and worth a look at the real thing before committing.

**Q4 — Durable dismissal.** Needs `POST .../ack` (§6.6). If RotaryPhone declines, the note reappears after every app restart. **Designer recommendation: push for the endpoint.** A kiosk that restarts nightly would resurrect a week-old dismissed note forever, and users learn to ignore alerts that come back.

**Q5 — Can the physical handset still answer?** If the INVITE never reached the ATA, the ATA has no call, so lifting the handset probably does nothing. Needs confirmation from RotaryPhone. If confirmed, §5.5's Reject/Silence tooltip changes become required rather than polish, and it is worth considering whether the copy should say so outright.

**Q6 — Should a dead bell shorten the 60-second ring timeout?** Sitting in `Ringing` for a full minute when we *know* nothing is audible is arguably wrong. This is backend behaviour, not UI, so it is out of scope here — but it is a UX-relevant question and someone should decide it deliberately. **Designer recommendation: do not shorten.** The screen is now a valid answer path, and the caller deserves the full window.

**Q7 — Nav-pill badge corner.** Chosen: bottom-right, so the unread count keeps top-right. Alternative: fault takes top-right (it is more urgent) and unread moves down. **Designer recommendation: keep fault at bottom-right** — it changes no existing behaviour, and the glyph is distinctive enough that corner position is not carrying meaning.

**Q8 — Assertive vs polite announcement.** §8.1 uses `role="alert"` for the live strip, siding with shipped code (`VoicemailPlayer.razor:12`) over the handoff corpus's stated "always polite" convention. **Designer recommendation: keep assertive for the live strip only.** A polite live region can be queued behind other speech and may not surface until after the caller has hung up — which for a blind user reproduces the original bug exactly. States C and D stay polite. If the owner prefers corpus consistency, the fallback is `aria-live="assertive"` on a persistent region rather than `role="alert"`, which announces on content change without the remount hazard in §8.1.

---

## 12. Out of scope

Flag any of these if scope-creep pressure builds.

1. **Any RotaryPhone code.** Radio.Web is UI-only for phone functionality. §6 is a *request*; it ships in RotaryPhone PR2, authored there.
2. **Fixing the underlying race.** `CallManager.cs:363` broadcasting `Ringing` before the INVITE at `:389` is a RotaryPhone concern. This spec makes the consequence visible; it does not reorder the backend.
3. **Audible alerting from Radio.Web.** No browser-side ring sound as a bell substitute. That is a real idea and a real feature, with its own volume/ducking/source-priority questions against the audio pipeline. Separate spec.
4. **Retry / re-ring controls.** No "try ringing again" button. Ringing is the backend's job.
5. **Outbound-call bell failures.** Bell health is an inbound-ring concern only (§7n).
6. **Historical fault log.** No "bell failures over time" list or chart. The sticky note holds the most recent event; Diagnostics holds the current detail. A trend view is a Metrics-surface feature if it is ever wanted.
7. **Notifications outside the app.** No push, SMS, or TTS announcement of a dead bell.
8. **Generalising to other hardware faults.** The nav-pill fault badge is bell-specific in this pass. Turning it into a general "phone subsystem health" indicator (BT, SIP, GV bridge, trunk) is an attractive follow-up and a bigger IA question — it would need to decide precedence among four independent faults. Not now.
9. **The `.gv-reconnect-banner` hub-down case on the Dashboard tab** (§7k). Candidate follow-up.

---

## Hand-off summary for Planner / Builder

One new client-side bell-health enum (`Ok` / `Suspect` / `Failed` / `Unknown`) derived from two inputs: a new `BellInviteFailed` subscription in `PhoneHubService.cs` (alongside the existing six handlers, single-DTO shape) and the existing `Ht801Reachable` field on `PhoneSystemStatusDto`. It is passed into `PhoneStatusHero.razor` as one new parameter.

`PhoneStatusHero.razor` gains: a conditional `.phone-hero-alert` strip between the LED word and the meta row; a `· BELL SILENT` suffix on the state label; a condition on the existing `.ring-pulse` class; conditioned `.phone-hero-empty` copy; an sr-only state mirror; and two conditional button tooltips. `PhoneDashboardPanel.razor` gets a one-row change (relabel `HT801 ATA` → `BELL`, tri-state pill). `PhoneDiagnosticsPanel.razor` gets one new `.phone-diag-card`. `MainLayout.razor` gets a conditional 12px glyph badge on the `/phone` nav pill plus an `aria-label` extension.

Four new CSS classes in the `§Ph` block, all built from existing tokens and existing rgba recipes. **Zero new design tokens. Zero new keyframes. No light-mode branch — the app is dark-only.** Layout fits the 1920×720 budget with ~100px of slack; no font-size reductions, no reflow when the strip appears.

**Blocked on:** §6.1 (the event), §6.3 (`Ht801LastCheckedUtc`), and §6.4 (`lastBellFailure` on `/api/phone/status`) — that last one is what makes the signal survive a page reload, without which the spec degrades back into a 60-second window. §6.5 (`CallId`) is strongly preferred. §6.2 (recovery) can ship as the `SystemStatusChanged` guarantee rather than a new event. §6.6 / §6.7 are graceful-degradation-friendly: build the buttons, wire them to fallbacks if the endpoints do not land.

**Suggested build order:** the persistent signals (§3.3, §3.6, §3.7) depend only on `Ht801Reachable`, which already exists — they can ship *before* RotaryPhone PR2 lands and deliver most of the "obvious next time" value on their own. The live-call treatment (§3.2) and the sticky note (§3.4) follow once the event and `lastBellFailure` are available.

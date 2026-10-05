# UAT — Google Voice live data on `/phone` (Voicemail + Texts)

**Date:** 2026-07-31
**Target:** `http://radio:5002/phone` (Ubuntu box `radio`, Intel N100)
**Viewport:** 1920x720 (real console resolution) — verified via `window.innerWidth/innerHeight`
**Tool:** Playwright MCP
**Context:** First UAT pass against real Google Voice data after the `PositionalGvThreadParser`
fix (parser expected a JSON object root; `alt=protojson` returns arrays, so every response
previously yielded 0 items behind a clean HTTP 200).
**Primary consumer:** **GV-7** (rendering non-dialable SMS senders).

> **Scope note.** This pass observes and reports only. No production code was read for
> diagnosis and none was changed. Root causes below are stated as *observations plus
> candidate explanations*, not as diagnosed defects.

---

## Summary

| | |
|---|---|
| Flows exercised | 9 |
| Passed | 6 |
| Failed | 3 |
| HIGH severity findings | 1 |
| MEDIUM severity findings | 2 |
| LOW severity findings | 3 |
| Console errors | **0** |
| Failed network requests | **0** |

**Headline:** Live GV data renders correctly and the non-dialable-sender case degrades
*gracefully* — the raw identifier is used as the name, never a blank or a wrong name, and
a synthetic 36-character identifier causes **no layout blowout at 1920x720**. GV-7 is
unblocked on its stated risk.

The one HIGH finding is unrelated to GV-7: **conversation message bodies fail to load
silently**, rendering a misleading "Start the conversation below." empty state with no
error and no retry — invisible in both the console and the network log.

---

## Punch-list

| # | Flow | Result | Severity |
|---|---|---|---|
| 1 | Texts tab lists live threads | **PASS** | — |
| 2 | Non-dialable senders render without layout damage | **PASS** | — |
| 3 | Contact resolution degrades gracefully | **PASS** | — |
| 4 | Conversation header renders counterparty | **PASS (with nit)** | LOW |
| 5 | Inbound bubbles + meta render | **PASS** | — |
| 6 | Composer unreachable (`SendEnabled=false`) | **PASS** | — |
| 7 | Conversation message bodies load reliably | **FAIL** | **HIGH** |
| 8 | Thread-list error handling | **PASS** | — |
| 9 | Unread/read row alignment | **FAIL** | LOW |

---

## Findings for GV-7

*This is the section GV-7 is blocked on. The queue row reads
"⚠ LIVE OBSERVATIONS INCOMING — do not design blind."*

### G-1. The "2" in `Texts 2` is an unread count, not a thread count

The interrupted session recorded "the filter bar showed `Texts 2`" and the task framing
assumed that meant **2 threads**. It does not. The Texts tab renders **20 threads**; the
badge is the unread count (`<span class="phone-pill cyan">2</span>`, same treatment as
`Voicemail 6` = 6 unread of 14). The badge disappeared entirely once those 2 threads were
opened and marked read.

**GV-7 should design against a 20-row list, not a 2-row list.**

### G-2. Counterparty identifier inventory (live, all 20 threads)

| Kind | Count | Literal values |
|---|---|---|
| Resolved contact name | 6 | Mark Mackelprang, Carol Everett, Van Mackelprang, Mary Carmen Wiser, Lynne Marley, Darlann Romney |
| E.164, unresolved | 9 | `+1801***8129`, `+1336***9432`, `+1855***0400`, `+1662***0199`, `+1772***7803`, `+1478***2306`, `+1213***7467`, `+1209***7467`, `+1919***8923` |
| Short code | 5 | `51789`, `39041`, `47864`, `837402`, `32665` (reported in full — not personal) |
| Opaque 36-char sender ID | **0** | **none observed** |

Short-code lengths observed: **5 and 6 characters only**. Longest identifier of any kind
in the live top-20 is 12 characters (E.164).

**14 of 20 (70%) render an identifier rather than a name.** That is a higher unresolved
share than the ~⅓ figure from the prior wire-data analysis, so the fallback path is the
*common* case on this surface, not an edge case.

### G-3. No opaque 36-character sender IDs are observable

The prior wire-data analysis predicted opaque 36-character sender IDs. **None appear in
the live top-20.** The Texts feed has no pagination or "load more" control (the only
`More` control on the page is the left-hand nav), so the surface caps at 20 threads and
older threads that might contain such IDs are not reachable through the UI.

**This is a "could not observe" result, stated plainly.** GV-7 should not assume the
opaque-ID case is absent from the corpus — only that it is not reachable from this
surface today. To de-risk it anyway, see G-4.

### G-4. Synthetic long-identifier layout probe — **no blowout at 1920x720**

Because no long identifier exists live, I injected one into the DOM (measurement only,
reverted immediately) to answer GV-7's specific layout question. Results at 1920x720:

| Identifier length | Thread-list name line | Conversation header name | Header number line | Page overflow |
|---|---|---|---|---|
| 12 (baseline) | fits (1045px avail) | fits | fits | none |
| **36 (opaque ID)** | **fits, no ellipsis** | **fits, no ellipsis** | **fits** | **none** |
| 60 | ellipsized cleanly | ellipsized cleanly | at cap | none |
| 120 | ellipsized cleanly | ellipsized cleanly | **clips mid-character** | none |

Computed styles:

- `.list-item-title` — `white-space: nowrap; overflow: hidden; text-overflow: ellipsis` ✅ protected
- `.texts-conv-name` — `white-space: nowrap; overflow: hidden; text-overflow: ellipsis` ✅ protected
- `.texts-conv-number` — `white-space: normal; overflow: visible; text-overflow: clip` ⚠️ **unprotected**

The header title block grows 127px → 371px → caps at 443px (right edge 1908px against a
1920px viewport, 12px margin). `document.scrollWidth` stayed exactly 1920 in every trial —
**no horizontal page overflow was introduced at any length tested.**

**Answer for GV-7: a 36-character opaque ID is safe at 1920x720.** It needs no truncation
strategy for layout-safety reasons. The single robustness gap is `.texts-conv-number`,
which lacks `text-overflow: ellipsis` and so clips mid-character rather than ellipsizing
beyond ~60 characters. That is a LOW-severity hardening item (F-4), not a blocker.

### G-5. Contact resolution degrades gracefully — no empty or misleading names

`ContactResolutionService` behaves correctly on live data:

- **Resolves (6/20):** header shows the contact name on the title line and the *real* E.164
  number on the subtitle line — two distinct values.
- **Does not resolve (14/20):** the raw identifier is used as the name. **Never blank,
  never a placeholder, never a wrong name.**
- **Short codes never resolve** — structurally expected and confirmed. A 5-digit code has
  no contact record to match, and there is no `fromName` in the GV payload to fall back to.

No misleading output was observed in any of the 20 threads. **This is a pass.**

### G-6. Header duplicates the identifier when no name resolves

The conversation header is a two-line block: `.texts-conv-name` (16px, `rgb(240,239,244)`)
over `.texts-conv-number` (12px, `rgb(181,188,201)`). When the name falls back to the
identifier, **both lines render the identical string**:

```
32665            <- .texts-conv-name
32665            <- .texts-conv-number
```

Confirmed for **all 14** unresolved threads (`headerName === headerNumber`), across both
short codes and E.164 numbers. For the 6 resolved threads the two lines correctly differ.

Cosmetic, not a functional defect — but it is the most visible artifact of the fallback and
is squarely in GV-7's scope. Evidence: `texts-conversation-shortcode-32665-1920x720.png`,
`texts-conversation-e164-unresolved-1920x720.png`.

### G-7. Thread-list rows: identifier occupies the name line, preview truncates correctly

Row structure is `.list-item-title` (name line) over `.list-item-subtitle` (preview), with
a `.feed-chip--text` `chat_bubble` icon, a right-aligned date, and a `chevron_right`.

- The name line holds the raw identifier when unresolved — **not blank, not a placeholder.**
- The name line is **not truncated** for any live identifier (1045px available vs. 12 chars max).
- The preview line truncates with a proper ellipsis on long messages (5 of 20 rows).

### G-8. MMS previews embed a sender prefix

Two threads show a preview in the form `+1XXXXXXXXXX - <text>`:

- Mary Carmen Wiser → `+1919***7670 - MMS Received`
- Darlann Romney → `+1919***5840 - ❤️Love you too! ❤️`

The literal string `MMS Received` appears as preview text in one case. GV-7 may want to
account for this prefix format, since it renders a *second* phone number inside a row that
already displays a counterparty. These same two threads never rendered a body (see F-1).

### G-9. Bubble and meta treatment

Inbound messages render as left-aligned bubbles on a lighter surface within the right-hand
pane, each with a small dim timestamp *below* the bubble text (`3:29 PM`, `3:30 PM`), and a
centered uppercase date separator above each day group (`APR 21, 2022`, `MAR 2, 2021`).

Treatment is **identical for short codes, E.164 numbers, and resolved contacts** — no
special-casing by sender kind. Long URLs inside bubbles wrap within the bubble and do not
overflow the pane.

One nit: bubble text for the `32665` thread ended in a literal `...` (`"...Reply help for ..."`),
matching the truncated list preview rather than the full message body — see F-5.

---

## Detailed findings

### F-1 · HIGH · Conversation bodies fail to load silently, showing a misleading empty state

Opening a thread frequently renders the empty state **"Start the conversation below."**
even though the thread demonstrably has messages (its list row shows a real preview and a
date). There is **no error message, no spinner, and no retry affordance** — the UI presents
"this conversation is empty" when the truth is "loading failed."

This is invisible to standard instrumentation: **0 console errors and 0 failed network
requests** throughout. The page is Blazor Server, so the fetch happens server-side over
SignalR and its failure never surfaces to the browser.

**Steps to reproduce**
1. Open `http://radio:5002/phone` at 1920x720, click the **Texts** tab.
2. Click any thread. Wait 4s.
3. Observe either the messages *or* "Start the conversation below."
4. Open several threads in succession — the empty state becomes progressively more frequent.

**Observed behaviour (trusted Playwright clicks, clean circuit):**

| Attempt | Thread | Body |
|---|---|---|
| Early sweep | 18 of 20 threads | rendered content ✅ |
| Early sweep | Mary Carmen Wiser, Darlann Romney | empty ❌ |
| After `Retry` | `32665` (rendered content earlier) | empty ❌ |
| After 75s cooldown | `51789` | rendered content ✅ |
| Immediately after | Darlann Romney | empty ❌ |
| Immediately after | `39041` (control) | empty ❌ |
| After 90s cooldown | Lynne Marley | empty ❌ |

**Candidate explanation (not diagnosed):** the pattern — works after a cooldown, degrades
within 1–2 subsequent opens, recovers partially with time — is consistent with upstream
Google Voice throttling or quota exhaustion on the per-thread message fetch. The thread
*list* fetch is separate and stayed healthy.

**Two distinct problems are bundled here; both need addressing:**
1. **The silent failure itself** — whatever the upstream cause, a failed load must not
   render as an empty conversation.
2. **The missing error state** — the thread list already has the right pattern
   ("Couldn't load conversations." + `Retry`, see F-6). The conversation pane has no
   equivalent.

**Sub-observation (weaker, worth a look):** across every attempt, 18 of 20 threads rendered
a body at least once. The only 2 that **never** did are exactly the 2 MMS-preview threads
from G-8 (Mary Carmen Wiser, Darlann Romney). That correlation may indicate a *separate*
MMS-specific rendering defect layered on top of the general flakiness. Not confirmed —
the general flakiness makes it impossible to distinguish from bad luck without a clean
upstream.

Evidence: `texts-conversation-mms-empty-body-1920x720.png`,
`texts-silent-empty-body-resolved-contact-1920x720.png`

---

### F-2 · MEDIUM · Empty-state copy invites an action the disabled composer forbids

The empty state reads **"Start the conversation below."** — but the composer below it is
hard-disabled (`SendEnabled=false`, F-3). The copy directs the user toward an action that
is impossible. On a short-code thread it is doubly wrong: short codes cannot receive
replies at all, regardless of the flag.

Compounded by F-1, this is the worst case: a conversation that *failed to load* tells the
user it is empty and invites them to reply into a disabled box.

---

### F-3 · PASS · Composer is genuinely unreachable — `SendEnabled=false` is honoured

Verified at the DOM level:

```
.texts-compose-input  → disabled = true   (HTMLInputElement.disabled)
button "Send"         → disabled = true,  opacity 0.35
```

Both controls are **hard-disabled**, not merely styled to look inactive. A disabled input
is not keyboard-focusable, so compose is unreachable by mouse and by Tab. **No contradiction
of `RotaryPhone:Gv:SendEnabled = false` was found.**

One note for GV-7: the composer is **rendered but disabled**, not hidden, and carries no
explanation of *why* it is disabled — no tooltip, no helper text, no `title` attribute
(`title` is empty). A user sees an input they cannot type into with no stated reason.
Whether to hide it, or label it ("Sending is not enabled"), is a design decision — flagging
it, not prescribing it.

---

### F-4 · LOW · `.texts-conv-number` lacks overflow protection

`white-space: normal; overflow: visible; text-overflow: clip`. Its two siblings
(`.list-item-title`, `.texts-conv-name`) all carry
`nowrap` + `overflow: hidden` + `text-overflow: ellipsis`. Beyond ~60 characters this line
clips mid-character instead of ellipsizing. No live data triggers it and a 36-char ID does
not (G-4), so this is hardening, not a fix.

---

### F-5 · LOW · Bubble text appears truncated with a literal `...`

In the `32665` conversation the bubble read
`"Confirmed! To edit SMS preferences go to m.facebook.com/settings. To turn off SMS for your
Facebook account on this mobile number reply stop. Reply help for ..."` — ending in an
ellipsis inside the bubble, matching the truncated list preview. Suggests the conversation
view may render the list snippet rather than the full message body for some messages. Worth
confirming against a known long message once F-1 is resolved and bodies load reliably.

---

### F-6 · PASS · Thread-list error handling works correctly

Mid-session the feed entered a clean error state: a `cloud_off` icon with
**"Couldn't load conversations."** and a **`Retry`** button. Clicking `Retry` restored all
20 threads within 1s. This is the correct pattern and is exactly what the conversation pane
is missing (F-1).

Evidence: `texts-error-couldnt-load-conversations-1920x720.png`

---

### F-7 · LOW · Unread rows are misaligned with read rows by 20px

The unread indicator is an inline `<span class="unread-dot">` placed as a sibling *before*
`.list-item-identity`, so it displaces the text rather than sitting in a reserved gutter:

- Unread rows — name line starts at **x = 251px**
- Read rows — name line starts at **x = 231px**

Every row shifts horizontally the moment it is marked read. Visible in
`texts-threadlist-1920x720.png` (compare `+1801***8129` / `Mark Mackelprang` against
`51789` / `+1336***9432`). A reserved-width gutter would hold the text steady.

---

### F-8 · Not reproducible · Header/body desync under a degraded circuit

At one point the conversation pane showed **Darlann Romney**'s header above **Lynne Marley**'s
messages while Lynne's row was selected — a wrong-conversation render with obvious privacy
implications. Only one `.texts-conversation` pane existed in the DOM, so this was a genuine
content desync, not a duplicate-node artifact.

**However, it was driven by synthetic `element.click()` calls dispatched from
`page.evaluate`, on a circuit that had already degraded.** A clean reload followed by
**trusted** Playwright clicks never reproduced it — header and selected row stayed in sync
in every subsequent trial (`inSync: true`).

**Reported for completeness only. Not a confirmed defect.** Synthetic DOM events are a
weaker signal than trusted input for Blazor Server, and this should not be actioned without
independent reproduction using real user input.

Evidence: `texts-header-body-desync-artifact-1920x720.png`

---

## Voicemail tab — results from the interrupted session

Folded in so this pass is documented as a whole. These were confirmed in the earlier run
that was cut off before the Texts tab was opened; they are **carried forward as previously
recorded, not re-verified today** — except where noted.

| Check | Result |
|---|---|
| Feed renders 20 voicemail rows with real transcripts, sender numbers, dates | **PASS** |
| Filter shows `Voicemail 6` — 6 unread / 14 read | **PASS** |
| Read vs unread rows visually differentiated | **PASS** |
| Inline audio player loads with metadata | **PASS** |
| Play button + seek slider present | **PASS** |

Incidentally re-confirmed today from the **All** tab: real transcripts are flowing
(CVS Pharmacy, Allstate, AAA travel, Window Genie, plus personal messages), with a mix of
resolved contact names (Mark Mackelprang, Joe Lete) and raw E.164 numbers
(`+1937***6039`, `+1615***1164`, `+1801***8129`, `+1313***6471`, `+1919***1494`,
`+1919***7327`). Rows with no transcript render **"No transcript available."** — a proper
placeholder, not a blank.

The `Voicemail 6` badge behaves like the Texts badge (G-1): it is an unread count, not a
row count.

**The parser fix is working.** Both surfaces return real data where they previously
returned 0 items behind a clean HTTP 200.

---

## Environment notes

- **Console:** 0 errors, 0 warnings across the whole session (3 informational messages).
- **Network:** 0 failed requests. Only `/_blazor/initializers` (200) and
  `/_blazor/negotiate` (200) are non-static — all data flows over SignalR, so upstream GV
  failures are **not observable from the browser**. Any monitoring for F-1 must be
  server-side.
- **Screen dim:** the console dims the display after ~10 minutes idle. Expected ambient
  behaviour, not a defect. Visible in `texts-threadlist-scrolled-shortcodes-1920x720.png`.
- **Mark-read works:** the `MESSAGES` badge went 13 → 11 and the `Texts 2` badge cleared as
  threads were opened.

---

## Screenshots

All at 1920x720, in this directory.

| File | Shows |
|---|---|
| `texts-threadlist-1920x720.png` | Thread list top — short code `51789` and E.164 rows; unread dot alignment (F-7) |
| `texts-threadlist-scrolled-shortcodes-1920x720.png` | Thread list tail — `32665`, no load-more control (G-3); screen dimmed |
| `texts-conversation-shortcode-32665-1920x720.png` | Short-code thread with bubbles; duplicated header (G-6); disabled composer (F-3) |
| `texts-conversation-e164-unresolved-1920x720.png` | Unresolved E.164 thread; duplicated header (G-6) |
| `texts-conversation-mms-empty-body-1920x720.png` | MMS thread, resolved name, silently empty body (F-1) |
| `texts-silent-empty-body-resolved-contact-1920x720.png` | Correct in-sync header over a silently empty body (F-1) |
| `texts-error-couldnt-load-conversations-1920x720.png` | Thread-list error state with `Retry` (F-6) |
| `texts-header-body-desync-artifact-1920x720.png` | Header/body desync — **not reproducible**, see F-8 |

---

## Recommended disposition

**GV-7 is unblocked.** Its stated risk — layout damage from long non-dialable identifiers —
**did not materialise**: fallback rendering is graceful, and a 36-char ID is safe at
1920x720. Design against G-1 through G-9; note that no opaque 36-char ID is observable
live (G-3), so that case remains untested against real data.

**F-1 (HIGH) should be triaged separately from GV-7.** It is not a rendering concern and
blocks confident UAT of *any* conversation-level behaviour, including GV-7's own once it
ships. It likely needs a debugging pass with server-side logs on `radio`, since the failure
is invisible from the browser.

Suggested split:
- **GV-7 (design, unblocked):** G-6 header duplication, G-8 MMS preview prefix, F-2 empty-state copy, F-3 composer labelling.
- **New bug item (HIGH):** F-1 silent body-load failure + missing conversation error state.
- **Polish backlog (LOW):** F-4 overflow hardening, F-5 bubble truncation, F-7 unread alignment.
- **No action:** F-8 (not reproducible with trusted input).

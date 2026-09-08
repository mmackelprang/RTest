# PLAN — `GV-9` · Texts-surface polish: an overflow, a 20px jump, and a guard the dead copy never got

> **Row:** `GV-9`, [`docs/queue/GV-9.md`](../../docs/queue/GV-9.md). 📋 queued, `_plan TBD (small; **no longer CSS-only**)_`.
> **Branch:** `fix/gv-texts-polish-overflow-unread-align` (the row names it).
> **Depends on:** `GV-3` ✅ merged. `GV-8` ✅ merged (#461) — the guard idiom to copy. `GV-4` ✅ merged
> (#441) — wired the mark-read that made `F-7` visible.
> **Estimate:** **0.5 d.** §0.6 says what would push it to 1 d.
> **Spec:** [UAT F-4 / F-7](../../docs/uat/2026-07-31-gv-live-data/REPORT.md) ·
> [GV-8 UAT (guard provenance)](../../docs/uat/2026-07-31-gv8-error-state/REPORT.md) ·
> [handoff](../../docs/design-handoffs/HANDOFF-phone-dark-theme-and-scrollbars.md).
> **Planned against** `main` at **`084a6bbd`**. ⚠ **Every line number in the row itself was
> re-derived against that commit and MOST OF THEM HAD MOVED** — see §0.3, which is the most
> important section in this plan. Where a line is likely to move again it is quoted as well as
> numbered.
> **`D31` status:** ✅ assessed and unaffected, and the row already says so. None of the three items
> is send.
> **`PHN-4` ordering note:** ✅ **discharged, not pending.** See `C-205`.

---

## 0. Read this before Task 1

### 0.1 What this row is, in one paragraph

Three unrelated small defects that happen to share one surface, grouped so the file is opened once.
**`F-4`** — the conversation header's number line is the only one of its three siblings without
overflow handling, so a long identifier is not ellipsized. **`F-7`** — the unread dot is a real
in-flow flex child, so every list row's text slides 20px sideways the moment it is marked read.
**The third item** — the thread-list branch never received the `X == null` guard that `GV-8` shipped
in conversation mode, so a stale error flag would outrank thread rows that had actually arrived.
The first two are layout; the third is a one-line condition plus the test it never had. Nothing here
changes behaviour anyone can currently reach in production (§0.5), which is why the whole row is
consistency work and must not be re-filed as a bug fix.

### 0.2 The three items, traced

**`F-4` — the header number has no overflow handling.** `design-system.css:5958-5965`, two adjacent
rules inside the same flex column:

```css
.texts-conv-name {
  font-size: 16px; font-weight: 600; color: var(--text-high);
  white-space: nowrap; overflow: hidden; text-overflow: ellipsis;   /* ← all three */
}
.texts-conv-number {
  font-family: var(--font-mono); font-size: 12px; color: var(--text-medium);
  letter-spacing: 0.04em;                                           /* ← none of the three */
}
```

Both are children of `.texts-conv-title` (`:5957`), which already carries `min-width: 0` — the
precondition an ellipsis needs inside a flex column. So `.texts-conv-name` ellipsizes and
`.texts-conv-number` inherits the initial `white-space: normal` / `overflow: visible` /
`text-overflow: clip`. The third sibling on this surface, `.list-item-title` (`:649-656`), carries
all three as well. `.texts-conv-number` is the only one that does not. The fix is three declarations
copied from the rule directly above it.

**`F-7` — the dot displaces the text instead of occupying a gutter.** The 20px in the UAT
(`251px` unread vs `231px` read) is exactly reproducible from two declarations, and the arithmetic is
worth stating because it is what makes the fix a known quantity rather than a guess:

| Source | Declaration | Contribution |
|---|---|---|
| `design-system.css:5786` | `.unread-dot { width: 8px; … flex-shrink: 0 }` | 8px |
| `design-system.css:598` | `.list-item-touch { gap: 12px }` | 12px |
| | | **20px** |

`.unread-dot` is rendered inside an `@if`, so on a read row **the element does not exist** and
neither does its 20px. There is no CSS that can reserve space for an absent element by styling that
element — which is the whole reason this item is less trivial than it looks. §1 decides what to do
about that.

**The third item — the thread-list branch's missing `== null` guard.** The two modes of
`PhoneTextsPanel` now disagree:

| | conversation mode | thread-list mode |
|---|---|---|
| skeleton | `:41` `Messages == null && Loading` | `:136` `Threads == null && Loading` |
| error | `:59` **`Error && Messages == null`** | `:153` **`Error`** ← the defect |
| empty | `:73` `Messages != null && Messages.Count == 0` | `:161` `Threads == null \|\| Threads.Count == 0` |
| rows | `:83` `Messages != null` | `:170` `else` |

The canonical form is `Error && <collection> == null`, and it exists in three shipped places:
`PhoneMessagesPanel.razor:78` (voicemail), `PhoneMessagesPanel.razor:110` (threads), and
`PhoneTextsPanel.razor:59` (conversation, shipped by `GV-8`'s M-1). The thread-list branch is the
one hold-out.

### 0.3 ⚠ Anchor drift — read this before trusting a single number in the row

The row's prose predates `PHN-4` (#578) and `PHN-3` (#598), **both of which moved this file**.
`PhoneTextsPanel.razor` is now **377 lines**, not the 258 it was after `PHN-4`. Every anchor below
was re-read at `084a6bbd`. **Of the row's twelve anchors, only three are still correct: eight have
moved, and a ninth points at code that no longer exists.** The last table row is from the planning
brief rather than the queue row, and is listed for completeness.

| Row's anchor | Row's claim | Actual at `084a6bbd` | Verdict |
|---|---|---|---|
| `PhoneTextsPanel.razor:61` | the `GV-8` guard, "on `main` to copy from" | guard is at **`:59`**; `:61` is now a line *inside* its explanatory comment | **moved −2** |
| `PhoneTextsPanel.razor:162` | the bare `else if (Error)` | **`:153`**; `:162` is now inside the empty-state comment | **moved −9** |
| `PhoneTextsPanel.razor:175-177` | send-gated "New message" button inside the empty state | **already deleted by `PHN-4`.** `:175-177` is now the thread-row `<button>` opening tag | **discharged** — `C-205` |
| `PhoneTextsPanel.razor:191` | the dead `.unread-dot` site | **`:180`**; `:191` is now the `chevron_right` icon | **moved −11** |
| "29 lines below the bare `else if`" | distance dot ↔ error branch | `180 − 153` = **27** | **moved** |
| `PhoneMessagesPanel.razor:663` | the live `.unread-dot` | **`:662`** | **moved −1** |
| `PhoneMessagesPanel.razor:184` | the panel's only call site | **`:185`** (`:182-184` is its comment); the `@if (_openThreadId != null)` gate is `:180` | **moved −1**, claim holds |
| `PhoneMessagesPanel.razor:110` | canonical guard (threads) | `else if (ThreadsError && Threads == null)` | ✅ **exact** |
| `PhoneMessagesPanel.razor:78` | canonical guard (voicemail) | `else if (VoicemailError && Voicemails == null)` | ✅ **exact** |
| `VoicemailRow.razor:10` | third `.unread-dot` site | `<span class="unread-dot"></span>` | ✅ **exact** |
| `PhoneTextsPanelTests` `:124`, `:141`, `:197` | "the three places `Error` is set" | **four** places: `:174`, `:194`, `:210` (sets it *false*), `:246` | **moved, and the count is wrong** |
| `PhoneTextsPanelTests` `:32`, `:41`, `:68` | "the three thread-list-mode tests" | **four**: `:64`, `:73`, `:130`, `:141` | **moved, and the count is wrong** |
| `.list-item-subtitle` `:658-664`, `.msg-bubble` `:5852-5858` | (from the planning brief) | both exact | ✅ **exact** |

**The row's substantive claims all survive the drift.** The guard is still missing, the dot still
displaces, the number line still has no overflow rule, and — the one that mattered most to re-check —
**no test sets `Error` in thread-list mode in either direction.** The behaviour is genuinely
unasserted, so Task 5 must *add* an assertion rather than preserve one, exactly as the row says.

### 0.4 What the row claims, and what the code says

Three corrections. None kills the row; all three change how Builder should work.

1. **"Two LOW findings … both pure CSS."** `F-4` is pure CSS. **`F-7` is not**, on the obvious
   implementation. The natural fix — always render the span and hide it when read — is a markup
   change in three files *and* it breaks a shipped test (`C-206`). §1 finds a genuinely CSS-only
   route, but it costs one small markup edit in the dead copy to make the selector reachable. The
   row's "no longer CSS-only" caveat is therefore true for a **second** reason it does not mention.
2. **`F-7`: "the `unread-dot` is a sibling placed *before* `.list-item-identity`."** True at the two
   **live** sites. At the **dead** site (`PhoneTextsPanel.razor:180`) there is no
   `.list-item-identity` at all — the identity column is an unclassed `<div>` carrying an inline
   `style` that duplicates the class verbatim — and the dot precedes the **chip**, not the identity
   (`C-207`).
3. **"a 36-char opaque ID does not trigger it (measured, UAT G-4)."** Consistent with what the CSS
   says: `.texts-conv-title` has `min-width: 0` but no `overflow`, so a long unbreakable string
   *spills* rather than being clipped by this rule. The row's own "clips mid-character" wording
   describes an ancestor's clipping, not `.texts-conv-number`'s. Immaterial to the fix — the three
   declarations are right either way — but Builder should not go hunting for a clip that this rule
   does not perform (`C-208`).

### 0.5 Reachability — what a human could actually see change

Stated up front because it decides the UAT section and the auto-merge call.

| Item | Reachable in production today? |
|---|---|
| `F-4` | **No.** Needs a >60-char header identifier; the row records that no live data produces one, and `GV-7`'s `G-3` records that opaque 36-char IDs are not even reachable through the feed (no pagination past 20 threads). |
| `F-7` | **Yes.** Every unread→read transition in the messages feed, which `GV-4` wired. This is the only item with a live visual signature. |
| the guard | **No.** `PhoneTextsPanel`'s only call site is `PhoneMessagesPanel.razor:185`, inside `@if (_openThreadId != null)` (`:180`) — conversation mode only. The thread-list branch is dead code and the file says so at `:139-141`. |

Two items are unreachable. That is not an argument for dropping them — the file's own comment
(`:140-141`) says the dead copy is *"kept in sync so it isn't the next thing someone copies"*, which
is the project's stated policy for exactly this branch — but it is the reason UAT cannot be the gate
for two of the three (§4.5).

### 0.6 The estimate

**0.5 d**, assuming one Builder cycle.

| | |
|---|---|
| Task 1 — `F-4`, three declarations | 5 min |
| Task 2 — `F-7` rule + the arithmetic comment | 30 min |
| Task 3 — dead-copy sync (the markup the selector needs) | 15 min |
| Task 4 — the guard, one line | 5 min |
| Task 5 — four tests | 45 min |
| Build + `Radio.Web.Tests` + full suite | 30 min |
| Static CSS harness + screenshots (§4.5) | 30 min |
| Docs, PR body, review round | 45 min |

**What pushes it to 1 d:** if the owner declines the "every feed row moves 20px" consequence in
§1.2 Option A, `F-7` needs a different shape and a design conversation, and should be split out
rather than re-decided by Builder.

### 0.7 Constraints found while planning — numbering continues from `C-202` (`OPS-3`)

**`C-203` — the row's line numbers are stale; §0.3 is the corrected set.** Nine of thirteen anchors
had moved under `PHN-3` and `PHN-4`. Builder must work from §0.3, not from the row. This is the
`PHN-3` failure mode repeating: that plan was written before `PHN-4` and three of its prescribed
edits referenced deleted members, so they would not have compiled.

**`C-204` — the files are under `Components/Pages/`, not `Components/Shared/`.**
`PhoneTextsPanel.razor`, `PhoneMessagesPanel.razor`, `MessageBubble.razor` and `VoicemailRow.razor`
all live in `src/Radio.Web/Components/Pages/`. Only `PhoneDevTray.razor` and `PhoneStatusHero.razor`
are in `Shared/`. A `Shared/`-rooted path will simply not resolve.

**`C-205` — the `PHN-4` ordering note is DISCHARGED, not pending.** The row's ⚠ says
`PhoneTextsPanel.razor:175-177` holds a send-gated "New message" button inside the empty state that
`PHN-4` will delete, and that the two rows must never run concurrently. `PHN-4` merged as #578; the
button is gone and the empty state at `:161-169` carries a comment saying so. **There is no
remaining ordering constraint with `PHN-4`.** Builder must not go looking for a button to preserve.

**`C-206` — "always render the dot and hide it when read" BREAKS A SHIPPED TEST, and it would
compile clean.** `VoicemailRowTests.cs:38` asserts `Assert.Empty(cut.FindAll(".unread-dot"))` for a
read voicemail. Any implementation that emits the span unconditionally fails that test at runtime
with a green build. It would also make `VoicemailRowTests.cs:29` and
`PhoneTextsPanelTests.cs:151` — both `.unread-dot`-presence assertions — **vacuous**, passing for
read and unread alike. This is the `GV-6` shape (a guard removed with every test still green) and
the `UI-6` shape (a change that compiles at 47/0 and fails 15 tests at runtime) in one item. §1.2
rejects that implementation for this reason.

**`C-207` — the dead thread row is structurally different from the two live ones, in two ways.**
`PhoneTextsPanel.razor:175-191`: (a) its identity column is `<div style="flex: 1; min-width: 0;
display: flex; flex-direction: column;">` — an inline duplicate of `.list-item-identity`
(`design-system.css:5808`) minus its `gap: 1px` — so **no class selector can reach it**; and (b) its
`.unread-dot` precedes the chip, where both live sites put the dot *after* the chip. Task 3 fixes
both, and Task 2's rule does not work on this row until it does.

**`C-208` — `.texts-conv-number` does not clip; it spills.** `overflow: visible` is the computed
value, and `.texts-conv-title` sets no `overflow`. Whatever clipping the UAT observed came from an
ancestor. The fix is unchanged; the mental model should be.

**`C-209` — `F-7` has no automated gate and cannot have one: bUnit does not evaluate CSS.**
`RenderComponent` produces a DOM, not a layout — there is no computed style, no box model and no
`:has()` evaluation. No test in this repository can observe a 20px shift. What Task 5 *can* pin is
the **structural precondition** the rule depends on (dot is a direct child; identity column carries
the class), which is what makes a silent regression detectable. Stated so nobody cites a green
suite as evidence that `F-7` works.

**`C-210` — the Dev Tray cannot seed SMS threads, so the texts feed cannot be populated locally.**
`PhoneDevTray.razor` simulates handset and call events only — `Sms`, `Text`, `Thread` and
`Voicemail` appear nowhere in it. The feed's data comes from the GV bridge on the box. A local
`dotnet run` of `Radio.Web` therefore renders this surface's **error or empty** state, never rows.
§4.5 is built around that fact rather than around a browser walk that cannot happen.

**`C-211` — do NOT add truncation to `.msg-bubble` or `.msg-text`, and the reason is `PHN-3`.**
`.msg-bubble` (`design-system.css:5852-5858`) has `max-width: 72%; word-break: break-word` and no
`text-overflow`, line-clamp, max-height or `nowrap`; `.msg-text` (used at `MessageBubble.razor:107`)
has **no CSS rule at all**. Since `PHN-3` merged, that text is read aloud —
`PhoneTextsPanel.razor:113-116` passes the message to `MessageBubble`, which speaks
`SmsMessageDto.Text`. A **display** truncation there would be invisible to the speak path: the
bubble would show `…` while the console reads the whole message to the room. A **data** truncation
would not diverge, but would silently shorten what is spoken. Neither is in this row's scope. This
plan changes nothing about the bubble; §6.1 records it as a declined item so the next reader does
not think it was missed.

**`C-212` — `:has()` is safe on this appliance and degrades to today's behaviour.** The kiosk runs
Chrome 151 (`CLAUDE.md` § *Remote UI driving*); `:has()` shipped in Chrome 105. If it were ever
unsupported the rule is simply dropped and the surface behaves exactly as it does today — the bug,
not a new one. No fallback is required and none should be written.

**`C-213` — express the gutter as `calc(8px + 12px)`, NOT with `--sp-*` tokens.** The two source
declarations use literals: `.unread-dot { width: 8px }` and `.list-item-touch { gap: 12px }`.
`--sp-2` (8px) and `--sp-3` (12px) happen to hold the same values, but nothing couples them to those
declarations — retokenising `--sp-2` would move the gutter without moving the dot, silently
reintroducing a misalignment. The literal `calc()` plus a comment naming both source lines is the
honest encoding. This is the one place in this plan where the brief's "reuse existing tokens" is
deliberately not followed, and this is why.

### 0.8 Things Builder must NOT do

- ⛔ **Do not touch the live box.** No SSH, no `curl`, no deploy. The appliance is unattended.
- ⛔ **Do not re-file `F-4` as a bug.** The row is explicit: no live data triggers it. It is
  consistency hardening.
- ⛔ **Do not "fix" the thread-list empty branch** (`:161`, `Threads == null || Threads.Count == 0`)
  to match `PhoneMessagesPanel`'s `Threads is { Count: 0 }`. They differ, the difference is
  pre-existing, and changing it changes what a null-with-no-error list renders. §6.2.
- ⛔ **Do not implement `F-7` by always rendering the dot.** `C-206`.
- ⛔ **Do not touch `.msg-bubble` / `.msg-text`.** `C-211`.
- ⛔ **Do not restructure the thread-row markup beyond Task 3.** `GV-7` owns that row's design (§5).

---

## 1. Decision — how `F-7` reserves the gutter

### 1.1 The problem, stated exactly

On a read row the `.unread-dot` element **does not exist**. Reserving its 20px therefore cannot be
done by styling the dot. Something else must hold the space, and the options differ in what they
cost.

### 1.2 The three options

**Option A — reserve the gutter on every feed row, via `:has()`. ✅ Recommended.**

```css
.phone-messages-feed .list-item-touch:not(:has(> .unread-dot)) > :is(.list-item-identity, .vm-row-main),
.texts-thread-list   .list-item-touch:not(:has(> .unread-dot)) > .list-item-identity {
  margin-left: calc(8px + 12px);
}
```

A row that has no dot pushes its identity column right by exactly the width the dot would have
occupied. Zero markup change at the two live sites, zero test churn, and `.unread-dot` keeps meaning
"this row is unread" — so the three existing `.unread-dot` assertions stay honest (`C-206`).

**Its cost, stated plainly: read *call* rows also move 20px right.** Call rows never carry a dot, so
they gain the gutter too. That is a visual change `F-7` did not ask for. It is also, on inspection,
the correct outcome: the feed interleaves calls, voicemail and texts, and today a read text row and
a call row sit at `x=231` while an unread row sits at `x=251`. Option A puts **every** row in the
feed on one left edge at `x=251`. The alternative — fixing only the kinds that can carry a dot —
trades an intermittent jump for a permanent two-edge feed, which is worse.

**Option B — scope the rule to text and voicemail rows only.** Fixes the jump; leaves call rows at
`x=231` and text/voicemail rows at `x=251`, permanently. Rejected: it converts a transient
misalignment into a standing one, on the surface whose whole redesign (handoff §Issue 4, "a shared
44px identity chip gives calls the same left spine as voicemail / texts") was about getting these
three kinds onto one spine.

**Option C — take the dot out of flow** (`position: absolute` in the row's 16px left padding, row
`position: relative`). Nothing shifts and no gutter is needed. Rejected: it moves the dot from
"after the chip" to the far left edge, contradicting the handoff and
`PhoneMessagesPanel.razor:654-656`'s own comment (*"Unread dot follows the chip (matches the
voicemail row)"*). That is a design change, and design changes on this surface belong to `GV-7`.

### 1.3 What Option A requires, and the one thing that must be checked

The selector depends on two structural facts:

1. `.unread-dot` is a **direct child** of `.list-item-touch` — true at all three sites.
2. the identity column is a **direct child** of `.list-item-touch` **and carries a class** —
   true at the two live sites (`.list-item-identity`, `.vm-row-main`), **false at the dead site**
   (`C-207`). Task 3 fixes it.

Both are markup facts, so both are assertable in bUnit even though the CSS is not (`C-209`). Task 5
pins them.

### 1.4 Where the rule goes

Immediately after the `.unread-dot` block at `design-system.css:5790`, because that is where a
reader looking at the dot will look for its layout contract.

---

## 2. Tasks

### Task 1 — `F-4`: give the header number the overflow handling its siblings have

**File:** `src/Radio.Web/wwwroot/css/design-system.css`

Replace the rule at `:5962-5965`:

```css
.texts-conv-number {
  font-family: var(--font-mono); font-size: 12px; color: var(--text-medium);
  letter-spacing: 0.04em;
}
```

with:

```css
/* GV-9 / UAT F-4 — the three declarations its two siblings already carry
   (.texts-conv-name :5958-5961, .list-item-title :649-656). Without them this
   line inherits white-space: normal / overflow: visible / text-overflow: clip
   and a long identifier spills instead of ellipsizing. The ellipsis works
   without touching the parent because .texts-conv-title (:5957) already sets
   min-width: 0 — the precondition it needs inside a flex column.
   ⚠ This is a DISPLAY truncation of the header identifier only. It renders
   U+2026 and does not alter any string; nothing reads this element. Do not
   extend the same treatment to .msg-bubble / .msg-text — since PHN-3 that text
   is spoken, and a display truncation there would diverge from the utterance
   (plan C-211). */
.texts-conv-number {
  font-family: var(--font-mono); font-size: 12px; color: var(--text-medium);
  letter-spacing: 0.04em;
  white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
}
```

### Task 2 — `F-7`: reserve the unread gutter

**File:** `src/Radio.Web/wwwroot/css/design-system.css`

Insert immediately after the `.unread-dot` block (currently ending `:5790`):

```css
/* GV-9 / UAT F-7 — reserved unread gutter.
   The dot is a real in-flow flex child rendered inside an @if, so on a READ row
   it does not exist and neither does the space it occupied: every row's text
   slid 20px sideways the moment it was marked read (measured 251px unread vs
   231px read, at 1920x720). Since no CSS can style an absent element, the
   dotless row reserves the space instead.

   The 20px is not a magic number — it is the dot's own width plus the row's
   flex gap, and it must track BOTH of these if either changes:
     .unread-dot   { width: 8px }   design-system.css:5786
     .list-item-touch { gap: 12px } design-system.css:598
   ⚠ Deliberately NOT var(--sp-2) + var(--sp-3). Those tokens hold 8px and 12px
   today, but neither source declaration uses them, so retokenising --sp-* would
   move this gutter without moving the dot (plan C-213).

   Scoped to the two phone containers on purpose: .list-item-touch is also used
   by FileBrowserDialog and QueueHistoryPanel, which have no dots and must not
   gain a gutter.

   ⚠ This rule is invisible to the test suite — bUnit renders a DOM, not a
   layout (plan C-209). Its two structural preconditions ARE tested: the dot is
   a direct child of the row, and the identity column is a direct child that
   carries a class. If a future change nests either one deeper, this rule stops
   matching SILENTLY and the 20px jump returns with a green suite. See
   PhoneTextsPanelTests.ThreadRow_KeepsTheStructureTheUnreadGutterRuleDependsOn. */
.phone-messages-feed .list-item-touch:not(:has(> .unread-dot)) > :is(.list-item-identity, .vm-row-main),
.texts-thread-list   .list-item-touch:not(:has(> .unread-dot)) > .list-item-identity {
  margin-left: calc(8px + 12px);
}
```

### Task 3 — sync the dead thread row to the live ones

**File:** `src/Radio.Web/Components/Pages/PhoneTextsPanel.razor`

Two changes to the block at `:174-191`, both required by `C-207`: the dot moves after the chip to
match both live sites, and the inline-styled div gains the class it duplicates — which is also what
makes Task 2's selector reach this row at all.

Replace:

```razor
        var captured = thread;
        <button type="button"
                class="list-item-touch @(captured.ThreadId == SelectedThreadId ? "list-item-active" : "")"
                @onclick="@(() => OnOpenThread.InvokeAsync(captured.ThreadId))">
          @if (captured.HasUnread)
          {
            <span class="unread-dot" aria-hidden="true"></span>
          }
          <span class="phone-pill cyan" aria-hidden="true">
            <RadzenIcon Icon="chat_bubble" />
          </span>
          <div style="flex: 1; min-width: 0; display: flex; flex-direction: column;">
            <span class="list-item-title">@ResolveThreadName(captured)</span>
            <span class="list-item-subtitle">@PreviewText(captured)</span>
          </div>
```

with:

```razor
        var captured = thread;
        <button type="button"
                class="list-item-touch @(captured.ThreadId == SelectedThreadId ? "list-item-active" : "")"
                @onclick="@(() => OnOpenThread.InvokeAsync(captured.ThreadId))">
          @* GV-9: dot AFTER the chip, matching both live sites
             (PhoneMessagesPanel.razor:660-663, VoicemailRow.razor:8-11) — it used to
             precede the chip here, which is a third layout for the same row. Dead in
             production (this panel is only ever hosted in conversation mode) but kept
             in sync so it isn't the next thing someone copies. *@
          <span class="phone-pill cyan" aria-hidden="true">
            <RadzenIcon Icon="chat_bubble" />
          </span>
          @if (captured.HasUnread)
          {
            <span class="unread-dot" aria-hidden="true"></span>
          }
          @* GV-9: .list-item-identity replaces an inline style that duplicated it
             verbatim (design-system.css:5808, which also adds gap: 1px). The class is
             load-bearing, not tidying: the F-7 gutter rule selects the identity column
             by class, and an unclassed div is unreachable by any selector. *@
          <div class="list-item-identity">
            <span class="list-item-title">@ResolveThreadName(captured)</span>
            <span class="list-item-subtitle">@PreviewText(captured)</span>
          </div>
```

⚠ **The `gap: 1px` that `.list-item-identity` adds is a real, intended 1px difference** from the
inline style it replaces — it is what the two live rows already use between their title and
subtitle. That is the point of the sync, not an accident.

### Task 4 — the guard

**File:** `src/Radio.Web/Components/Pages/PhoneTextsPanel.razor`

Replace `:153`:

```razor
    else if (Error)
```

with:

```razor
    @* GV-9: content outranks a stale error flag — the canonical shape on this
       surface is XError && X == null (PhoneMessagesPanel.razor:78 voicemail,
       :110 threads), and GV-8's M-1 shipped exactly this one branch in
       conversation mode at :59. The thread list was the hold-out. Without the
       Threads == null guard a stale Error would outrank rows that had actually
       arrived — the same lie GV-8 removed, one level up.
       Dead in production today (this panel is only ever hosted in conversation
       mode, PhoneMessagesPanel.razor:180/185), fixed under the file's own
       stated policy for this branch at :139-141. *@
    else if (Error && Threads == null)
```

Nothing else in the chain changes. The resulting behaviour, stated in full so the reviewer can check
it against the table in §0.2:

| `Threads` | `Loading` | `Error` | branch | changed? |
|---|---|---|---|---|
| `null` | `true` | any | skeleton `:136` | no |
| `null` | `false` | `true` | error `:153` | no |
| `null` | `false` | `false` | empty `:161` | no |
| `[]` | any | `true` | **empty `:161`** | **yes** (was error) |
| `[]` | any | `false` | empty `:161` | no |
| `[t1]` | any | `true` | **rows `:170`** | **yes** (was error) |
| `[t1]` | any | `false` | rows `:170` | no |

Both changed rows match conversation mode's behaviour for the same inputs, and both match
`PhoneMessagesPanel.razor:110`/`:118`. That three-way agreement is the point of the change.

### Task 5 — tests

**File:** `tests/Radio.Web.Tests/Components/PhoneTextsPanelTests.cs`

All four reuse the existing `Register(available: true)` helper (`:39-62`) — no new DI is needed.
The `SmsThreadDto` positional signature was re-read at `084a6bbd`
(`src/Radio.Web/Models/ApiModels.cs:1147-1153`): `(string ThreadId, string CounterpartyNumber,
string? CounterpartyName, DateTime LastMessageAt, bool HasUnread, string? LastMessagePreview)`.

Append after `LoadedThreads_RenderRows` (`:152`):

```csharp
  // ── GV-9: the thread-list branch's missing == null guard ───────────────────
  //
  // ⚠ These are the FIRST tests to set Error in thread-list mode in either
  // direction. Before GV-9 the four tests that set Error (:174, :194, :210,
  // :246) all set OpenThreadId too, and the four thread-list-mode tests
  // (:64, :73, :130, :141) never set Error — so the branch was unasserted, not
  // covered. The row's deferral note said otherwise; the code says this.

  [Fact]
  public void ThreadList_ShowsThreads_WhenErrorSetButThreadsArrived()
  {
    // ⭐ THE headline gate for this row, and the one to run the mutation against.
    // Mutation: revert :153 to a bare `else if (Error)`. BOTH assertions below
    // must then fail — the error copy appears and the row content does not.
    // Same shape as GV-8's Conversation_ShowsMessages_WhenErrorSetButMessages-
    // Arrived (:181), one level up: a stale error flag must not outrank content
    // that has actually arrived.
    Register(available: true);
    var cut = RenderComponent<PhoneTextsPanel>(p => p
      .Add(x => x.Threads, new List<SmsThreadDto>
        { new("t1", "+15551234567", "Mom", DateTime.UtcNow, true, "see you soon") })
      .Add(x => x.Error, true));

    Assert.Contains("Mom", cut.Markup);
    Assert.Contains("see you soon", cut.Markup);
    Assert.DoesNotContain("Couldn't load conversations.", cut.Markup);
  }

  [Fact]
  public void ThreadList_ShowsError_WhenErrorSetAndNothingLoaded()
  {
    // The other side of the coin, and the reason the fix is a GUARD and not a
    // deletion. Mutation: delete the :153 branch entirely — this fails while the
    // test above still passes, which is what distinguishes the two.
    Register(available: true);
    var cut = RenderComponent<PhoneTextsPanel>(p => p
      .Add(x => x.Threads, (List<SmsThreadDto>?)null)
      .Add(x => x.Error, true));

    Assert.Contains("Couldn't load conversations.", cut.Markup);
    Assert.Contains("Retry", cut.Markup);
    Assert.DoesNotContain("No conversations yet", cut.Markup);
  }

  // ── GV-9 / F-7: the structure the unread-gutter CSS rule depends on ────────

  [Fact]
  public void ThreadRow_OmitsTheDot_WhenRead()
  {
    // ⚠ The invariant the F-7 rule is built on: .unread-dot present <=> unread.
    // LoadedThreads_RenderRows (:141) already asserts the positive; without this
    // negative, an implementation that always emitted the span would make BOTH
    // that assertion and VoicemailRowTests.cs:29 vacuous while VoicemailRow-
    // Tests.cs:38 failed at runtime on a green build (plan C-206).
    Register(available: true);
    var cut = RenderComponent<PhoneTextsPanel>(p => p
      .Add(x => x.Threads, new List<SmsThreadDto>
        { new("t1", "+15551234567", "Mom", DateTime.UtcNow, false, "see you soon") }));

    Assert.Empty(cut.FindAll(".unread-dot"));
    Assert.Contains("Mom", cut.Markup);
  }

  [Fact]
  public void ThreadRow_KeepsTheStructureTheUnreadGutterRuleDependsOn()
  {
    // ⚠ bUnit evaluates no CSS (plan C-209), so this does NOT prove the 20px
    // gutter works — nothing in this repository can. What it pins is the two
    // structural facts the selector needs, which is what makes a silent
    // regression loud:
    //   .phone-messages-feed .list-item-touch:not(:has(> .unread-dot))
    //     > :is(.list-item-identity, .vm-row-main)
    // Nest the dot or the identity column one level deeper and the rule stops
    // matching with every test still green.
    Register(available: true);
    var cut = RenderComponent<PhoneTextsPanel>(p => p
      .Add(x => x.Threads, new List<SmsThreadDto>
        { new("t1", "+15551234567", "Mom", DateTime.UtcNow, true, "see you soon") }));

    var row = cut.Find(".list-item-touch");
    Assert.NotNull(row.QuerySelector(":scope > .unread-dot"));
    Assert.NotNull(row.QuerySelector(":scope > .list-item-identity"));
  }
```

⚠ **If `:scope` is unsupported by the AngleSharp build in use**, assert the same two facts by
walking `row.Children` and checking `ClassList` — do **not** weaken the assertions to
`cut.FindAll(".unread-dot")`, which would no longer pin *direct-child* placement and would defeat
the entire purpose of this test.

### Task 6 — docs

- `design/FUTURE-WORK.md` — no entry. Nothing is stubbed or deferred by this row.
- `design/INTEGRATIONS.md` — no entry. No integration surface changes.
- The queue row's Plan cell — §7.

---

## 3. Ordering

Tasks 1 and 4 are independent of everything. **Task 3 must precede Task 2's verification** — until
the dead row carries `.list-item-identity`, Task 2's selector cannot match it, and a harness run
would show a false negative on that row. Task 5 depends on Tasks 3 and 4.

Recommended: **4 → 5 (guard tests) → 1 → 3 → 2 → 5 (structure tests) → gates.** Landing the guard
and its tests first means the one item with a real behavioural assertion is green before any layout
work starts.

---

## 4. Test plan

### 4.1 The mutations, and what must fail under each

**This is the section to check against `GV-6`'s vacuous headline test and `PHN-3`'s unfalsifiable
one.** Each mutation is specified with the assertion that must break.

| # | Mutation | Test that must FAIL | Why it cannot be vacuous |
|---|---|---|---|
| **M1** | `:153` `Error && Threads == null` → `Error` | `ThreadList_ShowsThreads_WhenErrorSetButThreadsArrived` | The fixture sets `Error: true` **and** a one-element `Threads`. Under the mutation the error branch wins, so `"Couldn't load conversations."` appears (fails `DoesNotContain`) **and** `"Mom"` / `"see you soon"` do not render (fails both `Contains`). Three independent assertion failures, and the fixture reaches the branch under test by construction. |
| **M2** | delete the `:153` branch entirely | `ThreadList_ShowsError_WhenErrorSetAndNothingLoaded` | `Threads: null, Error: true` then falls to the empty branch, so `"No conversations yet"` appears and `"Couldn't load conversations."` does not. M2 fails while M1's test still passes — which is what proves the fix is a guard and not a deletion. |
| **M3** | emit `<span class="unread-dot">` unconditionally | `ThreadRow_OmitsTheDot_WhenRead` **and** `VoicemailRowTests.cs:38` | Both assert `Assert.Empty`. This is the mutation `C-206` exists to catch, and it is the one a reviewer is most likely to propose as "simpler". |
| **M4** | wrap the identity column in an extra `<div>`, or move the dot inside it | `ThreadRow_KeepsTheStructureTheUnreadGutterRuleDependsOn` | The `:scope >` assertions fail. Without this test M4 is **completely silent**: build green, suite green, gutter gone. |

**Explicitly NOT mutation-covered: the `F-7` CSS rule itself and the `F-4` declarations.** `C-209` —
no test in this repository evaluates CSS. Claiming otherwise is the failure this plan is written to
avoid. Their gate is §4.4.

### 4.2 Existing tests that must still pass unchanged

`PhoneTextsPanelTests` `:64`, `:73`, `:130`, `:141` (the four thread-list-mode tests — none sets
`Error`, so Task 4 cannot affect them), `:151`'s `.unread-dot` assertion (Task 3 keeps the dot
conditional), and all of `VoicemailRowTests` and `PhoneMessagesFeedRowTests` (untouched by every
task).

### 4.3 Gates

```bash
dotnet build --configuration Release          # 0 warnings; warnings are errors in Release
dotnet test RadioConsole.sln -c Release > /tmp/gv9.log 2>&1; echo "exit=$?"
grep -E "Passed!|Failed!|error" /tmp/gv9.log
```

⚠ **Never pipe `dotnet test` into `tail`** — `CLAUDE.md` records a run that exited `0` with five
tests failing. Read the per-project summary lines.

Known-failing on Windows and **not** a regression: four `SrcVariableResamplerTests`
(`libsamplerate.so.0`, `TEST-5`) and `NwsObservationIntegrationTests.RealNwsCall_*` (live network,
`Category=Integration`, CI-excluded).

Targeted run while iterating:

```bash
dotnet test tests/Radio.Web.Tests -c Release --filter "FullyQualifiedName~PhoneTextsPanelTests"
```

### 4.4 Nothing here touches async

`CLAUDE.md` § *Test Timing* is satisfied trivially: no task adds a timer, a `Task.Delay`, or a
poll. All four new tests are synchronous `RenderComponent` calls with no rendezvous to get wrong.
Stated rather than assumed, because the brief asks for it.

### 4.5 UAT — what is possible locally, and what is not

⚠ **The live box is off-limits for this cycle** (unattended). Everything below is local.

**What CANNOT be done locally, and why.** `C-210`: the Dev Tray simulates handset and call events
only, and the texts feed's data comes from the GV bridge on the box. A local `dotnet run --project
src/Radio.Web` renders this surface's error or empty state, never rows. **There is no local browser
walk that populates the messages feed.** Any claim of "UAT passed" that implies otherwise is false.

**What each item can honestly be gated on:**

| Item | Gate |
|---|---|
| the guard | **Unit tests only** — and that is sufficient, because the branch is unreachable in production (§0.5). No UAT could exercise it even on the box. |
| `F-4` | **Static harness.** No live data produces a >60-char header identifier, so even on the box this is unobservable. |
| `F-7` | **Static harness**, plus an owner look on the box in a later cycle. |

**The static harness — the honest instrument for a pure-CSS layout change.** Build a throwaway
local HTML file (in the scratchpad, **not** committed) that links the real
`src/Radio.Web/wwwroot/css/design-system.css` and reproduces the four row kinds **verbatim from the
`.razor` sources**: a call row, a read voicemail row, an unread voicemail row, a read text row and
an unread text row, inside `<div class="phone-messages-feed">`; plus a `.texts-conv-header` with a
70-character number. Open at 1920×720 and measure the left edge of each title.

- **`F-7` passes** when all five rows report the same title `x`.
- **`F-4` passes** when the 70-char number renders a single line ending in `…` (U+2026) rather than
  spilling or wrapping.

**Why this is sound rather than a dodge:** the thing under test *is* CSS, and the harness feeds it
the real stylesheet. Its one weakness — that the markup is copied rather than rendered by Blazor —
is exactly what Task 5's `ThreadRow_KeepsTheStructureTheUnreadGutterRuleDependsOn` covers, by
pinning the real render tree's class names and direct-child placement. **The harness and that test
are only sound as a pair; neither is sufficient alone.** Say so in the PR body.

**Attach the before/after screenshots.** The "before" is the same harness against `main`'s CSS, and
it is what makes the 20px claim checkable by a reviewer who did not run it.

### 4.6 The one thing to flag for the owner

Option A moves **read call rows 20px right** (§1.2). That is a deliberate, defensible consequence
and not a side effect, but it is a visible change to a surface `F-7` never mentioned. It belongs in
the PR body under its own heading, with the harness screenshot, so it is a decision the owner sees
rather than discovers.

---

## 5. Coordination with `GV-7` — what would collide

`GV-7` is 📋 queued, **unplanned and unclaimed**, and shares this surface. Its scope
(`docs/queue/GV-7.md`) is *"thread-list row … conversation header … bubble/meta treatment"* for
non-dialable senders. The overlap is direct on both CSS items and nil on the guard.

| | `GV-9` touches | `GV-7` will touch | Collision |
|---|---|---|---|
| `.texts-conv-number` (`:5962`) | adds `nowrap` + `overflow` + `ellipsis` | **the same rule** — its "conversation header" scope is *"what occupies the name line when there is no name and the identifier is 36 chars"* | ⚠⚠ **Direct, and possibly contradictory.** `GV-9` says "ellipsize at the end". A 36-char opaque ID's *tail* is often what distinguishes it, so `GV-7` may well conclude it should wrap to two lines or middle-truncate. Those are mutually exclusive treatments of one rule. |
| thread-row markup (`:174-191`) | Task 3: dot moves after the chip, identity column gains its class | the row's name line, font, truncation and layout at 1920×720 | ⚠ **Direct.** Task 3's edits are inside the block `GV-7` redesigns. Trivially rebasable, but a rebase that silently drops the class re-breaks the gutter. |
| the `F-7` gutter rule | adds `:has(> .unread-dot)` + `> :is(.list-item-identity, .vm-row-main)` | may restructure the row | ⚠⚠ **The dangerous one, because it fails SILENTLY.** The rule is coupled to *direct-child* placement. If `GV-7` wraps the identity column or moves the dot inside it, the selector stops matching: no build error, no test failure, and the 20px jump returns. `ThreadRow_KeepsTheStructureTheUnreadGutterRuleDependsOn` is the only thing that would catch it — which is why that test exists and why `GV-7`'s planner must be told not to delete it as "an odd structural assertion". |
| the `== null` guard (`:153`) | one condition | nothing | **None.** Different concern. |

**Recommendation: `GV-9` first, then `GV-7`** — and this is the same argument `GV-7`'s own row has
already accepted once. Its Depends-on cell says to *"prefer `GV-8` first: `GV-8` rewrites the
conversation pane's state branches, which this row also touches — and designing … on top of a pane
that cannot express 'failed' would bake the F-1 confusion into the new design."* `GV-9` does the
identical thing one level up, for the thread-list branches. Designing the non-dialable-sender
treatment on a list that cannot express "failed", and on rows that jump 20px when marked read, would
bake both into the new design.

**If they must run concurrently:** they must not. Both edit `PhoneTextsPanel.razor:174-191` and
`.texts-conv-number`. Claim in either order, never at the same time.

**If `GV-7` lands first:** re-derive `F-4` before applying it — `GV-7` may already have answered it,
possibly in the opposite direction, and blindly re-adding `nowrap` would undo a deliberate choice.

---

## 6. Deliberately not done

### 6.1 Truncation on `.msg-bubble` / `.msg-text`

`C-211`. `.msg-bubble` has no `text-overflow`, no line-clamp, no max-height and no `nowrap`;
`.msg-text` has no rule at all. Both are load-bearing absences now that `PHN-3` reads
`SmsMessageDto.Text` aloud: a **display** truncation would show `…` while the console spoke the
whole message, and a **data** truncation would shorten the utterance. Either needs a design decision
about the speak feature, which is not this row's to make. Recorded here so the next reader knows it
was considered and declined, not overlooked.

### 6.2 Aligning the thread-list empty branch with `PhoneMessagesPanel`

`PhoneTextsPanel.razor:161` is `Threads == null || Threads.Count == 0`; `PhoneMessagesPanel.razor:118`
is `Threads is { Count: 0 }`. They differ in what a null-with-no-error list renders — an empty state
here, nothing there. Pre-existing, unrelated to the guard, and changing it alters a reachable
rendering decision. Out of scope.

### 6.3 The dead row's `.list-item-meta` vs the live rows' `.list-item-meta-stack`

A third divergence in the dead copy (`:189` uses a bare `.list-item-meta` and a `"g"`-format
timestamp; the live rows use `.list-item-meta-stack` with `FormatFeedTimestamp`). Not fixed: it does
not affect the gutter rule, and syncing it is a cosmetic change to unreachable code that `GV-7` will
likely rewrite anyway. Task 3 fixes only the two divergences that Task 2 structurally requires.

### 6.4 Extracting the 20px gutter to a `:root` token

`C-213`. A token would need to track two literals in two unrelated rules; the `calc()` plus a comment
naming both source lines is more honest and no less maintainable at one use site.

---

## 7. Queue row wording

⛔ **This plan does not edit `docs/BUILDER_QUEUE.md` or `docs/queue/GV-9.md`** — other agents are
editing queue files concurrently. The wording below is for whoever applies it.

**`docs/BUILDER_QUEUE.md` § Queue, `GV-9` row — Plan cell**, replacing `_plan TBD (small; **no longer
CSS-only** — two CSS fixes plus one `.razor` guard and its missing test)_`:

> [`design/plans/GV-9-texts-surface-polish.md`](../design/plans/GV-9-texts-surface-polish.md) — 0.5 d

**`docs/queue/GV-9.md`, suggested additions** (as a dated note; do not rewrite the verbatim Detail):

> ✅ **PLANNED 2026-09-08 against `main` `084a6bbd`.** ⚠ **Only three of this row's twelve line
> anchors are still correct — eight moved under `PHN-3` (#598) and `PHN-4` (#578), and a ninth
> points at deleted code** — the plan's §0.3 carries the
> corrected set; do not work from the numbers in the Detail below. Three corrections to the row's
> own framing: (1) the `PHN-4` ordering note is **discharged** — the "New message" button was
> deleted by #578 and there is no remaining constraint; (2) `F-7` is **not** "pure CSS" on the
> obvious implementation — always rendering the dot breaks `VoicemailRowTests.cs:38` and makes two
> other assertions vacuous, so the plan uses a `:has()` gutter plus one markup sync in the dead
> copy; (3) `F-7`'s fix moves **every** feed row 20px right, call rows included — a deliberate
> consequence the owner should see in the PR. `GV-7` shares this surface: **prefer `GV-9` first**,
> for the same reason this row's neighbour already prefers `GV-8` first.

---

## 8. Self-review

### 8.1 Verified first-hand at `084a6bbd`

- Every anchor in §0.3, read out of the tree file by file.
- `SmsThreadDto`'s positional signature (`ApiModels.cs:1147-1153`) — so Task 5's fixtures compile.
  This is the specific check `PHN-3`'s plan skipped.
- All three `.unread-dot` render sites, and all three `.unread-dot` assertions in the test suite
  (`VoicemailRowTests.cs:29`, `:38`, `PhoneTextsPanelTests.cs:151`) — the basis for `C-206`.
- That no test sets `Error` in thread-list mode, by reading all eighteen test methods in
  `PhoneTextsPanelTests.cs` end to end rather than grepping for `Error`. Grepping would have found
  the four `.Add(x => x.Error, …)` sites but not established that each also sets `OpenThreadId`,
  which is the claim that matters.
- `.list-item-touch`'s other consumers (`FileBrowserDialog`, `QueueHistoryPanel`) — the basis for
  scoping Task 2's selector.
- `.phone-messages-feed` (`PhoneMessagesPanel.razor:48`) and `.texts-thread-list`
  (`PhoneTextsPanel.razor:135`) as the two containers.
- `PhoneDevTray.razor` contains no SMS/thread/voicemail simulation — the basis for `C-210` and the
  whole shape of §4.5.
- `.list-item-subtitle` `:658-664` and `.msg-bubble` `:5852-5858` — both exactly as the brief stated.

### 8.2 Not verified, and what it costs

- **The 20px is derived, not re-measured.** It comes from `8px + 12px` in the stylesheet and matches
  the UAT's `251 − 231`. Two independent sources agreeing is good evidence, but no browser was
  opened for this plan. If the harness in §4.5 reports a different number, **the harness wins** and
  the `calc()` needs re-deriving before merge.
- **`:scope` support in this repo's AngleSharp build.** Task 5 carries a fallback instruction rather
  than a verified answer.
- **Nothing was run.** No build, no test, no browser — this is a planning session on a checkout
  other agents are working in.

### 8.3 What would falsify this plan's central decision

Option A rests on the claim that putting every feed row on one left edge is an improvement rather
than a regression. **If the owner says call rows must not move**, Option A is dead and neither B nor
C is good: B leaves a permanent two-edge feed, C contradicts the handoff. In that case `F-7` needs a
design pass and should be split out of this row and handed to `GV-7`, leaving `GV-9` as `F-4` plus
the guard — which is a 15-minute cycle. Builder should raise this **before** starting Task 2, not
after.

### 8.4 Auto-merge recommendation

**Yes — auto-mergeable on green gates, with one artifact required and one flag raised.**

Against the four conditions in the auto-merge policy:

1. **Implementation cycle complete** — six small tasks, all specified with literal code here.
2. **Tests pass** — full suite green (minus the two documented Windows-only known-failures). Four
   new tests, all four mutation-specified in §4.1.
3. **Code review passes** — nothing here is subtle enough to be contentious except §4.6.
4. **Important issues addressed** — none outstanding.

**Why UI does not force a pause here:** two of the three items are unreachable in production
(§0.5), and the third is a layout rule whose value is arithmetically derived from two declarations
in the same stylesheet and verified against the real CSS in the harness. Nothing touches auth,
secrets, migrations or production config.

**Required artifact:** the §4.5 harness before/after screenshots in the PR body. Without them the
`F-7` claim is unverified, and the plan should not be treated as authorising a merge on the strength
of a green suite alone — `C-209` says plainly that the suite cannot see this change.

**Flag, not a blocker:** §4.6 — read call rows move 20px. Put it under its own PR heading.

**Pause and ask if:** the harness disagrees with the 20px, or the owner wants call rows left alone
(§8.3).

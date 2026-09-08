# GV-9 — ASSESSED AGAINST `D31` 2026-09-05 AND UNAFFECTED — all three items survive.

> Queue dossier for row **`GV-9`** of [`BUILDER_QUEUE.md`](../BUILDER_QUEUE.md).
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
| Plan | _plan TBD (small; **no longer CSS-only** — two CSS fixes plus one `.razor` guard and its missing test)_ |
| Spec / handoff | [UAT F-4/F-7](../uat/2026-07-31-gv-live-data/REPORT.md) · [GV-8 UAT (guard provenance)](../uat/2026-07-31-gv8-error-state/REPORT.md) · [handoff](../design-handoffs/HANDOFF-phone-dark-theme-and-scrollbars.md) |
| Depends on | **GV-3** (texts surface). _No external dependency; claimable now. GV-8 is merged, so the guard's canonical form (`PhoneTextsPanel.razor:61`) is on `main` to copy from — read it before writing the thread-list version rather than re-deriving it. Same surface as GV-7: if both are in flight expect `PhoneTextsPanel` and the thread-list row markup to have moved._ |
| Branch | `fix/gv-texts-polish-overflow-unread-align` |

> ✅ **PLANNED 2026-09-08 against `main` `084a6bbd`.** ⚠ **Only three of this row's twelve line
> anchors are still correct — eight moved under `PHN-3` (#598) and `PHN-4` (#578), and a ninth
> points at deleted code** — the plan's §0.3 carries the corrected set; do not work from the
> numbers in the Detail below. Three corrections to the row's own framing: (1) the `PHN-4`
> ordering note is **discharged** — the "New message" button was deleted by #578 and there is no
> remaining constraint; (2) `F-7` is **not** "pure CSS" on the obvious implementation — always
> rendering the dot breaks `VoicemailRowTests.cs:38` and makes two other assertions vacuous, so
> the plan uses a `:has()` gutter plus one markup sync in the dead copy; (3) `F-7`'s fix moves
> **every** feed row 20px right, call rows included — a deliberate consequence the owner should
> see in the PR. `GV-7` shares this surface: **prefer `GV-9` first**, for the same reason this
> row's neighbour already prefers `GV-8` first.
>
> 🚧 **IN FLIGHT 2026-09-08 (Builder).** Branch `fix/gv-texts-polish-overflow-unread-align`; all
> six plan tasks implemented, gates green (47 warnings / 0 errors; suite green but for the six
> documented known-failures). **The PR is deliberately held unmerged at the owner's instruction**
> — not on a failing gate, but on the §4.6 consequence: the `F-7` fix also moves read **call**
> rows 20px right, which is deliberate and well-argued but is a visible, unrequested change to
> the appliance UI that `C-210` means nobody can verify in the real app. Before/after harness
> screenshots are in [`docs/uat/2026-09-08-gv9-texts-polish/`](../uat/2026-09-08-gv9-texts-polish/).

## Detail

✅ **ASSESSED AGAINST `D31` 2026-09-05 AND UNAFFECTED — all three items survive.** CSS overflow on a *displayed* identifier, a 20px *list* alignment jump made visible by `GV-4`'s mark-read, and a `== null` guard on the **thread-list** branch — a different collection from the conversation-mode branch the send path touched. None of it is send.

⚠ **ONE ORDERING NOTE WITH `PHN-4`, not a dependency:** `PhoneTextsPanel.razor:175-177` puts a send-gated `New message` button **inside the "No conversations yet" empty state** — the same block this row hardens — and `PHN-4` deletes it.

**Whichever ships second edits a block the first one moved: claim in either order, never concurrently.**

**Texts-surface polish: overflow hardening + unread-row alignment.** Two LOW findings from the same UAT, both pure CSS on the same surface, grouped into one cycle.

**F-4** — `.texts-conv-number` computes to `white-space: normal; overflow: visible; text-overflow: clip`, while **both** its siblings (`.list-item-title`, `.texts-conv-name`) carry `nowrap` + `overflow: hidden` + `text-overflow: ellipsis`; beyond ~60 characters it clips **mid-character** instead of ellipsizing. _Honest severity: **no live data triggers this**, and a 36-char opaque ID does **not** (measured at 1920×720, UAT G-4). This is consistency hardening, not a fix for an observed break — do not let it be re-filed as a bug._

**F-7** — unread rows sit **20px** out of alignment with read rows, because the `<span class="unread-dot">` is a sibling placed *before* `.list-item-identity` and therefore **displaces** the text rather than occupying a reserved gutter (unread name line x=**251px**, read x=**231px**). Every row jumps horizontally the moment it is marked read — newly visible now that GV-4 wired mark-read. Fix is a reserved-width gutter that holds the text steady in both states.

**Third item, FOLDED IN 2026-07-31 out of the GV-8 cycle — the thread-list branch never got the `== null` guard that GV-8 shipped in conversation mode.** `PhoneTextsPanel.razor:162` is a bare `else if (Error)`; the canonical form on this surface is `Error && <collection> == null` — `PhoneMessagesPanel.razor:110` (threads) and `:78` (voicemail) — and GV-8's M-1 fix shipped exactly that one branch over at `PhoneTextsPanel.razor:61`.

**Consequence if the branch ever goes live:** a stale error flag would outrank thread rows that had actually arrived — the same lie GV-8 was written to remove, just one level up.

**Builder deliberately did not fix this in PR #461, and was right not to:** it was not in Polisher's findings, and the branch is **dead in production** — the panel's only call site (`PhoneMessagesPanel.razor:184`) mounts it solely under `_openThreadId != null`, i.e. conversation mode, which the file's own comment already records. It was flagged forward to this row instead.

**Fix it here, because the file argues for it in its own words:** the comment above the dead skeleton markup says it is *"kept in sync so it isn't the next thing someone copies."* That is the project's stated policy for this exact branch; the guard falls under it.

**Two consequences, stated so they are not a surprise mid-cycle.** (1) **This row is NO LONGER CSS-only** — it becomes CSS plus a one-line `.razor` condition, so budget a `Radio.Web.Tests` run rather than a visual check. (2) **Correction to the deferral note this arrived on:** the behaviour is *not* covered by pre-existing tests — `PhoneTextsPanelTests` sets `Error` only with `OpenThreadId` present (`:124`, `:141`, `:197`), and the three thread-list-mode tests (`:32`, `:41`, `:68`) never set it. It is **unasserted in both directions**, so the fix must **add** an assertion, not preserve one.

**Why folded here rather than a standalone row:** GV-9 is already the row that owns this surface's polish and its dead-copy sync — F-7's reserved gutter has to behave in all three `.unread-dot` sites (`PhoneMessagesPanel.razor:663` live, `PhoneTextsPanel.razor:191` dead, `VoicemailRow.razor:10`), and the dead one sits **29 lines below** the bare `else if`. A separate row would be a third queue entry against the same file for a one-line change in unreachable code.

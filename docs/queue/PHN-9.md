# `PHN-9` — the unread badge requires you to have already visited the page it points at

[← Builder Queue index](../BUILDER_QUEUE.md)

🟠 **P1.** Filed 2026-09-09. **Found on the live appliance**, not by reading code: the topbar PHONE
pill showed a badge of **14** before a redeploy and **no badge at all** after it, with nobody touching
the phone in between. Mechanism traced and confirmed by a read-only investigation.

## What actually happens

1. `PhoneUnreadState` is a **process-wide singleton** holding `private int _count;`
   (`PhoneUnreadState.cs:12`), registered `AddSingleton` at `Program.cs:431`. A fresh process starts
   at **0**.
2. **Its only writer is `PhonePage`, and only while that page is mounted.** All 17 `Set` call sites
   are in `PhonePage.razor` (`:282, 367, 416, 457, 509, 686, 700, 727, 746, 764, 777, 812, 857, 871,
   912, 936, 997`). A repo-wide grep for `PhoneUnread` across `src/` returns only `PhonePage.razor`,
   `MainLayout.razor`, and `Program.cs:431`.
3. `MainLayout` seeds the badge with `_phoneUnread = PhoneUnread.Count;` (`:402`) — **a read of an
   in-memory singleton, not a fetch** — and subscribes to `Changed`.
4. The badge is gated `@if (_phoneUnread > 0)` (`:224-227`), so **zero renders no element**, not a
   `0`.
5. `radio-kiosk-launch:11` reopens the kiosk at **root**, not `/phone`. `PhonePage` never mounts.

**Result: after every `radio-web` restart the badge is absent for the life of the process, until
somebody taps PHONE.** Deploys restart `radio-web`, so this is the normal state, not an edge case.

## ⛔ A message arriving does NOT fix it

The obvious mitigation — "it will repopulate when the next voicemail lands" — is **false**. The hub
handlers that recompute the count (`OnGvVoicemailReceived`, `OnGvSmsReceived`, `OnReadStateChanged`,
`OnCallHistoryUpdated`) are subscribed at `PhonePage.razor:269-276` and **unsubscribed in `Dispose` at
`:1247-1255`**. They do not exist when the page is not mounted.

⚠ **`MainLayout` cannot hear a phone hub event under any circumstance** — it does not inject
`PhoneHubService`, `GvTrunkHubService` or `GvBridgeStatusService` (full `@inject` list at `:5-28`).
**Opening the Phone page is the only recovery path.**

⚠ **And the latch goes stale in the other direction too.** `PhonePage.Dispose` does not reset the
count, so the last published value persists for the life of the process — messages read on the
handset afterwards never decrement it. **The badge can be wrong high as easily as wrong low.**

## ⭐ The repo already names this defect, about a different badge

`MainLayout.razor:406-408`:

> *"Unlike PhoneUnread (which PhonePage publishes), BellHealthService polls on its own, so the badge
> does not require the user to have already visited the page it points at."*

And `:420-423`, on the encoder badge, describes this exact failure mode as something already fixed
there: *"a deploy restarts radio-api and radio-web together… the cache stays null, and the badge never
appears for the life of the process."*

**Three sibling badges in the same topbar hydrate authoritatively and this one does not:**

| Badge | How it hydrates |
|---|---|
| Queue | `await RefreshQueueCountAsync()` → `QueueApi.GetQueueAsync()` (`:475`, `:1244`) |
| Mute | `await LoadMuteStateAsync()` (`:392`) — comment at `:389-391` explains change-only events leave it blank |
| Encoder fault | `await SeedEncoderStateAsync()` (`:435`) |
| Bell fault | `BellHealthService` background poller (`Program.cs:447-453`) |
| **PHONE unread** | **reads a singleton that only `/phone` writes** |

`PhoneUnreadState.cs:7` concedes it in its own XML doc — *"v1 counts are UI-local only — a hard reload
re-derives from isRead/hasUnread"* — **which is true only if the reload lands on `/phone`.**

## ⚠ This is NOT a small fix, and the plan must say so

**There is no server-side unread total to hydrate from.** `GvBridgeStatusService` exposes only
`Current` / `IsAvailable` / `IsHealthy` (`Services/GvBridgeStatusService.cs:34-54`); a grep for
`unread|unheard` across `src/Radio.Web/**/*.cs` finds no count-bearing DTO or API method — only
per-item `HasUnread` / `IsRead` flags (`Models/ApiModels.cs:1209,1233`). The count is computed
client-side as `MissedCallCount + UnheardVoicemailCount + UnreadThreadCount` (`PhonePage.razor:227`)
from three page-instance fields.

So a fix needs **either** a new server-side total **or** a hosted poller in `Radio.Web` that derives
one — the `BellHealthService` shape at `Program.cs:443-453` is the in-tree precedent. ⚠ **Deriving it
client-side means three REST calls on every app start**, which is a real cost on this box; price it
before choosing.

⚠ **Cross-repo:** if the answer is a server-side total, it is **RotaryPhone's** endpoint, not ours.
Follow the boundary protocol — do not assume they will build it, and do not build a Radio-side poller
that silently duplicates a total they already intend to serve. Ask first.

## Verification

**Live, no code change required — run this first to confirm the diagnosis on the deployed build:**
tap PHONE once, then navigate Home. The badge should appear and persist; it should survive a kiosk
reload but **not** a `radio-web` restart. ⭐ **If it does not behave that way, this row is wrong** and
the mechanism above needs re-deriving.

Unit-testable after that: assert the topbar renders a badge on a fresh circuit that has never mounted
`PhonePage`. ⚠ **Must fail first** — today it cannot pass. Note that no existing test exercises the
badge seed at all: every `tests/` reference to `PhoneUnreadState` is a DI registration in bUnit setup
(`PhonePageTests.cs:89`, `ConsolePlaybackChipTests.cs:73`, `PhonePageRecoveryRefetchTests.cs:82`,
`PhonePageThreadLoadErrorTests.cs:92`).

## Provenance

⭐ **Found because a badge disappeared across a deploy and the disappearance was investigated rather
than explained away.** The first two guesses were both wrong and both discarded: it was not a
regression from the four PRs deployed that morning, and it was not the `30 s` hub `TimeoutException`
in the log — **no hub carries an unread count at all** (`PhoneHubService.cs:18-36` delivers individual
items, never a total), and `MainLayout` could not hear one if it did.

## Depends on

None. ⚠ **Related but distinct from `PHN-7`** — that row is about `BellHealthService` polling a
transport that lies; this one is about a badge that polls nothing whatsoever.

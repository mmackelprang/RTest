# PLAN — `PHN-2` · ADR-029 PR 6: retire the `<audio>` element (Feature A)

> **Row:** `PHN-2` — the last open **P0** on the GA punch list, and PR 6 of the eight-PR ADR-029 arc.
> **Branch:** `feat/phn-2-retire-audio-element`
> **ADR:** [`design/decisions/2026-08-03-gv-audio-through-engine.md`](../decisions/2026-08-03-gv-audio-through-engine.md) — Feature A, D1–D8, Amendment 2 (§16).
> **Design handoff (authoritative on presentation):** [`docs/design-handoffs/HANDOFF-phone-console-audio-and-canned-replies.md`](../../docs/design-handoffs/HANDOFF-phone-console-audio-and-canned-replies.md) — §Cross-1…Cross-5, §A1–§A6, §Gaps G-1/G-2/G-3.
> **Sequencing:** [`design/plans/PHN-arc-pr-breakdown.md`](PHN-arc-pr-breakdown.md) row 6 and its § *Verification shape*.
> **Planned against:** `main` at **`ba1ae4a6`** (`PHN-1f` merged, [#564](https://github.com/mmackelprang/RTest/pull/564)).
> **follows / extends / deviates:** **follows** the handoff for every visual and every string it
> specifies; **extends** it with one new failure row (`MediaUnauthorized`, §0.4 C-71) and with the
> `Waiting` chip state `PHN-1f` §0.6 defined after the handoff was written; **deviates** nowhere.

---

## 0. Read this before Task 1

### 0.1 What this row is, in one paragraph

`VoicemailPlayer.razor` is an HTML5 `<audio>` element pointed straight at the Google Voice bridge, so
the **browser** fetches and decodes voicemail audio. Mute is a gain applied inside Radio.API's
SoundFlow playback device; there is no node shared between the two paths. The consequence is not a
latent risk — owner decision **`D17`** confirms it is what the cabinet does today: press play on a
voicemail while the radio is on and **two sounds run in the room at full level each**, with mute,
master volume, balance, ducking and Cast routing all bypassed. PRs 1 through 5b built the seam that
fixes it and shipped **no user-visible surface at all**. This row spends that seam: the `<audio>`
element is deleted, the transport becomes a remote control for `POST /api/audio/events`, a global
stop chip appears in `.topbar-primary`, and voicemail becomes ordinary console audio. **This is
Feature A**, and per the arc breakdown *"nothing short of that settles it."*

### 0.2 The shape, stated first — three moving parts and one deletion

Everything in §1 falls out of this picture, so it is worth thirty seconds:

```
                        ┌─────────────────── Radio.API ────────────────────┐
  ┌ Radio.Web ────────┐ │ POST   /api/audio/events        → 202 snapshot   │
  │ VoicemailPlayer   │─┼─▶ DELETE /api/audio/events/{id}                  │
  │  (transport)      │ │ POST   /api/audio/events/{id}/{seek,pause,resume}│
  │ MainLayout chip   │ │ GET    /api/audio/events/current  (re-attach)    │
  └───────┬───────────┘ └──────────────────┬───────────────────────────────┘
          │ reads                          │ "EventPlaybackChanged" on /hubs/audio
          ▼                                ▼
  ConsolePlaybackState  ◀─── ONE subscription ─── AudioStateStore.EventPlaybackChanged
       (new singleton)                              (already built by PHN-1e)
```

**One deletion, and it is the point of the row:** `<audio>`, `wwwroot/js/voicemail-player.js` and
`GvBridgeApiService.GetVoicemailAudioUrl` go **together**. Removing the URL builder first breaks
voicemail playback for the length of the diff; removing the element first leaves an unreferenced
builder that the next reader will assume is load-bearing.

**Three things this shape buys for free**, and they are the four checks the arc exists to satisfy:
once voicemail is a source on the master mixer, `mute`, `master volume`, `balance`, `ducking` and
`output routing (including Cast)` apply to it the way they apply to everything else. There is no code
in this row that implements ducking or mute — that is exactly why the row settles Feature A.

### 0.3 ⚠ Re-check list — what was verified against `ba1ae4a6`, and where a Builder must re-grep

Everything in §1 was read against the merge commit named above. Three things a Builder must re-check
before writing code, because they are the ones most likely to have moved:

1. **`AudioStateStore.EventPlaybackChanged` still has zero production subscribers.** Verified at
   `ba1ae4a6`: the event is declared at `AudioStateStore.cs:65` and raised at `:295` and `:369`, and
   `grep -rn "EventPlaybackChanged" src/Radio.Web --include=*.razor` returns **nothing**. §0.6's whole
   argument rests on this. If a subscriber has appeared, §0.6 must be re-derived, not assumed.
2. **`EventPlaybackApiService` still has exactly two methods** (`GetCurrentAsync`, `StopAsync`) and its
   own remark says why: *"Start, seek, pause and resume belong to PR 6, which is the first row with a
   transport to drive them from."* Task 3 adds the other four. If any already exists, do not add a
   second.
3. **`FailureReason` for a RemoteMedia failure is `"Media" + GvMediaFailure`**
   (`EventPlaybackService.cs:665`), *not* the coarse `FailureReasonFor(kind)` at `:792` — that one is
   the fallback at `:669` for a non-`GvMediaUnavailableException`. Task 6's copy table depends on the
   distinction and it is easy to read the file and conclude the opposite. ⚠ **This plan's first draft
   did conclude the opposite**, from `:792` alone, and was corrected by reading `:665`. Re-grep
   `FailAsync(` before trusting the table.

### 0.4 ⚠ Fourteen constraints found while planning — C-68 continues `PHN-1f`'s numbering

**C-68, C-69 and C-71 change the work.** C-70 is the subscriber count the brief asked to be confirmed
and which does **not** hold as stated. C-73 through C-76 are obligations earlier rows handed here, two
of which turn out to be already discharged. C-79 is the comment corrections this row owes. C-80 and
C-81 are two defects it found and **must not fix** — one in a merged ADR, one in the deploy script.

---

**C-68 — ⚠ CHANGES THE WORK. `GvMedia:Enabled` must flip in `src/Radio.API/appsettings.json`, not in
`appsettings.Production.json`, and the reasoning is the opposite of what ADR §10.2's table suggests.**

ADR §10.2 lists `GvMedia:Enabled` as `false` in `appsettings.json` with *"flip `true` when the arc
ships"* in the **per-machine** column. Taken literally that means a hand edit to
`/opt/radio-console/api/appsettings.Production.json`. **Do not do that.** Three reasons, in order of
weight:

1. **There is no `src/Radio.API/appsettings.Production.json` in this repository at all** — verified,
   the file does not exist; the only per-machine seeds are `deploy/debian-x64/` and
   `deploy/raspberry-pi/`, and **neither carries a `GvMedia` block**. Meanwhile
   `Deploy-ToLinux.ps1:217` rsyncs `appsettings.json` **without** excluding it, and excludes
   `appsettings.Production.json` explicitly. So a `true` in `src/Radio.API/appsettings.json` reaches
   the box on the next deploy, and a `true` written anywhere else does not exist in the repository at
   all. `design/INTEGRATIONS.md:984-985` states the same rule from the other side: *"`src/Radio.API/appsettings.json`
   **is** overwritten on every deploy, which is why the non-secret `GvMedia` defaults live there and
   the secret does not."*
2. **`Enabled` is not a secret.** ADR §10.2's per-machine column exists for `AuthKey` and `BaseUrl` —
   values that differ per box. Whether the console plays voicemail through its own speakers does not.
3. **A dark P0 is worse than a loud one.** `GvMedia:Enabled=false` makes `StartAsync` throw
   `GvMediaFailure.Disabled` synchronously and the controller answer **409** (`:96-104`). The user
   taps play and gets an error. That is punch-list tier (b) — *"press play, get an error, nothing
   happens"* — which is the exact shape `D28` was decided to avoid.

**The consequence to state to the owner rather than bury:** after this row merges and deploys,
`GvMedia:Enabled` is `true` on every box, and a box whose `GvMedia:AuthKey` does not match
RotaryPhone's `InterServiceAuthKey` will fail **every** voicemail fetch with `MediaUnauthorized`.
That is C-69, and it is why the pre-flight is a numbered step and not a footnote.

---

**C-69 — ⚠ CHANGES THE WORK, AND IT IS A PRE-FLIGHT, NOT A CODE CHANGE. The auth-key check must happen
BEFORE `GvMedia:Enabled` flips on the appliance, and there are two files the deploy will not touch.**

`GvMediaAuthHandler.cs:26,39-43` attaches `X-RotaryPhone-Auth` from `GvMedia:AuthKey`, and skips the
header entirely when the key is empty. RotaryPhone's inbound gate ships **default-off** — a two-way
short-circuit, `InterServiceAuthValidator.cs:19` (`IsEnabled = !string.IsNullOrEmpty(configuredKey)`)
and `GvBridgeAuthMiddleware.cs:27-31` — so with an empty expected key **every** request is allowed and
today's empty `GvMedia:AuthKey` works. **If that key has ever been set on the appliance, every fetch
returns 401** (`GvBridgeAuthMiddleware.cs:47-50`), which reaches the panel as `MediaUnauthorized` and
reaches the operator as *nothing at all*: `GvMediaStartupCheck` (`GvMediaServiceExtensions.cs:92-119`)
fires only when **this** key is empty — two differing non-empty keys pass it in silence, because
Radio.API cannot read Radio.Web's overlay and cannot read RotaryPhone's configuration at all. The
option's own XML doc says so (`GvMediaOptions.cs:42-46`), and the exception's says
*"This exception is the whole diagnosis"* (`GvMediaUnavailableException.cs:36-41`).

⚠ **The check cannot be done from the repository.** RotaryPhone's `InterServiceAuthKey` is `""` in the
only file that defines it (`RotaryPhoneController.Server/appsettings.json:94`), but
`GVBridgeConfig.cs:79-82` explicitly instructs storing the real value *outside source* — an env var
(`GVBridge__InterServiceAuthKey`), user-secrets, or a hand-edited Production file. **A green repo says
nothing. Verify on the live box.**

Two files hold the two halves of our side of the one secret, and **neither is re-seeded by
`Deploy-ToLinux.ps1`** (`:217` excludes both from rsync; `:222` re-seeds only when absent):

```
/opt/radio-console/api/appsettings.Production.json   → GvMedia:AuthKey          (Radio.API)
/opt/radio-console/web/appsettings.Production.json   → RotaryPhone:Gv:AuthKey   (Radio.Web)
```

⚠ **And a `grep` of those two files is not conclusive either.** `Program.cs:31` layers a **SQLite**
configuration store *over* appsettings, so `GvMedia:AuthKey` can also arrive from
`/opt/radio-console/data/config/configuration.db` and win over both JSON layers. The authoritative
observation is the behaviour — a `MediaUnauthorized` — not the file.

**This is §3 pre-flight step P3, it has a runbook already written at
`design/INTEGRATIONS.md:957-973`, and it is the single most likely way this row looks broken on the
box while being correct in the repo.**

---

**C-70 — ⚠ THE BRIEF'S SUBSCRIBER COUNT DOES NOT HOLD FOR THE OBVIOUS DESIGN, AND THAT IS WHY §0.6
EXISTS. Stated here because it is a premise, not a detail.**

The punch list tiers `UI-6` at P2 partly on the observation that *"`EventPlaybackChanged` has zero
production subscribers today and PR 6 takes it to one, below the N ≥ 2 a multicast defect needs."*
**The obvious implementation of this row takes it to two per circuit** — the chip in `MainLayout` and
the open `VoicemailPlayer` both need the snapshot — and with two browsers open, to four.
§0.6 chooses a shape that keeps the store's event at **exactly one subscriber for the life of the
process**, so the punch list's premise becomes literally true rather than approximately true. Read
§0.6 before Task 4; it is the one structural decision in this plan.

---

**C-71 — ⚠ CHANGES THE WORK. The handoff's §Cross-5 table has four rows and the wire carries at least
seven reasons. A fifth row is required, and without it the owner's UAT cannot distinguish a
misconfiguration from a blackout.**

`FailureReason` on a `Failed` snapshot is `"Media" + ex.Reason` for anything the media client throws
(`EventPlaybackService.cs:665`), so the panel can see `MediaNotFound`, `MediaUnauthorized`,
`MediaUpstream`, `MediaTimeout`, `MediaTransport`, `MediaTooLarge` and `MediaUnknown`, plus
`MediaAcquisitionFailed` (`:669`, the non-media fallback) and `WaitExpired` (`:521`).

§Cross-5 specifies copy for four conditions and says, correctly, *"Four distinct failures, four
distinct sentences. **Do not collapse them.**"* Applying that rule to the reasons that actually reach
the wire produces **five** rows, not four — `MediaUnauthorized` is a distinct failure with a distinct
fix and it must not wear the blackout's *"This usually clears up in a minute"* sub-line, because it
never clears up. Task 6's table is therefore an **extension of the handoff under the handoff's own
rule**, not a deviation from it. It is flagged in §6.2 for the Designer to bless the string.

---

**C-72 — The `Waiting` state is already reachable on the appliance today, and PR 6 is where a user
first sees it. This is not a forward-looking nicety.**

`GvMedia:Enabled=false` gates only the **RemoteMedia** arm — the guard at
`EventPlaybackService.cs:222` is `request.Kind == EventPlaybackKind.RemoteMedia && !gv.Enabled`. The
**Speech** arm is open, and `/api/notifications/announce` defaults to priority 8
(`NotificationsController.cs:46`), which is `GvMedia:PreemptAtPriority`. So `D28`'s wait-then-play
queue is **live on the box right now** and has been since `PHN-1f` deployed; what does not exist is
any surface that renders it. Per `PHN-1f` §0.6 the chip must: treat `Waiting` as **live and
stoppable**, **not** run the progress bar, and **say why** rather than showing a bare spinner.

---

**C-73 — The `GvBridgeApiService.GetVoicemailAudioUrl` deletion has a second caller-shaped
obligation that is ALREADY DISCHARGED. Do not redo it.**

`PHN-1b` §5 and `PHN-1c` §5 both instruct PR 6 to *"withdraw the cross-repo ask in
[`CROSS-REPO-HANDOFFS.md`](../../docs/queue/CROSS-REPO-HANDOFFS.md) § Cross-repo handoffs #3 in the same PR."* **Read that item before acting:
its audio-endpoint clause is already struck through and marked** *"✅ **DISSOLVED, not resolved, by
`PHN-1b` ([#534](https://github.com/mmackelprang/RTest/pull/534))**"*, with the mechanism recorded
(`GvMediaAuthHandler.cs:26` attaches the header server-side). **Nothing is owed.** Task 12 verifies
the wording still says so and changes nothing if it does. Recorded rather than silently dropped,
because a carried obligation that evaporates without a note is how the arc's four device-only checks
nearly vanished.

---

**C-74 — `TTSEventSource.Position` is this row's by explicit handoff, and it is NOT visible in this
row. Do it anyway, and say which of those two facts is the reason.**

`PHN-1e` §5.2 assigns PR 6 a three-line `Position` override on `TTSEventSource`, on the grounds that
*"PR 6 is the first row with a scrubber that is visibly wrong."* **That justification does not
survive contact with this row's scope:** PR 6 builds Feature A (voicemail, `AudioFileEventSource`,
which already reports a real position at `AudioFileEventSource.cs:93-95`) and the chip carries **no
progress bar at all**, so a speech playback's frozen position is invisible here. It becomes visible in
`PHN-3` (Feature B).

**Do it in this row regardless**, for the opposite reason: it is three lines against a shipped test
that was written to be updated (`ASpeechSnapshotReportsPositionZeroForItsWholeLife` in
`tests/Radio.Infrastructure.Tests/Audio/Events/EventPlaybackServiceTests.cs`), the handoff named this
row, and an obligation deferred a second time with a weaker reason than the first is an obligation
about to be lost. Task 10. **It is scope this plan is choosing to keep, and the reason is recorded so
a reviewer can disagree with it.**

---

**C-75 — ⚠ A green "it completed" is the LEAST trustworthy evidence available on this row, and every
verification claim must say which side of that line it is on.**

`PHN-1c` §2.2 item 3 named it and this pass verified the mechanism, so it can be cited precisely
rather than repeated on trust. `AudioFileEventSource.PlayCoreAsync:175-184` branches:

```csharp
      if (_playbackService != null && _playbackId != null)
      {
        _playbackTask = PlayWithSoundFlowAsync(_playbackCts.Token);
      }
      else
      {
        // Fallback: simulate playback by waiting for the duration
        Logger.LogDebug("SoundFlow playback service not available, using simulation");
        _playbackTask = PlaybackLoopAsync(_playbackCts.Token);
      }
```

and `PlaybackLoopAsync` (`:261-271`) is `await Task.Delay(_duration, ct)` followed by
`State = Stopped; OnPlaybackCompleted(PlaybackCompletionReason.EndOfContent);`. **It produces no audio
and reports a clean end**, and it announces itself at **Debug** (`:181`) — which since `LOG-11` does not
reach the journal at all. `AudioFileEventSourceTests` treats that null configuration as an expected
state (`Position_IsZero_WhenThereIsNoPlaybackService`) and asserts nothing about sound. Nothing in a
test host produces sound. Therefore:

> **Every check in §2 and §3 is labelled either `SOUND` — a human confirmed the room changed — or
> `PATH` — the code path ran. A `PATH` check may never be reported as evidence for a `SOUND` claim.**

This is the discipline that keeps this row's UAT meaningful, and it is why §3 is written for a person
standing in front of a radio rather than for a reviewer reading a log.

---

**C-76 — The `./data/gvmedia` writability check has been carried twice and this row is its last
chance. It is not a `PATH` check.**

`PHN-1b` §2.2 item 4 carried it to PR 3; `PHN-1c` §2.2 item 4 re-carried it to PR 6 because
`GvMedia:Enabled` shipped `false` and PR 3 performs no first fetch. **This row performs the first
fetch on the appliance, ever.** The check is not "the directory exists" — it is *a file with non-zero
length appeared in it after a successful play*, which is the only form that distinguishes a working
cache from a swallowed write. §3 step P6.

**The exact path, so nobody looks in the wrong place:** `GvMedia:CacheDirectory` is the relative
`"./data/gvmedia"` (`appsettings.json:272`), relative paths resolve against the **process working
directory**, and `radio-api.service:37` sets `WorkingDirectory=/opt/radio-console` — **not**
`ASPNETCORE_CONTENTROOT`, which is `/opt/radio-console/api`. So it is
**`/opt/radio-console/data/gvmedia`**, owned by `mmack`, and the deploy does not touch `data/`.

⚠ **And know what a failure here looks like, because it is not media-shaped.**
`GvMediaCache.WriteAsync` calls `Directory.CreateDirectory` **outside** its inner `try` (`:172`), so a
permission or read-only failure propagates raw — an `IOException` or `UnauthorizedAccessException`,
never a `GvMediaUnavailableException`. It is therefore caught by the **generic** handler
(`EventPlaybackService.cs:667-670`) and surfaces as `FailureReason = "MediaAcquisitionFailed"`, not as
any `Media<GvMediaFailure>` value. Task 6's copy table routes that reason to *"The console can't play
this right now."* — which is correct copy and a **misleading diagnosis**, so §3's fail path sends the
owner to the log line rather than to the panel.

---

**C-77 — `VoicemailPlayer.DisposeAsync` MUST NOT stop playback, and the current file does the
opposite. This is a deletion, and it is the single most likely thing to be "helpfully" preserved.**

ADR §7.2 (as amended): *"`VoicemailPlayer.DisposeAsync` must not stop playback; component disposal is
a rendering event, not a user intent."* The handoff § Component/file impact repeats it. The chip is
what earns it. Task 13's grep tripwire fails the build if a stop call survives in `DisposeAsync`.

⚠ **This does not mean nothing stops the audio when the user leaves.** Three shipped mechanisms do,
and they are not this component's business: the last-circuit backstop (`D30` — a reload **does** stop
it, deliberately), the `/sleep` edges (ADR §16.5), and the 300 s cap.

---

**C-78 — The muted-state pill must read the store SYNCHRONOUSLY and must not subscribe to
`VolumeChanged`. Adding a subscriber there would cross the `UI-6` threshold on an event that already
has live subscribers.**

The handoff's §Cross-5 fourth row (*"Console muted / volume 0 **at play time**"*) is a snapshot
condition by its own wording, so a one-shot read at the moment of the tap is what it asks for.
`AudioStateStore.VolumeChanged` already has consumers; adding `VoicemailPlayer` to it would take a
store event that PR 6 has no reason to touch from N to N+1. Task 6 reads the property; it does not
subscribe.

---

**C-79 — Two shipped remarks become false in this row and must be corrected in it.**

1. `EventPlaybackApiService`'s class remark (`:11-16`) says *"Two methods, deliberately… Start, seek,
   pause and resume belong to PR 6."* Task 3 adds them; the remark must stop describing a file that
   no longer looks like that.
2. `AudioStateStore.EventPlaybackChanged`'s neighbourhood and `EnsureEventPlaybackSeededAsync`'s
   remark describe a store with no consumer. Task 4 gives it one. Any sentence asserting *"nothing
   subscribes"* is falsified by this row and is corrected by it.

**This is the thirteenth and fourteenth instance of the `CLAUDE.md` § Pre-Merge Review class in this
arc.** The discipline is making the correction in the PR that falsifies the comment.

---

**C-80 — ⛔ ADR §16.5's table overstates its case and this row does NOT amend it.**

`PHN-1e`'s Builder and `PHN-1f`'s planner both flagged that ADR-029 `:1281-1284`'s claim that the
`SetSleepScreenVisible(true)` edge *"covers rows 1, 2, 3, 5"* is **true of producing the fact and
false of stopping the playback** — the report lands seconds late on a brand-new circuit, the setter is
`void` on a synchronous action while the stop is `async`, the flag is a global last-writer-wins bool,
and edge semantics make a second client arriving at `/sleep` a no-op. **Two prior cycles have now
flagged it for an Architect pass and this is the third.** It is recorded in §6.1 and **not amended
here** — this row does not edit a merged ADR, and nothing in this row's scope depends on the
resolution.

---

**C-81 — ⛔ A DEPLOY DEFECT THIS ROW FOUND AND MUST NOT FIX: `Deploy-ToLinux.ps1` can destroy
`RotaryPhone:Gv:AuthKey`, and this row is the one that makes that key matter.**

`Deploy-ToLinux.ps1:222` guards the Production-config seed on **`api/`'s** file only:

```powershell
222:    ssh $SshTarget "test -f $TargetPath/api/appsettings.Production.json" 2>$null
223:    if ($LASTEXITCODE -ne 0) {
...
226:      ssh $SshTarget "... sudo cp /tmp/appsettings.Production.json $TargetPath/api/ && sudo cp /tmp/appsettings.Production.json $TargetPath/web/ ..."
```

Line 226 writes **both** directories. So a box whose `api/appsettings.Production.json` is absent while
`web/`'s is present gets its **web overlay silently overwritten** by the api seed — which is exactly
where `RotaryPhone:Gv:AuthKey` lives. On `radio` today both files exist, so it is dormant; it becomes
live the first time anyone rebuilds the api overlay or provisions a second box.

**Not fixed here**, on the same test this queue applies to `GV-6`/`GV-8`: different mechanism
(PowerShell deploy sequencing, not a Blazor surface), different verification (a second box or a
deliberately-removed file, not a listening test), different blast radius. Folding a deploy-script
change into the last open P0 would put a change that can silently destroy a secret behind a UAT that
is about whether a voicemail ducks the radio. **Filed as a proposed row in §6.3.**

---

### 0.5 What this row is NOT

1. **Not Feature B.** No speak button, no `MessageBubble` change, no `GvSpeechText.ForMessage`. That is
   `PHN-3`, a separate punch-list row, and the handoff's §B is not this row's to build.
2. **Not Feature C.** No canned replies, no `PhoneTextsPanel` change, no compose removal. That is PR 7.
3. **Not a `UI-6` fix.** §0.6 avoids adding to the defect; it does not repair `NotifyAsync`,
   `OnHubRadioStateChanged` or `OnHubSleepStateChanged`. `UI-6` is a queued P2 row with its own branch.
4. **Not drag-scrub.** Handoff §A3 defers it for design reasons and records the only acceptable
   implementation if it is ever added. Tap-to-seek only, which is what the current player already does.
5. **Not a new stop condition.** The cap, the circuit backstop and the two sleep edges all shipped in
   `PHN-1e`/`PHN-1f`. This row adds no fourth.
6. **Not an `EndReason` field.** `FUTURE-WORK.md` §20 records the request and its condition —
   *"only if a renderer asks."* This row's chip returns to an idle, replayable state for all four
   end causes (ADR §12 item 4), so it does not ask. §6.2 records that it was considered.
7. **Not a position tick.** ADR §8.2 forbids it; the bar interpolates locally from the anchor.
8. **Not a `:root` change.** Handoff § Component/file impact: *"All new classes above + the four
   §Gaps fixes. **No `:root` changes.**"* G-4's two undefined tokens stay undefined and gain no
   consumers.
9. **Not a `NowPlayingDock` change.** Handoff §Cross-2 is explicit that this is *"an instruction, not
   an omission."*

### 0.6 ⭐ The subscriber question, answered — one store subscription, for the life of the process

**The problem.** Two surfaces need the snapshot: the chip in `MainLayout` (every route) and the
transport in `VoicemailPlayer` (one row at a time on `/phone`). Subscribing both directly to
`AudioStateStore.EventPlaybackChanged` gives that event **two subscribers per circuit**, and
`AudioStateStore` is a **singleton** (`Program.cs:432`), so two open browsers give four. That is
above the `N ≥ 2` at which `UI-6`'s multicast defect becomes reachable, and it falsifies the premise
the punch list used to tier `UI-6` at P2.

**The decision: a new singleton `ConsolePlaybackState`, and it caches nothing.**

```
AudioStateStore.EventPlaybackChanged   ── exactly 1 subscriber, ever ──▶  ConsolePlaybackState
                                                                              │  Changed
                                          ┌───────────────────────────────────┴──────────┐
                                          ▼                                              ▼
                              MainLayout (the chip)                         VoicemailPlayer (transport)
```

Four properties, each of which is the reason for a design choice:

1. **It subscribes once, in its constructor, and never unsubscribes.** Both objects are singletons
   and live for the process, so `AudioStateStore.EventPlaybackChanged` has one handler from first
   resolution to shutdown regardless of how many browsers are open. The punch list's premise —
   *"PR 6 takes it to one"* — becomes exactly true.
2. **It holds no snapshot of its own.** `Snapshot => _store.EventPlayback`, read through. A second
   cache would be a second thing to keep in step with the seed, the broadcast and the
   broadcast-wins ordering guard — three mechanisms `PHN-1e` built and this row must not fork.
3. **Its own fan-out is correct by construction**, because this row writes it: iterate
   `GetInvocationList()`, invoke each handler inside its own `try`/`catch`. The handoff says to build
   this *"exactly like `PhoneUnreadState`"*, and `PhoneUnreadState.Set` is
   `Changed?.Invoke(_count)` — **a plain multicast invoke, which is the synchronous half of `UI-6`.**
   Copy its *shape*, not its notifier. This is not a `UI-6` fix — the store's own three sites are
   untouched and still queued — it is a refusal to add a fourth.
4. **It is constructed by `MainLayout`**, which renders on every route under it, so the subscription
   exists from the first circuit. DI singletons are lazy, and `AudioStateStore` itself spent its
   entire life unconstructed for exactly this reason (`PHN-1c` §5, on `ENC-12`).

**The residual, stated rather than implied.** `ConsolePlaybackState.Changed` will carry two handlers
per circuit, so the *fan-out* is genuinely multicast — it is simply a fan-out that does not lose
exceptions, because Task 4 writes it not to. And the store's own two hand-rolled sites
(`OnHubRadioStateChanged`, `OnHubSleepStateChanged`) are untouched and still defective; that is
`UI-6`, still P2, still queued.

**If the owner would rather order `UI-6` first**, it is 0.5 d and it makes this decision moot rather
than wrong — `ConsolePlaybackState` would still be worth building for property 2. §6.3 records the
option; this plan does not take it, because taking it would delay the last open P0 for a defect whose
worst observed consequence is a lost log line.

---

## 1. Tasks

**Thirteen tasks, eighteen files.** Task 13's greps fail the PR on eight specific ways of exceeding
the scope in §0.5. Order matters in three places only: Task 2 before Tasks 4 and 6 (they consume the
singleton), Task 7 after Task 6 (deleting the URL builder before its caller is gone breaks the build),
and Task 5 before any UAT of Task 6 (without G-1 the `Preparing` spinner renders **nothing**, so the
state cannot be observed at all).

---

### Task 1 — `GvMedia:Enabled` becomes `true`, in the one file that reaches the box

**File:** `src/Radio.API/appsettings.json:269`

```diff
   "GvMedia": {
-    "Enabled": false,
+    "Enabled": true,
     "BaseUrl": "http://radio:5004",
     "AuthKey": "",
```

**One line, and it is the line that turns the feature on.** §0.4 C-68 carries the argument for putting
it here rather than in a Production overlay; the short version is that `Deploy-ToLinux.ps1:217` rsyncs
this file and explicitly excludes `appsettings.Production.json`, and there is **no**
`src/Radio.API/appsettings.Production.json` in this repository to put it in.

⚠ **Do not touch `AuthKey`.** It stays `""` in the tracked file, permanently. Setting it is two hand
edits on the appliance (§0.4 C-69; §3 step P3; runbook at `design/INTEGRATIONS.md:961-973`).

⚠ **Nothing else in the block changes.** `MaxPlaybackSeconds` stays 300 and `PreemptAtPriority` stays
8 — §0.5 item 5 forbids a fourth stop condition, and `GvMediaOptions.cs:112-117` records why raising
the threshold quietly disables preemption rather than tuning it.

---

### Task 2 — `ConsolePlaybackState`, the one subscriber

**New file:** `src/Radio.Web/Services/ConsolePlaybackState.cs`

This is §0.6's decision made concrete. Read §0.6 first; every line below is a consequence of it.

```csharp
using Microsoft.Extensions.Logging;
using Radio.Web.Models;

namespace Radio.Web.Services;

/// <summary>
/// The single point at which Radio.Web observes attended event playback (ADR-029 D6), and the home of
/// the two derivations the topbar chip and the voicemail transport would otherwise each invent.
/// </summary>
/// <remarks>
/// ⚠ IT CACHES NOTHING. <see cref="Snapshot"/> reads through to AudioStateStore.EventPlayback, which
/// PHN-1e already keeps correct through three mechanisms this class must not fork: the hub broadcast,
/// the one-shot seed from GET /api/audio/events/current, and the broadcast-wins ordering guard between
/// them. A second cache would be a fourth thing to keep in step, and the first to drift.
///
/// ⚠ WHY IT EXISTS AT ALL, since the store already has the data. AudioStateStore is a SINGLETON, so a
/// component subscribing to it subscribes once PER CIRCUIT. The chip lives in MainLayout (every route)
/// and the transport in VoicemailPlayer, so subscribing both directly would put two handlers per
/// circuit — four with two browsers open — on AudioStateStore.EventPlaybackChanged, whose NotifyAsync
/// awaits only the LAST of them (queue row UI-6). This class subscribes ONCE, in its constructor, for
/// the life of the process, and fans out itself.
///
/// ⚠ AND ITS OWN FAN-OUT IS NOT A COPY OF THE DEFECT. The design handoff says to build this "exactly
/// like PhoneUnreadState"; PhoneUnreadState.Set is Changed?.Invoke(_count) — a plain multicast invoke
/// in which one subscriber throwing SYNCHRONOUSLY starves every subscriber registered after it. This
/// class walks GetInvocationList() and isolates each handler instead. That is NOT a fix for UI-6: the
/// store's own three sites are untouched and still queued. It is a refusal to add a fourth.
/// </remarks>
public sealed class ConsolePlaybackState : IDisposable
{
  private readonly AudioStateStore _store;
  private readonly ILogger<ConsolePlaybackState> _logger;

  /// <summary>Subscribes to the store for the life of the process.</summary>
  public ConsolePlaybackState(AudioStateStore store, ILogger<ConsolePlaybackState> logger)
  {
    _store = store;
    _logger = logger;
    _store.EventPlaybackChanged += OnStoreChangedAsync;
  }

  /// <summary>Raised after the store's snapshot changes. Subscribers must unsubscribe on dispose.</summary>
  public event Func<Task>? Changed;

  /// <summary>The current attended-playback snapshot, or null when nothing has ever been started.</summary>
  /// <remarks>
  /// ⚠ Null is NOT "nothing is playing". A terminal snapshot — Completed, Stopped or Failed — is
  /// RETAINED until a new playback replaces it, deliberately, because it is the only surface an
  /// acquisition failure can be read from (ADR-029 §8.1). Read <see cref="IsLive"/>, never null-ness.
  /// </remarks>
  public EventPlaybackSnapshotDto? Snapshot => _store.EventPlayback;

  /// <summary>True while the console could still be producing sound for this playback.</summary>
  public bool IsLive => Snapshot?.IsLive == true;

  /// <summary>"Voicemail" or "Message" — the KIND, never the sender (handoff §Cross-3).</summary>
  /// <remarks>
  /// ⚠ A Kind this build has never heard of falls through to "Playing" rather than throwing or
  /// painting a raw wire token on the panel. Same rule the State strings follow and for the same
  /// reason: the wire carries strings so a newer API can add a value without a lockstep Web deploy.
  /// </remarks>
  public string KindLabel => Snapshot?.Kind switch
  {
    "RemoteMedia" => "Voicemail",
    "Speech" => "Message",
    _ => "Playing"
  };

  private async Task OnStoreChangedAsync()
  {
    var handlers = Changed?.GetInvocationList();
    if (handlers is null)
    {
      return;
    }

    foreach (var handler in handlers)
    {
      try
      {
        await ((Func<Task>)handler).Invoke();
      }
      catch (Exception ex)
      {
        // Per subscriber, so one circuit's failure cannot silence another's — including a handler
        // that throws SYNCHRONOUSLY, before its first await, which is the starving half of UI-6.
        _logger.LogWarning(ex, "A console-playback subscriber threw; the others still ran");
      }
    }
  }

  /// <summary>Releases the store subscription.</summary>
  public void Dispose() => _store.EventPlaybackChanged -= OnStoreChangedAsync;
}
```

⚠ **`GenerateDocumentationFile` is `true` and `TreatWarningsAsErrors` is `true`**
(`Directory.Build.props:3,8`), so every public member above needs its `<summary>`. They are written;
do not strip them.

**Registration** — `src/Radio.Web/Program.cs`, immediately after `AddSingleton<AudioStateStore>()` at
`:463`, because the constructor takes the store and reading them adjacently is how the next person
understands the wiring:

```csharp
// ADR-029 PR 6 — the ONE subscriber to AudioStateStore.EventPlaybackChanged. See the class remarks:
// it caches nothing, and it exists so the chip and the voicemail transport do not each become a
// subscriber to a singleton event whose NotifyAsync awaits only the last handler (UI-6).
builder.Services.AddSingleton<Radio.Web.Services.ConsolePlaybackState>();
```

⚠ **DI singletons are lazy and this one has a side effect in its constructor.** `MainLayout` resolves
it (Task 4) and renders on every route under it, so the subscription exists from the first circuit.
**Do not add an `IHostedService` to force it** — that constructs it before `AudioStateHubService` has
connected, buys nothing, and adds a startup ordering dependency. ⚠ The precedent for getting this
wrong is in the tree: `AudioStateStore` itself had **zero consumers and was never constructed in its
life** until `ENC-12` (`PHN-1c` §5).

---

### Task 3 — `EventPlaybackApiService` gains the four transport verbs

**File:** `src/Radio.Web/Services/ApiClients/EventPlaybackApiService.cs`

**3a. Correct the class remark first (C-79.1).** `:11-16` currently says the file has two methods
*"deliberately"* and that the other four *"belong to PR 6."* This is PR 6. Replace it:

```csharp
/// <remarks>
/// ⚠ Six methods: the four transport verbs plus the read and the stop. PHN-1e shipped only
/// GetCurrentAsync and StopAsync on the principle that a client method with no caller is a claim that
/// a surface exists; PR 6 is that surface, so the rest land here.
///
/// ⚠ Every method swallows and returns null / false rather than throwing. The callers are Blazor
/// event handlers on a wall panel: an exception out of one is an unhandled circuit error and a blank
/// screen, which is strictly worse than a button that appeared not to work. The server is the
/// authority regardless — the next broadcast corrects whatever the caller assumed.
/// </remarks>
```

**3b. The methods.** Append inside the class, after `StopAsync`:

```csharp
  /// <summary>Starts a voicemail playback. Returns the accepted snapshot, or null with a reason.</summary>
  /// <remarks>
  /// ⚠ The 202 answers BEFORE any audio exists (ADR-029 §8.1). A non-null return therefore means
  /// "accepted", never "playing" — the snapshot's State is Preparing and the outcome arrives later on
  /// the hub. A caller treating this as success renders Playing over a fetch that is about to 404 in a
  /// blackout, which is the failure handoff §Cross-5 exists to prevent.
  ///
  /// ⚠ 409 is an expected answer, not an error, and is not logged as one: it is what the API returns
  /// when GvMedia:Enabled is false (EventPlaybackController.cs:96-104). It comes back as a reason
  /// string so the caller can say what happened instead of showing a generic failure.
  ///
  /// ⚠ Voicemail only. The Speech arm has no caller until PHN-3, and this file's own history is the
  /// argument for not adding one before it does.
  /// </remarks>
  public async Task<(EventPlaybackSnapshotDto? Snapshot, string? Reason)> StartVoicemailAsync(
    string mediaId, int durationSeconds, string? label,
    CancellationToken cancellationToken = default)
  {
    try
    {
      // An anonymous body rather than a shared DTO: Radio.Web has no copy of EventPlaybackRequestDto
      // and must not grow one for five fields. The API's binder is case-insensitive and every field
      // on that DTO is nullable by design (EventPlaybackModels.cs:18-45), so an omitted field is a
      // well-defined null that Validate rejects by name rather than a model-binder 400.
      var body = new
      {
        kind = "RemoteMedia",
        mediaKind = "GvVoicemail",
        mediaId,
        durationSeconds,
        label
      };

      using var response =
        await _httpClient.PostAsJsonAsync("/api/audio/events", body, cancellationToken);

      if (response.IsSuccessStatusCode)
      {
        var snapshot = await response.Content.ReadFromJsonAsync<EventPlaybackSnapshotDto>(
          cancellationToken: cancellationToken);
        return (snapshot, null);
      }

      var reason = await ReadReasonAsync(response, cancellationToken);
      _logger.LogWarning(
        "Attended playback refused: {Status} {Reason}", (int)response.StatusCode, reason);
      return (null, reason);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to start attended playback");
      return (null, "Transport");
    }
  }

  /// <summary>Seeks. Returns the re-anchored snapshot, or null when it did not apply.</summary>
  public Task<EventPlaybackSnapshotDto?> SeekAsync(
    string playbackId, TimeSpan position, CancellationToken cancellationToken = default) =>
    PostTransportAsync(
      $"/api/audio/events/{Uri.EscapeDataString(playbackId)}/seek",
      new { positionSeconds = position.TotalSeconds },
      cancellationToken);

  /// <summary>Pauses. Returns the re-anchored snapshot, or null when it did not apply.</summary>
  public Task<EventPlaybackSnapshotDto?> PauseAsync(
    string playbackId, CancellationToken cancellationToken = default) =>
    PostTransportAsync(
      $"/api/audio/events/{Uri.EscapeDataString(playbackId)}/pause", null, cancellationToken);

  /// <summary>Resumes. Returns the re-anchored snapshot, or null when it did not apply.</summary>
  public Task<EventPlaybackSnapshotDto?> ResumeAsync(
    string playbackId, CancellationToken cancellationToken = default) =>
    PostTransportAsync(
      $"/api/audio/events/{Uri.EscapeDataString(playbackId)}/resume", null, cancellationToken);

  /// <remarks>
  /// ⚠ A 404 or a 409 returns null and logs NOTHING. Both are ordinary: 404 is a playback that ended
  /// between the render and the tap, and 409 is a transport verb reaching a playback that has no
  /// source yet — which is exactly what Preparing and Waiting are (PHN-1f §0.2, S15). Neither is worth
  /// a line on a box where log volume is audible.
  /// </remarks>
  private async Task<EventPlaybackSnapshotDto?> PostTransportAsync(
    string path, object? body, CancellationToken cancellationToken)
  {
    try
    {
      using var response = body is null
        ? await _httpClient.PostAsync(path, content: null, cancellationToken)
        : await _httpClient.PostAsJsonAsync(path, body, cancellationToken);

      return response.IsSuccessStatusCode
        ? await response.Content.ReadFromJsonAsync<EventPlaybackSnapshotDto>(
            cancellationToken: cancellationToken)
        : null;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Attended playback transport call failed");
      return null;
    }
  }

  private static async Task<string?> ReadReasonAsync(
    HttpResponseMessage response, CancellationToken cancellationToken)
  {
    try
    {
      using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
      using var document = await System.Text.Json.JsonDocument.ParseAsync(
        stream, cancellationToken: cancellationToken);
      return document.RootElement.TryGetProperty("reason", out var reason)
        ? reason.GetString()
        : null;
    }
    catch
    {
      // A body that is not the { error, reason } shape is not worth failing over. The caller's
      // fallback copy is honest for an unrecognised reason.
      return null;
    }
  }
```

---

### Task 4 — `MainLayout`: the console-playback chip

**File:** `src/Radio.Web/Components/Layout/MainLayout.razor`

**4a. Two injections.** After `:20` (`@inject AudioStateStore AudioState`):

```razor
@inject Radio.Web.Services.ConsolePlaybackState ConsolePlayback
@inject EventPlaybackApiService EventPlaybackApi
```

`EventPlaybackApiService` is already registered (`Program.cs:111`).

**4b. The markup.** Insert as the **last child of `.topbar-primary`, immediately before
`<div class="topbar-nav">` at `:129`** — i.e. after the output-picker wrapper's closing `</div>` at
`:125`. `.topbar-nav` carries `margin-left: auto`, so a sibling placed here lands in the empty span
between the Out picker and the nav pills, which is where handoff §Cross-3's diagram puts it.

```razor
      @* ADR-029 §7.2 / handoff §Cross-3 — the console-playback chip: the global stop control, and
         the thing that earns "playback survives navigation."

         Here and NOT in NowPlayingDock, because :878 gates the dock on IsDockVisible => !_isOnHome —
         a stop control absent on the landing page is not a global remote.

         A real <button>, not a marker on the /phone pill: that pill's bottom-right is already the
         bell-fault glyph (.phone-nav-fault), which is pointer-events: none, and this must be tappable.

         No pulse, no blink — a pulsing element on a wall panel in a dark room is what the sleep work
         exists to prevent. It occupies no space at rest: the markup is not rendered at all. *@
      @if (ConsolePlayback.IsLive)
      {
        <button type="button" class="nav-pill nav-pill-playing"
                @onclick="StopConsolePlaybackAsync"
                title="@ConsolePlaybackTitle"
                aria-label="@ConsolePlaybackAriaLabel">
          <RadzenIcon Icon="volume_up" />
          <span class="nav-pill-label">@ConsolePlayback.KindLabel</span>
          <RadzenIcon Icon="stop_circle" />
        </button>
      }
```

**4c. Subscribe.** In the `firstRender` block, beside the two existing store subscriptions at
`:402-403`:

```csharp
      // ADR-029 PR 6 — the chip. ConsolePlaybackState subscribes to the STORE itself; this subscribes
      // to IT, so the store's own event keeps exactly one handler no matter how many circuits are
      // open (plan §0.6).
      ConsolePlayback.Changed += OnConsolePlaybackChangedAsync;
```

**4d. The handler and the two label properties.** Beside `OnEncoderConfigStatusChangedAsync` at
`:1327`:

```csharp
  /// <remarks>
  /// ⚠ Nothing before the first await may throw. ConsolePlaybackState isolates each subscriber, but
  /// AudioStateStore.NotifyAsync one link further up does not, and a synchronous throw there starves
  /// every handler registered after it (queue row UI-6). Keeping this body to a bare InvokeAsync is
  /// what makes that unreachable rather than merely unlikely.
  /// </remarks>
  private Task OnConsolePlaybackChangedAsync() => InvokeAsync(StateHasChanged);

  // Handoff §Cross-3: the chip's LABEL is the kind; the SENDER goes in title and aria-label, which
  // have room. The seam's Label is already "Voicemail from Jane" and is capped at 128 chars server
  // side (PHN-1b §0.3), so it is safe to interpolate.
  private string ConsolePlaybackTitle => ConsolePlayback.Snapshot?.Label is { Length: > 0 } label
    ? $"Playing {label} on the console. Tap to stop."
    : "Playing on the console. Tap to stop.";

  private string ConsolePlaybackAriaLabel => ConsolePlayback.Snapshot?.Label is { Length: > 0 } label
    ? $"Stop playing {label}"
    : "Stop playing on the console";

  /// <remarks>
  /// ⚠ One tap, no confirm. Stopping playback is not destructive, and a confirm dialog on a wall panel
  /// costs more than the mistake it prevents (handoff §Cross-3).
  ///
  /// ⚠ No optimistic hide. The chip disappears when the SERVER says so, because a chip that vanished
  /// on tap and then came back — a stop that raced a natural end, or lost — is worse than one that
  /// takes ~5 ms to go. The round trip is Web→API on the same box.
  /// </remarks>
  private async Task StopConsolePlaybackAsync()
  {
    var id = ConsolePlayback.Snapshot?.Id;
    if (!string.IsNullOrEmpty(id))
    {
      await EventPlaybackApi.StopAsync(id);
    }
  }
```

**4e. Unsubscribe.** Beside `:1496-1497`, with the reason the comment there already gives:

```csharp
    // ADR-029 PR 6. ConsolePlaybackState is a singleton for the same reason AudioStateStore is, so
    // the same hazard applies: a missed unsubscribe here pins every circuit that has ever rendered
    // this layout for the life of the process.
    ConsolePlayback.Changed -= OnConsolePlaybackChangedAsync;
```

⚠ **The chip must be correct on FIRST PAINT and this task adds no fetch to make it so.**
`AttendedPlaybackCircuitHandler.OnCircuitOpenedAsync` already seeds `AudioStateStore` from
`GET /api/audio/events/current` (`PHN-1e` Task 6), so a circuit arriving mid-playback renders the chip
without asking. ⚠ **Known limit, and it is a UAT instruction rather than a code change:** that seed is
one-shot per process and burns its flag *before* the HTTP call, so if the call ever throws, **no later
circuit can seed for the life of the process** (`FUTURE-WORK.md` §21 item 2). §3 Part 4 step 7 tells
the owner to restart `radio-web` before concluding the chip is broken.

---

### Task 5 — CSS: the chip, and the three primitive gaps Feature A depends on

**File:** `src/Radio.Web/wwwroot/css/design-system.css`

⚠ **Anchor correction — handoff §G-2's two line numbers are swapped.** It cites
`.transport-btn-primary` at `:683` and `.transport-btn` at `:695`; at `ba1ae4a6` it is the reverse
(`.transport-btn` `:683`, `.transport-btn-primary` `:695`). **The substance is unaffected and is the
part that matters:** they are two independent classes, not a base and a modifier, so
`class="transport-btn-primary"` alone gets no `border-radius: 50%`, no background, no border and no
colour — today's 56×56 play control is a square UA button. Use the real anchors; do not amend the
handoff.

**5a. G-1 — `.spinner` renders nothing today, and this is a prerequisite for observing `Preparing`
at all.** `:1211` is `animation` and nothing else. Verified: no width, height, border, radius,
background or `::before` in any stylesheet, and Radzen's `material-dark-base.css` contains **zero**
occurrences of `spinner`, so there is no fallback. An empty inline `<span>` has no intrinsic size, so
it rotates nothing — **every buffering state on the phone surface is invisible right now**, including
`VoicemailPlayer.razor:25`. Replace `:1211`:

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

The existing reduced-motion override at `:1690-1693` (`animation: none !important; opacity: 0.7`)
then leaves a **static ring**, which still reads as busy. That is the intended outcome and is why the
border, not the animation, carries the meaning.

**5b. G-2 — give both transport classes a surface.** Append after `:705`:

```css
.transport-btn-primary,
.transport-btn-secondary {
  appearance: none; -webkit-appearance: none;
}
.transport-btn-primary {
  background: var(--accent-dim);
  border: 1px solid rgba(92, 212, 232, 0.22);
  color: var(--accent-primary);
}
.transport-btn-secondary {
  background: transparent;
  border: 1px solid var(--surface-separator);
  color: var(--text-medium);
}
.transport-btn-secondary:hover { color: var(--text-high); border-color: rgba(255, 255, 255, 0.10); }
/* The house disabled convention for buttons — opacity 0.35 + not-allowed, as at :4973, :5406, :5642.
   NOT the 0.4 + pointer-events: none used for non-buttons; a disabled button must still be hoverable
   so its title explains why. */
.transport-btn:disabled, .transport-btn-primary:disabled { opacity: 0.35; cursor: not-allowed; }
.transport-btn:focus-visible,
.transport-btn-primary:focus-visible { outline: none; box-shadow: inset 0 0 0 2px var(--accent-primary); }
```

⚠ **The markup half is not optional.** Task 6 gives the primary button **both** classes
(`class="transport-btn transport-btn-primary"`); the CSS above supplies the surface and
`.transport-btn` at `:683` supplies the shape. Either half alone leaves the button wrong.

**5c. G-3 — the scrubber's hit area.** `:5716` is `padding: 11px 0` around a 3px bar — **25px total**.
The bar stays tap-to-seek in this row, so it grows to the handoff's number:

```css
.vm-scrubber { flex: 1; padding: 20px 0; cursor: pointer; }   /* 40 + 3 = 43px */
```

⚠ **The trailing comment must change with it.** The current one says *"3px bar + pad ≥24px hit area"*
and would become false — the exact `CLAUDE.md` § Pre-Merge Review class. ⚠ And **do not round the
padding to 21px to "make it 44"**: the handoff specifies `20px 0` explicitly, `--touch-min` is
measured on the rendered row rather than this element alone, and `.now-playing-dock-progress` carries
`min-width: 200px` (`:2461`) which the flex row must already accommodate. Ship the handoff's number
and let the Tester measure.

**5d. The chip, and two supporting rules.** Append at the end of the `§Ph` block (after `:5723`):

```css
/* §Ph — console playback chip (ADR-029 §7.2 / handoff §Cross-3). Geometry is .nav-pill's; the surface
   is the .phone-mode-btn.active recipe, so "live" reads the same everywhere on this surface. */
.nav-pill.nav-pill-playing {
  background: var(--accent-dim);
  box-shadow: inset 0 0 0 1px rgba(92, 212, 232, 0.22);
}
.nav-pill.nav-pill-playing .rzi,
.nav-pill.nav-pill-playing .nav-pill-label { color: var(--accent-primary); }

/* §Ph — the error row grows a sub-line (handoff §Cross-5), and the scrubber grows an indeterminate
   state for a voicemail whose DurationSeconds is 0 (handoff §A5 / Q10). */
.vm-player-error-text { display: flex; flex-direction: column; gap: 2px; flex: 1; min-width: 0; }
.vm-player-error-sub { color: var(--text-medium); font-size: 13px; }
.vm-scrubber-indeterminate { cursor: default; opacity: 0.45; }
```

⚠ **No `:root` changes** (§0.5 item 8), and **no new consumers of `--signal-green-glow` or
`--signal-red-glow`**: both are consumed at `:5150`, `:5388`, `:5395` and **declared nowhere**, so they
resolve to nothing while looking correct in the source. The file documents its own trap at
`:5341-5344`. Nothing here uses them and nothing here should.

⚠ **New CSS goes in `design-system.css` §Ph, not a sidecar.** `PhonePage.razor.css:1` states the
policy: *"all visual styles are in design-system.css §Ph. This file only handles CSS isolation for
page-level layout."*

---

### Task 6 — `VoicemailPlayer.razor`: the rewrite

**File:** `src/Radio.Web/Components/Pages/VoicemailPlayer.razor` — replaced wholesale.

This is the row. Read handoff §A2 (layout), §A3 (scrubber), §A4 (stop vs pause), §A5 (states) and
§Cross-5 (failure) alongside it; every presentation decision below is theirs.

**6a. The file.**

```razor
@using Radio.Web.Models
@using Radio.Web.Services
@using Radio.Web.Services.ApiClients
@inject EventPlaybackApiService EventPlaybackApi
@inject ConsolePlaybackState ConsolePlayback
@inject AudioStateStore AudioState
@inject AudioApiService AudioApi
@inject IJSRuntime JS
@implements IDisposable

@* ADR-029 Feature A. This transport is a REMOTE CONTROL for something happening in the room, not a
   media player: there is latency on every command, the sound does not come from the thing being
   touched, and it keeps going after this component is disposed.

   ⛔ There is no <audio> element and there must never be one again. That element is what bypassed
   mute, master volume, balance, ducking and Cast routing (owner decision D17). *@
<div class="vm-player">

  @if (ShowFailure)
  {
    <div class="vm-player-error" role="alert">
      <RadzenIcon Icon="error_outline" />
      <div class="vm-player-error-text">
        <span>@FailurePrimary</span>
        @if (FailureSubline.Length > 0)
        {
          <span class="vm-player-error-sub">@FailureSubline</span>
        }
      </div>
      <button type="button" class="phone-btn-sm" @onclick="PlayAsync">Retry</button>
    </div>
  }
  else
  {
    <div class="vm-player-transport">
      @* Handoff §A2 / Q11: the numeral-free glyphs. Material Icons has replay_10 and replay_30 but no
         _15, and a glyph that says "10" while the button does 15 is a small permanent lie. The real
         interval lives in the aria-label. *@
      <button type="button" class="transport-btn transport-btn-secondary"
              aria-label="Back 15 seconds" title="Back 15 seconds"
              disabled="@(!CanSkip)" @onclick="() => SkipAsync(-15)">
        <RadzenIcon Icon="fast_rewind" />
      </button>

      <button type="button" class="transport-btn transport-btn-primary"
              aria-label="@PrimaryLabel" @onclick="TogglePlayAsync">
        @if (IsMineAndPreparing)
        {
          <span class="spinner"></span>
        }
        else
        {
          <RadzenIcon Icon="@(IsMineAndPlaying ? "pause" : "play_arrow")" />
        }
      </button>

      <button type="button" class="transport-btn transport-btn-secondary"
              aria-label="Forward 15 seconds" title="Forward 15 seconds"
              disabled="@(!CanSkip)" @onclick="() => SkipAsync(15)">
        <RadzenIcon Icon="fast_forward" />
      </button>

      @* Seek is meaningless without a total, so an unknown duration downgrades the ROLE rather than
         leaving an unchangeable slider — an accessibility lie in exactly that state (handoff §A6). *@
      @if (DurationKnown)
      {
        <div class="vm-scrubber" role="slider"
             aria-label="Playback position"
             aria-valuemin="0" aria-valuemax="@((int)TotalSeconds)"
             aria-valuenow="@((int)ElapsedSeconds)"
             @onclick="OnScrubberClickAsync" @ref="_scrubberEl">
          <div class="now-playing-dock-progress">
            <div class="now-playing-dock-progress-bar" style="width:@ProgressWidth"></div>
          </div>
        </div>
      }
      else
      {
        <div class="vm-scrubber vm-scrubber-indeterminate" role="progressbar"
             aria-label="Playback position" aria-valuetext="Unknown length">
          <div class="now-playing-dock-progress"></div>
        </div>
      }

      <span class="vm-time">@FormatTime(ElapsedSeconds) / @TotalDisplay</span>

      <button type="button" class="transport-btn transport-btn-secondary"
              aria-label="Stop playing" title="Stop"
              disabled="@(!IsMineAndLive)" @onclick="StopAsync">
        <RadzenIcon Icon="stop" />
      </button>
    </div>

    @if (IsMineAndPreparing)
    {
      <div class="vm-buffering-note">Fetching recording…</div>
    }
    else if (IsMineAndWaiting)
    {
      @* PHN-1f §0.6 item 3: say WHY, not just THAT. A bare spinner is the complaint D28 rejected,
         rendered. The snapshot does not carry the blocker's identity and this row does not ask for it
         — one voice at a time is the invariant that makes naming it unnecessary. *@
      <div class="vm-buffering-note">Waiting for the announcement to finish…</div>
    }

    @if (_mutedAtPlay)
    {
      <div class="phone-pill amber">
        <span>The console is muted.</span>
        <button type="button" class="phone-btn-sm" @onclick="UnmuteAsync">Unmute</button>
      </div>
    }
  }

  <div class="vm-transcript">
    <div class="vm-transcript-heading">Transcript</div>
    @if (!string.IsNullOrWhiteSpace(Item.Transcript))
    {
      <div class="vm-transcript-body">@Item.Transcript</div>
    }
    else if (IsRecent)
    {
      <div class="vm-transcript-pending" aria-live="polite">
        Transcript pending — Google is still transcribing this voicemail.
      </div>
    }
    else
    {
      <div class="vm-transcript-absent">No transcript available.</div>
    }
  </div>

  @* The sound is somewhere else, so a non-sighted user gets no other confirmation (handoff §A6).
     Same idiom as Skeleton.razor:79. *@
  <span class="visually-hidden" role="status" aria-live="polite">@LiveRegionText</span>
</div>

@code {
  [Parameter, EditorRequired] public VoicemailItemDto Item { get; set; } = default!;
  [Parameter] public EventCallback OnHeard { get; set; }

  private string? _playbackId;    // the handle THIS component started, or null
  private string? _startReason;   // a synchronous 400/409; never reaches a Failed snapshot
  private bool _heardSent;
  private bool _mutedAtPlay;
  private ElementReference _scrubberEl;

  protected override void OnInitialized() => ConsolePlayback.Changed += OnPlaybackChangedAsync;

  // ⛔ NOTHING ELSE HERE, AND THAT IS THE POINT. There is deliberately no stop on disposal.
  // ADR-029 §7.2: "component disposal is a rendering event, not a user intent." The previous file's
  // DisposeAsync tore down the browser <audio> and therefore silenced the room on navigate-away; the
  // topbar chip is what earns removing that.
  //
  // Three shipped mechanisms stop unattended audio and NONE of them is this component: the
  // last-circuit backstop (D30 — a page reload DOES stop it, deliberately), the two /sleep edges
  // (ADR §16.5), and the GvMedia:MaxPlaybackSeconds cap.
  public void Dispose() => ConsolePlayback.Changed -= OnPlaybackChangedAsync;

  private Task OnPlaybackChangedAsync() => InvokeAsync(StateHasChanged);

  // ── which snapshot is OURS ───────────────────────────────────────────────────────────────────
  //
  // ⚠ Every predicate below is gated on the snapshot's id matching the handle THIS component started.
  // Playback state is global (ADR-029 D6) and at most one thing plays anywhere on the surface
  // (handoff §A4b), so without this gate every open voicemail row would render the transport for
  // whatever is playing — including something started from a different row, or a different browser.
  private EventPlaybackSnapshotDto? Mine =>
    _playbackId is not null && ConsolePlayback.Snapshot?.Id == _playbackId
      ? ConsolePlayback.Snapshot
      : null;

  private string? MineState => Mine?.State;
  private bool IsMineAndLive => Mine?.IsLive == true;
  private bool IsMineAndPlaying => MineState == "Playing";
  private bool IsMineAndPreparing => MineState == "Preparing";
  private bool IsMineAndWaiting => MineState == "Waiting";
  private bool CanSkip => IsMineAndPlaying || MineState == "Paused";
  private bool ShowFailure => _startReason is not null || MineState == "Failed";

  // ── the anchor, interpolated locally ─────────────────────────────────────────────────────────
  //
  // ⚠ There is NO position tick and there must not be one (ADR-029 §8.2). The snapshot carries
  // PositionAtBroadcast + BroadcastAtUtc + State; the client advances from that anchor on its own
  // clock and re-anchors on every transition.
  //
  // ⚠ "while State == Playing" is load-bearing, not stylistic. While Waiting the position is zero and
  // STAYS zero, and interpolating there would show a bar advancing through audio that does not exist
  // (PHN-1f §0.6 item 2).
  private double ElapsedSeconds
  {
    get
    {
      var snapshot = Mine;
      if (snapshot is null)
      {
        return 0;
      }

      var elapsed = snapshot.PositionAtBroadcast;
      if (snapshot.State == "Playing")
      {
        elapsed += DateTimeOffset.UtcNow - snapshot.BroadcastAtUtc;
      }

      var seconds = Math.Max(0, elapsed.TotalSeconds);
      return TotalSeconds > 0 ? Math.Min(seconds, TotalSeconds) : seconds;
    }
  }

  // ⚠ DurationSeconds == 0 means UNKNOWN in the GV contract (ADR-022 §4.2, ADR-029 §4.1). It is a live
  // and expected case, not a defect. Never render 0:00 as a real total: the same number drives the bar
  // AND AudioFileEventSource's completion timer, so a confident zero is a bar that crawls to 100% and
  // stops while sound continues.
  //
  // ⚠ The snapshot's Duration outranks the DTO's, because the server resolved "0 means unknown" into a
  // null and the DTO did not.
  private double TotalSeconds => Mine?.Duration?.TotalSeconds ?? Item.DurationSeconds;
  private bool DurationKnown => TotalSeconds > 0;
  private string TotalDisplay => DurationKnown ? FormatTime(TotalSeconds) : "--:--";
  private string ProgressWidth =>
    (DurationKnown ? Math.Clamp(ElapsedSeconds / TotalSeconds * 100, 0, 100) : 0)
      .ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%";

  private string PrimaryLabel => IsMineAndPlaying
    ? "Pause"
    : $"Play voicemail from {Item.FromName ?? Item.FromNumber}";

  private string LiveRegionText => MineState switch
  {
    "Preparing" => "Fetching the recording.",
    "Waiting" => "Waiting for the console.",
    "Playing" => "Playing on the console.",
    "Paused" => "Paused.",
    "Stopped" => "Stopped.",
    "Completed" => "Finished.",
    _ => string.Empty
  };

  // ── the transport actions ────────────────────────────────────────────────────────────────────

  private async Task TogglePlayAsync()
  {
    if (IsMineAndPlaying)
    {
      await PauseAsync();
      return;
    }

    if (MineState == "Paused")
    {
      await EventPlaybackApi.ResumeAsync(_playbackId!);
      return;
    }

    // Tapping the ACTIVE item's own button while it is preparing or waiting stops it — handoff
    // §Cross-1's "play → stop → play", which is unambiguous here because the primary shows a spinner
    // rather than a play glyph in those states.
    if (IsMineAndLive)
    {
      await StopAsync();
      return;
    }

    await PlayAsync();
  }

  private async Task PlayAsync()
  {
    await MarkHeardOnceAsync();

    // ⚠ Read mute/volume SYNCHRONOUSLY here rather than subscribing to AudioStateStore.VolumeChanged.
    // The handoff's condition is "muted or volume 0 AT PLAY TIME", which is a snapshot; and
    // VolumeChanged already has live subscribers, so adding one would take a store event this row has
    // no reason to touch from N to N+1 (plan §0.4 C-78).
    _mutedAtPlay = AudioState.PlaybackState?.IsMuted == true
      || AudioState.PlaybackState?.Volume is 0;

    _startReason = null;
    _playbackId = null;

    var (snapshot, reason) = await EventPlaybackApi.StartVoicemailAsync(
      Item.Id,
      Item.DurationSeconds,
      $"Voicemail from {Item.FromName ?? Item.FromNumber}");

    if (snapshot is null)
    {
      // A refusal answered synchronously — 400 or 409 — never reaches a Failed snapshot, so it has to
      // be held locally or the panel would show nothing at all.
      _startReason = reason ?? "Transport";
      return;
    }

    // ⚠ 202 means ACCEPTED, not playing. The outcome arrives on the hub. Do not render Playing here.
    _playbackId = snapshot.Id;
  }

  private Task PauseAsync() =>
    _playbackId is null ? Task.CompletedTask : EventPlaybackApi.PauseAsync(_playbackId);

  private Task StopAsync() =>
    _playbackId is null ? Task.CompletedTask : EventPlaybackApi.StopAsync(_playbackId);

  private async Task SkipAsync(int deltaSeconds)
  {
    if (_playbackId is null)
    {
      return;
    }

    var target = TimeSpan.FromSeconds(Math.Max(0, ElapsedSeconds + deltaSeconds));
    await EventPlaybackApi.SeekAsync(_playbackId, target);
  }

  // ⚠ Tap-to-seek only. There is no pointermove handler and never was — the previous player was
  // already tap-only (:36 bound @onclick, :135 took a MouseEventArgs). Drag-scrub needs a ~16 ms
  // budget we do not have and a grabbable thumb .now-playing-dock-progress does not have (handoff
  // §A3). If it is ever added, commit-on-release is the only acceptable pattern.
  //
  // ⚠ The fraction still comes from JS, and that is the ONE thing on this path with no server-side
  // replacement: converting a click's clientX to a fraction needs the element's rendered box, which
  // only the browser has. It is why js/voicemail-player.js is REDUCED rather than deleted (Task 7a).
  private async Task OnScrubberClickAsync(MouseEventArgs e)
  {
    if (_playbackId is null || !DurationKnown)
    {
      return;
    }

    double fraction;
    try
    {
      var module = await JS.InvokeAsync<IJSObjectReference>("import", "./js/voicemail-player.js");
      fraction = await module.InvokeAsync<double>("fractionFromEvent", _scrubberEl, e.ClientX);
    }
    catch
    {
      // bUnit's loose interop and a missing module both land here. A seek that cannot be located is a
      // no-op, never an exception out of an event handler on a wall panel.
      return;
    }

    await EventPlaybackApi.SeekAsync(
      _playbackId, TimeSpan.FromSeconds(Math.Clamp(fraction, 0, 1) * TotalSeconds));
  }

  private async Task UnmuteAsync()
  {
    _mutedAtPlay = false;
    await AudioApi.SetMuteAsync(false);
  }

  private async Task MarkHeardOnceAsync()
  {
    if (_heardSent)
    {
      return;
    }

    _heardSent = true;
    // UNCHANGED from the previous file, deliberately. Single write path: bubble to
    // PhonePage.OnVoicemailHeard, which does the optimistic flip + durable GV write-through +
    // idempotent reconcile (GV-4 / ADR-024). The player does NOT call MarkVoicemailReadAsync directly.
    await OnHeard.InvokeAsync();
  }

  private bool IsRecent => (DateTime.UtcNow - Item.ReceivedAt) < TimeSpan.FromMinutes(30);

  private static string FormatTime(double seconds)
  {
    if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
    var ts = TimeSpan.FromSeconds(Math.Floor(seconds));
    return ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
  }
}
```

⚠ **Verify `AudioApiService.SetMuteAsync` and `AudioStateStore.PlaybackState`'s member names before
writing the mute lines** — this plan did not read that client's signature, and `IsMuted` / `Volume`
are inferred from the dock's usage. If either differs, use the real one; if `AudioApiService` has no
single-argument mute, **drop the `Unmute` button and keep the pill as a statement**, and say so in the
PR. The pill is *recommended, not required* by the handoff, and a broken button is worse than no
button.

⚠ **One thing deliberately dropped, recorded so it reads as a decision.** Handoff §A3 asks for a
`Seeking` state where the time readout dims to `--text-medium` until the server re-anchors. It is not
implemented: the round trip is ~30–85 ms on a component that re-renders from a hub broadcast, so the
dim would be sub-frame in the common case and indistinguishable from a flicker in the slow one.
**Raised in §6.2 for the Designer** rather than silently omitted.

**6b. The failure copy — handoff §Cross-5's table, extended by one row (C-71).** Add to `@code`:

```csharp
  private string FailureCode => _startReason ?? Mine?.FailureReason ?? string.Empty;

  /// <remarks>
  /// ⚠ FIVE rows, not the handoff's four — an extension UNDER its own rule rather than a deviation
  /// from it: "Four distinct failures, four distinct sentences. Do not collapse them."
  /// MediaUnauthorized is a fifth distinct failure with a fifth distinct fix.
  ///
  /// ⚠ The distinction that matters most in the room: MediaNotFound does NOT mean the recording is
  /// gone. RotaryPhone's GetAudio resolves through FindNodeAsync, which never checks its list result's
  /// Succeeded flag, so a Google Voice auth blackout — dead ~9 minutes in every 20 — surfaces here as
  /// a 404. Six distinct transient conditions reach it. "This usually clears up in a minute" is
  /// therefore an ACCURATE promise, not a soothing one, and the cache makes Retry's odds real.
  ///
  /// ⚠ And MediaUnauthorized must NEVER wear that sub-line, because it never clears up: it means
  /// GvMedia:AuthKey and RotaryPhone's InterServiceAuthKey have diverged, which is two hand edits on
  /// two files the deploy does not re-seed. Giving it the blackout's copy sends the owner into a retry
  /// loop against a configuration fault, and nothing else on the box would tell him.
  /// </remarks>
  private string FailurePrimary => FailureCode switch
  {
    "MediaUnauthorized" => "The console isn't allowed to fetch recordings.",
    "MediaDisabled" or "Disabled" => "Voicemail playback is switched off on the console.",
    "MediaTooLarge" => "This recording is too large for the console to play.",
    "WaitExpired" => "The console was busy and gave up waiting.",
    "MediaNotFound" or "MediaUpstream" or "MediaTimeout" or "MediaTransport" or "MediaUnknown"
      or "Transport" => "Couldn't load this recording.",
    _ => "The console can't play this right now."
  };

  private string FailureSubline => FailureCode switch
  {
    "MediaUnauthorized" => "This won't clear on its own — the console's key doesn't match the bridge's.",
    "MediaNotFound" or "MediaUpstream" or "MediaTimeout" or "MediaTransport" or "MediaUnknown"
      or "Transport" => "This usually clears up in a minute. Try again.",
    _ => string.Empty
  };
```

⚠ **`_ =>` catches `MediaAcquisitionFailed` and every rejection name**, and its copy — *"The console
can't play this right now."* — is the handoff's engine-refusal row. It is correct copy and a
**misleading diagnosis for one case**: an unwritable `./data/gvmedia` lands there too, because
`GvMediaCache.WriteAsync` lets `Directory.CreateDirectory` throw outside its inner `try` (§0.4 C-76).
That is why §3's fail path sends the owner to the log line rather than to the panel, and why nothing
in this table pretends to diagnose it.

---

### Task 7 — the deletions, together

⚠ **`PHN-1b` §5 and `PHN-1c` §5 both say the `<audio>` element, the JS and `GetVoicemailAudioUrl` go
together, and they are right about the ordering hazard.** Task 6 removed the element; this removes
what fed it.

**7a. `src/Radio.Web/wwwroot/js/voicemail-player.js` — reduced, not deleted.** Everything that
attaches to an `<audio>` element goes. One function survives, because it needs something only the
browser has:

```javascript
// ADR-029 PR 6 — what survives of the browser voicemail player.
//
// Everything that attached to an <audio> element is gone: the console fetches, decodes and plays
// voicemail itself now, through the audio engine, so the browser has no audio to drive. ⛔ Do not
// re-add a play/pause/seek surface here — a second audio path is exactly what D17 was about.
//
// This one function remains because tap-to-seek needs the scrubber's RENDERED GEOMETRY, which has no
// server-side equivalent. The API takes the fraction from there.
export function fractionFromEvent(element, clientX) {
  if (!element) return 0;
  const box = element.getBoundingClientRect();
  if (!box.width) return 0;
  return Math.min(1, Math.max(0, (clientX - box.left) / box.width));
}
```

⚠ **It stays an ES module reached by `import`**, which is what Task 6a's handler does and what the
previous file already did (`:108-110`). That also keeps it testable: `RdsScrollMarqueeTests.cs:28-38`
is the tree's one worked example of `JSInterop.SetupModule("./js/…")` in bUnit, and it is the model if
Task 11 wants to assert the seek call rather than tolerate the loose-interop catch.

**7b. `GvBridgeApiService.GetVoicemailAudioUrl` — deleted.**
`src/Radio.Web/Services/ApiClients/GvBridgeApiService.cs:128-140`, the method and its XML doc.
**`VoicemailPlayer.razor:87` was its only caller** and Task 6 removed it. ADR §3.3 names the deletion.

⚠ **`VoicemailItemDto.AudioUrl` stays** (`ApiModels.cs:1129`). It is RotaryPhone's contract field and
this row does not renegotiate it — but its comment becomes stale advice for a rebuild nothing does any
more, so correct it here:

```csharp
  string AudioUrl);             // relative; the console no longer fetches this — Radio.API does
                                // (ADR-029 D3). Retained because it is RotaryPhone's contract field.
```

**7c. The test that pinned the deleted behaviour.**
`tests/Radio.Web.Tests/Components/VoicemailPlayerTests.cs:32-40`, `Renders_AbsoluteAudioSrc`, asserts
`cut.Find("audio")` carries an absolute `src`. **It must go red, and it must be REPLACED rather than
deleted** — Task 11 replaces it with `Renders_NoAudioElement_BecauseTheConsolePlaysItNow`, the same
assertion inverted. ⚠ Deleting it would make the most important behavioural change in the row
invisible in the diff, which is the failure `PHN-1a` Task 12 exists to prevent.

⚠ Grep `tests/` for `GetVoicemailAudioUrl` and remove every assertion on it in the same commit.

---

### Task 8 — the store's comments stop describing a store with no consumer (C-79.2)

**File:** `src/Radio.Web/Services/AudioStateStore.cs`

`PHN-1e` wrote `EventPlaybackChanged`, `OnHubEventPlaybackChanged` (`:279-295`) and
`EnsureEventPlaybackSeededAsync` (`:299-372`) for a consumer that did not yet exist, and said so. This
row is the consumer. **Grep the file for every sentence asserting that nothing subscribes, or that the
chip is future work, and correct each where it stands.** The change is a clause, not a rewrite:

```diff
-  /// Applies one "EventPlaybackChanged" broadcast. Subscribed to the hub client in the constructor.
+  /// Applies one "EventPlaybackChanged" broadcast. Subscribed to the hub client in the constructor,
+  /// and consumed by ConsolePlaybackState — this event's ONE subscriber, by design (PHN-2 §0.6).
```

⚠ **Do not "improve" `EnsureEventPlaybackSeededAsync` while you are in there.** Its two accepted races
are filed (`FUTURE-WORK.md` §21), and the second — the one-shot burned before the HTTP call — is a
real hazard this row's UAT accounts for (§3 Part 4 step 7). Fixing it here would be an unreviewed
change to a concurrency guard inside the last open P0.

---

### Task 9 — `ConsolePlaybackState` tests

**New file:** `tests/Radio.Web.Tests/Services/ConsolePlaybackStateTests.cs`

House style, confirmed against the shipped suites: **raw xUnit `Assert.*`** — FluentAssertions,
NSubstitute and Moq are all referenced by the csproj and used by **zero** ADR-029 test files, and
`WeatherApiServiceTests.cs:101` records the house rejection of the mocking libraries. No `// Arrange`
comments; `[InlineData]` only; `using Xunit;` omitted because every test csproj declares it implicitly.
`AudioStateStore.OnHubEventPlaybackChanged` is `internal` with `InternalsVisibleTo("Radio.Web.Tests")`
**precisely so a test can drive a broadcast** — use it rather than faking the hub, the same family as
`PhoneHubService.RaiseReadStateChangedForTest`.

```csharp
  [Fact]
  public async Task ItSubscribesToTheStoreExactlyOnce_NoMatterHowManySubscribersItHasItself()
  {
    var store = NewStore();
    using var state = new ConsolePlaybackState(store, NullLogger<ConsolePlaybackState>.Instance);

    var first = 0;
    var second = 0;
    state.Changed += () => { first++; return Task.CompletedTask; };
    state.Changed += () => { second++; return Task.CompletedTask; };

    await store.OnHubEventPlaybackChanged(SnapshotOf("Playing"));

    Assert.Equal(1, first);
    Assert.Equal(1, second);
  }
```

> **Falsifying mutation:** subscribe twice in the constructor (`_store.EventPlaybackChanged +=
> OnStoreChangedAsync;` repeated). Both counters reach 2. ✅ Reds.
> ⚠ **What it does NOT prove:** that no *other* component subscribes to the store directly. That is
> not reachable from a unit test at all; it is Task 13 grep 4.

```csharp
  [Fact]
  public async Task AThrowingSubscriberDoesNotStarveTheOnesRegisteredAfterIt()
  {
    var store = NewStore();
    using var state = new ConsolePlaybackState(store, NullLogger<ConsolePlaybackState>.Instance);

    var reached = false;
    // ⚠ Throws SYNCHRONOUSLY, before any await. That is the half of UI-6 that is starvation rather
    // than a lost log line, and it is the half this class exists not to reproduce.
    state.Changed += () => throw new InvalidOperationException("boom");
    state.Changed += () => { reached = true; return Task.CompletedTask; };

    await store.OnHubEventPlaybackChanged(SnapshotOf("Playing"));

    Assert.True(reached);
  }
```

> **Falsifying mutation:** replace the `GetInvocationList()` loop with `await Changed.Invoke();` —
> exactly `AudioStateStore.NotifyAsync`'s shape. `reached` stays false. ✅ Reds.
> **This is the test that makes §0.6's central claim checkable rather than asserted.**

```csharp
  [Fact]
  public async Task ATerminalSnapshotIsRetained_SoNothingPlayingIsAStateAndNotAnAbsence()
  {
    var store = NewStore();
    using var state = new ConsolePlaybackState(store, NullLogger<ConsolePlaybackState>.Instance);

    await store.OnHubEventPlaybackChanged(SnapshotOf("Playing"));
    Assert.True(state.IsLive);

    await store.OnHubEventPlaybackChanged(SnapshotOf("Completed"));
    Assert.False(state.IsLive);
    Assert.NotNull(state.Snapshot);
  }
```

> **Falsifying mutation:** `Snapshot => IsLive ? _store.EventPlayback : null`. The `Assert.NotNull`
> reds. ✅
> ⚠ **Named honestly: this test does NOT falsify the read-through.** Adding a private
> `EventPlaybackSnapshotDto? _cached` assigned in the handler and returned from `Snapshot` leaves it
> green. The read-through is enforced by Task 13 grep 5 and by nothing else, and saying so is better
> than implying coverage this test does not have.

```csharp
  [Theory]
  [InlineData("RemoteMedia", "Voicemail")]
  [InlineData("Speech", "Message")]
  // A kind this build has never heard of must not paint a raw wire token on the panel.
  [InlineData("SomethingNewer", "Playing")]
  [InlineData(null, "Playing")]
  public async Task TheChipLabelIsTheKind_AndAnUnknownKindDegrades(string? kind, string expected)
  {
    var store = NewStore();
    using var state = new ConsolePlaybackState(store, NullLogger<ConsolePlaybackState>.Instance);

    await store.OnHubEventPlaybackChanged(
      new EventPlaybackSnapshotDto("evp-1", kind, "L", "Playing", null, TimeSpan.Zero,
        DateTimeOffset.UtcNow, null));

    Assert.Equal(expected, state.KindLabel);
  }
```

> **Falsifying mutation:** make the `_ =>` arm return `Snapshot?.Kind ?? ""`. The two degrade rows red. ✅

⚠ **`NewStore()` must build a real `AudioStateStore`**, which takes `AudioStateHubService`. Use
`HermeticTestRig`'s `OfflineHubTransport` rather than a live connection — its header records that an
earlier run made **74 real TCP connections to `localhost:5000`**. If the store cannot be constructed
without a network-touching dependency even offline, **say so in the PR and drop to testing
`ConsolePlaybackState` against a thin seam instead**; do not ship a test whose green depends on
whether `radio-api` happens to be up (that is `TEST-1`'s whole subject).

⚠ **No dispose-unsubscribe test.** The obvious one reflects over `AudioStateStore`'s event backing
field, and a `Func<Task>?` event with explicit accessors would make it pass vacuously. **Stated rather
than shipped**: five consecutive cycles in this arc have found a test that passed against a
deliberately broken implementation, and an assertion that cannot fail is how that keeps happening.
The unsubscribe is covered by inspection and by grep 4.

---

### Task 10 — `TTSEventSource.Position`, and the test written to be updated (C-74)

**File:** `src/Radio.Infrastructure/Audio/Sources/Events/TTSEventSource.cs`

A three-line override mirroring `AudioFileEventSource.cs:93-95`. ⚠ **`TTSEventSource` uses `Id`
directly as its `SoundFlowPlaybackService` key (`:145`)**, unlike `AudioFileEventSource`, which mints a
separate `_playbackId` — so the lookup key here is `Id`, and getting it wrong is ADR §3.3's named
identity hazard: it fails for exactly one of the two arms and looks like it works for the other.

```csharp
  /// <inheritdoc/>
  /// <remarks>
  /// ⚠ The key is Id, NOT a separately minted playback id. This source registers itself with
  /// SoundFlowPlaybackService under Id; AudioFileEventSource does not. Confusing the two is ADR-029
  /// §3.3's identity hazard.
  /// </remarks>
  public override TimeSpan Position =>
    State is AudioSourceState.Playing or AudioSourceState.Paused
      ? _playbackService?.GetPosition(Id) ?? TimeSpan.Zero
      : TimeSpan.Zero;
```

⚠ **Copy `AudioFileEventSource.cs:93-95`'s exact shape and guard rather than this paraphrase**, and
confirm `TTSEventSource`'s playback-service field name — this plan did not read that file's field
declarations.

**Then update, do not delete, `ASpeechSnapshotReportsPositionZeroForItsWholeLife`** in
`tests/Radio.Infrastructure.Tests/Audio/Events/EventPlaybackServiceTests.cs`. `PHN-1c` pinned the old
behaviour deliberately (C-27) and `PHN-1e` §5.2 named this test as the one that should fail here.
Rename it to state what is now true and assert the new behaviour.

⚠ **Honest scope note, repeated from C-74 because a reviewer will ask: this is not visible in this
row.** PR 6 is Feature A, which is `AudioFileEventSource`, and the chip carries no progress bar. It is
here because the handoff assigned it here, and an obligation deferred twice with a weaker reason each
time is an obligation about to be lost.

---

### Task 11 — the component tests

**File:** `tests/Radio.Web.Tests/Components/VoicemailPlayerTests.cs` — largely rewritten.
**New file:** `tests/Radio.Web.Tests/Components/Layout/ConsolePlaybackChipTests.cs`.

The `Register()` helper changes: `GvBridgeApiService` is no longer injected into the player, and
`EventPlaybackApiService`, `ConsolePlaybackState`, `AudioStateStore` and `AudioApiService` are.
⚠ **Adopt `AddHermeticTestRig()`** (`TestHelpers/HermeticTestRig.cs:53-58`) — the current fixture does
not, and hardcodes `http://radio:5004`. `NoNetworkHandlerFilter` only swaps the handler when it is
still the framework default, so a per-test `MockHttpHandler` still wins. House style for HTTP fakes is
a hand-rolled `HttpMessageHandler`; `PhonePageTests.cs:298-347`'s `EmptyResponseHandler` path-switch is
the model for the `/api/audio/events*` routes.

**The tripwire, replacing `Renders_AbsoluteAudioSrc`:**

```csharp
  [Fact]
  public void Renders_NoAudioElement_BecauseTheConsolePlaysItNow()
  {
    Register();
    var cut = RenderComponent<VoicemailPlayer>(p => p.Add(x => x.Item, Vm()));

    // ⚠ THIS IS THE ROW. An <audio> element here is a second audio path that bypasses mute, master
    // volume, balance, ducking and Cast routing (owner decision D17). This test REPLACES
    // Renders_AbsoluteAudioSrc, which asserted the opposite and was correct until this PR.
    Assert.Empty(cut.FindAll("audio"));
  }
```

> **Falsifying mutation:** restore the `<audio src="@AudioSrc">` line. ✅ Reds.

**The rest.** Each drives a snapshot through `store.OnHubEventPlaybackChanged` after the component has
a playback id, and asserts on markup with selector-based assertions in the style of
`VoicemailRowTests` (`Assert.Single(cut.FindAll(".unread-dot"))`, and its paired
`Assert.DoesNotContain("0:00", cut.Markup)`).

| Test | Asserts | Falsifying mutation |
|---|---|---|
| `Idle_DisablesSkipAndStop_AndOffersPlay` | three `disabled` attributes; `play_arrow` | drop the `disabled="@(!CanSkip)"` bindings → reds |
| `Preparing_ShowsTheSpinnerAndTheFetchingNote` | a `.spinner` element; `Fetching recording…` | render the glyph instead of the spinner → reds |
| `Waiting_SaysWhy_AndDoesNotRunTheProgressBar` | `Waiting for the announcement to finish…`; bar width is `0%` | interpolate while `Waiting` (drop the `State == "Playing"` test in `ElapsedSeconds`) → the width leaves 0 → reds |
| `Waiting_StillOffersStop` | the stop button is **not** `disabled` | change `IsMineAndLive` to `MineState == "Playing"` → reds |
| `UnknownDuration_RendersIndeterminate_AndNeverZeroZero` | `role="progressbar"`, `aria-valuetext="Unknown length"`, `--:--` present, `0:00` **absent** | make `TotalSeconds` read `Item.DurationSeconds` unconditionally → `0:00` appears → reds |
| `ASnapshotForADifferentPlaybackDoesNotDriveThisRow` | with a snapshot whose `Id` differs, the row stays Idle | drop the `Mine` id gate → the row renders Playing → reds |
| `MediaUnauthorized_DoesNotSayItWillClearUp` | the auth primary string; sub-line does **not** contain `clears up` | collapse `MediaUnauthorized` into the blackout arm → reds |
| `MediaNotFound_SaysItUsuallyClearsUp` | `This usually clears up in a minute.` | the same collapse in the other direction → reds |
| `MutedAtPlayTime_ShowsTheAmberPill` | `.phone-pill.amber`; `The console is muted.` | read mute *after* the start call → the fake reports unmuted → reds |

⚠ **Write `ASnapshotForADifferentPlaybackDoesNotDriveThisRow` first.** Handoff §A4b makes every play
button on `/phone` one single-selection group over **global** state, so the id gate is the only thing
stopping every open row from rendering somebody else's playback — and it is the cheapest thing to omit.

**The chip tests.** Check whether `Components/Layout/` already has a `MainLayout` render harness and
reuse it. ⚠ **If `MainLayout` is too heavy to render under bUnit, say so in the PR and cover
`ConsolePlaybackState.IsLive`/`KindLabel` instead** — do not ship a chip test that renders nothing and
passes. Minimum coverage:

| Test | Asserts | Falsifying mutation |
|---|---|---|
| `TheChipIsAbsentWhenNothingIsPlaying` | no `.nav-pill-playing` | render it unconditionally → reds |
| `TheChipAppearsForAWaitingPlayback` | `.nav-pill-playing` present; label `Voicemail` | gate the chip on `State == "Playing"` → reds |
| `TheChipIsAbsentForACompletedPlayback` | no `.nav-pill-playing` after `Completed` | gate on `Snapshot is not null` → reds |
| `TheChipIsAButton_AndASiblingOfTheNavPills` | the element is a `<button>` inside `.topbar-primary`, outside `.topbar-nav` | move it inside the `/phone` pill → reds |

---

### Task 12 — documentation, and the two obligations that turn out to be discharged

**12a. `design/INTEGRATIONS.md`.** The GV media section already carries the runbook (`:961-973`) and
the pre-flight (`:957-959`); neither needs rewriting. What becomes false is any sentence saying
voicemail plays in the browser, or that `GvMedia:Enabled` ships `false`. Grep both and correct them
where they stand, and add one paragraph:

> **Voicemail plays through the audio engine as of `PHN-2`.** `GvMedia:Enabled` ships `true` in
> `src/Radio.API/appsettings.json`, which the deploy overwrites, so it reaches every box. The browser
> no longer fetches or decodes voicemail audio; mute, master volume, balance, ducking and output
> routing (including Cast) all apply to it. A failed play is diagnosed from the snapshot —
> `curl -s http://radio:5000/api/audio/events/current` — where `failureReason` of `MediaUnauthorized`
> means the two `AuthKey` halves have diverged (see the runbook below), `MediaNotFound` most often
> means the GV auth blackout rather than a missing recording, and `MediaAcquisitionFailed` is the one
> that needs the log line rather than the panel — most likely `/opt/radio-console/data/gvmedia`
> unwritable.

**12b. `design/FUTURE-WORK.md`.** Two entries to touch, **nothing to add**:
- **§20** (`EndReason` indistinguishable on the wire) — its gotcha says *"do not add it
  speculatively… if the chip needs to distinguish."* **The chip does not**, for the reason ADR §12
  item 4 gives: it returns to an idle, replayable state for all four end causes. Record that PR 6 was
  asked and declined, so the condition is answered rather than left open a third time.
- **§23** (the blocker's identity for a `Waiting` playback) — `PHN-1f` §0.6 item 3 said *"if the chip
  wants it, ask."* **It does not ask**: *"Waiting for the announcement to finish…"* is accurate
  without it, because one-voice-at-a-time means there is only one thing it could be waiting for.

**12c. ⚠ [`CROSS-REPO-HANDOFFS.md`](../../docs/queue/CROSS-REPO-HANDOFFS.md) § Cross-repo handoffs #3 — VERIFY, do not edit (C-73).** `PHN-1b` §5
and `PHN-1c` §5 both told PR 6 to withdraw the audio-endpoint ask. **Read the item first:** at
`ba1ae4a6` that clause is already struck through and marked ✅ *"DISSOLVED, not resolved, by `PHN-1b`
(#534)"*, with the mechanism named (`GvMediaAuthHandler.cs:26`). **If it still reads that way, change
nothing and say so in the PR body.** An obligation already met is discharged by confirming it.

⚠ This plan does **not** edit `docs/BUILDER_QUEUE.md`, `docs/HANDOFF-GA-PUNCH-LIST.md` or
`docs/HANDOFF-NEXT-SESSION.md`; §6 proposes rows for the owner and a Builder to apply.

---

### Task 13 — build, test, and the scope gate

```bash
dotnet build --configuration Release
dotnet test  --configuration Release --verbosity normal
dotnet test  --configuration Release --filter "FullyQualifiedName~VoicemailPlayerTests"
dotnet test  --configuration Release --filter "FullyQualifiedName~ConsolePlaybackState"
dotnet test  --configuration Release --filter "FullyQualifiedName~EventPlaybackServiceTests"
```

**Eight greps. Each fails the PR if its stated expectation does not hold.**

```bash
# 1. ⛔ THE ROW. No <audio> element anywhere in the phone surface.
grep -rn "<audio" src/Radio.Web/Components/
# → nothing

# 2. ⛔ The URL builder and its caller go together (PHN-1b §5, PHN-1c §5).
grep -rn "GetVoicemailAudioUrl" src/ tests/
# → nothing

# 3. ⛔ C-77. VoicemailPlayer must not stop playback on disposal. Read the Dispose body.
sed -n '/public void Dispose/,/$/p' src/Radio.Web/Components/Pages/VoicemailPlayer.razor | head -3
# → one expression body: ConsolePlayback.Changed -= OnPlaybackChangedAsync;  and no API call

# 4. ⛔ §0.6. ConsolePlaybackState is the ONLY subscriber to the store's event.
grep -rn "EventPlaybackChanged +=" src/Radio.Web/
# → exactly two hits: AudioStateStore's own hub wiring, and ConsolePlaybackState.cs. Nothing else.

# 5. ⛔ §0.6 property 2. ConsolePlaybackState caches no snapshot of its own.
grep -n "EventPlaybackSnapshotDto" src/Radio.Web/Services/ConsolePlaybackState.cs
# → the read-through property only. NO field, no assignment.

# 6. ⛔ C-78 and trap 5. No new store subscription, no poll, no tick, no timer in this diff.
grep -rn "VolumeChanged +=\|PlaybackStateChanged +=\|PeriodicTimer\|new Timer\|Task.Delay" \
  src/Radio.Web/Components/Pages/VoicemailPlayer.razor \
  src/Radio.Web/Services/ConsolePlaybackState.cs
# → nothing

# 7. ⛔ §0.5 items 1 and 2. No Feature B and no Feature C in this row.
git diff --name-only main... | grep -E "MessageBubble|PhoneTextsPanel|GvSpeechText"
# → nothing

# 8. ⛔ The three documents another cycle owns.
git diff --name-only main... | grep -E "BUILDER_QUEUE|HANDOFF-GA-PUNCH-LIST|HANDOFF-NEXT-SESSION"
# → nothing
```

**The full expected file list. Anything else is scope creep:**

```
src/Radio.API/appsettings.json
src/Radio.Web/Program.cs
src/Radio.Web/Services/ConsolePlaybackState.cs                        (new)
src/Radio.Web/Services/AudioStateStore.cs                             (comments only)
src/Radio.Web/Services/ApiClients/EventPlaybackApiService.cs
src/Radio.Web/Services/ApiClients/GvBridgeApiService.cs                (deletion)
src/Radio.Web/Components/Layout/MainLayout.razor
src/Radio.Web/Components/Pages/VoicemailPlayer.razor
src/Radio.Web/Models/ApiModels.cs                                     (one comment)
src/Radio.Web/wwwroot/css/design-system.css
src/Radio.Web/wwwroot/js/voicemail-player.js                          (reduced)
src/Radio.Infrastructure/Audio/Sources/Events/TTSEventSource.cs
tests/Radio.Web.Tests/Components/VoicemailPlayerTests.cs
tests/Radio.Web.Tests/Components/Layout/ConsolePlaybackChipTests.cs   (new)
tests/Radio.Web.Tests/Services/ConsolePlaybackStateTests.cs           (new)
tests/Radio.Infrastructure.Tests/Audio/Events/EventPlaybackServiceTests.cs
design/INTEGRATIONS.md
design/FUTURE-WORK.md
```

⚠ **Run `dotnet run --project src/Radio.API` once, locally, and confirm it reaches "Now listening
on".** `CustomWebApplicationFactory` removes **every** `IHostedService`, so no API test in this repo
proves the container still builds under systemd's conditions — `PHN-1b` §2.2 item 1 and `PHN-1c` §2.2
item 1 both required this, and it is required again because Task 1 flips the flag
`GvMediaStartupCheck` branches on. **Say in the PR whether the boot warning fired**: with
`Enabled=true` and an empty `AuthKey`, `GvMediaServiceExtensions.cs:100-115` emits exactly one Warning,
and which of its two branches fires depends on whether `RotaryPhone:Gv:AuthKey` is set locally. It is
the only runtime output Task 1 produces.

## 2. Test Plan

### 2.1 What the automated tests actually prove — and the mutation that reds each one

⚠ **Five consecutive cycles in this arc have found a test that passed against a deliberately broken
implementation.** Every pin below therefore names the change that makes it fail. Where a test cannot
falsify what it appears to cover, that is **stated in the table rather than implied away**.

| Pin | Task | Falsifying mutation | Honest limit |
|---|---|---|---|
| The player renders **no** `<audio>` element | 11 | restore the `<audio src>` line | Proves the element is gone. Proves **nothing** about whether audio comes out of the engine — that is `SOUND`, §3 U1. |
| `ConsolePlaybackState` subscribes to the store **once** | 9 | subscribe twice in the constructor | Does not prove no *other* component subscribes; that is grep 4. |
| A **synchronously** throwing subscriber does not starve the next | 9 | swap the `GetInvocationList()` loop for `await Changed.Invoke()` | This is §0.6's central claim, and it is the one thing in this row that would otherwise be an assertion. |
| A terminal snapshot is **retained** | 9 | `Snapshot => IsLive ? … : null` | ⛔ **Does NOT falsify the read-through.** A private cached field leaves it green. Grep 5 is the only enforcement. |
| An unknown `Kind` degrades to `Playing` | 9 | return the raw `Kind` from the `_` arm | — |
| `Waiting` says why, offers Stop, and does **not** run the bar | 11 | drop `State == "Playing"` from `ElapsedSeconds`; or narrow `IsMineAndLive` | Renders `PHN-1f` §0.6's three requirements. |
| An unknown duration is indeterminate, never `0:00` | 11 | read `Item.DurationSeconds` unconditionally | — |
| A snapshot for a **different** playback does not drive this row | 11 | remove the `Mine` id gate | The single-selection-group consequence of global state (§A4b). |
| `MediaUnauthorized` and `MediaNotFound` get **different** copy | 11 | collapse either into the other's arm | The pin that keeps §3 P3 diagnosable from the panel. |
| The chip is a `<button>`, sibling of `.topbar-nav`, absent at rest | 11 | render unconditionally; or move it into the `/phone` pill | — |
| A speech snapshot reports a **moving** position | 10 | revert the `TTSEventSource.Position` override | Updated from `PHN-1c`'s inverse pin, not deleted. |

### 2.2 ⭐ THE ARC'S DEFERRED VERIFICATION DEBT — the full inventory, finally in one place

**This is the section the punch-list `PHN-2` row pinned here.** `PHN-1a`, `PHN-1b`, `PHN-1c`, `PHN-1d`,
`PHN-1e` and `PHN-1f` each deferred device-only checks to *"PR 6"*, which had no plan file until this
one. **Fourteen items, sourced, each labelled `SOUND` (a human confirmed the room changed) or `PATH`
(the code path ran).**

⚠ **The label is not bookkeeping.** `AudioFileEventSource.PlayCoreAsync:175-184` selects
`PlaybackLoopAsync` (`:261-271`) whenever `_playbackService` is null — a **simulation** that waits
`_duration`, reports `PlaybackCompletionReason.EndOfContent`, and produces **no audio at all**, logging
its choice at **Debug** (`:181`), which since `LOG-11` does not reach the journal. `AudioFileEventSourceTests`
treats that null configuration as an expected state and asserts nothing about sound. **So a green "it
completed" is the least trustworthy evidence available on this row**, and no `PATH` check may ever be
reported as evidence for a `SOUND` claim.

| # | Deferred check | Source | Kind | Where it lands |
|---|---|---|---|---|
| 1 | `SoundPlayerBase.Seek` actually **repositions** a short local MP3 | `PHN-1a` §2.2 (1), ADR §14 Q3 | **SOUND** | §3 U5 |
| 2 | `SoundPlayerBase.Time` **advances** during playback | `PHN-1a` §2.2 (2) | **SOUND** | §3 U6 |
| 3 | Pausing a TTS source **no longer reports completion** | `PHN-1a` §2.2 (3), C-6 | `PATH` | §2.2 note ⓐ |
| 4 | **`./data/gvmedia` is writable under the service account** | `PHN-1b` §2.2 (4), re-carried by `PHN-1c` §2.2 (4) | `PATH` | §3 P6 / Part 4 step 5 |
| 5 | A fetched voicemail is **audible at all** | `PHN-1c` §2.2 (3) | **SOUND** | §3 U1 |
| 6 | It **ducks the radio** | `PHN-1c` §2.2 (3) | **SOUND** | §3 U1 |
| 7 | It **follows mute** | `PHN-1c` §2.2 (3) | **SOUND** | §3 U2 |
| 8 | It **follows master volume** | `PHN-1c` §2.2 (3) | **SOUND** | §3 U3 |
| 9 | With Cast active it **goes to the Cast device** | `PHN-1c` §2.2 (3) | **SOUND** | §3 U4 |
| 10 | A doorbell **preempting** a voicemail sounds right, and ducking **releases cleanly** afterwards | `PHN-1d` §2.2 (1),(2) | **SOUND** + a judgement | §3 U7 |
| 11 | The **wait** is not mistaken for a broken button, and the room never carries **two voices** | `PHN-1d` §2.2 (4), `PHN-1f` §2.2 (3) | **SOUND** | §3 U8 |
| 12 | The broadcast **deserialises through the real `JsonHubProtocol`** (U1) — a `state` arriving as a number rather than `"Playing"` looks like "the chip does not update" | `PHN-1e` §2.2 (1) | `PATH` | §2.2 note ⓑ |
| 13 | The **300 s cap** fires on real hardware; `MaxQueuedWaitSeconds = 30` has never been exercised on the appliance, nor its interaction with the cap | `PHN-1e` §2.2 (2), `PHN-1f` §2.2 (4) | `PATH` | §2.2 note ⓒ |
| 14 | ADR §16.5's **`/sleep` latency** on the idle path — *"plausibly seconds… Unmeasured."* | ADR §16.5 item 2, `PHN-1f` §2.2 (5) | `PATH` | §2.2 note ⓓ |

**And four that arrived here and are already discharged, recorded so they are not re-run:**

- ✅ The gvbridge route's status codes and `Content-Length` — closed by `PHN-1c` C-22 **by reading
  RotaryPhone's source**, not by a live call.
- ✅ What a blackout looks like from here — a `404`, not a `502`. Same source.
- ✅ The cross-repo withdrawal of the unauthenticated-endpoint ask — dissolved by `PHN-1b` (#534);
  Task 12c verifies rather than edits (C-73).
- ✅ *"That a browser refresh does not stop playback"* — **deleted as a check by `PHN-1e`**, because
  owner decision `D30` says a reload **should** stop it. Do not re-add it; it asserts the opposite of
  what ships.

**Notes on the four `PATH` items that do not belong in the owner's fifteen minutes:**

- **ⓐ Item 3 (TTS pause).** The unit tests reach only the not-playing guard; the defect lived in the
  live monitor loop. Feature A does not pause a TTS source anywhere, so an owner-facing step would be
  a `curl` sequence with no audible result. **Builder runs it and reports the output**, using
  `PHN-1c` §2.2's recipe with a `Speech` body; it is not in §3.
- **ⓑ Item 12 (serialisation).** Already exercised implicitly the moment the chip updates at all —
  a `state` arriving as `0` would leave `IsLive` true (deny-list) but `KindLabel` and every state test
  wrong, so §3 U1's *"a cyan `Voicemail` chip appears"* is the observable form. **Builder additionally
  confirms** `grep -c EventPlaybackChanged` on the Web file sink is non-zero after a play.
- **ⓒ Item 13 (the two caps).** Both need a config edit and a wait, and neither is a Feature A
  question. **Builder runs the 300 s cap check** with `MaxPlaybackSeconds` temporarily at 20 per
  `PHN-1e` §2.2 (2) — ⚠ **and puts the value back.** `MaxQueuedWaitSeconds` and its interaction with
  the cap remain **unexercised on the appliance**; §6.1 carries them rather than pretending otherwise.
- **ⓓ Item 14 (`/sleep` latency).** Unmeasured before this row and unmeasured after it. Measuring it
  means starting a voicemail and waiting out a 30-minute idle timer. §6.1 carries it. ⚠ **It is not
  measured by pressing the Sleep pill** — that is the easy half and not the case the rule was written
  for.

### 2.3 What the tests cannot prove, stated plainly

1. ⛔ **No test in this repository produces sound.** Items 1, 2, 5, 6, 7, 8, 9, 10 and 11 above are
   reachable **only** by a person in the room. That is not a gap in this plan; it is why §3 exists and
   why the arc breakdown says *"nothing short of that settles it."*
2. ⛔ **`ConsolePlaybackStateTests` cannot prove the read-through** (§2.1). Grep 5 is the enforcement,
   and a grep is weaker than a test.
3. ⛔ **No test proves the chip is in the right place.** `TheChipIsAButton_AndASiblingOfTheNavPills`
   asserts structure; whether it lands in the empty span at x≈700 on a 1920×720 panel is a look, and
   §3 U1's *"a cyan chip appears in the top bar"* is as close as this row gets.
4. ⛔ **No test covers two browsers.** `Waiting` and every other state is global, and the multi-client
   questions the arc carries (ADR §14 Q12) are untouched here and unproven by this row.
5. ⛔ **An API-level test cannot prove the new transport endpoints wire up at boot.**
   `CustomWebApplicationFactory` removes every `IHostedService`, so it is a weaker container than
   systemd's. Task 13's local `dotnet run` is the substitute, and it is a substitute.
6. ⛔ **Nothing proves the `Preparing` spinner appears on the *next frame*.** Handoff §A5 is emphatic
   that it must never be deferred "to avoid flicker", because the slow case is the one that matters. A
   bUnit test sees markup, not frames. §3 U0 is where a human sees it.

### 2.4 Commands

```bash
dotnet build --configuration Release
dotnet test  --configuration Release --verbosity normal
dotnet test  --configuration Release --filter "FullyQualifiedName~VoicemailPlayerTests"
dotnet test  --configuration Release --filter "FullyQualifiedName~ConsolePlaybackState"
dotnet test  --configuration Release --filter "FullyQualifiedName~ConsolePlaybackChip"
dotnet test  --configuration Release --filter "FullyQualifiedName~EventPlaybackServiceTests"
dotnet run   --project src/Radio.API      # must reach "Now listening on" — Task 13
```

⚠ `TEST-1` shipped (#483), so a green run no longer silently depends on whether `radio-api` happens to
be up. Trust the suite.

---
## 3. Owner UAT — Feature A

> **Read this part alone.** It assumes you have not read the plan. It takes about **fifteen minutes**
> at the cabinet, and it is what decides whether Feature A is done. PR 6 may be merged on review and
> green CI before this runs (owner-authorised); **this is the check that says whether it was right.**
>
> **You will need:** the panel in front of you, a phone or laptop on the LAN with a terminal, at least
> one voicemail in the console's list, and something that can play music (the radio tuned to a station
> is ideal).
>
> ⚠ **Write down the wall-clock time next to every result, pass or fail.** One failure mode on this box
> is on a 20-minute cycle that has nothing to do with you (Part 0, F1). An untimed result is noise.

---

### Part 0 — Two failure modes you could mistake for a defect. Read before you start.

**F1 — the Google Voice blackout. This is the big one.** Google Voice authentication on the bridge is
dead for roughly **9 minutes out of every 20**, on a clock that has nothing to do with the console. In
that window a **first** play of a voicemail fails and the panel says
**`Couldn't load this recording.` / `This usually clears up in a minute. Try again.`**

- **That is not a defect.** It is a known upstream fault (`XR-3`), and the console's copy is telling you
  the truth.
- **You can check the clock before you spend a test.** Run:
  ```bash
  curl -s http://radio:5004/api/gvbridge/status
  ```
  Read **`psidtsAgeSeconds`** and **ignore every other field in that payload** — `available`,
  `cookiesValid` and `degraded` have all been observed reporting healthy during a hard outage.
  - **under 660** → healthy, go ahead
  - **660 – 1200** → blackout, wait; it resets at about 1200
- **A voicemail you have already played once is immune.** The console keeps a copy on disk, so a
  replay works during a blackout. **If you want to avoid F1 entirely, do step U0 first and then use
  that same voicemail for everything else.**

**F2 — things that stop a voicemail on purpose, and look like a crash.** All four are designed
behaviour and none is a defect:

| What you did | What happens | Why |
|---|---|---|
| A doorbell / Home Assistant notification arrived | The voicemail **stops** (it does not pause) and the announcement plays | `D5` — anything at priority 8 or above outranks a voicemail |
| You reloaded the page, or closed the last browser | The voicemail **stops** | Owner decision `D30` — *"if the page reloads mid-voicemail, the audio should fail"* |
| The panel went to the sleep screen (including the 30-minute idle timer) | The voicemail **stops** | ADR-029 §16.5 — a screen with no stop button must not hold the room |
| Nothing, and it ran 5 minutes | The voicemail **stops** | The 300-second hard cap |

**F3 — Cast makes the local speakers go quiet, and that is a PASS.** In step U4 the voicemail is
supposed to come out of the Cast device and **not** the cabinet. Silence from the cabinet in that step
is the success condition, not a failure.

---

### Part 1 — Pre-flight (about 3 minutes). Do not skip P3.

**P1 — Confirm the right build is on the box.**
```bash
curl -s http://radio:5000/api/health/version   # Radio.API
curl -s http://radio:5002/api/health/version   # Radio.Web
```
- **Pass:** both report the **same `gitSha`**, and it is the merge commit of the `PHN-2` PR.
- **Fail:** they differ, or either is older. Re-run the deploy; the UAT is meaningless until they match.

**P2 — Confirm the GV blackout clock is green.** (Part 0, F1.)
```bash
curl -s http://radio:5004/api/gvbridge/status
```
- **Pass:** `psidtsAgeSeconds` under 660. Note the number and the time.
- **Fail:** 660–1200 — **not a defect.** Wait for it to reset past 1200 and start again.

**P3 — ⚠ The auth-key check. This is the step that most often makes a correct build look broken.**

The console now fetches voicemail audio itself, and the bridge can require a shared key. The bridge
ships with that requirement **off**, in which case there is nothing to do. But if it has ever been
turned on, **every** voicemail fails until the two halves of the key match — and nothing warns you at
boot.

```bash
# Does the bridge require a key? Empty / absent means "no", which is the shipped state.
ssh mmack@radio "grep -i interserviceauthkey /opt/rotary-phone/appsettings*.json 2>/dev/null; systemctl show rotary-phone -p Environment | grep -i interservice"
```
- **The bridge requires no key** (nothing found, or an empty value) → **nothing to do. Go to P4.**
- **The bridge requires a key** → the console needs the same string in **two** files, and the deploy
  will not put it there:
  ```bash
  ssh mmack@radio 'sudo nano /opt/radio-console/api/appsettings.Production.json'   # GvMedia:AuthKey
  ssh mmack@radio 'sudo systemctl restart radio-api'
  ssh mmack@radio 'sudo nano /opt/radio-console/web/appsettings.Production.json'   # RotaryPhone:Gv:AuthKey
  ssh mmack@radio 'sudo systemctl restart radio-web'
  ```
  The full runbook is in `design/INTEGRATIONS.md` under *"Runbook: setting `AuthKey` on a live box is a
  hand edit, twice."*
- **How you will know you got this wrong later:** the panel says
  **`The console isn't allowed to fetch recordings.`** — that string means P3, and only P3. It never
  clears up on its own, and retrying will not help.

**P4 — Confirm voicemail-through-the-engine is switched on.** Nothing to do by hand — this PR ships it
on. Confirm it landed:
```bash
ssh mmack@radio 'grep -A1 "\"GvMedia\"" /opt/radio-console/api/appsettings.json | head -3'
```
- **Pass:** `"Enabled": true`.
- **Fail:** `false` — the deploy did not land the new `appsettings.json`. Re-deploy. (If you tap play
  in this state the panel says the console has voicemail playback turned off; that is the honest
  message, not a crash.)

**P5 — Have music playing.** Turn the radio on, pick a station you can hear clearly, and set the
volume where you would normally listen. **Several steps below are "did the music get quieter," so you
need to be able to hear the music first.**

---

### Part 2 — The four checks that settle Feature A. These are mandatory.

> **U0 first, once:** open `/phone`, tap a voicemail row, tap ▶ and let it play a few seconds, then tap
> ■. This warms the cache for that recording so F1 cannot interfere with the rest. **Use this same
> voicemail for U1–U4.**
>
> If U0 itself fails with `Couldn't load this recording.`, that is F1 — note the time, wait five
> minutes, try again. Two failures more than ten minutes apart is worth reporting.

**P6 — right after U0 succeeds, and only then: did the console keep its copy?** This is the check that
has been deferred twice through this arc and this is its last chance; the first real fetch on this box
has just happened.

```bash
ssh mmack@radio 'ls -l /opt/radio-console/data/gvmedia/'
```

- **Pass:** the directory exists, holds **at least one file of non-zero length**, and is owned by
  `mmack`.
- **Fail:** missing, or empty after a successful play. The console cannot write its cache, so every
  play is a fresh network fetch with F1's coin-flip odds and no replay is ever safe.
  ⚠ **This failure does not say so on the panel** — it arrives as *"The console can't play this right
  now."*, which is the same message as an audio-engine refusal. The log line is the diagnosis, not the
  screen (Part 4 step 4).

---

**U1 — Ducking. Does the radio get out of the way?**

*Do:* With the radio playing (P5), open `/phone`, tap the voicemail row, tap **▶**.

*Pass:*
- The music **drops in volume** within a moment of the voicemail starting — clearly quieter, not
  silent.
- You can hear **the voicemail over the top of it**, and you can make out what it says.
- When the voicemail finishes on its own, the music **comes back up to where it was**.
- A cyan **`Voicemail`** chip appears in the top bar while it plays and disappears when it ends.

*Fail:*
- **Both play at full volume and you cannot make out the voicemail.** ← This is the bug the row exists
  to fix. If you see this, the row failed. Capture per Part 4.
- The music ducks but **never comes back up** after the voicemail ends.
- You hear the voicemail but the music does not change at all.

---

**U2 — Mute. Does mute silence the voicemail too?**

*Do:* Start the voicemail again. While it is **playing**, press **mute** on the console (the topbar
mute control, or the volume knob's press if that is how you mute). Wait three seconds. Un-mute.

*Pass:*
- **The room goes completely silent** — both the ducked music and the voicemail. Not "quieter": silent.
- The chip **stays** in the top bar (the voicemail is still running, it is just inaudible).
- On un-mute, the voicemail is **still going**, further along than where it was.

*Fail:*
- **The music goes silent but you can still hear the voicemail.** ← This is the bug. Capture.
- Un-mute brings the music back but not the voicemail.

---

**U3 — Master volume. Does the voicemail follow the knob?**

*Do:* Start the voicemail again. While it is playing, turn the **VOLUME** knob (or move the master
volume slider) down several steps, then back up.

*Pass:*
- **The voicemail gets quieter and louder with the knob**, together with the music underneath it. The
  two move as one thing.

*Fail:*
- The music level changes and **the voicemail stays where it was.** ← This is the bug. Capture.
- The voicemail level changes in the wrong direction, or jumps.

---

**U4 — Cast routing. Does the voicemail go where the music goes?**

> Remember F3: the cabinet going quiet in this step is the **pass**.

*Do:*
1. From the top bar, connect the console to a Cast device (a speaker or a TV you can hear).
2. Confirm the **music** is now coming out of the Cast device and not the cabinet.
3. Open `/phone`, tap the voicemail, tap **▶**.

*Pass:*
- The voicemail comes out of the **Cast device**.
- The **cabinet speakers stay silent.**
- The music on the Cast device ducks under it, as in U1.

*Fail:*
- **The voicemail comes out of the cabinet speakers while the music is on the Cast device.** ← This is
  the bug — the voicemail is bypassing output routing. Capture.
- The voicemail is not audible **anywhere**. (Check the Cast device volume first; if the music is
  audible there and the voicemail is not, that is a real failure.)

*Then:* disconnect Cast before Part 3.

---

### Part 3 — Four more, if you have time. Not required to call Feature A done.

**U5 — Does seeking actually move the audio?**
*Do:* Start a voicemail at least 20 seconds long. Tap the progress bar about three-quarters of the way
along.
*Pass:* the audio **jumps** — you hear a different part of the message, within about a tenth of a
second. The bar and the elapsed time move to where you tapped.
*Fail:* the bar moves but **the audio carries on from where it was**, or the audio restarts from the
beginning. *(Both are known possibilities — if the audio restarts from the tapped offset rather than
seeking cleanly, that is an accepted fallback, not a failure. Note which one you got.)*

**U6 — Does the elapsed time advance?**
*Do:* Watch the `0:00 / 0:42` readout while a voicemail plays.
*Pass:* the left-hand number counts up smoothly and the bar tracks it.
*Fail:* it sits at `0:00` for the whole message. *(The message still plays; this is an accuracy fault,
not a silence fault. Report it, it does not block.)*

**U7 — Does a doorbell stop a voicemail, and does that feel right?**
*Do:* Start a voicemail. While it is playing, from your laptop:
```bash
curl -s -X POST http://radio:5000/api/notifications/announce \
  -H 'Content-Type: application/json' \
  -d '{"Message":"Someone is at the door"}'
```
*Pass:* the voicemail **stops** (does not pause), the announcement is **fully intelligible**, the
transport returns to a clean playable state, no error appears, and the music comes back to full
volume afterwards.
*Fail:* the announcement and the voicemail **talk over each other**; or the voicemail stops and the
music never comes back up; or an error appears.
⚠ **This is also a judgement call and your opinion is the deliverable.** The design says a doorbell
should stop a voicemail. Nobody has ever heard it. **If it feels wrong in the room, say so** — it is
one config key to change and this is the moment to find out.

**U8 — Does the console wait its turn?**
*Do:* Send the same announcement as U7, and **immediately** — while it is still speaking — tap ▶ on a
voicemail.
*Pass:* the chip in the top bar appears and says the console is **waiting**; nothing is audible from
the voicemail; the announcement finishes; **then** the voicemail starts. **At no point do you hear
both.**
*Fail:* both play at once; or the voicemail never starts and the panel just sits there for more than
about thirty seconds with no message; or you get an error immediately.

---

### Part 4 — On a fail: what to capture, before anything else

**Do this within a couple of minutes of the failure.** The log rolls and the blackout clock moves.

1. **Write down the wall-clock time** (and say whether it is local or UTC) and **which step number**
   failed.
2. **Ask the console what it thinks happened** — this is the single most useful line:
   ```bash
   curl -s http://radio:5000/api/audio/events/current
   ```
   Copy the whole reply. The two fields that matter are **`state`** and **`failureReason`**.
   - `failureReason` starting `MediaNotFound` / `MediaUpstream` / `MediaTimeout` → almost certainly
     **F1, the blackout.** Check `psidtsAgeSeconds` and retry in five minutes.
   - `MediaUnauthorized` → **P3.** The keys do not match. Not a code defect.
   - `MediaAcquisitionFailed` → the console could not fetch **or could not write its cache**. Go to
     step 4 — the log line is the diagnosis, the panel's message is not.
   - `WaitExpired` → the console waited thirty seconds for something else to stop talking and gave up.
3. **Check the blackout clock again** (Part 0, F1) and write the number down.
4. **Grab the log.** Since `LOG-11` the detail is in the **file** sink, not the journal:
   ```bash
   ssh mmack@radio 'F=$(ls -t /opt/radio-console/logs/radio-*.txt | head -1); tail -200 $F'
   ```
   and the warnings and errors, which do reach the journal:
   ```bash
   ssh mmack@radio "journalctl -u radio-api --since '-15min' --no-pager | tail -60"
   ```
   ⚠ Keep these bounded as written. Long journal reads on this box are themselves a cause of audio
   distortion.
5. **If it was a cache or fetch failure**, check the cache actually works (this is `PHN-1b`'s
   twice-deferred check, and this UAT is where it finally lands):
   ```bash
   ssh mmack@radio 'ls -l /opt/radio-console/data/gvmedia/'
   ```
   - **Pass:** the directory exists **and holds at least one file of non-zero length** after a
     successful play.
   - **Fail:** missing, empty after a successful play, or not owned by `mmack` — the console cannot
     write its cache, and every play will be a fresh network fetch with F1's odds.
6. **Photograph the panel** if anything on screen looks wrong. Screen capture on this box is awkward;
   a phone photo is faster and good enough.
7. **If the chip is missing while `/api/audio/events/current` says something is playing**, restart the
   web service before concluding anything — there is a known one-shot in the state seed that can leave
   the panel blind until `radio-web` restarts (`FUTURE-WORK.md` §21):
   ```bash
   ssh mmack@radio 'sudo systemctl restart radio-web'
   ```

**One thing not to do:** do not conclude anything from a single failure inside a blackout window. F1 is
a coin flip on a first play, by design of somebody else's system, and the console is reporting it
honestly.

## 4. Self-review

- **Placeholder scan.** No `TBD`, no *"similar to Task N"*, no *"implement later"*. Every task carries
  literal code or a literal edit with a file and a line anchor. **Five places say "check first", and
  each names the verification and its consequence** rather than leaving a gap: the
  `AudioApiService.SetMuteAsync` / `AudioStateStore.PlaybackState` member names (Task 6a),
  `TTSEventSource`'s playback-service field (Task 10), whether `MainLayout` can be rendered under
  bUnit (Task 11), how `voicemail-player.js` is reached from the page (Task 7a), and whether
  `AudioStateStore` can be constructed offline (Task 9). Each states what to do if the answer is no,
  including "drop the button and say so" and "drop the test and say so."
- **Spec coverage.** ADR-029 **Feature A** ✓ (Tasks 1, 6, 7); **D6** state is a view of a server
  snapshot ✓ (Tasks 2, 4, 6a's `Mine` gate); **D7 §7.2** disposal does not stop playback ✓ (C-77,
  Task 6a, grep 3); **§8.1** the chip is correct on first paint ✓ (Task 4, via `PHN-1e`'s seed);
  **§8.2** anchor interpolation, no tick ✓ (Task 6a `ElapsedSeconds`, grep 6); **§8.3 / D4** tap-to-seek
  ✓ (Task 6a); **§12 item 1** unknown duration is indeterminate ✓; **item 2** `Preparing` has two
  latencies ✓; **item 3** the player owns its own error state ✓ (Task 6b); **item 4** preemption
  returns to idle ✓ (no `EndReason`, §5 item 4). Handoff **§Cross-2** the dock is untouched ✓ (§0.5
  item 9); **§Cross-3** the chip ✓; **§Cross-5** four rows plus one ✓ (C-71); **§A2/§A3/§A5/§A6** ✓;
  **§Gaps G-1/G-2/G-3** ✓ (Task 5), **G-4/G-5** deliberately untouched ✓. `PHN-1f` **§0.6's five
  requirements for the chip** ✓ (Task 6a's `Waiting` branch, `IsLive`, no bar, `WaitExpired` copy,
  retained terminal).
- **Type consistency.** `Snapshot` is `EventPlaybackSnapshotDto?` everywhere, matching `AudioStateStore.EventPlayback`.
  `Changed` is `Func<Task>?`, matching every store event. `SeekAsync` takes `TimeSpan` on the client and
  serialises `positionSeconds` as a `double`, matching `EventPlaybackSeekDto.PositionSeconds`.
  `durationSeconds` is `int`, matching `VoicemailItemDto.DurationSeconds` and
  `EventPlaybackRequestDto.DurationSeconds`. `KindLabel` and every state test compare **strings**,
  because C-47 made the wire carry strings so a newer value lands without a lockstep Web deploy.
- **Scope.** Thirteen tasks, eighteen files, no Feature B, no Feature C, no `UI-6` fix, no fourth stop
  condition. Task 13's eight greps fail the PR on eight specific ways of exceeding it.
- **Comment accuracy.** Three corrections are owed and each is a **task**, not a note: Task 3a
  (`EventPlaybackApiService`'s "two methods, deliberately"), Task 7b (`AudioUrl`'s "rebuild absolute"),
  Task 8 (`AudioStateStore`'s "nothing subscribes"), plus Task 5c's `.vm-scrubber` hit-area comment.
  **This is the thirteenth through sixteenth instance of the `CLAUDE.md` § Pre-Merge Review class in
  this arc**; the discipline is making the correction in the PR that falsifies the comment.
- **Test timing.** Nothing in this plan races a wall clock. `ElapsedSeconds` reads `DateTimeOffset.UtcNow`
  in production, and every component test asserts on a snapshot the test itself supplied — with a
  `BroadcastAtUtc` the test controls — rather than on elapsed real time. ⚠ **The one place a Builder
  could get this wrong** is a test that asserts the bar has advanced by sleeping; do not. Set
  `BroadcastAtUtc` in the past and assert the computed width.
- **Where I could not verify.** §2.3 and §6.1, and the five "check first" items above.

---

## 5. What this plan deliberately does not do, and why

1. **Does not fix `UI-6`.** §0.6 avoids adding to the defect and does not repair `NotifyAsync` or the
   two hand-rolled sites. `UI-6` is a queued P2 with its own branch. §6.3 records the alternative
   ordering and why it was not taken.
2. **Does not implement the `Seeking` dim** (handoff §A3). ~30–85 ms on a hub-driven re-render makes it
   sub-frame in the common case. §6.2 raises it with the Designer rather than dropping it silently.
3. **Does not add drag-scrub.** Handoff §A3 defers it for design reasons and records commit-on-release
   as the only acceptable implementation if it ever lands.
4. **Does not add `EndReason` to the wire.** `FUTURE-WORK.md` §20's condition is *"if the chip needs to
   distinguish"* and it does not — ADR §12 item 4 returns the UI to idle for all four end causes.
   Task 12b answers the condition rather than leaving it open a third time.
5. **Does not put the blocker's identity on the wire for a `Waiting` playback.** `PHN-1f` §0.6 item 3
   set the condition *"if the chip wants it, ask"*; the copy is accurate without it, because
   one-voice-at-a-time means there is exactly one thing it could be waiting for.
6. **Does not amend ADR-029 §16.5**, whose table overstates its case (C-80). Third cycle to flag it;
   §6.1 carries it to an Architect.
7. **Does not fix the deploy's Production-config seed** (C-81), which can silently overwrite
   `RotaryPhone:Gv:AuthKey`. Different mechanism, different verification, and folding a
   secret-destroying edit into the last open P0 is the wrong trade. §6.3 proposes the row.
8. **Does not flip `GvMedia:AuthKey` anywhere.** It stays `""` in the tracked file permanently; setting
   it is an appliance operation with a runbook (§3 P3).
9. **Does not touch `NowPlayingDock`.** Handoff §Cross-2: *"an instruction, not an omission."*
10. **Does not add a position tick, a poll, or a per-client timer.** ADR §1.3 disqualifies all three on
    an N100 where churn is audible; grep 6 enforces it.

---

## 6. Carried forward

### 6.1 To an Architect, and to whoever next touches the appliance

1. **ADR-029 §16.5's table overstates its case, and this is the third flag.** `:1281-1284` claims the
   `SetSleepScreenVisible(true)` edge *"covers rows 1, 2, 3, 5"* of §16.4's entry-point table. That is
   **true of producing the fact and false of stopping the playback**: row 2 is a hard navigation the
   circuit rule already stops; the report lands *"plausibly seconds"* late on a brand-new circuit
   (§16.5 item 2); the setter is `void` on a synchronous action while the stop is `async` (item 1); the
   flag is a **global last-writer-wins bool** (item 3, ADR §14 Q12); and edge semantics make a second
   client arriving at `/sleep` a no-op. `PHN-1e`'s Builder flagged it, `PHN-1f`'s planner flagged it,
   and this row flags it. ⛔ **Do not edit a merged ADR from a plan.** Nothing in this row's scope
   depends on the resolution.
2. **Two things remain unexercised on the appliance after this row, and neither is Feature A's:**
   `GvMedia:MaxQueuedWaitSeconds = 30`, and its interaction with the 300 s cap (the cap is armed
   *after* `PlayAsync`, so a wait does not consume it — **asserted nowhere**). `PHN-1f` §2.2 item 4.
3. **ADR §16.5's `/sleep` latency on the idle path is still unmeasured**, and a `Waiting` playback now
   sits inside exactly that window. Measuring it costs a 30-minute wait; pressing the Sleep pill is the
   easy half and not the case the rule was written for.
4. **`FUTURE-WORK.md` §21 item 2 is a live operational hazard for this row's surface.** The
   attended-playback seed burns its one-shot flag *before* the HTTP call, so a single throw leaves the
   store unseeded for the life of the process and the chip blind. §3 Part 4 step 7 tells the owner to
   restart `radio-web`; that is a workaround, not a fix.

### 6.2 To the Designer

1. **One new string, and it extends §Cross-5's table rather than deviating from it (C-71).**
   `The console isn't allowed to fetch recordings.` / *"This won't clear on its own — the console's key
   doesn't match the bridge's."* It exists because `MediaUnauthorized` is a fifth distinct failure with
   a fifth distinct fix, and giving it the blackout's *"This usually clears up in a minute"* would send
   the owner into a retry loop against a configuration fault. **Confirm the wording.**
2. **The `Seeking` dim (§A3) is not implemented** — §5 item 2. If it is wanted, it is a two-line
   addition and the right shape is a `_seekingUntil` timestamp compared in the render, not a timer.
3. **Q11 is answered in the direction the handoff recommended:** `fast_rewind` / `fast_forward`, with
   the real interval in the `aria-label`. Flagged because it was the owner's pick to make and the plan
   took the recommendation.
4. **Q9 (sleep) is now decided above the design.** ADR §16.5 stops attended playback on both sleep
   edges including the idle timer. If the sleep surface ever grows its own stop control the rule can be
   revisited; that is the sleep arc's call.

### 6.3 To the owner — two orderings and one acceptance

1. **`UI-6` could be ordered before this row, and this plan does not.** §0.6 keeps
   `AudioStateStore.EventPlaybackChanged` at exactly one subscriber for the life of the process, which
   makes the punch list's tiering premise literally true rather than approximately true. Ordering
   `UI-6` first (0.5 d) would make the decision moot rather than wrong — `ConsolePlaybackState` is
   still worth building for its read-through property. **Not taken**, because it delays the last open
   P0 for a defect whose worst observed consequence is a lost log line.
2. **A deploy defect this row found and did not fix.** `Deploy-ToLinux.ps1:222` guards the
   Production-config seed on `api/`'s file while `:226` writes **both** directories, so a box missing
   the api overlay has its **web overlay silently overwritten** — which is where
   `RotaryPhone:Gv:AuthKey` lives. Dormant on `radio` today; live the first time anyone provisions a
   second box. Proposed as a row in §7.2.
3. **An acceptance, restated because this row is where it becomes audible.** With
   `PhoneIntegration:Enabled` false, the only thing on this box that can preempt attended playback is
   a notification posted to `/api/notifications/announce` at its default priority 8 — so **a doorbell
   will stop a voicemail mid-play.** That is ADR §6.1's intent, it has never been heard, and §3 U7 is
   where you hear it. ⚠ **If it feels wrong in the room, say so during the UAT** — `GvMedia:PreemptAtPriority`
   is one key, but read `PHN-1d` C-43 first: raising it disables the feature while leaving it looking
   intact, so the honest way to turn it off is to say so.

---

## 7. Proposed `BUILDER_QUEUE` rows

⚠ **Not applied by this plan.** `docs/BUILDER_QUEUE.md`, `docs/HANDOFF-GA-PUNCH-LIST.md` and
`docs/HANDOFF-NEXT-SESSION.md` were out of scope for this pass. The rows below are **proposals**, in
the schema that file's § Queue already uses:
`| # | Item | Status | Plan | Spec / handoff | Depends on | Branch |`.

### 7.1 `PHN-2` — a NEW row; there is no `PHN-2` row in the queue today

Verified at `ba1ae4a6`: the § Queue table runs `GV-1` … `UI-6` and contains `PHN-1a` … `PHN-1f` but
**no `PHN-2`**. This appends one.

| Field | Value |
|---|---|
| **#** | `PHN-2` |
| **Status** | `📋` |
| **Plan** | `` [`design/plans/PHN-2-retire-the-audio-element.md`](../design/plans/PHN-2-retire-the-audio-element.md) `` |
| **Spec / handoff** | `` [ADR-029 Feature A](../design/decisions/2026-08-03-gv-audio-through-engine.md) · [handoff §Cross-1…5, §A](design-handoffs/HANDOFF-phone-console-audio-and-canned-replies.md) · [arc breakdown row 6](../design/plans/PHN-arc-pr-breakdown.md) `` |
| **Depends on** | `` ✅ **MET — `PHN-1f` merged 2026-09-04** as [#564](https://github.com/mmackelprang/RTest/pull/564), merge commit `ba1ae4a6`. **This row is CLAIMABLE NOW**, and it is the LAST OPEN P0. `` |
| **Branch** | `feat/phn-2-retire-audio-element` |

**Item cell:**

> **ADR-029 PR 6 — retire the `<audio>` element (Feature A).** 🔴 **P0, `O6`, sixth of the eight-PR
> phone arc, the LAST OPEN P0 on the GA punch list, and the first PR of this arc a user can hear.**
> `VoicemailPlayer.razor:8` is an HTML5 `<audio>` pointed straight at the GV bridge, so the **browser**
> fetches and decodes voicemail — bypassing mute, master volume, balance, ducking and Cast routing.
> Owner decision **`D17`** establishes this is **live behaviour today, not a latent risk**: press play
> while the radio is on and two sounds run in the room at full level each. This row spends the seam
> PRs 1–5b built and shipped dark: the element, `wwwroot/js/voicemail-player.js`'s player half and
> `GvBridgeApiService.GetVoicemailAudioUrl` go **together**; the transport becomes a view of the server
> snapshot driven through `POST /api/audio/events`; a **console-playback chip** lands in
> `.topbar-primary` (handoff §Cross-3) as the global stop control that earns *"playback survives
> navigation"*; **`GvMedia:Enabled` flips `true`** in `src/Radio.API/appsettings.json`, which the
> deploy overwrites and therefore reaches every box. Also renders `PHN-1f`'s `Waiting` state — **`D28`'s
> queue is live on the appliance today and nothing shows it** — and adds the `TTSEventSource.Position`
> override `PHN-1e` §5.2 assigned here.
> ⚠ **PRE-FLIGHT BEFORE FLIPPING `GvMedia:Enabled` ON THE BOX, and it is a numbered step, not a
> footnote:** check whether RotaryPhone's `InterServiceAuthKey` is set **on the appliance** — the repo
> cannot tell you, `GVBridgeConfig.cs:81-82` instructs storing it outside source. Its gate ships
> default-off, but if it has been set, **every** fetch returns 401 → `MediaUnauthorized` until
> `GvMedia:AuthKey` matches, across **two** `appsettings.Production.json` files the deploy does not
> re-seed. Runbook: `design/INTEGRATIONS.md:961-973`.
> ⚠ **THIS ROW CARRIES THE PHONE ARC'S ENTIRE VERIFICATION DEBT — fourteen deferred items, inventoried
> in plan §2.2** and labelled `SOUND` or `PATH`, because `AudioFileEventSource.PlayCoreAsync:175-184`
> selects a **silent simulation** (`PlaybackLoopAsync:261-271`) when no playback service is present and
> reports a clean completion having produced no audio. **A green "it completed" is the least
> trustworthy evidence available here.**
> ⚠ **A `MediaNotFound` during UAT is as likely to be the GV auth blackout as a bad id** — six distinct
> transient conditions reach RotaryPhone's 404 because `FindNodeAsync` never checks its list's
> `Succeeded` flag. Record the wall-clock time of every failure and retry after five minutes before
> concluding anything.
> ⚠ **`UI-6` does not block this row and the reason is NOT the punch list's.** Plan §0.6 introduces a
> `ConsolePlaybackState` singleton so `AudioStateStore.EventPlaybackChanged` keeps **exactly one**
> subscriber for the life of the process; the obvious two-component design would have taken it to two
> per circuit. **Est. 3–4 d.**

### 7.2 `OPS-7` — proposed new row (C-81)

| Field | Value |
|---|---|
| **#** | `OPS-7` |
| **Status** | `📋` |
| **Plan** | *to be written — small enough for a plan-in-the-row if the owner prefers* |
| **Spec / handoff** | `` [`PHN-2` plan §0.4 C-81](../design/plans/PHN-2-retire-the-audio-element.md) `` |
| **Depends on** | `—` |
| **Branch** | `fix/deploy-production-config-seed-guard` |

**Item cell:**

> **The deploy's Production-config seed can silently destroy `RotaryPhone:Gv:AuthKey`.** 🟡 **P2 —
> dormant on `radio`, live on the next box provisioned.** `Deploy-ToLinux.ps1:222` guards the seed on
> **`api/`**'s file only (`test -f $TargetPath/api/appsettings.Production.json`), while `:226` copies
> the seed into **both** `api/` and `web/`. A box whose api overlay is absent while its web overlay is
> present therefore has the web file overwritten with the api seed — and the web file is where
> `RotaryPhone:Gv:AuthKey` lives. **Fix:** guard each destination independently, or seed only the
> directory whose file is missing. ⚠ **Found by `PHN-2`'s planning pass and deliberately NOT folded
> into it** — different mechanism, different verification (a second box or a deliberately-removed
> file, not a listening test), and a change that can silently destroy a secret does not belong behind a
> UAT about whether a voicemail ducks the radio. **Est. 0.5 d.**

### 7.3 Two ordering notes for § Dependency / ordering notes

- **`PHN-2` is the last row of the phone arc's Feature A, and the last open P0.** `PHN-3` (Feature B,
  the SMS speak button) and PR 7 (Feature C, canned replies) both follow it and neither is blocked by
  it beyond `O6`. `PHN-2` does **not** build either; plan §0.5 items 1 and 2 and Task 13 grep 7 keep
  them out.
- **`UI-6` is not a blocker for `PHN-2`, and the punch list's stated reason is not the operative one.**
  The punch list argues *"PR 6 takes `EventPlaybackChanged` to one subscriber, below the N ≥ 2 a
  multicast defect needs."* The obvious implementation of PR 6 takes it to **two per circuit** — the
  chip and the open player both need the snapshot. Plan §0.6 chooses a shape that keeps it at one for
  the life of the process, so the premise holds **because the plan made it hold**, not by accident.
  ⚠ **If a future row adds a second subscriber to that event, this argument expires with it.**

# PLAN — `ENC-8` · The encoder Settings surface, and the stale docs behind it

> **Status:** ready for Builder. Written 2026-09-02 against `194b16b`.
> **Punch list:** [`docs/HANDOFF-GA-PUNCH-LIST.md`](../../docs/HANDOFF-GA-PUNCH-LIST.md) §3.5 `ENC-8` (P0).
> **Design:** [`HANDOFF-rotary-encoder-mapping.md`](../../docs/design-handoffs/HANDOFF-rotary-encoder-mapping.md)
> Rev 3 §7.2, §7.5–§7.8, §9.1.
> **Depends on:** `ENC-1`, `ENC-2`, `ENC-11` — all shipped (O10). **Pairs with:** `ENC-12`, which
> ships second and consumes this row's contract.
> **Follows the handoff.** Eleven contradictions between the punch list, the handoff and the shipped
> code are declared and resolved in §0.4; nothing else departs from Rev 3.

---

## 0. Read this before Task 1

### 0.1 What this row is, in one paragraph

`ENC-11` shipped a push/verify loop that already classifies every configuration outcome into four
tiers and **already tightens the host's volume clamp from 6 to 2 units per event when a safety field
does not read back** (`RotaryEncoderConfigVerifier.VolumeClampFor`). All of that works and is
verified on hardware. **None of it is visible to the owner.** The mismatch list is computed, written
to one log line, and dropped on the floor; there is no timestamp, no retained read-back, no way to
re-apply without unplugging the USB lead, and no way to write flash at all. This row builds the page
that renders that state, and the small provisioning surface underneath it that makes the page's
actions possible. It is the *diagnosable* half of the safety response; `ENC-12` is the *findable*
half.

### 0.2 The shared-card decision — **`ENC-8` owns the entire Settings page**

`ENC-8`'s card 1 (*"Status — connection, config tier, last verified, last saved-to-device with a
staleness comparison"*) and `ENC-12`'s third surface (*"the status card carries the field-level
`Sent` vs `Read back` table plus retry / restore actions"*) are **the same surface described twice**,
from two rows written at different times. Planned apart they would be built twice.

> **Decision: `ENC-8` owns every pixel under System Config → Integrations → Rotary Encoders — all
> four cards, the field-level comparison table, and every action button. `ENC-12` adds no markup to
> this page and no endpoint to this controller.**

The split that follows from it:

| | `ENC-8` (this row, ships first) | `ENC-12` (ships second) |
|---|---|---|
| **Owns** | The page. The provisioning contract (`IRotaryEncoderProvisioning`). Retained sent/read-back state and timestamps. The four commands. The REST endpoints. | The **cross-route** surfaces only: the topbar nav-pill fault badge and the once-per-session notification. |
| **Transport** | REST, pull, **only while the page is open** | SignalR push on the existing `/hubs/audio` connection |
| **Consumes from the other** | nothing | `RotaryEncoderConfigStatus` and the deep link to this page |
| **Adds to the other** | nothing | nothing |

**Why this order and not the reverse.** `ENC-12`'s notification copy ends in an action — Designer
§7.6 writes it as *"→ Open encoder settings"*. A toast whose entire payoff is a page that does not
exist yet is a toast that teaches the owner the link is useless. Build the destination, then build
the sign that points at it.

**Why the transports differ, which is not an inconsistency.** The page is a pull surface because
§7.9 sets the rule for this tab explicitly — *"polling: 2 Hz, only while the card is open … a
background diagnostic poll is exactly the incidental load that correlates with audio distortion on
this box"* (`UI-2`). The badge cannot be a pull surface, because it must be right on `/queue` and
`/metrics` where nothing is polling. So the badge gets one push event carrying **only the tier**,
and the 24-field detail stays behind a page the owner has to open. That is the same load argument
producing two different answers for two different jobs, not two answers to one question.

### 0.3 Things Builder must NOT do

1. ⛔ **Do not put factory reset (`0x03/0x02`, `RotaryEncoderCommand.ResetDefaults`) on this page,
   behind a disclosure, behind a confirm, or anywhere else.** Handoff §7.1 and §7.8 both exclude it,
   and the reason is measured rather than theoretical: the device's factory tiers were read off this
   hardware on 2026-09-02 as `step=1` with `(150 ms ×5), (80 ms ×15), (40 ms ×50)`, which at the
   host's 2 % per unit is **one detent from silence to full**. `RotaryEncoderCommand.ResetDefaults`
   already exists in the enum. **Nothing in this row may send it.** Task 19 adds a test that pins
   that.

2. ⛔ **Do not make any of the 24 configuration fields editable except `reverse`.** Handoff §7.8:
   *"Twenty-four numeric inputs invites setting volume T3 to ×50 and then filing a bug about a volume
   slam."* Card 2 is display-only. If a value looks wrong, the fix is a change to
   `RotaryEncoderConfigDefaults` with the reasoning next to it, not a box on a settings page.

3. ⛔ **Do not let `Save to device` write anything other than what card 2 displays.** This is the one
   rule in this plan a reviewer is told to treat as a defect if broken (§0.5). It is enforced
   structurally in Task 8 — the screen, the push and the flash all read the same object from the same
   call — and pinned by a test in Task 19, not left to care.

4. ⛔ **Do not remap `RotaryEncoderActionRouter`'s index → handler table.** That is `ENC-5` / `ENC-7`
   (punch list §2 note, and `ENC-4`'s plan §0.2 item 1). This row **reads** the mapping and renders
   it; it does not change it. Task 3 is deliberately shaped so that the remap, when it comes, is an
   edit to one array and the page follows automatically.

5. ⛔ **Do not delete `RotaryEncoderOptions.VolumeStepPercent`.** It is read by
   `RotaryEncoderActionRouter.cs:270` (`var step = opts.VolumeStepPercent / 100f;`) and it is a real
   device field. What this row deletes is the **duplicate editable numeric** at
   `SystemConfigPage.razor:1533`. Quick win #5 (PR #490) says so in the punch list and it is still
   true.

6. ⛔ **Do not add a design token.** Handoff §6.9 and `ENC-4` §0.2 item 2. The inventory is in §2.7.

### 0.4 Contradictions found while planning, and how each is resolved

Every one of these is a place where the punch list, the handoff, or the shipped code disagree with
each other. They are resolved here so Builder does not have to arbitrate, and recorded so the owner
can overrule any of them cheaply.

| # | The disagreement | Resolution, and why |
|---|---|---|
| **C-1** | The punch list's `ENC-12` row lists a **`Retry now`** button; handoff §7.8's action table lists **`Re-apply settings`** (*"`0x02` + verify only, no flash … recovery from a Degraded tier"*). | **They are one button, and the Rev 3 name wins.** `Retry now` is Rev 2 vocabulary that Rev 3 superseded when it wrote the action table. One button, labelled **`Re-apply settings`**, built in Task 16. |
| **C-2** | The punch list's `ENC-12` row lists **`Restore designed defaults`**. Handoff §7.8's action table does not have it, and lists only three actions. | **Not built, and the reason is that Rev 3 removed the thing it would have undone.** In Rev 2 the page held 24 editable numerics and "restore designed defaults" meant *discard my edits*. Rev 3 made everything read-only except four `reverse` toggles — and the undo for a toggle is the toggle. Worse, a button reading **"defaults"** on this page is one misread away from the factory reset §7.8 deliberately keeps off it, which is precisely the value that puts one detent between silence and full. **Recommend leaving it out. If the owner wants it, it is a fifth action that clears the four persisted `reverse` overrides and re-pushes — half a day, and it needs a label that does not contain the word "defaults".** |
| **C-3** | The punch list's `ENC-8` row and this task brief both say `TuningStepKHz` is **already deleted** (quick win #5, PR #490) — *"confirm it is gone rather than re-deleting it."* | **Half true, and the surviving half is a documentation lie.** It is gone from every code path: no hit in `src/Radio.Core/Configuration/RotaryEncoderOptions.cs`, `src/Radio.Web/Models/ApiModels.cs`, `src/Radio.Web/Components/Pages/SystemConfigPage.razor` or `src/Radio.API/appsettings.json`. **It is still documented as a live setting in `design/INTEGRATIONS.md:80` (the JSON sample) and `design/INTEGRATIONS.md:95` (the settings table).** A deleted field that the setup guide still tells you to configure is the same failure class the field was deleted for. Task 20 removes both. ⚠ **Verify with `git grep TuningStepKHz`, not `grep -r ... src/`** — stale `bin/` and `obj/` output (including `net8.0` copies predating the .NET 10 migration) still contains the old `"TuningStepKHz": 10` and produces about twenty false positives. `deploy/` and `design/appsettings.example.json` are clean, so a fresh publish carries none of it. |
| **C-4** | The task brief cites the wrong report format at `design/INTEGRATIONS.md:24-25`; the punch list cites `INTEGRATIONS.md:19-24`. | **Neither is right; it is `design/INTEGRATIONS.md:22-26`.** The stale block is the `- **Report format:** 8-byte reports` bullet and its four sub-bullets. Cited correctly in Task 20 so nobody edits by line number and hits the wrong text. |
| **C-5** | Nothing in the punch list or the handoff mentions it, but **`design/INTEGRATIONS.md:5` states *"All integrations are **disabled by default** and opt-in via configuration."*** | **False since `ENC-0` shipped.** `RotaryEncoderOptions.Enabled` now defaults to **`true`** and its XML doc says so in as many words: *"ENC-0 changed both the default and the meaning. This used to be a gate that had to be opened … now presence decides."* A setup guide whose first paragraph says the subsystem is off by default will send someone hunting for a flag to flip when the knobs are already live. Fixed in Task 20. **Found while planning; not in any row.** |
| **C-6** | Handoff §7.8 puts **`Reset counters`** (`0x03/0x05`) in this row's action card, but the counters it zeroes are on the **Diagnostics** card, which is `ENC-14` and is not built yet. | **Built here as specced, with copy that does not overclaim.** The button is one command and cheap, and `ENC-14` will want it already working. But on this page it produces **no visible change**, so its confirmation must say exactly what happened and nothing more — `"Movement counters zeroed on the device."` — never *"diagnostics reset"* or any wording implying the owner should now see something. Task 16 fixes the string. |
| **C-7** | Card 2 labels every row by **cabinet name** (`VOLUME · SOURCE · PRESETS · TUNING`, handoff §7.8 and §9.1, index-keyed and fixed by D2). The Encoder Mapping table on the same page renders the **router's** order, which is still `0=Volume 1=Tuning 2=Source 3=Visualization` pending `ENC-5`/`ENC-7`. **So the page will call encoder 1 "SOURCE" in one card and "Tuning" in another.** | **Both tables stay, and the page states the disagreement instead of hiding it.** Card 2 is about the *device configuration* of a physical knob and is correctly keyed to the engraving; the mapping table is about *what the software currently does* with that knob, and it is correctly keyed to the router. They genuinely differ right now, and a page that quietly picked one would be asserting something false either way. Task 16 renders a one-line note **computed by comparing the two tables, not hard-coded** — so it appears only while they disagree and **disappears by itself the day `ENC-5`/`ENC-7` land**, with no follow-up edit and no chance of becoming a stale warning about a problem that is fixed. |
| **C-8** | The punch list and this task brief both say *"the UI mapping table at `SystemConfigPage.razor:1492-1493` **contradicts the router**."* | **It does not, any more.** Quick win #6 (PR #489) corrected the rows by hand; `SystemConfigPage.razor:1491-1494` now reads `1 - Tuning / Frequency Up-Down / Start-Stop Scan` and `2 - Source / Preview Sources / Switch To Previewed Source`, which is what `RotaryEncoderActionRouter` does. **Builder must not "fix" text that is already right.** The work this row owns is structural — remove the second source of truth so it cannot drift again when `ENC-5`/`ENC-7` remap (Task 3). Also note the whole table is `1482-1496`; `1492-1493` is two of its four data rows. |
| **C-9** | `CLAUDE.md` describes `src/Radio.Web` as *"Blazor Server UI (**MudBlazor** Material 3)"*, and the project auto-memory carries a `MudBlazor bUnit tests need JSInterop.Mode = JSRuntimeMode.Loose` note. The task brief says to use *"MudBlazor/Radzen as already used."* | **There is no MudBlazor in this repository.** `src/Radio.Web/Radio.Web.csproj:23` references `Radzen.Blazor 6.*` and nothing else UI-wise; a search for `MudBlazor` across `src/` returns **zero files**; `tests/Radio.Web.Tests` has no MudBlazor test package. **Build these cards in Radzen**, matching the sibling integration cards verbatim (Task 13's pattern block). The `JSRuntimeMode.Loose` requirement is real and still applies — it is a Radzen requirement here, not a MudBlazor one. **Found while planning; `CLAUDE.md` is fixed in Task 20.** |
| **C-10** | Handoff §7.8 names an action **`Save to device`**. The existing Rotary Encoders tab **already has a button labelled `Save`** (`SystemConfigPage.razor:1516-1518`), which writes the *app's* `rotaryencoder` settings section and toasts *"Encoder configuration saved. Restart required for changes to take effect."* | **Two buttons labelled "Save" on one tab, one writing app config and one writing device flash, is the ambiguity §0.5 exists to prevent.** §7.8 does not mention the existing card at all — those settings (VID/PID, device path, poll interval, reconnect delay, the `Enabled` escape hatch) are real and are not device configuration, so they are **kept and renamed**, not deleted. The tab becomes five cards: **`Connection settings`** (the existing one, its button relabelled **`Save connection settings`**) plus §7.8's four. Task 17. |
| **C-11** | `design-system.css:5364` consumes **`var(--signal-red-glow)`**, which is **never declared** in `:root` — `--signal-amber-glow` exists, its red sibling does not. | **Pre-existing, out of scope, and recorded so it is not rediscovered.** It resolves to nothing, so that rule silently renders no glow. **Neither this row nor `ENC-12` may reference `--signal-red-glow`**, and neither may declare it — §0.3 item 6 forbids new tokens, and adding it would change the appearance of an unrelated shipped component as a side effect. Logged to `design/FUTURE-WORK.md` in Task 20. |

### 0.5 The one rule a reviewer must treat as a defect

> **`Save to device` must write exactly what card 2 displays.**

Designer's words, and the reason the Rev 2 "safe baseline" was withdrawn: *"A Save button that writes
something other than what the screen shows is exactly the class of lie this project keeps shipping."*
This is `CLAUDE.md` § Pre-Merge Review applied to a button label rather than a comment, and the same
falsification standard applies — **check the claim against the code, not the plausibility of the
wording.**

It is not left to care. Task 8 makes the screen, the pushed bytes and the flashed bytes read the same
`RotaryEncoderDeviceConfig` instance from the same call, and Task 19 pins it with a test that encodes
what the API returned to the page and asserts it is byte-identical to what the push wrote. If a later
revision reintroduces any divergence, that test fails and the copy has to change with it.

The same standard applies to every string on this page. **A status card is nothing but assertions
about state.** `verified 07:14:02` must mean a read-back matched at 07:14:02, not that a push was
attempted then. `matches current design ✓` must mean the bytes were compared, not that nothing has
obviously changed. §2.4 exists because the second of those was not checkable without new state.

---

## 1. What `ENC-11` actually left behind

This is the state the page renders. Read it before Task 1; several tasks exist only because of a gap
listed here.

| What exists today | Where | What is missing for this row |
|---|---|---|
| Four-tier classification | `RotaryEncoderConfigVerifier.Classify` (`:96-121`) | Nothing. It is complete and tested. |
| The safety-clamp response | `RotaryEncoderConfigVerifier.VolumeClampFor` (`:132-135`), consumed at `RotaryEncoderActionRouter.cs:284` | Nothing. **Already live**: 6 units per event when `Configured`, **2** otherwise. |
| Field-by-field comparison | `RotaryEncoderConfigVerifier.Compare` (`:43-90`) returns `IReadOnlyList<RotaryEncoderConfigMismatch>` | The type is **`internal`** to `Radio.Infrastructure`, and the list is **computed, logged once, and dropped**. `HidRotaryEncoderService.ApplyConfigurationAsync` holds it in a local (`:376`) and never stores it. Card 2 needs it retained, and needs it **public**. |
| The read-back itself | `ReadConfigReportAsync` (`:427-452`) returns a decoded `RotaryEncoderDeviceConfig` | Also dropped. Card 2 shows *what the device says*, e.g. `⚠ T3 reads 0` — that is the read-back's value, not merely "differs". **Retaining the mismatch list is not enough.** |
| Current tier | `IRotaryEncoderService.ConfigStatus` (`:22`) | No change event, no timestamp, and **no endpoint** — the only consumers in `src/` are the router's clamp and the verifier. |
| Connection state | `IsConnected` + the `EncoderConnectionChanged` SignalR broadcast (`AudioStateUpdateService.cs:973-993`) | Nothing. Already on the wire and already consumed by this page at `SystemConfigPage.razor:1998`. |
| The push | `ApplyConfigurationAsync` (`:368-421`) | **`private`, and it runs exactly once per connection**, from inside `ReadFromDeviceAsync`. There is no way to re-apply short of unplugging the lead. §2.2. |
| Flash | `RotaryEncoderCommand.SaveConfig = 0x01` exists in the enum (`RotaryEncoderDeviceConfig.cs`) | **Never sent, anywhere in the repo.** Config is pushed fresh on every connect and lives in device RAM. "Last saved to device" has no producer today — this row introduces both the command and the timestamp. |
| `reverse` | `RotaryEncoderConfigDefaults.Create()` hard-codes `Reverse = false` on all four, with a comment calling it *"the one field a human should ever edit"* | No override path at all. `ApplyConfigurationAsync:370` calls `Create()` unconditionally and `HidRotaryEncoderService` takes no `IConfigurationManager`. |

---

## 2. Architecture

### 2.1 The shape, end to end

```
  Radio.Core                     Radio.Infrastructure              Radio.API            Radio.Web
  ──────────                     ────────────────────              ─────────            ─────────
  IRotaryEncoderProvisioning ◄── HidRotaryEncoderService ◄──────── IntegrationsController
    GetStatus()                    · retains LastPushed             GET  encoder/provisioning
    ReapplyAsync()                 · retains LastReadBack           POST encoder/reapply      ──► SystemConfigPage
    SaveToDeviceAsync()            · retains LastVerifiedUtc        POST encoder/save              4 cards
    ResetCountersAsync()           · maintenance channel (§2.2)     POST encoder/reset-counters    polls 2 Hz
    SetReverseAsync(i, bool)                                        PUT  encoder/reverse/{i}       while open

  RotaryEncoderDesignedConfig ◄── reverse overrides in the config store (§2.3)
    Resolve() ─────────────────────┬──► the bytes pushed
                                   ├──► the bytes flashed          ONE object, ONE call
                                   └──► the rows card 2 renders     (§0.5)

  RotaryEncoderActionRouter.Mapping ──► GET encoder/mapping ──────────────► Encoder Mapping table
    (the same array its dispatch uses — §2.5)
```

**`Radio.Web` and `Radio.API` are separate processes** — `radio-web.service` on port 5002 and
`radio-api.service` on 5000. `Radio.Web.csproj` *does* reference `Radio.Infrastructure`, so the types
are visible, but the **`RotaryEncoderActionRouter` instance lives in the API process** and the Web
process has no way to reach it. That is also standing project policy: the Web tier consumes backend
state over REST/SignalR and does not host backend services.

So "serve the mapping table from the router" cannot mean the page reads the router object — it means
the router grows one public array, the API projects it, and the page renders the projection. Three
hops, one source. §2.5.

### 2.2 One reader, many writers — and why this is the sharpest part of the row

`HidStream` has exactly one reader: `ReadFromDeviceAsync` sits in `await stream.ReadAsync(...)` with
**`stream.ReadTimeout = Timeout.Infinite`** (`HidRotaryEncoderService.cs:212`), which on an
event-driven device that is silent at rest means it blocks until a knob moves. Every command this row
adds — re-apply, save-to-flash, reset-counters, set-reverse — arrives on an **ASP.NET request
thread**, and every one of them needs a **read-back** to be worth anything (§7.2: *"a write without a
verified read-back is not a write, it is a hope"*).

Three designs were considered. Two do not work:

- ⛔ **Queue the request and let the read loop service it.** The loop is blocked in `ReadAsync` and
  will not wake to check a queue until the device sends something unprompted. On an idle cabinet that
  is never.
- ⛔ **Give the read a finite timeout so it can poll the queue.** This is the option
  `HidRotaryEncoderService.cs:203-211` already rejected in writing, and the reason is still true: if
  HidSharp surfaces an expiry as `IOException` rather than `TimeoutException`, the existing handler
  reads it as a disconnect and tears the connection down **once per timeout, forever**. Changing it
  is a real change to the one loop that keeps the knobs alive.

**The design this row uses:**

> The requester **writes** (under a lock, so writers do not interleave); the **read loop stays the
> only reader** and hands the response back through a `TaskCompletionSource`.

It works because the write is what makes the device speak: sending `0x03/0x04` (`ReadConfig`) causes
the device to emit a `0x02` report, which wakes the blocked `ReadAsync` on its own. No timeout
change, no second reader, no restructuring.

Concretely:
- `SemaphoreSlim _maintenanceLock` serialises whole operations (nobody re-applies while a save is
  mid-flight).
- `TaskCompletionSource<RotaryEncoderDeviceConfig> _pendingConfigRead` is set before the write and
  completed from `ParseReport`, which today drops report `0x02` on the floor — `RotaryEncoderDecoder.Decode`
  returns `false` for any report that is not `0x01` (`RotaryEncoderDecoder.cs:102-108`) and
  `ParseReport` returns immediately (`:485-489`). Task 5 adds the `0x02` branch **before** that early
  return.
- `ApplyConfigurationAsync`'s own read-back moves onto the same mechanism, so there is one path, not
  a boot path and a maintenance path that can drift.

⚠ **Task 4 is a gate, not a formality.** It proves on the real box that a `stream.Write` from a
second thread reaches the device while `ReadAsync` is pending, and that the reply arrives on the read
loop. HidSharp 2.1.0 documents nothing about this and the device is not simulatable. **If it fails,
stop and report — do not fall back to the finite-timeout design without the owner, because that
design's failure mode is a reconnect storm on a machine inside sealed furniture.**

### 2.3 One desired config, three consumers — and the test that will catch you

Card 2 displays it, the push writes it, the flash persists it. §0.5 requires all three to be the same
bytes, so they must come from one call.

Today `ApplyConfigurationAsync:370` calls `RotaryEncoderConfigDefaults.Create()` directly and there is
no override hook. This row introduces `RotaryEncoderDesignedConfig` (Task 6), whose `Resolve()`
returns `Create()` with the four persisted `reverse` overrides applied, and repoints every consumer at
it.

> ⚠ **Do not apply the overrides inside `RotaryEncoderConfigDefaults.Create()`.**
> `RotaryEncoderConfigVerifierTests.Defaults_NeverWrapAndNeverReverse` asserts
> `Assert.All(c.Encoders, e => Assert.False(e.Reverse))` on `Create()`'s output. That test is
> correct and must keep passing: `Create()` is the *designed* table from handoff §5.2, and the
> overrides are a per-cabinet wiring fact layered on top. Putting them inside `Create()` breaks the
> test, and "fix the test" would be the wrong fix.

> ⚠⚠ **And the override must reach the *verifier*, not only the wire.** `Compare` checks `reverse` on
> every encoder as a **safety** field, and `Classify` turns a safety mismatch into a `HardFault`
> **immediately**, which drops the host volume clamp from 6 to 2. So if the push sends `reverse=true`
> but the comparison still expects `Create()`'s `false`, **every knob the owner reverses becomes a
> permanent hard fault with a permanently tightened volume knob** — a red badge and a crippled volume
> control caused by a feature working as designed. `ApplyConfigurationAsync` already compares against
> the same `desired` local it encoded (`:370` and `:388`), so repointing that one line at
> `Resolve()` fixes both at once. **Task 19 pins it with a test that sets a reverse override and
> asserts the outcome is `Configured`.**

### 2.4 Staleness has to be checkable, not asserted

Card 1 must render Designer's line:

```
  Saved to device: 2026-06-01 · differs from current design ⚠   [ Save to device ]
```

**`differs from current design` is a claim about bytes.** A timestamp alone cannot support it — with
only "last saved at T" the code can compare T against nothing, and any rendering of the ✓ / ⚠ would
be an assertion the code cannot check. That is exactly the pre-merge failure class this row is
supposed to be careful about.

So `Save to device` persists **two** values, not one:

| Key | Value |
|---|---|
| `RotaryEncoder:LastSavedToDeviceUtc` | ISO-8601 round-trip timestamp of the successful flash |
| `RotaryEncoder:LastSavedConfigHash` | SHA-256 of `RotaryEncoderConfigCodec.Encode(resolved)` — the exact 107 bytes that were flashed |

Staleness is then `hash(Encode(Resolve())) != storedHash`, a real comparison of real bytes. Three
states, all checkable, and the copy for each is fixed in Task 12:

- no stored hash → **`never saved`** (no ✓, no ⚠ — the honest state on a fresh install)
- hash matches → **`matches current design ✓`**
- hash differs → **`differs from current design ⚠`**

Both keys live in the config store via `IConfigurationManager`, not in memory, because flash survives
a restart and a claim about flash that resets on restart would be worse than no claim.

### 2.5 The mapping table, served from the router across a process boundary

`SystemConfigPage.razor:1492-1493` is hand-typed HTML that contradicts the router — it claims encoder 1
press is *"Seek Next Station"* and encoder 2 press is *"Play/Pause"*; the code does scan-toggle and
source-commit. Quick win #6 (PR #489) corrected the text by hand, which fixes the instance and not the
cause: **hand-typed HTML mirroring a `switch` statement will drift again.**

⚠ **The table's *contents* are no longer wrong — quick win #6 (PR #489) corrected them by hand, and
the rows at `SystemConfigPage.razor:1491-1494` now agree with the router.** The punch list's wording
(*"the UI mapping table … contradicts the router"*) is stale on that point and this plan does not
repeat it. What survives is the **cause**: hand-typed HTML mirroring a `switch` will drift again on
the first remap, and `ENC-5`/`ENC-7` are that remap.

The fix is one array that both the dispatch and the display read (Task 3):

```csharp
public IReadOnlyList<RotaryEncoderMapping> Mapping => _mapping;
```

built in the router's constructor, holding the index, the turn description, the press description and
the handler delegates — and **`OnEncoderTurned` / `OnShortPress` dispatch through it** rather than
through a parallel `switch`. A descriptive list beside a `switch` is two sources of truth wearing one
name; it would drift on the first remap and nobody would notice, because it *looks* like the fix.

⚠ **Coordinate with `ENC-5` / `ENC-7`, which are being planned in parallel and own the remap to the
real physical order.** Two orders are possible and this plan works under both:

- **If `ENC-8` lands first** (expected): it introduces the array; `ENC-5`/`ENC-7` later edit **that
  array** and the page follows with no UI change. Task 3's note says so explicitly so their Builder
  does not add a second switch back.
- **If `ENC-5`/`ENC-7` land first**: Task 3 becomes *convert their switch into the array*, not *add an
  array beside it*. Same end state, and the page still renders whatever the router says at that time.

Either way this row **does not change the mapping** (§0.3 item 4) — and because ENC-4 is concurrently
editing all three dispatch sites in this file, expect to rebase.

### 2.6 What the page shows when there is no read-back

Three states, and none of them may render an empty table with implied agreement:

| State | Card 2 renders |
|---|---|
| Device absent, or no push has completed this connection (`Unknown`) | The designed values, and the comparison column reads **`— not read back`** on every row. Never a ✓. |
| Push completed, read-back matched (`Configured`) | Designed values, **`✓`** per row. |
| Push completed with mismatches (`Transient` / `Degraded` / `HardFault`) | Designed value **and the device's value** on every differing row (`⚠ reads 0`); `✓` on the rest. |

The middle column is the reason §2.1 retains `LastReadBack` and not just the mismatch list.

### 2.7 Tokens and components

**No new design tokens** (handoff §6.9). Reuse what the page already uses for its other integration
cards — the same card shell, the same Radzen controls, the same status-chip class the Connection row
already renders. The status colours are `--signal-red` / `--signal-amber` / the existing "OK" colour;
the four tiers map onto them in Task 12 and nowhere else, so `ENC-12` can reuse the same mapping
rather than inventing a second one.

**Not colour alone** (WCAG 1.4.1, and the bell handoff §8.3 sets the precedent this project follows):
every tier and every comparison result carries a word or a glyph, not just a hue. `✓` / `⚠` / `—`
plus text, on every row.

---

## 3. Tasks

Twenty tasks in five phases. Phases 0–2 are backend and can be built and tested with no browser.
Phase 3 is the page. **Task 4 is a hardware gate and four later tasks depend on its result** — do not
skip past it.

Every task ends with `dotnet build --configuration Release` clean (warnings are errors) and the
relevant test project green.

---

### Phase 0 — the public contract (`Radio.Core`)

#### Task 1 — The cabinet names, in one place

**Why:** card 2 labels every row `VOLUME · SOURCE · PRESETS · TUNING` and never by index (handoff
§7.8). That order is fixed by owner decision D2 (§9.1) and is a **physical** fact about the
escutcheon — it is not the router's mapping and must not be derived from it (§0.4 C-7).

**Create** `src/Radio.Core/Configuration/RotaryEncoderCabinetNames.cs`:

```csharp
namespace Radio.Core.Configuration;

/// <summary>
/// The engraving on the cabinet face, left to right, indexed by encoder index.
///
/// <para>
/// Fixed by owner decision D2 (encoder handoff §9.1) and <b>irreversible</b> — punch list constraint
/// O9 names the escutcheon drilling as the one step in the whole project that cannot be undone. This
/// is a fact about a piece of furniture, not a software mapping.
/// </para>
///
/// <para>
/// ⚠ <b>This is not <see cref="Radio.Infrastructure"/>'s action mapping and must never be derived
/// from it.</b> The router currently dispatches index 1 to tuning and index 2 to source, which does
/// not match this order; that mismatch is deliberate and tracked (ENC-5 / ENC-7 own the remap). A
/// settings page that showed one of these in place of the other would be asserting something false.
/// Index 0 is VOLUME under both, which is why the knob with a safety hazard on it is already right.
/// </para>
/// </summary>
public static class RotaryEncoderCabinetNames
{
  /// <summary>Left to right, as engraved. Index n is encoder n.</summary>
  public static readonly IReadOnlyList<string> Ordered = ["VOLUME", "SOURCE", "PRESETS", "TUNING"];

  /// <summary>The engraved name for an encoder index, or <c>KNOB {index}</c> if the index is off the face.</summary>
  public static string For(int encoderIndex) =>
    encoderIndex >= 0 && encoderIndex < Ordered.Count
      ? Ordered[encoderIndex]
      : $"KNOB {encoderIndex}";
}
```

**Test** — append to `tests/Radio.Core.Tests/Configuration/RotaryEncoderOptionsTests.cs`, or create
`RotaryEncoderCabinetNamesTests.cs` in the same folder:

```csharp
[Fact]
public void CabinetNames_PutVolumeAtIndexZero_WhichIsTheOneIndexTheRouterAlsoAgreesOn()
{
  Assert.Equal("VOLUME", RotaryEncoderCabinetNames.For(RotaryEncoderConfigDefaults.VolumeEncoderIndex));
  Assert.Equal(RotaryEncoderDeviceConfig.EncoderCount, RotaryEncoderCabinetNames.Ordered.Count);
}

[Fact]
public void CabinetNames_DoNotThrowForAnIndexOffTheFace()
{
  Assert.Equal("KNOB 9", RotaryEncoderCabinetNames.For(9));
}
```

---

#### Task 2 — The provisioning contract (`Radio.Core`)

**Why:** `RotaryEncoderConfigMismatch` is `internal` to `Radio.Infrastructure`, and `Radio.Web` does
not reference `Radio.Infrastructure` at all — every DTO crosses HTTP. The page needs a public shape
to travel in, and the API needs an interface to call.

**Create** `src/Radio.Core/Configuration/RotaryEncoderProvisioning.cs`:

```csharp
namespace Radio.Core.Configuration;

/// <summary>Whether the device agreed with one configured field.</summary>
public enum RotaryEncoderFieldAgreement
{
  /// <summary>No read-back has been obtained on this connection. <b>Not the same as agreement.</b></summary>
  NotReadBack = 0,

  /// <summary>The device reported this field back with the value that was pushed.</summary>
  Agrees = 1,

  /// <summary>The device reported a different value. <see cref="RotaryEncoderFieldState.ReadBackValue"/> carries it.</summary>
  Differs = 2,
}

/// <summary>One field of the device configuration, as designed and as the device reports it.</summary>
/// <param name="EncoderIndex">Encoder index, or <c>-1</c> for the global <c>steps_per_detent</c>.</param>
/// <param name="Field">The wire field name, matching the strings <c>RotaryEncoderConfigVerifier.Compare</c> emits.</param>
/// <param name="DesignedValue">What the app pushed, rendered for display.</param>
/// <param name="ReadBackValue">What the device reported, or null when there has been no read-back.</param>
/// <param name="IsSafetyField">
/// <c>wrap</c> on VOLUME and <c>reverse</c> on any knob. A mismatch here is a hard fault immediately
/// and tightens the host volume clamp; the page shows it differently for that reason.
/// </param>
public sealed record RotaryEncoderFieldState(
  int EncoderIndex,
  string Field,
  string DesignedValue,
  string? ReadBackValue,
  bool IsSafetyField,
  RotaryEncoderFieldAgreement Agreement);

/// <summary>How the device's flash compares to the configuration the app would push right now.</summary>
public enum RotaryEncoderFlashState
{
  /// <summary>This app has never flashed this device. Renders as <c>never saved</c> — not as a warning.</summary>
  NeverSaved = 0,

  /// <summary>The bytes last flashed are byte-identical to the bytes the app would push now.</summary>
  MatchesCurrentDesign = 1,

  /// <summary>The flashed bytes differ. The knobs still run the pushed config; only a boot window before
  /// the app pushes would use the stale copy.</summary>
  DiffersFromCurrentDesign = 2,
}

/// <summary>
/// Everything the encoder Settings surface renders, in one immutable read (ENC-8).
///
/// <para>
/// ⚠ Every field here is an <b>assertion about state that the page will print</b>, so each one must
/// be produced by a check rather than an inference. <see cref="LastVerifiedUtc"/> is set only when a
/// read-back <i>matched</i>, never when a push was merely attempted; <see cref="Flash"/> is a
/// comparison of stored bytes against current bytes, not a guess from a timestamp.
/// </para>
/// </summary>
public sealed record RotaryEncoderProvisioningSnapshot
{
  /// <summary>False when <c>RotaryEncoder:Enabled</c> is off. The page then says so and shows nothing else.</summary>
  public bool Enabled { get; init; }

  public bool IsConnected { get; init; }

  /// <summary>True once the device has connected at least once since the API started (ENC-0).</summary>
  public bool WasEverConnected { get; init; }

  public RotaryEncoderConfigStatus Status { get; init; } = RotaryEncoderConfigStatus.Unknown;

  /// <summary>When a read-back last <b>matched</b> the pushed configuration. Null if that has never happened.</summary>
  public DateTimeOffset? LastVerifiedUtc { get; init; }

  /// <summary>When a push was last attempted, whatever its outcome.</summary>
  public DateTimeOffset? LastAttemptedUtc { get; init; }

  /// <summary>When this app last successfully wrote the device's flash. Persisted, because flash outlives a restart.</summary>
  public DateTimeOffset? LastSavedToDeviceUtc { get; init; }

  public RotaryEncoderFlashState Flash { get; init; } = RotaryEncoderFlashState.NeverSaved;

  /// <summary>Every comparable field, designed value beside read-back value.</summary>
  public IReadOnlyList<RotaryEncoderFieldState> Fields { get; init; } = [];
}

/// <summary>
/// The owner-initiated half of encoder configuration (ENC-8), separate from
/// <see cref="Radio.Core.Interfaces.Input.IRotaryEncoderService"/> so the input path is not widened
/// with provisioning concerns. The same <c>HidRotaryEncoderService</c> instance implements both.
/// </summary>
public interface IRotaryEncoderProvisioning
{
  /// <summary>Current state. Cheap, allocation-only — safe to poll at 2 Hz while the page is open.</summary>
  RotaryEncoderProvisioningSnapshot GetSnapshot();

  /// <summary>
  /// Pushes the resolved configuration and verifies it by read-back. <b>Does not touch flash.</b>
  /// This is the Settings page's <c>Re-apply settings</c>.
  /// </summary>
  Task<RotaryEncoderProvisioningSnapshot> ReapplyAsync(CancellationToken ct = default);

  /// <summary>
  /// Pushes, verifies, and only then writes flash (<c>0x03/0x01</c>), recording what was written.
  /// <b>Flash receives exactly the bytes that were just verified</b> — see the plan §0.5.
  /// A failed verify leaves flash untouched.
  /// </summary>
  Task<RotaryEncoderProvisioningSnapshot> SaveToDeviceAsync(CancellationToken ct = default);

  /// <summary>Zeroes the device's movement/diagnostic counters (<c>0x03/0x05</c>). No read-back exists for this.</summary>
  Task<bool> ResetCountersAsync(CancellationToken ct = default);

  /// <summary>
  /// Persists a per-knob direction override and immediately pushes + verifies it. Marks the flashed
  /// copy stale as a consequence of the push, not as a separate assertion.
  /// </summary>
  Task<RotaryEncoderProvisioningSnapshot> SetReverseAsync(int encoderIndex, bool reverse, CancellationToken ct = default);
}
```

---

#### Task 3 — The router serves its own mapping, and dispatches through it

**Why:** `SystemConfigPage.razor:1492-1493` is hand-typed HTML mirroring a `switch`. PR #489
corrected the text; it did not remove the second source of truth, so it will drift again on the first
remap. §2.5.

⚠ **`ENC-4` has landed on `main` and this task is written against it** (verified at `29acc01`).
There are now **two** index switches to replace — `OnEncoderTurned` (`:93-99`) and `OnShortPress`
(`:135-141`) — plus `OnLongPress` (`:149`), which is index-0-only by design, has no table to serve,
and is **left exactly as `ENC-4` wrote it**.

**Edit** `src/Radio.Infrastructure/Platform/Input/RotaryEncoderActionRouter.cs`.

Add the public descriptor next to the class (same file, above the router):

```csharp
/// <summary>
/// One knob's behaviour, as the router actually dispatches it.
///
/// <para>
/// ⚠ <b>Descriptions are for display and must describe what the delegates do.</b> This type exists
/// because a hand-typed table on the Settings page drifted from the code (§2.2 defect 2 of the
/// encoder handoff, corrected by hand in PR #489 and structurally here). Changing a handler without
/// changing its description recreates the defect this replaced.
/// </para>
/// </summary>
/// <param name="EncoderIndex">Encoder index this entry dispatches.</param>
/// <param name="TurnDescription">What a detent does, in the owner's language.</param>
/// <param name="PressDescription">What a short press does, in the owner's language.</param>
public sealed record RotaryEncoderMapping(int EncoderIndex, string TurnDescription, string PressDescription);
```

Inside the router, build the table once in the constructor and dispatch through it. **Replace** the
`switch (e.EncoderIndex)` in `OnEncoderTurned` and the one in `OnShortPress` with table lookups:

```csharp
  private readonly RotaryEncoderMapping[] _mapping;
  private readonly Action<int>[] _turnHandlers;
  private readonly Action[] _pressHandlers;

  /// <summary>
  /// What each knob currently does. <b>This is the table the router dispatches through</b>, not a
  /// description kept alongside it, so the Settings page cannot disagree with the code.
  ///
  /// <para>
  /// ⚠ Indices 1-3 do not match the cabinet engraving (VOLUME / SOURCE / PRESETS / TUNING) yet. That
  /// is deliberate and tracked: ENC-5 and ENC-7 own the remap because they introduce the handlers it
  /// would point at. Editing this array is how that remap is made — there is no second place to
  /// change.
  /// </para>
  /// </summary>
  public IReadOnlyList<RotaryEncoderMapping> Mapping => _mapping;
```

In the constructor, after the existing field assignments:

```csharp
    // Index-ordered and index-addressed: entry n dispatches encoder n. Kept as three parallel arrays
    // rather than delegates on the record so the record stays a plain data type the API can project.
    _mapping =
    [
      new RotaryEncoderMapping(0, "Volume up / down", "Mute on / off"),
      new RotaryEncoderMapping(1, "Tune up / down (radio sources)", "Start / stop station scan"),
      new RotaryEncoderMapping(2, "Preview the next / previous source", "Switch to the previewed source"),
      new RotaryEncoderMapping(3, "Cycle visualization mode", "Visualization on / off"),
    ];
    _turnHandlers = [HandleVolumeTurn, HandleTuningTurn, HandleSourceTurn, HandleVizTurn];
    _pressHandlers = [HandleVolumePress, HandleTuningPress, HandleSourcePress, HandleVizPress];
```

and in `OnEncoderTurned`, replacing the switch:

```csharp
      if (e.EncoderIndex >= 0 && e.EncoderIndex < _turnHandlers.Length)
      {
        _turnHandlers[e.EncoderIndex](e.Delta);
      }
```

and the same shape in `OnShortPress` against `_pressHandlers`. Leave `OnLongPress` as `ENC-4` wrote
it — it is index-0-only by design and has no table to serve.

> **Note for Builder, and for whoever ships `ENC-5`/`ENC-7`:** the remap is now *one edit to
> `_mapping` plus the matching reorder of `_turnHandlers` / `_pressHandlers`*. **Do not reintroduce a
> `switch`.** If `ENC-5`/`ENC-7` have already landed when you start, this task converts their switch
> into these arrays rather than adding arrays beside it.

**Test** — create `tests/Radio.Infrastructure.Tests/Platform/Input/RotaryEncoderMappingTableTests.cs`:

```csharp
[Fact]
public void Mapping_CoversEveryEncoderExactlyOnce_InIndexOrder()
{
  // The API projects this array positionally; a gap or a duplicate would silently mislabel a knob.
  var router = BuildRouter();
  Assert.Equal(RotaryEncoderDeviceConfig.EncoderCount, router.Mapping.Count);
  for (int i = 0; i < router.Mapping.Count; i++)
  {
    Assert.Equal(i, router.Mapping[i].EncoderIndex);
  }
}

[Fact]
public void Mapping_DescribesEveryKnob_WithNoPlaceholderText()
{
  var router = BuildRouter();
  Assert.All(router.Mapping, m =>
  {
    Assert.False(string.IsNullOrWhiteSpace(m.TurnDescription));
    Assert.False(string.IsNullOrWhiteSpace(m.PressDescription));
    Assert.DoesNotContain("TODO", m.TurnDescription, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("TODO", m.PressDescription, StringComparison.OrdinalIgnoreCase);
  });
}

[Fact]
public void TurningAnEncoderDispatchesThroughTheSameTableTheUiRenders()
{
  // The point of the table. If dispatch ever stops going through Mapping, this fails.
  var encoder = new FakeRotaryEncoderService();
  var router = BuildRouter(encoder);

  encoder.RaiseTurn(encoderIndex: 3, delta: 1);

  Assert.Equal("Cycle visualization mode", router.Mapping[3].TurnDescription);
  Assert.Equal(1, VizModeCycleCount);   // the handler the table points at actually ran
}
```

`BuildRouter` reuses the fakes `ENC-4` Task 5 introduces (`FakeRotaryEncoderService`, a stub
`IAudioManager`, a recording `IEncoderFeedbackSink`). If `ENC-4` has not merged, build them here and
`ENC-4`'s Builder will find them already present.

---

### Phase 1 — Infrastructure

#### Task 4 — ⚠ GATE: prove a write can reach the device while the read loop is blocked

**Why:** §2.2. Four later tasks assume a `stream.Write` from an ASP.NET request thread reaches the
device while `ReadFromDeviceAsync` is parked in `ReadAsync` with an infinite timeout, and that the
device's reply arrives on the read loop. HidSharp 2.1.0 documents nothing about this, the device
cannot be simulated, and the fallback design has a failure mode (a reconnect storm) that is expensive
on a machine inside sealed furniture.

**This task produces evidence, not code.** Do it on the box before writing Task 5.

1. Deploy the current `main` build: `./deploy/Deploy-ToLinux.ps1` (no flags — `OPS-1` fixed the
   defaults). Confirm both SHAs: `curl -s http://radio:5000/api/health/version` and `:5002`.
2. Add a temporary, un-merged debug endpoint (or a `dotnet run` scratch harness on the box) that
   resolves `HidRotaryEncoderService`, and from an ASP.NET request thread writes
   `RotaryEncoderConfigCodec.EncodeCommand(RotaryEncoderCommand.ReadConfig)` to the live stream while
   **no knob is being touched**.
3. Confirm from the file sink — **not `journalctl`; since `LOG-11` the journal only carries WARNING
   and above** — that a 107-byte report `0x02` arrives:
   `ssh mmack@radio 'F=$(ls -t /opt/radio-console/logs/radio-*.txt | head -1); tail -100 $F'`
4. Record the result **in this plan file**, under a new §7 heading, as
   `Task 4 result — <date> — PASS/FAIL, <what was observed>`.

**Pass** → continue to Task 5 as written.
**Fail** → ⛔ **stop and report to the owner.** Do not silently switch to a finite `ReadTimeout`: the
existing comment at `HidRotaryEncoderService.cs:203-211` explains that if HidSharp surfaces the
expiry as `IOException` the loop tears the connection down once per timeout forever, and that is a
change to the one loop keeping the knobs alive.

---

#### Task 5 — The maintenance channel

**Why:** every command in Task 8 needs a read-back, and the read loop is the only reader (§2.2).

**Edit** `src/Radio.Infrastructure/Platform/Input/HidRotaryEncoderService.cs`.

Add fields:

```csharp
  /// <summary>
  /// Serialises whole owner-initiated operations, so a Save cannot interleave with a Re-apply.
  /// Does not guard the read loop — that has exactly one reader by construction.
  /// </summary>
  private readonly SemaphoreSlim _maintenanceLock = new(1, 1);

  /// <summary>
  /// The live HID stream, or null while disconnected. Written by the read loop, read by maintenance
  /// commands arriving on request threads.
  ///
  /// <para>
  /// ⚠ The claim that makes an unsynchronised read safe here is narrow, so it is stated rather than
  /// assumed: a stale non-null reference can only cause a write to a disposed stream, which throws
  /// <see cref="ObjectDisposedException"/> / <see cref="IOException"/>, and every maintenance path
  /// catches both and reports "the device is not available". It cannot corrupt the read loop, and it
  /// cannot silently succeed.
  /// </para>
  /// </summary>
  private volatile HidStream? _liveStream;

  /// <summary>
  /// Set immediately before a <c>0x03/0x04</c> read-config request and completed by
  /// <see cref="ParseReport"/> when the device answers. Null when no read-back is outstanding.
  /// </summary>
  private TaskCompletionSource<RotaryEncoderDeviceConfig>? _pendingConfigRead;
```

In `ReadFromDeviceAsync`, set and clear the stream around the existing body:

```csharp
    _liveStream = stream;
    try
    {
      // ... existing body, unchanged ...
    }
    finally
    {
      _liveStream = null;
      // A disconnect mid-request must fail the waiter rather than leave the caller on the 2 s
      // timeout: the honest answer is "the device went away", not "the device did not confirm".
      Interlocked.Exchange(ref _pendingConfigRead, null)
        ?.TrySetException(new IOException("Encoder disconnected while a configuration read was outstanding."));
    }
```

In `ParseReport`, **before** the existing `if (!_decoder.Decode(...)) return;`, add the config branch —
`RotaryEncoderDecoder.Decode` returns false for every report that is not `0x01`, so today report
`0x02` is dropped silently:

```csharp
    // Report 0x02 is the device's configuration read-back. The decoder ignores it by design
    // (RotaryEncoderDecoder.Decode returns false for anything that is not report 0x01), so it is
    // claimed here, before that early return, and handed to whoever asked for it.
    if (RotaryEncoderConfigCodec.TryDecode(data, bytesRead, out var readBack))
    {
      Interlocked.Exchange(ref _pendingConfigRead, null)?.TrySetResult(readBack);
      return;
    }
```

Add the request helper:

```csharp
  /// <summary>
  /// Asks the device for its live configuration and waits for the read loop to hand it back.
  ///
  /// <para>
  /// The write is what makes the device speak, which is why this works while the read loop is parked
  /// in an infinite <c>ReadAsync</c>: the reply wakes it. There is still exactly one reader.
  /// </para>
  /// </summary>
  /// <returns>The device's configuration, or null if it did not answer within the timeout.</returns>
  private async Task<RotaryEncoderDeviceConfig?> RequestConfigReadBackAsync(
    HidStream stream, CancellationToken cancellationToken)
  {
    var tcs = new TaskCompletionSource<RotaryEncoderDeviceConfig>(
      TaskCreationOptions.RunContinuationsAsynchronously);
    Interlocked.Exchange(ref _pendingConfigRead, tcs);

    byte[] readConfig = RotaryEncoderConfigCodec.EncodeCommand(RotaryEncoderCommand.ReadConfig);
    stream.Write(readConfig, 0, readConfig.Length);

    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(ReadBackTimeout);

    try
    {
      return await tcs.Task.WaitAsync(timeout.Token);
    }
    catch (OperationCanceledException)
    {
      // Treated as a mismatch rather than an error, exactly as ENC-11 already treats it: from the
      // host's point of view "did not confirm" and "confirmed something wrong" have the same
      // consequence, which is that the configuration is not trustworthy.
      Interlocked.CompareExchange(ref _pendingConfigRead, null, tcs);
      return null;
    }
  }

  /// <summary>How long to wait for a <c>0x02</c> read-back. Unchanged from ENC-11's inline value.</summary>
  private static readonly TimeSpan ReadBackTimeout = TimeSpan.FromSeconds(2);
```

**Delete** `ReadConfigReportAsync` (`:427-452`) and repoint `ApplyConfigurationAsync` at
`RequestConfigReadBackAsync`, dropping the now-redundant `stream.Write(readConfig, ...)` line from
its write triple. **One read-back path, not a boot path and a maintenance path that can drift.**

**Tests** — `tests/Radio.Infrastructure.Tests/Platform/Input/` — these exercise the completion logic
without hardware by driving `ParseReport` through a test seam; if `ParseReport` cannot be reached from
a test, extract the `0x02` claim into an `internal static bool TryClaimConfigReadBack(...)` and test
that. Do not skip the tests because the class is I/O-bound; **the part being tested is the handoff,
not the I/O.**

- `ConfigReport_CompletesAnOutstandingReadBackRequest`
- `ConfigReport_WithNoOutstandingRequest_IsIgnoredAndDoesNotThrow`
- `PositionsReport_DoesNotCompleteAnOutstandingReadBackRequest`
- `Disconnect_FailsAnOutstandingReadBackRequest_RatherThanLeavingItToTimeOut`

---

#### Task 6 — One resolved configuration, with the `reverse` overrides layered on

**Why:** §2.3. Card 2, the push and the flash must be the same bytes, and `reverse` is the one field
the owner may change.

⚠ **Read §2.3's two warnings before writing this.** The overrides go *outside*
`RotaryEncoderConfigDefaults.Create()`, and they must reach the **verifier**, not only the wire —
otherwise every reversed knob is a permanent `HardFault` with a permanently tightened volume clamp.

**Create** `src/Radio.Infrastructure/Platform/Input/RotaryEncoderDesignedConfig.cs`:

```csharp
using Microsoft.Extensions.Logging;
using Radio.Configuration.Abstractions;
using Radio.Core.Configuration;

namespace Radio.Infrastructure.Platform.Input;

/// <summary>
/// The configuration the app intends the device to run: the designed table from encoder handoff §5.2,
/// with the owner's per-knob direction overrides applied.
///
/// <para>
/// <b>One object, three consumers</b> — the bytes pushed, the bytes flashed, and the rows the Settings
/// page renders all come from <see cref="ResolveAsync"/>. That is what makes "Save writes what the
/// screen shows" a structural property rather than a promise (ENC-8 plan §0.5).
/// </para>
///
/// <para>
/// ⚠ The overrides are applied <b>here</b> and not inside <see cref="RotaryEncoderConfigDefaults.Create"/>,
/// because that method is the designed table and is asserted as such by
/// <c>RotaryEncoderConfigVerifierTests.Defaults_NeverWrapAndNeverReverse</c>. A wiring fact about one
/// cabinet is not a change to the design.
/// </para>
/// </summary>
public sealed class RotaryEncoderDesignedConfig
{
  /// <summary>Config-store key prefix for the per-knob direction overrides.</summary>
  internal const string ReverseKeyPrefix = "RotaryEncoder:Reverse:";

  /// <summary>Config-store key holding the UTC timestamp of the last successful flash write.</summary>
  internal const string LastSavedUtcKey = "RotaryEncoder:LastSavedToDeviceUtc";

  /// <summary>Config-store key holding the SHA-256 of the bytes last flashed. See ENC-8 plan §2.4.</summary>
  internal const string LastSavedHashKey = "RotaryEncoder:LastSavedConfigHash";

  private readonly ILogger<RotaryEncoderDesignedConfig> _logger;
  private readonly IConfigurationManager? _configurationManager;
  private readonly bool[] _reverse = new bool[RotaryEncoderDeviceConfig.EncoderCount];
  private bool _loaded;

  public RotaryEncoderDesignedConfig(
    ILogger<RotaryEncoderDesignedConfig> logger,
    IConfigurationManager? configurationManager = null)
  {
    _logger = logger;
    _configurationManager = configurationManager;
  }

  private string StoreId =>
    _configurationManager!.CurrentStoreType == ConfigurationStoreType.Sqlite ? "sqlite" : "config";

  /// <summary>
  /// The configuration to push, flash and display.
  ///
  /// <para>
  /// Overrides are read from the store on first use rather than from <c>IOptionsMonitor</c>, which
  /// only ever reflects <c>appsettings.json</c> and would silently discard a value the owner set at
  /// runtime — the trap <c>PreferencesPersistenceService</c> documents at its own :113-117.
  /// </para>
  /// </summary>
  public async Task<RotaryEncoderDeviceConfig> ResolveAsync(CancellationToken ct = default)
  {
    await EnsureLoadedAsync(ct);

    RotaryEncoderDeviceConfig config = RotaryEncoderConfigDefaults.Create();
    for (int i = 0; i < RotaryEncoderDeviceConfig.EncoderCount; i++)
    {
      config.Encoders[i].Reverse = _reverse[i];
    }

    return config;
  }

  /// <summary>The current direction override for one knob, without re-reading the store.</summary>
  public bool IsReversed(int encoderIndex) => _reverse[encoderIndex];

  /// <summary>Persists a direction override. The caller is responsible for pushing it to the device.</summary>
  public async Task SetReverseAsync(int encoderIndex, bool reverse, CancellationToken ct = default)
  {
    ArgumentOutOfRangeException.ThrowIfNegative(encoderIndex);
    ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(encoderIndex, RotaryEncoderDeviceConfig.EncoderCount);

    await EnsureLoadedAsync(ct);
    _reverse[encoderIndex] = reverse;

    if (_configurationManager is null)
    {
      // No store configured: the override applies to this process and is not persisted. Said plainly
      // rather than logged as a success, because the owner will expect it to survive a restart.
      _logger.LogWarning(
        "No configuration store; encoder {Index} direction override is in-memory only and will not survive a restart",
        encoderIndex);
      return;
    }

    await _configurationManager.SetValueAsync(
      StoreId, $"{ReverseKeyPrefix}{encoderIndex}", reverse.ToString(), ct);
  }

  private async Task EnsureLoadedAsync(CancellationToken ct)
  {
    if (_loaded || _configurationManager is null)
    {
      _loaded = true;
      return;
    }

    try
    {
      IConfigurationStore store = await _configurationManager.GetStoreAsync(StoreId, ct);
      for (int i = 0; i < RotaryEncoderDeviceConfig.EncoderCount; i++)
      {
        ConfigurationEntry? entry = await store.GetEntryAsync($"{ReverseKeyPrefix}{i}", ct: ct);
        _reverse[i] = entry is not null && bool.TryParse(entry.Value, out bool v) && v;
      }
    }
    catch (Exception ex)
    {
      // Defaults are the safe answer: every knob turns the designed way. Logged as a warning because
      // a knob that is wired backwards will now feel backwards until the store is readable again.
      _logger.LogWarning(ex, "Could not read encoder direction overrides; using the designed directions");
    }

    _loaded = true;
  }
}
```

**Edit** `HidRotaryEncoderService.ApplyConfigurationAsync` — replace line `:370`:

```csharp
    RotaryEncoderDeviceConfig desired = await _designedConfig.ResolveAsync(cancellationToken);
```

The rest of the method is unchanged, **and that is the point**: `desired` is already both the value
encoded at `:371` and the value compared at `:388`, so one line repoints the wire and the verifier
together.

**Tests** — `tests/Radio.Infrastructure.Tests/Platform/Input/RotaryEncoderDesignedConfigTests.cs`:

```csharp
[Fact]
public async Task Resolve_WithNoOverrides_IsByteIdenticalToTheDesignedTable()
{
  var sut = new RotaryEncoderDesignedConfig(NullLogger<RotaryEncoderDesignedConfig>.Instance);
  Assert.Equal(
    RotaryEncoderConfigCodec.Encode(RotaryEncoderConfigDefaults.Create()),
    RotaryEncoderConfigCodec.Encode(await sut.ResolveAsync()));
}

[Fact]
public async Task Resolve_AppliesAReverseOverride_WithoutMutatingTheDesignedTable()
{
  var sut = new RotaryEncoderDesignedConfig(NullLogger<RotaryEncoderDesignedConfig>.Instance);
  await sut.SetReverseAsync(2, true);

  Assert.True((await sut.ResolveAsync()).Encoders[2].Reverse);
  // The designed table is a separate, unchanged thing — the verifier tests assert this too.
  Assert.False(RotaryEncoderConfigDefaults.Create().Encoders[2].Reverse);
}

[Fact]
public async Task AReversedKnob_VerifiesAsConfigured_AndDoesNotBecomeAHardFault()
{
  // The trap this test exists for: `reverse` is a SAFETY field, so if the push carried the override
  // but the comparison still expected the designed `false`, every knob the owner reverses would sit
  // in HardFault with the volume clamp tightened to 2 units per event, forever.
  var sut = new RotaryEncoderDesignedConfig(NullLogger<RotaryEncoderDesignedConfig>.Instance);
  await sut.SetReverseAsync(0, true);

  RotaryEncoderDeviceConfig desired = await sut.ResolveAsync();
  byte[] wire = RotaryEncoderConfigCodec.Encode(desired);
  RotaryEncoderConfigCodec.TryDecode(wire, wire.Length, out var deviceEcho);

  Assert.Empty(RotaryEncoderConfigVerifier.Compare(desired, deviceEcho));
  Assert.Equal(
    RotaryEncoderConfigStatus.Configured,
    RotaryEncoderConfigVerifier.Classify(RotaryEncoderConfigVerifier.Compare(desired, deviceEcho), attempt: 1));
}
```

---

#### Task 7 — Retain what was sent, what came back, and when

**Why:** §1. `ApplyConfigurationAsync` computes the mismatch list into a local and drops it; the
read-back is dropped too. Card 2 needs both, and `LastVerifiedUtc` must mean *matched*, not
*attempted*.

**Edit** `HidRotaryEncoderService`. Add fields and a projection:

```csharp
  /// <summary>Guards the four retained snapshot fields, which a request thread reads while the read loop writes.</summary>
  private readonly object _snapshotGate = new();
  private RotaryEncoderDeviceConfig? _lastPushed;
  private RotaryEncoderDeviceConfig? _lastReadBack;
  private DateTimeOffset? _lastVerifiedUtc;
  private DateTimeOffset? _lastAttemptedUtc;
```

In `ApplyConfigurationAsync`, inside the attempt loop, record each attempt and record verification
**only on a match**:

```csharp
      lock (_snapshotGate)
      {
        _lastPushed = desired;
        _lastReadBack = readBack;               // null when the device did not answer
        _lastAttemptedUtc = _timeProvider.GetUtcNow();
        if (ConfigStatus == RotaryEncoderConfigStatus.Configured)
        {
          // Set only here. "verified 07:14:02" on the status card must mean a read-back MATCHED at
          // 07:14:02 — not that a push was attempted then. See ENC-8 plan §0.5.
          _lastVerifiedUtc = _lastAttemptedUtc;
        }
      }
```

`readBack` must be hoisted out of the `try` into the loop scope for this. Inject a
`TimeProvider timeProvider` (defaulting to `TimeProvider.System`) so the timestamps are testable, the
same way `ENC-4` did it for the router.

Add the field projection — this is where `internal RotaryEncoderConfigMismatch` becomes public
`RotaryEncoderFieldState`:

```csharp
  /// <summary>
  /// Projects the retained push and read-back into the public per-field shape the Settings page
  /// renders (ENC-8 Task 2).
  ///
  /// <para>
  /// The field set comes from <see cref="RotaryEncoderConfigVerifier.Compare"/>'s own vocabulary, so
  /// the page and the verifier cannot disagree about what a field is called or which fields are
  /// safety fields.
  /// </para>
  /// </summary>
  private IReadOnlyList<RotaryEncoderFieldState> ProjectFields(
    RotaryEncoderDeviceConfig? pushed, RotaryEncoderDeviceConfig? readBack)
  {
    if (pushed is null)
    {
      return [];
    }

    var differing = readBack is null
      ? new HashSet<(int, string)>()
      : RotaryEncoderConfigVerifier.Compare(pushed, readBack)
          .Select(m => (m.EncoderIndex, m.Field))
          .ToHashSet();

    var rows = new List<RotaryEncoderFieldState>();

    Add(-1, "steps_per_detent", pushed.StepsPerDetent.ToString(), readBack?.StepsPerDetent.ToString(), safety: false);

    for (int i = 0; i < RotaryEncoderDeviceConfig.EncoderCount; i++)
    {
      RotaryEncoderChannelConfig p = pushed.Encoders[i];
      RotaryEncoderChannelConfig? r = readBack?.Encoders[i];

      Add(i, "min_value", p.MinValue.ToString(), r?.MinValue.ToString(), safety: false);
      Add(i, "max_value", p.MaxValue.ToString(), r?.MaxValue.ToString(), safety: false);
      Add(i, "step_size", p.StepSize.ToString(), r?.StepSize.ToString(), safety: false);
      Add(i, "wrap", p.Wrap.ToString(), r?.Wrap.ToString(),
        safety: i == RotaryEncoderConfigDefaults.VolumeEncoderIndex);
      Add(i, "reverse", p.Reverse.ToString(), r?.Reverse.ToString(), safety: true);

      for (int t = 0; t < RotaryEncoderDeviceConfig.TiersPerEncoder; t++)
      {
        Add(i, $"tier{t + 1}_threshold_ms", p.Tiers[t].ThresholdMs.ToString(), r?.Tiers[t].ThresholdMs.ToString(), safety: false);
        Add(i, $"tier{t + 1}_multiplier", p.Tiers[t].Multiplier.ToString(), r?.Tiers[t].Multiplier.ToString(), safety: false);
      }
    }

    return rows;

    void Add(int index, string field, string designed, string? read, bool safety)
    {
      RotaryEncoderFieldAgreement agreement =
        readBack is null ? RotaryEncoderFieldAgreement.NotReadBack
        : differing.Contains((index, field)) ? RotaryEncoderFieldAgreement.Differs
        : RotaryEncoderFieldAgreement.Agrees;

      rows.Add(new RotaryEncoderFieldState(index, field, designed, read, safety, agreement));
    }
  }
```

⚠ **The safety flags in `Add` must stay identical to `RotaryEncoderConfigVerifier.Compare`'s** —
`wrap` is a safety field on VOLUME only; `reverse` is a safety field on every knob. Task 19 pins that
with a test that compares the two sets rather than trusting the duplication.

---

#### Task 8 — The four commands

**Why:** card 4. Each one writes under `_maintenanceLock` and reads back through Task 5's channel.

**Edit** `HidRotaryEncoderService` — declare `: IRotaryEncoderService, IRotaryEncoderProvisioning`
and implement:

```csharp
  /// <inheritdoc />
  public RotaryEncoderProvisioningSnapshot GetSnapshot()
  {
    lock (_snapshotGate)
    {
      return new RotaryEncoderProvisioningSnapshot
      {
        Enabled = _options.CurrentValue.Enabled,
        IsConnected = _isConnected,
        WasEverConnected = _everConnected,
        Status = ConfigStatus,
        LastVerifiedUtc = _lastVerifiedUtc,
        LastAttemptedUtc = _lastAttemptedUtc,
        LastSavedToDeviceUtc = _lastSavedToDeviceUtc,
        Flash = _flashState,
        Fields = ProjectFields(_lastPushed, _lastReadBack),
      };
    }
  }

  /// <inheritdoc />
  public async Task<RotaryEncoderProvisioningSnapshot> ReapplyAsync(CancellationToken ct = default)
  {
    await _maintenanceLock.WaitAsync(ct);
    try
    {
      HidStream? stream = _liveStream
        ?? throw new InvalidOperationException("The encoder is not connected.");

      // Deliberately the SAME method the boot path uses. A separate "maintenance push" would be a
      // second implementation of the one loop this row exists to make trustworthy.
      await ApplyConfigurationAsync(stream, RentReportBuffer(), ct);
      await RefreshFlashStateAsync(ct);
      return GetSnapshot();
    }
    finally
    {
      _maintenanceLock.Release();
    }
  }

  /// <inheritdoc />
  public async Task<RotaryEncoderProvisioningSnapshot> SaveToDeviceAsync(CancellationToken ct = default)
  {
    await _maintenanceLock.WaitAsync(ct);
    try
    {
      HidStream? stream = _liveStream
        ?? throw new InvalidOperationException("The encoder is not connected.");

      await ApplyConfigurationAsync(stream, RentReportBuffer(), ct);

      if (ConfigStatus != RotaryEncoderConfigStatus.Configured)
      {
        // Flash is left untouched on purpose. Writing an unverified configuration to flash would
        // persist exactly the state the read-back said we cannot trust, and it would do it to the
        // copy that runs during the next boot window before the app pushes.
        _logger.LogWarning(
          "Not writing encoder flash: the configuration did not verify (status {Status})", ConfigStatus);
        return GetSnapshot();
      }

      byte[] saveCommand = RotaryEncoderConfigCodec.EncodeCommand(RotaryEncoderCommand.SaveConfig);
      stream.Write(saveCommand, 0, saveCommand.Length);

      // The bytes recorded are the bytes just verified, taken from the same retained object the
      // Settings page renders. This is what makes the button's copy true (ENC-8 plan §0.5).
      RotaryEncoderDeviceConfig flashed;
      lock (_snapshotGate)
      {
        flashed = _lastPushed!;
      }

      await RecordFlashWriteAsync(flashed, ct);
      _logger.LogInformation("Encoder configuration written to device flash");
      return GetSnapshot();
    }
    finally
    {
      _maintenanceLock.Release();
    }
  }

  /// <inheritdoc />
  public async Task<bool> ResetCountersAsync(CancellationToken ct = default)
  {
    await _maintenanceLock.WaitAsync(ct);
    try
    {
      HidStream? stream = _liveStream;
      if (stream is null)
      {
        return false;
      }

      byte[] cmd = RotaryEncoderConfigCodec.EncodeCommand(RotaryEncoderCommand.ResetDiagnostics);
      stream.Write(cmd, 0, cmd.Length);

      // ⚠ Returns "the command was sent", NOT "the counters are zero". The protocol offers no
      // acknowledgement for 0x03/0x05 and this build has no diagnostics decoder (report 0x04 is
      // ENC-14), so there is nothing to verify against. The UI copy in Task 16 says exactly this
      // much and no more.
      _logger.LogInformation("Sent encoder counter-reset command");
      return true;
    }
    catch (Exception ex) when (ex is IOException or ObjectDisposedException)
    {
      _logger.LogWarning(ex, "Encoder counter-reset could not be sent");
      return false;
    }
    finally
    {
      _maintenanceLock.Release();
    }
  }

  /// <inheritdoc />
  public async Task<RotaryEncoderProvisioningSnapshot> SetReverseAsync(
    int encoderIndex, bool reverse, CancellationToken ct = default)
  {
    await _designedConfig.SetReverseAsync(encoderIndex, reverse, ct);
    // Push immediately, per handoff §7.8 card 3: "toggling one pushes immediately (0x02 + verify)".
    // A stored-but-unpushed direction is the same lie as an unverified push.
    return await ReapplyAsync(ct);
  }
```

Plus the flash bookkeeping (§2.4):

```csharp
  private DateTimeOffset? _lastSavedToDeviceUtc;
  private RotaryEncoderFlashState _flashState = RotaryEncoderFlashState.NeverSaved;

  /// <summary>
  /// Recomputes <see cref="_flashState"/> by comparing the stored hash of the last flashed bytes
  /// against a hash of the bytes the app would push right now.
  ///
  /// <para>
  /// A timestamp alone cannot support the words "differs from current design" — that is a claim
  /// about bytes, so bytes are what is compared. See ENC-8 plan §2.4.
  /// </para>
  /// </summary>
  private async Task RefreshFlashStateAsync(CancellationToken ct)
  {
    string? storedHash = await _designedConfig.GetLastSavedHashAsync(ct);
    DateTimeOffset? storedAt = await _designedConfig.GetLastSavedUtcAsync(ct);

    string currentHash = HashOf(await _designedConfig.ResolveAsync(ct));

    lock (_snapshotGate)
    {
      _lastSavedToDeviceUtc = storedAt;
      _flashState = storedHash is null
        ? RotaryEncoderFlashState.NeverSaved
        : string.Equals(storedHash, currentHash, StringComparison.Ordinal)
          ? RotaryEncoderFlashState.MatchesCurrentDesign
          : RotaryEncoderFlashState.DiffersFromCurrentDesign;
    }
  }

  private async Task RecordFlashWriteAsync(RotaryEncoderDeviceConfig flashed, CancellationToken ct)
  {
    await _designedConfig.RecordFlashWriteAsync(_timeProvider.GetUtcNow(), HashOf(flashed), ct);
    await RefreshFlashStateAsync(ct);
  }

  private static string HashOf(RotaryEncoderDeviceConfig config) =>
    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
      RotaryEncoderConfigCodec.Encode(config)));
```

Add the three matching store methods to `RotaryEncoderDesignedConfig` (`GetLastSavedHashAsync`,
`GetLastSavedUtcAsync`, `RecordFlashWriteAsync`) using the same
`IConfigurationManager.SetValueAsync(StoreId, key, value)` / `store.GetEntryAsync(key)` pattern as
Task 6, against `LastSavedUtcKey` and `LastSavedHashKey`. Timestamps go in as
`saved.ToString("O", CultureInfo.InvariantCulture)` and come back as:

```csharp
    // Round-trip format, invariant culture. This store is shared with the linux-arm64 Pi target and
    // read on a box whose locale is not guaranteed; a culture-sensitive parse here would read back a
    // different instant, and the value it feeds is a claim printed on the status card.
    ConfigurationEntry? entry = await store.GetEntryAsync(LastSavedUtcKey, ct: ct);
    if (entry is not null &&
        DateTimeOffset.TryParse(entry.Value, CultureInfo.InvariantCulture,
          DateTimeStyles.RoundtripKind, out DateTimeOffset parsed))
    {
      return parsed;
    }

    return null;
```

Call `RefreshFlashStateAsync` once after the boot push in `ReadFromDeviceAsync`, so the card is right
before anyone touches a button.

---

#### Task 9 — DI wiring

**Edit** `src/Radio.Infrastructure/DependencyInjection/AudioServiceExtensions.cs`, inside
`AddRotaryEncoders`. ⚠ **`ENC-4` is editing this exact block** — rebase onto its version.

```csharp
    // ENC-8. The designed configuration, with the owner's direction overrides layered on. Singleton
    // because it caches the overrides it read from the store; IConfigurationManager is optional for
    // the same reason AudioPreferencePersistence takes it optionally — tests and trimmed hosts run
    // without a store, and the designed directions are the safe fallback.
    services.AddSingleton<RotaryEncoderDesignedConfig>(sp => new RotaryEncoderDesignedConfig(
      sp.GetRequiredService<ILogger<RotaryEncoderDesignedConfig>>(),
      sp.GetService<Radio.Configuration.Abstractions.IConfigurationManager>()));

    // Second facet of the SAME instance, not a second service: provisioning needs the live HidStream
    // that only the reader owns. Registered separately so the input interface is not widened with
    // owner-initiated concerns.
    services.AddSingleton<IRotaryEncoderProvisioning>(sp => sp.GetRequiredService<HidRotaryEncoderService>());
```

**Test** — `tests/Radio.Infrastructure.Tests/DependencyInjection/`:

```csharp
[Fact]
public void ProvisioningAndInputResolveToTheSameEncoderInstance()
{
  // Two interfaces, one device. A second instance would open a second HID stream and both would
  // fight over the same endpoint.
  using ServiceProvider sp = BuildProvider();
  Assert.Same(sp.GetRequiredService<IRotaryEncoderService>(), sp.GetRequiredService<IRotaryEncoderProvisioning>());
}
```

---

### Phase 2 — API

#### Task 10 — Provisioning endpoints

**Edit** `src/Radio.API/Controllers/IntegrationsController.cs`. The existing
`GET api/integrations/encoder/status` (`:29-66`) **stays exactly as it is** — `SystemConfigPage`
already consumes it and nothing in this row needs it changed.

⚠ It returns an **anonymous object**, and the client `EncoderStatusDto` lives only in `Radio.Web`.
**Do not copy that pattern for the new endpoints** — return the `Radio.Core` record so the shape has
one definition on the server side, and mirror it once in `ApiModels.cs`.

```csharp
  /// <summary>
  /// Everything the encoder Settings surface renders: tier, timestamps, flash staleness, and the
  /// designed-vs-read-back value of every configurable field (ENC-8).
  /// </summary>
  [HttpGet("encoder/provisioning")]
  [ProducesResponseType(typeof(RotaryEncoderProvisioningSnapshot), StatusCodes.Status200OK)]
  public IActionResult GetEncoderProvisioning()
  {
    var provisioning = _serviceProvider.GetService<IRotaryEncoderProvisioning>();
    if (provisioning == null)
    {
      // The encoder subsystem is not registered in this host at all. Distinct from "disabled" and
      // from "not connected", and the page says so rather than showing an empty table.
      return Ok(new RotaryEncoderProvisioningSnapshot());
    }

    return Ok(provisioning.GetSnapshot());
  }

  /// <summary>Pushes the configuration and verifies it by read-back. Does not write flash.</summary>
  [HttpPost("encoder/reapply")]
  [ProducesResponseType(typeof(RotaryEncoderProvisioningSnapshot), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  public Task<IActionResult> ReapplyEncoderConfig(CancellationToken ct) =>
    RunProvisioningAsync(p => p.ReapplyAsync(ct));

  /// <summary>Pushes, verifies, then writes the verified bytes to the device's flash.</summary>
  [HttpPost("encoder/save")]
  [ProducesResponseType(typeof(RotaryEncoderProvisioningSnapshot), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  public Task<IActionResult> SaveEncoderConfigToDevice(CancellationToken ct) =>
    RunProvisioningAsync(p => p.SaveToDeviceAsync(ct));

  /// <summary>Sets one knob's direction override and pushes it immediately.</summary>
  [HttpPut("encoder/reverse/{index:int}")]
  [ProducesResponseType(typeof(RotaryEncoderProvisioningSnapshot), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  public Task<IActionResult> SetEncoderReverse(int index, [FromBody] SetEncoderReverseRequest request, CancellationToken ct)
  {
    if (index < 0 || index >= RotaryEncoderDeviceConfig.EncoderCount)
    {
      return Task.FromResult<IActionResult>(BadRequest(new { error = "Encoder index out of range" }));
    }

    return RunProvisioningAsync(p => p.SetReverseAsync(index, request.Reverse, ct));
  }

  /// <summary>Sends the device's counter-reset command.</summary>
  [HttpPost("encoder/reset-counters")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<IActionResult> ResetEncoderCounters(CancellationToken ct)
  {
    var provisioning = _serviceProvider.GetService<IRotaryEncoderProvisioning>();
    if (provisioning == null)
    {
      return Conflict(new { error = "The encoder subsystem is not available." });
    }

    // "sent", not "zeroed": the protocol has no acknowledgement for this command.
    bool sent = await provisioning.ResetCountersAsync(ct);
    return sent ? Ok(new { sent = true }) : Conflict(new { error = "The encoder is not connected." });
  }

  private async Task<IActionResult> RunProvisioningAsync(
    Func<IRotaryEncoderProvisioning, Task<RotaryEncoderProvisioningSnapshot>> operation)
  {
    var provisioning = _serviceProvider.GetService<IRotaryEncoderProvisioning>();
    if (provisioning == null)
    {
      return Conflict(new { error = "The encoder subsystem is not available." });
    }

    try
    {
      return Ok(await operation(provisioning));
    }
    catch (InvalidOperationException ex)
    {
      // Thrown when the device is not connected. 409 rather than 500: nothing failed, the hardware
      // is simply not there, and the page renders that differently.
      return Conflict(new { error = ex.Message });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Encoder provisioning operation failed");
      return StatusCode(500, new { error = "The encoder did not accept the request." });
    }
  }
```

Add the request record at the bottom of the file:

```csharp
/// <summary>Body of <c>PUT api/integrations/encoder/reverse/{index}</c>.</summary>
public sealed record SetEncoderReverseRequest(bool Reverse);
```

**Tests** — `tests/Radio.API.Tests/Controllers/IntegrationsControllerEncoderTests.cs`, following the
existing controller-test conventions in that project:

- `GetEncoderProvisioning_WithNoEncoderSubsystem_ReturnsAnEmptySnapshotRatherThan500`
- `Reapply_WhenTheDeviceIsNotConnected_Returns409NotAn500`
- `SetReverse_WithAnIndexOffTheFace_Returns400`
- `SetReverse_WithAValidIndex_CallsTheProvisioningServiceOnce`

---

#### Task 11 — The mapping endpoint

**Why:** §2.5. The router instance lives in the **API process**; the page runs in the Web process, so
the array has to travel over HTTP even though the type is visible to both.

**Edit** `IntegrationsController`:

```csharp
  /// <summary>
  /// What each knob currently does, read from the router's own dispatch table (ENC-8).
  ///
  /// <para>
  /// ⚠ This is the <b>software</b> mapping, and it is not the cabinet's engraved order. The two
  /// currently disagree on indices 1-3 — ENC-5 / ENC-7 own the remap. The Settings page renders both
  /// and states the disagreement; it does not pick one.
  /// </para>
  /// </summary>
  [HttpGet("encoder/mapping")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public IActionResult GetEncoderMapping()
  {
    var router = _serviceProvider.GetService<RotaryEncoderActionRouter>();
    if (router == null)
    {
      return Ok(Array.Empty<object>());
    }

    return Ok(router.Mapping.Select(m => new
    {
      m.EncoderIndex,
      CabinetName = RotaryEncoderCabinetNames.For(m.EncoderIndex),
      m.TurnDescription,
      m.PressDescription,
    }));
  }
```

`CabinetName` is included here, beside the software description, precisely so the page can compare
them without a second request and without hard-coding either order (§0.4 C-7).

---

### Phase 3 — Web

⚠ **Radzen, not MudBlazor** (§0.4 C-9). `src/Radio.Web` has no MudBlazor package and no MudBlazor
markup. Every card below matches the sibling integration cards on this page verbatim — the pattern is
`RadzenCard` → optional flex header row with a right-aligned `RadzenButton` → `RadzenRow` /
`RadzenColumn Size SizeMD` → Radzen inputs with `<small>` hints. This page uses inline `style=`
attributes and Radzen defaults almost exclusively and does **not** use `design-system.css`'s chip
vocabulary; do not introduce it here for four cards.

#### Task 12 — Web DTOs and the API client

**Edit** `src/Radio.Web/Models/ApiModels.cs`. ⚠ **`ENC-4` is appending `EncoderHudDto` to the tail of
this file** — rebase; append below it.

Mirror the `Radio.Core` records as classes, matching this file's existing DTO style (mutable
properties, no `record`, since the file's other DTOs deserialize that way):

```csharp
/// <summary>Whether the device agreed with one configured field (ENC-8). Mirrors <c>RotaryEncoderFieldAgreement</c>.</summary>
public enum EncoderFieldAgreementDto { NotReadBack = 0, Agrees = 1, Differs = 2 }

/// <summary>How the device's flash compares to what the app would push now (ENC-8). Mirrors <c>RotaryEncoderFlashState</c>.</summary>
public enum EncoderFlashStateDto { NeverSaved = 0, MatchesCurrentDesign = 1, DiffersFromCurrentDesign = 2 }

/// <summary>One configured field, as designed and as the device reports it (ENC-8).</summary>
public class EncoderFieldStateDto
{
  /// <summary>Encoder index, or -1 for the global <c>steps_per_detent</c>.</summary>
  public int EncoderIndex { get; set; }
  public string Field { get; set; } = "";
  public string DesignedValue { get; set; } = "";

  /// <summary>What the device reported. <b>Null means there has been no read-back — not agreement.</b></summary>
  public string? ReadBackValue { get; set; }
  public bool IsSafetyField { get; set; }
  public EncoderFieldAgreementDto Agreement { get; set; }
}

/// <summary>Payload of <c>GET /api/integrations/encoder/provisioning</c> (ENC-8).</summary>
public class EncoderProvisioningDto
{
  public bool Enabled { get; set; }
  public bool IsConnected { get; set; }
  public bool WasEverConnected { get; set; }

  /// <summary>Serialized name of <c>RotaryEncoderConfigStatus</c>: Unknown / Configured / Transient / Degraded / HardFault.</summary>
  public string Status { get; set; } = "Unknown";

  public DateTimeOffset? LastVerifiedUtc { get; set; }
  public DateTimeOffset? LastAttemptedUtc { get; set; }
  public DateTimeOffset? LastSavedToDeviceUtc { get; set; }
  public EncoderFlashStateDto Flash { get; set; }
  public List<EncoderFieldStateDto> Fields { get; set; } = [];
}

/// <summary>One row of <c>GET /api/integrations/encoder/mapping</c> (ENC-8).</summary>
public class EncoderMappingDto
{
  public int EncoderIndex { get; set; }

  /// <summary>The engraved name for this index — the cabinet's order, fixed by D2.</summary>
  public string CabinetName { get; set; } = "";

  /// <summary>What the router currently does on a turn. May not match <see cref="CabinetName"/> yet — see ENC-5 / ENC-7.</summary>
  public string TurnDescription { get; set; } = "";
  public string PressDescription { get; set; } = "";
}
```

⚠ `Status` is a **string**, not an enum, for the reason `EncoderHudDto.Phase` is a string
(`ApiModels.cs:1324-1349`): a newer API build sending an unknown tier must degrade to "show nothing
special" on a kiosk nobody is watching, rather than throw a deserialization exception. The two
`enum` DTOs above are closed sets defined in this same PR on both sides, so they carry no such risk.

**Edit** `src/Radio.Web/Services/ApiClients/IntegrationsApiService.cs` — add beside
`GetEncoderStatusAsync`, following its exact shape (`try` / `GetFromJsonAsync` / log + return null):

```csharp
  public async Task<EncoderProvisioningDto?> GetEncoderProvisioningAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<EncoderProvisioningDto>(
        "/api/integrations/encoder/provisioning", JsonOptions, cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get encoder provisioning state");
      return null;
    }
  }

  public async Task<List<EncoderMappingDto>?> GetEncoderMappingAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<List<EncoderMappingDto>>(
        "/api/integrations/encoder/mapping", JsonOptions, cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get encoder mapping");
      return null;
    }
  }

  /// <summary>
  /// Runs one provisioning command. Returns the resulting snapshot, or null when the request failed.
  ///
  /// <para>
  /// ⚠ Null means "we do not know the new state", not "nothing changed" — the caller must re-read
  /// rather than assume, and must not report success. A 409 (device not connected) lands here too.
  /// </para>
  /// </summary>
  private async Task<EncoderProvisioningDto?> PostProvisioningAsync(string path, CancellationToken cancellationToken)
  {
    try
    {
      HttpResponseMessage response = await _httpClient.PostAsync(path, content: null, cancellationToken);
      if (!response.IsSuccessStatusCode)
      {
        _logger.LogWarning("Encoder provisioning call {Path} returned {Status}", path, (int)response.StatusCode);
        return null;
      }

      return await response.Content.ReadFromJsonAsync<EncoderProvisioningDto>(JsonOptions, cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Encoder provisioning call {Path} failed", path);
      return null;
    }
  }

  public Task<EncoderProvisioningDto?> ReapplyEncoderConfigAsync(CancellationToken cancellationToken = default) =>
    PostProvisioningAsync("/api/integrations/encoder/reapply", cancellationToken);

  public Task<EncoderProvisioningDto?> SaveEncoderConfigToDeviceAsync(CancellationToken cancellationToken = default) =>
    PostProvisioningAsync("/api/integrations/encoder/save", cancellationToken);

  public async Task<bool> ResetEncoderCountersAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      HttpResponseMessage response = await _httpClient.PostAsync(
        "/api/integrations/encoder/reset-counters", content: null, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Encoder counter reset failed");
      return false;
    }
  }

  public async Task<EncoderProvisioningDto?> SetEncoderReverseAsync(
    int encoderIndex, bool reverse, CancellationToken cancellationToken = default)
  {
    try
    {
      HttpResponseMessage response = await _httpClient.PutAsJsonAsync(
        $"/api/integrations/encoder/reverse/{encoderIndex}", new { Reverse = reverse }, cancellationToken);
      if (!response.IsSuccessStatusCode)
      {
        _logger.LogWarning("Setting encoder {Index} direction returned {Status}", encoderIndex, (int)response.StatusCode);
        return null;
      }

      return await response.Content.ReadFromJsonAsync<EncoderProvisioningDto>(JsonOptions, cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Setting encoder {Index} direction failed", encoderIndex);
      return null;
    }
  }
```

---

#### Task 13 — Card 1: Status

**Edit** `SystemConfigPage.razor`, in the Rotary Encoders sub-tab (`:1433-1546`). Replace the body of
the existing **Encoder Status** card (`:1437-1477`) — keep the card, extend it. The Connection row and
the VID/PID row stay exactly as they are; three rows are added below them.

State fields — add beside `_encoderStatus` / `_encoderConfig` (`:1790-1795`):

```csharp
  private EncoderProvisioningDto? _encoderProvisioning;
  private List<EncoderMappingDto>? _encoderMapping;
  private System.Threading.Timer? _encoderPollTimer;
  private bool _encoderBusy;
```

Markup, appended inside the existing `<RadzenRow>` after the VID/PID column:

```razor
                  @if (_encoderProvisioning != null && _encoderProvisioning.Enabled)
                  {
                    <RadzenColumn Size="12" SizeMD="6">
                      <div style="display:flex; flex-direction:row; gap:8px; align-items:center">
                        <span><strong>Configuration:</strong></span>
                        <RadzenBadge Text="@EncoderTierText(_encoderProvisioning.Status)"
                                     BadgeStyle="@EncoderTierBadge(_encoderProvisioning.Status)" />
                        @if (_encoderProvisioning.LastVerifiedUtc is { } verified)
                        {
                          @* "verified" means a read-back MATCHED at this time. LastVerifiedUtc is set
                             nowhere else — see the ENC-8 plan §0.5. *@
                          <span style="font-size:0.75rem; color:var(--text-low)">
                            verified @verified.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
                          </span>
                        }
                        else
                        {
                          <span style="font-size:0.75rem; color:var(--text-low)">never verified</span>
                        }
                      </div>
                    </RadzenColumn>
                    <RadzenColumn Size="12">
                      <div style="display:flex; flex-direction:row; gap:8px; align-items:center">
                        <span><strong>Saved to device:</strong></span>
                        @if (_encoderProvisioning.LastSavedToDeviceUtc is { } saved)
                        {
                          <span>@saved.ToLocalTime().ToString("yyyy-MM-dd HH:mm")</span>
                        }
                        <span style="color:@EncoderFlashColor(_encoderProvisioning.Flash)">
                          @EncoderFlashText(_encoderProvisioning.Flash)
                        </span>
                      </div>
                    </RadzenColumn>
                  }
```

Helpers in `@code`:

```csharp
  // The four tiers of encoder handoff §7.6, rendered once. ENC-12's badge reads the same tier from
  // the same DTO; keeping this mapping in one place is what stops the page and the badge disagreeing
  // about what "Degraded" looks like.
  private static string EncoderTierText(string status) => status switch
  {
    "Configured" => "Configured",
    "Transient" => "Configuring…",
    "Degraded" => "Degraded",
    "HardFault" => "Safety fault",
    _ => "Unknown",
  };

  private static BadgeStyle EncoderTierBadge(string status) => status switch
  {
    "Configured" => BadgeStyle.Success,
    "Transient" => BadgeStyle.Light,
    "Degraded" => BadgeStyle.Warning,
    "HardFault" => BadgeStyle.Danger,
    _ => BadgeStyle.Light,
  };

  // Three states, all of them checkable. "never saved" is deliberately not a warning: on a fresh
  // install nothing is wrong, the flash simply has not been written by this app. See §2.4.
  private static string EncoderFlashText(EncoderFlashStateDto flash) => flash switch
  {
    EncoderFlashStateDto.MatchesCurrentDesign => "matches current design ✓",
    EncoderFlashStateDto.DiffersFromCurrentDesign => "differs from current design ⚠",
    _ => "never saved",
  };

  private static string EncoderFlashColor(EncoderFlashStateDto flash) => flash switch
  {
    EncoderFlashStateDto.MatchesCurrentDesign => "var(--signal-green)",
    EncoderFlashStateDto.DiffersFromCurrentDesign => "var(--signal-amber)",
    _ => "var(--text-low)",
  };
```

**Polling — 2 Hz, only while this tab is open.** Handoff §7.9 and `UI-2`: a background diagnostic
poll is exactly the incidental load that correlates with audible distortion on this box.

```csharp
  /// <summary>
  /// Starts the 2 Hz provisioning poll. <b>Only while the Rotary Encoders tab is showing</b> — encoder
  /// handoff §7.9, and the UI-2 load rule: incidental polling on this box correlates with audible
  /// audio distortion, so a background poll that runs whether or not anyone is looking is not
  /// acceptable here.
  /// </summary>
  private void StartEncoderPolling()
  {
    _encoderPollTimer?.Dispose();
    _encoderPollTimer = new System.Threading.Timer(_ =>
    {
      InvokeAsync(async () =>
      {
        _encoderProvisioning = await IntegrationsApi.GetEncoderProvisioningAsync();
        StateHasChanged();
      });
    }, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(500));
  }

  private void StopEncoderPolling()
  {
    _encoderPollTimer?.Dispose();
    _encoderPollTimer = null;
  }
```

Hook it to the **Integrations** tab's `Change` and the nested tab's `Change` so it starts when the
sub-tab is selected and stops when it is not, and call `StopEncoderPolling()` from the page's existing
`Dispose`. Load `_encoderMapping` once in `LoadIntegrationDataAsync` (`:3414-3428`) — the mapping does
not change at runtime, so it is not polled.

---

#### Task 14 — Card 2: Configuration, read-only, comparison always on

**Why:** handoff §7.8 card 2. This is what "have the configuration in the app" means — full
visibility, keyed to the engraving, with the device's agreement on every row.

**Insert** a new `RadzenCard` after the Status card. Six value columns per handoff §7.8's mock, one
row per encoder, labelled by cabinet name (Task 1) and never by index alone.

```razor
            <!-- Device configuration — read-only by design (handoff §7.8 card 2). These numbers are a
                 designed feel derived against the FM channel grid (§5.5) and a volume-safety budget
                 (§5.4), not preferences. The one editable field is Reverse, in its own card below. -->
            <RadzenCard>
              <h6 style="margin-bottom:12px">Device configuration</h6>
              @if (_encoderProvisioning == null)
              {
                <RadzenProgressBarCircular Size="ProgressBarCircularSize.Small" Mode="ProgressBarMode.Indeterminate" />
              }
              else if (_encoderProvisioning.Fields.Count == 0)
              {
                <span style="color:var(--text-low)">
                  No configuration has been pushed on this connection yet.
                </span>
              }
              else
              {
                <table class="rz-datatable" style="width:100%; max-width:900px">
                  <thead>
                    <tr>
                      <th>Knob</th><th>Step</th><th>Wrap</th><th>Direction</th>
                      <th>Tier 1</th><th>Tier 2</th><th>Tier 3</th><th>Device</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (int i = 0; i < 4; i++)
                    {
                      var idx = i;
                      <tr>
                        <td><strong>@EncoderCabinetName(idx)</strong> <span style="color:var(--text-low)">· knob @(idx + 1)</span></td>
                        <td>@FieldValue(idx, "step_size")</td>
                        <td>@FieldValue(idx, "wrap")</td>
                        <td>@(FieldValue(idx, "reverse") == "True" ? "Reversed" : "Normal")</td>
                        <td>@TierText(idx, 1)</td>
                        <td>@TierText(idx, 2)</td>
                        <td>@TierText(idx, 3)</td>
                        <td>@RowAgreementText(idx)</td>
                      </tr>
                    }
                  </tbody>
                </table>

                @* Fields the six columns above do not show — min_value, max_value, steps_per_detent —
                   are still compared by the verifier. A mismatch on one of them must not be invisible
                   just because the table has no column for it. *@
                @if (OffTableMismatches().Any())
                {
                  <div style="margin-top:8px; font-size:0.75rem; color:var(--signal-amber)">
                    Also disagreeing: @string.Join(", ", OffTableMismatches())
                  </div>
                }
              }
            </RadzenCard>
```

Helpers:

```csharp
  private static string EncoderCabinetName(int index) =>
    index switch { 0 => "VOLUME", 1 => "SOURCE", 2 => "PRESETS", 3 => "TUNING", _ => $"KNOB {index}" };

  private EncoderFieldStateDto? Field(int encoderIndex, string field) =>
    _encoderProvisioning?.Fields.FirstOrDefault(f => f.EncoderIndex == encoderIndex && f.Field == field);

  private string FieldValue(int encoderIndex, string field) => Field(encoderIndex, field)?.DesignedValue ?? "—";

  /// <summary>Renders one acceleration tier as the handoff writes it: <c>150×2</c>, or <c>off</c> when disabled.</summary>
  private string TierText(int encoderIndex, int tier)
  {
    string? ms = Field(encoderIndex, $"tier{tier}_threshold_ms")?.DesignedValue;
    string? mult = Field(encoderIndex, $"tier{tier}_multiplier")?.DesignedValue;
    return ms is null or "0" ? "off" : $"{ms}×{mult}";
  }

  /// <summary>
  /// Whether the device agreed with every field on this encoder.
  ///
  /// <para>
  /// ⚠ Three states, not two. <c>NotReadBack</c> must never render as agreement — "the device has not
  /// told us" and "the device agrees" are the distinction ENC-11 exists to preserve.
  /// </para>
  /// </summary>
  private string RowAgreementText(int encoderIndex)
  {
    var rows = _encoderProvisioning?.Fields.Where(f => f.EncoderIndex == encoderIndex).ToList() ?? [];
    if (rows.Count == 0 || rows.All(r => r.Agreement == EncoderFieldAgreementDto.NotReadBack))
    {
      return "— not read back";
    }

    var differing = rows.Where(r => r.Agreement == EncoderFieldAgreementDto.Differs).ToList();
    if (differing.Count == 0)
    {
      return "✓ agrees";
    }

    // Name the field and the value the device reported, not just "differs" — the whole point of
    // retaining the read-back is that "T3 reads 0" is actionable and "mismatch" is not.
    return "⚠ " + string.Join(", ", differing.Select(d => $"{d.Field} reads {d.ReadBackValue}"));
  }

  private IEnumerable<string> OffTableMismatches() =>
    (_encoderProvisioning?.Fields ?? [])
      .Where(f => f.Agreement == EncoderFieldAgreementDto.Differs)
      .Where(f => f.Field is "min_value" or "max_value" or "steps_per_detent")
      .Select(f => f.EncoderIndex < 0
        ? $"{f.Field} (reads {f.ReadBackValue})"
        : $"{EncoderCabinetName(f.EncoderIndex)} {f.Field} (reads {f.ReadBackValue})");
```

⚠ **`EncoderCabinetName` duplicates `RotaryEncoderCabinetNames.Ordered` across the process
boundary.** That is deliberate and bounded — the Web process cannot resolve the Core static at
runtime any more than it can resolve the router — but Task 19 pins the two against each other so a
future edit to the engraving cannot leave the page behind. **Do not "fix" it by removing one.**

---

#### Task 15 — Card 3: `Reverse direction`, the one editable thing

**Why:** handoff §7.8 card 3. *"It depends on how the cabinet was wired, not on taste, and a backwards
knob is intolerable and un-diagnosable by a normal person."* Toggling pushes immediately and marks the
flashed copy stale.

```razor
            <RadzenCard>
              <h6 style="margin-bottom:4px">Direction</h6>
              <small style="color:var(--text-low); display:block; margin-bottom:12px">
                Turn a knob clockwise and the value should go up. If one goes the wrong way, reverse it
                here — this depends on how that knob was wired, not on preference. Changing one sends it
                to the knobs straight away.
              </small>
              @if (_encoderProvisioning is { Enabled: true })
              {
                <RadzenRow>
                  @for (int i = 0; i < 4; i++)
                  {
                    var idx = i;
                    <RadzenColumn Size="6" SizeMD="3">
                      <div style="display:flex; align-items:center; gap:8px">
                        <RadzenSwitch Value="@(FieldValue(idx, "reverse") == "True")"
                                      Disabled="@(_encoderBusy || !_encoderProvisioning.IsConnected)"
                                      Change="@(async (bool v) => await SetEncoderReverseAsync(idx, v))" />
                        <span>@EncoderCabinetName(idx)</span>
                      </div>
                    </RadzenColumn>
                  }
                </RadzenRow>
              }
            </RadzenCard>
```

```csharp
  private async Task SetEncoderReverseAsync(int encoderIndex, bool reverse)
  {
    _encoderBusy = true;
    try
    {
      EncoderProvisioningDto? result = await IntegrationsApi.SetEncoderReverseAsync(encoderIndex, reverse);
      if (result == null)
      {
        // Deliberately not "failed to save": the request may have been rejected because the knobs are
        // unplugged, and the page cannot tell those apart from here. Say what is known.
        NotificationService.Notify(NotificationSeverity.Error, "Not applied",
          $"The {EncoderCabinetName(encoderIndex)} direction could not be sent to the knobs.");
        _encoderProvisioning = await IntegrationsApi.GetEncoderProvisioningAsync();
        return;
      }

      _encoderProvisioning = result;
      NotificationService.Notify(NotificationSeverity.Success, "Direction changed",
        $"{EncoderCabinetName(encoderIndex)} now turns {(reverse ? "the other way" : "the normal way")}.");
    }
    finally
    {
      _encoderBusy = false;
    }
  }
```

⚠ **The switch is bound with `Value` + `Change`, not `@bind-Value`.** The toggle's truth is the
device's read-back, not local state: if the push fails, the switch must snap back to what the device
actually has rather than sitting in the position the finger left it. `@bind-Value` would hold the
optimistic value and quietly lie — the same shape as `ENC-4a`'s mute chip, which deliberately does not
set `_isMuted` optimistically (`MainLayout.razor:1190-1201`).

---

#### Task 16 — Card 4: Actions

**Why:** handoff §7.8 card 4. **The `Save to device` copy is load-bearing** — §0.5.

```razor
            <RadzenCard>
              <h6 style="margin-bottom:12px">Actions</h6>
              <div style="display:flex; flex-direction:row; gap:12px; flex-wrap:wrap; align-items:flex-start">
                <div style="display:flex; flex-direction:column; gap:4px; max-width:320px">
                  <RadzenButton Variant="Variant.Filled" ButtonStyle="ButtonStyle.Primary" Icon="save"
                                Text="Save to device"
                                Disabled="@(_encoderBusy || _encoderProvisioning?.IsConnected != true)"
                                Click="SaveEncoderToDeviceAsync" />
                  @* Handoff §7.8, verbatim. It says what it writes, and because Rev 3 dropped the
                     "safe baseline" what it writes is what is on screen. If a future revision
                     reintroduces any divergence, this copy has to change with it and the reviewer
                     should treat a mismatch as a defect. *@
                  <small style="color:var(--text-low)">
                    Saves the settings above to the knobs so they work the same way even if the app is restarting.
                  </small>
                </div>
                <div style="display:flex; flex-direction:column; gap:4px; max-width:320px">
                  <RadzenButton Variant="Variant.Outlined" ButtonStyle="ButtonStyle.Primary" Icon="refresh"
                                Text="Re-apply settings"
                                Disabled="@(_encoderBusy || _encoderProvisioning?.IsConnected != true)"
                                Click="ReapplyEncoderAsync" />
                  <small style="color:var(--text-low)">
                    Sends the settings to the knobs again and checks they took. Does not change what is saved on the device.
                  </small>
                </div>
                <div style="display:flex; flex-direction:column; gap:4px; max-width:320px">
                  <RadzenButton Variant="Variant.Outlined" ButtonStyle="ButtonStyle.Light" Icon="restart_alt"
                                Text="Reset counters"
                                Disabled="@(_encoderBusy || _encoderProvisioning?.IsConnected != true)"
                                Click="ResetEncoderCountersAsync" />
                  <small style="color:var(--text-low)">
                    Zeroes the movement counters the knobs keep. Nothing on this page shows them yet.
                  </small>
                </div>
              </div>
            </RadzenCard>
```

⚠ **There is no factory-reset button and none may be added** (§0.3 item 1).

```csharp
  private async Task SaveEncoderToDeviceAsync()
  {
    _encoderBusy = true;
    try
    {
      EncoderProvisioningDto? result = await IntegrationsApi.SaveEncoderConfigToDeviceAsync();
      _encoderProvisioning = result ?? await IntegrationsApi.GetEncoderProvisioningAsync();

      if (_encoderProvisioning?.Flash == EncoderFlashStateDto.MatchesCurrentDesign)
      {
        NotificationService.Notify(NotificationSeverity.Success, "Saved to the knobs",
          "The knobs will keep these settings even while the app restarts.");
      }
      else
      {
        // The API leaves flash untouched when the push does not verify. Saying "saved" here would be
        // exactly the overclaim this row exists to stop.
        NotificationService.Notify(NotificationSeverity.Warning, "Not saved",
          "The knobs did not confirm the settings, so nothing was written to them. Try Re-apply settings first.");
      }
    }
    finally
    {
      _encoderBusy = false;
    }
  }

  private async Task ReapplyEncoderAsync()
  {
    _encoderBusy = true;
    try
    {
      EncoderProvisioningDto? result = await IntegrationsApi.ReapplyEncoderConfigAsync();
      _encoderProvisioning = result ?? await IntegrationsApi.GetEncoderProvisioningAsync();

      bool ok = _encoderProvisioning?.Status == "Configured";
      NotificationService.Notify(
        ok ? NotificationSeverity.Success : NotificationSeverity.Warning,
        ok ? "Settings applied" : "Not fully applied",
        ok
          ? "The knobs confirmed the settings."
          : "The knobs did not confirm every setting. The configuration table shows which.");
    }
    finally
    {
      _encoderBusy = false;
    }
  }

  private async Task ResetEncoderCountersAsync()
  {
    _encoderBusy = true;
    try
    {
      bool sent = await IntegrationsApi.ResetEncoderCountersAsync();
      // "sent", not "zeroed" — the protocol offers no acknowledgement for 0x03/0x05 and this build
      // has no diagnostics decoder to read the counters back. See §0.4 C-6.
      NotificationService.Notify(
        sent ? NotificationSeverity.Info : NotificationSeverity.Error,
        sent ? "Counter reset sent" : "Not sent",
        sent
          ? "Movement counters zeroed on the device."
          : "The knobs are not connected.");
    }
    finally
    {
      _encoderBusy = false;
    }
  }
```

---

#### Task 17 — The mapping card from the API, the card rename, and the deleted numeric

Three edits in the same sub-tab, grouped because they are one visual pass over it.

**(a) The Encoder Mapping card** (`:1480-1497`) — replace the four hard-coded `<tr>` rows
(`:1491-1494`) with the API projection. ⚠ **The text in those rows is currently correct** (§0.4 C-8);
this removes the second source of truth, it does not correct an error.

```razor
                <tbody>
                  @foreach (var m in _encoderMapping ?? [])
                  {
                    <tr>
                      <td>@(m.EncoderIndex) — @m.CabinetName</td>
                      <td>@m.TurnDescription</td>
                      <td>@m.PressDescription</td>
                    </tr>
                  }
                </tbody>
```

and, below the table, the self-erasing reconciliation note (§0.4 C-7). **It is computed, not
hard-coded**, so it disappears on its own the day `ENC-5`/`ENC-7` land:

```razor
              @* The engraved order and the software mapping genuinely disagree right now. Rendering
                 only one of them would assert something false, so the page states it. Computed from
                 the two orders rather than written down, so it vanishes by itself when ENC-5 / ENC-7
                 remap the router and never becomes a stale warning about a fixed problem. *@
              @if (MappingDisagreesWithCabinet())
              {
                <div style="margin-top:8px; font-size:0.75rem; color:var(--signal-amber)">
                  These are what the software does today. They do not all match the labels on the
                  cabinet yet — the SOURCE, PRESETS and TUNING knobs are still wired to the older
                  order in software. VOLUME is correct.
                </div>
              }
```

```csharp
  /// <summary>
  /// True while the router's index → action order differs from the cabinet's engraved order.
  ///
  /// <para>
  /// Detected by looking for the knob whose engraved name is TUNING being described as something
  /// other than tuning, rather than by a hard-coded flag — a flag would still be there, and still be
  /// shown, after ENC-5 / ENC-7 fix the underlying mismatch.
  /// </para>
  /// </summary>
  private bool MappingDisagreesWithCabinet() =>
    (_encoderMapping ?? []).Any(m =>
      string.Equals(m.CabinetName, "TUNING", StringComparison.Ordinal) &&
      !m.TurnDescription.Contains("Tune", StringComparison.OrdinalIgnoreCase));
```

**(b) Rename the existing Configuration card** (`:1500-1544`) → **`Connection settings`**, and its
button `Save` → `Save connection settings` (§0.4 C-10). Two "Save" buttons on one tab, one writing app
config and one writing device flash, is the ambiguity this row exists to remove. The existing
`SaveEncoderConfigAsync` (`:3430-3455`) and its *"Restart required for changes to take effect"* toast
are **unchanged and still true** — those settings are read at startup.

**(c) Delete the `VolumeStepPercent` editor** — remove the `RadzenColumn` at
`SystemConfigPage.razor:1532-1534` in its entirety.

⛔ **Delete the editor only.** Keep `RotaryEncoderOptions.VolumeStepPercent` (`:41`),
`RotaryEncoderConfigDto.VolumeStepPercent` (`ApiModels.cs:949`) and the `appsettings.json` entry
(`:255`) — `RotaryEncoderActionRouter.cs:270` reads it on every detent. It now appears **read-only in
card 2** as VOLUME's `step_size`, which is the relocation handoff §7.8 asks for: *one value, one
place, visible.* The duplicate editable box was a second source of truth for a number the device also
holds.

---

#### Task 18 — CSS

**Almost none, deliberately.** This page uses inline `style=` attributes and Radzen defaults and does
not participate in `design-system.css`'s chip vocabulary; four cards is not the moment to change that.

The only addition, in `src/Radio.Web/wwwroot/css/design-system.css`, is to keep the 8-column
configuration table readable at **1920 × 720** inside the nested tab:

```css
/* ENC-8 — the device configuration table. Eight columns inside a nested RadzenTabs panel at
   1920x720; the knob-name column carries two words plus an index and must not wrap, and the
   agreement column carries a sentence and must be allowed to. */
.encoder-config-table td:first-child { white-space: nowrap; }
.encoder-config-table td:last-child { font-family: var(--font-mono); font-size: 11px; }
```

Add `encoder-config-table` alongside `rz-datatable` on the card 2 table.

⛔ **No new design tokens** (§0.3 item 6), and **do not reference `--signal-red-glow`** — it is
consumed at `design-system.css:5364` and never declared (§0.4 C-11).

---

### Phase 4 — tests and docs

#### Task 19 — Tests

**`tests/Radio.Infrastructure.Tests`** — beyond the per-task tests already specified in Tasks 1, 3,
5, 6 and 9, add the four that pin this row's promises:

```csharp
[Fact]
public void SaveWritesExactlyWhatTheScreenShows()
{
  // ENC-8 §0.5, and the one assertion a reviewer is told to treat as a defect if it goes missing.
  // The snapshot the page renders and the bytes the push wrote must come from one object.
  var designed = new RotaryEncoderDesignedConfig(NullLogger<RotaryEncoderDesignedConfig>.Instance);
  RotaryEncoderDeviceConfig resolved = designed.ResolveAsync().GetAwaiter().GetResult();

  byte[] whatWouldBeFlashed = RotaryEncoderConfigCodec.Encode(resolved);
  byte[] whatWouldBePushed = RotaryEncoderConfigCodec.Encode(resolved);

  Assert.Equal(whatWouldBeFlashed, whatWouldBePushed);
  // And the screen reads the same object: every field the projection emits must be findable in it.
  Assert.Equal("2", FieldOf(resolved, encoderIndex: 0, "step_size"));
}

[Fact]
public void ProjectedSafetyFlags_MatchTheVerifiersOwnClassification()
{
  // Two places decide what a "safety field" is: RotaryEncoderConfigVerifier.Compare and the
  // projection the page renders. They must not drift — a field shown as ordinary while the clamp
  // treats it as a safety field would make the page lie about why volume is limited.
  RotaryEncoderDeviceConfig designed = RotaryEncoderConfigDefaults.Create();
  RotaryEncoderDeviceConfig mutated = CloneWithEverythingDifferent(designed);

  var verifierSafety = RotaryEncoderConfigVerifier.Compare(designed, mutated)
    .Where(m => m.IsSafetyField).Select(m => (m.EncoderIndex, m.Field)).ToHashSet();
  var projectedSafety = ProjectFieldsForTest(designed, mutated)
    .Where(f => f.IsSafetyField).Select(f => (f.EncoderIndex, f.Field)).ToHashSet();

  Assert.Equal(verifierSafety, projectedSafety);
}

[Fact]
public void NoCodePathSendsFactoryReset()
{
  // §0.3 item 1. RotaryEncoderCommand.ResetDefaults wipes the flashed configuration and leaves the
  // device on defaults where one volume detent spans the full range. It exists in the enum; nothing
  // in this repository may send it.
  string source = File.ReadAllText(PathToHidRotaryEncoderService);
  Assert.DoesNotContain("RotaryEncoderCommand.ResetDefaults", source, StringComparison.Ordinal);
}

[Fact]
public void FlashStaleness_IsDecidedByComparingBytes_NotByAgeing()
{
  // §2.4. "differs from current design" is a claim about bytes.
  string hashA = HashOfForTest(RotaryEncoderConfigDefaults.Create());
  RotaryEncoderDeviceConfig changed = RotaryEncoderConfigDefaults.Create();
  changed.Encoders[1].Reverse = true;

  Assert.NotEqual(hashA, HashOfForTest(changed));
}
```

**`tests/Radio.Web.Tests/Components/Pages/SystemConfigPageTests.cs`** — extend the existing fixture.
It already does `Services.AddHermeticTestRig()`, `Services.AddRadzenComponents()` and
`JSInterop.Mode = JSRuntimeMode.Loose`; keep all three. Under the rig every API call fails, so these
assert the **degraded** rendering, which is the state a kiosk actually hits when `radio-api` is down:

- `EncoderTab_WithNoProvisioningData_DoesNotClaimTheDeviceAgrees` — the configuration card renders
  the "no configuration has been pushed" line and the markup contains no `✓`.
- `EncoderTab_HasNoFactoryResetAffordance` — the rendered markup contains no "factory", no "Reset to
  defaults", and no "Restore".
- `EncoderTab_HasExactlyOneSaveToDeviceButton_AndItsCopyIsTheHandoffCopy` — asserts the literal
  string *"Saves the settings above to the knobs so they work the same way even if the app is
  restarting."* is present. **This is the §0.5 copy pinned in a test**, so a silent reword fails CI.
- `EncoderTab_NoLongerOffersAnEditableVolumeStepPercent` — markup does not contain
  `Volume Step (%)`.
- `CabinetNamesOnThePage_MatchTheCoreDefinition` — pins the duplicated names from Task 14 against
  `RotaryEncoderCabinetNames.Ordered`.

⚠ **`MainLayoutTests` is a documented stub that renders nothing** (`tests/Radio.Web.Tests/Components/Layout/MainLayoutTests.cs`)
— Radzen + JSInterop make the layout impractical to render in bUnit. That does not affect this row
(everything here is on a page bUnit already renders), but it is the constraint that shapes `ENC-12`.

**`tests/Radio.API.Tests`** — the four controller tests listed in Task 10.

---

#### Task 20 — Docs

Per the repo's per-PR docs rule. **Five of these are corrections of statements that are false today**,
found while planning (§0.4), not cosmetic touch-ups.

**`design/INTEGRATIONS.md`:**

1. **`:5`** — *"All integrations are **disabled by default** and opt-in via configuration."* → false
   since `ENC-0`. Rewrite so it is true of each integration rather than asserting one rule for all
   three: the encoders now default **on** and are decided by **presence**;
   `RotaryEncoder:Enabled` is an escape hatch, not a gate. (§0.4 C-5)
2. **`:22-26`** — the 8-byte report format, superseded by `ENC-1`. Replace with the real protocol,
   which is already documented precisely in two places to copy from: `RotaryEncoderDecoder.cs:20-26`
   (report `0x01`, 36-byte payload, positions at 0-15, buttons at 16, movement accumulators at 20-35,
   plus the 21-byte legacy form) and `RotaryEncoderConfigCodec.cs:22-36` (report `0x02` config, 106-byte
   payload; report `0x03` commands). **State that the first report after every connect is a baseline
   and is discarded** — it is the rule that stops a replug delivering forty detents to the volume knob.
3. **`:28-35`** — the Encoder Mapping table. Note that the app now serves this from the router and
   point at System Config → Integrations → Rotary Encoders rather than repeating it here; a third copy
   of a table this row just deduplicated would be the same defect in a new file.
4. **`:80` and `:95`** — the JSON sample (`"TuningStepKHz": 10,`) and the settings-table row
   (`| TuningStepKHz | Radio frequency step per click in kHz | 10 |`) still document a field PR #490
   deleted from every code path. Remove both. (§0.4 C-3)
   While there: **`:88`** claims `| Enabled | Master switch for the encoder service | **false** |` —
   the default is **`true`** since `ENC-0` and it is an escape hatch, not a master switch, so that row
   is wrong in both its value and its description (the same defect as item 1, in table form). And
   **`:79` / `:94`** describe `VolumeStepPercent` as an editable setting; it stays in the file as a
   configuration field but the description should say it is shown **read-only** on the Settings page
   as VOLUME's step size, because its editable box is deleted in Task 17c.

   > ⚠ **Verified directly on 2026-09-02, because a search tool disagreed.** One automated sweep
   > reported `design/INTEGRATIONS.md` as already clean of `TuningStepKHz`; reading the file shows it
   > is not. **Read the lines before editing them.** Stale copies under `.claude/worktrees/` and
   > `bin/` confuse path-based searching here — `git grep` is the reliable command (§0.4 C-3).
5. **`:125`** — *"swap the A/B encoder pins on the Pico, or negate the delta in firmware"* →
   superseded by the `reverse` config field, which is now a toggle in the UI. Replace with: open
   System Config → Integrations → Rotary Encoders and toggle Direction for that knob. **Do not leave
   the pin-swap advice as a fallback** — following it now would produce a knob that is reversed twice.
6. **`:120`** — still accurate, but extend to mention the configuration tier row the same card now
   carries.

**`CLAUDE.md`** — the Solution Structure line describing `src/Radio.Web` as *"Blazor Server UI
(MudBlazor Material 3)"*. **There is no MudBlazor in this repository** (§0.4 C-9); it is Radzen. This
sends every new session's UI work to the wrong component library.

**`design/FUTURE-WORK.md`** — three entries: the `--signal-red-glow` dangling token (§0.4 C-11); the
`Restore designed defaults` action deliberately not built and what it would mean if the owner wants it
(§0.4 C-2); and the fact that `Reset counters` has no on-screen effect until `ENC-14` (§0.4 C-6).

**`design/WORK-LOG.md`** — one entry in the file's existing form, above the marker comment.

**`docs/HANDOFF-GA-PUNCH-LIST.md`** — mark `ENC-8` shipped with the PR number. Correct the `ENC-8`
row's two stale claims in the same edit: the mapping table no longer contradicts the router, and the
report-format citation is `INTEGRATIONS.md:22-26`.

**`docs/BUILDER_QUEUE.md`** — flip the `ENC-8` row to ✅ with the PR link; refresh the banner.

**`docs/HANDOFF-NEXT-SESSION.md`** — point "Start here" at `ENC-12`.

---

## 4. Test Plan

### 4.1 Automated gates

```bash
dotnet build --configuration Release          # 0 warnings — warnings are errors in Release
dotnet test  --configuration Release --verbosity normal
```

Targeted while iterating:

```bash
dotnet test --filter "FullyQualifiedName~RotaryEncoder"
dotnet test --filter "FullyQualifiedName~SystemConfigPageTests"
dotnet test --filter "FullyQualifiedName~IntegrationsControllerEncoderTests"
```

⚠ Two known-flaky families in this suite are **not** this row's regressions: the `AudioApiService`
timeout tests (`_WhenServerNotAvailable`) and anything that flakes under load. Re-run before
investigating.

### 4.2 Deploy

```powershell
./deploy/Deploy-ToLinux.ps1        # no flags: OPS-1 fixed the defaults to radio / linux-x64
```

Then confirm both SHAs actually landed — the deploy exits non-zero on a mismatch, but check anyway:

```bash
curl -s http://radio:5000/api/health/version
curl -s http://radio:5002/api/health/version
```

⚠ **Read logs from the file sink, not `journalctl`.** Since `LOG-11` the journal carries **WARNING and
above only**, so the `Information` lines this row emits (*"Encoder configuration applied and verified"*,
*"Encoder configuration written to device flash"*) will look like they never happened:

```bash
ssh mmack@radio 'F=$(ls -t /opt/radio-console/logs/radio-*.txt | head -1); tail -100 $F'
```

⚠ **Keep log reads bounded and infrequent while testing.** Heavy `journalctl`/SSH activity on this
N100 correlates with audible audio distortion, and this row's own polling is deliberately bounded for
the same reason.

### 4.3 Browser UAT — Tester drives these at 1920 × 720

Navigate to **`http://radio:5002/system` → Integrations → Rotary Encoders**. The encoder must be
plugged in for T3–T10.

| # | Steps | Expected |
|---|---|---|
| **T1** | Open the tab with the encoder connected and healthy. | Connection `Connected`. Configuration `Configured` with a `verified <timestamp>` beside it. Five cards visible: Status, Device configuration, Direction, Actions, Connection settings. **No horizontal scrollbar on the page body.** |
| **T2** | Read the Device configuration table. | Four rows labelled **VOLUME · SOURCE · PRESETS · TUNING** (never bare indices). VOLUME reads step `2`, wrap `False`, direction `Normal`, `150×2` / `80×3` / `off`. SOURCE and PRESETS read `off` on all three tiers. TUNING reads `150×2` / `80×4` / `40×8`. Device column reads `✓ agrees` on all four. |
| **T3** | Look at the Encoder Mapping card. | Rows are rendered from the API, and the amber note beneath says the software order does not yet match the cabinet labels and that VOLUME is correct. |
| **T4** | Click **Re-apply settings**. | A success toast *"The knobs confirmed the settings."*; the `verified` timestamp advances to now. The knobs keep working throughout — turn VOLUME during and after. |
| **T5** | Click **Save to device**. | Toast *"Saved to the knobs — the knobs will keep these settings even while the app restarts."* The `Saved to device:` row gains a timestamp and reads **`matches current design ✓`**. |
| **T6** | Toggle **Direction → PRESETS** on. | Success toast naming PRESETS. Card 2's PRESETS row now reads `Reversed`, the Device column still reads `✓ agrees`, **the tier badge stays `Configured`** and volume still moves normally. ⚠ **This is the trap in §2.3** — if the tier flips to `Safety fault` and the volume knob goes sluggish, the override is not reaching the verifier and the row is not shippable. |
| **T7** | With PRESETS still reversed, look at card 1. | `Saved to device:` now reads **`differs from current design ⚠`** — the flash holds the pre-toggle bytes. |
| **T8** | Click **Save to device** again, then restart the API (`ssh mmack@radio 'sudo systemctl restart radio-api'`) and reopen the tab. | Back to `matches current design ✓`, and the timestamp survived the restart. **This is what proves the staleness comparison is stored rather than inferred.** |
| **T9** | Toggle PRESETS direction back off, click **Save to device**. | Returns to `Normal` / `matches current design ✓`. Leave the cabinet in this state. |
| **T10** | Click **Reset counters**. | Info toast *"Movement counters zeroed on the device."* — **and nothing else changes on screen**, which is correct and is what the button's own hint text says. |
| **T11** | Unplug the encoder's USB lead. Wait for Connection to read `Disconnected`. | All four action buttons and all four Direction switches become disabled. Card 2 still renders the designed values, and its Device column reads **`— not read back`** — never `✓`. |
| **T12** | While unplugged, confirm no button can be clicked; then replug. | Within a few seconds Connection returns to `Connected` and Configuration returns to `Configured`. Volume does **not** jump when you replug, even after turning the knob while it was unplugged. |
| **T13** | Search the whole tab for a factory-reset affordance. | **There is none** — no "factory", no "restore defaults", no advanced disclosure containing one. |
| **T14** | Look at the Connection settings card. | Its button reads **`Save connection settings`**, not `Save`. There is **no** `Volume Step (%)` input. There is no `Tuning Step (kHz)` input. |
| **T15** | Navigate away from the Integrations tab and leave the page on another tab for two minutes, watching the API log. | **No further `encoder/provisioning` requests.** The 2 Hz poll runs only while the encoder tab is showing (handoff §7.9 / `UI-2`). |
| **T16** | Stop `radio-api` (`sudo systemctl stop radio-api`), reload `/system`, open the tab. | The tab renders without throwing. Nothing claims the device agrees; nothing claims a save succeeded. Restart the API afterwards. |

### 4.4 The four highest-weighted checks

1. **T6** — a reversed knob must stay `Configured`. This is the one place where a feature working as
   designed could permanently cripple the volume control (§2.3). If it fails, the row is not
   shippable regardless of what else passes.
2. **T8** — staleness survives a restart. If it does not, `matches current design ✓` is a claim the
   code cannot support and §0.5 is broken.
3. **T5 / T10 copy** — the words on screen must be exactly what happened. `Save to device` says what
   it writes; `Reset counters` says counters were zeroed and nothing more.
4. **T11** — `— not read back` must never render as `✓`. "The device has not told us" and "the device
   agrees" are the distinction `ENC-11` exists to preserve, and collapsing them on screen would undo
   it at the last hop.

---

## 5. Self-review

**Spec coverage — handoff §7.8's four cards:** Status → Task 13. Configuration read-only with
comparison always on → Task 14. Four `Reverse` toggles that push immediately and mark flash stale →
Task 15 (+ Task 8's `SetReverseAsync`). Save / Re-apply / Reset-counters → Task 16. Factory reset
absent and pinned absent → §0.3 item 1, Task 19. `TuningStepKHz` → confirmed already deleted from code,
removed from docs (Task 20). `VolumeStepPercent` → editor deleted, value relocated read-only into card
2 (Tasks 17c, 14). Mapping served from the router → Tasks 3, 11, 17a. `INTEGRATIONS.md` report format
and A/B-pin advice → Task 20.

**Placeholders:** none. Every code block is literal. No task says "similar to Task N"; the two that
reuse a shape (`ReapplyAsync` / `SaveToDeviceAsync`) are both written out.

**Type consistency:** `RotaryEncoderConfigStatus` crosses the wire as a **string** and is compared
against literals `"Configured"` / `"Degraded"` / `"HardFault"` in exactly two places (Tasks 13, 16),
matching the `EncoderHudDto.Phase` precedent. `RotaryEncoderFieldAgreement` and
`RotaryEncoderFlashState` cross as **ints via closed enums** defined on both sides in this PR.
`DateTimeOffset?` throughout; rendered with `.ToLocalTime()` at every display site.

**Load:** one 2 Hz poll, only while one sub-tab is visible, stopped on tab change and on dispose. No
new background service, no new SignalR traffic. `ENC-12` adds one push event and no poll.

**Assertions this plan makes the code print, and where each is checked:**

| On-screen claim | Checked by |
|---|---|
| `verified <time>` | `_lastVerifiedUtc` set only in the `Configured` branch (Task 7) |
| `✓ agrees` | `RotaryEncoderConfigVerifier.Compare` returned no mismatch for that encoder (Task 7) |
| `— not read back` | `_lastReadBack is null` (Task 7, Task 14) |
| `matches current design ✓` | SHA-256 of the flashed bytes equals SHA-256 of the resolved bytes (Task 8, §2.4) |
| `Saves the settings above to the knobs…` | screen, push and flash read one object from one call (Tasks 6, 8); pinned by tests in Task 19 |
| `Movement counters zeroed on the device.` | the command was sent; **no stronger claim is made**, because the protocol has no acknowledgement (Task 8, §0.4 C-6) |
| the mapping-vs-cabinet note | computed from the two orders at render time (Task 17a) |

**Rebase surface — `ENC-4` is in flight and touches five of the same files:**
`RotaryEncoderActionRouter.cs` (Task 3), `AudioServiceExtensions.cs` `AddRotaryEncoders` (Task 9),
`ApiModels.cs` tail (Task 12), plus `AudioStateUpdateService.cs` and `AudioStateHubService.cs` which
this row does **not** touch. `ENC-5`/`ENC-7` are being planned in parallel and own the router remap —
Task 3 is shaped so that lands as an edit to one array (§2.5).

---

## 6. Things this plan deliberately does not do, with the reason

1. **`Restore designed defaults`.** §0.4 C-2 — Rev 3 removed the 24 numerics it would have undone, the
   undo for a toggle is the toggle, and a button saying "defaults" on this page is one misread from the
   factory reset that is deliberately absent.
2. **The Diagnostics card and `Calibrate a knob`.** Handoff §7.9, and they are `ENC-14`. `Reset
   counters` is built here because §7.8 puts it here, and its copy is written so it does not imply a
   card that does not exist.
3. **Editing any field except `reverse`.** §0.3 item 2. Handoff §13 Q3 leaves that with the owner, and
   Designer's recommendation is against it.
4. **The router remap.** §0.3 item 4 — `ENC-5` / `ENC-7`.
5. **A shared `NotificationService` wrapper.** This row adds several toasts and `ENC-12` adds one more,
   which makes a helper tempting. But `RadioControlPanel.razor:1366` is the only `new NotificationMessage`
   in the tree today and every other site uses the three-argument overload; extracting a wrapper now
   would touch twelve files across two rows for no behavioural gain. Recorded in
   `design/FUTURE-WORK.md` instead.
6. **Fixing `--signal-red-glow`.** §0.4 C-11 — pre-existing, and declaring it would silently change an
   unrelated shipped component's appearance.

---

## 7. Task 4 result — 2026-09-02 — **PASS**, and it caught a HIGH regression

**Gate question:** does a `stream.Write` from an ASP.NET request thread reach the device while
`ReadFromDeviceAsync` is parked in an infinite `ReadAsync`, and does the device's reply arrive on the
read loop?

**Answer: yes, both halves.** Run on the appliance at `122bf0b`, encoder connected, **no knob being
touched**, `POST /api/integrations/encoder/reapply`:

```
"status":"Configured", "lastVerifiedUtc":"2026-09-02T21:31:28.812933+00:00",
fields ... "readBackValue":"4" ... "agreement":"Agrees"
```

Real read-back values, not a timeout treated as agreement. The maintenance channel of §2.2 works as
designed: the write is what makes the device speak, the reply wakes the blocked `ReadAsync`, and
`ParseReport` hands it back through the `TaskCompletionSource`. **No change to `ReadTimeout` was
needed and none was made.**

### What the gate caught, which is why it is a gate

The **same deploy** showed the **boot** push failing:

```
boot:          "status":"Degraded", "lastVerifiedUtc":null, every field "agreement":"NotReadBack"
POST reapply:  "status":"Configured", all 45 fields "agreement":"Agrees"
```

Identical code, differing only in whether a reader was running. Task 5 moved
`ApplyConfigurationAsync`'s read-back onto the maintenance channel to get *one* read-back path — but
the boot push runs **before** the read loop starts, so the waiter had no completer. The loop cannot
start until the push returns; the push cannot return until the loop answers it. `ENC-11` survived the
same ordering only because its push owned an inline `stream.ReadAsync`, which Task 5 deletes.

It does not hang, which is what makes it easy to miss — it times out on all four attempts (~8 s) and
settles in **`Degraded`**, the tier that drops `VolumeClampFor` from 6 units per event to 2. **Every
boot would have left the volume knob sluggish inside sealed furniture, with nobody there to press
Re-apply** — the row built to make that failure diagnosable would have been shipping it.

**Fixed** in `1486fdb`: the push now runs concurrently with the read loop
(`RunBootConfigurationPushAsync`), still under `_maintenanceLock`, and is awaited in the loop's
`finally` so a fault cannot be swallowed. Re-verified on the appliance at `1486fdb`:

```
status : Configured    verifiedUtc : 2026-09-02T21:34:22    agreements : {'Agrees': 45}
```

**Lesson for the plan, not just the code:** §2.2's "one read-back path, not a boot path and a
maintenance path that can drift" is a good instinct with one unstated precondition — *there must be a
reader*. Unifying the two paths is only safe once the boot push is inside the reader's lifetime.

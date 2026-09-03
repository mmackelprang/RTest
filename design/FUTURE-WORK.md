# Future Work — Stubbed & Unimplemented Features

This document catalogs features that have been designed at the interface level but not yet fully implemented. Each entry includes the rationale for the stub, the platform APIs needed to complete it, and any known gotchas.

> **Rule:** When a feature is stubbed rather than implemented, it must be documented here with enough context for a future developer (or LLM) to pick it up without re-researching the problem.

---

## Encoder HUD (ENC-4) — three seams left open on purpose, of which ONE is still open

> Seam **1** was closed by `ENC-7`, seam **3** by the owner's decision `D27`, and the `ENC-5`/`ENC-7`
> warning below was discharged by `ENC-4c`. **Only seam 2 (no exit animation) remains.** The closed ones
> are struck through rather than deleted, because each is a decision a later session would otherwise
> re-derive and possibly reverse.

**1. ~~The PRESETS long-press consumer is not wired.~~ CLOSED by `ENC-7`.** Both long-press actions the design
names now exist — VOLUME → Standby and PRESETS → Save — and there is deliberately no third. `OnLongPress`
switches on indices 0 and 2; `PublishHold` publishes for those two only, and for index 2 only on the `HoldStart`
edge — and only while the PRESETS overlay is **not already open**, because a hold phase is not a selector phase,
so a card published over the open list would replace it with a label-only card for the whole of every press. The
save's own `SelectorNotice` is what collapses the ring. Indices 1 and 3 still publish no hold phase at all, for
the original reason: a progress ring that fills and then does nothing is a promise the code does not keep.

**2. There is no exit animation.** The card enters with `.snackbar-enter` and then simply unmounts. A 200 ms exit
needs a two-phase teardown (keep the element alive while it animates out), which changes `EncoderHudService`'s
contract and would be inherited by the `ENC-5`/`ENC-7` overlays. Judged not worth a state machine: the enter is the
perceptually load-bearing half.

**3. ~~`prefers-reduced-motion` keeps the ring sweeping — an open question for the owner.~~ ✅ CLOSED by `D27`,
2026-09-02 — the owner ratified the shipped behaviour.** Handoff §6.5 asked that the ring *"become a filling bar"*
under reduced motion. It keeps sweeping, because freezing it hides the only indication that a 600 ms hold is arming
standby — and the plan's own draft CSS (`animation-name: none`) would not have produced a filling bar either, since
it freezes `--ring-turn` at `0turn`, i.e. an empty ring.

⛔ **Do NOT re-open this, and do not "restore" §6.5 on a later consistency pass.** Punch list §6 records it as
**WON'T DO — owner-accepted deviation, D27**, and §6.5 has been amended to describe what actually ships. This is a
*declared* deviation from an accessibility line item, not drift. The entry is kept, struck through rather than
deleted, because an empty space here is what invites the next session to re-derive the question and re-ask a closed
one.

**Also unverified rather than stubbed:** the encoder-disconnect → `Dismiss()` wiring is confirmed by inspection
only. `AudioStateHubService.EncoderConnectionChanged` is a field-like event, so a unit test cannot raise it, and
unplugging USB is not reachable remotely.

**✅ ~~`ENC-5`/`ENC-7` must deal with this before they add a phase.~~ DONE by `ENC-4c`
([#526](https://github.com/mmackelprang/RTest/pull/526)) — the instruction outlived the work.** *The problem, kept
for the record:* `EncoderHudService.Publish` used to map an unrecognised phase to `_ => IsHolding`, i.e. it
*preserved* the current hold state, and a true `IsHolding` cancels the dismissal timer — so `HoldStart` → unknown
phase → `Value` left the card up with a ring and **no timer to take it down**, on a kiosk nobody is watching. It was
unreachable while both builds knew the same four phase names, but `ENC-5`/`ENC-7` added phases and API-ahead-of-Web
skew is an ordinary deploy-window state.

**What `ENC-4c` actually shipped** (`src/Radio.Web/Services/EncoderHudService.cs:140-146`) is the recommended
resolution: four explicit arms, `_ => false`, under the comment *"Handoff 6.10 — four explicit arms, and the fourth
one is the point."* ⚠ **`"Value" => IsHolding` is deliberately its own arm and must stay one** — turning the knob
mid-hold publishes a `Value` card and the ring has to keep drawing through it, so the obvious edit of flipping a
shared default to `false` would fix the stranding and silently break hold-and-turn.

⚠ **The test named above no longer exists under that name.** `UnknownPhase_LeavesIsHoldingAlone` was **renamed, not
deleted**, and now asserts the opposite behaviour with its reason in the title:
`UnknownPhase_IsNotHolding_SoTheCardCannotBeStranded`
(`tests/Radio.Web.Tests/Services/EncoderHudServiceTests.cs:159`), joined by
`UnknownPhaseMidHold_StillDismissesOnTime` (`:182`). Searching for the old name will find nothing and could easily
be misread as the pin having been dropped.

---

## Encoder Settings surface (ENC-8) — three things deliberately not built

**1. `--signal-red-glow` is consumed and never declared.** `design-system.css:5364` reads
`var(--signal-red-glow)`; `:root` declares `--signal-amber-glow` but no red sibling, so that rule
silently renders no glow. **Pre-existing and untouched by `ENC-8`**, which was forbidden from adding
design tokens. It cannot be fixed by simply declaring the variable: doing so would change the
appearance of an already-shipped component as a side effect, so whoever fixes it must look at what
that rule is for and decide whether the glow was ever wanted. `ENC-8` and `ENC-12` both avoid
referencing it.

**2. `Restore designed defaults` is not on the Settings page, and the reason is not oversight.** The
punch list's `ENC-12` row listed it; handoff Rev 3's action table does not. In Rev 2 the page held 24
editable numerics and the button meant *discard my edits*. Rev 3 made everything read-only except the
four `reverse` toggles — and the undo for a toggle is the toggle. If the owner still wants it, it is a
fifth action that clears the four persisted `RotaryEncoder:Reverse:{n}` overrides and re-pushes: about
half a day. ⚠ **It needs a label that does not contain the word "defaults".** A button reading
"defaults" on this page is one misread away from the device factory reset
(`RotaryEncoderCommand.ResetDefaults`), which is deliberately absent because factory tiers were
measured on this hardware as one volume detent from silence to full. A test pins that command unsent.

**3. `Reset counters` has no visible effect until `ENC-14`.** Handoff §7.8 puts the button in
`ENC-8`'s action card, but the movement counters it zeroes are rendered on the Diagnostics card, which
is `ENC-14` and does not exist. The button works and the command reaches the device; its confirmation
copy is deliberately limited to *"Movement counters zeroed on the device."* and its hint says nothing
is shown for them yet. ⚠ **There is no acknowledgement for `0x03/0x05` in the protocol and no
diagnostics decoder in this build**, so the API returns *"the command was sent"*, not *"the counters
are zero"* — `ENC-14` should not upgrade that claim without a report `0x04` decoder to check it
against.

**4. The configuration read-back channel has no correlation id.** `_pendingConfigRead` in
`HidRotaryEncoderService` is a single slot and the `0x03/0x04` request carries no nonce, so a reply is
matched to whoever is waiting rather than to the request that produced it. `_maintenanceLock` serialises
whole operations and every retry inside one push re-sends the same bytes, so the only residual window is
a reply arriving after its own 2 s timeout **and** after a different operation has armed a new waiter,
which would then compare it against the wrong `desired`. Not reachable in practice on a directly
attached USB HID device with single-digit-millisecond round-trips, and self-correcting on the next
operation. Closing it properly means adding a nonce to the request and echoing it in report `0x02` —
a firmware protocol change, so it belongs in a future protocol revision rather than on its own.

**Also not extracted: a shared `NotificationService` wrapper.** `ENC-8` adds several toasts and
`ENC-12` adds one more, which makes a helper tempting. `RadioControlPanel.razor:1366` is the only
`new NotificationMessage` in the tree; every other site uses the three-argument overload. Extracting a
wrapper now would touch a dozen files across two rows for no behavioural gain.

---

## Tuning acceleration meets a 52 ms tuner — measured in `ENC-5` UAT, deliberately not changed there

**What was measured**, on the appliance at `80fcacf`, radio active on FM (SDR / RTL-SDR):

- **One `StepFrequencyUpAsync` costs ~52 ms.** Five consecutive `POST /api/radio/frequency/up` calls
  measured 51.8-54.4 ms each; a `GET /api/health/version` on the same connection is 1.1-1.5 ms, so
  essentially all of it is the hardware call, not HTTP.
- **The tuner serializes.** Three parallel step requests completed in 158 ms — the same as three
  sequential (201 ms) within noise — so overlapping `StepRadioFrequencyAsync` batches queue at the
  device rather than racing. There is no state-corruption hazard here.
- **No audio distortion.** 1,664 tuner calls over ~94 s of continuous stepping produced **zero new
  PipeWire xruns** (output sink `ERR` 2 → 2, `Radio.API` 0 → 0) and one "missed callback deadline"
  warning attributed to a GC burst, during the *lighter* of the two runs. That was UAT check H2 and
  it passed.

**The consequence, which is a UX issue rather than a correctness one.** `ENC-5`'s remap put the
tuning tiers `(150 x2 / 80 x4 / 40 x8)` on the knob that actually tunes, so `TuningClamp = 8` became
reachable for the first time. A full 8-step detent therefore costs **~416 ms of sequential awaits**,
and `HandleTuningTurn` launches it fire-and-forget (`_ = StepRadioFrequencyAsync(...)`). Detents
arriving faster than 416 ms queue at the tuner, so **the dial keeps moving after the hand stops**.
Handoff §5.5's design target — "a hard flick crosses the FM band in ~0.6 revolutions" — is ~14
detents x 8 steps = ~112 steps = **~6 seconds** of tuning after the flick ends.

**Why `ENC-5` did not change it.** Nothing here is a defect in `ENC-5`'s code: this is the designed
acceleration meeting a slow tuner, and the row's job was to make the knob reachable, which it did.
Every available fix — coalescing overlapping batches, dropping queued steps when a newer detent
arrives, lowering `TuningClamp`, or making the step call cheaper — changes how tuning *feels* and
needs design input rather than a Builder's judgement inside a P0 row about a different knob.

**What a follow-up should consider**, in rough order of cost:
1. **Coalesce, don't queue.** If a step batch is in flight, fold a newly arrived delta into it rather
   than starting a second batch. Bounds the overshoot to one batch (~416 ms) regardless of spin speed.
2. **Ask why a step costs 52 ms.** For an SDR this is a retune of a software front end; 52 ms is
   suspicious for something that should be a register write plus a resample-chain flush. If it can be
   brought to single-digit ms the problem evaporates without any behaviour change.
3. **Only then** reconsider the tier values. They came from the handoff and match the cabinet's feel;
   they are the last thing to change, not the first.

## Two `RadioApiService` client/server contract bugs (found by `ENC-5`, deliberately not fixed there)

Both were found while mapping the radio surface for the SOURCE overlay. Neither is in `ENC-5`'s path, and
folding unrelated fixes into a P0 encoder row is how a reviewable PR stops being one. Both were confirmed
against **both halves** of the call, not inferred from one side.

**1. `GetPowerStateAsync` deserializes a DTO against an endpoint that returns a bare `bool`.**
`src/Radio.Web/Services/ApiClients/RadioApiService.cs:215` calls
`GetFromJsonAsync<RadioPowerStateDto>("/api/radio/power")`. The server
(`src/Radio.API/Controllers/RadioController.cs:449`) is `Task<ActionResult<bool>>` returning
`Ok(powerState)` — the body is the JSON scalar `true`/`false`, not an object. Deserializing a scalar into
`RadioPowerStateDto` throws, the method's `catch` swallows it and returns `null`, so **the call never
yields a power state and never reports why**. Fix is a one-line decision: return the DTO server-side, or
read a `bool` client-side. Pick one and delete the other shape.

**2. `SetEqualizerAsync` posts `{ preset }` against a server binding `Mode`.**
`RadioApiService.cs:187` posts `new { preset }`. The server (`RadioController.cs:310-318`) binds
`SetEqualizerModeRequest` and reads `request.Mode`, so the bound value is null, `Enum.TryParse` fails, and
the endpoint returns **400 with a "valid values are…" message the client discards** — `SetEqualizerAsync`
reads only `IsSuccessStatusCode` and returns `false`. The radio equalizer cannot be set from the Web client
at all, and the failure is silent to the user. Fix: rename the anonymous property to `Mode` (or add a
`[JsonPropertyName]`), plus a test that posts the client's real payload shape.

**Why they are grouped:** both are the same defect class — a hand-written payload on one side of an HTTP
boundary that nothing checks against the other side. A contract test over `RadioApiService`'s payloads
would have caught both.

---

## Encoder fault surfacing (ENC-12) — two things left open

**1. `SystemConfigPage` has no tab deep-linking, so the fault toast lands one tap short.** The
notification's `Click` navigates to `/system`, but the encoder card lives under **Integrations →
Rotary Encoders** and the page's tabs are `RadzenTabs` with no route parameter — so the owner arrives
on the page and still has to pick the tab. Fixing it means giving `SystemConfigPage` a route parameter
or query string (`/system?tab=integrations`) and seeding `SelectedIndex` from it, which is a change to
a page `ENC-8` had just rewritten and which nothing else needed, so it was left out rather than
smuggled in. The toast copy deliberately does not promise more than it delivers. Roughly an hour,
including the two other places that would then want to link straight to a tab.

**2. `MainLayout` has no bUnit coverage at all, and that is now load-bearing for three indicators.**
`tests/Radio.Web.Tests/Components/Layout/MainLayoutTests.cs` is a documented stub that renders nothing
— its own XML doc says Radzen plus JSInterop make the layout impractical to render — so **nothing under
`tests/` asserts `.topbar-mute-chip` (ENC-4a), `.phone-nav-fault` (bell surfacing) or
`.encoder-nav-fault` (ENC-12) in rendered markup.** Each of those is a fault indicator whose whole job
is to be correct when nobody is looking at it.

The workaround so far has been to push every decision into a pure, unit-testable class
(`BellHealthRules`, now `EncoderFaultRules`) and leave the layout with branches that contain no logic.
That is worth keeping regardless, but it does not cover the wiring: a badge bound to the wrong property,
a missing `@if`, or a subscription that is never made would pass every test in the suite.

Making the layout renderable needs: a bUnit `TestContext` with `Services.AddRadzenComponents()`,
`JSInterop.Mode = JSRuntimeMode.Loose` (the pattern `EncoderHudTests` already uses), and test doubles
for the ~15 injected services — `SystemApiService`, `SourcesApiService`, `DevicesApiService`,
`AudioApiService`, `QueueApiService`, `AudioStateHubService`, `AudioStateStore`, `BellHealthService`,
`PhoneUnreadState`, `GainPopoverService`, `EncoderHudService`, `EncoderFaultAnnouncer`,
`DeviceDisplayStateService`, `RadioPanelToggleService`, and two `IOptionsMonitor<>`s. Most are concrete
classes rather than interfaces, so this needs either extracted interfaces or a real
`IServiceCollection` wired to stubs. Realistically a day, and it pays for itself the first time one of
the three indicators regresses silently.

**3. ~~The handoff's tier table puts the volume clamp in the hard-fault row only, and the code does
not.~~ RESOLVED 2026-09-03 by `ENC-16` — kept as the record of which side was wrong and why.**
The defect: `RotaryEncoderConfigVerifier.VolumeClampFor` returned the loose 6-unit clamp for
`Configured` and nothing else, so a *Degraded* console had volume-knob behaviour identical to one in
hard fault while ENC-12's Degraded toast told the owner only that the knobs *"may feel wrong"*.
ENC-12 correctly refused to reword spec-verbatim copy to match code and logged it here instead.

**The owner's decision was to relax the clamp**, on the reasoning that acceleration disagreeing is a
*feel* fault and tightening volume for it is the actual bug. `Degraded` and `Configured` now run the
normal ±6 clamp; `Unknown`, `Transient` and `HardFault` keep the tight ±2, which is exactly the set
where `wrap`/`reverse` are unverified or disagreeing. **No owner-facing copy changed** — both toasts
were written for this behaviour and are true of it for the first time. Encoder handoff §7.6 Rev 6
carries the clamp column the table never had.

⚠ **The non-obvious half, worth keeping:** this entry's own closing argument was that the loose clamp
*"almost certainly should not"* apply to Degraded, since a feel-field mismatch still means read-back
did not confirm the device's tiers. That is true, and the owner accepted it as a feel trade — but it
concealed a second case that is **not** a feel trade. `Classify(null, 3)` — the device never answered
at all — also returned `Degraded`, and that state confirms *nothing*, `wrap` and `reverse` included.
Relaxing the tier without moving that case would have handed the normal clamp to a device possibly
still on factory tiers. `ENC-16` moved it to `HardFault`, which is also the only tier whose copy tells
the owner volume is limited. **A tier used as a safety predicate has to mean one thing.**

**4. `AudioStateStore.NotifyAsync` awaits only the last subscriber of a multicast `Func<Task>`.**
`await handler.Invoke()` on a multicast delegate returns the task of the **final** subscriber only.
Every earlier subscriber's task is started and then dropped: it runs unobserved, its exceptions escape
the `try`/`catch` written to contain them, and the ordering the store appears to give — each broadcast
fully handled before the next is processed — holds for one subscriber and silently stops holding for
two. Pre-existing and long-standing, but **ENC-12 is the first code to give that singleton real
per-circuit subscribers**, so it is newly load-bearing: with two browsers open, two encoder broadcasts
arriving back to back can be handled concurrently by the first circuit. Fix:
`await Task.WhenAll(handler.GetInvocationList().Cast<Func<Task>>().Select(h => h()))` with per-handler
exception capture. It applies to the eight `Func<Task>` events routed through `NotifyAsync`, and the
same flaw is in the two typed events (`RadioStateChanged`, `SleepStateChanged`) that invoke themselves
directly. An hour, plus a test that subscribes twice and asserts both tasks were awaited.

---

## TTS seam — four deferrals that existed only in prose until 2026-09-03

**Status:** filed here for the first time. **Nothing below was fixed by the act of filing it.**
**Verified against the tree at `5e571b88`, i.e. after `TTS-9` (#548) removed eSpeak** — which closed
half of item 1 and left the rest standing. Line numbers below are post-`TTS-9`.

> ⚠ **Why this section exists.** `design/plans/PHN-1c-event-playback-service-and-route.md` §4 item 5
> stated that items **1** and **2** below were already *"recorded in `design/FUTURE-WORK.md` with
> reproductions."* They were not — `git show --stat 34f73c7` touched only that plan and
> `docs/BUILDER_QUEUE.md`. That single sentence is what made two untracked deferrals look tracked. The
> plan has been corrected; this section is what makes the sentence true.

### 1. `TTSParameters` — a four-field default trap, of which `TTS-9` closed two

⚠ **Half of this was fixed while it was being filed, and the surviving half is the quieter half.**
As audited (at `01220d0c`) all four fields of `TTSParameters` were **non-nullable with type
initializers**, so `TTSFactory.CreateAsync`'s fallback chain could never fire for a caller who passed
any instance at all. **`TTS-9` (#548) made `Engine` and `Voice` nullable**, which fixes those two by
construction. **`Speed` and `Pitch` were left as `float` with `= 1.0f`, so the trap survives on them.**

Current state, `src/Radio.Core/Interfaces/Audio/ITTSFactory.cs:84-107`:

```csharp
public TTSEngine? Engine { get; init; }          // fixed by TTS-9
public string? Voice { get; init; }              // fixed by TTS-9
public float Speed { get; init; } = 1.0f;        // still traps
public float Pitch { get; init; } = 1.0f;        // still traps
```

`TTSFactory.CreateAsync` resolves them at
`src/Radio.Infrastructure/Audio/Services/TTSFactory.cs:77-80`:

```csharp
var engine = parameters?.Engine ?? ParseEngine(opts.DefaultEngine);
var voice  = parameters?.Voice  ?? opts.DefaultVoice;
var speed  = parameters?.Speed  ?? opts.DefaultSpeed;   // ?? unreachable
var pitch  = parameters?.Pitch  ?? opts.DefaultPitch;   // ?? unreachable
```

**On the last two lines the `??` can only fire when `parameters` itself is null**, because `float` is a
non-nullable member of a non-null instance. So a caller who passes *any* instance silently overrides the
configured `TTS:DefaultSpeed` and `TTS:DefaultPitch` with `1.0f` — even when the owner set them to
something else.

**Reproduction:** set `TTS:DefaultSpeed` to `1.4` in configuration, then call
`ITTSFactory.CreateAsync("hello", new TTSParameters { Voice = "en-US-News-K" })`. The synthesis runs at
speed `1.0`, not `1.4`, and nothing logs a discrepancy.

**Why this is smaller than it was, and still worth a row:** the pre-`TTS-9` version of this defect
swapped the *engine and voice*, which is audible immediately. Speed and pitch drift is subtle — it
sounds like the configured value never took, which is precisely the failure mode that gets misdiagnosed
as a broken settings page. It is also the same defect class `ENC-8a` and `TTS-10` exist for.

**Fix:** make `Speed` and `Pitch` nullable too, so "unset" is expressible for all four. ADR-029 §9.3 and
`PHN-1a` §4 flagged the whole record; `PHN-1c` declined it deliberately, because a nullable `Engine`
alone *looks* like a complete fix while being a quarter of one — an argument that now applies to the
remaining half.

### 2. `TTSFactory._cachedEngines` is never invalidated, so a key added in the UI needs a restart

`src/Radio.Infrastructure/Audio/Services/TTSFactory.cs:32,60-61`. `_cachedEngines ??=
DetectAvailableEngines()` is the **only** write to the field, and `TTSFactory` is registered **singleton**
(`AudioServiceExtensions.cs:334-335`), so the cache lives for the life of the process. **Unaffected by
`TTS-9`** — verified at `5e571b88`, which leaves lines 32 and 60-61 exactly as they were. With eSpeak
gone, both remaining engines are key-gated, so this is now the *only* thing standing between pasting a
key and the engine becoming usable.

⚠ **Be precise about what is stale, because the obvious phrasing is wrong.** Engine and voice
*selection* is **not** affected — `CreateAsync` reads `_options.CurrentValue` on every call. What is
frozen is **availability detection**: `DetectAvailableEngines` (`:259`) snapshots `_secrets.CurrentValue`
once (`:262`) and derives each `IsAvailable` from whether `GoogleAPIKey` (`:265`) / `AzureAPIKey` +
`AzureRegion` (`:276-277`) are non-empty. So an owner who pastes a Google or Azure key into the Secrets
page sees
`GET /api/sources/tts/engines` (`SourcesController.cs:350`) keep reporting that engine **unavailable**
until `radio-api` restarts — on an appliance that runs for weeks.

**Fix:** subscribe to `IOptionsMonitor<TTSSecrets>.OnChange` and clear the field, or drop the cache
entirely. `PHN-1c` §4 item 4 notes this is **one change** with that plan's §9.4 defect (a).

### 3. `TTSEventSource.Position` is never overridden, so every speech snapshot reports zero

`EventAudioSourceBase.Position` is `public virtual TimeSpan Position => TimeSpan.Zero`
(`src/Radio.Infrastructure/Audio/Sources/Events/EventAudioSourceBase.cs:31`). `AudioFileEventSource`
overrides it (`:93`); **`TTSEventSource` does not** — it overrides `Name`, `Type`, `Duration` and the
lifecycle methods only.

⚠ **This is deliberately pinned, and the pin is the hazard.** `PHN-1c`'s plan ships
`ASpeechSnapshotReportsPositionZeroForItsWholeLife`, which asserts the real behaviour rather than the
desirable one, and its "do not do this here" list names *"Override `TTSEventSource.Position`"* first. Once
that PR lands, **a scrubber rendered from `PositionAtBroadcast` sits at zero for the life of every spoken
message, with a green test saying that is intended.** Pinning is the right call for that PR; leaving the
consequence undiscoverable is not.

**Fix (belongs to the arc's PR 5, per the plan):** mirror `AudioFileEventSource` —
`_playbackService.GetPosition(Id) ?? TimeSpan.Zero`. Update the pin's name and reason in the same commit;
never delete it.

### 4. `TTSOptions.GenerationTimeoutSeconds` has never had a reader

Filed as punch-list **`TTS-10`** (P1, §4.4) rather than here, because the owner can edit it in the UI —
which makes it a settings-field-that-lies rather than a private code defect. Cross-referenced here so all
four TTS deferrals are findable in one place.

---

## `GvMediaClient.BuildVoicemailUri` — an accepted security residual

**Status:** accepted; honest in the method's own comment; filed for completeness.

`src/Radio.Infrastructure/External/GvMediaClient.cs:154-168`. The method defends against an id that walks
**out** of the media prefix: `Uri` compresses dot segments, so an id of `".."` turns
`/api/gvbridge/voicemail/../audio` into `/api/gvbridge/audio` — same host, different route — and
`Uri.EscapeDataString` leaves `".."` untouched because both characters are unreserved. The
`AbsolutePath.StartsWith(VoicemailPathPrefix)` check refuses that, pinned by
`ADotDotIdIsRefused_BecauseUriCompressionWalksItOffTheRoute`.

**The residual:** a **single** `"."` collapses to `/api/gvbridge/voicemail/audio` — a different route, but
still *under* the prefix, so the prefix check passes. **Only `EventPlaybackRequest.ValidateMediaId`
refuses it**, and `BuildVoicemailUri` deliberately does not rely on that validator, which is the whole
reason it compares rather than asserts.

**Impact is low and should not be overstated:** every reachable caller goes through `ValidateMediaId`,
whose allow-list is `[A-Za-z0-9._~-]` with `"."` and `".."` explicitly rejected. This is a
defence-in-depth gap in a method written to hold *without* the validator — not a live hole.

**Fix, if taken:** reject any path segment equal to `"."` or `".."` inside `BuildVoicemailUri` itself, or
compare against the full expected path rather than its prefix.

---

## `RotaryEncoderOptions` — a stated hand-off that evaporated, recorded so it is not later read as lost work

**Status:** ✅ **no work is owed.** This entry exists only to close a dangling plan reference.

`ENC-2` (#504) deliberately did **not** replace `RotaryEncoderOptions`' flat fields with the per-encoder
shape its punch-list row asks for. Its commit message says so plainly:

> *DELIBERATELY NOT DONE: replacing RotaryEncoderOptions' eight flat fields with the per-encoder shape,
> which the row also lists. ... **The swap belongs with ENC-11**, where the values start meaning something.*

**`ENC-11` shipped (#508 / #509 / #510) without it, and nothing recorded that.** What happened instead is
that `ENC-8` introduced **`RotaryEncoderDesignedConfig`**
(`src/Radio.Infrastructure/Platform/Input/RotaryEncoderDesignedConfig.cs`, #527) as the single source of
truth for the designed table plus the owner's per-knob overrides — *"one object, three consumers ... the
bytes pushed, the bytes flashed, and the rows the Settings page renders."* That is a **better** answer than
widening `RotaryEncoderOptions`, and it is why the swap is no longer wanted.

`RotaryEncoderOptions` therefore still holds its **seven** settable fields (`Enabled`, `VendorId`,
`ProductId`, `DevicePath`, `PollIntervalMs`, `VolumeStepPercent`, `ReconnectDelayMs`) plus the
`SectionName` const — the "eight" in `ENC-2`'s message counts the const. All seven have readers; none is
a settings-field-that-lies. **Functionally fine. Do not "finish" the swap.**

---

## 1. Bluetooth AVRCP Volume Sync — Windows

**Status:** Linux fully implemented; Windows still stubbed
**Added:** 2026-02-11 (Phase 3 of audio-latency-research-and-fixes)
**Updated:** 2026-02-12 — Linux implementation complete
**Priority:** Low — Linux (Pi target) is done; Windows is dev-only

### What's Implemented (Linux)

Bidirectional AVRCP volume sync via BlueZ `MediaTransport1` D-Bus interface:
- `LinuxBluetoothService.AttachMediaTransportAsync()` — attaches to `MediaTransport1` when A2DP transport appears
- `OnTransportPropertiesChanged()` — fires `VolumeChanged` event when phone volume changes
- `SetDeviceVolumeAsync()` — sets BlueZ volume (0-127) via D-Bus property
- `AudioStateUpdateService` — subscribes to `VolumeChanged`, syncs to `MasterVolume`, and pushes console volume changes back to the BT device

### What's Needed — Windows

Windows stub (`WindowsBluetoothService`) still needs implementation using NAudio CoreAudioApi:

```csharp
// NAudio CoreAudioApi — find the BT audio endpoint
using NAudio.CoreAudioApi;
var enumerator = new MMDeviceEnumerator();
var btEndpoint = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
    .FirstOrDefault(d => d.FriendlyName.Contains(btDeviceName));
float volume = btEndpoint.AudioEndpointVolume.MasterVolumeLevelScalar; // 0.0-1.0
```

**Gotchas:**
- Must be under `#if WINDOWS_TARGET` — NAudio.CoreAudioApi requires Windows
- The BT endpoint may not appear immediately after `AudioPlaybackConnection.Open()`
- `MMDeviceEnumerator` is COM — must be created on an STA thread
- The endpoint may change if the user switches BT codecs (SBC→AAC)

---

## 2. Radio Device Switching (API)

**Status:** Validates request, returns device info, does not switch
**Added:** 2026-02-11
**Priority:** Low — RF320 is the only radio hardware; SDR radio uses a separate source

### What Exists

| File | What's There |
|------|-------------|
| `Radio.API/Controllers/RadioController.cs:845-858` | `POST /api/radio/device/select` — validates device type, checks availability via `_radioFactory.IsDeviceAvailable()`, returns `RadioDeviceInfoDto` with `IsActive = false` |

### What's Needed

The controller needs to call `AudioManager.GetOrCreateSourceAsync(AudioSourceType.Radio)` with the selected device type. This requires:

1. `IRadioFactory.CreateRadioSource(deviceType)` already exists and works
2. The `AudioManager` would need a way to recreate the Radio source with a different device type (currently the source is cached — switching device type requires evicting the cached source)
3. Consider: should switching radio device type stop the current radio and create a new one, or hot-swap?

**Gotchas:**
- The current `_sourceCache` in AudioManager uses `AudioSourceType` as key — both RTL-SDR and RF320 are `AudioSourceType.Radio`. Switching between them requires cache eviction and source disposal.
- RF320 requires physical power — software can't start it. SDR can be started/stopped programmatically.

---

## 3. RF320 Radio Software Control

**Status:** Fully stubbed — hardware limitation, not a software gap
**Added:** 2026-02-11
**Priority:** None — this is a hardware constraint, not future work

### What Exists

`Radio.Infrastructure/Audio/Sources/Primary/RadioAudioSource.cs` — The RF320BT is a vintage Bluetooth-controlled radio with USB audio output. It has no software-controllable tuner. All control methods (`SetFrequencyAsync`, `StepFrequencyUpAsync`, `SetBandAsync`, `StartScanAsync`, `TogglePowerStateAsync`, etc.) are no-ops that log warnings.

**This is intentional and permanent.** The RF320 can only be controlled via its physical knobs and the RaddyRF320BT Bluetooth protocol (separate git submodule). If SDR radio support is the active device, these methods are handled by `SDRRadioAudioSource` which fully implements them.

No further action needed unless a new radio hardware type is added.

---

## 4. TTS Audio Cache

**Status:** Cache key computed, lookup not implemented
**Added:** 2026-02-11
**Priority:** Low — TTS is used infrequently (announcements, alerts)

### What Exists

| File | What's There |
|------|-------------|
| `Radio.Infrastructure/Audio/Services/TTSFactory.cs:91-102` | Computes `cacheKey = $"{engine}_{voice}_{text.GetHashCode()}"`, sets `isCached = false` always, increments cache hit/miss metrics |

### What's Needed

1. Define a cache directory (e.g., `./data/tts-cache/`)
2. On synthesis, save the audio stream to `{cacheDirectory}/{cacheKey}.wav` (or `.mp3`)
3. On lookup, check if `{cacheDirectory}/{cacheKey}.wav` exists and return it
4. Add TTL-based cleanup (similar to `AlbumArtCacheService` pattern — SHA256 content-addressed, 7-day TTL)
5. Consider cache invalidation when voice settings change

**Gotchas:**
- `text.GetHashCode()` is not deterministic across .NET processes/versions — use SHA256 of the text instead
- Cache should include voice parameters (speed, pitch) in the key, not just engine+voice+text
- Audio format should match what the TTS source expects (PCM WAV at engine sample rate)

---

## 5. Kiosk Mode (Fullscreen Browser)

**Status:** COMPLETE — running on Ubuntu x64 touchscreen (1920x720)
**Added:** 2026-02-13
**Priority:** Medium — needed for the final console radio experience, but deferred until all testing/debugging/validation is complete on the Pi

### What Exists

The Radio.Web Blazor Server UI runs as a separate systemd service (`radio-web.service`) on port 5002. The Pi's touchscreen display (1920x576) can access it via any browser.

### What's Needed

A kiosk mode setup that launches Chromium in fullscreen `--app` mode pointing at the local Web UI, auto-starting on boot.

#### Implementation Steps

1. **Auto-login for the display user** (e.g., `pi` or a dedicated `kiosk` user):

```bash
# /etc/systemd/system/getty@tty1.service.d/autologin.conf
[Service]
ExecStart=
ExecStart=-/sbin/agetty --autologin pi --noclear %I $TERM
```

2. **Chromium kiosk systemd user service** (`~/.config/systemd/user/radio-kiosk.service`):

```ini
[Unit]
Description=Radio Console Kiosk Browser
After=graphical-session.target
Wants=graphical-session.target

[Service]
Type=simple
Environment=DISPLAY=:0
ExecStartPre=/bin/sleep 5
ExecStart=/usr/bin/chromium-browser --kiosk --app=http://localhost:5002 --noerrdialogs --disable-infobars --disable-session-crashed-bubble --check-for-update-interval=31536000
Restart=on-failure
RestartSec=5

[Install]
WantedBy=graphical-session.target
```

3. **Disable screen blanking / power management:**

```bash
# /etc/xdg/lxsession/LXDE-pi/autostart (add these lines)
@xset s off
@xset -dpms
@xset s noblank
```

4. **Hide mouse cursor** (for touchscreen):

```bash
apt install unclutter
# Add to autostart: @unclutter -idle 0.1 -root
```

#### Gotchas

- Chromium `--kiosk` vs `--app`: `--kiosk` is true fullscreen (no window chrome, no way to exit without keyboard). `--app` shows minimal chrome but allows window management. For a console radio, `--kiosk` is preferred.
- The `ExecStartPre=/bin/sleep 5` ensures the X server and radio-web service are fully up before Chromium launches. May need adjustment.
- On Raspberry Pi OS Bookworm with Wayland (labwc), the approach differs — use `wlr-randr` for display config and ensure Chromium runs under Wayland.
- If using X11 (default on Pi OS Bookworm desktop), ensure LightDM auto-login is configured in `/etc/lightdm/lightdm.conf` under `[Seat:*]`.
- The 1920x576 ultrawide touchscreen may need custom display configuration via `xrandr` or `/boot/config.txt` HDMI settings.
- Consider adding a health-check loop that restarts Chromium if it crashes or if the Web UI becomes unresponsive.

---

## 6. Google Cast Custom Web Receiver — Low-Latency Streaming

**Status:** COMPLETE — registered, deployed, and tested with DirectChannel streaming
**Added:** 2026-02-16 (Phase 11.2 Cast latency reduction)
**Priority:** Medium — eliminates 10-15s Cast buffering delay; code-side changes already reduce ceremony from ~15s to ~8s

### What Exists

| File | What's There |
|------|-------------|
| `docs/receiver.html` | Complete CAF Custom Web Receiver with low-latency buffer config (deployed via GitHub Pages) |
| `Radio.Core/Configuration/AudioOutputOptions.cs` | `GoogleCastOutputOptions.ApplicationId` — configurable app ID (default: `CC1AD845`) |
| `Radio.Infrastructure/Audio/Outputs/GoogleCastOutput.cs` | Uses `_options.ApplicationId` for all `LaunchApplicationAsync()` calls |
| `Radio.API/appsettings.json` | `AudioOutput.GoogleCast.ApplicationId` setting |

### How the Custom Receiver Reduces Latency

The Default Media Receiver (CC1AD845) has a hardcoded 10-15 second audio prefill buffer. For live radio streams, this creates unacceptable delay. The custom receiver uses CAF (Cast Application Framework) with:

- `initialPlaybackWatermark = 0.2` — start playback after just 200ms of buffered audio (vs 10-15s default)
- `resumePlaybackWatermark = 0.5` — resume after rebuffer with only 500ms
- `disableIdleTimeout: true` — keeps receiver alive for continuous streams
- `StreamType.LIVE` already enforced by our sender code

### Step-by-Step Setup

#### Step 1: Host `receiver.html` on HTTPS

Google Cast requires the receiver URL to be served over HTTPS. The simplest free option is GitHub Pages:

1. In your GitHub repo, go to **Settings → Pages**
2. Under "Source", select **Deploy from a branch**
3. Choose the `main` branch and `/deploy/cast-receiver` folder (or push `receiver.html` to a `docs/` folder on `main` if your repo structure requires it)
4. Click **Save** — GitHub will deploy to `https://<username>.github.io/<repo>/receiver.html`
5. Verify the URL loads in a browser — you should see a dark page with "Radio Console" text

Alternative: If GitHub Pages doesn't suit your setup, any static HTTPS host works (Firebase Hosting free tier, Netlify, Cloudflare Pages, etc.).

#### Step 2: Create a Google Cast Developer Account

1. Go to the [Google Cast SDK Developer Console](https://cast.google.com/publish/)
2. Sign in with your Google account
3. Pay the **one-time $5 registration fee** (non-refundable)
4. **Important**: The email address cannot be changed after account creation — use the account you want to own this long-term

#### Step 3: Register the Custom Receiver Application

1. From the Developer Console, click **Add New Application**
2. Select **Custom Receiver**
3. Fill in the form:
   - **Name**: `Radio Console` (displayed briefly while the receiver loads on Cast devices)
   - **URL**: Your HTTPS receiver URL from Step 1 (e.g., `https://yourname.github.io/RTest/receiver.html`)
   - **Relay casting**: Leave **disabled** (not needed for local network streaming)
   - **Audio-only devices**: **Enable** — this allows Cast to audio-only devices like speakers and smart displays in audio mode
4. Click **Save**
5. **Copy the Application ID** shown (e.g., `A1B2C3D4`) — you'll need this for configuration

#### Step 4: Register Your Cast Device for Testing

Unpublished apps only work on devices whose serial numbers are registered in the console.

1. In the Developer Console, click **Add New Device**
2. Find your Chromecast's serial number:
   - **Chromecast/Chromecast Audio**: Printed on the back of the device, or:
     - Open the Google Home app → tap your device → tap the gear icon → scroll to "Cast firmware version" area where serial is shown
   - **Android TV / Google TV**: Settings → System → About → Status → Serial number (use the **Cast serial number**, not hardware serial)
   - **Smart speakers/displays**: Open the Google Home app → tap your device → gear icon → scroll to serial number
3. Enter the **serial number** and a **description** (e.g., "Living Room Chromecast")
4. Click **OK**
5. **Wait 15 minutes** for registration to propagate
6. **Reboot your Cast device** — unplug power, wait 10 seconds, plug back in
7. The device status in the console should change to **"Ready for Testing"**

#### Step 5: Configure Radio Console to Use the Custom Receiver

Update `appsettings.json` (or the Pi's deployed config) with your new Application ID:

```json
"AudioOutput": {
  "GoogleCast": {
    "ApplicationId": "A1B2C3D4"
  }
}
```

Replace `A1B2C3D4` with the actual ID from Step 3. Then restart the Radio.API service.

#### Step 6: Verify It Works

1. Start Radio.API and Radio.Web
2. Open the Web UI → go to the Cast/Output section
3. Select your Cast device and connect
4. Play any audio source — audio should start on the Cast device within **1-3 seconds** instead of the 10-15 second delay with the default receiver
5. If something goes wrong, revert `ApplicationId` to `"CC1AD845"` to fall back to the default media receiver

#### Step 7: Publish (Optional)

Once tested and working, you can publish so the app works on **all** Cast devices without serial number registration:

1. In the Developer Console, click **Edit** on your application
2. Fill in the required listing details:
   - **Category**: Music & Audio
   - **Title**: `Radio Console` (≤50 chars)
   - **Description**: `Low-latency audio streaming for Radio Console` (≤80 chars)
   - **Icon**: Upload a 512×512 PNG icon
3. Click **Save**, then **Publish**
4. The app becomes available to all Cast devices worldwide (takes a few minutes to propagate)

### Gotchas

- **HTTPS required**: Cast receivers must be served over HTTPS. `http://` URLs are rejected by the Cast SDK.
- **Test device registration**: Unpublished custom receivers only work on devices registered in the Cast SDK Console. After registering, **reboot the Chromecast** for the change to take effect.
- **Registration propagation**: New app IDs can take 5-15 minutes to propagate to Cast devices after registration.
- **CAF v3 only**: The receiver uses CAF v3 (`cast_receiver_framework.js`). Do not use the deprecated v2 receiver SDK.
- **Fallback**: If the custom receiver has issues, revert `ApplicationId` to `"CC1AD845"` to use the default media receiver (with higher latency).

---

## 7. Google Cast — WebSocket + Web Audio API (Sub-500ms Latency)

**Status:** Researched & tested — blocked by mixed content policy
**Added:** 2026-02-19
**Priority:** Low — current MP3 approach (~3-10s latency) is acceptable for music; this only matters for intercom/doorbell use cases

### Concept

Replace the standard CAF media pipeline (`<audio>` element) with a fully custom receiver that:
1. Connects back to the C# app via WebSocket
2. Receives PCM audio chunks wrapped in 44-byte WAV headers
3. Decodes via `AudioContext.decodeAudioData()`
4. Schedules playback via `BufferSource.start()` with a tiny jitter buffer (~100ms)

This bypasses Chrome's 10-20s buffering entirely, achieving sub-500ms theoretical latency.

### What Would Be Needed

**C# Server Side:**
- WebSocket server (e.g., Fleck or `System.Net.WebSockets`) on a LAN port
- Audio capture in small chunks (50-100ms) from the existing `TappedOutputStream`
- Each chunk wrapped in a 44-byte WAV header (RIFF + fmt + data)
- Custom Cast namespace messages to send the WebSocket URL to the receiver

**Custom Receiver (JavaScript):**
- `AudioContext` + `decodeAudioData()` for chunk decoding
- Jitter buffer with scheduled playback via `BufferSource.start()`
- WebSocket reconnection logic
- No CAF media player — fully custom audio pipeline

### Why It's Blocked

**Mixed content policy** prevents the HTTPS-served receiver from connecting to local LAN servers:

| Protocol | Result | Notes |
|----------|--------|-------|
| `ws://` (plain WebSocket) | **BLOCKED** | Chrome 92 on Cast devices enforces mixed content. No TCP connection attempted. |
| `wss://` (self-signed cert) | **BLOCKED** | Chrome rejects self-signed certs from HTTPS context. No TLS handshake attempted. |

Both were tested on 2026-02-19 with a TCP/TLS listener on the Pi (port 9999) — zero connections received in 120s for each test.

### Possible Workarounds

1. **Trusted CA certificate** — Use Let's Encrypt with a real domain (e.g., via DuckDNS) pointing to the LAN IP. The receiver would connect to `wss://radio.duckdns.org:8080/audio`. Requires: domain registration, cert renewal automation, DNS configuration.

2. **Cast custom message channel** — Route audio data through the Cast SDK's custom namespace messaging (`urn:x-cast:com.radioconsole.audio`). This stays within the Cast protocol and avoids mixed content entirely. Downsides: message size limits, higher overhead, unknown latency characteristics for binary audio data.

3. **HTTP-served receiver** — Google Cast technically requires HTTPS for registered receivers, but some older Chromecast firmware may accept HTTP. Not reliable and not future-proof.

### Current Approach (For Reference)

The existing HTTP MP3 streaming approach achieves ~3-10s latency:
- Server delivers MP3 at ~1.1x real-time via throttled HTTP progressive download
- Custom receiver (`docs/receiver.html`) forces `play()` after 3s buffer
- `maxAheadSeconds=10` provides deep buffer for stable playback
- `lagSeconds=5` gives initial burst on connection

This is adequate for music playback and significantly simpler than the WebSocket approach.

---

## 8. Google Cast — Pre-loaded Event Sounds (Zero-Latency Alerts)

**Status:** Not implemented — documented for future use
**Added:** 2026-02-19
**Priority:** Low — only relevant if TTS/alert sounds need instant Cast playback

### Concept

Pre-load static audio files (doorbell, phone ring, TTS announcements) on the Cast receiver at startup. When the C# app needs to play an alert, it sends a lightweight JSON message via the Cast custom namespace — the receiver plays the pre-loaded audio instantly (no buffering, no streaming).

### What Would Be Needed

**Custom Receiver Changes:**
```javascript
// Pre-load sounds into browser memory
const sounds = {
  doorbell: new Audio('https://your-server.com/sounds/doorbell.mp3'),
  alert: new Audio('https://your-server.com/sounds/alert.mp3')
};
Object.values(sounds).forEach(s => s.load());

// Listen for custom namespace messages
const NAMESPACE = 'urn:x-cast:com.radioconsole.alerts';
context.addCustomMessageListener(NAMESPACE, (event) => {
  const msg = JSON.parse(event.data);
  if (sounds[msg.sound]) {
    sounds[msg.sound].currentTime = 0;
    sounds[msg.sound].play();
  }
});
```

**C# Sender Changes:**
- Add a custom namespace channel to the Cast connection
- Send JSON trigger messages: `{"sound": "doorbell"}`
- Sound files must be hosted on HTTPS (same server as receiver, or any HTTPS CDN)

### Gotchas

- Sound files must be served over HTTPS (same mixed content restriction)
- Pre-loading too many large files increases receiver startup time
- `new Audio()` + `.load()` may not work on all Cast device types (smart speakers vs Chromecast)
- The existing `AudioFileEventSource` in our pipeline handles event sounds locally — this would be an additional output path specifically for Cast devices

---

## 9. Queue UI — Deferred Features from /queue Page Removal

**Status:** Partially implemented — drive selector and path entry added; drag-reorder still deferred
**Added:** 2026-03-02
**Updated:** 2026-03-02 — Drive selector + path entry implemented in `FileBrowserDialog`
**Priority:** Low — current home-page `QueueHistoryPanel` + `FileBrowserDialog` covers primary use cases

### What Was Removed

The dedicated `/queue` page had several features not carried over:

1. **Drag-reorder queue items** — The QueuePage had a toggle for drag-and-drop reordering using `MudDropContainer`. The `QueueHistoryPanel` does not support drag reorder. The API endpoint `POST /api/queue/move` exists and works.

2. ~~**Drive selector**~~ — **IMPLEMENTED** in `FileBrowserDialog` location dropdown. Loads drives via `GET /api/files/drives`, supports browsing absolute paths via `GET /api/files?absolutePath=X`.

3. ~~**Custom path entry + virtual keyboard**~~ — **IMPLEMENTED** as editable path bar in `FileBrowserDialog`. Click the edit icon to enter an absolute path, press Enter to navigate.

### What's Still Needed

- **Drag-reorder**: Add `MudDropContainer` to `QueueHistoryPanel` queue list, wire up `QueueApiService.MoveItemAsync(fromIndex, toIndex)`. CSS for `.queue-item-draggable` / `.queue-item-dragging` already exists in `design-system.css`.

### Gotchas

- Drag-and-drop in MudBlazor requires `MudDropContainer` + `MudDropZone` with explicit item tracking — the queue index changes after each move, so refresh from API after each reorder
- Touch drag on the kiosk needs `touch-action: none` on draggable items

---

## 10. Rotary Encoders — Pico HID Report Format Verification

**Status:** ✅ **RESOLVED by `ENC-1` (2026-09-02) and verified on the live device. Kept as the record of what was
assumed, because the assumption was wrong in every particular.**
**Added:** 2026-03-03 · **Closed:** 2026-09-03

> ⚠ **Everything in "What Needs Verification" below is the 2026-03-03 *guess*, and it is superseded.** It is struck
> through rather than deleted so that a reader who finds the old 8-byte layout quoted somewhere else can see it was
> retired deliberately. **The real format:** Input Report **`0x01`** carries a **36-byte payload** (37 bytes on the
> wire including the report id), not 8 — `RotaryEncoderDecoder.PositionPayloadSize = 36`,
> `src/Radio.Infrastructure/Platform/Input/RotaryEncoderDecoder.cs:23`. Pre-accumulator firmware sends a **21-byte**
> payload (`LegacyPositionPayloadSize`, `:26`) and positions/buttons parse identically in both. The device reports
> **free-running movement accumulators**, not per-report deltas, so the decoder differences successive samples — and
> **the first report after any connect is a baseline, not an input** (`:53-60`), or an outage's worth of accumulated
> movement arrives as one delta on the volume knob. VID/PID `0xCAFE`/`0x4005` were confirmed correct. Two further
> reports exist that this section never anticipated: `0x02` (106-byte config, `ENC-2`) and `0x04` (diagnostics,
> whose byte 4 is `steps_per_detent` as the firmware is actually using it).
>
> **Also stale in the table below:** `RotaryEncoderHostedService` is **no longer "gated by `RotaryEncoder:Enabled`"** —
> `ENC-0` inverted that flag's meaning to an escape hatch defaulting to `true`, and **presence decides**. And the
> udev rule is no longer something to add by hand: it ships as `deploy/common/99-rotaryusb-encoder.rules`.

### What Exists

| File | What's There |
|------|-------------|
| `Radio.Infrastructure/Platform/Input/HidRotaryEncoderService.cs` | HID reader, report parsing, event firing |
| `Radio.Infrastructure/Platform/Input/RotaryEncoderActionRouter.cs` | Maps encoder events → volume/tune/source/viz |
| `Radio.API/Services/RotaryEncoderHostedService.cs` | BackgroundService, gated by `RotaryEncoder:Enabled` |
| `Radio.Core/Configuration/RotaryEncoderOptions.cs` | VID=0xCAFE, PID=0x4005, step sizes |

### What Needs Verification

~~The HID report format is **assumed** based on typical KY-040 Pico implementations:~~ **(all three lines below
are the retired guess — see the banner above for the measured format)**
- ~~**Bytes 1-4**: signed encoder deltas (`sbyte` per encoder)~~ — actually free-running **accumulators**, differenced
  by the host
- ~~**Byte 5**: button bitmask (bit N = encoder N)~~
- ~~**Report size**: 8 bytes~~ — actually **36-byte payload / 37-byte report** (21-byte payload on legacy firmware)

**On first hardware connection:**
1. Enable logging, connect Pico, verify report bytes match assumed format
2. Adjust `ParseReport()` if the actual Pico firmware uses different byte offsets
3. Verify VID/PID match (`0xCAFE`/`0x4005` are TinyUSB defaults — may differ)
4. Linux: add udev rule `SUBSYSTEM=="hidraw", ATTRS{idVendor}=="cafe", ATTRS{idProduct}=="4005", MODE="0666"` for hidraw access

### Gotchas

- The Pico firmware is a separate project — both sides may need adjustments
- HidSharp on Linux needs hidraw permissions (udev rule or running as root)
- Encoder direction (CW = positive/negative) depends on wiring — may need to negate delta

---

## 11. Phone Call Integration — RotaryPhone Hub Protocol Verification

**Status:** Code implemented, hub protocol assumed
**Added:** 2026-03-03
**Priority:** High — must verify when RotaryPhone server is first available

### What Exists

| File | What's There |
|------|-------------|
| `Radio.Infrastructure/External/PhoneCallClient.cs` | SignalR client connecting to RotaryPhone hub |
| `Radio.Infrastructure/External/PhoneContactLookupService.cs` | REST client for contacts API |
| `Radio.API/Services/PhoneCallIntegrationService.cs` | BackgroundService orchestrating ring + TTS |
| `Radio.Core/Configuration/PhoneIntegrationOptions.cs` | Hub URL, ring sound path, priorities |

### What Needs Verification

**SignalR hub contract** (assumed):
- Hub URL: `http://radio:5004/hub`
- Server method: `CallStateChanged(string state, string phoneNumber)` — may also send caller name as 3rd param
- State values: `"Ringing"`, `"InCall"`, `"Ended"`, `"Idle"` — lenient parser handles variants

**Contacts REST API** (assumed):
- Endpoint: `GET {baseUrl}/api/contacts/lookup?phone={number}`
- Response: `{ "Name": "...", "PhoneNumber": "..." }`

**On first integration:**
1. Start RotaryPhone server, check actual hub URL path
2. Verify `CallStateChanged` method signature matches (param count, types)
3. Verify contacts API response shape
4. Test ring sound file exists at configured path (`media/sounds/phone-ring.wav`)
5. Provide a placeholder ring sound WAV

### Gotchas

- PhoneCallClient registers two `CallStateChanged` overloads (2 and 3 params) for resilience
- The call state parser is lenient (`"ringing"`, `"ring"`, `"incoming"` all map to Ringing)
- If RotaryPhone server isn't running, the client logs a warning and retries with backoff — no crash

---

## 7. Sleep Mode — Rotary Encoder Wake/Sleep Button

**Status:** ⚠ **CORRECTED 2026-09-02 by the `ENC-15` gate result — the previous status overclaimed in two
ways.** (1) It said *"Display DPMS control implemented … in `SleepService`"*; the display-power calls at
`SleepService.cs:164-168` and `:198-199` are **commented out**, so nothing in the running service turns the
panel off today. (2) It named `org.gnome.ScreenSaver SetActive` as the DPMS control; that route **does not
reach DPMS-off** — see the recovery section in `design/INTEGRATIONS.md` §1 and the full write-up at
[`docs/uat/2026-09-02-enc15-touch-wake-gate/REPORT.md`](../docs/uat/2026-09-02-enc15-touch-wake-gate/REPORT.md).
Sleep/wake as a *UI state* works; **panel blanking does not ship** and this section is the record of why.
**Added:** 2026-03-16
**Priority:** Medium for the encoder wake integration. ⛔ **Blanking itself: do not implement** — see the
blocker below.

### ⛔ Blocker — the panel has no usable wake path, measured on the box

`ENC-15` set out to confirm that touch could wake a blanked panel. It cannot, and the reason is
structural rather than a configuration problem:

- **The touchscreen is powered by the panel.** When the panel powers down the touch controller leaves the
  USB bus (`usb 3-1: USB disconnect` about a second after the blank, re-enumerating about a second after
  the unblank). There is no input device left to generate a touch event, at any layer.
- **The rotary encoder is not a compositor input device.** `cafe:4005` exposes `/dev/hidraw3` and **zero
  evdev nodes**, so it cannot reset the GNOME idle timer or wake a blank by itself. A knob wake works only
  if `radio-api` reads hidraw and *itself* issues the unblank — **making that service a single point of
  failure in the only remaining wake path**, which is precisely the condition under which somebody most
  wants the screen back.

That is one application-mediated wake path, not the two the design assumed. Blanking therefore stays off.
**What has not been tested is panel-firmware wake-on-touch** — a panel that watches its own digitizer while
powered down. If that exists on this hardware the conclusion changes; a confirmatory script was staged at
`/tmp/enc15-touchtest.sh` (note `/tmp` does not survive a reboot).

### What's Implemented

- `SleepService` (Radio.API) pauses audio, mutes, and broadcasts via SignalR. ⚠ **The display-power half is
  commented out** — the service does not blank the panel.
- Web UI power button (`power_settings_new` icon in MainLayout topbar) triggers sleep
- Wake via API call (`POST /api/system/sleep { sleep: false }`), or by touching the screen while it is still
  **lit** (the JS idle-dimmer dims in-page; it does not blank the panel). Touch on a *blanked* panel is a
  different case and does not work — see the blocker.

### What's Needed — Rotary Encoder

When rotary encoders are integrated via `RotaryEncoderActionRouter`:
1. **Sleep trigger:** Long-press (or dedicated button press) on a rotary encoder should call `ISleepService.EnterSleepAsync()`
2. **Wake trigger:** Any rotary encoder event (press or turn) while sleeping should call `ISleepService.WakeAsync("rotary-encoder")` BEFORE processing the encoder action. ⚠ This is an **application-mediated** wake, not a hardware one — it only functions while `radio-api` is running and reading hidraw.
3. **Implementation:** In `RotaryEncoderActionRouter`, check `ISleepService.IsSleeping` at the top of each event handler. If sleeping, call `WakeAsync` and consume the event (don't pass it through as a source change or volume adjustment)
4. ✅ **The wake latch was a real defect here, and `ENC-6` fixed it.** `TryWakeFromSleep` called `WakeAsync` fire-and-forget and returned true, so with a 10 ms poll several more events arrived and were silently discarded before `IsSleeping` flipped. It is replaced by a sleep gate that consults `ISleepService.WakeState` and takes a synchronous claim through `TryClaimWake()`; a fast spin now loses one detent rather than every detent that arrives before the browser leaves `/sleep`.

### Code Pointers

- `src/Radio.API/Services/SleepService.cs` — sleep/wake logic; display-power calls present but commented out
- `src/Radio.Infrastructure/Platform/Input/RotaryEncoderActionRouter.cs` — encoder event routing
- `src/Radio.Core/Interfaces/ISleepService.cs` — interface

### Gotchas

- GNOME ScreenSaver D-Bus requires the desktop session user (`mmack`) and session bus address — Radio.API runs `sudo -u mmack DBUS_SESSION_BUS_ADDRESS=... gdbus call`
- ⚠ **`org.gnome.ScreenSaver SetActive false` is a no-op when the panel is held down by DPMS rather than by the screensaver** — a state observed on this box, with `GetActive=(false,)` while `dpms=Off`. The control that works in both states is `org.gnome.Mutter.DisplayConfig PowerSaveMode`, set to `0`. The recovery commands, in the order to try them, are in `design/INTEGRATIONS.md` §1.
- An earlier revision of this section warned that *"`SetActive(true)` may not reliably wake the display"*. `SetActive(true)` **blanks**; `SetActive(false)` wakes. The real unreliability is the DPMS/screensaver split above.

⚠ **There are two sections numbered 7 in this file** — *Google Cast — WebSocket + Web Audio API* and
this one. `SleepService`'s class remarks point at **this** one. Renumbering is deliberately not done
here: it would churn every cross-reference in the file for a cosmetic gain, and the ambiguity is now
recorded rather than latent.

**`ENC-6` (2026-09-02) shipped the sleep/wake half and left the blanking half withdrawn.** Sleep is
now a three-state model — `Awake` / `Ambient` / `Standby`, in `Radio.Core.Interfaces.ConsoleWakeState`
— derived from `IsSleeping` plus a new `IsSleepScreenVisible` that the `/sleep` page reports about
itself. Nothing in that work turns the panel off, and nothing should: the two dark states in
Designer Rev 5 §8.2 are withdrawn with the blanking half.

### ⚠ `IsSleepScreenVisible` does not survive an API restart — open follow-up from `ENC-6`

**Deliberately not fixed on `ENC-6`; recorded here so it is not rediscovered as a new bug.**
`_isSleepScreenVisible` and the wake claim are in-memory fields on `SleepService`. If `radio-api`
restarts while the kiosk is parked on `/sleep`, the new process boots with the flag `false`, so
`WakeState` reads `Awake` while the browser is still showing the clock — which silently reinstates
exactly the defect `ENC-6` fixed, until something re-reports.

**Nothing re-reports on its own.** `Sleep.razor.OnAfterRenderAsync` fires once per component
lifetime, and `Radio.Web` is a separate process whose circuit survives an API restart untouched, so
the page has no reason to render again. `MainLayout` only corrects the flag when the user navigates
back through it. **This matters because every deploy restarts `radio-api`**, and the deploy relaunches
the kiosk onto Home rather than `/sleep`, so the common case self-corrects — but an API crash or a
manual `systemctl restart radio-api` overnight does not.

A second, related exposure: the claim taken by `TryClaimWake()` is released by the browser confirming
it left the route. If the `SleepStateChanged(false)` broadcast that drives that navigation is ever
lost — this box is WiFi-only — the claim stays set, `WakeState` reads `Awake`, and further knob
turns dispatch for real against overlays that live in `MainLayout` and are therefore not on screen
under `/sleep`'s `EmptyLayout`. A screen tap self-heals it; a knob-only interaction does not.

**Both are closed by the same mechanism:** a periodic re-report from `Sleep.razor` (the page already
runs a 60 s drift timer that could carry it), or a `Reconnected` hook on `AudioStateHubService`
re-asserting visibility. `SleepService.SetSleepScreenVisible` was made **edge-triggered** on `ENC-6`
specifically so that such a heartbeat can be added without wiping an in-flight wake claim.

## 12. GV (Google Voice) Messages — Durable Read-State, Send & Auth Seams

**Status:** PR1 foundation + PR2 voicemail surface + PR3 texts surface + PR4 durable mark-read shipped (DTOs, read client, status poll, `/phone` unified Messages feed with call rows + voicemail rows + inline player + new-arrival path + **text thread rows interleaved into the feed** + master-detail conversation + bubbles + compose/new-recipient composer + **durable read-state via GV write-through**). **SMS send is feature-flagged OFF** (`RotaryPhone:Gv:SendEnabled=false`). **Mark-read is now durable (GV write-through, ADR-024)** behind `RotaryPhone:Gv:MarkReadEnabled=false`. RotaryPhone's routes are **shipped** and dark behind their own `GVBridge:EnableMarkRead=false`, so lighting it up is a **two-side config flip** (theirs first), not a build wait. The inter-service auth gate remains deferred.
**Added:** 2026-06-20 (GV Messages PR1 — Foundation + IA shell); updated 2026-06-20 (PR2 — Voicemail surface).
**Updated:** 2026-06-21 (GV Messages PR3 — Texts surface reconciled onto PR2; texts wired into the `FeedItem` projection as `FeedKind.Text`, send + new-recipient composer flag-gated, on-screen entry reuses the existing global virtual keyboard); 2026-06-21 (PR4 — durable read-state: GV mark-read routes wired, unified `ReadStateChanged` event, idempotent reconciler).
**Priority:** Medium — read experience (incl. durable read-state) ships today behind flags; SMS send is one config flip + the endpoint. The seams below are wired OFF and ready to flip.

### What's Implemented (PR1)

- DTOs: `VoicemailItemDto`/`VoicemailListDto`, `SmsMessageDto`/`SmsThreadDto`/`SmsThreadListDto`/`SmsThreadMessagesDto`, `SendSmsRequest`/`SendSmsResponse`, `GvDirection` helper (`src/Radio.Web/Models/ApiModels.cs`).
- `GvBridgeApiService` read methods: voicemail list/item + **absolute** audio-URL builder, SMS threads/messages (`src/Radio.Web/Services/ApiClients/GvBridgeApiService.cs`).
- `PhoneHubService.GvSmsReceived` / `GvVoicemailReceived` push events on the existing `/hub` connection.
- `GvBridgeStatusService` — single ~10s `/api/gvbridge/status` poll → reconnecting banner + (future) Send gate.
- `RotaryPhoneAuthHandler` — header seam, OFF.
- `PhoneUnreadState` — UI-local unread sum surfaced to the topbar `/phone` pill.
- `PhoneMessagesPanel` — unified feed shell (segmented filter + call rows + detail pane + reconnecting banner).

### What's Implemented (PR2)

- `VoicemailRow` + `VoicemailPlayer` components (`src/Radio.Web/Components/Pages/`) — row (chip + unread dot + caller + transcript-preview + duration) and inline accordion player (seekable Range-backed scrubber via `wwwroot/js/voicemail-player.js`, transcript present/pending/absent, 502 audio-error state). `<audio src>` is the **absolute** `radio:5004` URL from `GvBridgeApiService.GetVoicemailAudioUrl` (never the relative DTO field).
- Unified-feed `FeedItem`/`FeedKind` projection in `PhoneMessagesPanel` — interleaves calls + voicemail by timestamp under **All**; single-stream under **Voicemail**. **PR3 (texts) extends this projection** (add `FeedKind.Text` + project threads; the shape is intentionally trivial to extend).
- `GvVoicemailReceived` new-arrival path in `PhonePage` — prepend + animate row, calm `NotificationSeverity.Info` toast only (never modal, never pauses audio per the hard rule), badge ++.
- Mark-heard on the voicemail-open path — opening a voicemail flips it heard (optimistic) + decrements the badge. **PR4 made this durable** (see below): the optimistic flip is now a presentation-only bridge to the authoritative GV write-through.

### What's Implemented (PR4 — durable read-state, ADR-024)

- **GV mark-read routes wired** (`GvBridgeApiService`): `MarkVoicemailReadAsync` → `POST /api/gvbridge/voicemail/{id}/read`, `MarkSmsThreadReadAsync` → `POST /api/gvbridge/sms/threads/{threadId}/read`. Body `{ "isRead": true }`; both return the updated DTO (`VoicemailItemDto?` / `SmsThreadDto?`). `200`→DTO, `404`→null (item gone), `502`/non-2xx→null but the caller keeps the optimistic flip. **No client-side auto-retry** — one attempt per user action. Gated on `RotaryPhone:Gv:MarkReadEnabled=false`.
- **Unified `ReadStateChanged` event** on the existing `/hub` (`PhoneHubService.ReadStateChanged`, payload `ReadStateChangedDto { Kind, Id, ThreadId, IsRead, ChangedAtUtc }`). Defensive: unknown `Kind` ignored at Debug. RotaryPhone broadcasts **unconditionally, including back to the originator**.
- **Idempotent reconciler** (`ReadStateReconciler`, plain unit-tested class) keyed by `(id-or-threadId + isRead)` — the mark route's returned DTO, the echoed broadcast, and the 502-deferred reconcile all collapse to one badge state with no flicker (no-op when already in state).
- **No local read-state store** — list endpoints' `isRead`/`hasUnread` are source-of-truth on (re)load. The GV-2 `_locallyHeard` / GV-3 `_locallyReadThreads` seeding was removed; the per-circuit optimistic flip is presentation-only and is never replayed onto fresh list data.
- **Single write path** for voicemail mark-read: `PhonePage.OnVoicemailHeard` (the player bubbles via `OnHeard`; it no longer calls the route directly). **No unread toggle** (ADR-024 §6).

### What's Needed / Deferred

1. **Durable read-state — SHIPPED behind `MarkReadEnabled` (PR4 / ADR-024).** "heard"/"read" now persists to Google Voice via GV write-through; Google is the single source of truth and RadioConsole keeps no local read-state store. **RotaryPhone's side is shipped too** (no longer owner-HELD): `GvVoicemailController.MarkRead`, `GvSmsController.MarkThreadRead`, and the unconditional `ReadStateChanged` broadcast all exist, with route templates matching our call sites verbatim. **Both sides are dark by config only** — theirs `GVBridge:EnableMarkRead=false`, ours `RotaryPhone:Gv:MarkReadEnabled=false` — so lighting up is a **two-side config flip, not a build wait**: flip theirs first, confirm the routes stop returning the dark rejection, then set `RotaryPhone:Gv:MarkReadEnabled=true` in `appsettings.Production.json` (deploy overwrites `appsettings.json`). Until both are flipped the routes are never called and read-state is per-circuit optimistic only (re-derived from `isRead`/`hasUnread` on the next list load). Missed-call badging remains UI-local (owner decision 2).
   - **Behavior while our flag is off:** the optimistic flip is no longer replayed onto refetched lists (the GV-2/GV-3 `_locallyHeard`/`_locallyReadThreads` seeding is gone by design — ADR-024 §2). So hearing a voicemail and then pressing **Refresh** in the same session restores the unread marker, because we never wrote the read through. Lists reload only on initial load, the explicit Refresh button, and error-retry — no background poll refetches them — so this is the full extent of the difference. It resolves the moment both flags are on.
   - **Deferred (informational, not blocking) — unread support:** their `GVBridge:AllowMarkUnread` is `false`, so `isRead:false` returns `400 unread_unsupported`; our UI never sends `false` and the toggle stays hidden (ADR-024 §6). One UI change if unread is ever wanted, no contract change.
   - **Deferred (informational, not blocking) — path (b), phone→kiosk LIVE push:** RotaryPhone's poller-flip fast-follow. Until it ships, externally-originated reads (phone/GV-web) reconcile on our **next list refresh / poll**, not as an instant push. The SAME `ReadStateChanged` handler covers both — no GV-4-side change when it lands.
   - **Constraint to hold:** keep the voicemail audio endpoint **unauthenticated** (ADR-022 §8.1) — the native `<audio src>` cannot send the auth header (see deferred item 3 gotcha).
2. **GV SMS send** — **built in PR3, flagged off** via `RotaryPhone:Gv:SendEnabled=false`. The whole compose/reply + new-recipient write path (optimistic → sending → sent → failed-with-preserved-text, 429/in-flight/degraded guardrails) is implemented behind `GvBridgeSendService` (`src/Radio.Web/Services/ApiClients/GvBridgeSendService.cs`); `SendAsync` throws `SendNotAvailableException` until the flag flips. **`POST /api/gvbridge/sms/send` has now SHIPPED on RotaryPhone** (dark behind their `GVBridge:EnableSmsSend=false`), so the endpoint is no longer the blocker — but **do not flip `RotaryPhone:Gv:SendEnabled` until the response shape is reconciled.** The shapes have diverged: ours is `SendSmsResponse(SmsMessageDto? Message, string? Error)` (still marked **provisional**); theirs is `SendSmsResponse(bool Queued, string Code, string? ThreadId, string? Error, SmsMessageDto? Message)`. `Message`/`Error` still bind by name, so this fails *quietly* rather than loudly — we silently drop **`Queued`** and the **`Code`** taxonomy (`queued` | `invalid_number` | `rate_limited` | `auth_unavailable` | `upstream_error` | `timeout` | `send_disabled` | `error`) that `GvBridgeSendService`'s 429/degraded guardrails need to distinguish a retryable rate-limit from a hard failure. Their dark response is `409` + `Code:"send_disabled"`. Needs its own queue row.
3. **On-screen text entry — reuses the EXISTING global virtual keyboard.** The compose message field and the new-recipient field are ordinary `<input>`s; the app-wide keyboard (`wwwroot/js/virtual-keyboard.js`, loaded in `App.razor`) auto-shows on focus, and the recipient field opts into the numeric layout via `data-keyboard="numeric"`. **This supersedes the design spec's "build a touch keyboard" recommendation** — no new keyboard component was built or skinned. If explicit show/hide is ever needed, use `window.virtualKeyboardInterop.show(element)` / `.hide()`.
4. **`RotaryPhoneAuthHandler`** — header (`X-RotaryPhone-Auth`) injected only when `RotaryPhone:Gv:AuthKey` is non-empty; empty today (LAN-only no-auth posture). One place to flip on when the inter-service auth gate ships (ADR-022 §8.1). The `GvBridgeSendService` typed client carries this handler too, so send authenticates the moment the gate flips. **GV-4 mark-read routes ride the same `RotaryPhone:Gv:AuthKey` seam** — the `/api/gvbridge/*` prefix gate auto-covers the two POST routes; no new auth key (ADR-024 §7).
   - **Gotcha:** a native `<audio>` element CANNOT send the auth header. If the voicemail audio endpoint ever becomes auth-required, the direct-`<audio src>` approach (PR2) breaks — keep that endpoint unauthenticated or token-in-query (ADR-022 §8.1 / contract risk #4).
5. **Voicemail player Call back / Text back quick actions** — deferred (owner decision 3). The `VoicemailPlayer` carries a `@* fast-follow … *@` marker where these belong. Call back routes through the existing phone dial path; Text back opens/creates the GV text thread for the caller (the PR3 texts surface is now in place to host it). No UI shipped yet.

### Code Pointers

- `src/Radio.Web/Services/ApiClients/GvBridgeApiService.cs` — read methods + audio-URL builder + **PR4 durable mark-read** (`MarkVoicemailReadAsync` / `MarkSmsThreadReadAsync` → the two `POST .../read` routes; 200→DTO, 404→null, 502→null-keep-optimistic, no retry; flag-gated). **GV-8:** `GetSmsThreadMessagesAsync` is the one read method that returns `GvResult<T>` rather than `T?`; the others still return `T?` because their callers already handle `null` correctly (the thread list keeps its last good list and toasts).
- `src/Radio.Web/Services/ApiClients/GvResult.cs` — **GV-8** outcome type (`Success` / `HttpError` / `Timeout` / `Transport` / `Malformed`, plus `StatusCode` and RotaryPhone's `error`/`code` discriminator). Exists because collapsing every failure to `null` let a 502 render as an empty conversation (UAT F-1). **GV-6 adopts this same type** for the two mark-read methods — the two rows share the idiom, not the PR (see `docs/BUILDER_QUEUE.md` § Dependency / ordering notes).
- `src/Radio.Web/Services/ReadStateReconciler.cs` — **PR4** idempotent `(id-or-threadId + isRead)` reconciler (the ADR-024 §9 invariant; plain unit-tested class, no Blazor dep).
- `src/Radio.Web/Services/Hub/PhoneHubService.cs` — **PR4** `ReadStateChanged` event on the existing `/hub` (defensive Kind guard), alongside `GvVoicemailReceived` / `GvSmsReceived`.
- `src/Radio.Web/Services/ApiClients/GvBridgeSendService.cs` — **PR3 flagged send seam** (the only write path; 4 typed exceptions + in-flight/429/degraded guardrails).
- `src/Radio.Web/Components/Pages/PhoneTextsPanel.razor` — **PR3** thread list + conversation + compose + new-recipient composer.
- `src/Radio.Web/Components/Pages/MessageBubble.razor` — **PR3** inbound/outbound bubble + status glyph.
- `src/Radio.Web/wwwroot/js/phone-texts.js` — **PR3** auto-scroll-to-bottom helper for the conversation pane.
- `src/Radio.Web/Components/Pages/VoicemailRow.razor` / `VoicemailPlayer.razor` — voicemail row + inline player (PR2).
- `src/Radio.Web/wwwroot/js/voicemail-player.js` — seek + HTML5 `<audio>` event bridge (PR2).
- `src/Radio.Web/Services/Http/RotaryPhoneAuthHandler.cs` — the auth seam.
- `src/Radio.Web/Components/Pages/PhoneMessagesPanel.razor` — unified feed with the `FeedItem`/`FeedKind` projection: **PR2 added `Call`/`Voicemail`, PR3 added the `Text` case** + renders text thread rows interleaved newest-first + hosts the conversation in the detail pane.
- `design/decisions/2026-06-20-gvbridge-voicemail-sms-integration.md` — ADR-022 (decisions + risks).
- `design/decisions/2026-06-20-gv-mark-read-durable-readstate.md` — ADR-024 (durable read-state contract + the de-dupe invariant).
- Config: `RotaryPhone:Gv:{SendEnabled,MarkReadEnabled,StatusPollSeconds,AuthKey}` in `appsettings.json` (+ `appsettings.Production.json` for per-machine `AuthKey`). `MarkReadEnabled` is the **consumer** flag (gates whether our seam calls the route); distinct from RotaryPhone's server-side `GVBridge:EnableMarkRead` config flag (gates whether their already-shipped route acts or returns the dark rejection). Flip theirs first.

---

## 13. Bell-Failure Surfacing — Live-Call States (blocked on RotaryPhone)

**Status:** Persistent signals shipped; the live-call + sticky-note states are stubbed pending a backend contract
**Added:** 2026-07-29 (PR "surface bell failures")
**Priority:** Medium — the shipped half already covers the "notice it at all" case; this half closes the 60-second window

### Why this exists

A call arrived, `/phone` showed RINGING in 96px amber with the pulse running, and the physical
rotary phone stayed silent for the full 60-second timeout. Nothing on screen indicated a
problem — the screen was not merely unhelpful, it was confidently wrong. Root cause was a
stale ring-INVITE target inside RotaryPhone (fixed there). Full spec, including the ASCII
mockups, copy deck and edge-case table, is in
`docs/design-handoffs/HANDOFF-bell-failure-surfacing.md`.

### What Exists

- `Radio.Web/Models/BellHealth.cs` — the `Ok` / `Suspect` / `Failed` / `Unknown` enum plus
  `BellHealthRules` (derivation, pill mapping, nav-pill accessible names).
- `Radio.Web/Services/BellHealthService.cs` — singleton `IHostedService` polling
  `GET /api/phone/system-status` app-wide (default 15s, `RotaryPhone:BellHealthPollSeconds`)
  so the topbar badge works from any route. `PhonePage` also `Publish()`es into it.
- Tri-state BELL pill in the System Status card, the `HT801 ATA` -> `BELL` relabel, the
  degraded idle hero (State C), and the crossed-bell nav-pill badge.

### What's Stubbed

**`BellHealth.Failed` has no producer.** It is modelled and handled everywhere a value is
consumed, but nothing ever sets it. Three UI states therefore do not exist yet:

1. **State B (§3.2)** — the live `.phone-hero-alert` strip during `Ringing`, the
   `· BELL SILENT` label suffix, and withdrawal of `.ring-pulse`.
2. **The predictive-degrade rule (§2)** — render the strip immediately at `Ringing` onset when
   health is already `Suspect`, because `BellInviteFailed` lands ~5s *after* `Ringing` and the
   hero would otherwise lie for that whole window.
3. **State D (§3.4)** — the sticky amber "A call at 2:14 PM didn't ring the phone." note that
   survives the call ending, plus its dismissal.

### What's Needed — from RotaryPhone (handoff §6)

| Item | Status | Needed for |
|---|---|---|
| `BellInviteFailed` hub event (§6.1, single-DTO shape) | REQUIRED | States B + D |
| `lastBellFailure` on `GET /api/phone/status` (§6.4) | REQUIRED | State D surviving a reload |
| `Ht801LastCheckedUtc` on `PhoneSystemStatusDto` (§6.3) | REQUIRED | The `last checked 14:32` sub-line, currently omitted |
| Recovery signal (§6.2) | REQUIRED | Clearing a fault without waiting for the next call |
| `PhoneCallStateDto.CallId` (§6.5) | Strongly requested | Correlating a failure to a specific call |
| `POST /api/phone/bell-failure/ack` (§6.6) | Requested | Durable dismissal across restarts |
| `POST /api/phone/bell/probe` (§6.7) | Requested | Making "Check again" a real probe rather than a status re-read |

File the request at `D:\prj\RotaryPhone\docs\prompts\radioconsole-bell-failure-request.md`,
following the convention set by `radioconsole-gv-markread-readstate-request.md`.

### Gotchas

- **Radio.Web is UI-only for phone functionality.** Consume RotaryPhone.API over REST/SignalR;
  never register RotaryPhone backend services here.
- **`Ht801Reachable == null` must never alarm** (§7m). It means "not probed / cannot
  determine". The original bug was rendering it as a red **Offline** on every cold load;
  `PhoneDashboardPanelTests.BellPill_NullReachable_RendersUnknown_NotOffline` pins this.
- **Non-obvious edge cases** the naive implementation gets wrong — handoff §7 (f), (h), (i),
  (m): a failure arriving after the call ended must NOT show a live strip; a later successful
  ring is the strongest recovery evidence; `[ Dismiss ]` must not silence a live fault; and
  absence of evidence is not a fault.
- **Reduced motion makes the glyph load-bearing.** `design-system.css:1675` zeroes all
  animation globally, so withdrawing `.ring-pulse` communicates nothing to those users. The
  crossed-bell glyph and text are mandatory channels, not redundancy (§4, §8.3).
- **`role="alert"` for the live strip only** (§8.1). It must be keyed stably or it re-announces
  on every hero re-render — the hero re-renders on each `InCall` duration tick.
- Remaining CSS for those states is specified verbatim in handoff §10:
  `.phone-hero-alert.is-warn` and `.phone-hero-state-label-fault`. Both were deliberately left
  out of the first PR rather than shipped unused.

---

## 14. Event-Playback Seam — Two Defects `PHN-1a` Deliberately Did Not Fix

**Status:** Both found while planning `PHN-1a` (ADR-029 PR 1); one fixed there, one logged here
**Added:** 2026-09-02
**Priority:** Medium — a capability the UI advertises does not work, but nothing that worked is broken

### 14a. `FilePlayerAudioSource.IsSeekable` claims a seek that does not move any audio

#### What Exists

`FilePlayerAudioSource.cs:119` declares `public override bool IsSeekable => true;`.
`SeekCoreAsync` (`:910-922`) range-checks its argument and then executes `_position = position;`.
**It assigns a field.** No audio is repositioned — and `_position` is the very field `Position`
(`:116`) reads back, so a seek reports success, moves the readout, and changes nothing audible.
`/api/audio` and the UI therefore report a new position while the audio keeps playing from where
it was.

Found while planning `PHN-1a`. **ADR-029 §8.3 asserts the opposite** — that this source "already
implements seeking over a local file through `SoundFlowPlaybackService`" — and is wrong on both
halves: the seek does not work, and `SoundFlowPlaybackService` had no seek method at all until
`PHN-1a` Task 4 added one.

#### What's Needed

`SoundFlowPlaybackService.Seek(sourceId, position)` — **added by `PHN-1a` Task 4** — called from
`SeekCoreAsync`, with `_position` becoming a read-through to `SoundFlowPlaybackService.GetPosition`
rather than an independently tracked field. Roughly ten lines.

#### Gotchas

- This is a **live primary-source path**, not a dormant one. The `/queue` scrubber and the
  persisted resume position both read that field — `StopCoreAsync` writes
  `_preferences.CurrentValue.SongPositionMs` from `_position` at `:903`, and `:987` does the same
  on the track-change path. Turning `_position` into a read-through changes what gets persisted.
- It therefore needs **its own UAT on the box** — play a file, scrub, stop, restart, confirm the
  resume position is still right — rather than riding along with an unrelated change. That is why
  `PHN-1a` logged it instead of smuggling it in.
- `SoundPlayerBase.Seek` returns `bool`. Propagate it; do not report an unconditional success,
  which is the same failure this entry is about.

### 14b. `SoundFlowPlaybackService.GetPosition` was a stub behind a false comment (fixed in `PHN-1a`)

`GetPosition` returned `TimeSpan.Zero` behind the comment
*"Position tracking not available in current SoundFlow API"*. The comment was **false** for the
referenced package: `SoundPlayerBase.Time` exists in SoundFlow 1.4.x (`<PackageReference
Include="SoundFlow" Version="1.*" />`, restored 1.4.1) and reports the current playback time in
seconds. `PHN-1a` Task 4 corrected the method and deleted the comment; the method had zero callers
repo-wide, so the correction had no behavioural blast radius.

Recorded here because the **pattern** is the point, not the fix: this is the fifth known instance of
the `CLAUDE.md` § Pre-Merge Review failure class — a comment asserting more than the code does.
**Three** are catalogued in `CLAUDE.md` itself: `SoundFlowMasterMixer`'s "Removed audio source" log,
`BluetoothAudioSource`'s unreachable `Playing` branch, and `GoogleCastOutput._lifecycleLock`'s
over-claimed guard. The fourth is 14a above, found in the same `PHN-1a` planning pass and recorded
here rather than there. A wrong comment survives the code it described, and the next
engineer debugs the description instead of the behaviour: in this case it kept a working API
looking unavailable for as long as anyone read the comment instead of the package.

---

## 15. `PhoneContactLookupService` logs raw phone numbers on four lines, and the contact's full name on the masked one

**Status:** Found while planning `PHN-1b` (ADR-029 PR 2); deliberately not fixed there
**Added:** 2026-09-02
**Priority:** Medium — not a live leak today, but it becomes one the moment `PhoneIntegration:Enabled` flips

### What Exists

ADR-029 §5.1 points at this file as the **example** of "log-masking discipline (it masks numbers to
`***1234`)". Re-verified against the current tree, it masks on one line and logs raw on four:

| `src/Radio.Infrastructure/External/PhoneContactLookupService.cs` | What it logs |
|---|---|
| `:62` | `LogInformation("PBAP contact found: {Number} → {Name}", phoneNumber, contact.DisplayName)` — **raw number *and* the contact's real name** |
| `:78` | `LogDebug("Looking up contact for {PhoneNumber}", phoneNumber)` — raw |
| `:87-90` | the one masked line — but it still logs `contact.Name` **in full**, and ADR-029 §5.1 asks for voicemail ids **and caller identity** to be masked |
| `:96-97` | `LogDebug("Contact lookup returned {StatusCode} for {PhoneNumber}", ..., phoneNumber)` — raw |
| `:102` | `LogWarning(ex, "Contact lookup failed for {PhoneNumber}", phoneNumber)` — raw, **and at Warning**, so since `LOG-11` this is one of the few that still reaches `journalctl` |

A fifth leak the "mask every line" rule does not by itself cover: `:102` logs `ex`, so any exception
whose `Message` carries a raw identifier leaks through **every** caller's catch block.

### What's Needed

Compute the masked form **once at method entry** and use only it thereafter; mask `contact.Name` the
same way; make sure no raw identifier can reach an exception message either. Roughly fifteen lines.
`GvMediaClient` is the worked example of the stronger rule — a raw id reaches no log message, no log
argument and no exception message — and `GvMediaClientTests.TheRawMediaIdNeverReachesALogLineOrAnExceptionMessage`
is the shape of test that pins it.

### Gotchas

- **The mask shape is not one-size-fits-all.** `***1234` is right for a phone number because a human
  recognises one by its last four digits. It is wrong for an opaque identifier, where a suffix leaks
  four characters for zero operator benefit — `GvMediaCache.MaskFor` uses a SHA-256 hash prefix
  (`gvm:1a2b3c4d`) instead, which still correlates log lines with each other and with the file on
  disk.
- **ADR-029 §5.1 cites this file as the example to follow, so the defect is actively propagating.**
  Any new code told to "follow `PhoneContactLookupService`'s masking" will copy the wrong half.
- `PHN-1b` deliberately did **not** fix it: it is a live path with its own callers and its own arm of
  the phone integration, and `PhoneIntegration:Enabled` is `false` and has never been true — so this
  is not leaking today, and widening a voicemail-cache PR into a phone-integration one is how a
  Medium change becomes a risky one.
## 16. Radio presets (found by `ENC-7`, deliberately not fixed there)

`ENC-7` put the preset bank behind a knob and read the stack end to end doing it. Four things it found and
left alone, because each is a change to a shipped surface with existing callers rather than an encoder change.

### 1. `RadioPresetService` signals "bank full" and "duplicate" through exception **message text**

`AddPresetAsync` throws `InvalidOperationException` for both, and the only thing distinguishing them is the
wording: `"Maximum of 50 presets reached. Please delete an existing preset first."` and
`"A preset already exists for {band} - {frequency}: {name}"`.

**Two call sites now match on those strings.** `RadioController` routes them to 409 / 400
(`ex.Message.Contains("already exists")` / `.Contains("Maximum")`), and `ENC-7`'s
`PresetSelectorService.SaveAsync` uses a `when (ex.Message.Contains("Maximum", StringComparison.Ordinal))`
filter to reach its `PRESETS FULL` notice. Both are commented as debt rather than defended.

#### What's Needed

A typed exception pair (`PresetLimitReachedException` / `DuplicatePresetException`) or a result object, and
the two call sites moved onto it. **Gotcha:** it is a breaking change to a shipped service, and the
controller's status-code mapping has to move with it or the API starts returning 500s for a full bank. Both
callers must change in the same PR; a half-migration leaves one of them matching text that no longer exists.

### 2. `RadioApiService`'s preset mutations return `bool` and discard the server's DTO

`SavePresetAsync`, `LoadPresetAsync` and `RenamePresetAsync` all return `bool`, throwing away the
`RadioPresetDto` the server sent back — so every touchscreen mutation is followed by a full refetch of the
whole bank to learn what it already knew.

`RenamePresetAsync`'s XML doc is also **wrong**: it promises *"the new name on success"* and returns a
`bool`. That is the `CLAUDE.md` § Pre-Merge Review failure class — a comment asserting more than the code
does — in a doc comment on a shipped method.

#### What's Needed

Return the DTO (nullable for the failure case) and have the callers apply it locally. Fix the doc comment
with it, or independently and immediately — it costs nothing and it is actively misleading today.

### 3. `RadioPreset` ordering is derived three different ways in one stack

- the repository sorts by `Name`;
- `RadioControlPanel` re-sorts by band, then per-band slot, then creation time;
- `RadioPage` sorts by `CreatedAt`.

`ENC-7`'s knob follows `RadioControlPanel`, because that is the bank it is a remote control for, which makes
**four** derivations of one order. There is no stored slot number — neither `RadioPreset` nor the
`RadioPresets` table has one, and `RadioController.GetPresets` recomputes the per-band ordinal on every
request — so this is a presentation decision repeated in four places rather than a data problem.

#### What's Needed

One ordering helper the repository, both pages and the selector share. **Gotcha:** do not "fix" it by adding
`SlotNumber` to the persisted model. Deleting a preset renumbers the ones after it within its band, so a
stored ordinal becomes state that every delete has to maintain, in exchange for saving a projection.

### 4. `RadioPreset.GetDefaultName` formats hertz as display units

`RadioPreset.Frequency` holds **hertz** (a `double`). `GetDefaultName(band, frequency)` formats it as though
it were display units — `RadioBand.FM => $"FM - {frequency:F1}"` — so a real frequency produces
**`FM - 101500000.0`**, not `FM - 101.5`.

It is reachable: `RadioPresetService.AddPresetAsync` calls it whenever a caller passes a null or blank name.
`ENC-7`'s save path deliberately routes around it by always passing an explicit non-blank name built from
`new Frequency((long)hz).ToDisplayString()`, matching `RadioControlPanel.OpenSavePresetDialog`'s own
fallback, and its tests pin that the generated name contains no raw hertz.

#### What's Needed

Either take a `Frequency` rather than a `double`, or convert inside the method. **Gotcha:** it is a shipped
`public static` with other callers, and any fix changes the names those callers generate — so it needs a
look at every call site and at whatever is already stored in the database, rather than a one-line edit.

---

## 17. ~~Visualisation mode has no writer, and its broadcast chain is unreachable~~ — RESOLVED 2026-09-03 by `ENC-9`: the chain is deleted

> **Owner's decision: delete. Visualiser mode is now single-surface and local-only.**
>
> **What was removed**, so anyone wanting a second surface knows exactly what they have to rebuild:
> `VisualizationModeService` (with `CycleMode`, `ToggleEnabled`, `ModeChanged` and
> `VisualizationModeChangedEventArgs`), its DI registration in `AudioServiceExtensions`,
> `AudioStateUpdateService`'s field / resolve / subscribe / unsubscribe / `OnVisualizationModeChanged`
> broadcast, `AudioStateHubService`'s `VisualizationModeChanged` event and its `_hubConnection.On`
> handler, `VisualizationModeDto`, and `VisualizerPanel`'s `AudioStateHubService` injection, its
> subscription, `HandleModeChangedRemotely`, and the dispose-time unsubscribe.
>
> **What is lost: cross-client sync.** Change the visualiser mode on the kiosk and a phone browsing
> the same console will not follow. ⚠ **State that precisely rather than as a regression** — per the
> analysis below, *the picker never produced that sync either*. The knob was the only writer, so no
> user ever had cross-client sync from the picker; what is gone is the **mechanism** that would have
> carried it, not a behaviour anyone was using. **Rebuilding it means writing a writer, not just
> re-adding a listener** — which is precisely the mistake `ENC-9a` made, subscribing to a broadcast
> that could already never be sent.
>
> **What survives, verified on the appliance rather than reasoned about:** the six-segment picker. It
> mutates `VisualizerPanel`'s own private enum and never went through the service.
>
> The original analysis is kept below, unedited apart from this banner, because it is the record of
> how three layers of unreachable code came to ship.

`ENC-7` removed `VisualizationModeService` from `RotaryEncoderActionRouter`. The encoder was that service's
**only** writer, so the removal left a whole chain shipping with nothing to drive it.

### What is dead, precisely

- `VisualizationModeService.CycleMode` and `VisualizationModeService.ToggleEnabled` have **no callers** anywhere
  in `src/`.
- Therefore `VisualizationModeService.ModeChanged` **cannot fire**.
- Therefore `AudioStateUpdateService.OnVisualizationModeChanged` never runs, and the
  `VisualizationModeChanged` SignalR broadcast it makes is unreachable.
- Therefore `VisualizerPanel.HandleModeChangedRemotely` and its `StateHub.VisualizationModeChanged`
  subscription are dead, as is `VisualizationModeDto` on that path.

`VisualizerPanel`'s own comment on that subscription still describes the mode changing via "the encoder's
visualizer knob, another browser, or an API call" — none of those is true today. It is left in place only
because deleting the chain, not editing the comment, is the resolution.

### What is *not* lost

**The capability.** Home's six-segment picker calls `VisualizerPanel.SelectMode`, which mutates the panel's own
`_currentMode` and persists a preference; it never touched `VisualizationModeService`. The System Config
"Visualizer" tab is FFT size / smoothing / peak-hold and has no mode control at all. So a user can still choose
a visualisation exactly as before.

What *is* lost is **cross-circuit sync** — a mode change in one browser no longer reaches another. Note honestly
that the picker never produced that sync either: the knob was the only writer, so what stopped happening is
exactly the input `ENC-7` deliberately removed.

### What's Needed — `ENC-9`'s to resolve

Either **wire the picker through the service** (`SelectMode` → `VisualizationModeService`, so the broadcast fires
and the sync becomes real for the first time), or **delete the chain** (`CycleMode`/`ToggleEnabled`,
`ModeChanged`, the `AudioStateUpdateService` subscription and broadcast, the `VisualizerPanel` subscription and
`HandleModeChangedRemotely`). `ENC-7` did neither on purpose: the registration is kept because
`AudioStateUpdateService` still resolves the type, and a deletion spanning API, Infrastructure and Web is a
different row's change. **Gotcha:** `VisualizerPanelTests` asserts the panel *has* subscribed to
`VisualizationModeChanged`, so the delete option has to take that test with it.

---

## 18. `FingerprintDbContext` is a singleton over one unsynchronised `SqliteConnection`

**Pre-existing and widened by `ENC-7`, not introduced by it.**

`FingerprintDbContext` is registered `AddSingleton` (`FingerprintingServiceExtensions`) and
`GetConnectionAsync` returns the **same** `SqliteConnection` instance to every caller. Its only lock,
`_initLock`, guards *initialisation* — it is released before the connection is handed out and does not
serialise use of it. `Microsoft.Data.Sqlite` connections are **not** thread-safe.

The scoped registrations above it (`IRadioPresetRepository`, `IRadioPresetService`) do not change this: a scope
yields a fresh repository object over the same singleton connection, so scoping buys **lifetime legality, not
isolation**. Both `PresetSelectorService` and `AddRotaryEncoders` are commented to say exactly that, so the
comments do not imply an isolation that does not exist.

### Why `ENC-7` widened it

Before this row the callers were HTTP request threads and `BackgroundIdentificationService`. `ENC-7` adds
another non-request-thread caller: the HID read loop and the fire-and-forget continuations it starts
(`PresetSelectorService.RefreshAsync` / `RecallAsync` / `SaveAsync`). A knob turn and an HTTP request can now
touch the same connection concurrently.

### What's Needed

Either serialise access (a `SemaphoreSlim` taken for the whole of each repository method) or stop sharing one
connection (open per-operation connections against the same file and let SQLite's own locking do the work).

**Gotcha 1:** the lock has to span the whole method, not the `ExecuteReaderAsync` call. Every repository here
drains its reader after the command returns, so a lock released at the command boundary would let a second
caller issue against the same connection while the first is still reading.

**Gotcha 2:** serialising individual commands does not make the *service* atomic. `RadioPresetService.
AddPresetAsync` is three separate repository calls — count, existence check, insert — so its own
check-then-write window survives any per-command lock. That window is what `PresetSelectorService`'s duplicate
catch arm exists to report.

Whichever way it goes, it is a change to the whole fingerprinting data layer, not to the encoder row that
noticed it.

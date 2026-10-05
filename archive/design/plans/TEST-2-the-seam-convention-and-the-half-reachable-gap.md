# PLAN — `TEST-2` · The native harness was never needed — the row's premise is false, and the gap closes for real

> **Row:** `TEST-2`, [`docs/queue/TEST-2.md`](../../docs/queue/TEST-2.md). Lowest priority of the
> 2026-08-10 tranche. Anchors last re-verified by the row itself 2026-08-11 against `8b1ce0a`.
> **Branch:** `test/bt-capture-branch-dispatch-coverage`
> **Estimate:** **1.0 d**. §0.10 derives it.
> **Auto-mergeable on green gates — but ONLY if §4.1's falsification step is run and recorded.** §0.11.
> **Planned against** `main` at **`a529ccf7`**. Every line number below was read out of the tree at
> that commit. ⚠ The planning session's working directory was checked out on
> `fix/gv-texts-polish-overflow-unread-align` (a Builder is mid-cycle); that branch's diff against
> `main` touches only `PhoneTextsPanel.razor`, `PhoneTextsPanelTests.cs` and `docs/BUILDER_QUEUE.md`,
> none of which this plan cites, so every anchor below is equally true of `main`. Verified, not assumed.
> **Nothing on the box was touched.** No SSH, no `curl`, no deploy. Every claim is from source, from
> `git`, or from decompiled IL of the pinned `SoundFlow 1.4.1` assembly (§0.4 says which).

---

## 0. Read this before Task 1

### 0.1 ⭐ This row **CLOSES — and it closes by BUILDING the coverage, not by recording its absence**

**The literal feasibility question is answered NO:** a native SoundFlow `AudioEngine` cannot be stood
up in a unit test on this CI (§1.1). The row anticipated that and named a fallback — write the seam
convention instead.

**But the question was the wrong one, and answering it honestly makes the row close better than it
expected.** Producing an `AudioCaptureDevice` or a `SoundComponent` **never required a native engine**.
Both are abstract SoundFlow types whose constructors merely store the engine reference without ever
dereferencing it, so Moq can subclass either with a null engine — which this repo has already been
doing for `SoundComponent`, in CI, since before the row was filed. Nobody checked whether the same was
true of `AudioCaptureDevice`. It is (§0.4).

So the branch-dispatch gap the row exists to record is **not infeasible to close. It is closable this
afternoon, with no native engine, no hardware, and no new seam** — and once it is closed, the
`internal` seam at `BluetoothAudioSource.cs:454` has no remaining justification and is **retired**
(Task 6). That discharges `AUD-3` residue (c) by *deleting* one of the seams it complained about
rather than merely labelling it.

The convention is still written (Tasks 1–2) — two Cast seams remain genuinely justified and `AUD-5`
adds a third — but it is no longer this row's only deliverable.

⚠ **One confidence caveat, stated up front because the plan leans on it.** The `AudioCaptureDevice`
finding is derived from the type's decompiled shape, **not** from an executed test — no
`Mock<AudioCaptureDevice>` exists in the repo today. §4.1 makes proving it Task 5's first action, and
§1.3 states exactly what happens if it fails.

### 0.2 ⚠⚠ `C-214` — THE ROW'S CENTRAL PREMISE IS FALSE

`docs/queue/TEST-2.md:26` says:

> **Why, and it is already recorded in the code:** constructing **either** type needs a **native
> SoundFlow `AudioEngine`**, which the method's own doc comment states at
> `BluetoothAudioSource.cs:449-452` — that constraint is exactly why the seam is `internal` +
> `InternalsVisibleTo` in the first place.

**Neither type needs one.** The claim is false for `SoundComponent` by demonstration and false for
`AudioCaptureDevice` by construction.

**`SoundComponent` — refuted by a test that has been green in CI for months.**
`tests/Radio.Infrastructure.Tests/Audio/WasapiLoopbackTests.cs:68-69`:

```csharp
var mockComponent = new Mock<global::SoundFlow.Abstracts.SoundComponent>(
  MockBehavior.Loose, null!, default(global::SoundFlow.Structs.AudioFormat));
```

Moq subclassing the abstract type **with a null engine**. The test drives `InitializeAsync` end-to-end
into the real `capture is SoundComponent` arm at `BluetoothAudioSource.cs:166` and asserts the real
state and metadata consequences (`:93-97`). It carries no `Category=Integration` trait and is not
under `Radio.Web.E2ETests`, so `.github/workflows/build.yml:58`'s filter runs it on every push, and it
is absent from `CLAUDE.md`'s known-failing list.

**So one of the three dispatch sites the row calls uncovered — `:159`/`:166` — has been covered
end-to-end this whole time.** The row's *"Three places make the same `capture is …` decision and none
is covered end-to-end"* (`docs/queue/TEST-2.md:30`) was already untrue when written.

### 0.3 ⚠ `C-215` — the second dispatch site is reachable through a public event

`:483`/`:494` sits inside `TryAcquireAudioCaptureAsync` (`:462`), which is `private` — but **not
unreachable**. It is event-driven:

- `OnDeviceConnected` (`:361`) fires it at `:370`; `OnCaptureStreamRecovered` (`:373`) fires it at `:381`.
- `IBluetoothService.DeviceConnected` is a **public interface event**
  (`src/Radio.Core/Interfaces/Audio/IBluetoothService.cs:110`), so `Mock<IBluetoothService>` can raise it.
- `BluetoothAudioSourceTests.DeviceConnected_UpdatesMetadata` (`:94`) **already triggers this path
  today** via `SimulateConnection` (`:104`). It reaches the `else` at `:505` only because
  `MockBluetoothService.GetAudioCaptureDeviceAsync` returns a bare string — the repo's own comment says
  so at `BluetoothAudioSourceTests.cs:910`.

**And the thing that looks like it would block a test — mixer routing — does not.**
`RouteCaptureThroughMixerAsync` (`:521`) is guarded on `_playbackService` in both arms (`:535`, `:576`).
`_playbackService` is a nullable **optional constructor parameter** (`:34`, `:93`) that neither
`BluetoothAudioSourceTests` (`:41-47`) nor `WasapiLoopbackTests` (`:85-91`) passes. With it null both
arms fall through and the `finally` releases `_routeLock` (`:604-607`).

⭐ **Consequence worth stating separately: the mock is never invoked.** With `_playbackService` null,
no method is called on the capture object in any of the three dispatch paths — `:159-173`, `:483-503`,
`:684-693` only assign it, read metadata, and set state. A `MockBehavior.Loose` proxy that is only ever
stored is as safe as a stub can be.

### 0.4 ⭐ `C-216` — `AudioCaptureDevice` is mockable too, and this is the finding that changes the row

Read out of `SoundFlow 1.4.1` (`src/Radio.Infrastructure/Radio.Infrastructure.csproj:32`; resolved at
`C:/Users/mark/.nuget/packages/soundflow/1.4.1/lib/net8.0/SoundFlow.dll`) by reflection and decompilation:

```csharp
namespace SoundFlow.Abstracts.Devices;
public abstract class AudioCaptureDevice : AudioDevice
{
    protected AudioCaptureDevice(AudioEngine engine, AudioFormat format, DeviceConfig config)
        : base(engine, format, config) { }   // the ONLY constructor
    // Start(), Stop(), Dispose() remain public abstract, inherited from AudioDevice
}

// and the base, in full:
protected AudioDevice(AudioEngine engine, AudioFormat format, DeviceConfig config)
{
    Format = format;
    Engine = engine;
    Config = config;
}
```

Every property that matters:

- **Abstract, not sealed**, with abstract `Start`/`Stop`/`Dispose` for Castle DynamicProxy to override —
  structurally identical to `SoundComponent`, which Moq already proxies here.
- **The constructor never dereferences `engine`.** It stores it. `null!` is as safe as it is in the
  `SoundComponent` call that already works.
- **`DeviceConfig` is `public abstract class DeviceConfig { }`** — an empty reference type, so `null` is
  a legal third argument.
- The sole constructor is `protected`, which is not an obstacle: Moq invokes protected constructors.

So this should work, by the same mechanism already proven in this repo:

```csharp
new Mock<global::SoundFlow.Abstracts.Devices.AudioCaptureDevice>(
  MockBehavior.Loose, null!, default(global::SoundFlow.Structs.AudioFormat), null)
```

**What is genuinely native is the engine, not the device.** `MiniAudioEngine`'s constructor calls
`InitializeBackend()` → `Native.AllocateContext()` / `Native.ContextInit()` synchronously, and the only
concrete capture device, `MiniAudioCaptureDevice`, is `internal sealed` and obtainable solely through
`AudioEngine.InitializeCaptureDevice`. **All of that is true and all of it is irrelevant** — the code
under test does a type check and an assignment, and a proxy satisfies both.

⚠ **This is inference from type shape, not an executed test.** No `Mock<AudioCaptureDevice>` exists in
the repo. §4.1 proves it before anything is built on it; §1.3 says what happens if it fails.

### 0.5 ⚠ `C-217` — the FOURTH seam does not exist. There are THREE.

`docs/queue/TEST-2.md:42` (added by `AUD-5`'s plan, 2026-09-05) asserts:

> **Four seams now exist solely because a native/hardware dependency makes the real path untestable**

**Three do.** `GoogleCastOutput.CastStatusReadOverrideForTests` appears **nowhere in `src/`**. A
whole-repo grep finds it only inside `design/plans/AUD-5-stale-cast-volume-persists-as-master.md`
(`:377`, `:484`, `:486`, `:701`, `:745`) — *proposed*, not shipped. Corroborating: `AUD-5`'s status is
`📋` (`docs/queue/AUD-5.md:14`), and `GoogleCastOutput.SyncInitialVolumeAsync` is still the
zero-argument form at `GoogleCastOutput.cs:1543`, not the `(client, generation)` form that plan introduces.

**This matters because the row's strengthened conclusion is an argument from a recurrence count, and
the count is inflated by one.** The conclusion survives (§1.2), but the convention must be authored
against **three shipped seams plus one queued**, and Task 3 must not hunt for a member that is not there.

### 0.6 `C-218` — anchor re-derivation. Every `BluetoothAudioSource.cs` anchor held; both Cast anchors moved.

Re-derived at `a529ccf7`:

| Row's claim | At `a529ccf7` | Verdict |
|---|---|---|
| `BluetoothAudioSource.cs:159`/`:166` | both `capture is …` arms in `InitializeAsync` | ✅ exact |
| `BluetoothAudioSource.cs:449-452` (doc comment) | the `internal`-justification `<para>` is `:447-452`; its prose spans `:448-451` | ⚠ **off by one at both ends** |
| `BluetoothAudioSource.cs:454` | `internal void ApplyDeferredCaptureState()` | ✅ exact |
| `BluetoothAudioSource.cs:462` | `private async Task TryAcquireAudioCaptureAsync()` | ✅ exact |
| `BluetoothAudioSource.cs:483`/`:494` | both arms | ✅ exact |
| `BluetoothAudioSource.cs:684`/`:689` | both arms in `TryReacquireCaptureAsync` (`:670`) | ✅ exact |
| `BluetoothAudioSourceTests.cs:917`/`:930`/`:954` | the three `ApplyDeferredCaptureState()` calls | ✅ exact |
| `GoogleCastOutput.cs:54` `ConnectRaceHookForTests` | **`:120`** | ❌ **moved +66** |
| `GoogleCastOutput.cs:64` `ConnectTransportOverrideForTests` | **`:130`** | ❌ **moved +66** |

Both Cast anchors are also stale at their other source, `docs/BUILDER_QUEUE_ARCHIVE.md:349`
(`AUD-3` residue (c)), which still cites `:54` and `:64`.

### 0.7 `C-219` — the doc comment the row cites as its authority is itself an over-claiming comment

This points straight at `CLAUDE.md` § *Pre-Merge Review*, whose first checklist item is *"Do comments …
assert only what the code actually does?"*

`BluetoothAudioSource.cs:447-452`:

```csharp
  /// <para>
  /// <c>internal</c> for unit testing via <c>InternalsVisibleTo</c>: the real
  /// call sites require a native SoundFlow <c>AudioEngine</c> to produce an
  /// <c>AudioCaptureDevice</c>/<c>SoundComponent</c> and cannot be exercised
  /// directly in a unit test.
  /// </para>
```

**Every claim in it is false**, by §0.2–§0.4. The comment is the row's stated authority
(`docs/queue/TEST-2.md:26`, *"it is already recorded in the code"*), and the row inherited its error
wholesale — the exact failure mode `CLAUDE.md` gives three worked examples of, where *"the next
engineer debugs the description instead of the behavior."* **Here the next engineer was a queue row,
and the cost was thirteen months of a row premised on a constraint that does not exist.** Add it as a
fourth worked example (§5.3).

### 0.8 The AUD-3 residue (c) claim — ✅ **IT HOLDS, and this row over-delivers on it**

`docs/queue/TEST-2.md:40` says closing this way *"would also retire `AUD-3`'s residue (c)"*.
`docs/BUILDER_QUEUE_ARCHIVE.md:349`:

> **(c) Closing the test gap cost two `internal` seams** — `ConnectRaceHookForTests`
> (`GoogleCastOutput.cs:54`) and `ConnectTransportOverrideForTests` (`:64`) … Genuine design debt, and
> the **second** time this codebase has bought coverage with a seam — **folded into `TEST-2` as a
> pattern rather than left as an incident.**

Residue (c) explicitly delegates itself to `TEST-2` and asks only for the pattern treatment. Tasks 1
and 3 discharge it; Task 6 goes further and **deletes** the third seam rather than labelling it. ✅

⚠ **(a) and (b) are NOT retired and must not be recorded as such.** (a) is the never-performed
hardware UAT of the Cast race; (b) is the untested service-level epoch commit
(`AudioEngineInitializationService.cs:522`/`:569` gated on the concrete engine type while
`AudioEngineInitializationServiceStartupTests` supplies `Mock<IAudioEngine>`). Both are live. §7.4.

### 0.9 The seam taxonomy — the reason a convention is possible at all

The row treats "internal + `InternalsVisibleTo`" as one thing. It is four, and **they carry completely
different risk** — which is why no one-line rule works and a short convention does. Inventory across
`src/` at `a529ccf7`:

| Kind | What it does | Risk | Instances |
|---|---|---|---|
| **A · Visibility** | Widens access to a value production already computes. Path unchanged. | ~none | `BluetoothCaptureWatchdog.cs:51`, `TTSFactory.cs:432` + `:465`, `RadioBandService.cs:59`, `PhoneCallClient.cs:126`, `PhoneCallIntegrationService.cs:119`, `AudioStateStore.cs` (3), `NowPlayingPanel.razor:409` |
| **B · Injection** | Test-only writer for state otherwise unreachable. Path unchanged. | low | `NwsWeatherService.cs:725`, `:740`, `:754`; `BackgroundIdentificationService.cs:159`; `PhoneHubService.cs:161`, `:174` |
| **C · Substitution** | A delegate **production reads on every run**, short-circuiting a real collaborator. | **high** — a live branch in shipped code | `GoogleCastOutput.cs:120`, `:130` (+ `AUD-5`'s when it ships) |
| **D · Entry-point** | A real production method made `internal` so a test calls it **directly, bypassing the dispatch that selects it**. | **high — and uniquely deceptive** | `BluetoothAudioSource.cs:454` — **retired by Task 6** |

**Kind D is what this row is actually about, and nobody had a name for it.** A Kind-D test looks
exactly like coverage in a coverage report — the method executes, the assertion is real — while the
dispatch that selects it is untested. That is how `#469` pinned the state decision and left `:483`/
`:494` unpinned with nothing looking wrong.

⚠ **A name-suffix lint cannot find Kind D.** All nine suffixed members (`ForTest`/`ForTests`/
`ForTesting`) are Kinds A, B and C. `ApplyDeferredCaptureState` carries **no suffix** — it is a real
method with a real name — so a naming-keyed lint would miss the exact seam that motivated the row.
Task 7 keys on the **label**.

### 0.10 The estimate — **1.0 d**

| Task | | |
|---|---|---|
| 1 | Convention section in `design/TESTING.md` | 2.0 h |
| 2 | `ADR-030` in `design/DECISION-LOG.md` | 0.5 h |
| 3 | Retrofit the two Cast seams + fix the archive's stale anchors | 0.75 h |
| 4 | (subsumed by Task 6 — see there) | — |
| 5 | Prove `Mock<AudioCaptureDevice>`, then the four dispatch tests | 2.0 h |
| 6 | Retire the `ApplyDeferredCaptureState` seam; rewrite its three tests | 1.5 h |
| 7 | `TestSeamLabelLintTests` + canary | 1.0 h |
| | Gates, self-review, PR | 1.0 h |
| | **Total** | **≈ 8.75 h → 1.0 d** |

**Up from the 0.75 d this plan first estimated**, because §0.4 turned the row from *record the gap* into
*close it and delete the seam*. That is the right trade, but it is a real increase and is not hidden.

### 0.11 Auto-merge — **yes on green gates, conditional on §4.1**

1. **Production behaviour is unchanged.** Task 6 narrows `internal` → `private` (a visibility change
   with no runtime effect) and rewrites a doc comment; Tasks 1–3 and 7 touch docs and tests; Task 5
   adds tests. **No production statement changes.**
2. **Not sensitive.** No auth, secrets, migrations, production config or service files.
3. **UAT not applicable; the substitute is stated.** Not user-facing, so per policy the suite stands in.
4. **No hardware.** Nothing goes near the box — exactly what distinguishes this from `AUD-3` residue (a).

⚠ **Two things must NOT auto-merge:**
- **If §4.1's falsification step is skipped**, the new dispatch tests are unproven and the row must
  stop for review. A dispatch test that has not been shown to fail against a wrong dispatch is the
  thing this row exists to prevent.
- **If Task 7's lint cannot pass its own canary within its timebox** (§4.5), ship Tasks 1–6 and leave
  Task 7 out, recording why. **A lint that cannot prove it is looking is worse than no lint** (§Task 7.3).

### 0.12 ⛔ Things Builder must NOT do

- ⛔ **Do not build a native `AudioEngine` harness, provision `snd-dummy`, or `apt`-install an audio
  stack in CI.** §1.1. It is unnecessary (§0.4) as well as infeasible.
- ⛔ **Do not add a mock that re-asserts `ApplyDeferredCaptureState` from a new angle.** The row
  forbids it (`docs/queue/TEST-2.md:32`, `:44`) and it stays forbidden. Task 5 is the opposite — it
  drives the dispatch the seam bypasses. Read §0.3 and §7.4 before writing a line.
- ⛔ **Do not delete the two Cast seams.** `ConnectTransportOverrideForTests` is load-bearing for a
  path genuinely unreachable offline (`GoogleCastOutput.cs:122-128`). Only the Kind-D seam retires,
  and only because Task 5 removes its justification.
- ⛔ **Do not touch `docs/BUILDER_QUEUE.md`, `docs/queue/TEST-2.md`, `docs/queue/UX-1.md`,
  `docs/queue/AUD-13.md`, `design/plans/UX-1-*` or `design/plans/AUD-13-*`.** Other agents are in
  queue and plan files concurrently. §9 gives the queue row's replacement text; applying it is a
  separate later step.
- ⛔ **Do not modify `MockBluetoothService`** (`src/Radio.Infrastructure/Platform/Bluetooth/MockBluetoothService.cs`)
  to make Task 5 easier. It is **production** code shipped in `Radio.Infrastructure`, and bending a
  production double to serve one test is the exact trade this row exists to discipline. Use
  `Mock<IBluetoothService>`, as `WasapiLoopbackTests` already does.
- ⛔ **Do not renumber `C-NNN`.** §0.13.

### 0.13 ⚠ `C-NNN` numbering — collision warning

This plan uses **`C-214`…`C-219`**, continuing from `C-213`, the highest in the tree at `a529ccf7`
(`design/plans/UI-7-…md:424`, `design/plans/GV-9-…md:239`). ⚠ **`UI-7` and `GV-9` already collide with
each other** — both allocate `C-202`…`C-213` (`UI-7:264-268`, `GV-9:172-239`) — because they were
planned concurrently, which is the condition holding again now with two other Planners live. If a
concurrent plan also claims `C-214+`, **leave both and note the collision**; do not renumber a merged plan.

---

## 1. Decision

### 1.1 Native `AudioEngine` in a unit test: **NO.**

Three independent lines, none relying on the row's own assertion:

**(a) `MiniAudioEngine`'s constructor enters native code synchronously.** Decompiled from
`SoundFlow 1.4.1`: the ctor calls `InitializeBackend()`, which calls `Native.AllocateContext()` and
`Native.ContextInit(...)` and throws `InvalidOperationException` on failure. There is no lazy path.

**(b) CI is a headless container with no audio device.** `.github/workflows/build.yml:32` —
`runs-on: [self-hosted, linux, x64, appserver]`, on `myoung34/github-runner:ubuntu-noble` (`:39-41`).
No PipeWire, no ALSA sink; nothing in the workflow provisions one.

**(c) The repo forbids it in `src/` already.** `NoRawMiniAudioEngineConstructionTests.cs:37-78` fails
the build on any `new MiniAudioEngine(` outside the single gated factory `SerializedMiniAudioEngine.cs`.
A harness would fight the codebase's own stated design.

⚠ **The evidence the row and the task framing both offer for this is WRONG, and the correct reading
cuts the other way.** Both cite four `SrcVariableResamplerTests` failing on a missing
`libsamplerate.so.0` as evidence about native-library availability in CI. **CI installs
`libsamplerate0` explicitly** — `build.yml:37-45`, with a comment saying exactly why. Those four fail
**on Windows dev boxes only**; on CI they pass. So the true precedent is *this repo will happily
`apt install` a native dependency for a test* — an argument **for** feasibility. It still loses to (b),
because the blocker is a **device**, not a library, and `apt` cannot manufacture a sound card.
Recorded because the next person to re-open this will meet the same misleading citation.

### 1.2 …but the native engine was never the requirement, so the gap closes anyway

§0.2–§0.4. The code under test performs a **type check and an assignment**; a Moq proxy satisfies both,
and with `_playbackService` null nothing is ever called on it. **All three dispatch sites become
coverable**, and the Kind-D seam's justification evaporates.

**The convention is still written.** Two Cast seams remain genuinely justified — a fake socket cannot
complete a Cast handshake — and `AUD-5` adds a third. `AUD-3` asked for the pattern treatment and gets
it. What changes is that this row now *also* deletes the seam it was named after.

### 1.3 ⚠ If §4.1 falsifies §0.4 — the fallback, decided in advance

If `Mock<AudioCaptureDevice>` will not construct (Castle DynamicProxy refuses the protected ctor, or
`AudioDevice` turns out to have members the proxy cannot satisfy):

- **Task 5 keeps its two `SoundComponent` tests** — those rest on a mechanism already proven in CI and
  are unaffected.
- **Task 6 does NOT retire the seam.** Instead it applies the Kind-D label from Task 1, with *"Why the
  real path is unreachable"* naming the **actual** blocker discovered in §4.1 — not the false one the
  comment carries today.
- **Estimate drops to ~0.75 d**; the row still closes; §9's queue wording changes only in its first
  clause.

Either way the over-claiming comment (§0.7) is corrected, because it is wrong under both outcomes.

### 1.4 Home: **`design/TESTING.md`** for the convention, **`design/DECISION-LOG.md`** for the decision

Neither is invented. `design/TESTING.md` is the repo's testing document (`## Test Projects`,
`## Running Tests`, `## Integration Tests`, `## Unit Tests`, `## Test Configuration`); a testing
convention belongs in it as a new `##` between `## Unit Tests` and `## Test Configuration`.
`design/DECISION-LOG.md` holds 29 `## ADR-NNN` entries with a fixed Context/Decision/Alternatives/
Rationale/Consequences shape; the *decision to adopt* is one of those, as `ADR-030`.

⛔ **Do not create `design/conventions/`.** No such directory exists and one row does not justify
standing one up.

---

## 2. Tasks

### Task 1 — The convention, inserted into `design/TESTING.md`

Insert **between** the fence closing `### Common Patterns` (`design/TESTING.md:284`) and the `---` at
`:286`. Literal content:

````markdown
---

## Test Seams — when `internal` + `InternalsVisibleTo` is acceptable

A **test seam** is any member made more visible, or any branch added to production code, whose
justification is a test. This repository has a dozen-odd and will grow more. They are not banned —
some reach a path that hardware or a network genuinely makes unreachable — but an unlabelled seam is
indistinguishable from coverage, and that is how a gap hides.

**The rule is one sentence: a seam must say what it displaces.**

### The four kinds

Classify a seam before adding one. The kind determines what you owe the reader.

| Kind | Definition | What you owe |
|---|---|---|
| **A · Visibility** | Widens access (`private` → `internal`) to a value or method the production path already computes and reaches unchanged. | One line saying it is test-only. |
| **B · Injection** | A test-only method that writes state the test could not otherwise establish. Production path unchanged. | One line, plus why the state is otherwise unreachable. |
| **C · Substitution** | A field or property that production code **reads on every run** and that, when set, replaces a real collaborator. | The full label below. It adds a live branch to shipped code. |
| **D · Entry-point** | A real production method made `internal` so a test can call it **directly**, bypassing the dispatch that normally selects it. | The full label below. **This is the deceptive one.** |

**Kinds A and B are cheap and need no ceremony.** They change nothing about what runs in production;
the seam is an observation post. Prefer them — if a Kind-C or Kind-D seam can be restated as an A or B,
restate it.

**Kinds C and D are debt and must be labelled.** Kind C puts a branch in shipped code that exists only
for tests. Kind D is worse in one specific way: **a test entering through a Kind-D seam looks like
coverage in every coverage report** — the method really executes, the assertion is real — while the
dispatch that would have selected it is untested. Nothing distinguishes the two from outside. The
label is the only thing that does.

### The label

Every Kind-C and Kind-D seam carries this block in its XML doc, verbatim in shape:

```csharp
/// <para>
/// <b>Test seam (kind D — entry point).</b> Reached directly from
/// <c>SomeTests.SomeTest</c> via <c>InternalsVisibleTo</c>.
/// <b>Why the real path is unreachable:</b> &lt;the concrete blocker — a device, a
/// socket, a handshake; not "it is hard"&gt;.
/// <b>NOT covered by this seam:</b> &lt;the specific dispatch, branch or collaborator
/// the seam bypasses, with file:line&gt;.
/// </para>
```

Three parts, and the third earns the label:

- **kind** — `A`, `B`, `C` or `D`, with its word. An author who cannot pick a kind usually has two seams.
- **Why the real path is unreachable** — a *mechanism*, not a difficulty. "A fake socket cannot
  complete a Cast handshake" is a mechanism. "Hard to set up" is not, and is a sign the seam is
  unnecessary.
- **NOT covered by this seam** — the gap, in the file, beside the thing that caused it. This is the
  clause the convention exists for. If the answer is "nothing — it only observes", the seam is Kind A
  or B and does not need this block.

### Rules

1. **Prefer no seam. Check whether the real path is reachable before assuming it is not.** It very
   often is, and this repository has already been wrong about it once, expensively:
   - `Mock<T>` can subclass an abstract SoundFlow type **with a null engine** — both `SoundComponent`
     and `AudioCaptureDevice` store the engine reference without dereferencing it. Only the *engine*
     is native.
   - A `private` method reached by an interface event is drivable by raising that event on the mock
     (`IBluetoothService.DeviceConnected`).
   - A collaborator held in an **optional nullable constructor parameter** is often absent in tests,
     making the code it guards a safe no-op.
2. **Prefer Kind A or B over C or D.** Observe rather than substitute wherever the assertion allows.
3. **A Kind-C seam must be inert in production** — null-checked, defaulting to the real collaborator,
   never settable from production code.
4. **A Kind-D seam does not close a branch-dispatch gap**, and a test using one must not be described
   as if it did. Pin the dispatch separately, or record that it is unpinned.
5. **Never close a coverage gap with a second seam that re-asserts the first from another angle.** That
   adds a test without adding coverage and makes the gap harder to see.
6. **A labelled seam is a standing invitation to delete it.** When the blocker goes away, the seam and
   its label go with it. `ApplyDeferredCaptureState` was a Kind-D seam from PR #469 until `TEST-2`
   showed the blocker never existed; it was retired rather than labelled.

### ⚠ The reason rule 1 leads

`BluetoothAudioSource.ApplyDeferredCaptureState` was made `internal` in PR #469 with a doc comment
asserting that the real call sites *"require a native SoundFlow `AudioEngine` … and cannot be exercised
directly in a unit test."* **That was never true.** A test mocking `SoundComponent` with a null engine
was already green in CI at the time. The comment was then cited as authority by queue row `TEST-2`,
which sat open for thirteen months waiting for a native harness nobody needed.

The lesson is not "seams are bad". It is that **the sentence justifying a seam is a technical claim and
gets checked like one** — see `CLAUDE.md` § *Pre-Merge Review*.

### Enforcement

`TestSeamLabelLintTests` (`tests/Radio.Core.Tests/TestSeamLabelLintTests.cs`) asserts that every Kind-C
and Kind-D seam carries a complete label. It is a **regression lint over the seams that exist**, not a
proof that no unlabelled seam can be added — a new seam in a shape it does not recognise passes. Read
its class remarks before trusting a green run.
````

### Task 2 — `ADR-030` in `design/DECISION-LOG.md`

Append after the last entry (`## ADR-029 Amendment 2`, `design/DECISION-LOG.md:494`):

````markdown
---

## ADR-030: Test seams are classified and labelled, not banned

**Date:** 2026-09-08
**Status:** Accepted

### Context

Four rows bought test coverage with an `internal` + `InternalsVisibleTo` seam because a native or
network dependency was believed to make the real path unreachable: `ApplyDeferredCaptureState` (#469),
`ConnectRaceHookForTests` and `ConnectTransportOverrideForTests` (#468), and
`CastStatusReadOverrideForTests` (planned by `AUD-5`). `AUD-3` recorded the second and third as design
debt and folded them into `TEST-2` "as a pattern rather than an incident".

`TEST-2` asked whether a native SoundFlow `AudioEngine` could be constructed in a unit test so the
seams could be retired. **It cannot** — `MiniAudioEngine`'s constructor enters native code, CI is a
headless container with no audio device, and the repo separately forbids raw `MiniAudioEngine`
construction in `src/` (`NoRawMiniAudioEngineConstructionTests`).

**Planning the row established that the native engine was never the requirement.** `SoundComponent`
and `AudioCaptureDevice` are abstract types whose constructors store the engine reference without
dereferencing it, so Moq subclasses either with a null engine — which the suite had already been doing
for `SoundComponent` in CI since before the row was filed. The seam at `ApplyDeferredCaptureState` was
therefore never necessary, and its doc comment — cited by the row as its authority — asserted a
constraint that did not exist.

That surfaced the sharper problem: the seams are **not one thing**. Most are harmless visibility
widenings. Two put a live branch in shipped code. One was a real production method entered directly by
tests, **bypassing the branch dispatch that selects it**, and such a test is indistinguishable from
real coverage in any coverage report. The gap left at `BluetoothAudioSource.cs:483`/`:494` went
unnoticed for exactly that reason.

### Decision

Seams are **classified into four kinds by what they displace**. Kind C (substitution) and Kind D
(entry point) must carry a label naming the mechanism that makes the real path unreachable **and what
the seam consequently does not cover**. Kinds A (visibility) and B (injection) need only a line. The
convention lives in `design/TESTING.md` § *Test Seams* and is enforced by `TestSeamLabelLintTests`.

Its first rule is **prefer no seam, and check reachability before assuming** — because the failure this
ADR is written from was not a bad seam, it was an unchecked sentence.

### Alternatives considered

- **Ban the seams.** Rejected: `ConnectTransportOverrideForTests` reaches a path genuinely unreachable
  offline; removing it would delete real coverage.
- **Build a native test harness.** Rejected: infeasible on this CI, and — the more useful finding —
  unnecessary, since the types under test are mockable without an engine.
- **A single "avoid `InternalsVisibleTo`" guideline.** Rejected: it would be ignored for the ~13
  harmless Kind-A/B seams, and a rule ignored in the common case is unavailable in the rare one.
- **A naming convention (`*ForTests`) plus a name-based lint.** Rejected, and this is the load-bearing
  rejection: all nine suffixed members are Kinds A–C. `ApplyDeferredCaptureState` carried no suffix, so
  a name-keyed lint would have missed **the exact seam that motivated the decision**. The lint keys on
  the label.

### Consequences

- Two shipped Cast seams retrofitted; `AUD-5` applies the label to its own when it ships.
- The Kind-D seam is **retired**, not labelled: `ApplyDeferredCaptureState` returns to `private` and its
  three tests are rewritten to enter through the real dispatch.
- All three `capture is …` dispatch sites gain end-to-end coverage with no seam and no hardware.
- The over-claiming comment at `BluetoothAudioSource.cs:447-452` is corrected, and is added to
  `CLAUDE.md` § *Pre-Merge Review* as a fourth worked example — the first whose victim was a queue row
  rather than a code change.
````

### Task 3 — Retrofit the two shipped Cast seams

**3a · `GoogleCastOutput.cs:113-120`** — replace `ConnectRaceHookForTests`' doc:

```csharp
  /// <summary>
  /// <b>Test seam (kind C — substitution).</b> Awaited inside <see cref="ConnectAsync"/>
  /// after the receiver has been resolved but before the network connect, which is
  /// precisely where the connect/teardown race used to corrupt state. Set by
  /// <c>GoogleCastOutputConcurrencyTests</c> (<c>:51</c>, <c>:116</c>).
  /// <b>Why the real path is unreachable:</b> the window is microseconds wide, so a stress
  /// loop lands on it only by luck; the hook makes the interleaving deterministic.
  /// <b>NOT covered by this seam:</b> nothing — it inserts a pause, it does not replace a
  /// collaborator. The connect either side of it is the real one.
  /// Null (and therefore free) in production.
  /// </summary>
```

**3b · `GoogleCastOutput.cs:122-130`** — replace `ConnectTransportOverrideForTests`' doc:

```csharp
  /// <summary>
  /// <b>Test seam (kind C — substitution).</b> Substitutes the SharpCaster transport
  /// connect. Set by <c>GoogleCastOutputConcurrencyTests:121</c>.
  /// <b>Why the real path is unreachable:</b> a fake socket can never complete a Cast
  /// handshake, so offline the connect always throws and diverts into the error handler —
  /// making the supersede-after-a-SUCCESSFUL-connect path, which is the whole point of the
  /// generation check, unreachable without hardware.
  /// <b>NOT covered by this seam:</b> the real SharpCaster handshake and everything its
  /// failure modes imply. The generation check, the publish and the teardown either side
  /// are real; the socket is not. Only hardware UAT covers the transport itself — and per
  /// <c>AUD-3</c> residue (a), that UAT has never been performed.
  /// Null (and therefore free) in production.
  /// </summary>
```

**3c · Fix the stale anchors in `docs/BUILDER_QUEUE_ARCHIVE.md:349`** — `(GoogleCastOutput.cs:54)` →
`(GoogleCastOutput.cs:120)`, `(:64)` → `(:130)`. Append inside residue (c):

> _Anchors re-derived 2026-09-08 at `a529ccf7`; both had moved +66. **Residue (c) is DISCHARGED by
> `TEST-2`** — labelled per ADR-030, and the third seam it names (`ApplyDeferredCaptureState`) was
> retired outright rather than labelled. **(a) and (b) remain open.**_

⚠ This is the **archive**, not `BUILDER_QUEUE.md` or a `queue/` dossier. If another agent has it open,
defer 3c and say so in the PR body.

### Task 4 — Correct the over-claiming doc comment

**Subsumed by Task 6** (the seam is retired, so the comment goes with it) — or, under §1.3's fallback,
by Task 6's labelling variant. Kept as its own line item so the PR body and queue row can name it:
`CLAUDE.md` § *Pre-Merge Review* makes "a comment asserting an invariant the code does not enforce" a
standing review item, and this is a worked instance found by planning rather than by review.
**If Builder ships nothing else, ship this.**

### Task 5 — Cover all three dispatch sites through the real path

**5a — first, prove §0.4.** Before writing anything else, add one throwaway assertion and run it:

```csharp
var probe = new Mock<global::SoundFlow.Abstracts.Devices.AudioCaptureDevice>(
  MockBehavior.Loose, null!, default(global::SoundFlow.Structs.AudioFormat), null);
Assert.NotNull(probe.Object);   // delete once proven; §4.1 records the outcome either way
```

If it throws, **stop and go to §1.3.** Record the exception verbatim in the PR body — a negative here
is a real finding and the first time the claim will have been tested rather than repeated.

**5b — the tests.** Add to `tests/Radio.Infrastructure.Tests/Audio/BluetoothAudioSourceTests.cs`:

```csharp
  // -----------------------------------------------------------------------
  // Branch-dispatch coverage (TEST-2). These enter through the real event path
  // — DeviceConnected -> OnDeviceConnected (:361) -> TryAcquireAudioCaptureAsync
  // (:370) -> the real `capture is ...` arms at :483 / :494 — rather than by
  // calling ApplyDeferredCaptureState directly. That distinction is the point of
  // the row: entering at the method executes the state decision without executing
  // the dispatch, which reads as coverage and is not. design/TESTING.md § Test Seams.
  //
  // RouteCaptureThroughMixerAsync (:521) is reached and no-ops because
  // _playbackService is null (an optional ctor parameter this fixture does not
  // pass), so both of its arms fall through. That is load-bearing: if a future
  // change makes routing unconditional, these break loudly rather than silently
  // stopping short of the branch. Nothing is ever called on the capture mock.
  // -----------------------------------------------------------------------

  private static Mock<IBluetoothService> BuildBtMock(object? capture)
  {
    var btMock = new Mock<IBluetoothService>();
    btMock.Setup(b => b.IsAudioManagedByPlatform).Returns(false);
    btMock.Setup(b => b.StartAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(true);
    btMock.Setup(b => b.GetAudioCaptureDeviceAsync(It.IsAny<CancellationToken>()))
      .ReturnsAsync(capture);
    btMock.Setup(b => b.ConnectedDevice).Returns(new BluetoothDeviceInfo
    {
      Address = "AA:BB:CC:DD:EE:FF",
      Name = "Test Phone",
      IsPaired = true,
      IsConnected = true
    });
    return btMock;
  }

  private BluetoothAudioSource BuildSource(Mock<IBluetoothService> btMock) =>
    new(_loggerMock.Object,
        _deviceManagerMock.Object,
        btMock.Object,
        _options,
        identificationService: null,
        metricsCollector: _metricsMock.Object);

  private static object NewCaptureDeviceMock() =>
    new Mock<global::SoundFlow.Abstracts.Devices.AudioCaptureDevice>(
      MockBehavior.Loose, null!, default(global::SoundFlow.Structs.AudioFormat), null).Object;

  private static object NewSoundComponentMock() =>
    new Mock<global::SoundFlow.Abstracts.SoundComponent>(
      MockBehavior.Loose, null!, default(global::SoundFlow.Structs.AudioFormat)).Object;

  public static TheoryData<string> CaptureKinds => new() { "AudioCaptureDevice", "SoundComponent" };

  [Theory]
  [MemberData(nameof(CaptureKinds))]
  public async Task DeviceConnectedEvent_TakesTheMatchingBranch_AndLandsReady(string kind)
  {
    var capture = kind == "AudioCaptureDevice" ? NewCaptureDeviceMock() : NewSoundComponentMock();
    var btMock = BuildBtMock(capture);
    await using var source = BuildSource(btMock);

    // Not played, so ApplyDeferredCaptureState — reached at :486 or :497 from its REAL
    // call site inside the arm under test — must land the source in Ready.
    Assert.Equal(AudioSourceState.Created, source.State);

    btMock.Raise(
      b => b.DeviceConnected += null,
      new BluetoothDeviceConnectedEventArgs { Device = btMock.Object.ConnectedDevice! });

    // TryAcquireAudioCaptureAsync is fire-and-forget from :370, so synchronize on the
    // observation rather than on elapsed time (CLAUDE.md § Test Timing).
    await WaitForAsync(() => source.State == AudioSourceState.Ready);

    Assert.Equal(AudioSourceState.Ready, source.State);
    btMock.Verify(
      b => b.GetAudioCaptureDeviceAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
  }

  [Theory]
  [MemberData(nameof(CaptureKinds))]
  public async Task DeviceConnectedEvent_WhilePlaying_TakesTheBranchAndStaysPlaying(string kind)
  {
    // The #469 invariant, driven through the real dispatch instead of the seam: a source
    // already Playing must survive deferred acquisition, because SoundFlowAudioTap.IsActive
    // gates fingerprinting on Playing and a demotion silently kills song recognition.
    var capture = kind == "AudioCaptureDevice" ? NewCaptureDeviceMock() : NewSoundComponentMock();
    var btMock = BuildBtMock(capture);
    await using var source = BuildSource(btMock);

    await source.PlayAsync(CancellationToken.None);
    Assert.Equal(AudioSourceState.Playing, source.State);

    btMock.Raise(
      b => b.DeviceConnected += null,
      new BluetoothDeviceConnectedEventArgs { Device = btMock.Object.ConnectedDevice! });

    await WaitForAsync(() => btMock.Invocations.Any(
      i => i.Method.Name == nameof(IBluetoothService.GetAudioCaptureDeviceAsync)));

    Assert.Equal(AudioSourceState.Playing, source.State);
  }

  /// <summary>
  /// Polls a condition to a deadline. Used instead of a fixed Task.Delay because
  /// TryAcquireAudioCaptureAsync is fire-and-forget — there is no handle to await, so the
  /// test must synchronize on the observation. Per CLAUDE.md § Test Timing this is the safe
  /// direction: starvation can only slow it, never flip a pass to a fail, because every
  /// assertion re-checks after the wait returns.
  /// </summary>
  private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 5000)
  {
    var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
    while (!condition() && DateTime.UtcNow < deadline)
    {
      await Task.Delay(10);
    }
  }
```

**5c — extend `WasapiLoopbackTests` to the `AudioCaptureDevice` arm of `:159`.** That file already
covers the `SoundComponent` arm (`:64-100`); add the mirror using `NewCaptureDeviceMock()`, asserting
`Ready` + metadata + `NeedsFingerprintingLookup`, so `InitializeAsync`'s dispatch is covered on both arms.

⚠ **Builder must verify two mechanical details first** — both cheap, both fatal if wrong:
(i) `BluetoothDeviceConnectedEventArgs`' property shape
(`src/Radio.Core/Interfaces/Audio/IBluetoothService.cs:245`) — the sketch assumes a settable `Device`;
(ii) that the `global::` type names resolve as they do at `WasapiLoopbackTests.cs:68`.

### Task 6 — Retire the Kind-D seam

Conditional on Task 5a succeeding. If it did not, apply §1.3 instead.

**6a** — `BluetoothAudioSource.cs:454`: `internal void ApplyDeferredCaptureState()` → `private void …`.

**6b** — Delete the false `<para>` at `:447-452` and replace with:

```csharp
  /// <para>
  /// <c>private</c>. This was <c>internal</c> for tests until <c>TEST-2</c> (ADR-030) showed
  /// the constraint that justified it did not exist: the real call sites are drivable from a
  /// unit test by raising <c>IBluetoothService.DeviceConnected</c> with a mocked capture
  /// object, because <c>SoundComponent</c> and <c>AudioCaptureDevice</c> are abstract types
  /// whose constructors store the engine without dereferencing it. Only the engine is native.
  /// The dispatch that reaches this method is covered by
  /// <c>BluetoothAudioSourceTests.DeviceConnectedEvent_*</c>; do not re-widen this to reach it.
  /// </para>
```

**6c** — Rewrite the three tests at `BluetoothAudioSourceTests.cs:917`, `:930`, `:954` so they no
longer call the method directly:

- `:907 DeferredCaptureAcquisition_AfterPlay_LeavesSourcePlaying` — **superseded** by
  `DeviceConnectedEvent_WhilePlaying_TakesTheBranchAndStaysPlaying`. Delete.
- `:923 DeferredCaptureAcquisition_AfterPlay_KeepsAudioTapActive` — **keep the `SoundFlowAudioTap`
  assertion**, which is the invariant that actually matters, but reach `Playing` by raising the event
  rather than by calling the seam. This is the one test whose *assertion* is irreplaceable.
- `:946 ApplyDeferredCaptureState_WhenNotPlaying_SetsReady` — **superseded** by
  `DeviceConnectedEvent_TakesTheMatchingBranch_AndLandsReady`. Delete.

⚠ **`ApplyDeferredCaptureState` has a third call site, `:469`, on the `IsAudioManagedByPlatform` path,
and no test above covers it.** Add one: `BuildBtMock` with `IsAudioManagedByPlatform` → `true`, raise
`DeviceConnected`, assert `Ready` and that `GetAudioCaptureDeviceAsync` was **never** called. Without
it, retiring the seam trades one uncovered branch for another.

### Task 7 — `TestSeamLabelLintTests`, with a canary that proves it is looking

New file `tests/Radio.Core.Tests/TestSeamLabelLintTests.cs`, modelled on `LogSafetyLintTests.cs` — same
project, same `FindRepositoryRoot` idiom **including its worktree-exclusion logic** (copy that method;
do not write the simpler variant at `NoRawMiniAudioEngineConstructionTests.cs:23-34`, which a reviewer
has already seen silently scan a stale nested checkout).

**7.1 What it asserts.** Scan `src/**/*.cs` (excluding `bin/`/`obj/`). For each `internal` member whose
preceding XML doc block mentions a test (case-insensitive `InternalsVisibleTo`, `test seam`,
`for unit testing`, `test-only`) **or** whose name matches `(ForTest|ForTests|ForTesting)$`:

- If the doc contains `Test seam (kind C` or `Test seam (kind D`, it **must** also contain both
  `Why the real path is unreachable:` and `NOT covered by this seam:`. Missing either is a failure,
  reported with `file:line`.
- Collect every matched member into `found` regardless of kind.

**7.2 What it does NOT assert — state this in the class remarks**, mirroring `LogSafetyLintTests.cs:12-19`,
which is explicit that it is a regression lint over known shapes. It does **not** prove every seam is
labelled: a new `internal` member with no doc comment matches no trigger and passes. It catches
**degradation of the labels that exist**; a wholly new unlabelled seam is a code-review responsibility.
A lint that overstates its reach is the same defect class as an over-claiming comment — which is the
defect this very row was filed on top of.

**7.3 ⭐ The canary — how a zero-violations lint proves it is looking**

A lint whose passing state is "no violations found" is indistinguishable from one that scanned nothing.
`LogSafetyLintTests.cs:190-236` solves this with numeric floors and says why. **Do that, and then do
one thing better**, because a floor proves the scanner *ran*; it does not prove the scanner can still
*recognise* what it is looking for. Both assertions are required:

```csharp
    // Floors: prove the scan ran at all. Not exact counts — they exist to catch
    // "scanned nothing", the failure mode a zero-violation lint cannot otherwise
    // distinguish from success. The tree held 451 source files when this was written.
    Assert.True(files.Count > 200, $"Only {files.Count} source files found under '{src}'.");
    Assert.True(
      found.Count >= 8,
      $"Only {found.Count} test-seam members matched — the extractor is broken.");

    // POSITIVE CONTROL: prove the scan still recognises the seams this lint exists for.
    // A floor proves the scanner ran; only a named control proves it still finds THESE.
    // If a seam is renamed or its label deleted, this fails loudly instead of the lint
    // quietly guarding nothing — the failure mode RotaryEncoderProvisioningPromisesTests
    // guards against by asserting its forbidden symbol still exists before trusting green.
    foreach (var (member, kind) in new[]
             {
               ("ConnectRaceHookForTests", 'C'),
               ("ConnectTransportOverrideForTests", 'C'),
             })
    {
      var hit = found.SingleOrDefault(f => f.Member == member);
      Assert.True(hit is not null,
        $"Positive control '{member}' was not found. Either it was renamed or removed "
        + "(update this control and design/TESTING.md), or the scanner no longer recognises "
        + "it — in which case every green run of this lint since the change was meaningless.");
      Assert.Equal(kind, hit!.Kind);
    }
```

**The positive control is the answer to "how does it prove it is looking":** the lint does not only
assert *zero of the bad thing*, it asserts *named instances of the good thing, with their kind*. The
two fail in opposite directions, so no single breakage — wrong root, broken regex, changed doc format,
renamed member — leaves the test green. `NoRawMiniAudioEngineConstructionTests` has neither and is the
cautionary example: it asserts only `offenders.Count == 0` over a tree it never proves it enumerated.

⚠ **Two deliberate absences from the control list.** `CastStatusReadOverrideForTests` is not there
because it does not exist yet (§0.5) — `AUD-5` adds it (§5.3). `ApplyDeferredCaptureState` is not there
because Task 6 retires it; **after this row there is no live Kind-D seam**, so the `kind D` branch of
the lint is exercised only by §4.5's falsification step. Say so in the class remarks rather than
letting a future reader assume it is covered.

---

## 3. Ordering

1. **Task 5a first** — the whole shape of Tasks 5, 6 and the estimate hinges on it (§1.3).
2. **Task 1** (convention) — Tasks 2, 3, 6 and 7 all cite it.
3. **Task 2** (`ADR-030`).
4. **Task 3** (Cast retrofit + archive anchors). Independent of 5/6; parallelisable.
5. **Task 5** (dispatch tests). Must be green **before** Task 6 — retiring the seam before its
   replacement coverage exists would leave a window with neither.
6. **Task 6** (retire the seam).
7. **Task 7** (lint) **last** — its control asserts the exact kind letters Task 3 writes.

---

## 4. Test plan

### 4.1 `T1` — the new dispatch tests are proven non-vacuous ⚠ **gates auto-merge (§0.11)**
Run the four Task 5b tests plus Task 5c. Then **falsify them**: temporarily swap the arms at `:483`/
`:494` (make the first `is SoundComponent` and the second `is AudioCaptureDevice`) and confirm the
`AudioCaptureDevice` theory cases fail. Revert. **A dispatch test that passes against a wrong dispatch
is precisely what this row exists to prevent**, and this is the only step that shows it does not.

### 4.2 `T2` — Task 5a's outcome is recorded either way
Put the verdict in the PR body: either "`Mock<AudioCaptureDevice>` constructs — §0.4 confirmed" or the
verbatim exception plus the §1.3 fallback taken. This is the row's central factual question; its answer
must outlive the PR.

### 4.3 `T3` — the retired seam's invariant survives
After Task 6, the `SoundFlowAudioTap.IsActive` assertion (formerly `:924-944`) must still exist and
still pass, and the `IsAudioManagedByPlatform` path (`:469`) must have its own test. Confirm
`ApplyDeferredCaptureState` appears **nowhere** in `tests/` afterwards.

### 4.4 `T4` — no coverage was traded away
Diff the covered branches of `BluetoothAudioSource` before and after. `:159`, `:166`, `:483`, `:494` and
`:469` should all be newly covered; `:684`/`:689` remain uncovered by design (§7.2).

### 4.5 `T5` — the lint's canary is itself falsifiable ⚠ **the one task with real unknowns**
Three deliberate breakages, each must turn the lint **red**, each reverted:
1. Delete the `NOT covered by this seam:` clause from `ConnectRaceHookForTests` → red.
2. Point `FindRepositoryRoot` at a directory with no `.cs` files → red on the file floor.
3. Rename a control-list entry to a nonexistent member → red on the control.

Additionally, because no live Kind-D seam remains: temporarily add a `kind D` label with a missing
clause and confirm red, proving that branch of the matcher works.

**If the extractor cannot be made to do (1) reliably within Task 7's timebox** — XML-doc block
association is fiddlier than `LogSafetyLintTests`' single-line call matching — **stop and ship Tasks
1–6 without the lint**, recording why in the PR body. §0.11. Do not ship a lint that passes (2) and (3)
but not (1); that is a lint proving it ran without proving it checked.

### 4.6 Gates
- `dotnet build --configuration Release` — **must equal the 47-warning / 0-error baseline**
  (`CLAUDE.md`; the 53 figure in older notes is stale). Build `main` too if the number looks off.
- `dotnet test` **redirected to a file, never piped to `tail`** — `CLAUDE.md` documents a piped run
  exiting `0` with five failures:
  ```bash
  dotnet test RadioConsole.sln -c Release > /tmp/test.log 2>&1; echo "exit=$?"
  grep -E "Passed!|Failed!|error" /tmp/test.log
  ```
  Read the per-project summary lines. Known-failing on Windows and not a regression: four
  `SrcVariableResamplerTests`, `NwsObservationIntegrationTests.RealNwsCall_*`, and
  `CoverArtPipelineIntegrationTests.CoverArtArchive_ReturnsValidUrl_ForKnownRecording`.
- No UAT. Nothing user-facing; §0.11 condition 3.

---

## 5. Docs and queue

**5.1** `design/TESTING.md` — new `## Test Seams` section (Task 1).
**5.2** `design/DECISION-LOG.md` — `ADR-030` (Task 2).
**5.3** ⚠ **A note for `AUD-5`'s Builder — the only cross-row obligation this plan creates.** When
`AUD-5` ships `CastStatusReadOverrideForTests`, it must (a) carry the kind-C label and (b) join
`TestSeamLabelLintTests`' control list. Put this in the PR body so it is visible in `git log`;
**do not edit `docs/queue/AUD-5.md` or `design/plans/AUD-5-*.md`** — a Planner may be in them.
**5.4** `docs/BUILDER_QUEUE_ARCHIVE.md:349` — anchors + residue-(c) discharge (Task 3c).
**5.5** `CLAUDE.md` § *Pre-Merge Review* — add `BluetoothAudioSource.cs:447-452` as a fourth worked
example (§0.7). **One sentence**, matching the existing three: a comment asserting a constraint that
never held, whose victim was a queue row rather than a code change.
**5.6** ⛔ **This plan does not edit `docs/BUILDER_QUEUE.md` or `docs/queue/TEST-2.md`.** Other agents
are in queue files concurrently. §9 holds the text; applying it is a later, separate step.

---

## 6. Deliberately not done

**6.1 `TryReacquireCaptureAsync` (`:684`/`:689`) gets no test.** It is reachable via
`OnCaptureStreamRecovered` (`:373`), but neither arm calls `ApplyDeferredCaptureState` and with
`_playbackService` null neither has an observable consequence to assert. **A test that executes a
branch and asserts nothing is coverage theatre** — the same trade §0.9 warns about from the other
direction. Recorded here and in the queue row instead.

**6.2 No native harness, no `snd-dummy`, no `apt`-installed audio stack in CI.** §1.1, and after §0.4
it would buy nothing: the tests do not need an engine.

**6.3 The ~13 Kind-A/B seams are not relabelled.** The convention says explicitly they need only a
line. Rewriting a dozen harmless comments would bury the two that matter.

**6.4 `AUD-3` residues (a) and (b) are untouched.** (a) needs a real Chromecast; (b) is a distinct
wiring gap at `AudioEngineInitializationService.cs:522`/`:569`. **The PR body must say so** — the
archive lists three residues and this row discharges exactly one.

**6.5 No follow-up row is filed for the `SoundFlowAudioEngine`/`MiniAudioEngine` split.** §1.1(c)'s lint
already enforces it and nothing here weakens that.

---

## 7. Self-review

**7.1 Placeholder scan.** No `TBD`, no "similar to Task N", no "implement later". Every markdown and
C# block is literal proposed content. The two conditional branches (§1.3, §4.5) each specify both
outcomes rather than deferring a decision.

**7.2 Claims I could not fully verify, stated rather than smoothed over.**
- **`Mock<AudioCaptureDevice>` was never executed.** §0.4 is inference from decompiled IL — abstract
  type, protected ctor that only assigns, `DeviceConfig` an empty reference type, abstract
  `Start`/`Stop`/`Dispose` for the proxy to override. Confidence high, proof absent. Task 5a proves it
  first and §1.3 pre-decides the fallback; **no other task is allowed to depend on it unproven.**
- **I did not run the test suite.** Every Task 5/6 snippet is unexecuted; §4.1's falsification exists
  because of that.
- **The 451-file / 8-member floors** are carried from `LogSafetyLintTests`' comment and my own grep
  (9 suffixed members + `ApplyDeferredCaptureState`, which Task 6 removes). Builder should re-count and
  set floors comfortably below the true values.
- **`BluetoothDeviceConnectedEventArgs`' property shape was not read** — flagged inline in Task 5.

**7.3 Scope check.** The row asked for a feasibility answer and, failing that, a convention. It gets
both, plus four things it did not ask for and would want: a false premise refuted (§0.2–§0.4), a false
seam count corrected (§0.5), an over-claiming comment fixed (§0.7), and the gap itself actually closed.
Tasks 5–7 are the scope additions; all three serve the row's stated goal and §6 bounds them. **The
estimate rose from 0.75 d to 1.0 d because of them and that is stated in §0.10, not buried.**

**7.4 Does this violate the row's prohibition?** No — and this is the check that matters most.
`docs/queue/TEST-2.md:32`/`:44` forbid closing with *"a mock that re-asserts the seam from a different
angle."* Task 5 removes the seam from the path under test and drives the dispatch it bypasses; Task 6
then deletes the seam entirely. That is the **stronger** form of what the row wanted — it says the only
real close is *"a harness that can produce a native `AudioEngine` (or a genuine integration test on the
box)"*, and this is a third route the row did not consider because its premise foreclosed it. §4.1's
falsification step is what distinguishes this from the forbidden outcome in substance rather than in
letter.

---

## 8. ⚠ For the owner — what I found wrong, in one place

Four things in the row or its framing did not survive checking. Listed because §7.2 of the framing
asked for them explicitly and because three of the four would have wasted a Builder cycle:

1. **The row's core premise is false** (§0.2–§0.4): neither `SoundComponent` nor `AudioCaptureDevice`
   needs a native engine. The row waited thirteen months for a harness it never needed.
2. **The "four seams" count is three** (§0.5): the fourth is in `AUD-5`'s *plan*, not in `src/`.
3. **The `libsamplerate` evidence points the other way** (§1.1): CI installs it deliberately
   (`build.yml:37-45`); those four tests fail on Windows only. Cited in `CLAUDE.md` and in the task
   framing as evidence *against* native availability in CI, it is weak evidence *for* it.
4. **The doc comment the row cites as its authority is itself an over-claiming comment** (§0.7) — the
   exact defect class `CLAUDE.md` § *Pre-Merge Review* exists to catch, with a queue row as the victim.

**What held:** every `BluetoothAudioSource.cs` and `BluetoothAudioSourceTests.cs` anchor (§0.6); the
`AUD-3` residue (c) claim (§0.8); and the row's own judgement that the convention is worth writing and
that "still infeasible" is an acceptable answer — it was right about the shape of the close even while
wrong about the reason.

---

## 9. Queue row wording

⛔ **Not applied by this plan** (§5.6). For whoever updates `docs/BUILDER_QUEUE.md` § Queue:

| TEST-2 | **Feasibility answered: NO to a native engine — but the row's premise was false and none was ever needed.** Closes by covering all three dispatch sites through the real path, retiring the Kind-D seam, and writing the seam convention (`ADR-030`). — [detail](queue/TEST-2.md) | 📋 | [`design/plans/TEST-2-the-seam-convention-and-the-half-reachable-gap.md`](../design/plans/TEST-2-the-seam-convention-and-the-half-reachable-gap.md) — 1.0 d | _no spec doc_ · #469, #468 (`8b1ce0a`) | — _(none. **Creates one obligation on `AUD-5`:** its seam must carry the kind-C label and join the lint's control list — plan §5.3.)_ | `test/bt-capture-branch-dispatch-coverage` |

---

## Planned — 2026-09-08

`TEST-2` asked whether a native SoundFlow `AudioEngine` could be built in a unit test. **It cannot** —
`MiniAudioEngine`'s constructor enters native code, CI is a headless container with no audio device,
and the repo already forbids raw construction in `src/`. **But the engine was never the requirement.**
`SoundComponent` and `AudioCaptureDevice` are abstract types whose constructors store the engine
without dereferencing it, so Moq subclasses either with a null engine — which the suite has been doing
for `SoundComponent`, in CI, since before the row was filed. One of the three "uncovered" dispatch
sites was already covered; a second is reachable by raising a public interface event; and with
`_playbackService` null the mixer routing that looked like a blocker is a no-op.

So the row closes by **building** the coverage rather than recording its absence: all three `capture is
…` sites covered through the real path, the Kind-D seam at `BluetoothAudioSource.cs:454` retired
outright, and the convention written as `design/TESTING.md` § *Test Seams* + `ADR-030` for the two Cast
seams that remain genuinely justified. `AUD-3` residue (c) is discharged; (a) and (b) are not. The doc
comment the row cited as its authority was itself asserting something untrue — `CLAUDE.md` §
*Pre-Merge Review*'s named failure mode, with a queue row as the victim.

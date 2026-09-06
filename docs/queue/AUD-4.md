# AUD-4 — Unify the two source-removal layers, and rename `SoundFlowMasterMixer` — it is not a mixer.

> Queue dossier for row **`AUD-4`** of [`BUILDER_QUEUE.md`](../BUILDER_QUEUE.md).
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
| Plan | [`design/plans/AUD-4-unify-source-removal-and-rename-the-mixer.md`](../../design/plans/AUD-4-unify-source-removal-and-rename-the-mixer.md) · **2 d** minimal / **3 d** split · **not auto-mergeable — Task 4 changes engine-stop behaviour on the live audio path** |
| Spec / handoff | _no spec doc — the diagnosis is in this row_ · commit `03a6fea` is the provenance for the "one layer too high" trap · **PR #468 (`8b1ce0a`) is the provenance for the third layer** |
| Depends on | — _(**no dependency. ⚠ The "prefer `AUD-2` first" note that stood here was FALSIFIED 2026-09-06 and is removed, not softened.** [`ORDERING-NOTES.md`](ORDERING-NOTES.md)'s claim that `AUD-2` and `AUD-4` are "two symptoms of ONE root cause" and that `AUD-2` "decides the key" is **false**: per-source teardown is key-symmetric — `SDRRadioAudioSource.cs:915` mints `_playbackId` and `:1027`/`:1059` stop with the same field — and this row's roster is keyed by **object reference**, not by string, so there is no key here to decide. The row's prescribed "sweep `_activeComponents` instead" is unnecessary and also points at dead code: `SoundFlowPlaybackService.StopAll()` has **zero callers in the tree**. Plan: `design/plans/AUD-4-unify-source-removal-and-rename-the-mixer.md` §0.4, `C-148`, `C-150`. **Still rebase past #468.**)_ |
| Branch | `refactor/unify-source-removal-and-rename-mixer` |

## Detail

**Unify the two source-removal layers, and rename `SoundFlowMasterMixer` — it is not a mixer.** Two cleanups on one mechanism, deliberately in one cycle because the rename is what stops the bug from recurring.

**The misnomer, and the damage it already did.** `src/Radio.Infrastructure/Audio/SoundFlow/SoundFlowMasterMixer.cs:13` is `private readonly List<IAudioSource> _sources = []` — a **registry of source objects**, not the audio mixer. The real audio mixer is SoundFlow's `playbackDevice.MasterMixer`, and the real detach is `SoundFlowPlaybackService.StopAsync` (`src/Radio.Infrastructure/Audio/SoundFlow/SoundFlowPlaybackService.cs:494`) → `MasterMixer.RemoveComponent` (`:526` / `:548`). But `SoundFlowMasterMixer.RemoveSource` (`:109-121`) logs **`"Removed audio source {SourceId} ({SourceName}) from mixer"`** (`:118`), which reads as an audio detach and is not one.

**Commit `03a6fea`** ("fix: Source exclusivity, PipeWire BT auto-link cleanup, and default device selection", 2026-02-26) was applied **one layer too high** — at `AudioManager.SwitchSourceAsync`'s `mixer.RemoveSource(oldSource)` (`src/Radio.Infrastructure/Audio/Services/AudioManager.cs:214-217`, which logs the equally misleading `"Removed old source {SourceName} from mixer"`) — **and silently did nothing for months**, because the audio stayed attached one layer down.

**The rename is therefore the durable half of this row, not the cosmetic half.**

**⚠ ADDED 2026-08-11 — PR #468 changed the ground under this row, and it is the one thing in this tranche nobody anticipated. Read this before writing a plan.** #468 rewrote `AudioSourceBase.StopAsync` (`src/Radio.Infrastructure/Audio/Sources/AudioSourceBase.cs:100-124`): teardown is no longer gated on `State` being `Playing`/`Paused`, only `Created`/`Disposed` are skipped. The reason it gives is **exactly this row's failure class, at a layer this row does not name** — *"a source can hold an attached, audible sound component while its state reads Ready/Stopped/Error … gating teardown on the state flag meant those sources kept producing audio after a switch-away, because `StopCoreAsync` — the only code that detaches the component — was skipped."*

**Two consequences.** **(1) There are THREE layers here, not two.** This row names `SoundFlowMasterMixer.RemoveSource` (registry) and `SoundFlowPlaybackService.StopAsync` → `MasterMixer.RemoveComponent` (audio); the third is `AudioSourceBase.StopAsync` → `StopCoreAsync`, whose **entry condition just changed**. Any unification written against the two-layer picture will be unifying two of three while the third has moved underneath it — which is, precisely, how `03a6fea` went wrong.

**(2) #468's comment and this row make competing "the only detach" claims and they cannot both be the whole truth.** #468 says `StopCoreAsync` is *the only code that detaches the component*; this row says the real detach is `SoundFlowPlaybackService.StopAsync` → `RemoveComponent` (`:526`/`:548`). Most likely both are locally right about different sources (per-source `StopCoreAsync` implementations vs. the shared playback service), which would mean the detach is **doubly** non-unified — but that is an inference, and **resolving it is the first task of this row, before any code moves.** Do not silently pick one.

**Related, and cheap to confirm at the same time:** #468's state-independent teardown may already have closed part of what this row's "no caller left able to do half the job" was aiming at. Say explicitly in the plan what remains rather than assuming the whole scope survived.

**Scope:** (a) unify the layers so one call detaches both the registry entry and the audio component, with no caller left able to do half the job; (b) rename `SoundFlowMasterMixer` (and consider `IMasterMixer`, `:10`) to something that says *registry* — the exact name is the implementer's call, but **it must not contain "Mixer"**; (c) fix both log strings to state what actually happened.

**⚠ TRAP FOR THE IMPLEMENTER — stated explicitly because it is the exact mistake this row exists to stop repeating: do NOT sweep by `oldSource.Id`.** SDR registers its component under `sdr-radio-<guid>` (`SDRRadioAudioSource.cs:908`) while `AudioManager` knows that same source as `Radio-<guid>` (`AudioSourceBase.cs:28`) — an `Id`-keyed sweep would miss the SDR source and, once again, log success.

**Sweep `SoundFlowPlaybackService._activeComponents` (`:25`) instead**, which is keyed by whatever each source actually registered.

⚠ **BOTH PARAGRAPHS ABOVE WERE FALSIFIED 2026-09-06 while planning this row, and are corrected rather than deleted because the reasoning is the asset.** This row's roster is a `List<IAudioSource>` mutated by **object reference** — there is no string key here for an `Id`-keyed sweep to get wrong, so the trap does not apply to `AUD-4` and the prescribed `_activeComponents` sweep is unnecessary. It also points at dead code: `SoundFlowPlaybackService.StopAll()` has **zero callers in the tree** (plan `C-150`). The live defect is elsewhere — see the revised scope below.

⚠ **"Coordinate with `AUD-2` — it is the same key-identity problem seen from the ducking side" is FALSE and is withdrawn.** `AUD-2` is a key-identity defect in a *third party* (`AudioManager` addressing a source by `Id` when the source registered under a key it minted); per-source teardown is key-symmetric, so this row has no key problem to wait on. **Either row may be claimed first** (plan §0.4, `C-148`).

**Scope, revised by the plan:** (a) the unification now has a **named live defect** under it —
`SoundFlowAudioEngine.StopAsync:700` clears the roster and never stops the audio, while the sweep
written for exactly that (`StopAll`) has no callers (`C-151`, `C-150`); (b) the rename is
**recommended as a split into `AudioSourceRegistry` + `MasterOutputState`**, because the two halves
of the type share no state and no single honest name covers both (`C-154`) — and the class has no
SoundFlow dependency at all, so the prefix goes with the suffix (`C-153`); (c) log/comment honesty
as filed. **⚠ The row's claim that "the rename is the durable half — it is what stops the bug
recurring" did not survive planning (`C-147`): the rename would not have prevented `03a6fea`, and
the actual recurrence-preventer shipped in #468. The rename is justified as log honesty and
detection latency. Re-price the row if that was what bought its priority.**
_plan: `design/plans/AUD-4-unify-source-removal-and-rename-the-mixer.md` · **2 d** minimal / **3 d**
split · **not auto-mergeable — Task 4 changes engine-stop behaviour on the live audio path**_

**Budget note:** renaming the interface touches DI wiring and any test doubles, so this is a larger diff than the behaviour change suggests. _**Anchors re-verified 2026-08-11 against `main` @ `8b1ce0a`** and all are byte-exact and unchanged: `SoundFlowMasterMixer.cs:10`/`:13`/`:109-121`/`:118`, `SoundFlowPlaybackService.cs:25`/`:494`/`:526`/`:548`, `AudioManager.cs:214-217`, `SDRRadioAudioSource.cs:908`, and `AudioSourceBase.cs:28` — that last one checked specifically because #468 **did** touch the file, but its hunk starts at `:97`, well below the `Id` derivation._

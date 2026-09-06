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
| Plan | _plan TBD (medium; the rename dominates the diff, the layer-unification dominates the risk — **and the first task is now the three-layer reconciliation above, not code**)_ |
| Spec / handoff | _no spec doc — the diagnosis is in this row_ · commit `03a6fea` is the provenance for the "one layer too high" trap · **PR #468 (`8b1ce0a`) is the provenance for the third layer** |
| Depends on | — _(no hard row dependency. **Prefer AUD-2 first** — it decides the key identity this row's sweep must use, and doing them in the other order risks building the sweep around a mismatch that AUD-2 then removes. **Also rebase past #468**, which changed `AudioSourceBase.StopAsync`'s teardown gate — see the ⚠ above.)_ |
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

**Coordinate with AUD-2 — it is the same key-identity problem seen from the ducking side.** If AUD-2 confirms and lands first, adopt its single-key answer here rather than coding around the mismatch a second time.

**Budget note:** renaming the interface touches DI wiring and any test doubles, so this is a larger diff than the behaviour change suggests. _**Anchors re-verified 2026-08-11 against `main` @ `8b1ce0a`** and all are byte-exact and unchanged: `SoundFlowMasterMixer.cs:10`/`:13`/`:109-121`/`:118`, `SoundFlowPlaybackService.cs:25`/`:494`/`:526`/`:548`, `AudioManager.cs:214-217`, `SDRRadioAudioSource.cs:908`, and `AudioSourceBase.cs:28` — that last one checked specifically because #468 **did** touch the file, but its hunk starts at `:97`, well below the `Id` derivation._

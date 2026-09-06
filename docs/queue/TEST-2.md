# TEST-2 — Close the deferred-capture branch-dispatch coverage gap left by PR #469 — if a native test harness ever becomes feasible.

> Queue dossier for row **`TEST-2`** of [`BUILDER_QUEUE.md`](../BUILDER_QUEUE.md).
> The detail below was moved verbatim out of that row's Item cell on 2026-09-06; only
> whitespace, the table's `\|` escapes and docs-relative link prefixes changed.

| Field | Value |
|---|---|
| Status | 📋 |
| Plan | _plan TBD — **feasibility check first**: if a native SoundFlow test harness is not practical, close the row and say so — **and if so, prefer closing it with the seam convention described above rather than with nothing**_ |
| Spec / handoff | _no spec doc — the diagnosis is in this row_ · PR #469 (merged) is where the seam and its tests landed · **PR #468 (`8b1ce0a`) is where the second and third seams landed** |
| Depends on | — _(no dependency. **Touches the same file as AUD-1** (`BluetoothAudioSource.cs`); if both are in flight, expect line anchors to move.)_ |
| Branch | `test/bt-capture-branch-dispatch-coverage` |

## Detail

**Close the deferred-capture branch-dispatch coverage gap left by PR #469 — if a native test harness ever becomes feasible.**

**This row records a known limit so it is not mistaken for coverage. It may legitimately close as "still infeasible," and that is an acceptable outcome.** PR #469's deferred-capture test reaches the acquisition through the **internal `ApplyDeferredCaptureState()` seam** (`src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs:454`, driven from `tests/Radio.Infrastructure.Tests/Audio/BluetoothAudioSourceTests.cs:917`, `:930`, `:954`) rather than by driving the real `capture is AudioCaptureDevice` / `capture is SoundComponent` branches end-to-end.

**Why, and it is already recorded in the code:** constructing either type needs a **native SoundFlow `AudioEngine`**, which the method's own doc comment states at `BluetoothAudioSource.cs:449-452` — that constraint is exactly why the seam is `internal` + `InternalsVisibleTo` in the first place.

**What IS pinned by #469:** the state decision (a source already `Playing` stays `Playing`; only a not-yet-playing source lands in `Ready`) and its tap consequence — demoting to `Ready` would silently kill fingerprinting while audio keeps flowing.

**What is NOT pinned:** the branch dispatch around it — `TryAcquireAudioCaptureAsync` (`:462`) at `:483` / `:494`, plus the two sibling dispatch sites at `:159` / `:166` and `:684` / `:689`. Three places make the same `capture is …` decision and none is covered end-to-end.

**⚠ Do not "close" this with a mock that asserts the seam again from a different angle** — that adds a test without adding coverage, and would make the gap harder to see than leaving it open. The only real close is a harness that can produce a native `AudioEngine` (or a genuine integration test on the box).

**Lowest priority in the 2026-08-10 tranche** — nothing is broken; the risk is that a future refactor of the dispatch goes unnoticed.

**⚠ ADDED 2026-08-11 — this stopped being a one-off, which raises the row's value without widening its scope.** PR #468 (`8b1ce0a`) closed *its* test gap the same way, and needed **two** `internal` seams to do it: `GoogleCastOutput.ConnectRaceHookForTests` (`:54`) to interleave a teardown at the exact point between receiver resolution and the network connect, and then `ConnectTransportOverrideForTests` (`:64`) because the first could not reach the succeeded-then-lost branch offline — a fake socket cannot complete a Cast handshake, so every connect diverted into the error handler instead.

**So the codebase now has three `internal`+`InternalsVisibleTo` seams that exist solely because a native/hardware dependency makes the real path untestable** (`ApplyDeferredCaptureState` here, plus #468's two).

**What this changes about this row: the feasibility check should ask the general question, not just the Bluetooth one.** If the honest answer is *"native `AudioEngine` construction in tests is not practical,"* then the durable output of this row is **a stated, written convention for when a seam is acceptable and how it must be labelled** — which would also retire `AUD-3`'s residue (c) — rather than a Bluetooth-specific test. That is a legitimate close for this row and a better one than another mock.

**Still forbidden, unchanged:** do not close this with a mock that re-asserts the seam from a different angle. _Anchors re-verified 2026-08-11 against `main` @ `8b1ce0a` — all byte-exact and unchanged (#468 did not touch `BluetoothAudioSource.cs`): `:159`/`:166`, `:449-452`, `:454`, `:462`, `:483`/`:494`, `:684`/`:689`, and `BluetoothAudioSourceTests.cs:917`/`:930`/`:954`._

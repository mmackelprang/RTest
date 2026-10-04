# `TEST-11` — `PipeWireNativeStreamPropertiesTests`' skip guard probes a different library name than the P/Invoke it protects, so the test FAILS instead of SKIPS wherever PipeWire runs without its dev package — and CI on `main` is red

[← Builder Queue index](../BUILDER_QUEUE.md)

🟠 **P1.** Filed 2026-09-28. Found twice the same day: first on a fresh Linux dev box
(`appserver`, Ubuntu 24.04) where it was the **only** failure in the suite, then in CI on
[PR #677](https://github.com/mmackelprang/RTest/pull/677), where it was again the only failure. The
P1 is for the second half of the title: **CI has failed at the Test step on every `main` run since
2026-09-26**, which means the merge gate's "second opinion" (CLAUDE.md § *The merge gate*) has
been saying nothing for two days.

## The mismatch, in two lines

`tests/Radio.Infrastructure.Tests/Platform/Bluetooth/Native/PipeWireNativeStreamPropertiesTests.cs:72`:

```csharp
Skip.IfNot(OperatingSystem.IsLinux()
  && NativeLibrary.TryLoad("libpipewire-0.3.so.0", out _), "…");
```

`src/Radio.Infrastructure/Platform/Bluetooth/Native/PipeWireNative.cs:13`:

```csharp
private const string PipeWireLib = "pipewire-0.3";
```

The guard asks whether the **versioned runtime soname** `libpipewire-0.3.so.0` loads. The
`[DllImport("pipewire-0.3")]` calls it protects ask the .NET runtime to probe `pipewire-0.3` —
which expands to `libpipewire-0.3.so`, `pipewire-0.3.so`, `libpipewire-0.3`, `pipewire-0.3`,
**never `.so.0`**. The unversioned `libpipewire-0.3.so` is a symlink that only the **`-dev`**
package ships. So on any box with `libpipewire-0.3-0` and not `libpipewire-0.3-dev`:

- the guard finds `.so.0` → *"PipeWire is here, run the test"*;
- the constructor's `pw_init` P/Invoke probes for `.so` → `DllNotFoundException`;
- the test **fails**, with the exact error the guard exists to convert into a skip.

Measured on `appserver` before and after: `dpkg -l libpipewire-0.3-0t64` present, test fails with
the `DllNotFoundException` listing every probed path; `apt-get install libpipewire-0.3-dev` (which
creates `/usr/lib/x86_64-linux-gnu/libpipewire-0.3.so → libpipewire-0.3.so.0`), same test passes
7/7 in its class. The CI runner container (`myoung34/github-runner:ubuntu-noble`, `build.yml:41`)
is in the first state.

⭐ **The comment on the guard says the opposite of what it does** — *"Skipped wherever it cannot
be loaded — including a Linux CI runner without PipeWire installed"* — which is the CLAUDE.md
§ *Pre-Merge Review* defect class: a comment asserting a guarantee the code does not provide. It is
true only when "PipeWire installed" means "the dev package installed", and nothing says so.

## Why `radio` never showed it

The appliance is provisioned by `deploy/provision/packages.sh`, whose `PIPEWIRE_PKGS` includes
`libpipewire-0.3-dev` because `build-native.sh` needs the headers to build `libpw_helper.so`. So
production always has the unversioned symlink, and **the P/Invoke has never been exercised on a box
without it** — the test bug and the runtime dependency are the same fact seen from two sides.

## CI history — what is and is not established

- `gh run list --workflow build.yml --branch main`: **five consecutive failures, 2026-09-26 →
  2026-09-28** (`e46fa68`, `76557c0`, `4f3b033`, `0aa7c6f`, `d1674e4`), each at the **Test** step.
- The 2026-09-28 run on PR #677 (`f409bb9`) fails on **this one test and nothing else**.
- ✅ **CONFIRMED 2026-09-28 — one cause.** `gh run view --log-failed` returned nothing for the older
  runs, but the raw job-log endpoint (`gh api repos/…/actions/jobs/<id>/logs`) does: run
  `36247191913` (`e46fa68`, 2026-09-26) and run `36333531919` (`d1674e4`, 2026-09-27) each list
  exactly one failure, `PipeWireNativeStreamPropertiesTests.Constructor_NonZeroTargetSerial_DoesNotThrow`.
  There is no second row hiding behind this one.

## Recommended shape

Two changes, both small; do the first regardless of how the second is decided.

1. **Make the guard probe exactly what the P/Invoke probes** (test-only, no production change):

   ```csharp
   NativeLibrary.TryLoad("pipewire-0.3", typeof(PipeWireNative).Assembly, null, out _)
   ```

   The `(string, Assembly, DllImportSearchPath?)` overload applies the same prefix/suffix probing
   as `[DllImport]`; the `(string)` overload used today is a bare `dlopen` and does not. Fix the
   comment to say what is actually required (`libpipewire-0.3-dev`, or any box where the unversioned
   `.so` resolves). This is the same shape as `TEST-2`'s lesson — a test comment cited as
   authority for a claim the code refutes.

2. **Decide whether production should tolerate the missing symlink.** A
   `NativeLibrary.SetDllImportResolver` on `PipeWireNative` mapping `pipewire-0.3` →
   `libpipewire-0.3.so.0` would let `Radio.API` load PipeWire on any box with the runtime package
   only, and would make the test pass everywhere without the guard. That is a **runtime behaviour
   change on the BT capture path** — not auto-mergeable, and arguably unnecessary while
   `packages.sh` guarantees the dev package on every provisioned box. ⚠ Do not do this one silently
   inside a "fix the flaky test" PR.

⚠ **Do not fix CI by adding `libpipewire-0.3-dev` to `build.yml`'s apt step and stopping there.**
That hides the guard bug behind the same package that hid it on `radio`; the next fresh dev box
finds it again. Adding the package to CI *after* fixing the guard is fine and lets the test run
rather than skip there.

## Depends on

Nothing. Test-only for part 1. Part 2 is an owner decision.

## ✅ Part 1 merged — [#681](https://github.com/mmackelprang/RTest/pull/681), squash `89b5b12`

Merged to `main` as `89b5b12a`: the guard now probes what the P/Invoke probes. **Part 2 is still an owner
decision.** The row stays live as 📋, with "owner decision first" in its Item cell (2026-10-04).

# OPS-2 — Pin the six floating package versions.

> Queue dossier for row **`OPS-2`** of [`BUILDER_QUEUE.md`](../BUILDER_QUEUE.md).
> The detail below was moved verbatim out of that row's Item cell on 2026-09-06; only
> whitespace, the table's `\|` escapes and docs-relative link prefixes changed.

| Field | Value |
|---|---|
| Status | 📋 |
| Plan | _plan TBD (small; scope is fully enumerated in this row)_ |
| Spec / handoff | _no spec doc — the diagnosis is in this row_ |
| Depends on | — _(no dependency; claimable now. Best sequenced **after TEST-1**, because its only real gate is a green full-suite run and TEST-1 is what makes that signal trustworthy.)_ |
| Branch | `chore/pin-floating-package-versions` |

## Detail

**Pin the six floating package versions.** Pure build-reproducibility hygiene. **Pin to the versions that currently resolve — do NOT upgrade anything as part of this row.** Six distinct packages float across **seven** `PackageReference` lines in three project files (`Serilog.AspNetCore` floats in two projects, which is why the line count and the package count differ): `src/Radio.Web/Radio.Web.csproj:23` — `Radzen.Blazor 6.*` → **6.6.4**; `src/Radio.Web/Radio.Web.csproj:24` **and** `src/Radio.API/Radio.API.csproj:35` — `Serilog.AspNetCore 8.*` → **8.0.3**; `src/Radio.Infrastructure/Radio.Infrastructure.csproj:34` — `Serilog 4.*` → **4.4.0**; `:35` — `Serilog.Extensions.Logging 8.*` → **8.0.0**; `:32` — `SoundFlow 1.*` → **1.4.1**; `src/Radio.API/Radio.API.csproj:34` — `Scalar.AspNetCore 2.*` → **resolved version was not recorded on 2026-08-10; read it off the restore graph (`dotnet list package`) rather than picking a number.**

**⚠ Note for whoever picks this up, so it is not re-framed as a bug fix: a floating-version theory WAS investigated on 2026-08-10 as the cause of a CI failure and was DISPROVED.** This row is reproducibility hygiene and nothing more; if it is ever cited as the root cause of that failure, the citation is wrong.

**Housekeeping trap:** `grep` for `Version="N.*"` across the repo also hits `.claude/worktrees/**` — those are **stale, untracked agent worktrees** carrying copies of the same csproj files and are **not** part of the build. Change only the three files under `src/`. **Verification for this row is a clean restore + full `dotnet test`**, not a visual diff — the point is proving the pinned graph is the graph that was already building.

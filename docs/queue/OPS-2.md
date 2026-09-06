# OPS-2 — Pin the six floating package versions.

> Queue dossier for row **`OPS-2`** of [`BUILDER_QUEUE.md`](../BUILDER_QUEUE.md).
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
| Plan | [design/plans/OPS-2-pin-the-six-floating-package-versions.md](../../design/plans/OPS-2-pin-the-six-floating-package-versions.md) |
| Spec / handoff | _no spec doc — the diagnosis is in this row_ |
| Depends on | — _(no dependency; claimable now. Best sequenced **after TEST-1**, because its only real gate is a green full-suite run and TEST-1 is what makes that signal trustworthy.)_ |
| Branch | `chore/pin-floating-package-versions` |

## Detail

**Pin the six floating package versions.** Pure build-reproducibility hygiene. **Pin to the versions that currently resolve — do NOT upgrade anything as part of this row.** Six distinct packages float across **seven** `PackageReference` lines in three project files (`Serilog.AspNetCore` floats in two projects, which is why the line count and the package count differ): `src/Radio.Web/Radio.Web.csproj:23` — `Radzen.Blazor 6.*` → **6.6.4**; `src/Radio.Web/Radio.Web.csproj:24` **and** `src/Radio.API/Radio.API.csproj:35` — `Serilog.AspNetCore 8.*` → **8.0.3**; `src/Radio.Infrastructure/Radio.Infrastructure.csproj:34` — `Serilog 4.*` → **4.4.0**; `:35` — `Serilog.Extensions.Logging 8.*` → **8.0.0**; `:32` — `SoundFlow 1.*` → **1.4.1**; `src/Radio.API/Radio.API.csproj:34` — `Scalar.AspNetCore 2.*` → **2.17.2** _(resolved 2026-09-06 from `dotnet list package` and `obj/project.assets.json`, which agree; it was unrecorded when this row was filed)_

**⚠ Note for whoever picks this up, so it is not re-framed as a bug fix: a floating-version theory WAS investigated on 2026-08-10 as the cause of a CI failure and was DISPROVED.** This row is reproducibility hygiene and nothing more; if it is ever cited as the root cause of that failure, the citation is wrong.

**Housekeeping trap:** `grep` for `Version="N.*"` across the repo also hits `.claude/worktrees/**` — those are **stale, untracked agent worktrees** carrying copies of the same csproj files and are **not** part of the build. Change only the three files under `src/`. **Verification for this row is a clean restore + full `dotnet test`**, not a visual diff — the point is proving the pinned graph is the graph that was already building.

**Planned 2026-09-06.** The row's count verified exact against `main` — six packages, seven lines, three files, line numbers unchanged. All six resolved: `Radzen.Blazor` **6.6.4**, `Serilog.AspNetCore` **8.0.3** (both projects), `Scalar.AspNetCore` **2.17.2**, `SoundFlow` **1.4.1**, `Serilog` **4.4.0**, `Serilog.Extensions.Logging` **8.0.0**. **`dotnet list package --outdated --highest-minor` is empty for all three projects**, so every float already sits at the top of its major and the pin is provably behaviour-neutral with no drift window (plan `C-132`). **No central package management** (no `Directory.Packages.props`) and **no lock files** (plan `C-130`, `C-131`), so the fix is seven attribute edits. **None of the six is in a packed project** — the five packed packages neither contain nor reference the three edited files, so no `.nuspec` dependency range moves (plan `C-134`). **No float is deliberate**; the two nearby security pins (`Microsoft.OpenApi` NU1903, `SQLitePCLRaw`) are already exact and must not be touched (plan `C-136`). **One open risk:** `Radio.API` and `Radio.Web` single-target conditionally, so their four floats were resolved under `net10.0-windows10.0.19041.0` only, while CI and the box build `net10.0` — plan Task 4 measures it (plan `C-133`). ⚠ **This row's own "`.claude/worktrees/**`" housekeeping trap is currently inert** — that directory is empty and `git worktree list` shows one checkout — but agent worktrees come and go, so the "edit only the three files under `src/`" instruction stands (plan `C-137`).

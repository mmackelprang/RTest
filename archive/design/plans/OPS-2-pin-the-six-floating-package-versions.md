# PLAN — `OPS-2` · Pin the six floating package versions

> **Row:** `OPS-2`, [`docs/queue/OPS-2.md`](../../docs/queue/OPS-2.md). 📋 queued, no dependency, claimable now.
> **Branch:** `chore/pin-floating-package-versions` (the row names it).
> **Estimate:** **0.5 d.** §0.6 says why, and what would push it to 1 d.
> **Planned against** `main` at **`656f58e6`**. Every line number below was read out of `main` at that
> commit via `git show main:<path>`, *not* out of the working tree — this checkout is sitting on
> `fix/phn-5-phone-pii-out-of-the-logs` at `35e4ed5a`. **`git diff main..HEAD` over `*.csproj`,
> `Directory.Build.props` and `nuget.config` is empty**, so the resolved versions measured here are
> valid for `main` (**C-129**).
> **The row's hard constraint governs everything below:** *pin to the versions that currently resolve
> — do NOT upgrade anything as part of this row.* §1 shows that constraint is satisfiable exactly,
> with zero drift, and §0.4 shows why.

---

## 0. Read this before Task 1

### 0.1 What this row is, in one paragraph

Six packages are declared with a `N.*` wildcard across seven `PackageReference` lines in three
project files. NuGet re-resolves each one against nuget.org on every restore that does not already
have an assets file — which includes **every CI run** (`.github/workflows/build.yml:49`,
`dotnet restore`, on an ephemeral runner). So the version of Radzen, Serilog, SoundFlow and Scalar
that a given build compiles against is a function of *when* it ran, not of what is in the
repository. This row replaces the six wildcards with the six exact versions they resolve to today.
It fixes no defect. **It must not be described as fixing one** (**C-135**) — the queue's own
ordering note at [`ORDERING-NOTES.md`](../../docs/queue/ORDERING-NOTES.md) records that a floating-version theory was investigated
on 2026-08-10 as the cause of a CI failure and **disproved**.

### 0.2 ✅ The row's count is exactly right — six packages, seven lines, three files

The row is unusually precise and **every part of it verified against `main` at `656f58e6`**. Stated
explicitly because a stale count would have been the finding, and here there is none.

```
$ git show main:src/Radio.Web/Radio.Web.csproj | grep -n 'Version="[0-9]*\.\*"'
23:    <PackageReference Include="Radzen.Blazor" Version="6.*" />
24:    <PackageReference Include="Serilog.AspNetCore" Version="8.*" />

$ git show main:src/Radio.API/Radio.API.csproj | grep -n 'Version="[0-9]*\.\*"'
34:    <PackageReference Include="Scalar.AspNetCore" Version="2.*" />
35:    <PackageReference Include="Serilog.AspNetCore" Version="8.*" />

$ git show main:src/Radio.Infrastructure/Radio.Infrastructure.csproj | grep -n 'Version="[0-9]*\.\*"'
32:    <PackageReference Include="SoundFlow" Version="1.*" />
34:    <PackageReference Include="Serilog" Version="4.*" />
35:    <PackageReference Include="Serilog.Extensions.Logging" Version="8.*" />
```

Seven lines. Six distinct packages — `Serilog.AspNetCore` appears twice, in `Radio.Web` and
`Radio.API`, exactly as the row says. Three files, all under `src/`. **Line numbers are unchanged
from the row's.**

**The search was wider than the row's claim, and the wider search found nothing more** — that is the
part worth recording, because "six" is only trustworthy if something looked for a seventh:

| Where | Searched for | Result |
|---|---|---|
| all 24 tracked `.csproj` (`src/`, `tests/`, `tools/`) | `Version="…*…"`, `Version="[…"`, `Version="(…"` | **only the 7 above**; nothing in `tests/` or `tools/` |
| all 24 tracked `.csproj` | `PackageReference` with **no** `Version` attribute | **none** — the six bare `</PackageReference>` hits are closing tags of multi-line elements whose opening tag carries a `Version` (e.g. `tests/Radio.Metrics.Tests/…:13`, `xunit.runner.visualstudio` `2.8.2`) |
| `Directory.Build.props` | any `PackageReference` / `PackageVersion` | **none** — it carries compiler settings, packaging metadata and the git-SHA stamping target only |
| repo root | `Directory.Packages.props` | **does not exist** — **central package management is NOT in play** (**C-130**) |
| repo root, all `.csproj` | `RestorePackagesWithLockFile`, `RestoreLockedMode`, `packages.lock.json` | **none tracked, none set** (**C-131**) |

### 0.3 ⭐ The six resolved versions, and where each number comes from

**Two independent sources agree on all six**, which is why they can be pinned without a fresh
restore. Both were read on 2026-09-06 against the restore graph generated 2026-09-05 06:33.

- **Source A — `dotnet list package`**, run per project. This is the source the row itself asks for
  (*"read it off the restore graph (`dotnet list package`) rather than picking a number"*).
- **Source B — `obj/project.assets.json`**, grepped for the `"<id>/<version>"` target keys.

| # | File:line (`main`) | Package | Requested | **Resolved** | A | B |
|---|---|---|---|---|---|---|
| `F1` | `src/Radio.Web/Radio.Web.csproj:23` | `Radzen.Blazor` | `6.*` | **`6.6.4`** | ✅ | ✅ |
| `F2` | `src/Radio.Web/Radio.Web.csproj:24` | `Serilog.AspNetCore` | `8.*` | **`8.0.3`** | ✅ | ✅ |
| `F3` | `src/Radio.API/Radio.API.csproj:34` | `Scalar.AspNetCore` | `2.*` | **`2.17.2`** | ✅ | ✅ |
| `F4` | `src/Radio.API/Radio.API.csproj:35` | `Serilog.AspNetCore` | `8.*` | **`8.0.3`** | ✅ | ✅ |
| `F5` | `src/Radio.Infrastructure/…csproj:32` | `SoundFlow` | `1.*` | **`1.4.1`** | ✅ | ✅ |
| `F6` | `src/Radio.Infrastructure/…csproj:34` | `Serilog` | `4.*` | **`4.4.0`** | ✅ | ✅ |
| `F7` | `src/Radio.Infrastructure/…csproj:35` | `Serilog.Extensions.Logging` | `8.*` | **`8.0.0`** | ✅ | ✅ |

**Re-derivation, exactly as run** — a Builder should reproduce these before editing anything:

```bash
dotnet list src/Radio.Web/Radio.Web.csproj            package
dotnet list src/Radio.API/Radio.API.csproj            package
dotnet list src/Radio.Infrastructure/Radio.Infrastructure.csproj package

# Second source, no jq on this machine:
grep -o '"Radzen.Blazor/[0-9.]*"\|"Serilog.AspNetCore/[0-9.]*"'  src/Radio.Web/obj/project.assets.json | sort -u
grep -o '"Scalar.AspNetCore/[0-9.]*"\|"Serilog.AspNetCore/[0-9.]*"' src/Radio.API/obj/project.assets.json | sort -u
grep -o '"Serilog/[0-9.]*"\|"Serilog.Extensions.Logging/[0-9.]*"\|"SoundFlow/[0-9.]*"' \
     src/Radio.Infrastructure/obj/project.assets.json | sort -u
```

⭐ **`F3` is the one number the row could not supply.** It says of `Scalar.AspNetCore`: *"resolved
version was not recorded on 2026-08-10; read it off the restore graph rather than picking a
number."* It is **`2.17.2`**. The row's other five predictions were all correct.

### 0.4 ⭐⭐ The pin is provably a no-op today, and there is no drift window (**C-132**)

The obvious worry with *"pin to what currently resolves"* is that *currently* is a moving target: if
nuget.org has published a higher in-range version since the last restore, a fresh CI restore would
already be on a different number, and the pin would silently be a **downgrade** rather than a
freeze.

**Measured, not assumed.** `--highest-minor` constrains the "latest" column to the same major
version — which is precisely what an `N.*` float resolves to:

```bash
dotnet list src/Radio.Web/Radio.Web.csproj            package --outdated --highest-minor
dotnet list src/Radio.API/Radio.API.csproj            package --outdated --highest-minor
dotnet list src/Radio.Infrastructure/Radio.Infrastructure.csproj package --outdated --highest-minor
```

**All three returned an empty table** (a header row and no packages). Every one of the six already
sits at the highest version available inside its declared major. So:

1. The pinned number **equals** what a fresh restore would pick today — the pin is behaviour-neutral
   by construction, not by hope.
2. There is **no pending in-range release to race**, so the Builder does not need to land within a
   deadline to keep the numbers true.

⚠ **This is a snapshot, not a guarantee.** It was true on 2026-09-06. If a `6.6.5` or an `8.0.4`
ships between now and the claim, the Builder's own §0.3 re-derivation will show it and **the newly
resolved number is the one to pin** — that is still "the version that currently resolves" and still
not an upgrade. ⛔ **Do not pin to the numbers in this plan if the re-derivation disagrees with
them.** Report the disagreement in the PR body and pin the measured value.

⭐ **A useful side observation:** `Serilog.Extensions.Logging`'s latest overall is **`10.0.0`**, and
`8.*` holds it at `8.0.0`. The wildcard is *capping* that dependency, not tracking it. Pinning to
`8.0.0` therefore preserves a real constraint rather than freezing an accident — and it makes the cap
visible in the file instead of implicit in a major-version glob.

### 0.5 The four things this row could have gone wrong on, all checked

**1. Per-TFM divergence — clean for `F5`–`F7`, unobserved for `F1`–`F4` (**C-133**).**
This is the one place the row could silently change behaviour, and it needs stating precisely because
the answer differs by project.

| Project | `TargetFramework(s)` declaration (`:3-4`) | On Windows | On Linux (CI, deploy) |
|---|---|---|---|
| `Radio.Infrastructure` | `<TargetFrameworks>`, conditional | `net10.0;net10.0-windows10.0.19041.0` | `net10.0` |
| `Radio.API` | `<TargetFramework>`, conditional | `net10.0-windows10.0.19041.0` **only** | `net10.0` **only** |
| `Radio.Web` | `<TargetFramework>`, conditional | `net10.0-windows10.0.19041.0` **only** | `net10.0` **only** |

`Radio.Infrastructure` builds **both** TFMs on this machine, and `dotnet list package` printed two
tables. **`Serilog 4.4.0`, `Serilog.Extensions.Logging 8.0.0` and `SoundFlow 1.4.1` are identical in
both** — so `F5`, `F6` and `F7` are measured to be TFM-independent, not argued to be.

`Radio.API` and `Radio.Web` use a **conditional single target**, so a Windows run can only ever
observe `net10.0-windows10.0.19041.0`. **`F1`–`F4` have therefore not been observed under `net10.0`,
which is the TFM CI and the box actually build.** Two reasons this is a small risk rather than a
gap, followed by the check that closes it anyway:

- NuGet picks a float by taking the highest version in range **that exists in the feed**, then checks
  TFM compatibility and *fails* if it is unsatisfiable — it does not quietly fall back to a lower
  version per TFM. So a float resolving differently across TFMs is not the normal behaviour.
- `Radio.Infrastructure` is the in-tree control: three floats, two TFMs, identical resolution.

⛔ **Do not treat that as proof.** Task 4 closes it by measurement on the Linux side.

**2. Packed NuGet packages — the risk class is empty (**C-134**).**
CI packs five projects (`.github/workflows/build.yml:61-65`, mirrored in `pack-local.ps1:30-34`):
`RTLSDRCore`, `Radio.AudioAnalysis`, `Radio.Metrics`, `Radio.Configuration`, `Radio.Fingerprinting`.

**None of the three files this row edits is one of them**, and none of the five reaches them:

- Neither `Radio.Web.csproj`, `Radio.API.csproj` nor `Radio.Infrastructure.csproj` declares a
  `PackageId` or `IsPackable` — and `Directory.Build.props:13` gates all packaging metadata on
  `'$(PackageId)' != ''`, so they are not packable at all.
- The only `ProjectReference` among the five packed projects is
  `Radio.Fingerprinting → Radio.Core, Radio.Metrics`. **No packed project references
  `Radio.Infrastructure`, `Radio.API` or `Radio.Web`.**

⇒ **All six floats are in app/consumer projects. Not one of them lands in a `.nuspec` dependency
range.** The two risk classes the row's reviewer would want separated are `{6 app}` and `{0 packed}`.

⚠ **One near-miss worth naming so it is not "discovered" in review:** `RTLSDRCore` — a **packed**
project — does depend on Serilog, at `src/RTLSDRCore/RTLSDRCore.csproj:29`,
`<PackageReference Include="Serilog" Version="4.0.0" />`. It is **already an exact version** and this
row does not touch it. Pinning `Radio.Infrastructure`'s `Serilog 4.*` → `4.4.0` does not alter
`RTLSDRCore`'s packed `>= 4.0.0` range.

**3. Deliberate floats — there are none (**C-136**).**
No comment, attribute or condition marks any of the seven lines as intentionally floating. But the
question is a fair one to have asked, because **this repository does carry deliberate security pins,
two of them, and both sit within three lines of a float**:

- `src/Radio.API/Radio.API.csproj:25-33` — `Microsoft.OpenApi` **`2.7.5`**, an eight-line comment
  citing `CVE-2026-49451 / GHSA-v5pm-xwqc-g5wc` and **`NU1903`**, overriding a vulnerable transitive
  `2.0.0`. It is the line **immediately above** `F3`.
- `src/Radio.Infrastructure/…csproj:38-40` — `SQLitePCLRaw` 3.0.x, citing
  `GHSA-2m69-gcr7-jv3q / CVE-2025-6965`.

Both are the `nu1903-sqlite-advisory` lineage. **Both are already exact versions**, so there is
nothing here to preserve or un-pin — but they are the reason the file looks like it has opinions
about versions, and a Builder should not mistake either comment for an argument about a neighbouring
float. ⛔ **Do not touch, reword, or reflow either comment.**

**4. The local NuGet feed cannot influence any of the six.**
`nuget.config` does `<clear />` then adds `local` → `./packages` **ahead of** nuget.org. NuGet
aggregates across sources and takes the highest match, so a stray local build could in principle
capture a float. `ls packages/` holds only `RTLSDRCore.1.0.0`, `Radio.AudioAnalysis.1.0.0`,
`Radio.Configuration.1.0.0`, `Radio.Fingerprinting.1.0.0`, `Radio.Metrics.{1.0.0,1.1.0,1.2.0}`.
**None of the six is present locally**, so nuget.org is their only source.

### 0.6 The estimate

**0.5 d.** Seven attribute edits, no code, no tests to write, and §0.4 has already established the
numbers are stable. The time is almost entirely in the two verification runs (§4), not the edit.

⚠ **What would push it to 1 d:** Task 4's Linux check (`C-133`). If `F1`–`F4` turn out to resolve
differently under `net10.0` than under `net10.0-windows10.0.19041.0`, this stops being a
seven-attribute row: the pin would have to be made TFM-conditional, or the row split, and the
"behaviour-neutral" claim would need re-deriving per TFM. **That outcome is unlikely and would be a
genuine finding — report it rather than working around it.**

### 0.7 Things Builder must NOT do

- ⛔ **Do not upgrade anything.** The row's hard constraint. If a pinned number differs from what the
  wildcard resolves to at claim time, the *measured* value wins — never the newest available. Six
  packages have a higher version outside their major (`Serilog.Extensions.Logging 10.0.0` most
  visibly); **none of them is in scope.**
- ⛔ **Do not introduce `Directory.Packages.props` / central package management** (**C-130**). It is a
  repo-wide build-system change affecting all 24 projects, wearing a seven-line diff's clothes.
  §6.1 files it.
- ⛔ **Do not add `RestorePackagesWithLockFile`** (**C-131**). It is the *other* answer to
  reproducibility and a strictly larger one — 24 lock files, a CI mode change, and a new merge-conflict
  surface. §6.2 files it. The row asked for pins.
- ⛔ **Do not touch the two security-pin comments** (`Microsoft.OpenApi`, `SQLitePCLRaw`) — §0.5 item 3.
- ⛔ **Do not edit any file outside the three named `.csproj`s**, except `docs/BUILDER_QUEUE.md` at
  merge (§5). No `appsettings`, no `CLAUDE.md`, no `Directory.Build.props`.
- ⛔ **Do not re-label this as a bug fix** (**C-135**), in the branch name, the commit subject, or the
  PR body.
- ⚠ **Do not run `dotnet restore --force`, `--force-evaluate`, or delete `obj/` before capturing the
  baseline.** §4.1's before/after comparison depends on the pre-edit assets file surviving.

### 0.8 ⚠ Eight constraints found while planning — numbering continues from `C-128` (`TEST-7`)

**`C-132` and `C-134` are the two that make this row provably safe.** **`C-133` is the one place it
could still go wrong.** **`C-137` retires a warning the row itself carries.**

---

**`C-129` — this checkout is not on `main`, and every number here was read off `main` anyway.**
`git worktree list` shows a single checkout, `D:/prj/RTest/RTest`, on
`fix/phn-5-phone-pii-out-of-the-logs` at `35e4ed5a`; `main` is `656f58e6`. Line numbers came from
`git show main:<path>`. The **resolved versions** came from `obj/project.assets.json` and
`dotnet list package` in the working tree, which is legitimate only because
`git diff main..HEAD -- '*.csproj' 'Directory.Build.props' 'nuget.config'` is **empty** — the restore
inputs are byte-identical between the two commits. Stated because otherwise the measurement's
provenance is unauditable.

---

**`C-130` — central package management is NOT in play.** There is no `Directory.Packages.props`
anywhere in the repository, `ManagePackageVersionsCentrally` is set nowhere, and
`Directory.Build.props` contains no `PackageReference` or `PackageVersion` item at all. **Every
version in this repository is declared on the `PackageReference` line itself**, which is why the fix
is seven attribute edits and not a props-file rewrite.

---

**`C-131` — there are no lock files and locked mode is off.** No tracked `packages.lock.json`, and
neither `RestorePackagesWithLockFile` nor `RestoreLockedMode` appears in any `.csproj` or props file.
**Consequence for this row's verification:** nothing on disk pins the graph today except the
untracked `obj/project.assets.json` files, so §4.1's before/after comparison must capture the
baseline by copying those files out **before** the edit. There is no committed artifact to diff
against.

---

**`C-132` — ⭐ MEASURED. All six floats already sit at the top of their major, so the pin is exactly
behaviour-neutral and nothing is racing it.** §0.4 has the commands and the empty-table result.
This is the single fact that turns *"pin to what currently resolves"* from a timing-sensitive
instruction into a deterministic one.

---

**`C-133` — ⚠ THE ONE REAL RISK. `F1`–`F4` were resolved under `net10.0-windows10.0.19041.0` only,
because `Radio.API` and `Radio.Web` use a conditional SINGLE `TargetFramework`. CI and the box build
`net10.0`.** §0.5 item 1 has the table and the reasoning. `Radio.Infrastructure` is the control and
its three floats resolve identically across both TFMs. **Task 4 measures the Linux side rather than
arguing it.** ⛔ If it diverges, stop and report — do not pin one TFM's answer over both.

---

**`C-134` — ⭐ the packed-package risk class is EMPTY, verified structurally.** None of the five
packed projects is edited by this row, and none references the three that are. `Radio.Web`,
`Radio.API` and `Radio.Infrastructure` declare no `PackageId`, and `Directory.Build.props:13` gates
packaging metadata on `'$(PackageId)' != ''`. ⇒ **no pin in this row can alter a published
`.nuspec` dependency range.** The near-miss — packed `RTLSDRCore` depending on `Serilog 4.0.0`,
already exact — is named in §0.5 item 2 so a reviewer does not have to find it.

---

**`C-135` — this row fixes no defect, and the queue says so in its own voice.**
[`ORDERING-NOTES.md`](../../docs/queue/ORDERING-NOTES.md): *"A floating-package-version theory was investigated on 2026-08-10 as
the cause of a CI failure and was **disproved**. Pinning is worth doing for reproducibility; it fixes
no known defect, and citing it as the root cause of that CI failure would be wrong."* ⛔ **The PR body
must not claim a fix.** The honest framing is in §7.3.

---

**`C-136` — no float is deliberate, but two neighbouring pins are, and both are security pins.**
§0.5 item 3. `Microsoft.OpenApi 2.7.5` (NU1903 / CVE-2026-49451) sits one line above `F3`;
`SQLitePCLRaw` 3.0.x (CVE-2025-6965) sits three lines below `F7`. Both already carry exact versions
and explanatory comments. **Nothing to do — and specifically, nothing to tidy.**

---

**`C-137` — ⚠ THE ROW'S OWN "housekeeping trap" IS CURRENTLY INERT, and saying so is safer than
leaving it to be rediscovered.** The row warns: *"`grep` for `Version="N.*"` across the repo also
hits `.claude/worktrees/**` — those are stale, untracked agent worktrees carrying copies of the same
csproj files."* **As of `35e4ed5a`, `.claude/worktrees/` is an empty directory** — `find` returns the
directory and nothing under it, `grep -rln 'Version="[0-9]*\.\*"' .claude/worktrees/` matches
nothing, and `git worktree list` reports only the main checkout.

⚠ **The warning stays worth honouring anyway**, and the plan does not delete it: agent worktrees are
created and destroyed between sessions, so the directory can repopulate before this row is claimed,
and `PHN-5`'s `C-100` records the sibling convention `D:/prj/RTest/worktrees/` being in use as
recently as last session. **The instruction is unchanged — edit only the three files under `src/` —
but a Builder who greps and sees exactly seven hits has not mis-run the search.**

---

## 1. Decision — plain exact versions, nothing cleverer

Four options were considered.

| Option | Verdict |
|---|---|
| **Exact version on each of the seven lines** — `Version="6.6.4"` etc. ✅ | **Taken.** Matches every other `PackageReference` in the repo (they are all exact already), matches the row's instruction literally, and is the smallest diff that achieves reproducibility. |
| **Central package management** (`Directory.Packages.props`) | **Rejected.** A repo-wide build-system change across 24 projects, with real merits, and entirely out of proportion to this row. §6.1. |
| **`packages.lock.json` + `RestoreLockedMode`** | **Rejected for this row.** It is the stronger answer — it pins the *transitive* graph too, which pinning direct references does not (**§7.2 item 3**) — but it is a different change with a different review surface and its own CI implications. §6.2 files it as the honest follow-up. |
| **Bracket ranges** (`[6.6.4]`) | **Rejected.** Semantically an exact pin, but written in a notation nothing else in this repository uses, and NuGet already treats a bare version as a floor rather than an exact match — which would make the notation the *only* thing carrying the intent. Consistency wins. |

⭐ **Why the diff is this boring, said once so it is not mistaken for under-scoping:** §0.4 established
each wildcard already resolves to the exact number being written in. The pin therefore changes what
the file *says*, and nothing about what it *builds*. That property is the whole deliverable, and §4
is how it gets proved rather than asserted.

---

## 2. Tasks

> All seven edits are one attribute value each. **Change only the `Version` attribute** — do not
> reorder lines, reflow whitespace, reorder attributes, or touch any surrounding comment.

### Task 1 — `Radio.Web` (`F1`, `F2`)

**File:** `src/Radio.Web/Radio.Web.csproj`, lines `23-24`.

```diff
   <ItemGroup>
     <PackageReference Include="Microsoft.AspNetCore.SignalR.Client" Version="10.0.0" />
-    <PackageReference Include="Radzen.Blazor" Version="6.*" />
-    <PackageReference Include="Serilog.AspNetCore" Version="8.*" />
+    <PackageReference Include="Radzen.Blazor" Version="6.6.4" />
+    <PackageReference Include="Serilog.AspNetCore" Version="8.0.3" />
     <PackageReference Include="Serilog.Sinks.Async" Version="2.1.0" />
     <PackageReference Include="Serilog.Sinks.File" Version="7.0.0" />
   </ItemGroup>
```

⭐ Note the two neighbours are already exact. After this edit the `ItemGroup` is uniform.

### Task 2 — `Radio.API` (`F3`, `F4`)

**File:** `src/Radio.API/Radio.API.csproj`, lines `34-35`.

```diff
     <PackageReference Include="Microsoft.OpenApi" Version="2.7.5" />
-    <PackageReference Include="Scalar.AspNetCore" Version="2.*" />
-    <PackageReference Include="Serilog.AspNetCore" Version="8.*" />
+    <PackageReference Include="Scalar.AspNetCore" Version="2.17.2" />
+    <PackageReference Include="Serilog.AspNetCore" Version="8.0.3" />
   </ItemGroup>
```

⛔ **The eight-line comment at `:25-32` above `Microsoft.OpenApi` is untouched** (`C-136`). It
documents an NU1903 security pin and has nothing to do with either line being changed here.

⚠ **`F4` must get the same `8.0.3` as `F2`.** They are the same package in two projects; pinning them
to different versions would be an actual behaviour change smuggled in as hygiene.

### Task 3 — `Radio.Infrastructure` (`F5`, `F6`, `F7`)

**File:** `src/Radio.Infrastructure/Radio.Infrastructure.csproj`, lines `32`, `34`, `35`.

```diff
   <!-- Common packages -->
   <ItemGroup>
     <PackageReference Include="InTheHand.Net.Bluetooth" Version="4.2.2" />
     <PackageReference Include="NAudio.Lame.CrossPlatform" Version="2.2.1" />
-    <PackageReference Include="SoundFlow" Version="1.*" />
+    <PackageReference Include="SoundFlow" Version="1.4.1" />
     <PackageReference Include="SharpCaster" Version="3.0.0" />
-    <PackageReference Include="Serilog" Version="4.*" />
-    <PackageReference Include="Serilog.Extensions.Logging" Version="8.*" />
+    <PackageReference Include="Serilog" Version="4.4.0" />
+    <PackageReference Include="Serilog.Extensions.Logging" Version="8.0.0" />
     <PackageReference Include="Microsoft.Extensions.Options" Version="10.0.0" />
```

⚠ **`SoundFlow` is the highest-consequence pin in the row**, and it is worth one sentence in the PR
body. It is the audio engine — `1.*` means any future `1.5.0` would enter the audio pipeline on the
next clean CI restore with no diff and no review. That is precisely the reproducibility hole this row
closes, and it is the best single argument for the row's existence.

⛔ **Do not touch the `SQLitePCLRaw` pin or its comment** at `:38-40` (`C-136`).

### Task 4 — ⭐ close `C-133` by measuring the Linux resolution

**No file is edited by this task.** It exists because `F1`–`F4` were only ever observed under the
Windows TFM (§0.5 item 1), and it is the one thing in this row that is not already proved.

**Preferred — on the box**, which is the real `net10.0` target and is already trusted for this
repository's Linux answers:

```bash
ssh mmack@radio 'cd /tmp && rm -rf ops2 && git clone --depth 1 --branch chore/pin-floating-package-versions \
  <repo-url> ops2 >/dev/null 2>&1 && cd ops2 && \
  dotnet list src/Radio.Web/Radio.Web.csproj package && \
  dotnet list src/Radio.API/Radio.API.csproj package'
```

⚠ **Keep it short and bounded.** `CLAUDE.md` records that this box is resource-constrained, is on
WiFi, and that heavy activity correlates with audible audio distortion. A shallow clone plus two
`list` calls is acceptable; a full build on the box is not — **do not build there**.

**Acceptable alternative — read it out of CI.** The pinned branch's own CI run does
`dotnet restore` on the Linux appserver runner (`build.yml:49`). If the build is green, the pinned
versions restored successfully **under `net10.0`**, which is the property being checked. A restore
that could not satisfy a pin fails loudly (`NU1102`), so a green Linux build is a positive result and
not merely an absent negative.

**What the result means:**

| Outcome | Action |
|---|---|
| Linux resolves the same four versions | ✅ `C-133` closed. Record the output in the PR body. |
| Linux resolves **different** versions | ⛔ **Stop.** Do not pin. Report it — that is a genuine finding about this repository's build, larger than this row, and §0.6 says it changes the estimate. |
| Neither route available | Ship on the CI-green signal and **say in the PR body that the per-TFM check was indirect.** Do not imply a measurement that was not made. |

---

## 3. Ordering

Tasks 1–3 are independent of each other and of their order — three files, no shared symbol, no
compile-order relationship. **Task 4 runs after 1–3** because it needs the pinned branch to exist.

**One PR, one commit.** Seven attribute edits across three files are a single reviewable unit; the
deliverable is *"the graph is now written down"*, which is not true of any proper subset.

**Suggested commit subject** (repo style — lowercase scope, a claim about the change, no defect
language per `C-135`):

```
chore(build): pin the six floating package versions to what already resolved
```

---

## 4. Verification — how a Builder proves the pin was behaviour-neutral

> ⭐ **The gate is equality, not health.** A green build proves the pin is *satisfiable*; it does not
> prove it is *the same graph*. §4.1 is the load-bearing check and it must be run first, because its
> baseline is destroyed by the edit.

### 4.1 ⭐ The restore graph is identical before and after — the primary gate

**Capture the baseline BEFORE editing anything** (`C-131`: nothing in git holds this):

```bash
mkdir -p /tmp/ops2-before
cp src/Radio.Web/obj/project.assets.json            /tmp/ops2-before/web.json
cp src/Radio.API/obj/project.assets.json            /tmp/ops2-before/api.json
cp src/Radio.Infrastructure/obj/project.assets.json /tmp/ops2-before/infra.json
```

Then make the edits, restore, and compare:

```bash
dotnet restore RadioConsole.sln > /tmp/ops2-restore.log 2>&1; echo "exit=$?"

for p in Web API Infrastructure; do
  case $p in Web) f=web;; API) f=api;; Infrastructure) f=infra;; esac
  echo "== Radio.$p"
  diff <(grep -oE '"[A-Za-z0-9._]+/[0-9][0-9A-Za-z.+-]*"' /tmp/ops2-before/$f.json | sort -u) \
       <(grep -oE '"[A-Za-z0-9._]+/[0-9][0-9A-Za-z.+-]*"' src/Radio.$p/obj/project.assets.json | sort -u)
done
```

⭐ **Every one of the three diffs must be empty.** That is the proof: the same package identities at
the same versions, **transitives included**, before and after. A non-empty diff means the pin changed
the graph — which the row forbids — and the Builder must stop and report rather than accept it.

⚠ **The comparison is on package-identity keys, not on the whole file.** `project.assets.json` also
embeds the requested range (`"Radzen.Blazor": "6.*"` → `"6.6.4"`), so a whole-file `diff` is
*expected* to differ in the `dependencies` and `projectFileDependencyGroups` blocks and would drown
the signal. **That expected difference is the diff a reviewer should ask to see**, and it is the
second check:

```bash
grep -E '"(Radzen\.Blazor|Serilog|Serilog\.AspNetCore|Serilog\.Extensions\.Logging|SoundFlow|Scalar\.AspNetCore)":' \
  src/Radio.Web/obj/project.assets.json src/Radio.API/obj/project.assets.json \
  src/Radio.Infrastructure/obj/project.assets.json
```

Expect the requested ranges to now read as exact versions, with **no `*` remaining**.

### 4.2 The wildcards are gone, and only from where they should be

```bash
git diff --stat main..HEAD          # exactly 3 files, 7 insertions, 7 deletions
git grep -n 'Version="[0-9]*\.\*"' -- '*.csproj'   # must return NOTHING
```

⚠ The second command is `git grep`, which searches **tracked files only** — so it cannot be polluted
by `.claude/worktrees/` even if that directory has repopulated (`C-137`).

### 4.3 Build and test gates

- `dotnet build --configuration Release` — **0 errors.** Warnings are errors in Release
  (`Directory.Build.props:3`), so a clean build additionally proves no new warning was introduced.
  ⭐ **Capture the warning count before and after and assert they are EQUAL** — the working baseline
  is **53**, but equality is the real gate and it survives the baseline moving under an unrelated
  merge:
  ```bash
  dotnet build RadioConsole.sln -c Release > /tmp/ops2-build.log 2>&1; echo "exit=$?"
  grep -cE 'warning [A-Z]+[0-9]+' /tmp/ops2-build.log
  ```
- `dotnet test --configuration Release` — full suite green.
  ⛔ **Never pipe it to `tail`** (`CLAUDE.md`): a pipeline reports `tail`'s exit code, and this has
  already produced a `0` beside five failing tests in this repository. Redirect, echo `$?`, then grep
  the file, and read the **per-project** summary lines.
  ```bash
  dotnet test RadioConsole.sln -c Release > /tmp/ops2-test.log 2>&1; echo "exit=$?"
  grep -E "Passed!|Failed!|error" /tmp/ops2-test.log
  ```
  Known-failing on Windows and **not** regressions: four `SrcVariableResamplerTests`
  (`libsamplerate.so.0`, `TEST-5`) and `NwsObservationIntegrationTests.RealNwsCall_*` (live network,
  `Category=Integration`, CI-excluded).
- **Task 4's Linux resolution check** (`C-133`), with its output in the PR body.

⚠ **The full suite is the row's own stated gate** — *"Verification for this row is a clean restore +
full `dotnet test`, not a visual diff — the point is proving the pinned graph is the graph that was
already building."* §4.1 is what makes that sentence checkable rather than aspirational.

### 4.4 What none of this proves

⚠ **Stated so a green run is not over-read.** The suite exercises the pinned versions; it does not
prove the pinned versions are *identical in behaviour* to the wildcards, because §0.4 already
established they are **the same packages**. If §4.1's diff is empty, behaviour-neutrality follows
from identity and needs no further argument. **If §4.1's diff is non-empty, no amount of green
testing rescues the claim** — the row's constraint has been violated and the correct response is to
stop.

### 4.5 On-box verification — not required

**None.** This row changes no runtime behaviour and produces byte-comparable binaries from the same
package graph. A deploy is not part of the merge gate. ⚠ **`OPS-2` is not exempt from the
auto-merge-on-green policy** — the queue's [`ORDERING-NOTES.md`](../../docs/queue/ORDERING-NOTES.md) exemption names `OPS-3`, not
this row.

---

## 5. Docs and queue

| # | Task |
|---|---|
| 1 | `docs/BUILDER_QUEUE.md` — Builder marks `OPS-2` ✅ at merge, links the PR, and adds a cycle banner entry. **⚠ Also correct the `Scalar.AspNetCore` cell**, which reads *"resolved version was not recorded"*; it is `2.17.2` (§0.3). Leaving a resolved unknown in a shipped row is the stale-record class this queue keeps warning about. |
| 2 | ⛔ **`design/FUTURE-WORK.md` — nothing.** This row stubs nothing and defers no implementation. §6 files two follow-ups, but both are build-system options rather than unimplemented features, and the queue is where they belong if the owner wants them. |
| 3 | ⛔ **`CLAUDE.md` — nothing.** No build command, deploy step or box fact changes. |
| 4 | ⛔ **No punch-list edit.** `OPS-2` has no `docs/HANDOFF-GA-PUNCH-LIST.md` row; stated so the absence is not read as an omission. |

**PR body must contain**, beyond the standard sections:

- The six resolved versions **with the command output that produced them** (§0.3).
- §4.1's three empty diffs, quoted.
- The before/after warning counts (§4.3).
- Task 4's Linux result, or an explicit statement that the check was indirect (`C-133`).
- ⚠ **A Docs Impact line**, and the sentence from `C-135`: this is reproducibility hygiene, it fixes
  no known defect, and it is **not** the root cause of the 2026-08-10 CI failure.

---

## 6. Deliberately not done

### 6.1 Central package management

`C-130`. A `Directory.Packages.props` would put all ~40 distinct package versions in one file and
make this class of drift structurally impossible rather than repeatedly fixable. **Genuinely
better, and genuinely a different row:** it touches all 24 project files, changes how every future
`PackageReference` is written, and needs its own review. ⛔ **Not folded in** — a seven-line hygiene
row is the worst possible vehicle for a repo-wide build-system migration.

### 6.2 ⭐ Lock files — the honest observation about what this row does *not* achieve

`C-131`. **Pinning direct references does not make the build reproducible. It makes it
less irreproducible.** The transitive graph is still resolved fresh on every clean restore, and a
transitive dependency declared as `>= x` by an upstream package can still float underneath a fully
pinned direct set.

`RestorePackagesWithLockFile` + `RestoreLockedMode` in CI is the change that would actually deliver
the property this row's title implies. **Not done here** because it is a larger change with a real
cost — 24 committed lock files, a new and conflict-prone merge surface, and a CI failure mode
(`NU1004`) that is unfamiliar to this repo.

⚠ **This belongs in the PR body, not just here.** A reviewer reading *"pin the floating versions"*
may reasonably conclude the build is now reproducible, and it is not. §7.3 gives the wording.
**Recommend to the owner that it be filed as its own row.**

### 6.3 The `Serilog.Extensions.Logging` major gap

`8.0.0` is pinned while `10.0.0` exists (§0.4). `Serilog` itself is on `4.x` and `Serilog.AspNetCore`
on `8.x`, so the Serilog family's versions across this solution are worth someone's attention.
⛔ **Not this row** — the constraint forbids upgrades, and a Serilog major bump is a behavioural
change to every log sink on a box where `LOG-11` and `PHN-5` have both recently made sink behaviour
load-bearing. **Named so the pin is not later read as an endorsement of `8.0.0` as the right
version** — it is a record of the version that was already in use.

---

## 7. Self-review

### 7.1 What was verified first-hand

- **All seven lines, on `main` at `656f58e6`**, via `git show main:<path>` — package id, requested
  range, and line number. All three files are byte-identical between `main` and this checkout's HEAD
  (`C-129`).
- **All 24 tracked `.csproj`** swept for wildcards, bracket ranges, and missing `Version` attributes.
  Seven hits, all accounted for. The six bare `</PackageReference>` matches were each opened and
  confirmed to be closing tags of `Version`-bearing elements.
- **`Directory.Build.props` in full** — no package items; `:3` `TreatWarningsAsErrors`; `:13` the
  `PackageId` gate that `C-134` turns on.
- **`nuget.config` in full**, and `ls packages/` — the local feed holds no package this row touches.
- **All six resolved versions, from two independent sources** that agree (§0.3).
- **`--outdated --highest-minor` on all three projects** — empty, which is `C-132`.
- **`Radio.Infrastructure` resolved under both TFMs** — identical for `F5`–`F7`.
- **The five packed projects' `PackageReference` and `ProjectReference` sets in full** — none reaches
  the three edited files (`C-134`).
- **`.github/workflows/build.yml:29-70`** — the Linux self-hosted runner, the fresh `dotnet restore`,
  and the five `dotnet pack` lines.
- **`.claude/worktrees/` is empty and `git worktree list` shows one checkout** (`C-137`).

### 7.2 What could not be verified, and what it costs

1. **`F1`–`F4` under `net10.0`** (`C-133`). This is a Windows machine and those two projects
   single-target conditionally, so the Linux resolution is inferred from NuGet's documented float
   semantics plus `Radio.Infrastructure` as a control. **Task 4 exists to close this and must not be
   skipped.**
2. **Nothing was built or tested for this plan.** No `dotnet build` or `dotnet test` was run — a
   Builder is concurrently working in this checkout on `fix/phn-5-phone-pii-out-of-the-logs`, and a
   Release build would have rewritten the `obj/` and `bin/` trees underneath them. The **53-warning
   baseline is therefore taken from the row's framing, not measured here** — which is exactly why
   §4.3 gates on before/after *equality* rather than on the number.
3. **This row does not make the build reproducible**, only less irreproducible — the transitive graph
   still floats (§6.2). Recorded as a limitation of the row rather than of the plan.
4. **`--highest-minor` was read on 2026-09-06 and is a snapshot** (`C-132`). A release inside any of
   the six ranges between now and the claim would change the right answer, which is why §0.3's
   re-derivation is an instruction and not a formality.
5. **The 2026-08-10 CI failure was not re-investigated.** This plan takes the queue's word
   (`:447`) that the floating-version theory was disproved. Nothing in this row depends on that
   being right — but the PR body's claim in `C-135` does.

### 7.3 The one thing most likely to be over-claimed, and the wording that avoids it

The failure mode for this row is not a bad diff — it is a PR body that says more than the diff did.
Two specific over-claims to avoid, and the honest version of each:

| ❌ Do not write | ✅ Write |
|---|---|
| *"Fixes non-deterministic builds / the CI flake."* | *"Reproducibility hygiene. Fixes no known defect; the floating-version theory for the 2026-08-10 CI failure was investigated and disproved ([`ORDERING-NOTES.md`](../../docs/queue/ORDERING-NOTES.md))."* |
| *"The build is now reproducible."* | *"Direct package references are now pinned. The transitive graph is still resolved fresh on each clean restore — lock files would be the change that closes that, and it is filed as a follow-up (§6.2)."* |

⭐ This is the `CLAUDE.md` § *Pre-Merge Review* discipline applied to a PR body rather than a code
comment: **the claim to check is the reason offered, not the conclusion.** A seven-line diff that
describes itself accurately is worth more than one that oversells a real improvement.

---

## Queue row wording

For `docs/BUILDER_QUEUE.md`. ⛔ **Planner did not apply this** — a Builder is concurrently editing
that file and `OPS-2` §5 item 1 is the Builder's own merge-time edit. Two changes, both to the
existing `OPS-2` row, preserving its established column shape:

**1. The `_plan TBD_` cell** becomes a link:

```
[design/plans/OPS-2-pin-the-six-floating-package-versions.md](../design/plans/OPS-2-pin-the-six-floating-package-versions.md)
```

**2. The `Scalar.AspNetCore` clause** — replace the unresolved-version text
(*"resolved version was not recorded on 2026-08-10; read it off the restore graph (`dotnet list
package`) rather than picking a number."*) with:

```
**2.17.2** _(resolved 2026-09-06 from `dotnet list package` and `obj/project.assets.json`, which agree; it was unrecorded when this row was filed)_
```

**3. Append to the row's description**, after the existing verification sentence:

```
**Planned 2026-09-06.** The row's count verified exact against `main` — six packages, seven lines, three files, line numbers unchanged. All six resolved: `Radzen.Blazor` **6.6.4**, `Serilog.AspNetCore` **8.0.3** (both projects), `Scalar.AspNetCore` **2.17.2**, `SoundFlow` **1.4.1**, `Serilog` **4.4.0**, `Serilog.Extensions.Logging` **8.0.0**. **`dotnet list package --outdated --highest-minor` is empty for all three projects**, so every float already sits at the top of its major and the pin is provably behaviour-neutral with no drift window (plan `C-132`). **No central package management** (no `Directory.Packages.props`) and **no lock files** (plan `C-130`, `C-131`), so the fix is seven attribute edits. **None of the six is in a packed project** — the five packed packages neither contain nor reference the three edited files, so no `.nuspec` dependency range moves (plan `C-134`). **No float is deliberate**; the two nearby security pins (`Microsoft.OpenApi` NU1903, `SQLitePCLRaw`) are already exact and must not be touched (plan `C-136`). **One open risk:** `Radio.API` and `Radio.Web` single-target conditionally, so their four floats were resolved under `net10.0-windows10.0.19041.0` only, while CI and the box build `net10.0` — plan Task 4 measures it (plan `C-133`). ⚠ **This row's own "`.claude/worktrees/**`" housekeeping trap is currently inert** — that directory is empty and `git worktree list` shows one checkout — but agent worktrees come and go, so the "edit only the three files under `src/`" instruction stands (plan `C-137`).
```

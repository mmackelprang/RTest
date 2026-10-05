# Contributing

## Build

```bash
dotnet build RadioConsole.sln -c Release --no-incremental > build.log 2>&1; echo "exit=$?"
grep -E "^\s+[0-9]+ Warning\(s\)|^\s+[0-9]+ Error\(s\)" build.log
```

**The gate is equality with the warning baseline, not zero warnings.** The baseline is **46 warnings and 0
errors on Windows** and **32 warnings and 0 errors on Linux**, all `IDE0011`. The two differ only because
`Radio.Infrastructure` builds a second, Windows-specific target framework on Windows. A change must not raise
the count for its host.

**Measure with `--no-incremental`.** An incremental build reports `0 Warning(s)`, because MSBuild does not
re-emit warnings for projects it did not rebuild. That result looks like an improvement, but the count is
simply unmeasured.

## Test

```bash
dotnet test RadioConsole.sln -c Release > test.log 2>&1; echo "exit=$?"
grep -E "Passed!|Failed!" test.log      # one summary line per test project
```

**Never pipe `dotnet test` into `tail`, `head` or `grep`.** A pipeline reports the exit status of its last
command, so `dotnet test ... | tail` exits `0` while tests fail. Redirect to a file, check `$?`, and then read
the per-project summary lines.

Some tests fail for known reasons and are not regressions:
- `SrcVariableResamplerTests` on Windows (it needs `libsamplerate.so.0`).
- Tests in `Category=Integration`, which call live network services. CI excludes them with
  `--filter "Category!=Integration"`.

Run a single test with `dotnet test --filter "FullyQualifiedName~ClassName.MethodName"`. Test conventions,
including the rule against racing a wall clock in tests, are in [docs/testing.md](docs/testing.md).

## Code style

2-space indentation, enforced by `.editorconfig`; file-scoped namespaces; nullable reference types; explicit
types preferred. Code must run on Linux: put Windows-only APIs behind `#if WINDOWS_TARGET`. Comments must claim
only what the code actually does.

## Changes and pull requests

- **One branch per change** (`feat/<topic>`, `fix/<topic>` or `docs/<topic>`), merged to `main` by pull request.
  Do not commit to `main` directly.
- Stage named paths. Do not use `git add -A`, `git add .` or `git commit -a`.
- CI (`.github/workflows/build.yml`) is advisory. `main` has no branch protection. The local build and test
  gates above decide whether a change merges. If CI disagrees with your local result, find out which one is wrong
  before merging.
- After a push, confirm the branch reached the remote with `git ls-remote --heads origin <branch>`. A failed
  push can still end with `Everything up-to-date`.

## Where the work queue lives

- [`docs/BUILDER_QUEUE.md`](docs/BUILDER_QUEUE.md) is the live backlog: one row per work item, with its
  status. The detail for each live row is in `docs/queue/<ID>.md`.
- [`docs/queue/ORDERING-NOTES.md`](docs/queue/ORDERING-NOTES.md) gives the required claim order.
  [`docs/queue/CROSS-REPO-HANDOFFS.md`](docs/queue/CROSS-REPO-HANDOFFS.md) lists the work that belongs to the
  RotaryPhone repo.
- Shipped rows and their dossiers are in [`archive/queue/`](archive/queue/).
- Stubbed or deferred features must be recorded in
  [`docs/known-issues-and-future-work.md`](docs/known-issues-and-future-work.md).

`CLAUDE.md` holds the full operational notes for the deployed box. AI agents working in this repo read it as
well.

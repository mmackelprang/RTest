# Radio Console: instructions for coding assistants

The project's instructions for AI coding assistants live in two places. Read them before making changes:

- [`CLAUDE.md`](../CLAUDE.md): build and test gates (including the Release warning baseline and the
  `dotnet test | tail` trap), code style, the merge gate, git traps, and the operational notes for the
  deployed box.
- [`docs/README.md`](../docs/README.md): the index of current documentation (architecture, configuration,
  deployment, integrations, testing, known issues).

In short: .NET 10, 2-space indentation, file-scoped namespaces, nullable reference types enabled. Code must
run on Linux; Windows-only APIs go behind `#if WINDOWS_TARGET`. The deployed target is an Intel N100 (`x86_64`)
running Ubuntu; a Raspberry Pi 5 (`linux-arm64`) is an untested build target. One branch per change, merged by
pull request; stage named paths only.

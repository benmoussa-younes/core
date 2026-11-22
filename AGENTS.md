# AGENTS.md — Core

C# foundation libraries for Linux desktop tools — primitives, platform ports,
IO, concurrency, serialization, MVP contracts and a shared Avalonia shell —
built for .NET 10, Native AOT and trimming. Core is consumed as source by the
products; it is not a NuGet package.

## Build and test

```sh
dotnet build -c Release
dotnet test -c Release
reuse lint
tools/scripts/binaries/lint-comments     # comment form of the added lines
tools/scripts/binaries/gate.scs          # one VERDICT line, exit 0/1
```

The gate needs Git, this file and the Scripts interpreter on `PATH` (`scripts
run`); it does not change tracked files. It checks the comment form of the lines
added since the base, builds before it tests, and fails the tests when the build
fails.

## Conventions

- Warnings are errors; the limits are measured, not silenced.
- `Result`/`Result<T>` and `CoreError` carry expected failure; an exception is a
  bug, not a control flow.
- Only AOT-safe and trim-safe types; `IsAotCompatible` and the trim analyzer stay
  on.
- Public API is declared in the `PublicAPI` pair and checked in the gate. Doc
  comments only on public API; no file header; a comment states a reason and
  carries `// WHY:` (or `# WHY:` in scripts). `lint-comments` checks the form of
  the lines added since the base.
- A subfolder may carry a short `AGENTS.md` when it has rules of its own; the
  closest file wins.

## Repository facts

- Core is `LGPL-3.0-or-later`; no other source is vendored here, and the gate
  runs the Scripts interpreter from `PATH`.
- The tracked Markdown is `README.md`, `AGENTS.md`, `CHANGELOG.md`,
  `SECURITY.md`, `CONTRIBUTING.md` and the deltas (`src/**/AGENTS.md`,
  `tests/AGENTS.md`, `tools/AGENTS.md`).
- A release is a signed tag and the source tree; no package is published.

## Commits and releases

- One branch per task, `work/<slug>`; one concern per commit; English,
  imperative subject; the body says why.
- Commits and tags are GPG-signed. A change lands on `main` only when
  `tools/scripts/binaries/gate.scs` prints `VERDICT: PASS`; a release is the
  maintainer's decision.

## Security

- No secret and no consumer's data in the tree; a sample uses documentation
  values only.
- A vulnerability report is private: follow `SECURITY.md`.

## Maintenance

- Keep the version and `CHANGELOG.md` up to date after every release.

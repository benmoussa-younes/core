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
tools/scripts/binaries/gate.scs          # one VERDICT line, exit 0/1
```

The gate needs Git, this file and the Scripts interpreter (vendored by source; a local the Scripts interpreter 0.5.0
on `PATH` is accepted until that source lands). It does not change tracked files.

## Conventions

- Warnings are errors; the limits are measured, not silenced.
- `Result`/`Result<T>` and `CoreError` carry expected failure; an exception is a
  bug, not a control flow.
- Only AOT-safe and trim-safe types; `IsAotCompatible` and the trim analyzer stay
  on.
- Public API is declared in the `PublicAPI` pair and checked in the gate. Doc
  comments only on public API; no file header; a comment states a reason.
- A subfolder may carry a short `AGENTS.md` when it has rules of its own; the
  closest file wins.

## Repository facts

- Core is `LGPL-3.0-or-later`; the vendored the Scripts interpreter source is `GPL-3.0-or-later`.
- The only Markdown files are `README.md`, `AGENTS.md`, `CHANGELOG.md`,
  `SECURITY.md` and `CONTRIBUTING.md`.
- A release is a signed tag and the source tree; no package is published.

## Commits and releases

- One branch per task, `work/<slug>`; one concern per commit; English,
  imperative subject; the body says why.
- Commits and tags are GPG-signed. The maintainer merges a green `check`
  locally; publishing (push, tag, release) is the owner's act.

## Security

- No secret and no consumer's data in the tree; a sample uses documentation
  values only.
- A vulnerability report is private: follow `SECURITY.md`.

## Maintenance

- Keep the version and `CHANGELOG.md` up to date after every release.

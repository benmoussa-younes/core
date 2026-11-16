# AGENTS.md — Core

A short `AGENTS.md` may exist in a subfolder when that folder has important
LLM rules. Keep it to a few lines; the closest file wins.

## Build and test

```sh
dotnet build -c Release
dotnet test -c Release
reuse lint
```

The gate is `tools/scripts/binaries/gate`. It needs Git, this `AGENTS.md`, and
`bcs`. `bcs` is vendored by source, not as a binary. Until that source lands, a
local `bcs` 0.5.0 on `PATH` is accepted. The gate prints one `VERDICT` line and
does not change tracked files.

## Repository facts

- Only these Markdown files are allowed: `README.md`, `AGENTS.md`,
  `CHANGELOG.md`, `SECURITY.md`, `CONTRIBUTING.md`.
- Core is LGPL-3.0-or-later. The vendored `bcs` source is GPL-3.0-or-later.
- Core is consumed as source. No NuGet package is produced.

## Maintenance

- Keep the project documented.
- Keep the version and `CHANGELOG.md` up to date after every release.

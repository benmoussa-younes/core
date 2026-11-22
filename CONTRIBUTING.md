# Contributing

## External contributions

External contributions are **not accepted at this time**. Core is maintained by
the owner; unsolicited pull requests, patches and issues may be closed without
review. This policy does not limit the rights the licences grant you: the source
is free software, and you may fork it and modify it under the licence terms.

## Support and warranty

No support, service level or warranty is provided. The software is distributed
under **LGPL-3.0-or-later**, without warranty, as stated in [`LICENSE`](LICENSE).
The Scripts interpreter is a toolchain prerequisite, installed separately
under **GPL-3.0-or-later**. You are responsible for your deployment and for
complying with the licences when you redistribute or modify the code.

## Maintainer build

- .NET SDK 10 (`global.json` floor `10.0.100`, `latestFeature`)
- `reuse` 5.0.2 for the licence check
- the Scripts interpreter for the repository gate, on `PATH`; no binary is
  placed in `tools/binaries/`.

```sh
dotnet build -c Release
dotnet test -c Release
reuse lint
tools/scripts/binaries/lint-comments
scripts run tools/scripts/binaries/gate.scs
```

The gate runs the build, the tests, `reuse lint`, the architecture checks, the
portable-layer guard and `lint-comments`. It expects a Git checkout (it uses
`git ls-files` and the diff against the base) and the root `AGENTS.md`. It builds
before it tests, so a red build cannot pass the test step on the previous
binaries.

## Maintainer rules

- One change per branch; let the maintainer review the result.
- Reproduce before fixing: a failing case, then the smallest change.
- Keep the public API documented and add `PublicAPI.Unshipped.txt` entries.
- Keep the libraries NativeAOT- and trim-safe; the gate rejects a Linux API in
  the portable assemblies.
- No secret, credential, internal URL or personal path in a tracked file.
- Core is LGPL-3.0-or-later; the documentation is CC BY-SA 4.0.
- The maintainer commits, tags, pushes and publishes.

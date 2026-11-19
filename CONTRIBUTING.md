# Contributing

## External contributions

External contributions are **not accepted at this time**. Core is maintained by
the owner; unsolicited pull requests, patches and issues may be closed without
review. This policy does not limit the rights the licences grant you: the source
is free software, and you may fork it and modify it under the licence terms.

## Support and warranty

No support, service level or warranty is provided. The software is distributed
under **LGPL-3.0-or-later**, without warranty, as stated in [`LICENSE`](LICENSE).
The the Scripts project is vendored by source under **GPL-3.0-or-later**; its
complete source and licence ship in the vendored tree. You are responsible for
your deployment and for complying with the licences when you redistribute or
modify the code.

## Maintainer build

- .NET SDK 10 (`global.json` floor `10.0.100`, `latestFeature`)
- `reuse` 5.0.2 for the licence check
- the Scripts interpreter for the repository gate; the scripts project is vendored by source, so
  no binary is placed in `tools/binaries/`. Until the source lands, a local
  the Scripts interpreter 0.5.0 on `PATH` is accepted.

```sh
dotnet build -c Release
dotnet test -c Release
reuse lint
scripts run tools/scripts/binaries/gate.scs.scs
```

The gate runs the build, the tests, `reuse lint`, the architecture checks and
the portable-layer guard. It expects a Git checkout (it uses `git ls-files`) and
the root `AGENTS.md`.

## Maintainer rules

- One writer per tree; work on a branch and let the owner review the result.
- Reproduce before fixing: a failing case, then the smallest change.
- Keep the public API documented and add `PublicAPI.Unshipped.txt` entries.
- Keep the libraries NativeAOT- and trim-safe; the gate rejects a Linux API in
  the portable assemblies.
- No secret, credential, internal URL or personal path in a tracked file.
- Core is LGPL-3.0-or-later; the vendored the Scripts interpreter source is GPL-3.0-or-later; the
  documentation is CC BY-SA 4.0.
- The owner commits, tags, pushes and publishes.

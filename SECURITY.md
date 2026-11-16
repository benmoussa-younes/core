# Security policy

## Supported versions

Security fixes are considered on a **best-effort** basis for the current 0.1.x
tree. There is no long-term support branch, support contract, response-time
guarantee or warranty. The software is distributed under
**LGPL-3.0-or-later**, without warranty, as stated in [`LICENSE`](LICENSE). The
vendored `bcs` source is under **GPL-3.0-or-later** and carries its own licence
in the vendored tree.

## Reporting a vulnerability

Report privately to <younes.benmoussa@pm.me>. Do **not** open a public issue
before a fix is available. Include:

- the affected assembly and version;
- a minimal reproducer (command, input file or code);
- the expected and observed behaviour;
- the impact you believe it has;
- whether you want to be credited.

Reports are reviewed on a best-effort basis; no fix, disclosure date or credit
is guaranteed.

## Scope

In scope: the `Core`, `Core.Platform`, `Core.Platform.Linux`, `Core.IO`,
`Core.Serialization`, `Core.Mvp`, `Core.Concurrency` and `Core.UI` libraries,
including their file, socket, process and parser boundaries.

Out of scope: vulnerabilities in a consuming product, in Avalonia or the .NET
runtime, and issues that require an already-compromised local account with the
same privileges as the process.

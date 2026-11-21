# Core

Foundation libraries for desktop tools: primitives, platform ports, IO,
concurrency, serialization, MVP contracts and a shared Avalonia shell. Linux is
the first adapter; .NET 10, NativeAOT-compatible and trim-safe.

## Assemblies

| Assembly | Contents |
|---|---|
| `Core` | `Result` / `Result<T>`, `CoreError`, `ByteSize`, `PathPattern`, `ICapability`, `ApplicationPaths`, the diagnostics contracts and the units of measure (energy, power, frequency, temperature) |
| `Core.Platform` | the OS ports and composition: `IFileSystem`, `IAtomicFileWriter`, `IMemoryMapping`, `IPathResolver`, `ISingleInstance`, `ICoreHostServices` and `CorePlatform` |
| `Core.Platform.Linux` | the Linux adapters: `LinuxFileSystem`, the directory reader, raw `mmap`, the atomic writer, owner-only rotated logs, the Unix-domain single-instance lock, `SystemProcessRunner`, `RawFileWalker` and `LinuxPlatform` |
| `Core.IO` | portable algorithms: descriptor-pinned directory walk, safe file reads, `MappedFile`, the parallel record scan/index, and the generic block pipeline; it contains no Linux type |
| `Core.Serialization` | one canonical data document (`DataDocument`: typed, ordered, columnar `DataTable`s with a `DataNode` escape hatch) read and written in multiple formats: the JSON, CSV and INI codecs, the atomic `DataFileStore` (document or typed via source-generated `JsonTypeInfo<T>`), and the `DataDocumentMapper`/`DataTableBuilder`/`KeyValueTable` helpers |
| `Core.Concurrency` | the bounded worker pool, the cooperative abort model, the block pipeline's parallelism primitives and the CPU-topology probe |
| `Core.Mvp` | passive-view MVP contracts and base classes: `IService`, `IModel`, `IView`, `IPresenter<TView>` and `PresenterBase<TView>` |
| `Core.UI` | the shared Avalonia shell: bootstrap and backend selection (native Wayland, X11 fallback, Avalonia platform detection elsewhere), the theme/accent tokens and controls (`Themes/`, `Controls/`), a GNOME header bar and Adwaita window, the content-dialog/About/Preferences suite, notifications, the tray, and the file-picker/clipboard adapters. `CoreHost` receives a `CorePlatform`; no Linux type is part of its public API |

## Requirements

- .NET SDK 10 (`global.json`)
- Linux

## Build and test

```sh
dotnet build -c Release
dotnet test -c Release
reuse lint
tools/scripts/binaries/gate.scs          # fast gate: build, tests, architecture
tools/scripts/binaries/gate.scs --perf   # manual/nightly AOT budgets (hyperfine, jq)
```

The gate needs a Git checkout (it uses `git ls-files`), the root `AGENTS.md`
and the Scripts interpreter on `PATH` (`scripts run`); it is not a
source-tarball tool.

## Release

A `v*` tag runs `.github/workflows/release.yml`: it builds, tests, checks the
licence metadata, verifies that the tag matches `VersionPrefix`, and attaches a
source tarball plus its SHA-256 to the GitHub release. Core 0.2.0 is source-only:
no NuGet package is published.

## Diagnostics

The three durable sinks live below the application's XDG state directory,
`$XDG_STATE_HOME/<vendor>/<application>/` (`~/.local/state/...` when the variable
is unset), and `ApplicationPaths` names them: `application.log`,
`operations.log`, `crash.log`.

A product with a `CoreHost` gets them from `CoreHost.Create` and routes unhandled
exceptions with `host.Attach(application)`. A product without a host builds the
same files on its own and assigns them, so an unhandled exception is written to
the crash log and the application log instead of being lost:

```csharp
var diagnostics = CoreDiagnostics.Create(options).Value;
application.Diagnostics = diagnostics;
```

Every unhandled exception then reaches both files, with the home directory and
the secrets redacted. Without a handler and without diagnostics, the fallback is
the redacted standard error.

## Reference

Core is source-only: it is consumed by `ProjectReference` (no NuGet package is
produced). In a product project file:

```xml
<ItemGroup>
  <ProjectReference Include="../../core/src/Core/Core.csproj" />
  <ProjectReference Include="../../core/src/Core.UI/Core.UI.csproj" />
  <ProjectReference Include="../../core/src/Core.Platform.Linux/Core.Platform.Linux.csproj" />
  <ProjectReference Include="../../core/src/Core.Serialization/Core.Serialization.csproj" />
  <ProjectReference Include="../../core/src/Core.IO/Core.IO.csproj" />
  <ProjectReference Include="../../core/src/Core.Mvp/Core.Mvp.csproj" />
</ItemGroup>
```

## Licence

LGPL-3.0-or-later. See [`LICENSE`](LICENSE); the other licence texts that cover
the assets (MIT, OFL-1.1, Apache-2.0, BSD-3-Clause, CC-BY-SA-4.0) live in [`LICENSES/`](LICENSES/), and the
third-party components are listed in [`THIRD-PARTY`](THIRD-PARTY). The
documentation is CC BY-SA 4.0. The Scripts interpreter is a toolchain
prerequisite, not a vendored dependency: it is installed separately and read
from `PATH`.

## Contributing and security

External contributions are **not accepted at this time**; unsolicited pull
requests or patches may be closed without review. No support, service level or
warranty is provided beyond the licences. See [`CONTRIBUTING.md`](CONTRIBUTING.md)
for the maintainer workflow and [`SECURITY.md`](SECURITY.md) for the private
vulnerability report.

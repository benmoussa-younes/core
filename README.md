# Core

Foundation libraries for desktop tools: primitives, platform ports, IO,
concurrency, serialization, MVP contracts and a shared Avalonia shell. Linux is
the first adapter; .NET 10, NativeAOT-compatible and trim-safe.

## Assemblies

| Assembly | Contents |
|---|---|
| `Core` | `Result` / `Result<T>`, `CoreError`, `ByteSize`, `PathPattern`, `ICapability`, `ApplicationPaths`, the diagnostics contracts and the units of measure (energy, power, frequency, temperature) |
| `Core.Platform` | the OS ports and composition: `IFileSystem`, `IAtomicFileWriter`, `IMemoryMapping`, `IPathResolver`, `ISingleInstance`, `ICoreHostServices` and `CorePlatform` |
| `Core.Platform.Linux` | the Linux adapters: `LinuxFileSystem`, the directory reader, raw `mmap`, the atomic writer, owner-only rotated logs, the Unix-domain single-instance lock, `SystemProcessRunner`, `RawFileWalker`, `LinuxPlatform`, and the Unix surface: peer credentials and descriptor passing (`UnixSocket`), pipes (`UnixPipe`), child spawning with inherited descriptors (`UnixProcessSpawn`, `UnixChildProcess`), pseudo-terminals and their window size (`UnixPseudoTerminal`, `UnixTerminalSize`), raw terminal mode (`UnixTerminalRawMode`), locked memory (`UnixLockedMemory`), process hardening and resource limits (`UnixProcessControl`, `UnixProcessLimits`), the `umask` scope and terminal echo control |
| `Core.IO` | portable algorithms: descriptor-pinned directory walk, safe file reads, `MappedFile`, the parallel record scan/index, and the generic block pipeline; it contains no Linux type |
| `Core.Serialization` | one canonical data document (`DataDocument`: typed, ordered, columnar `DataTable`s with a `DataNode` escape hatch) read and written in multiple formats: the JSON, CSV and INI codecs, the atomic `DataFileStore` (document or typed via source-generated `JsonTypeInfo<T>`), and the `DataDocumentMapper`/`DataTableBuilder`/`KeyValueTable` helpers |
| `Core.Concurrency` | the bounded worker pool, the cooperative abort model, the block pipeline's parallelism primitives and the CPU-topology probe |
| `Core.Mvp` | passive-view MVP contracts and base classes: `IService`, `IModel`, `IView`, `IPresenter<TView>` and `PresenterBase<TView>` |
| `Core.UI` | the shared Avalonia shell: bootstrap and backend selection (native Wayland, X11 fallback, Avalonia platform detection elsewhere), the theme/accent tokens and controls (`Themes/`, `Controls/`), a GNOME header bar and Adwaita window, the content-dialog/About/Preferences suite, notifications, the tray, and the file-picker/clipboard adapters. `CoreHost` receives a `CorePlatform`; no Linux type is part of its public API |

## Requirements

- .NET SDK 10 (`global.json`)
- Linux
- glibc 2.34 or later (Debian 12+, Ubuntu 22.04+): the spawn uses
  `posix_spawn_file_actions_addchdir_np` (2.29) and
  `posix_spawn_file_actions_addclosefrom_np` (2.34)

## Install

Core is source-only: a product references the projects it needs by
`ProjectReference`, from a sibling checkout or a submodule pinned to a tag; no
NuGet package is published.

## Usage

A product composes the assemblies it needs: `CoreHost` resolves the application
paths and the platform, `CoreAppBuilder.Configure<App>("id")` starts an Avalonia
UI. `core-consumer` owns the composition and the pin.

## Build and test

```sh
dotnet build -c Release
dotnet test -c Release
reuse lint
tools/scripts/binaries/lint-comments   # comment form and the SPDX header of code files
tools/scripts/binaries/gate.scs          # fast gate: build, tests, comments and SPDX, architecture
tools/scripts/binaries/gate.scs --aot   # manual/nightly AOT probe publish and self-test
```

The gate needs a Git checkout (it uses `git ls-files` and the diff against the
base), the root `AGENTS.md` and the Scripts interpreter on `PATH` (`scripts
run`); it is not a source-tarball tool. It builds before it tests, so a red
build fails the test step instead of running the previous binaries. The `CI`
workflow installs the Scripts interpreter (pinned version and SHA-256) and
`reuse`, then runs the same gate; the `desktop` job runs the GUI integration
suite under Xvfb.

## Release

A `v*` tag runs `.github/workflows/release.yml`: it builds, tests, checks the
licence metadata, verifies that the tag matches `VersionPrefix`, and attaches a
source tarball plus its SHA-256 to the GitHub release. Core is source-only: no
NuGet package is published.

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

## Forking

A fork is welcome. You maintain it: the licence grants use, study, change and
redistribution, and the software is delivered **as is**, without warranty or
support beyond the licence.

## Contributing and security

External contributions are **not accepted at this time**; unsolicited pull
requests or patches may be closed without review. No support, service level or
warranty is provided beyond the licences. See [`CONTRIBUTING.md`](CONTRIBUTING.md)
for the maintainer workflow and [`SECURITY.md`](SECURITY.md) for the private
vulnerability report.

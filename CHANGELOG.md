# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `UnixPeerCredentials` and `UnixSocket.TryGetPeerCredentials` read the peer
  identity of a connected Unix domain socket (`SO_PEERCRED`) through the BCL,
  so the peer-credentials path adds no libc import; a listening or unconnected
  socket is refused with `LinuxErrno.NotConnected`, a socket without peer
  credentials with `ProtocolNotAvailable`, and `LinuxErrno` names both
  (107, 92). `SingleInstanceLock` uses the same helper.
- `UnixLockedMemory.Lock(byte[])` pins a caller buffer and calls `mlock`;
  `Dispose` zeroes it before `munlock`, and a refused lock returns a
  `CoreError("unix.mlock")`.
- `UnixProcessControl` sets the dumpable and no-new-privileges flags and the
  thread name; `UnixProcessLimits` reads and writes the kernel resource limits;
  `UnixMaskScope` applies a process `umask` and restores it.
- `UnixTerminalEcho.Disable(int)` turns terminal echo off and restores the
  previous attributes on dispose; `TryGetEcho` reads the flag without changing
  it, and a non-terminal descriptor is refused with `NotATty` (25).
- `UnixSocket.TrySend`/`TryReceive` attach and receive file descriptors
  (`SCM_RIGHTS`, `MSG_CMSG_CLOEXEC`), `TrySetPassCredentials` enables the
  per-message sender credentials, and a truncated control buffer fails with
  `MessageTooLong` after closing every descriptor it installed.

### Security

- NuGet restores require a package signature trusted by the configured nuget.org
  repository or Microsoft author certificates.
- The Unix surface keeps secrets out of swap (`UnixLockedMemory` zeroes before
  `munlock`), lets a process turn off dumpable and tracing (`UnixProcessControl`)
  and set its no-new-privileges flag, exposes the kernel resource limits
  (`UnixProcessLimits`), hides a passphrase with terminal echo control, receives
  descriptors close-on-exec, and never leaks a descriptor on a refused or
  truncated message. `UnixMaskScope` applies the caller's mask and restores it
  (the tests use 0077). `SO_PEERCRED` identifies a connected peer and refuses a
  listening socket.

### Changed

- CI installs the Scripts interpreter from its pinned release and runs the same
  gate as the maintainer, so the comment form, the architecture checks and the
  build/test ordering apply to every pull request.
- The gate reads the version from `Directory.Build.props` and checks every
  evaluated `PackageReference` against `THIRD-PARTY`; the build restores from
  committed lock files and requires signed packages, and CI publishes the Unix
  probe with its ILC count and native self-test.
- Avalonia 12.2.0, xunit.v3 4.0.3, Microsoft.NET.Test.Sdk 18.12.0,
  Microsoft.CodeAnalysis.PublicApiAnalyzers 5.6.0 and coverlet.collector
  10.1.1.

## [0.2.3] - 2026-11-22

### Added

- `HeaderBar.IsDirty` shows a dot before the title; a view with a tab keeps the
  marker on the tab, and the header carries it for a view without one. The dot
  follows the theme and the accent, and `ClearChrome` removes it.

### Changed

- The gate runs `lint-comments` on the lines added since the base and
  `test-lint-comments` on a git fixture, and a failed build stops the test step
  instead of running it on the previous binaries. A build failure now reports the
  compiler error lines.

## [0.2.2] - 2026-11-21

### Added

- `SingleInstanceSocketName.For(vendor, application)` builds
  `<vendor>-<application>.socket` for a product whose runtime directory is
  already per-user. Both parts are validated path components, and the
  three-argument overload is unchanged.
- `CoreHostOptions.IncludeUserInSingleInstanceName` selects the socket name. Its
  default, `true`, keeps the released
  `<vendor>-<application>-<user>.sock`; `false` uses
  `<vendor>-<application>.socket` and reads no user name.

### Changed

- With `IncludeUserInSingleInstanceName: false`, `CoreHost.Create` no longer
  calls `Environment.UserName` and names the single-instance socket and its
  `.lock` after the vendor and the application only.

### Security

- The user-less name is correct only when `XDG_RUNTIME_DIR` is per-user and
  owner-only, as the XDG base directory specification requires. The default that
  carries the user stays the safe choice for a runtime directory shared by
  mistake.

## [0.2.1] - 2026-11-21

### Added

- `AboutDialog` accepts an `ISystemLinkLauncher`. When it is supplied, every link
  goes through the shell's scheme validation, so the platform launcher never sees
  an unvalidated `file:` or `smb:` URL. The parameter is optional: an existing
  construction keeps the platform navigation. A relative URL is refused when the
  dialog is constructed instead of failing inside the click handler.

### Changed

- The shell routes a programming fault from a lifecycle callback to
  `CoreApplication.ReportUnhandledException`, which writes the crash log, in
  addition to the banner the operator sees. An I/O, cancellation or disposal
  failure is a banner and a warning in the application log, because the operator
  can act on it.

## [0.2.0] - 2026-11-21

Security and robustness fixes from the 0.1.1 audit, the diagnostics composition
and a GUI integration suite. Every rule below landed with the test that failed
before it.

### Added

- `CoreDiagnostics` composes the application log, the operation journal and the
  crash log below the XDG state directory (`<state>/<vendor>/<application>`), and
  `CoreApplication.Diagnostics` gives a product without a host the same durable
  sink: an unhandled exception reaches both files instead of being lost.
  `ApplicationPaths` declares the three file names.
- `tests/Core.UI.IntegrationTests` hosts Avalonia on the real X11 backend, for the
  two facts the headless suite cannot prove: the dispatcher refuses a foreign
  thread, and a shell window reaches a compositor. It runs in CI under Xvfb.

### Changed

- The committed file mode is the intersection of the requested mode and the
  destination's: a commit never widens access, and an owner-only save repairs an
  over-permissive file. `EnsureDirectory` no longer tightens a pre-existing
  directory; it creates owner-only and refuses group- or world-writable.
- `CoreHost.Attach` writes the application log as well as the crash log, and
  `ShutdownAsync` restores the application's previous last-chance handler.
- `CoreApplication.ReportUnhandledException` no longer throws for the failure it
  reports: a handler or a sink that throws cannot take the process down.
- `TrayService.Show` reuses its native menu when the command set and the tooltip
  are unchanged.

### Security

- An owner-only save narrows an existing settings file to 0600 instead of
  preserving a wider mode, and a commit never widens a destination's access.
- A settings directory is created owner-only; a pre-existing one is checked for
  group or world write access, never re-chmoded, so a 0750 state directory
  survives and a directory owned by another user no longer fails with EPERM.
- A path with an interior NUL is refused by the platform encoder instead of being
  silently truncated at the NUL.
- Clearing a settings file removes only a regular file, so it no longer unlinks a
  symbolic link that loading and saving refuse.
- Two vendors that share an application name no longer share one single-instance
  socket; the name is built by `SingleInstanceSocketName`.
- A password prompt clears its field and its mask when it closes.
- A host that has shut down no longer raises activation, and it restores the
  application's last-chance handler on shutdown.
- The application survives an exception thrown from the dispatcher: it is reported
  once to the crash log and marked handled, instead of killing the process and
  writing the same entry twice.
- An unhandled exception reaches the application log as well as the crash log, and
  the home directory is redacted wherever it appears in a line, not only when the
  line is a bare path. Secrets are scrubbed before the line is capped.
- A UI service called from a foreign thread fails fast (`VerifyAccess`) instead of
  mutating the visual tree off the UI thread, and the dialog gate keeps the
  caller's context across its await. A cancellation raised while the dispatcher
  shuts down releases the gate instead of waiting for a post that never runs.
- A control character in `XDG_STATE_HOME` falls back to the spec default instead
  of throwing, a state directory that is a symbolic link is refused, and every
  state component the host creates is owner-only.
- A folder picked from a remote URI (`smb://`, `sftp://`, `http://`) no longer maps
  to a local path such as `/share`; the picker returns no path.

### Fixed

- The tray menu is reused when the command set and the tooltip are unchanged,
  instead of rebuilding the DBus menu on every publication.
- The modal layer is released only by the dialog that owns it: a stale completion
  no longer re-enables the shell under another overlay.
- A saved placement with a NaN or out-of-range size is ignored, and quitting then
  closing writes the placement once instead of twice.
- A closed presenter is no longer retained by the window for its whole lifetime.
- Each toast expires on its own deadline instead of a single timer that a stream of
  toasts re-armed forever; a burst keeps the ten most recent, and a live theme
  switch re-resolves the brushes of the toasts already showing.

## [0.1.1] - 2026-11-17

Security hardening for the raw Linux walker.

### Security

- Harden `RawFileWalker` name handling: derive the `openat` pointer from a
  bounds-checked array index instead of unchecked pointer arithmetic. This
  addresses the CodeQL `cs/unvalidated-local-pointer-arithmetic` finding
  (CWE-119, CWE-120, CWE-122, CWE-788). The pre-fix code held the same
  invariant in `ReadEntries`; the new form makes the bound explicit.
- Add a regression test with 300 long names so the 64 KiB name buffer grows
  and the long-name `openat` path stays covered.

## [0.1.0] - 2026-11-16

First public source release.

### Added

- **`Core`** — the zero-dependency kernel: `Result` / `Result<T>`, `CoreError`
  and its code catalogue, `ByteSize`, `PathPattern`, `PathComponent`,
  `PathRedaction`, `ICapability`, the application paths, the process seam,
  diagnostics, validation, text primitives and the units of measure.
- **`Core.Platform`** — the OS ports and the explicit composition: `IFileSystem`,
  `IAtomicFileWriter`, `IMemoryMapping`, `IPathResolver`, `ISingleInstance`,
  `ICoreHostServices` and `CorePlatform`.
- **`Core.Platform.Linux`** — the Linux adapters: the file system, directory
  reader, raw `mmap`, atomic writer, owner-only rotated diagnostics, Unix-domain
  single-instance lock, process runner, raw walker, XDG resolution and
  `LinuxPlatform`.
- **`Core.IO`** — the portable algorithms: descriptor-pinned directory walk,
  file metadata, safe streaming reads and writes, the read-only memory-mapped
  file, the parallel record scan and index, and the generic block pipeline.
- **`Core.Concurrency`** — the bounded worker pool, the cooperative abort model,
  the work partitioners and the CPU-topology probe.
- **`Core.Serialization`** — the canonical `DataDocument`/`DataTable` model, the
  JSON, CSV and INI codecs, the atomic `DataFileStore`, the typed mapping layer
  and the table/view/join/projection helpers.
- **`Core.Mvp`** — the passive-view MVP contracts: `IService`, `IModel`, `IView`,
  `IPresenter<TView>` and `PresenterBase<TView>`. Command activation carries the
  active view, so a background presenter can ignore another tab's command.
- **`Core.UI`** — the shared Avalonia shell: host bootstrap on `CorePlatform`,
  platform selection (Win32, macOS, Wayland, X11), theme and accent tokens,
  controls, the GNOME header bar and Adwaita window, dialogs, preferences,
  notifications, the tray, the file picker and the clipboard. The shell owns
  tabs and the navigation stack, the presenter initialize/activate/deactivate/
  dispose lifecycle, modal confirmation and prompt overlays, and an awaitable
  quit path (`QuitAsync`) that lets a product flush before shutdown.

### Notes

- Core is source-only: a consumer references the projects directly, no NuGet
  package is produced.
- External contributions are not accepted at this time. No support, service
  level or warranty is provided beyond the licences.
- Licence: LGPL-3.0-or-later; the documentation is CC BY-SA 4.0. The the Scripts interpreter
  scripts project is vendored by source, not as a binary, under
  GPL-3.0-or-later; its complete source and licence ship in the vendored tree.
- Every file under `src/**` and `tests/**` carries SPDX copyright and licence
  headers; non-commentable `.txt` files use `.license` sidecars.
- The texts in `LICENSES/` are the official Apache, GNU, Creative Commons and
  SPDX License List texts; the project `LICENSE` embeds the official LGPL text.

[0.2.1]: https://github.com/benmoussa-younes/core/releases/tag/v0.2.1

[0.2.0]: https://github.com/benmoussa-younes/core/releases/tag/v0.2.0

[0.1.1]: https://github.com/benmoussa-younes/core/releases/tag/v0.1.1

[0.1.0]: https://github.com/benmoussa-younes/core/releases/tag/v0.1.0

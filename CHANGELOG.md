# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
- Licence: LGPL-3.0-or-later; the documentation is CC BY-SA 4.0. The `bcs`
  scripts project is vendored by source, not as a binary, under
  GPL-3.0-or-later; its complete source and licence ship in the vendored tree.
- Every file under `src/**` and `tests/**` carries SPDX copyright and licence
  headers; non-commentable `.txt` files use `.license` sidecars.
- The texts in `LICENSES/` are the official Apache, GNU, Creative Commons and
  SPDX License List texts; the project `LICENSE` embeds the official LGPL text.

[0.1.1]: https://github.com/benmoussa-younes/core/releases/tag/v0.1.1

[0.1.0]: https://github.com/benmoussa-younes/core/releases/tag/v0.1.0

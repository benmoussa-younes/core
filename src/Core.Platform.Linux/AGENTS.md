<!-- SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me> -->
<!-- SPDX-License-Identifier: CC-BY-SA-4.0 -->

# Core.Platform.Linux

- Linux adapters only; portable assemblies must not reference this.
- Bounded reads; no-follow where the path is untrusted.
- Owner-only files and directories where the data is private.
- LP64 layouts only: `msghdr` 56, `iovec` 16, `cmsghdr` 16, `termios` 60,
  `rlimit` 16, `posix_spawnattr_t` 336, `posix_spawn_file_actions_t` 80; a
  32-bit build is unsupported.
- The spawn uses glibc 2.34 (`addchdir_np` 2.29, `addclosefrom_np` 2.34), no
  shell and no `PATH` lookup; the `/proc` scan closes every descriptor it
  observes except the sources and the targets; each source that is not a target
  is closed after its duplication. `addclosefrom_np` closes the close-from base
  (3 or the highest target plus one) and above, including a descriptor opened
  after the scan (skipped when the base reaches the soft limit). A descriptor at
  or above the soft `RLIMIT_NOFILE` cannot be closed by `addclose` (glibc returns
  `EBADF`), so the spawn is refused with `InvalidArgument` unless it is
  close-on-exec, which the exec closes; the caller keeps its other descriptors
  close-on-exec or ensures no other thread opens a non-CLOEXEC one during the
  spawn.
- `posix_openpt`/`grantpt`/`unlockpt`/`ptsname_r` own the master; the slave is
  opened by the child through `addopen` on 0, 1 and 2, so it becomes the
  controlling terminal after `SETSID`, and a terminal request cannot map those
  three descriptors. The window size is the 8-byte `winsize` read and written
  with `TIOCGWINSZ` (0x5413) and `TIOCSWINSZ` (0x5414); the raw attributes are
  the `cfmakeraw` set on the 60-byte `termios` and restore with `TCSAFLUSH`.
- A received descriptor is handed to the caller or closed by the call, never
  leaked.

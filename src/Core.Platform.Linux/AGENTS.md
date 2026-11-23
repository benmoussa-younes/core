<!-- SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me> -->
<!-- SPDX-License-Identifier: CC-BY-SA-4.0 -->

# Core.Platform.Linux

- Linux adapters only; portable assemblies must not reference this.
- Bounded reads; no-follow where the path is untrusted.
- Owner-only files and directories where the data is private.
- LP64 layouts only: `msghdr` 56, `iovec` 16, `cmsghdr` 16, `termios` 60,
  `rlimit` 16; a 32-bit build is unsupported.
- A received descriptor is handed to the caller or closed by the call, never
  leaked.

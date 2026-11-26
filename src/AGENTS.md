<!-- SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me> -->
<!-- SPDX-License-Identifier: CC-BY-SA-4.0 -->

# src

- C# 14 / .NET 10; NativeAOT- and trim-safe.
- No Linux API outside `Core.Platform.Linux`, except the CPU-topology probe
  (`Core.Concurrency/Platform.Linux/ProcessorTopology.cs`): the worker pool pins
  threads and reads physical cores, and `Core.Concurrency` cannot depend on the
  Linux adapter. The gate (F19) checks the rule and exempts that one file; a
  portable topology port in `Core.Platform` would remove the exception.
- Public API changes need `PublicAPI.Unshipped.txt`.

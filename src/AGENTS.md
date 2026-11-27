<!-- SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me> -->
<!-- SPDX-License-Identifier: CC-BY-SA-4.0 -->

# src

- C# 14 / .NET 10; NativeAOT- and trim-safe.
- No Linux API outside `Core.Platform.Linux`, except the CPU-topology probe
  (`Core.Concurrency/Platform.Linux/ProcessorTopology.cs`): the worker pool pins
  threads and reads physical cores, and `Core.Concurrency` cannot depend on the
  Linux adapter. The gate (F19) checks the rule and exempts that one file; a
  portable topology port in `Core.Platform` would remove the exception.
- The portable layers F19 checks are `Core`, `Core.IO`, `Core.Serialization`,
  `Core.Mvp`, `Core.Concurrency` and `Core.Platform`. `Core.UI` is not one: it
  selects the Avalonia backend by OS and the window chrome from that backend, so
  an `OperatingSystem.Is*` branch there is design, not a leak;
  `Core.Platform.Linux` holds the adapters.
- Public API changes need `PublicAPI.Unshipped.txt`.

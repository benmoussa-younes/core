<!-- SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me> -->
<!-- SPDX-License-Identifier: CC-BY-SA-4.0 -->

# tests

- xUnit v3; one test per behaviour.
- No network or real home; use temporary directories.
- GUI tests use the headless collection and `Headless.Dispatch`.
- The GUI integration suite (`tests/Core.UI.IntegrationTests`) is the exception:
  it owns the `desktop` collection, needs a real display, and every case is a
  `GuiFact` skipped unless `CORE_GUI_TESTS=1`.

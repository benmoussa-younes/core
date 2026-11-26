<!-- SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me> -->
<!-- SPDX-License-Identifier: CC-BY-SA-4.0 -->

# tests

- xUnit v3; one test per behaviour.
- No network or real home; use temporary directories.
- GUI tests use the headless collection and `Headless.Dispatch`.
- The GUI integration suite (`tests/Core.UI.IntegrationTests`) is the exception:
  it owns the `desktop` collection, needs a real display, and every case is a
  `GuiFact` skipped unless `CORE_GUI_TESTS=1`. Run it under a virtual X server:

  ```sh
  CORE_GUI_TESTS=1 CORE_DISABLE_WAYLAND=1 WAYLAND_DISPLAY= \
    xvfb-run -a dotnet test tests/Core.UI.IntegrationTests/Core.UI.IntegrationTests.csproj -c Release
  ```

  `CORE_DISABLE_WAYLAND=1` and an empty `WAYLAND_DISPLAY` keep the run on Xvfb
  when the session has a Wayland compositor; the `desktop` CI job runs the same
  suite.
- The VSTest console summary can print an impossible `Duration:` for
  `Core.Platform.Linux.Tests` (it equals the time of day); it appears once the
  `AnInvalidRequestIsRefused` theory rows run. It is a runner accounting
  artifact, not a measurement: the TRX `Times` and the per-test `duration`
  attributes are correct, and the gate keeps one TRX per project as its
  evidence. Never report a duration from that console line.

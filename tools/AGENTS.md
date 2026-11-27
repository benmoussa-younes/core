<!-- SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me> -->
<!-- SPDX-License-Identifier: CC-BY-SA-4.0 -->

# tools

- `tools/scripts/binaries/gate.scs` is read-only for tracked files: it clears the
  ignored `TestResults/` and writes one TRX per test project there, nothing else.
- `tools/scripts/binaries/lint-comments [--base <rev>] [<repository>]` judges the
  lines added since the base: `// WHY:` for a C# line comment, `# WHY:` for an
  inline scripts comment, and a doc comment only on public API. Every code file
  opens with the two SPDX header lines; a file without them is refused, so a
  removal is never silent. It reads the diff and the untracked files, and writes
  nothing.
- `tools/scripts/binaries/test-lint-comments` proves the linter on a git fixture
  under `/tmp/opencode/`; it writes nothing in the repository.
- `tools/scripts/binaries/test-gate` proves the gate's argument guard: it runs the
  gate with an unknown argument, an empty one and one carrying a newline, and
  requires exit 1, one verdict line and `VERDICT: FAIL`; the child carries a
  deadline so a broken guard fails instead of letting the run nest. It writes
  nothing.
- `tools/scripts/modules/` holds the blocks the binaries share (`verdict`,
  `repo`, `fs`, `testkit`); a new block descends here before it is copied.

<!-- SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me> -->
<!-- SPDX-License-Identifier: CC-BY-SA-4.0 -->

# tools

- `tools/scripts/binaries/gate.scs` is read-only for tracked files: it writes its
  test TRX under the ignored `TestResults/` and nothing else.
- `tools/scripts/binaries/lint-comments [--base <rev>] [<repository>]` judges the
  lines added since the base: `// WHY:` for a C# line comment, `# WHY:` for an
  inline scripts comment, and a doc comment only on public API. It reads the diff
  and the untracked files, and writes nothing.
- `tools/scripts/binaries/test-lint-comments` proves the linter on a git fixture
  under `/tmp/opencode/`; it writes nothing in the repository.
- `tools/scripts/modules/` holds the blocks the binaries share (`verdict`,
  `repo`, `fs`, `testkit`); a new block descends here before it is copied.

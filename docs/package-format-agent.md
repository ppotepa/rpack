# Agent Package Format

The agent package format is the `package-root` / `.rpack` flow used for ChatGPT-generated packages.

Principles:

- prefer payload operations over patches
- keep package-root authoring local and deterministic
- track repair metadata with `RepairsPackageId` and `RepairsOperations`
- use `.git/rpack/operation-journal` for apply history and undo
- make conflict output compact enough to feed back into repair generation

Commands:

- `rpack pack-root`
- `rpack validate-package-root`
- `rpack plan`
- `rpack apply-root`
- `rpack apply-remaining-root`
- `rpack diagnose-root`
- `rpack repair-root`
- `rpack undo-root`

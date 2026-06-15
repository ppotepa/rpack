# Current Package Format

The current package format is `rpack-v1`.

Key properties:

- manifest casing is PascalCase
- `Format` is `rpack-v1`
- `Mode` is `working-tree-patch`
- packages contain one or more patch entries
- patch content remains Git diff text
- `.git/rpack` stores local apply history

This format continues to support existing `check`, `apply`, `diagnose`, `rebase`, `undo`, and `history` workflows.

# Actions

Lifecycle actions run after validation and before/after patch application.

Supported categories include:

- command actions
- script actions
- `rpack.commit`

The current goal is to keep actions isolated from package validation and to make their effects visible in logs and diagnostics.

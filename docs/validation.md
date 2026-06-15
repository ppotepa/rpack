# Validation

Validation is split between the current package format and the agent package format.

For `rpack-v1`:

- validate manifest structure
- verify checksums
- validate patch path safety
- validate lifecycle actions

For agent packages:

- validate manifest presence and schema
- validate operations document presence
- validate payload hashes
- validate safe relative paths
- validate repair metadata consistency when present

Validation should return structured issues where possible and stable exit codes from the CLI.

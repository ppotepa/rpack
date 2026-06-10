# rpack package actions

rpack packages can declare lifecycle actions in `manifest.json`.

Actions run after manifest validation and checksum verification.

Execution order:

1. validate manifest and checksums
2. check repository cleanliness, base and patch dry-run
3. run `PreActions`
4. apply patches
5. store rpack apply state
6. run `PostActions`
7. write apply history with action results

Supported action kinds:

- `command` runs a shell command in the target repository root.
- `powershell` or `ps1` runs an action script from `actions/*.ps1`.
- `batch`, `bat` or `cmd` runs an action script from `actions/*.bat` or `actions/*.cmd`.
- `rpack.commit` stages only package-changed paths and creates a local Git commit.

Script actions must live under `actions/` inside the package and must include a `Sha256` value in the manifest.

Example:

```json
{
  "PreActions": [
    {
      "Name": "Preflight",
      "Kind": "command",
      "Command": "dotnet test",
      "Optional": false
    }
  ],
  "PostActions": [
    {
      "Name": "Commit package",
      "Kind": "rpack.commit",
      "Message": "rpack: apply {PackageTitle}",
      "Optional": false
    }
  ]
}
```

Use `--no-actions` to apply only patch content without running manifest actions.

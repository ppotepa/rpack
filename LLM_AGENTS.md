# rpack for LLM Agents

This document explains how coding agents should prepare and deliver `.rpack` files.

`rpack` is designed for agents that can edit a repository but should not directly commit, push, or rewrite Git history. The agent prepares a validated patch package. The human then inspects and applies it locally.

## Agent Contract

When asked to deliver changes as an `.rpack` package, the agent should:

- modify the working tree normally
- avoid creating commits
- avoid changing branches
- avoid rewriting Git history
- create a `.rpack` package from the final diff
- inspect the package
- check the package against the source repo when possible
- provide the package file and a short summary

The human applies the package with:

```bash
rpack check change.rpack
rpack apply change.rpack
```

On Windows, if rpack was installed from the MSI, the human can also place the
`.rpack` file inside the target repository and double-click it. The Windows
launcher collects opened packages in one batch window, performs inspect,
checksum verification, `git apply --check`, and then asks for confirmation before
applying checked ready packages.

If the package paths are relative to a snapshot subdirectory rather than the Git
root, tell the user to apply with `--path-prefix`. Example: if the snapshot root
was `D:\repo\src` and Git root is `D:\repo`, patch paths like `aot/file.cs`
need:

```bash
rpack check change.rpack --path-prefix src
rpack apply change.rpack --path-prefix src
```

## Basic Workflow

From the repository root:

```bash
git status --short
```

Make the requested code changes.

Review the final diff:

```bash
git diff --stat
git diff
```

Create the package from unstaged working tree changes:

```bash
rpack create -o change.rpack
```

Inspect the package:

```bash
rpack inspect change.rpack
```

Check that the package applies cleanly to the current repo:

```bash
rpack check change.rpack
```

Deliver `change.rpack` to the user.

`rpack` uses whitespace-compatible patch context matching by default. This helps
packages survive CRLF/LF and final-newline drift between working trees. If exact
context whitespace matters, tell the user to run strict mode:

```bash
rpack check change.rpack --strict
rpack apply change.rpack --strict
```

## Staged-Only Workflow

If the agent needs precise control over what enters the package, stage only the intended files:

```bash
git add path/to/file1 path/to/file2
git diff --cached --stat
rpack create --staged -o change.rpack
```

Use this when the working tree contains unrelated changes that must not be included.

## Revision Range Workflow

If the agent is working from an existing commit range but still wants to deliver a working-tree patch package:

```bash
rpack create --from HEAD~2 --to HEAD -o change.rpack
```

This does not preserve commits. It packages the resulting diff as a patch.

## Manual Package Creation Without rpack

If `rpack` is not installed in the agent environment, the agent can still create a valid `.rpack` file manually.

Important: the current `rpack-v1` reader expects JSON property names in the same casing used by the .NET model, for example `Format`, `Mode`, `Patches`, `Path`, and `Sha256`. Do not use camelCase property names when creating packages manually.

### Accepted Package Structure

An `.rpack` file is a ZIP archive with this structure:

```txt
change.rpack
├─ manifest.json
├─ patches/
│  ├─ 0001-core.patch
│  ├─ 0002-tests.patch
│  └─ 0003-docs.patch
├─ checksums.sha256
└─ README.md
```

Only these parts are required by the current reader:

- `manifest.json`
- one or more files under `patches/`
- the SHA-256 value in `manifest.json`

`checksums.sha256` and `README.md` are recommended for humans and tooling consistency.

### Patch Requirements

Each patch must be a Git diff that `git apply` can read:

```bash
git diff --binary > patches/change.patch
```

For staged changes:

```bash
git diff --binary --cached > patches/change.patch
```

For a revision range:

```bash
git diff --binary HEAD~2 HEAD > patches/change.patch
```

The target repository will later validate the patch with:

```bash
git apply --check <patch>
```

For multi-patch packages, the order of the `Patches` array is the application order.
Do not rely on filename sorting. The manifest is the source of truth:

```json
"Patches": [
  { "Path": "patches/0001-core.patch", "Kind": "git-diff", "Sha256": "..." },
  { "Path": "patches/0002-tests.patch", "Kind": "git-diff", "Sha256": "..." },
  { "Path": "patches/0003-docs.patch", "Kind": "git-diff", "Sha256": "..." }
]
```

`rpack undo` reverses the same list in reverse order.

### Minimal Valid manifest.json

This is the smallest practical manifest that current `rpack` accepts:

```json
{
  "Format": "rpack-v1",
  "Mode": "working-tree-patch",
  "RequiresCleanTree": true,
  "Patches": [
    {
      "Path": "patches/change.patch",
      "Kind": "git-diff",
      "Sha256": "<lowercase-sha256-of-patches/change.patch>"
    }
  ]
}
```

### Recommended manifest.json

Agents should prefer the fuller form:

```json
{
  "Format": "rpack-v1",
  "Id": "fix-login-validation-001",
  "Title": "Fix login validation",
  "Description": "Updates login validation and related tests.",
  "CreatedAtUtc": "2026-06-06T16:30:00Z",
  "BaseCommit": "<base-commit-sha-or-empty>",
  "Source": {
    "Repository": "<repository-name-or-empty>",
    "BaseCommit": "<base-commit-sha-or-empty>",
    "HeadCommit": "<head-commit-sha-or-empty>"
  },
  "RequiresCleanTree": true,
  "Mode": "working-tree-patch",
  "Patches": [
    {
      "Path": "patches/change.patch",
      "Kind": "git-diff",
      "Sha256": "<lowercase-sha256-of-patches/change.patch>"
    }
  ],
  "Validation": []
}
```

`BaseCommit` and `Source.BaseCommit` are diagnostic by default. A mismatch is a warning unless the user applies with `--strict-base`.

To include multiple patches, add more objects to `Patches` in the exact order they should be applied.

### checksums.sha256

Use the same lowercase SHA-256 values as in the manifest:

```txt
<lowercase-sha256-of-patches/0001-core.patch>  patches/0001-core.patch
<lowercase-sha256-of-patches/0002-tests.patch>  patches/0002-tests.patch
<lowercase-sha256-of-patches/0003-docs.patch>  patches/0003-docs.patch
```

The current implementation validates the checksum from `manifest.json`. The `checksums.sha256` file is included for transparency and future tooling.

### Build Manually With PowerShell

From the repository root:

```powershell
$packageName = "change.rpack"
$work = Join-Path $env:TEMP ("rpack-manual-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path (Join-Path $work "patches") | Out-Null

$patchPath = Join-Path $work "patches/change.patch"
git diff --binary --output="$patchPath"

$patchBytes = [IO.File]::ReadAllBytes($patchPath)
$sha = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($patchBytes)).ToLowerInvariant()
$base = (git rev-parse HEAD).Trim()
$repo = Split-Path -Leaf (git rev-parse --show-toplevel)
$created = [DateTimeOffset]::UtcNow.ToString("O")

$manifest = @"
{
  "Format": "rpack-v1",
  "Id": "manual-$($created.Replace(':', '').Replace('.', ''))",
  "Title": "Manual rpack package",
  "Description": "",
  "CreatedAtUtc": "$created",
  "BaseCommit": "$base",
  "Source": {
    "Repository": "$repo",
    "BaseCommit": "$base",
    "HeadCommit": "$base"
  },
  "RequiresCleanTree": true,
  "Mode": "working-tree-patch",
  "Patches": [
    {
      "Path": "patches/change.patch",
      "Kind": "git-diff",
      "Sha256": "$sha"
    }
  ],
  "Validation": []
}
"@

Set-Content -Path (Join-Path $work "manifest.json") -Value $manifest -NoNewline
Set-Content -Path (Join-Path $work "checksums.sha256") -Value "$sha  patches/change.patch"
Set-Content -Path (Join-Path $work "README.md") -Value "# Manual rpack package"

if (Test-Path $packageName) {
  Remove-Item $packageName
}

Compress-Archive -Path (Join-Path $work "*") -DestinationPath $packageName
```

Then verify:

```powershell
rpack inspect change.rpack
rpack check change.rpack
```

The PowerShell example creates a single-patch package. For a multi-patch package, create more files under `patches/`, compute each SHA-256 separately, and add each patch object to `Patches` in application order.

### Build Manually With Bash

From the repository root:

```bash
package_name="change.rpack"
work="$(mktemp -d)"
mkdir -p "$work/patches"

git diff --binary > "$work/patches/change.patch"
sha="$(sha256sum "$work/patches/change.patch" | awk '{print $1}')"
base="$(git rev-parse HEAD)"
repo="$(basename "$(git rev-parse --show-toplevel)")"
created="$(date -u +"%Y-%m-%dT%H:%M:%SZ")"

cat > "$work/manifest.json" <<EOF
{
  "Format": "rpack-v1",
  "Id": "manual-${created}",
  "Title": "Manual rpack package",
  "Description": "",
  "CreatedAtUtc": "${created}",
  "BaseCommit": "${base}",
  "Source": {
    "Repository": "${repo}",
    "BaseCommit": "${base}",
    "HeadCommit": "${base}"
  },
  "RequiresCleanTree": true,
  "Mode": "working-tree-patch",
  "Patches": [
    {
      "Path": "patches/change.patch",
      "Kind": "git-diff",
      "Sha256": "${sha}"
    }
  ],
  "Validation": []
}
EOF

printf "%s  patches/change.patch\n" "$sha" > "$work/checksums.sha256"
printf "# Manual rpack package\n" > "$work/README.md"

(cd "$work" && zip -r "$OLDPWD/$package_name" manifest.json patches checksums.sha256 README.md)
```

Then verify:

```bash
rpack inspect change.rpack
rpack check change.rpack
```

The Bash example creates a single-patch package. For a multi-patch package, create more files under `patches/`, compute each SHA-256 separately, and add each patch object to `Patches` in application order.

### Manual Package Validation Checklist

Before delivering a manually created package, the agent should verify:

```bash
rpack inspect change.rpack
rpack check change.rpack
```

If `rpack` is not available for validation, at least verify:

```bash
unzip -l change.rpack
sha256sum patches/change.patch
git apply --check patches/change.patch
```

The SHA-256 printed for every patch file must exactly match the corresponding `Sha256` value in `manifest.json`.

## Expected Response To The User

When returning an `.rpack`, the agent should include:

```txt
Package: change.rpack
Format: rpack-v1
Mode: working-tree-patch
Changed files: <number>
Added lines: <number>
Removed lines: <number>
Validation:
- rpack inspect: passed
- rpack check: passed/failed/not run
Notes:
- <important assumptions or manual follow-up>
```

Do not paste the full patch into chat unless explicitly requested. Attach or provide the `.rpack` file.

## Applying A Package

The user should apply a received package from the target repository:

```bash
rpack inspect change.rpack
rpack check change.rpack
rpack apply change.rpack
```

For an interactive terminal flow:

```bash
rpack open change.rpack
```

For the Windows MSI flow, put one or more `.rpack` files somewhere inside the
target Git repository and double-click them. rpack collects them in one batch
window. Normal double-click requires a clean working tree, except for the clicked
`.rpack` files themselves if they are untracked inside the repo. Holding Shift
while double-clicking allows a dirty working tree, equivalent to:

```bash
rpack open change.rpack --allow-dirty
```

Agents should not ask users to use Shift/dirtied-tree mode unless existing local
changes are intentional and relevant.

If the package was built from a subdirectory snapshot, prefix the patch paths at
runtime:

```bash
rpack inspect change.rpack --path-prefix src
rpack check change.rpack --path-prefix src
rpack apply change.rpack --path-prefix src
```

This keeps the `.rpack` checksum valid. `rpack` verifies the original package
first, then prefixes temporary patch paths before calling `git apply`.

If the target repo has different Git history but the patch context matches, `rpack check` may pass with a base commit warning. This is expected.

`rpack check` ignores whitespace-only differences in patch context by default.
Use strict mode only when the target must match the package context exactly:

```bash
rpack check change.rpack --strict
rpack apply change.rpack --strict
```

Use strict base matching only when the package must be applied to the exact recorded base commit:

```bash
rpack apply change.rpack --strict-base
```

## Undo

If the applied patch should be reverted:

```bash
rpack undo
```

By default, undo refuses to continue when there are dirty paths outside the last applied package. To override:

```bash
rpack undo --allow-dirty
```

## Safety Rules For Agents

Agents should not:

- include unrelated local changes
- include generated build outputs
- include secrets, tokens, credentials, or local machine paths
- create commits unless explicitly requested
- push branches unless explicitly requested
- bypass `rpack check` failures without explaining why

Agents should prefer:

- small focused packages
- clear package names, for example `camera-refactor.rpack`
- `--staged` packages when the working tree contains unrelated files
- short summaries with test results
- patch paths relative to the Git root whenever possible

When an agent cannot build Git-root-relative paths because it only has a
subdirectory snapshot, it must clearly state the required `--path-prefix`.

## Recommended Package Names

Use descriptive lowercase filenames:

```txt
fix-login-validation.rpack
camera-refactor.rpack
update-readme-agent-docs.rpack
```

Avoid:

```txt
patch.rpack
final.rpack
stuff.rpack
```

## Troubleshooting

If `rpack create` says there are no changes:

```bash
git diff
git diff --cached
```

If `rpack check` fails because the working tree is dirty:

```bash
git status --short
```

Then either clean the tree or, only when intentional:

```bash
rpack check change.rpack --allow-dirty
```

If `rpack check` warns about base commit mismatch but still says the patch can be applied, the package is compatible by patch context.

If `rpack check` fails at `git apply --check`, the target repository is not compatible with the patch in its current state.

If default `rpack check` passes but strict mode fails, the repository content is
close enough for Git when whitespace-only context differences are ignored. This
usually means CRLF/LF or final-newline drift. Apply normally unless exact
context whitespace is required:

```bash
rpack apply change.rpack
```

If `rpack check` reports an added-file conflict, the target repository already
contains file(s) the package wants to add, but their contents differ from the
package version. rpack reports target/package byte counts and timestamps for
diagnosis, but it will not choose a winner by size or modification time. The
agent should regenerate the package against the current target repository or
create a new package with only the remaining changes.

If `rpack check` fails with "No such file or directory" and the package was
created from a snapshot root below the Git root, retry with `--path-prefix`.
For example:

```bash
rpack check change.rpack --path-prefix src
```

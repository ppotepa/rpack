# Roadmap

This roadmap is intentionally short. `rpack` should stay focused: validated patch packages for Git working trees, without touching Git history.

## 0.1.x - MVP Hardening

- Keep the `rpack-v1` package format stable enough for local use.
- Improve CLI errors and exit codes.
- Add tests for invalid archives and edge-case patches.
- Document common workflows for humans and coding agents.

## 0.2.x - Distribution

- Add release builds for Linux and macOS.
- Publish the .NET global tool to NuGet.
- Automate GitHub release artifacts.

## 0.3.x - Better Patch Analysis

- Parse patch metadata before applying.
- Show added, modified, deleted, renamed, and binary file counts.
- Detect risky patch contents earlier.
- Improve warnings for base commit mismatch.

## 0.4.x - Package Capabilities

- Support multiple patch entries in one package.
- Add optional package validation commands.
- Add package-level notes for manual application.
- Consider file overlay mode for explicit full-file replacement.

## 0.5.x - Trust and Sharing

- Add optional package signing.
- Add signature verification.
- Add provenance metadata.
- Add stricter policy profiles for automated agents.

## Non-Goals

- Do not replace Git.
- Do not create commits by default.
- Do not modify Git history.
- Do not implement merge/rebase/cherry-pick semantics.
- Do not silently apply partial patches.

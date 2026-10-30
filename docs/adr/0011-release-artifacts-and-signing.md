# 0011. Release as MSIX installer and portable zip

- Status: Accepted
- Date: 2026-10-30

## Context

Users want either a normal installation or something they can unzip and run. Windows trusts an MSIX only if its signing certificate is trusted. A self-signed development certificate is trusted by nobody else.

## Decision

Each GitHub Release carries two files, built by `scripts/package.ps1`:

| File | Notes |
|---|---|
| `analyxell-<version>-win-x64.msix` | Installer. Signed. Install and uninstall through Windows. |
| `analyxell-<version>-portable-win-x64.zip` | Unpackaged. No certificate, no installation. |

Signing policy: local builds use a generated development certificate (git-ignored, never published). Public releases must be signed with a certificate the maintainer controls, supplied through a protected CI secret or a local PFX path; the public certificate (`.cer`) is attached to the release so users can choose to trust it. Until a publicly trusted certificate exists, the portable zip is the recommended download.

Releases are cut only after the pre-release checklist in [docs/releasing.md](../releasing.md) passes.

## Consequences

- Two verified paths for two kinds of users; neither requires admin rights except trusting a certificate for the MSIX.
- We do not claim the MSIX "just works" for everyone until a trusted certificate is used.
- ARM64 builds are supported by the scripts but not yet part of the release matrix.

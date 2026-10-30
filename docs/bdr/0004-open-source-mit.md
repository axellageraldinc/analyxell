# 0004. Open source under MIT, private until the MVP is verified

- Status: Accepted
- Date: 2026-10-30

## Context

The maintainer wants the project to be public so others can use and contribute, but a public repository is permanent: history, stray secrets and half-working code are visible forever.

## Decision

License under MIT. Develop in a private repository with full documentation and contribution guidelines from day one. Switch to public only after (1) the MVP is verified end to end, (2) `scripts/check-secrets.ps1 -History` is clean, and (3) the release checklist in [docs/releasing.md](../releasing.md) passes. Releases are published only after that.

## Consequences

- A clean first public impression, with docs, tests and decision records already in place.
- Contributors can start immediately after the switch because CONTRIBUTING, templates and CI exist.
- Any secret or personal data found in history before publishing must be removed by rewriting history, which is why scanning starts at the first commit.

## Success looks like

The repository goes public with no sensitive data in any commit, a green CI, and a first release that installs and runs on a clean machine.

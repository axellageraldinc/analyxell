# 0007. Distribute as an installer and a portable zip

- Status: Accepted
- Date: 2026-10-30

## Context

Some players want a normal installation with clean uninstall; others want a tool they can unzip and delete. Gamers are also used to SmartScreen and certificate prompts from small tools.

## Decision

Publish both on GitHub Releases, from the same source, after the MVP is verified: an MSIX installer and a portable zip, both self-contained. Documentation explains the certificate step for the installer and recommends the portable zip until a trusted certificate is available ([ADR 0011](../adr/0011-release-artifacts-and-signing.md)).

## Consequences

- Two artifacts to test per release; the release checklist covers both.
- No store submission, auto-update service or accounts in the first versions.
- Users on locked-down PCs can still run the portable build.

## Success looks like

A user can go from the Releases page to a running app in under two minutes with either download.

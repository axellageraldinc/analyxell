# 0007. Secrets: DPAPI storage, never in the repo, scanned in CI

- Status: Accepted
- Date: 2026-10-30

## Context

The app may hold an AI API key. The repository is private now and will be public later, so nothing sensitive may ever enter its history, and a leaked key in history cannot be fully taken back.

## Decision

- **At rest:** the API key is encrypted with Windows DPAPI (`DataProtectionScope.CurrentUser`) into a separate file. It never appears in `settings.json`, reports, logs or error messages (tests assert this).
- **In the repo:** `.gitignore` blocks key and certificate files and local data. `scripts/check-secrets.ps1` scans every tracked and untracked file for private keys, common API key formats, bearer tokens, real user paths, personal e-mail addresses and private AI-session links. It runs as a pre-commit hook (`.githooks`) and in CI. `-History` also scans all past commits and is a required step before going public.
- **Commits:** the repository uses the GitHub `noreply` address as the commit identity, not a personal e-mail.
- **Test data:** tests and UI tests use synthetic captures. Real captures contain device and hardware details and are never committed.
- **Signing material:** `.pfx` files are never committed. CI signing uses repository secrets in a protected step.

## Consequences

- A contributor cannot easily commit a secret by accident; if they do, CI fails.
- Pattern scanning is a safety net, not proof. Reviewers still check diffs.
- DPAPI ties the stored key to one Windows account; moving to a new PC means re-entering it, which is the intended behaviour.

# Releasing

## Pre-release checklist

Run these on a clean checkout of the release commit.

1. **Tests:** `dotnet test tests\Analyxell.Core.Tests` is green.
2. **UI smoke:** `powershell -File tests\ui\smoke.ps1` passes all checks.
3. **Secrets and personal data:** `powershell -File scripts\check-secrets.ps1 -History` reports nothing. Fix history before going further if it does.
4. **Docs:** README, user guide and screenshots match the app. `CHANGELOG.md` has the release notes. Version numbers agree (`Version` in `Analyxell.csproj`, `Package.appxmanifest`, the file names).
5. **Build both artifacts:** `scripts\package.ps1 -Version X.Y.Z` (with `-CertPath` for a real signing certificate).
6. **Verify the artifacts on a clean Windows machine or Windows Sandbox:**
   - Portable: unzip, run `Analyxell.exe`, open a capture, compare two, change settings, restart, settings persisted.
   - Installer: trust the certificate, install the MSIX, run the same steps, then uninstall and confirm it is gone.
7. **Upload** both files and the public certificate (`.cer`, never the `.pfx`) to a draft GitHub Release. Add SHA-256 checksums to the notes.
8. Publish the release.

## Going public (one time)

1. Complete the checklist above for the first release candidate.
2. `check-secrets.ps1 -History` clean, and skim `git log --stat` for files that should never have been committed (captures, certificates, local data).
3. Review repository settings: branch protection on `main`, secret scanning and push protection enabled, Dependabot alerts on, Actions permissions minimal.
4. Confirm `LICENSE`, `SECURITY.md` (with a real reporting address or GitHub private advisories), `CONTRIBUTING.md` and issue templates are present.
5. Switch visibility: `gh repo edit axellageraldinc/analyxell --visibility public --accept-visibility-change-consequences`.
6. Publish the first release.

Switching to public is irreversible in practice: clones and caches may keep history. That is why step 2 matters.

## Signing

Public releases should be signed with a certificate you control:

```powershell
winapp cert generate --manifest src\Analyxell\Package.appxmanifest   # one time, keep the .pfx private
powershell -File scripts\package.ps1 -Version X.Y.Z -CertPath C:\secure\release.pfx -CertPassword <password>
```

Export the public part as `.cer` for users who want to trust it. A certificate chained to a public CA (or Microsoft Store distribution) removes the trust step entirely; consider it once there are users.

For CI signing, store the PFX as an encrypted repository secret, restore it to a temp file in a protected job, and never echo it.

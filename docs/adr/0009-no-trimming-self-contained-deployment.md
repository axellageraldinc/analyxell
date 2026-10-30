# 0009. No trimming; self-contained MSIX and portable builds

- Status: Accepted
- Date: 2026-10-30

## Context

The WinUI template enables `PublishTrimmed` for Release. A trimmed, unpackaged build of this app crashed at startup inside `Microsoft.UI.Xaml.dll` (stowed exception, 0xC000027B); the same build with trimming off ran correctly. We also want users to install nothing besides the app: not the .NET runtime and not the Windows App Runtime.

## Decision

- `PublishTrimmed` is `false` for all configurations. ReadyToRun stays on for Release to keep start-up fast.
- Both release artifacts are self-contained (.NET runtime and Windows App SDK bundled):
  - **Portable:** `dotnet publish -p:Portable=true` produces an unpackaged folder, zipped.
  - **Installer:** `winapp package --self-contained -p SelfContained=true` produces a signed MSIX.
- Our own code stays trim- and AOT-friendly (`JsonDocument` and source-generated JSON, no reflection), so trimming can be revisited when WinUI tooling supports it.

## Consequences

- Larger downloads (about 106 MB zipped, 108 MB MSIX) in exchange for working out of the box.
- Updates are whole-package; the MSIX gives clean install and uninstall.
- Revisit when Windows App SDK documents a supported trimming path: remove the setting, run the UI smoke test against a trimmed portable build, and supersede this ADR.

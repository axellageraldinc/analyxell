# 0001. WinUI 3 on .NET 10 for the desktop app

- Status: Accepted
- Date: 2026-10-30

## Context

The tool is for Windows PC gamers, reads files from Windows tools (CapFrameX) and game folders under `%LOCALAPPDATA%`, and should feel like a native Windows app. The author works with Claude Code and Microsoft's `win-dev-skills`, which target WinUI 3.

## Decision

Build the app with WinUI 3 (Windows App SDK) and C# on .NET 10, using MVVM with CommunityToolkit.Mvvm and `x:Bind`.

## Consequences

- Native look, Mica backdrop, Fluent controls, accessibility and theming for free.
- Windows 10 1809+ only. That matches the audience (the games and CapFrameX are Windows-only).
- Tooling (WinApp CLI, UI Automation) supports building, running, packaging and UI-testing from the command line, which our tests use.
- Trimming and Native AOT are not safe for the app today (see [ADR 0009](0009-no-trimming-self-contained-deployment.md)).

## Alternatives considered

- **Tauri / Electron:** easier charts and cross-platform, but a larger runtime or a second language, and less native feel, for a Windows-only tool.
- **WPF:** mature, but older controls and no first-class support in the tooling we use.

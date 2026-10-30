# Security policy

## Reporting a vulnerability

Please **do not open a public issue** for a security problem. Use GitHub's private vulnerability reporting: on the repository page choose **Security > Report a vulnerability**. Include what you found, how to reproduce it, and the impact you expect.

You can expect an acknowledgement within a few days and a fix or mitigation plan as soon as it is understood. Please allow time to release a fix before disclosing publicly.

## Scope

In scope: the analyxell application and its build and release scripts, for example:

- anything that lets the app or the AI change files, settings or processes it should not (the app is read-only by design, see [ADR 0006](docs/adr/0006-read-only-by-design.md));
- leaking the API key, device identifiers or file paths to an AI endpoint, to logs or to reports;
- unsafe handling of capture or config files (parsing crashes, path traversal);
- bypassing the CapFrameX MCP read-only allow-list.

Out of scope: vulnerabilities in CapFrameX, games, Windows or the AI provider you choose to connect, and issues that require an attacker who already controls your user account.

## Supported versions

Only the latest release receives security fixes while the project is pre-1.0.

## Handling secrets

The AI API key is stored encrypted with Windows DPAPI and is never written to settings or logs. If you ever suspect a key was exposed, revoke it at the provider and delete the saved key in **Settings**.

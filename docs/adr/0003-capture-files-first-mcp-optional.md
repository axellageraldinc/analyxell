# 0003. Read capture files directly; CapFrameX MCP is optional and read-only

- Status: Accepted
- Date: 2026-10-30

## Context

CapFrameX records captures and, in 1.9.1, ships a built-in MCP server (HTTP, localhost) whose tools can analyze records, but also start and stop captures and change CapFrameX settings. It only works while CapFrameX is running and `McpEnabled` is on.

## Decision

The source of truth is the capture `.json` file, parsed by analyxell itself. The CapFrameX MCP server is an optional convenience used only to *list* captures and test connectivity, through a code-level allow-list of read-only tools (`McpToolPolicy`). Tools that write, start captures or change settings are blocked before any request is made.

## Consequences

- Works without CapFrameX running and does not depend on an MCP feature that was off by default in the installer build.
- Our numbers come from our own code, which is tested and validated against CapFrameX's results; we do not trust a remote tool's summary.
- A new MCP tool can only be used after adding it to the allow-list in a reviewed change; tests assert that write-style tools stay blocked.
- analyxell detects whether CapFrameX is installed (usual folders, the Windows installed-apps list, or a path the user chooses for the portable version), offers a link to its official releases page when it is missing, and can start it on request. Nothing more: recording stays CapFrameX's job.
- Capture recording remains CapFrameX's job (see [BDR 0002](../bdr/0002-complement-capframex.md)).

## Alternatives considered

- **MCP only:** simpler code, but needs CapFrameX open, and exposes write tools.
- **Files only:** works, but "recent captures" would need a configured folder; MCP gives discovery for free when available.

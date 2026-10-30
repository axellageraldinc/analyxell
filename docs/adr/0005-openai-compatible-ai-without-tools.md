# 0005. AI through an OpenAI-compatible endpoint, with no tools

- Status: Accepted
- Date: 2026-10-30

## Context

Users have different AI providers; some prefer a local or self-hosted gateway for privacy or cost. The author uses [xellter](https://github.com/axellageraldinc/xellter), an OpenAI-compatible gateway that routes a named preset to a pool of models.

## Decision

Support one protocol: OpenAI-compatible `POST {endpoint}/v1/chat/completions` with `model` set to a user-chosen preset or model name and an optional bearer key. The request contains a system message and one user message. It contains no `tools`, `functions` or `tool_choice`. Default endpoint: `http://127.0.0.1:8480`, default preset `text`.

## Consequences

- Works with xellter, local servers (Ollama, LM Studio, llama.cpp server) and hosted providers without provider-specific code.
- The model cannot call anything, so it cannot act on the user's machine even if it is manipulated by text in the input. A UI test asserts the request has no tools.
- Error messages are friendly and never include the key.
- Streaming and tool-using agents are not supported, deliberately.

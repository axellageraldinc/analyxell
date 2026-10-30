# 0005. AI through an OpenAI-compatible endpoint, with no tools

- Status: Accepted
- Date: 2026-10-30

## Context

Users have different AI providers; some prefer a local or self-hosted gateway for privacy or cost. The author uses [xellter](https://github.com/axellageraldinc/xellter), an OpenAI-compatible gateway that routes a named preset to a pool of models. "Preset" is that gateway's word; to the protocol it is simply the model name.

## Decision

Support one protocol: OpenAI-compatible `POST {endpoint}/v1/chat/completions` with `model` set to the **model name** the user entered (sent unchanged) and an optional bearer key, and `GET {endpoint}/v1/models` so the app can list what an endpoint offers. The request contains a system message and one user message. It contains no `tools`, `functions` or `tool_choice`. There is no default endpoint or model: AI is off until the user configures it ([BDR 0009](../bdr/0009-ai-is-optional.md)). The model is told that this is a single question and answer and that it must never ask questions ([ADR 0014](0014-suggestions-and-single-answer-ai.md)).

## Consequences

- Works with xellter, local servers (Ollama, LM Studio, llama.cpp server) and hosted providers without provider-specific code.
- The model cannot call anything, so it cannot act on the user's machine even if it is manipulated by text in the input. A UI test asserts the request has no tools.
- Error messages are friendly and never include the key.
- Streaming and tool-using agents are not supported, deliberately.

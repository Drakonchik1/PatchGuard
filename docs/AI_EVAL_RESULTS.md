# AI Evaluation Results — provider comparison

Compare structural quality and latency across council backends on the same findings.

## How to measure

1. Set `OpenAI:ApiKey` empty, `Ollama:Enabled` true, `Ai:ChatProvider` to `Rules` then `Ollama` (or set `Ollama:Model` to your pulled tag — see [OLLAMA_SETUP.md](OLLAMA_SETUP.md)).
2. Run the same sample scan (e.g. After Windows Update with Update services warning).
3. Record wall-clock for AI guidance and the persisted `CouncilEvaluationRecord` (`Source`, `ActionabilityScore`, `ConsistencyScore`, `LatencyMs`).

Automated structural scores still come from `CouncilEvaluator` (not “truth”).

## Snapshot

| Provider | Source label | Latency (ms) | Actionability | Consistency | Notes |
|----------|--------------|--------------|---------------|-------------|-------|
| Rules | Local+KB | (fill) | (fill) | (fill) | Deterministic, offline |
| Ollama (`qwen3.5:latest` or your model) | Ollama+KB | (fill) | (fill) | (fill) | No cloud key; slower multi-agent loop |
| OpenAI | AI+KB | (fill) | (fill) | (fill) | Requires consent + API key |

When to pick local: no cloud key, privacy-sensitive PC, offline demo.
When to pick cloud: faster debate, stronger instruction following, optional web research.

## Related

- AI plan: [AI_ROADMAP.md](AI_ROADMAP.md)
- Setup / switch model: [OLLAMA_SETUP.md](OLLAMA_SETUP.md)
- Baseline fixtures: [AI_EVAL_BASELINE.md](AI_EVAL_BASELINE.md)
- Privacy tests: `AiPrivacyAndProvenanceTests`, `ChatProviderResolverTests`

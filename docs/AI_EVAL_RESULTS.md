# AI Evaluation Results — provider comparison

Compare structural quality and latency across council backends on the same findings.

> Measured Sprint 1 · scenario: **After Windows Update** · finding: **Windows Update service not running**  
> See [SPRINT_PLAN.md](SPRINT_PLAN.md) · Sprint 7 adds a controlled before/after experiment.

## How to measure

1. Set `OpenAI:ApiKey` empty, `Ollama:Enabled` true, `Ai:ChatProvider` to `Rules` then `Ollama`.
2. Run the same sample scan (After Windows Update with Update services warning).
3. Record wall-clock for AI guidance and the persisted `CouncilEvaluationRecord` (`Source`, `ActionabilityScore`, `ConsistencyScore`, `LatencyMs`).

Automated structural scores still come from `CouncilEvaluator` (not “truth”).

## Snapshot

**Environment:** Windows 11 · Ollama 0.32 · `llama3.2:3b` · `NumPredict=512` · no cloud key · no external consent

| Provider | Source label | Latency (ms) | Actionability | Consistency | Notes |
|----------|--------------|--------------|---------------|-------------|-------|
| Rules | Local+KB | 4769 | 100.0 | 83.3 | Deterministic, offline, ~5 s |
| Ollama (`llama3.2:3b`) | Ollama+KB | 40641 | 100.0 | 83.3 | No cloud key; ~13 LLM calls, ~41 s |
| OpenAI | AI+KB | — | — | — | Requires consent + API key (optional row) |

**Consistency 83.3%** on this scan: guide has KB references but no duplicate-step / provenance issues beyond the structural check set (see `CouncilEvaluator`).

When to pick local: no cloud key, privacy-sensitive PC, offline demo.  
When to pick cloud: faster debate on strong hardware, stronger instruction following, optional web research.

## Related

- Architecture: [AI_ARCHITECTURE.md](AI_ARCHITECTURE.md)
- AI plan: [AI_ROADMAP.md](AI_ROADMAP.md)
- Setup / switch model: [OLLAMA_SETUP.md](OLLAMA_SETUP.md)
- Baseline fixtures: [AI_EVAL_BASELINE.md](AI_EVAL_BASELINE.md)
- Privacy tests: `AiPrivacyAndProvenanceTests`, `ChatProviderResolverTests`

# AI Development Roadmap

Learning / portfolio track for PatchGuard’s AI stack. Product UX phases stay in
[UX_ROADMAP.md](UX_ROADMAP.md). This file tracks **AI competencies**.

**Last updated:** 2026-08-11

## Competence checklist

| # | Competence | Phase | Deliverable | Status |
|---|------------|-------|-------------|--------|
| 9 | Quality metrics | 0 | Golden dataset + eval harness (`CouncilEvaluator`) | **Done** |
| 4 + 2 + 7 | RAG / Generative AI / n8n | 1 | Local KB + retrieval + provenance; n8n reindex → JSON (external) | **Done** (RAG); n8n workflow still **planned** |
| 5 + 2 | Local LLM / Generative AI | 2 | Ollama provider, council without cloud key | **Done** |
| 3 + 6 | Agentic AI / LangGraph analog | 3 | Semantic Kernel conditional graph + ≥2 read-only tools | Planned |
| 1 | Classic ML | 4 | Microsoft.ML anomaly model + metrics in tests | Planned |
| 8 | Azure / AWS | 5 | Azure OpenAI adapter + cloud setup doc | Planned |
| 9 | CI regression | 6 | Golden eval gate in CI | Planned (golden tests local today) |

## Phase 0 — Metrics (done)

- Aggregate `CouncilEvaluationRecord` (no raw prompts / PII)
- Structural actionability + consistency scores
- Golden fixtures under `PatchGuard.Tests/Fixtures/GoldenScenarios`
- Docs: [AI_EVAL_BASELINE.md](AI_EVAL_BASELINE.md)

## Phase 1 — RAG (done)

- Playbooks in `PatchGuard/KnowledgeBase/Playbooks`
- Chunking + offline hashing embeddings + ranked retrieval
- Guide labels: **Local knowledge base** + inspectable references
- Privacy: KB never uploaded for indexing

### Still planned from Phase 1 scope

- **n8n workflow:** reindex KB → export JSON artifact for CI / packaging

## Phase 2 — Local LLM / Ollama (done)

- `IChatCompletionProvider` + `OllamaChatProvider` + `OpenAiChatClient`
- `ChatProviderResolver`: `Auto` | `OpenAI` | `Ollama` | `Rules`
- Council without OpenAI key; Ollama does **not** need external consent
- Eval labels: `Ollama` / `Ollama+KB`
- UI: **Local LLM (Ollama)**
- Comparison stub: [AI_EVAL_RESULTS.md](AI_EVAL_RESULTS.md)

### Still planned from Phase 2 scope

- Settings UI radio (Cloud / Ollama / Rules) — today config-only via `appsettings`

## Phase 3 — Agentic (planned)

- Semantic Kernel (or equivalent) conditional graph
- ≥2 **read-only** tools (e.g. re-query KB, inspect safe local status)
- Keep preview/confirm for any future write actions (product Phase 3 guided fixes)

## Phase 4 — Classic ML (planned)

- Microsoft.ML anomaly / health signal model
- Metrics asserted in automated tests

## Phase 5 — Cloud adapter (planned)

- Azure OpenAI chat adapter behind the same `IChatCompletionProvider`
- Short cloud setup doc (endpoint, deployment name, key storage)

## Phase 6 — CI regression (planned)

- Run golden + privacy + provider resolver tests in GitHub Actions
- Fail PR if actionability/consistency averages drop below baseline

## How to choose / change the model

See [OLLAMA_SETUP.md](OLLAMA_SETUP.md) for install, pull, and switching models.

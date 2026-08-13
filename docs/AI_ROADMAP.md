# AI Development Roadmap

Learning / portfolio track for PatchGuard’s AI stack. Product UX phases: [UX_ROADMAP.md](UX_ROADMAP.md).  
**Execution:** [SPRINT_PLAN.md](SPRINT_PLAN.md) — one chat per sprint.

**Last updated:** 2026-08-13

## Competence checklist

| # | Competence | AI Phase | Sprint | Deliverable | Status |
|---|------------|----------|--------|-------------|--------|
| 9 | Quality metrics | 0 | 1, 5, 7 | Golden dataset + `CouncilEvaluator` + CI gate | **Core done** · CI Sprint 1 |
| 4 | RAG | 1 | 5 | Local KB + retrieval + provenance | **Core done** · hybrid + 15 docs Sprint 5 |
| 2 | Generative AI | 1–2 | — | RAG-augmented council + structured verdict | **Done** |
| 5 | Local LLM | 2 | 5 | Ollama without cloud key | **Core done** · Settings radio Sprint 5 |
| 3 | Agentic AI | 3 | 5 | Conditional graph + ≥2 read-only tools | **Core done** · trace + verify Sprint 5 |
| 6 | LangGraph analog | 3 | 1, 5 | Semantic Kernel graph | **Core done** · diagram Sprint 1 |
| 7 | n8n | 1 ext. | 7 | KB reindex → JSON export | **Planned** (optional) |
| 1 | Classic ML | 4 | 4 | Microsoft.ML anomaly + test metrics | **Done** |
| 8 | Azure / AWS | 5 | 6 | Azure adapter + cloud doc + Bedrock stub | **Planned** |
| 9 | CI regression | 6 | 1, 5, 7 | Golden eval gate in GitHub Actions | **Planned** |

## Phase 0 — Metrics (core done)

- Aggregate `CouncilEvaluationRecord` (no raw prompts / PII)
- Structural actionability + consistency scores
- **5** golden fixtures under `PatchGuard.Tests/Fixtures/GoldenScenarios` (target **10** Sprint 1, **15–20** Sprint 5)
- Verified averages: actionability **90.0%**, consistency **93.3%**
- Docs: [AI_EVAL_BASELINE.md](AI_EVAL_BASELINE.md)

**Remaining (Sprint 1, 5, 7):** CI workflow, expand golden set, threshold gate, controlled experiment.

## Phase 1 — RAG (core done)

- **6** playbooks in `PatchGuard/KnowledgeBase/Playbooks` (target **15+** Sprint 5)
- Chunking + offline hashing embeddings + ranked retrieval
- Guide labels: **Local knowledge base** + inspectable references
- Privacy: KB never uploaded for indexing

**Remaining (Sprint 5, 7):** hybrid keyword + embedding retrieval; n8n reindex workflow (optional Sprint 7).

## Phase 2 — Local LLM / Ollama (core done)

- `IChatCompletionProvider` + `OllamaChatProvider` + `OpenAiChatClient`
- `ChatProviderResolver`: `Auto` | `OpenAI` | `Ollama` | `Rules`
- Council without OpenAI key; Ollama does **not** need external consent
- Eval labels: `Ollama` / `Ollama+KB`; UI: **Local LLM (Ollama)**

**Remaining (Sprint 1, 5):** fill [AI_EVAL_RESULTS.md](AI_EVAL_RESULTS.md); Settings UI provider radio.

## Phase 3 — Agentic (core done)

- `CouncilAgentGraph`: Analyze → (conditional) ToolResearch → Debate → Rebuttal → ExplainVerdict
- Light path when no Warning/Critical findings
- Read-only SK tools: `query_knowledge_base`, `get_local_status`
- `DetailedExplanation`, per-step `WhyThisMatters` / `Evidence`
- No training mode; no write tools

**Remaining (Sprint 5):** collapsible agent trace in Guide UI; VerifySteps + 1 retry.

## Phase 4 — Classic ML (Sprint 4) ✅ Done

**Scope:** inference-only — train model **offline**, ship bundled artifact; **no** user-facing “train model” UI.

- Z-score baseline → Isolation Forest (bundled JSON) + Microsoft.ML RandomizedPCA (`.zip`) on sensor history
- `AnomalyDiagnosticModule`: finding with confidence % + human explanation
- Metrics (precision/recall/F1) in automated tests (floors ≥ 0.80; see [ML_REPORT.md](ML_REPORT.md))
- `docs/ML_REPORT.md`

**Depends on:** Sprint 2 sensor history (`ISensorHistoryService`).

## Phase 5 — Cloud adapter (Sprint 6)

- `AzureOpenAiChatProvider` behind `IChatCompletionProvider`
- DPAPI secret storage (not plain JSON)
- `docs/CLOUD_ARCHITECTURE.md` + Bedrock stub (honest scope)

## Phase 6 — Quality loop (Sprint 1, 5, 7)

- GitHub Actions: build + golden/privacy/provider tests (Sprint 1)
- PR fail if metrics drop >5% vs baseline (Sprint 5)
- One controlled experiment + demo script + CV bullets (Sprint 7)

## How to choose / change the model

See [OLLAMA_SETUP.md](OLLAMA_SETUP.md).

## Related docs

| Doc | Purpose |
|-----|---------|
| [SPRINT_PLAN.md](SPRINT_PLAN.md) | Sprint tasks + CHAT PROMPTs |
| [AI_EVAL_BASELINE.md](AI_EVAL_BASELINE.md) | Metrics + golden baseline |
| [AI_EVAL_RESULTS.md](AI_EVAL_RESULTS.md) | Provider comparison worksheet |
| [ML_REPORT.md](ML_REPORT.md) | Classic ML anomaly metrics + limitations |
| [UX_ROADMAP.md](UX_ROADMAP.md) | Product phases (parallel track) |

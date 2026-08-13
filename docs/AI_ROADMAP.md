# AI Development Roadmap

Learning / portfolio track for PatchGuard’s AI stack. Product UX phases: [UX_ROADMAP.md](UX_ROADMAP.md).  
**Execution:** [SPRINT_PLAN.md](SPRINT_PLAN.md) — one chat per sprint.

**Last updated:** 2026-08-13

## Competence checklist

| # | Competence | AI Phase | Sprint | Deliverable | Status |
|---|------------|----------|--------|-------------|--------|
| 9 | Quality metrics | 0 | 1, 5, 7 | Golden dataset + `CouncilEvaluator` + CI gate | **Done** · threshold Sprint 5 |
| 4 | RAG | 1 | 5 | Local KB + retrieval + provenance | **Done** · hybrid + 16 docs |
| 2 | Generative AI | 1–2 | — | RAG-augmented council + structured verdict | **Done** |
| 5 | Local LLM | 2 | 5 | Ollama without cloud key | **Done** · Settings radio |
| 3 | Agentic AI | 3 | 5 | Conditional graph + ≥2 read-only tools | **Done** · trace + verify |
| 6 | LangGraph analog | 3 | 1, 5 | Semantic Kernel graph | **Done** |
| 7 | n8n | 1 ext. | 7 | KB reindex → JSON export | **Planned** (optional) |
| 1 | Classic ML | 4 | 4 | Microsoft.ML anomaly + test metrics | **Done** |
| 8 | Azure / AWS | 5 | 6 | Azure adapter + cloud doc + Bedrock stub | **Planned** |
| 9 | CI regression | 6 | 1, 5, 7 | Golden eval gate in GitHub Actions | **Done** · >5% gate Sprint 5 |

## Phase 0 — Metrics (core done)

- Aggregate `CouncilEvaluationRecord` (no raw prompts / PII)
- Structural actionability + consistency scores
- **15** golden fixtures under `PatchGuard.Tests/Fixtures/GoldenScenarios`
- Verified averages: actionability **94.4%**, consistency **96.7%**
- Docs: [AI_EVAL_BASELINE.md](AI_EVAL_BASELINE.md)

**Remaining (Sprint 7):** controlled experiment + demo script.

## Phase 1 — RAG ✅ Done (Sprint 5 polish)

- **16** playbooks in `PatchGuard/KnowledgeBase/Playbooks`
- Chunking + offline hashing embeddings + **hybrid** keyword+embedding retrieval
- Guide labels: **Local knowledge base** + inspectable references
- Privacy: KB never uploaded for indexing

**Remaining (Sprint 7):** n8n reindex workflow (optional).

## Phase 2 — Local LLM / Ollama ✅ Done (Sprint 5 polish)

- `IChatCompletionProvider` + `OllamaChatProvider` + `OpenAiChatClient`
- `ChatProviderResolver`: `Auto` | `OpenAI` | `Ollama` | `Rules`
- Settings UI radio: Cloud / Ollama / Rules (persisted)
- Council without OpenAI key; Ollama does **not** need external consent
- Eval labels: `Ollama` / `Ollama+KB`; UI: **Local LLM (Ollama)**

## Phase 3 — Agentic ✅ Done (Sprint 5 polish)

- `CouncilAgentGraph`: Analyze → (conditional) ToolResearch → Debate → Rebuttal → ExplainVerdict → VerifySteps
- Light path when no Warning/Critical findings
- Read-only SK tools: `query_knowledge_base`, `get_local_status`
- `DetailedExplanation`, per-step `WhyThisMatters` / `Evidence`
- Collapsible `CouncilTrace` in Guide; unsafe steps rejected with max 1 retry
- No training mode; no write tools

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

- GitHub Actions: build + golden/privacy/provider tests (Sprint 1) ✅
- PR fail if metrics drop >5% vs baseline (Sprint 5) ✅
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

# PatchGuard — handoff

**Branch:** `main` (feature work may land on topic branches first) · **Remote:** `origin/main`  
**Build plan:** [docs/SPRINT_PLAN.md](docs/SPRINT_PLAN.md) — **one Cursor chat = one sprint**

**Last updated:** 2026-08-13

## What shipped

### Product (UX)
- Phase 1 UX shell: sidebar, design system, dashboard, reusable controls.
- Phase 2 diagnostic journey: step indicator, unified scoring, actionable findings, optional AI with consent + provenance.

### AI competencies
- Phase 0: golden eval harness (5 fixtures), `CouncilEvaluator`, `CouncilEvaluationRecord`, baseline docs.
- Phase 1 RAG: local playbook KB (6 playbooks), retrieval, KnowledgeBase provenance (offline embeddings).
- Phase 2 Local LLM: Ollama via `IChatCompletionProvider`, Auto/OpenAI/Ollama/Rules, council without cloud key.
- Phase 3 Agentic: `CouncilAgentGraph` (conditional path) + SK read-only tools + `DetailedExplanation`.

### Docs & security
- `docs/SPRINT_PLAN.md`, `docs/AI_ROADMAP.md`, `docs/UX_ROADMAP.md`, `docs/OLLAMA_SETUP.md`
- Security: launch URI policy, EF factory, sanitizer allowlist, navigation fixes.

## Run

```powershell
ollama pull qwen3.5:latest   # optional local LLM
dotnet run --project PatchGuard/PatchGuard.csproj
dotnet test PatchGuard.Tests/PatchGuard.Tests.csproj
```

Leave `OpenAI:ApiKey` empty → AI guidance without consent uses Ollama + KB when enabled.  
Model switch: `docs/OLLAMA_SETUP.md`.

## Sprint progress

| Sprint | Focus | Status |
|--------|-------|--------|
| 1 | CI + golden×10 + `AI_ARCHITECTURE.md` | ⬜ |
| 2 | Sensor history + alert engine | ⬜ |
| 3 | Guided fixes + alerts UI | ⬜ |
| 4 | Classic ML (inference-only) | ⬜ |
| 5 | AI polish (settings, trace, hybrid RAG) | ⬜ |
| 6 | Azure + DPAPI secrets | ⬜ |
| 7 | Quality loop + portfolio | ⬜ |
| 8 | UX optimization + settings full | ⬜ |

Mark ✅ in `docs/SPRINT_PLAN.md` when a sprint closes.

## Next sprint

**Sprint 1** — copy CHAT PROMPT from [docs/SPRINT_PLAN.md](docs/SPRINT_PLAN.md) into a new chat.

## Remaining gaps (by sprint)

| Gap | Sprint |
|-----|--------|
| GitHub Actions CI | 1 |
| Golden fixtures 5 → 10–20 | 1, 5 |
| `AI_EVAL_RESULTS.md` filled | 1 |
| `AI_ARCHITECTURE.md` | 1 |
| Sensor history + alerts | 2–3 |
| Guided fix pipeline | 3 |
| Microsoft.ML anomaly (no training UI) | 4 |
| Settings provider radio, agent trace, hybrid RAG | 5 |
| Azure OpenAI + secrets | 6 |
| CI threshold gate, demo script, experiment | 7 |
| Gaming Mode, settings full, history compare | 8 |
| n8n KB reindex export | 7 (optional) |

## Key files

| Area | Path |
|------|------|
| Sprint plan | `docs/SPRINT_PLAN.md` |
| Chat providers | `PatchGuard/Services/Ai/IChatCompletionProvider.cs`, `OllamaChatProvider.cs`, `OpenAiChatClient.cs` |
| Agentic graph | `PatchGuard/Services/Ai/CouncilAgentGraph.cs`, `SemanticKernelToolHost.cs` |
| RAG | `PatchGuard/Services/Ai/KnowledgeRetrievalService.cs` |
| AI / UX roadmaps | `docs/AI_ROADMAP.md`, `docs/UX_ROADMAP.md` |

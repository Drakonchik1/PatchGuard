# AI Architecture — PatchGuard council stack

How optional AI guidance works: local RAG, provider routing, multi-agent graph, and evaluation.

**Related:** [AI_ROADMAP.md](AI_ROADMAP.md) · [AI_EVAL_BASELINE.md](AI_EVAL_BASELINE.md) · [OLLAMA_SETUP.md](OLLAMA_SETUP.md) · [SPRINT_PLAN.md](SPRINT_PLAN.md)

**Last updated:** 2026-08-13 (Sprint 1)

---

## End-to-end flow

User completes a diagnostic scan → **Findings** → optional **AI guidance** → **RepairGuide** (steps, council transcript, provenance).

```mermaid
flowchart TD
    subgraph UI["Guide UI"]
        GV[GuideViewModel]
    end

    subgraph Core["AiCouncilService"]
        KB[KnowledgeRetrievalService]
        RES[ChatProviderResolver]
        RULES[LocalCouncilSession]
        GRAPH[CouncilAgentGraph]
        EVAL[CouncilEvaluationService]
    end

    subgraph Providers["IChatCompletionProvider"]
        OLL[OllamaChatProvider]
        OAI[OpenAiChatClient]
    end

    subgraph External["External — consent required"]
        TAV[TavilyWebSearchService]
    end

    GV -->|findings + consent flag| Core
    Core --> KB
    KB -->|KnowledgeHit[]| Core
    Core --> RES
    RES -->|null Rules| RULES
    RES -->|Ollama / OpenAI| GRAPH
    GRAPH --> OLL
    GRAPH --> OAI
    Core -->|allowExternalServices| TAV
    TAV -->|WebSearchResult[]| Core
    RULES --> RG[RepairGuide]
    GRAPH --> RG
    Core --> EVAL
    EVAL -->|CouncilEvaluationRecord| DB[(SQLite)]
    RG --> GV
```

**Privacy defaults**

- KB retrieval and Rules/Ollama run **without** external consent.
- OpenAI + Tavily require consent checkbox; payloads pass through `ExternalDiagnosticSanitizer`.

---

## Provider routing

`ChatProviderResolver` picks the chat backend from `appsettings` (`Ai:ChatProvider`).

| Mode | Resolves to | Leaves machine? | Consent |
|------|-------------|-----------------|---------|
| `Rules` | `null` → `LocalCouncilSession` | No | No |
| `Ollama` | `OllamaChatProvider` | No (localhost) | No |
| `OpenAI` | `OpenAiChatClient` | Yes | Yes |
| `Auto` | OpenAI if key + consent, else Ollama if enabled, else Rules | Depends | For cloud |

```mermaid
flowchart LR
    AUTO[Auto] --> C{consent + API key?}
    C -->|yes| OAI[OpenAI]
    C -->|no| O{Ollama enabled?}
    O -->|yes| OLL[Ollama]
    O -->|no| RULES[Rules council]
    RULES --> LC[LocalCouncilSession]
    OLL --> GRAPH[CouncilAgentGraph]
    OAI --> GRAPH
```

Default local model: **`llama3.2:3b`** (~2 GB). See [OLLAMA_SETUP.md](OLLAMA_SETUP.md).

On LLM HTTP failure, `AiCouncilService` falls back to `LocalCouncilSession` (deterministic rules + KB).

---

## RAG — local knowledge base

Playbooks live in `PatchGuard/KnowledgeBase/Playbooks/*.md`.

| Component | Role |
|-----------|------|
| `KnowledgeChunker` | Split markdown by headings |
| `HashingEmbeddingService` | Offline vector-like ranking (no cloud upload) |
| `KnowledgeRetrievalService` | Index + retrieve top-K chunks per finding |
| `KnowledgeReference` | Provenance in `RepairGuide` (playbook id, score) |

KB runs **before** council selection. Source label includes `+KB` when `GuidanceSource.KnowledgeBase` is present.

---

## Council backends

### Rules — `LocalCouncilSession`

Deterministic multi-phase simulation (Analysis → Research → Debate → Verdict) with fixed delays and template messages. No LLM. Always available offline.

### LLM — `CouncilAgentGraph`

Used when `ChatProviderResolver` returns Ollama or OpenAI. Conditional graph (LangGraph-style):

```mermaid
stateDiagram-v2
    [*] --> Analyze
    Analyze --> LightPath: no Warning/Critical
    Analyze --> ToolResearch: Warning/Critical
    ToolResearch --> Research
    Research --> Debate
    Debate --> Rebuttal
    Rebuttal --> ExplainVerdict
    LightPath --> ExplainVerdict
    ExplainVerdict --> [*]
```

**Light path:** no Warning/Critical findings → skip tool research and debate rounds.

**Heavy path (~13 LLM calls):** three debaters × (Analysis, Research, Debate, Rebuttal) + Chief JSON verdict.

Read-only Semantic Kernel tools (heavy path only):

- `query_knowledge_base`
- `get_local_status`

No write/admin tools — privileged fixes are planned for Sprint 3 guided-fix pipeline.

---

## Evaluation layer

| Piece | Purpose |
|-------|---------|
| `CouncilEvaluator` | Structural `ActionabilityScore` + `ConsistencyScore` on `RepairGuide` |
| `CouncilEvaluationService` | Persist aggregate row to SQLite (no PII, no raw prompts) |
| Golden fixtures | 10 JSON scenarios in `PatchGuard.Tests/Fixtures/GoldenScenarios/` |
| `GoldenScenarioTests` | Regression gate on expected scores |

Eval `Source` labels: `Local`, `Local+KB`, `Ollama`, `Ollama+KB`, `AI`, `AI+KB`, `AI+Web`, etc.

---

## Key files

| Area | Path |
|------|------|
| Orchestrator | `PatchGuard/Services/Ai/AiCouncilService.cs` |
| Provider resolver | `PatchGuard/Services/Ai/ChatProviderResolver.cs` |
| Ollama | `PatchGuard/Services/Ai/OllamaChatProvider.cs` |
| OpenAI | `PatchGuard/Services/Ai/OpenAiChatClient.cs` |
| Rules council | `PatchGuard/Services/Ai/LocalCouncilSession.cs` |
| Agent graph | `PatchGuard/Services/Ai/CouncilAgentGraph.cs` |
| RAG | `PatchGuard/Services/Ai/KnowledgeRetrievalService.cs` |
| Evaluator | `PatchGuard/Services/Ai/CouncilEvaluator.cs` |
| Agent prompts | `PatchGuard/Services/Ai/CouncilAgents.cs` |
| Sanitizer | `PatchGuard/Services/Ai/ExternalDiagnosticSanitizer.cs` |
| DI wiring | `PatchGuard/DependencyInjection.cs` |

---

## CI (Sprint 1)

GitHub Actions: `.github/workflows/ci.yml` — `dotnet build` + full test suite on push/PR to `main`.

Ollama is **not** required in CI; provider tests use HTTP stubs.

# AI Evaluation Baseline

## Purpose

PatchGuard now records one aggregate evaluation row for each completed AI council session.
The goal is to measure whether prompt, agent, or model changes improve guidance quality
without storing raw scan text, prompts, or other private content.

## What is stored

Each `CouncilEvaluationRecord` stores:

- `EvaluatedAt`
- `Scenario`
- `Source` (`Local`, `Local+KB`, `AI`, `AI+KB`, `AI+Web`, `Ollama`, `Ollama+KB`, `Web`, …)
- `LatencyMs`
- `FixStepCount`
- `CouncilMessageCount`
- `ActionabilityScore`
- `ConsistencyScore`

This record intentionally excludes finding details, hostnames, file paths, and full
guide text. It is safe for local trend analysis and Settings UI history.

## How scoring works

`CouncilEvaluator` computes two lightweight structural metrics:

- `ActionabilityScore`: how complete the fix steps are
  - non-empty title
  - sufficiently detailed instructions
  - supporting artifact such as `CopyText` or `LinkUrl`
- `ConsistencyScore`: how internally coherent the guide is
  - summary present
  - chief verdict present
  - `Local` source retained
  - council discussion present
  - step titles are distinct
  - provenance is coherent and all links are safe

These are not “truth” metrics. They are fast baseline metrics that help catch regressions
in output shape before deeper qualitative review.

## RAG groundedness (Phase 1)

Local playbooks live in `PatchGuard/KnowledgeBase/Playbooks`. Retrieval ranks chunks with
local hashing embeddings only (never uploads playbook text to OpenAI during indexing —
privacy + stable vector dimensions) and attaches `GuidanceSource.KnowledgeBase` plus
inspectable `KnowledgeReferences`. `OpenAiEmbeddingService` remains available for future
opt-in cloud features but is not used by the KB index.

KB retrieval runs even without external AI consent — it never leaves the machine.

Manual check after a local council on “After Windows Update” / Update services warning:

- Source labels include **Local knowledge base**
- Inspectable references list playbook ids (e.g. `windows-update-services`)
- Researcher messages mention `KB/` excerpts

Automated coverage: `KnowledgeRetrievalTests` (chunking, ranking, council provenance).

## Local LLM (Phase 2)

Same council loop (analysis → research → debate → verdict), different chat backend:

| Backend | Consent | Leaves machine? |
|---------|---------|-----------------|
| Rules (`LocalCouncilSession`) | No | No |
| Ollama (`OllamaChatProvider`) | No | No (localhost) |
| OpenAI (`OpenAiChatClient`) | Yes | Yes |
| Tavily web | Yes | Yes |

Config (`appsettings.json`):

- `Ai:ChatProvider` = `Auto` | `OpenAI` | `Ollama` | `Rules`
- `Ollama:Enabled` / `BaseUrl` / `Model` (default `llama3.2:3b`, ~2 GB)

`Auto` without consent prefers Ollama when enabled; with consent prefers OpenAI when an API key is set. HTTP failures fall back to rules. Eval `Source` uses `Ollama` / `Ollama+KB` (vs cloud `AI` / `AI+KB`). Guide UI shows **Local LLM (Ollama)** for local runs.

Sanitizer still strips PII from prompts sent to any LLM, including Ollama.

**Change model:** set `Ollama:Model` to any tag from `ollama list` (see [OLLAMA_SETUP.md](OLLAMA_SETUP.md)). Plans: [AI_ROADMAP.md](AI_ROADMAP.md) · [SPRINT_PLAN.md](SPRINT_PLAN.md).

## Build plan

Sprint execution and golden expansion targets: [SPRINT_PLAN.md](SPRINT_PLAN.md) (Sprint 1 → 10 fixtures, Sprint 5 → 15–20).

## Golden baseline

The baseline is defined by **15** curated golden fixtures in
`PatchGuard.Tests/Fixtures/GoldenScenarios`.

Current verified averages:

- Average actionability: `94.4%`
- Average consistency: `96.7%`

CI fails if live averages drop **more than 5%** below these baselines
(`GoldenAveragesMustNotDropMoreThanFivePercentVsBaseline`).

Per-fixture expected scores (original 10 + Sprint 5):

- `after-update-service-recovery`: `100.0 / 100.0`
- `ai-augmented-health-check`: `100.0 / 100.0`
- `critical-disk-pressure-response`: `100.0 / 100.0`
- `driver-remediation-with-web`: `100.0 / 100.0`
- `kb-grounded-update-playbook`: `100.0 / 100.0`
- `kb-provenance-drift`: `100.0 / 83.3`
- `duplicate-step-regression`: `100.0 / 83.3`
- `web-only-provenance-drift`: `83.3 / 83.3`
- `manual-verification-only`: `66.7 / 100.0`
- `short-instruction-regression`: `66.7 / 100.0`
- `memory-pressure-working-set`: `100.0 / 100.0`
- `startup-bloat-cleanup`: `100.0 / 100.0`
- `pending-reboot-finish`: `100.0 / 100.0`
- `network-dns-flush`: `100.0 / 100.0`
- `gpu-thermal-game-session`: `100.0 / 100.0`

Format: `ActionabilityScore / ConsistencyScore`

## Validation

Validated with:

```powershell
dotnet test PatchGuard.Tests/PatchGuard.Tests.csproj --filter "FullyQualifiedName~AiPrivacyAndProvenanceTests|FullyQualifiedName~CouncilEvaluatorTests|FullyQualifiedName~CouncilEvaluationServiceTests|FullyQualifiedName~GoldenScenarioTests|FullyQualifiedName~ChatProvider|FullyQualifiedName~Ollama|FullyQualifiedName~Knowledge"
```

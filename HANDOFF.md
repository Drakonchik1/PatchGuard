# PatchGuard — handoff

**Branch:** `main` (feature work may land on topic branches first) · **Remote:** `origin/main`  
**Build plan:** [docs/SPRINT_PLAN.md](docs/SPRINT_PLAN.md) — **one Cursor chat = one sprint**

**Last updated:** 2026-08-13

## What shipped

### Product (UX)
- Phase 1 UX shell: sidebar, design system, dashboard, reusable controls.
- Phase 2 diagnostic journey: step indicator, unified scoring, actionable findings, optional AI with consent + provenance.
- Sprint 2: sensor history (SQLite) + threshold alert engine; dashboard alert summary.
- Sprint 3: real Alerts UI, Monitor inline alert banner, guided-fix pipeline (preview → confirm → execute → verify → record).
- Sprint 4: Monitor ML anomaly banner; `AnomalyDiagnosticModule` findings with confidence %.
- Sprint 5: Settings provider radio (Cloud/Ollama/Rules); Guide collapsible agent trace.

### AI competencies
- Phase 0: golden eval harness (**15** fixtures), `CouncilEvaluator`, `CouncilEvaluationRecord`, baseline docs + CI >5% drop gate.
- Phase 1 RAG: local playbook KB (**16** playbooks), hybrid keyword+embedding retrieval, KnowledgeBase provenance (offline embeddings).
- Phase 2 Local LLM: Ollama via `IChatCompletionProvider`, Settings radio Cloud/Ollama/Rules, default **`llama3.2:3b`**.
- Phase 3 Agentic: `CouncilAgentGraph` (conditional path) + SK read-only tools + `DetailedExplanation` + `VerifySteps` (1 retry) + `CouncilTrace`.
- Phase 4 Classic ML: Z-score + Isolation Forest (bundled) + Microsoft.ML RandomizedPCA; inference-only.

### Sprint 1 (done)
- GitHub Actions CI (`.github/workflows/ci.yml`)
- Golden fixtures 5 → **10**; averages 91.7% / 95.0%
- `docs/AI_ARCHITECTURE.md`, filled `docs/AI_EVAL_RESULTS.md` (Rules + Ollama sample scan)

### Sprint 2 (done)
- `SensorSnapshotRecord` + `ISensorHistoryService` (7-day rolling retention)
- Live Monitor persists snapshots every ~10s while open
- `IAlertRuleEngine` default CPU/GPU temp + load thresholds
- Home dashboard alert summary (count + highest severity)
- AMD GPU mapping fix: prefer discrete AMD/NVIDIA over Intel iGPU; ADL Core/Hot Spot; D3D load fallback; warm-up Update

### Sprint 3 (done)
- `AlertsView` / `AlertsViewModel` (active + recently resolved)
- Monitor threshold alert banner
- `IGuidedFixPlanService` safety gate; `GuidedFixRuns` SQLite history
- “Run safe fix” from Findings / Guide (optimizer steps + `LaunchUriPolicy` only; no Explorer restart)

### Sprint 4 (done)
- `ZScoreAnomalyDetector` + `IsolationForestModel` + `MlNetAnomalyDetector` (fallback chain)
- Bundled artifacts: `Models/Ml/isolation-forest-v1.json`, `sensor-anomaly-rpca.zip`
- `AnomalyDiagnosticModule`; Monitor ML anomaly banner
- `AnomalyDetectorTests` precision/recall/F1 floors ≥ 0.80
- `docs/ML_REPORT.md` — **no training UI**

### Sprint 5 (done)
- Settings: Cloud / Ollama / Rules radio → `%LocalAppData%/PatchGuard/user-settings.json`
- `CouncilTrace` collapsible expander in Guide (nodes, tools, timing)
- `FixStepVerifier` + 1 retry in `CouncilAgentGraph`
- Hybrid RAG (`0.65` embedding + `0.35` keyword); **16** playbooks
- Golden fixtures **15**; baseline averages **94.4% / 96.7%**; CI threshold step
- `docs/AI_ARCHITECTURE.md` updated

### Docs & security
- `docs/SPRINT_PLAN.md`, `docs/AI_ROADMAP.md`, `docs/UX_ROADMAP.md`, `docs/OLLAMA_SETUP.md`, `docs/ML_REPORT.md`
- Security: launch URI policy, EF factory, sanitizer allowlist, navigation fixes; agent tools remain read-only.

## Run

```powershell
ollama pull llama3.2:3b   # optional local LLM (~2 GB, default)
dotnet run --project PatchGuard/PatchGuard.csproj
dotnet test PatchGuard.Tests/PatchGuard.Tests.csproj
```

Leave `OpenAI:ApiKey` empty → AI guidance without consent uses Ollama + KB when enabled.  
Provider switch: Settings UI (or `docs/OLLAMA_SETUP.md`).  
ML regen (offline only): `$env:PATCHGUARD_REGEN_ML='1'` + filter `RegenBundledModels`.

## Sprint progress

| Sprint | Focus | Status |
|--------|-------|--------|
| 1 | CI + golden×10 + `AI_ARCHITECTURE.md` | ✅ |
| 2 | Sensor history + alert engine | ✅ |
| 3 | Guided fixes + alerts UI | ✅ |
| 4 | Classic ML (inference-only) | ✅ |
| 5 | AI polish (settings, trace, hybrid RAG) | ✅ |
| 6 | Azure + DPAPI secrets | ⬜ |
| 7 | Quality loop + portfolio | ⬜ |
| 8 | UX optimization + settings full | ⬜ |

Mark ✅ in `docs/SPRINT_PLAN.md` when a sprint closes.

## Next sprint

**Sprint 6** — copy CHAT PROMPT from [docs/SPRINT_PLAN.md](docs/SPRINT_PLAN.md) (Azure + DPAPI secrets).

## Remaining gaps (by sprint)

| Gap | Sprint |
|-----|--------|
| Azure OpenAI + secrets | 6 |
| Demo script, controlled experiment | 7 |
| Gaming Mode, settings full, history compare | 8 |
| n8n KB reindex export | 7 (optional) |

## Key files

| Area | Path |
|------|------|
| Sprint plan | `docs/SPRINT_PLAN.md` |
| ML anomaly | `PatchGuard/Services/Ml/`, `docs/ML_REPORT.md` |
| Sensor history | `PatchGuard/Services/History/SensorHistoryService.cs` |
| Alert rules | `PatchGuard/Services/Alerts/AlertRuleEngine.cs` |
| Alerts UI | `PatchGuard/ViewModels/AlertsViewModel.cs`, `Views/AlertsView.xaml` |
| Guided fixes | `PatchGuard/Services/Fixes/GuidedFixPlanService.cs` |
| AI architecture | `docs/AI_ARCHITECTURE.md` |
| Eval results | `docs/AI_EVAL_RESULTS.md` |
| Chat providers | `PatchGuard/Services/Ai/IChatCompletionProvider.cs`, `OllamaChatProvider.cs`, `OpenAiChatClient.cs` |
| Agentic graph | `PatchGuard/Services/Ai/CouncilAgentGraph.cs`, `SemanticKernelToolHost.cs` |
| Step verify | `PatchGuard/Services/Ai/FixStepVerifier.cs` |
| Trace | `PatchGuard/Models/CouncilTrace.cs` |
| User settings | `PatchGuard/Services/Settings/JsonUserSettingsStore.cs` |
| RAG | `PatchGuard/Services/Ai/KnowledgeRetrievalService.cs` |
| AI / UX roadmaps | `docs/AI_ROADMAP.md`, `docs/UX_ROADMAP.md` |

# PatchGuard — Sprint Plan

**Purpose:** self-contained sprints for **one Cursor chat = one sprint**.  
**Repo:** `C:\Users\pashk\Desktop\chill\PatchGuard`  
**Related:** [AI_ROADMAP.md](AI_ROADMAP.md) · [UX_ROADMAP.md](UX_ROADMAP.md) · [HANDOFF.md](../HANDOFF.md)

**Last updated:** 2026-08-13 · **Notion synced:** PatchGuard AI Development Plan, Checklist, Handoff Guide

---

## How to use

1. Pick the next open sprint (check **Status** below).
2. Open a **new chat** in Cursor.
3. Copy the sprint **CHAT PROMPT** block verbatim.
4. At sprint end: run **Verify**, tick **Definition of Done**, update **Status** in this file, commit.

**Rules (all sprints)**

- One sprint = one focused deliverable; no scope mixing.
- `dotnet test PatchGuard.Tests/PatchGuard.Tests.csproj` must pass before closing.
- Privacy: `AiPrivacyAndProvenanceTests` must stay green after AI changes.
- Agent tools stay **read-only**; privileged fixes go through guided-fix pipeline (Sprint 3).
- Phase 4 ML: **inference-only** — no user-facing “train model” UI.

---

## Progress tracker

| Sprint | Name | Status | Depends on |
|--------|------|--------|------------|
| 1 | Stabilize baseline + CI | ✅ Done | — |
| 2 | Sensor history + alert engine | ✅ Done | Sprint 1 |
| 3 | Guided fixes + alerts UI | ⬜ Not started | Sprint 2 |
| 4 | Classic ML (Microsoft.ML) | ⬜ Not started | Sprint 2 |
| 5 | AI polish (RAG, agent, settings) | ⬜ Not started | Sprint 4 |
| 6 | Cloud adapters (Azure + secrets) | ⬜ Not started | Sprint 5 |
| 7 | Quality loop + portfolio | ⬜ Not started | Sprint 6 |
| 8 | UX optimization + settings full | ⬜ Not started | Sprint 3 |

Status values: ⬜ Not started · 🔄 In progress · ✅ Done

---

## Sprint 1 — Stabilize baseline + CI

**Goal:** lock quality baseline, add CI, document architecture, expand golden set.

**Duration:** 2–3 days

### Tasks

| ID | Task | Details |
|----|------|---------|
| S1-01 | GitHub Actions CI | `.github/workflows/ci.yml` — `dotnet build` + `dotnet test` on push/PR |
| S1-02 | Golden fixtures +5 | Add 5 JSON scenarios under `PatchGuard.Tests/Fixtures/GoldenScenarios/` (total **10**) |
| S1-03 | Fill eval results | Run Rules + Ollama sample scan; fill `docs/AI_EVAL_RESULTS.md` table |
| S1-04 | AI architecture doc | Create `docs/AI_ARCHITECTURE.md` — council, RAG, providers, agent graph (mermaid) |
| S1-05 | Sync references | Add link to this file in `HANDOFF.md` and `README.md` roadmap section |

### Definition of Done

- [x] CI workflow exists and passes on current branch
- [x] `GoldenScenarioTests` expects **10** scenarios, all pass
- [x] `AI_EVAL_RESULTS.md` has real numbers for Rules and Ollama (OpenAI row optional)
- [x] `docs/AI_ARCHITECTURE.md` exists with at least one architecture diagram
- [x] `dotnet test PatchGuard.Tests/PatchGuard.Tests.csproj` — 0 failures

### Verify

```powershell
dotnet build PatchGuard.slnx
dotnet test PatchGuard.Tests/PatchGuard.Tests.csproj
dotnet test PatchGuard.Tests/PatchGuard.Tests.csproj --filter "FullyQualifiedName~GoldenScenario"
```

### Key files

- `.github/workflows/ci.yml` (new)
- `PatchGuard.Tests/Fixtures/GoldenScenarios/*.json`
- `docs/AI_EVAL_RESULTS.md`
- `docs/AI_ARCHITECTURE.md` (new)
- `HANDOFF.md`, `README.md`

### CHAT PROMPT

```
PatchGuard Sprint 1 — Stabilize baseline + CI

Read docs/SPRINT_PLAN.md Sprint 1 section and HANDOFF.md first.

Deliver:
1. GitHub Actions CI (build + full test suite)
2. +5 golden scenario fixtures (total 10) with passing GoldenScenarioTests
3. Fill docs/AI_EVAL_RESULTS.md with real Rules/Ollama metrics from a sample scan
4. Create docs/AI_ARCHITECTURE.md (council, RAG, Ollama, CouncilAgentGraph — mermaid OK)
5. Link SPRINT_PLAN.md from HANDOFF.md and README.md

Constraints: minimal diff, match existing test/doc style, no new features.
Run dotnet test before finishing. Mark Sprint 1 ✅ in docs/SPRINT_PLAN.md when done.
```

---

## Sprint 2 — Sensor history + alert engine

**Goal:** persist hardware snapshots for ML and alerts; evaluate threshold rules.

**Duration:** 4–5 days  
**Depends on:** Sprint 1 ✅

### Tasks

| ID | Task | Details |
|----|------|---------|
| S2-01 | SQLite schema | `SensorSnapshotRecord` table + migration in `DatabaseSchemaInitializer` |
| S2-02 | `ISensorHistoryService` | Save rolling snapshots (CPU/GPU temp, load, RAM); configurable retention (e.g. 7 days) |
| S2-03 | Wire capture | `MonitorViewModel` or background timer saves snapshot every N seconds while monitor open |
| S2-04 | `IAlertRuleEngine` | Evaluate thresholds → `Alert` model (severity, timestamp, threshold, message, recommended action) |
| S2-05 | Default rules | CPU temp > 85°C warning, > 95°C critical; GPU temp; CPU load > 90% — configurable constants first |
| S2-06 | Dashboard hook | `HomeViewModel` shows latest alert summary (count + highest severity) |
| S2-07 | Tests | `SensorHistoryServiceTests`, `AlertRuleEngineTests` including synthetic spike |

### Definition of Done

- [x] Sensor snapshots persist to SQLite and survive app restart
- [x] Inject synthetic high-temp snapshot → `AlertRuleEngine` emits alert
- [x] Home dashboard shows alert summary when active alerts exist
- [x] Unit tests for history + rules pass
- [x] No PII in sensor records (numbers only)

### Verify

```powershell
dotnet test PatchGuard.Tests/PatchGuard.Tests.csproj --filter "FullyQualifiedName~Sensor|FullyQualifiedName~Alert"
dotnet test PatchGuard.Tests/PatchGuard.Tests.csproj
```

### Key files

- `PatchGuard/Data/Entities/SensorSnapshotRecord.cs` (new)
- `PatchGuard/Services/History/ISensorHistoryService.cs` (new)
- `PatchGuard/Services/History/SensorHistoryService.cs` (new)
- `PatchGuard/Services/Alerts/IAlertRuleEngine.cs` (new)
- `PatchGuard/Services/Alerts/AlertRuleEngine.cs` (new)
- `PatchGuard/Models/Alert.cs` (new)
- `PatchGuard/Data/DatabaseSchemaInitializer.cs`
- `PatchGuard/DependencyInjection.cs`
- `PatchGuard/ViewModels/MonitorViewModel.cs`, `HomeViewModel.cs`

### CHAT PROMPT

```
PatchGuard Sprint 2 — Sensor history + alert engine

Read docs/SPRINT_PLAN.md Sprint 2, docs/UX_ROADMAP.md Phase 3, HANDOFF.md.

Deliver:
1. SensorSnapshotRecord + ISensorHistoryService (SQLite, rolling retention)
2. Capture snapshots from Live Monitor (or timed service while monitor active)
3. IAlertRuleEngine with default CPU/GPU temp + load thresholds
4. HomeViewModel alert summary on dashboard
5. Unit tests: synthetic spike triggers alert

Do NOT build full Alerts UI yet (Sprint 3). No ML yet (Sprint 4).
Match EF factory pattern from existing history services.
Run dotnet test before finishing. Mark Sprint 2 ✅ in docs/SPRINT_PLAN.md.
```

---

## Sprint 3 — Guided fixes + alerts UI

**Goal:** complete UX Phase 3 — replace stubs with real alerts list and safe fix pipeline.

**Duration:** 5–7 days  
**Depends on:** Sprint 2 ✅

### Tasks

| ID | Task | Details |
|----|------|---------|
| S3-01 | Replace `AlertsViewModel` stub | Real list: active/resolved alerts, severity badges, timestamps |
| S3-02 | `AlertsView.xaml` | Replace `PlannedFeatureView` template for Alerts section |
| S3-03 | Monitor alerts | Show inline alert banner on `MonitorView` when threshold breached |
| S3-04 | `IGuidedFixPlanService` | Build plan from finding/guide step: preview steps, risk, admin requirement |
| S3-05 | Safety gate | preview → user confirm → execute → verify → record; cancel + timeout |
| S3-06 | Integration | “Run fix” from Findings/Guide (only safe, policy-approved actions) |
| S3-07 | History record | Persist fix run outcome to SQLite (link to scan if available) |
| S3-08 | Tests | Cancellation, partial failure, no auto-run without confirm |

### Definition of Done

- [ ] Alerts page shows real alerts (not placeholder message)
- [ ] One guided fix works end-to-end: preview → confirm → verify → SQLite record
- [ ] No privileged/destructive action runs without explicit confirm
- [ ] `AlertsViewModel` stub message removed
- [ ] Tests cover safety gate and cancellation

### Verify

```powershell
dotnet test PatchGuard.Tests/PatchGuard.Tests.csproj --filter "FullyQualifiedName~Alert|FullyQualifiedName~GuidedFix|FullyQualifiedName~Fix"
dotnet test PatchGuard.Tests/PatchGuard.Tests.csproj
```

Manual smoke: open Alerts → see alert after monitor spike; run one safe fix with confirm dialog.

### Key files

- `PatchGuard/ViewModels/AlertsViewModel.cs`
- `PatchGuard/Views/AlertsView.xaml` (new, replace PlannedFeatureView binding)
- `PatchGuard/Services/Fixes/IGuidedFixPlanService.cs` (new)
- `PatchGuard/Services/Fixes/GuidedFixPlanService.cs` (new)
- `PatchGuard/Views/FindingsView.xaml`, `GuideView.xaml`
- `PatchGuard/App.xaml` (DataTemplate for AlertsViewModel)
- `PatchGuard.Tests/GuidedFixTests.cs` (new)

### CHAT PROMPT

```
PatchGuard Sprint 3 — Guided fixes + alerts UI

Read docs/SPRINT_PLAN.md Sprint 3, docs/UX_ROADMAP.md Phase 3, HANDOFF.md.
Sprint 2 must be done (SensorHistoryService + AlertRuleEngine exist).

Deliver:
1. Real AlertsView + AlertsViewModel (replace PlannedFeatureView stub)
2. Monitor inline alert when thresholds breached
3. IGuidedFixPlanService: preview → confirm → execute → verify → record
4. "Run fix" entry point from Findings or Guide (safe actions only)
5. Tests: confirm required, cancellation, partial failure

Constraints: no auto-run privileged actions; reuse LaunchUriPolicy / existing optimizer where possible.
Run dotnet test before finishing. Mark Sprint 3 ✅ in docs/SPRINT_PLAN.md.
```

---

## Sprint 4 — Classic ML (Microsoft.ML)

**Goal:** anomaly detection on sensor history — **inference-only**, no training UI.

**Duration:** 5–7 days  
**Depends on:** Sprint 2 ✅ (sensor history)

### Scope decisions

- Train model **offline** (dev script or test fixture generator); ship bundled `.zip` / `.mlnet` artifact.
- Product: load model → score → finding with **confidence %** and **human explanation** (mean, std, deviation).
- No “Train model” button in Settings.

### Tasks

| ID | Task | Details |
|----|------|---------|
| S4-01 | Z-score detector | Pure C# baseline on sensor series; unit test with synthetic spike |
| S4-02 | Microsoft.ML package | Add `Microsoft.ML` + Isolation Forest training script (console or test helper) |
| S4-03 | Model artifact | Checked-in or embedded resource model trained on synthetic dataset |
| S4-04 | `IAnomalyDetector` | Interface wrapping ML + Z-score fallback when model missing |
| S4-05 | `AnomalyDiagnosticModule` | Emits `Finding` with confidence, sensor name, evidence text |
| S4-06 | Explanations | Finding details: “CPU temp 97°C vs baseline μ=55 σ=5 (z=8.4)” |
| S4-07 | UI surfacing | Show anomaly on Monitor/Home health area |
| S4-08 | Test metrics | Assert precision/recall/F1 on fixed synthetic dataset in tests |
| S4-09 | `docs/ML_REPORT.md` | Model type, metrics, limitations, no training UI note |

### Definition of Done

- [ ] Synthetic spike in test data → anomaly finding appears
- [ ] Finding includes confidence and plain-language explanation
- [ ] `AnomalyDetectorTests` assert metrics above agreed floor (document in ML_REPORT)
- [ ] Normal baseline data → no false positive in golden synthetic set
- [ ] `docs/ML_REPORT.md` complete
- [ ] No training UI added

### Verify

```powershell
dotnet test PatchGuard.Tests/PatchGuard.Tests.csproj --filter "FullyQualifiedName~Anomaly"
dotnet test PatchGuard.Tests/PatchGuard.Tests.csproj
```

### Key files

- `PatchGuard/Services/Ml/IAnomalyDetector.cs` (new)
- `PatchGuard/Services/Ml/ZScoreAnomalyDetector.cs` (new)
- `PatchGuard/Services/Ml/MlNetAnomalyDetector.cs` (new)
- `PatchGuard/Services/Diagnostics/AnomalyDiagnosticModule.cs` (new)
- `PatchGuard.Tests/AnomalyDetectorTests.cs` (new)
- `docs/ML_REPORT.md` (new)
- `docs/AI_ROADMAP.md` — mark Phase 4 done

### CHAT PROMPT

```
PatchGuard Sprint 4 — Classic ML (Microsoft.ML)

Read docs/SPRINT_PLAN.md Sprint 4, docs/AI_ROADMAP.md Phase 4, HANDOFF.md.
Requires Sprint 2 sensor history (ISensorHistoryService).

Deliver inference-only anomaly detection:
1. Z-score baseline detector + tests
2. Microsoft.ML Isolation Forest — train OFFLINE, bundle model artifact
3. IAnomalyDetector + AnomalyDiagnosticModule → Finding with confidence % and explanation
4. Surface on Monitor or Home
5. AnomalyDetectorTests with precision/recall/F1 on synthetic dataset
6. docs/ML_REPORT.md

HARD CONSTRAINT: NO user-facing training mode / train button.
Run dotnet test before finishing. Mark Sprint 4 ✅ in docs/SPRINT_PLAN.md and AI_ROADMAP Phase 4 Done.
```

---

## Sprint 5 — AI polish (RAG, agent, settings)

**Goal:** close remaining gaps from AI Phases 1–3 and expand eval coverage.

**Duration:** 4–5 days  
**Depends on:** Sprint 4 ✅

### Tasks

| ID | Task | Details |
|----|------|---------|
| S5-01 | Settings provider radio | Cloud / Ollama / Rules — persists choice (user settings file or secure store) |
| S5-02 | Agent trace UI | Collapsible `CouncilTrace` in Guide: nodes visited, tools called, timing |
| S5-03 | VerifySteps + retry | Reject unsafe fix steps in graph; max 1 retry; test coverage |
| S5-04 | Hybrid retrieval | Keyword overlap + embedding rank in `KnowledgeRetrievalService` |
| S5-05 | KB expansion | Add playbooks until **15+** documents |
| S5-06 | Golden +15 | Expand fixtures toward **15–20** total (incremental OK) |
| S5-07 | CI threshold gate | Fail PR if golden avg actionability/consistency drops >5% vs baseline |
| S5-08 | Update `AI_ARCHITECTURE.md` | Add trace, hybrid RAG, settings flow |

### Definition of Done

- [ ] Settings UI switches chat provider without editing appsettings manually
- [ ] Guide shows agent trace after council run
- [ ] Unsafe step triggers verify/retry path (test proves it)
- [ ] 15+ playbooks; hybrid retrieval test passes
- [ ] Golden count ≥15; CI gate enforced
- [ ] All existing privacy tests pass

### Verify

```powershell
dotnet test PatchGuard.Tests/PatchGuard.Tests.csproj --filter "FullyQualifiedName~Golden|FullyQualifiedName~Knowledge|FullyQualifiedName~Phase3|FullyQualifiedName~ChatProvider|FullyQualifiedName~AiPrivacy"
dotnet test PatchGuard.Tests/PatchGuard.Tests.csproj
```

### Key files

- `PatchGuard/Views/SettingsView.xaml`, `SettingsViewModel.cs`
- `PatchGuard/Models/CouncilTrace.cs` (new)
- `PatchGuard/Services/Ai/CouncilAgentGraph.cs`
- `PatchGuard/Views/GuideView.xaml`
- `PatchGuard/Services/Ai/KnowledgeRetrievalService.cs`
- `PatchGuard/KnowledgeBase/Playbooks/*.md`
- `.github/workflows/ci.yml` (threshold step)

### CHAT PROMPT

```
PatchGuard Sprint 5 — AI polish (RAG, agent, settings)

Read docs/SPRINT_PLAN.md Sprint 5, docs/AI_ROADMAP.md, HANDOFF.md.

Deliver:
1. Settings UI: Cloud / Ollama / Rules provider radio (persisted)
2. CouncilTrace collapsible UI in Guide (nodes + tools called)
3. VerifySteps + 1 retry in CouncilAgentGraph + test
4. Hybrid keyword+embedding retrieval in KnowledgeRetrievalService
5. Expand KB to 15+ playbooks
6. Golden fixtures → 15+; CI fails if metrics drop >5% vs baseline
7. Update docs/AI_ARCHITECTURE.md

Keep agent tools read-only. Run full dotnet test. Mark Sprint 5 ✅ in docs/SPRINT_PLAN.md.
```

---

## Sprint 6 — Cloud adapters (Azure + secrets)

**Goal:** Azure OpenAI behind existing provider abstraction; secrets not in plain JSON.

**Duration:** 4–5 days  
**Depends on:** Sprint 5 ✅

### Tasks

| ID | Task | Details |
|----|------|---------|
| S6-01 | `AzureOpenAiChatProvider` | Same `IChatCompletionProvider`; deployment name + endpoint + key |
| S6-02 | Resolver update | `ChatProviderResolver`: add `Azure` option; `Auto` order documented |
| S6-03 | `ISecretStorageService` | Windows DPAPI protect API keys at rest |
| S6-04 | Settings integration | Azure endpoint/deployment/key fields; migrate from plain appsettings |
| S6-05 | `docs/CLOUD_ARCHITECTURE.md` | Hybrid desktop diagram, Azure section, key storage |
| S6-06 | Bedrock stub | Interface or no-op provider + README honest scope |
| S6-07 | Tests | Mock HTTP Azure provider; resolver selection; secret round-trip |

### Definition of Done

- [ ] Azure provider works with mock test (or documented manual test steps)
- [ ] Secrets stored via DPAPI, not plain text in user-editable JSON
- [ ] `CLOUD_ARCHITECTURE.md` complete
- [ ] Bedrock stub documented as optional/future
- [ ] Privacy tests still pass

### Verify

```powershell
dotnet test PatchGuard.Tests/PatchGuard.Tests.csproj --filter "FullyQualifiedName~Azure|FullyQualifiedName~ChatProvider|FullyQualifiedName~Secret"
dotnet test PatchGuard.Tests/PatchGuard.Tests.csproj
```

### Key files

- `PatchGuard/Services/Ai/AzureOpenAiChatProvider.cs` (new)
- `PatchGuard/Services/Ai/ChatProviderResolver.cs`
- `PatchGuard/Services/Security/ISecretStorageService.cs` (new)
- `PatchGuard/Services/Security/DpapiSecretStorageService.cs` (new)
- `docs/CLOUD_ARCHITECTURE.md` (new)
- `docs/AI_ROADMAP.md` — Phase 5 done

### CHAT PROMPT

```
PatchGuard Sprint 6 — Cloud adapters (Azure + secrets)

Read docs/SPRINT_PLAN.md Sprint 6, docs/AI_ROADMAP.md Phase 5, HANDOFF.md.

Deliver:
1. AzureOpenAiChatProvider (IChatCompletionProvider)
2. ChatProviderResolver + Settings support for Azure
3. DPAPI secret storage — keys not plain JSON on disk
4. docs/CLOUD_ARCHITECTURE.md with hybrid desktop diagram
5. Bedrock stub + honest README scope
6. Tests with mocked Azure HTTP

Do not break Ollama/Rules paths. Run dotnet test. Mark Sprint 6 ✅ in docs/SPRINT_PLAN.md.
```

---

## Sprint 7 — Quality loop + portfolio

**Goal:** close AI Phase 6 — experiment, CI polish, demo, CV bullets.

**Duration:** 3–4 days  
**Depends on:** Sprint 6 ✅

### Tasks

| ID | Task | Details |
|----|------|---------|
| S7-01 | Controlled experiment | Pick ONE change (chunk size / top-K / prompt tweak); before/after table |
| S7-02 | Update eval docs | Fill experiment table in `docs/AI_EVAL_RESULTS.md` |
| S7-03 | Human rubric | Manually score 5 guides 1–5; note agreement with auto actionability |
| S7-04 | Demo script | `docs/DEMO_SCRIPT.md` — 3–5 min flow: scan → findings → Ollama → provenance → ML anomaly |
| S7-05 | CV bullets | 2 bullets with real metrics in README or `docs/PORTFOLIO.md` |
| S7-06 | n8n workflow (optional) | Export KB reindex workflow JSON to `docs/n8n/` + README note |
| S7-07 | Final checklist | Mark AI competencies in `docs/AI_ROADMAP.md`; update HANDOFF |

### Definition of Done

- [ ] Experiment documented with hypothesis + result (even if null)
- [ ] `DEMO_SCRIPT.md` ready to follow live
- [ ] CV bullets cite real numbers from baseline
- [ ] Full test suite + CI green
- [ ] HANDOFF “Next” section reflects completion

### Verify

```powershell
dotnet build PatchGuard.slnx
dotnet test PatchGuard.Tests/PatchGuard.Tests.csproj
# Manual: follow docs/DEMO_SCRIPT.md once
```

### Key files

- `docs/AI_EVAL_RESULTS.md`
- `docs/DEMO_SCRIPT.md` (new)
- `docs/PORTFOLIO.md` (new, optional)
- `docs/n8n/` (optional)
- `HANDOFF.md`, `docs/AI_ROADMAP.md`

### CHAT PROMPT

```
PatchGuard Sprint 7 — Quality loop + portfolio

Read docs/SPRINT_PLAN.md Sprint 7, docs/AI_EVAL_BASELINE.md, HANDOFF.md.

Deliver:
1. One controlled AI experiment (chunk/top-K/prompt) with before/after metrics in AI_EVAL_RESULTS.md
2. Human rubric notes for 5 guides (1-5 actionability/safety/clarity)
3. docs/DEMO_SCRIPT.md (3-5 min demo path)
4. 2 CV bullets with real metrics (README or docs/PORTFOLIO.md)
5. Optional: docs/n8n/ KB reindex workflow export
6. Update HANDOFF.md and AI_ROADMAP.md — mark Phase 6 complete

Minimal code changes unless experiment requires a small diff. Run dotnet test. Mark Sprint 7 ✅.
```

---

## Sprint 8 — UX optimization + settings full

**Goal:** UX Phases 4–5 — optimize sections, gaming mode, full settings, history compare.

**Duration:** 7–10 days  
**Depends on:** Sprint 3 ✅ (can parallelize with Sprints 4–7)

### Tasks

| ID | Task | Details |
|----|------|---------|
| S8-01 | Optimize layout | Sections: Safe Cleanup · Gaming Mode · Advanced |
| S8-02 | Gaming Mode | Reversible temp tweaks + pre-state capture + restore |
| S8-03 | Cleanup hardening | Impact estimates, execution log, verification, rollback where supported |
| S8-04 | Settings full | Alert thresholds UI, PresentMon path, appearance, elevation prefs |
| S8-05 | History comparison | Compare two scans side-by-side (health, finding count, trend) |
| S8-06 | FPS setup UX | Dependency detection, setup guidance, benchmark session flow |
| S8-07 | Remove remaining stubs | No `PlannedFeatureView` for shipped sections |
| S8-08 | Tests + UX doc | Update `docs/UX_ROADMAP.md` status to done |

### Definition of Done

- [ ] Optimize view has three sections with clear copy
- [ ] Gaming Mode applies and restores state
- [ ] Settings configures alert thresholds and paths
- [ ] History comparison UI works on stored scans
- [ ] `docs/UX_ROADMAP.md` Phases 3–5 marked complete
- [ ] Full test suite passes

### Verify

```powershell
dotnet test PatchGuard.Tests/PatchGuard.Tests.csproj
```

Manual smoke: Gaming Mode on/off; change alert threshold; compare two scans.

### Key files

- `PatchGuard/Views/OptimizeView.xaml`, `OptimizeViewModel.cs`
- `PatchGuard/Views/SettingsView.xaml`, `SettingsViewModel.cs`
- `PatchGuard/Services/Optimization/` (gaming mode service)
- `PatchGuard/ViewModels/HistoryCompareViewModel.cs` (new, or extend Home)
- `docs/UX_ROADMAP.md`

### CHAT PROMPT

```
PatchGuard Sprint 8 — UX optimization + settings full

Read docs/SPRINT_PLAN.md Sprint 8, docs/UX_ROADMAP.md Phases 4-5, HANDOFF.md.

Deliver:
1. OptimizeView: Safe Cleanup / Gaming Mode / Advanced sections
2. Gaming Mode with reversible changes + pre-state restore
3. Cleanup: impact estimates, logs, verification, rollback where feasible
4. Settings: alert thresholds, PresentMon path, appearance, elevation prefs
5. Scan history comparison UI
6. FPS setup improvements (dependency detection, guidance)
7. Remove PlannedFeatureView stubs for completed sections
8. Update docs/UX_ROADMAP.md

Safety: no destructive auto-run; match guided-fix patterns from Sprint 3.
Run dotnet test. Mark Sprint 8 ✅ in docs/SPRINT_PLAN.md.
```

---

## Competency → sprint map

| # | Competence | Closed in sprint |
|---|------------|------------------|
| 9 | Metrics (Phase 0) | ✅ already · reinforced Sprint 1, 5, 7 |
| 4 | RAG | ✅ core · hybrid + 15 docs Sprint 5 |
| 2 | Generative AI | ✅ already |
| 5 | Local LLM | ✅ core · Settings radio Sprint 5 |
| 3 | Agentic AI | ✅ core · trace + verify Sprint 5 |
| 6 | LangGraph analog | ✅ already · diagram Sprint 1, 5 |
| 1 | Classic ML | Sprint 4 |
| 8 | Azure/AWS | Sprint 6 |
| 7 | n8n | Sprint 7 (optional) |
| — | UX product | Sprints 2–3, 8 |
| — | Guided fixes | Sprint 3 |

---

## Final project Definition of Done

- [ ] Sprints 1–7 ✅ (Sprint 8 for full product polish)
- [ ] 9/9 AI competencies closed (AWS = honest stub OK)
- [ ] 15–20 golden scenarios + CI regression gate
- [ ] `AI_ARCHITECTURE.md`, `ML_REPORT.md`, `CLOUD_ARCHITECTURE.md`, `DEMO_SCRIPT.md`
- [ ] No agent write tools; secrets not in plain JSON
- [ ] `dotnet test` green locally and in CI
- [ ] Notion checklist synced with repo reality

---

## Quick reference — verify all

```powershell
dotnet build PatchGuard.slnx
dotnet test PatchGuard.Tests/PatchGuard.Tests.csproj
dotnet test PatchGuard.Tests/PatchGuard.Tests.csproj --filter "FullyQualifiedName~GoldenScenario|FullyQualifiedName~AiPrivacy|FullyQualifiedName~ChatProvider"
```

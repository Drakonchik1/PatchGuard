# PatchGuard — handoff

**Branch:** `main` · **Remote:** `origin/main`

## What shipped

- Phase 1 UX shell: sidebar, design system, dashboard, reusable controls.
- Phase 2 diagnostic journey: step indicator, unified scoring, actionable findings, optional AI with consent + provenance.
- AI Phase 0: golden eval harness + baseline docs.
- AI Phase 1 RAG: local playbook KB, retrieval, KnowledgeBase provenance (offline embeddings).
- AI Phase 2 Local LLM: Ollama via `IChatCompletionProvider`, Auto/OpenAI/Ollama/Rules, council without cloud key, `Ollama` / `Ollama+KB` eval labels.
- Docs: `docs/AI_ROADMAP.md`, `docs/OLLAMA_SETUP.md`, updated README.
- Security hardening: launch URI policy, EF factory, sanitizer allowlist, navigation fixes.

## Run

```powershell
# Optional local LLM
ollama pull qwen3.5:latest
dotnet run --project PatchGuard/PatchGuard.csproj
dotnet test PatchGuard.Tests/PatchGuard.Tests.csproj
```

Leave `OpenAI:ApiKey` empty → AI guidance without consent uses Ollama + KB when enabled.
To use another model: `ollama pull <name>` then set `Ollama:Model` — see `docs/OLLAMA_SETUP.md`.

## Next

### Product (UX) — still planned
1. Alerts + guided-fix orchestration (preview-confirm-execute-verify).
2. Optimization sections + Gaming Mode.
3. Settings, protected secret storage, history comparison UI, FPS setup UX.

### AI — still planned
1. n8n KB reindex → JSON export.
2. Settings UI radio for ChatProvider (config-only today).
3. Semantic Kernel agentic graph + ≥2 read-only tools.
4. Microsoft.ML anomaly model + test metrics.
5. Azure OpenAI adapter + cloud doc.
6. CI golden regression gate.

## Key files

| Area | Path |
|------|------|
| Chat providers | `PatchGuard/Services/Ai/IChatCompletionProvider.cs`, `OllamaChatProvider.cs`, `OpenAiChatClient.cs` |
| Provider select | `PatchGuard/Services/Ai/ChatProviderResolver.cs` |
| Council | `PatchGuard/Services/Ai/AiCouncilService.cs` |
| RAG | `PatchGuard/Services/Ai/KnowledgeRetrievalService.cs` |
| AI plan | `docs/AI_ROADMAP.md` |
| Ollama howto | `docs/OLLAMA_SETUP.md` |
| UX plan | `docs/UX_ROADMAP.md` |

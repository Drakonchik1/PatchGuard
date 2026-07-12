# PatchGuard — handoff

**Branch:** `feature/ux-roadmap` · **Base:** `main` · **Worktree:** `.worktrees/ux-roadmap`

## What shipped

- Phase 1 UX shell: sidebar, design system, dashboard, reusable controls.
- Phase 2 diagnostic journey: step indicator, unified scoring, actionable findings, optional AI with consent + provenance.
- `PatchGuard.Tests`: 133 automated tests.
- Security hardening: launch URI policy, EF factory, sanitizer allowlist, navigation fixes.

## Run

```powershell
cd PatchGuard
dotnet run
# or from repo root:
dotnet run --project PatchGuard/PatchGuard.csproj
dotnet test PatchGuard.Tests/PatchGuard.Tests.csproj
```

## Next (Phases 3–5)

1. Alerts + guided-fix orchestration (preview-confirm-execute-verify).
2. Optimization sections + Gaming Mode.
3. Settings, protected secret storage, history comparison UI, FPS setup UX.

## Key files

| Area | Path |
|------|------|
| Scoring | `PatchGuard/Services/Health/HealthScorePolicy.cs` |
| DB init | `PatchGuard/Data/DatabaseSchemaInitializer.cs` |
| AI privacy | `PatchGuard/Services/Ai/ExternalDiagnosticSanitizer.cs` |
| Link safety | `PatchGuard/Services/Ai/LaunchUriPolicy.cs` |
| Roadmap | `docs/UX_ROADMAP.md` |

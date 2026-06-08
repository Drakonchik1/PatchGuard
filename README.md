# PatchGuard

Windows desktop health checker (WPF + .NET 10). Read-only diagnostics, multi-agent AI council, and manual repair guides — no admin elevation.

## Run

```powershell
cd PatchGuard
dotnet run --project PatchGuard
```

## Scan scenarios

| Scenario | What it checks |
|----------|----------------|
| After Windows Update | Disk, updates, services, event log |
| Quick health check | OS build and free disk space |


## AI council

1. **Technician** — fixes from scan data  
2. **Skeptic** — challenges risky advice  
3. **Researcher** — web search context (Tavily when configured)  
4. **Chief Councilor** — one verdict + manual steps  

Without API keys, a **local council** runs the same flow with rule-based responses.

### API keys (optional)

Copy `PatchGuard/appsettings.example.json` → `PatchGuard/appsettings.Development.json`:

```json
{
  "OpenAI": {
    "ApiKey": "sk-...",
    "Model": "gpt-4o-mini"
  },
  "WebSearch": {
    "Provider": "tavily",
    "ApiKey": "tvly-..."
  }
}
```

## Diagnostics

| Module | Status |
|--------|--------|
| OS build info | Live |
| Disk space (C:) | Live |
| Windows Update history | Live |
| Event Log (48h errors) | Live |
| Update services | Live |
| GPU + driver | Live |
| Memory load | Live |
| Synthetic render test | Live |
| In-game FPS log | Live |

## Stack

.NET 10 WPF · MVVM · EF Core SQLite · OpenAI HTTP · Tavily search

## Safety

PatchGuard never runs elevated fixes. Users follow manual steps only.

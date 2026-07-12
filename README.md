# PatchGuard

Windows desktop health, performance, and boost tool (WPF + .NET 10). Live hardware
monitoring, real game FPS capture, a one-click safe optimizer, read-only diagnostics,
and optional multi-agent AI guidance with explicit privacy controls.

## Run

```powershell
dotnet run --project PatchGuard/PatchGuard.csproj
```

## Test

```powershell
dotnet test PatchGuard.Tests/PatchGuard.Tests.csproj
```

133 automated tests cover navigation, scoring, history, AI privacy, diagnostics, and UI contracts.

## Navigation

Labeled sidebar: **Dashboard**, **Diagnose**, **Live Monitor**, **Game Performance**,
**Optimize**, **Alerts**, **Settings**.

Diagnostic journey: **Choose scan** → **Scan** → **Review findings** → **Optional AI guidance**
(with persistent step indicator and predictable back/cancel).

See [docs/UX_ROADMAP.md](docs/UX_ROADMAP.md) for the full phased roadmap and [HANDOFF.md](HANDOFF.md) for developer handoff.

## Features

### Dashboard
Overall health from latest scan, recommended next action, recent scan history with
trends, live hardware snapshot, quick links to Monitor / FPS / Optimize.

### Live monitor
Real-time CPU/GPU temperatures, load, clocks, fan/power sensors, and memory usage
via [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor).
Load and RAM work without admin; full sensors require elevation ("Run as admin").

### Game FPS
Average / 1% low / 0.1% low via Intel [PresentMon](https://github.com/GameTechDev/PresentMon) (MIT).
Drop `PresentMon-x64.exe` into `PatchGuard/Tools/PresentMon/` (see `README.txt` there).
Capturing another process usually needs administrator rights. Binaries are Authenticode-verified before launch.

### Optimize (safe boost)
Reversible actions only — no Windows settings changes:

- Trim working sets
- Clear temp files (contained paths; skips reparse points)
- Empty Recycle Bin
- Flush DNS cache
- Optional: restart Explorer

### Diagnostics
Read-only modules: OS, disk, memory, temperatures, CPU, GPU, Windows Update history,
event log (48h), update services.

Scenarios: **Full system audit**, **Game performance check**, **After Windows Update**,
**Quick health check**.

### Health score
Single deterministic policy (`risk-capped-v1`): per-module caps prevent event-log noise
from collapsing the score. Snapshots persist with each scan for stable history trends.

### Findings
Each result includes explanation, evidence, recommended fix, action state, admin
requirement, risk, and verification status.

### AI guidance (optional)
Four-agent council flow (Technician, Skeptic, Researcher, Chief) when API keys are configured.

**Privacy by default:** external AI/web calls require an explicit consent checkbox per request.
Only sanitized diagnostic **categories** are sent — never titles, event text, paths, or secrets.
Guide UI shows source labels (local / AI / web) and inspectable reference links (safe http/https only).

Fix-step links open http(s) web pages or whitelisted `ms-settings:` pages only.

Without API keys, a **local council** runs the same flow with rule-based responses.

### API keys (optional)

Copy `PatchGuard/appsettings.example.json` → `PatchGuard/appsettings.Development.json` (gitignored):

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

## Stack

.NET 10 WPF · MVVM (CommunityToolkit.Mvvm) · EF Core SQLite · LibreHardwareMonitorLib ·
Intel PresentMon · OpenAI HTTP · Tavily search · xUnit

## Safety

- Optimizer: safe, reversible actions only.
- Diagnostics: read-only.
- Default: normal user; UAC only when you choose elevation.
- PresentMon: Intel-signed binary required in Tools folder.
- External links and AI payloads validated before use.

## Roadmap status

| Phase | Status |
|-------|--------|
| 1 — UX foundation | Done |
| 2 — Diagnostic journey | Done |
| 3 — Alerts + guided fixes | Planned |
| 4 — Optimization expansion | Planned |
| 5 — Settings, history, FPS UX | Planned |

Details: [docs/UX_ROADMAP.md](docs/UX_ROADMAP.md)

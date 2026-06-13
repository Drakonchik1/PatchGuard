# PatchGuard

Windows desktop health, performance, and boost tool (WPF + .NET 10). Live hardware
monitoring, real game FPS capture, a one-click safe optimizer, read-only diagnostics,
and a multi-agent AI council that explains how to fix what it finds.

## Run

```powershell
cd PatchGuard\PatchGuard
dotnet run
```

## Features

### Live monitor
Real-time CPU/GPU temperatures, load, clocks, fan/power sensors, and memory usage
via [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor).
Load and RAM work without admin; full temperature/fan sensors require administrator
rights, so the Monitor screen offers a one-click "Run as admin" relaunch.

### Game FPS
Measures real in-game **Average / 1% low / 0.1% low** frame rate using Intel
[PresentMon](https://github.com/GameTechDev/PresentMon) (MIT). PresentMon is not
redistributed in source control — drop `PresentMon-x64.exe` into
`PatchGuard/Tools/PresentMon/` (see that folder's `README.txt`). Capturing another
process usually needs administrator rights.

### Optimize (one-click safe boost)
Runs only safe, reversible actions and **changes no Windows settings**:

- Free RAM by trimming process working sets
- Clear temporary files (user/Windows temp + INet cache; in-use files are skipped)
- Empty the Recycle Bin
- Flush the DNS resolver cache
- Optional: restart Windows Explorer

### Diagnostics + AI council
Pick a scenario, run a read-only scan, then convene the AI council for a fix plan.

| Module | Status |
|--------|--------|
| OS build info | Live |
| Disk space (C:) | Live |
| Memory (RAM) | Live |
| Temperatures (CPU/GPU) | Live |
| CPU load | Live |
| Graphics card (model/driver/VRAM) | Live |
| Windows Update history (KB list) | Live |
| Event Log (48h errors) | Live |
| Update services (read-only) | Live |

Scenarios: **Full system audit**, **Game performance check**, **After Windows Update**,
**Quick health check**.

## AI council

1. **Technician** — proposes fixes from scan data
2. **Skeptic** — challenges risky or unproven advice
3. **Researcher** — adds web search context (Tavily when configured)
4. **Chief Councilor** — reads the full debate and writes one unified verdict + manual steps

Without API keys, a **local council** runs the same debate flow with rule-based responses.

### API keys (optional)

Copy `appsettings.example.json` → `appsettings.Development.json`:

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
Intel PresentMon · OpenAI HTTP · Tavily search

## Safety & permissions

- The optimizer only performs safe, reversible actions and never edits Windows settings.
- Diagnostics are read-only.
- PatchGuard runs as the normal user by default and only self-elevates (with a UAC
  prompt) when you choose "Run as admin" for full hardware sensors or to capture FPS
  for another process.
- Hardware monitoring loads a kernel driver via LibreHardwareMonitor; some antivirus
  tools may flag this. The app remains fully usable (load, RAM, boosts, scans) without
  elevation if you prefer not to grant it.

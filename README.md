# PatchGuard

Windows desktop health, performance, and boost tool (WPF + .NET 10). Live hardware
monitoring, real game FPS capture, a one-click safe optimizer, read-only diagnostics,
and optional multi-agent AI guidance with explicit privacy controls — including
**local RAG** and **Ollama** (no cloud key required).

## Run

```powershell
dotnet run --project PatchGuard/PatchGuard.csproj
```

With local AI (recommended for offline demos): install Ollama, pull a model, keep
`Ollama:Enabled` true — see [docs/OLLAMA_SETUP.md](docs/OLLAMA_SETUP.md).

## Test

```powershell
dotnet test PatchGuard.Tests/PatchGuard.Tests.csproj
```

Focused AI checks:

```powershell
dotnet test PatchGuard.Tests/PatchGuard.Tests.csproj --filter "FullyQualifiedName~AiPrivacy|FullyQualifiedName~Ollama|FullyQualifiedName~ChatProvider|FullyQualifiedName~Knowledge|FullyQualifiedName~Golden"
```

## Navigation

Labeled sidebar: **Dashboard**, **Diagnose**, **Live Monitor**, **Game Performance**,
**Optimize**, **Alerts**, **Settings**.

Diagnostic journey: **Choose scan** → **Scan** → **Review findings** → **Optional AI guidance**
(with persistent step indicator and predictable back/cancel).

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

Four-agent council (Technician, Skeptic, Researcher, Chief) with these backends:

| Backend | Needs cloud key? | Needs consent checkbox? | Notes |
|---------|------------------|-------------------------|--------|
| **Rules** | No | No | Deterministic offline council |
| **Ollama** (local LLM) | No | No | Same debate loop via localhost |
| **OpenAI** + optional Tavily | Yes (DPAPI) | Yes | Cloud; sanitized categories only |
| **Azure OpenAI** | Yes (DPAPI) | Yes | Same `IChatCompletionProvider` seam |
| **AWS Bedrock** | — | — | **Stub only** — not wired; see [CLOUD_ARCHITECTURE.md](docs/CLOUD_ARCHITECTURE.md) |

**RAG:** local playbooks under `PatchGuard/KnowledgeBase/Playbooks` are retrieved before
guidance. Guide UI shows provenance labels (local / Local LLM (Ollama) / AI / Azure / web / KB)
and inspectable references. Fix-step links are limited to safe http(s) / `ms-settings:`.

### Change the local model

1. `ollama pull <your-model>`
2. Set `Ollama:Model` in `appsettings` to the exact name from `ollama list`
3. Restart the app

Full steps: [docs/OLLAMA_SETUP.md](docs/OLLAMA_SETUP.md).

### API keys (optional cloud)

Copy `PatchGuard/appsettings.example.json` → `PatchGuard/appsettings.Development.json` (gitignored).
On first launch, keys in config are **migrated into Windows DPAPI** under
`%LocalAppData%\PatchGuard\secrets\` — clear them from the JSON afterward so they do not stay plaintext in the repo tree.

Azure (Settings UI or config):

```json
{
  "AzureOpenAI": {
    "Endpoint": "https://YOUR_RESOURCE.openai.azure.com/",
    "Deployment": "your-chat-deployment",
    "ApiKey": "your-azure-key",
    "ApiVersion": "2024-06-01"
  },
  "Ai": {
    "ChatProvider": "Azure"
  }
}
```

OpenAI / Tavily example:

```json
{
  "OpenAI": {
    "ApiKey": "sk-...",
    "Model": "gpt-4o-mini"
  },
  "WebSearch": {
    "Provider": "tavily",
    "ApiKey": "tvly-..."
  },
  "Ai": {
    "ChatProvider": "Auto"
  },
  "Ollama": {
    "Enabled": true,
    "BaseUrl": "http://localhost:11434",
    "Model": "llama3.2:3b"
  }
}
```

`Ai:ChatProvider`: `Auto` | `OpenAI` | `Azure` | `Ollama` | `Rules`.
**Auto order:** Azure → OpenAI → Ollama → Rules (cloud needs consent).

## Stack

.NET 10 WPF · MVVM (CommunityToolkit.Mvvm) · EF Core SQLite · LibreHardwareMonitorLib ·
Intel PresentMon · OpenAI HTTP · Ollama HTTP · Tavily search · local RAG · xUnit

## Safety

- Optimizer: safe, reversible actions only.
- Diagnostics: read-only.
- Default: normal user; UAC only when you choose elevation.
- PresentMon: Intel-signed binary required in Tools folder.
- External links and AI payloads validated before use.
- **Ollama:** loopback only (`localhost`, `127.0.0.1`, `[::1]`); remote Ollama endpoints are rejected.
- **Azure OpenAI:** HTTPS + official Azure OpenAI host suffixes only (`*.openai.azure.com`, `*.services.ai.azure.com`); endpoint is re-read on each request.
- **Cloud chat HTTP:** providers stream with `ResponseHeadersRead` and reject bodies over **1 MB**.
- Local Ollama traffic stays on the machine; cloud calls still require consent.

## Documentation

| Doc | Purpose |
|-----|---------|
| [HANDOFF.md](HANDOFF.md) | Short developer handoff |
| [docs/UX_ROADMAP.md](docs/UX_ROADMAP.md) | Product UX phases |
| [docs/AI_ROADMAP.md](docs/AI_ROADMAP.md) | AI competence phases (done + planned) |
| [docs/SPRINT_PLAN.md](docs/SPRINT_PLAN.md) | Sprint-by-sprint build plan (one chat = one sprint) |
| [docs/OLLAMA_SETUP.md](docs/OLLAMA_SETUP.md) | Install Ollama / switch models |
| [docs/AI_ARCHITECTURE.md](docs/AI_ARCHITECTURE.md) | Council, RAG, providers, agent graph |
| [docs/CLOUD_ARCHITECTURE.md](docs/CLOUD_ARCHITECTURE.md) | Hybrid desktop, Azure, DPAPI, Bedrock stub |
| [docs/AI_EVAL_BASELINE.md](docs/AI_EVAL_BASELINE.md) | Eval metrics + golden baseline |
| [docs/AI_EVAL_RESULTS.md](docs/AI_EVAL_RESULTS.md) | Provider comparison worksheet |

## Roadmap status

**Execution:** [docs/SPRINT_PLAN.md](docs/SPRINT_PLAN.md) — 8 sprints, one Cursor chat each.

| Sprint | Focus | Status |
|--------|-------|--------|
| 1 | CI + golden×10 + architecture doc | ✅ |
| 2 | Sensor history + alerts | ✅ |
| 3 | Guided fixes + alerts UI | ✅ |
| 4 | Classic ML (inference-only) | ✅ |
| 5 | AI polish (settings, trace, RAG) | ✅ |
| 6 | Azure + secrets | ✅ |
| 7 | Quality loop + portfolio | ⬜ |
| 8 | UX optimization + settings | ⬜ |

### Shipped

| Track | Done |
|-------|------|
| UX Phase 1–2 | Foundation + diagnostic journey |
| AI Phase 0–5 | Metrics, RAG, Ollama, agentic graph, ML, Azure + DPAPI |

Details: [docs/UX_ROADMAP.md](docs/UX_ROADMAP.md) · [docs/AI_ROADMAP.md](docs/AI_ROADMAP.md) · [HANDOFF.md](HANDOFF.md)

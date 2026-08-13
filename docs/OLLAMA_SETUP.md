# Ollama setup — local LLM for PatchGuard

Run the AI council **without a cloud API key**. Data stays on `localhost`.

## Recommended model (default)

PatchGuard defaults to **`llama3.2:3b`** (~2 GB):

- Much lighter than 7B+ “thinking” models (e.g. qwen3.5 ~6.6 GB)
- Enough quality for short council replies + JSON chief verdict
- Typical full guidance run: **1–4 minutes** on a modern PC (vs 10–15+ min on large models)

```powershell
ollama pull llama3.2:3b
ollama list
```

### Even lighter (low RAM / old laptop)

```powershell
ollama pull llama3.2:1b
```

Set `"Model": "llama3.2:1b"` — faster, slightly weaker JSON formatting.

### Avoid for daily use

Large reasoning models (`qwen3.5`, `gpt-4o` local tags, 7B+ with “thinking”) — high RAM/VRAM, many slow tokens per council phase. Use **Rules** or cloud OpenAI instead if the PC struggles.

## Prerequisites

1. Install [Ollama](https://ollama.com/download) for Windows.
2. Start the daemon (usually automatic after install), or:

```powershell
ollama serve
```

## App configuration

Edit `PatchGuard/appsettings.json` (or a local `appsettings.Development.json`):

```json
{
  "Ai": {
    "ChatProvider": "Auto"
  },
  "Ollama": {
    "Enabled": true,
    "BaseUrl": "http://localhost:11434",
    "Model": "llama3.2:3b",
    "NumPredict": 512,
    "NumCtx": 4096,
    "Temperature": 0.35
  },
  "OpenAI": {
    "ApiKey": ""
  }
}
```

| Setting | Meaning |
|---------|---------|
| `Ai:ChatProvider` | `Auto` (prefer OpenAI if key+consent, else Ollama), or force `Ollama` / `OpenAI` / `Rules` |
| `Ollama:Enabled` | Master switch for local LLM |
| `Ollama:BaseUrl` | Ollama HTTP API (default localhost) |
| `Ollama:Model` | Exact tag from `ollama list` |
| `Ollama:NumPredict` | Max tokens per agent reply (512 matches “&lt;130 words” prompts) |
| `Ollama:NumCtx` | Context window — 4096 is enough for KB + debate transcript |
| `Ollama:Temperature` | Lower (0.2–0.4) = faster, more deterministic steps |

Leave `OpenAI:ApiKey` empty for a fully offline demo. Open AI guidance **without** the external-consent checkbox — the Guide should show **Local LLM (Ollama)** (and often **Local knowledge base**).

## How to use your own model

Anyone can swap models without code changes:

1. Pull the model:

```powershell
ollama pull mistral
# or: ollama pull qwen2.5:3b
```

2. Confirm the name:

```powershell
ollama list
```

3. Set `Ollama:Model` to that exact name.

4. Restart PatchGuard (config is read at startup).

Tips:

- Prefer **3B–4B** chat models for the multi-agent loop.
- The council makes **~13** sequential chat calls per guidance run — small models stay usable; huge models do not.
- If Ollama is down or returns errors, PatchGuard falls back to the rule-based local council.

## Force Ollama even when an OpenAI key exists

```json
"Ai": { "ChatProvider": "Ollama" }
```

## Disable local LLM (rules + KB only)

```json
"Ollama": { "Enabled": false }
```

or

```json
"Ai": { "ChatProvider": "Rules" }
```

## Related

- Roadmap: [AI_ROADMAP.md](AI_ROADMAP.md)
- Eval notes: [AI_EVAL_BASELINE.md](AI_EVAL_BASELINE.md), [AI_EVAL_RESULTS.md](AI_EVAL_RESULTS.md)

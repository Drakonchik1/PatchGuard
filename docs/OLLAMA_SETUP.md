# Ollama setup — local LLM for PatchGuard

Run the AI council **without a cloud API key**. Data stays on `localhost`.

## Prerequisites

1. Install [Ollama](https://ollama.com/download) for Windows.
2. Start the daemon (usually automatic after install), or:

```powershell
ollama serve
```

3. Pull a chat model (default used by PatchGuard):

```powershell
ollama pull qwen3.5:latest
ollama list
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
    "Model": "qwen3.5:latest"
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

Leave `OpenAI:ApiKey` empty for a fully offline demo. Open AI guidance **without** the external-consent checkbox — the Guide should show **Local LLM (Ollama)** (and often **Local knowledge base**).

## How to use your own model

Anyone can swap models without code changes:

1. Pull the model:

```powershell
ollama pull llama3.2
# or: ollama pull mistral
# or: ollama pull phi4
```

2. Confirm the name:

```powershell
ollama list
```

3. Set `Ollama:Model` to that exact name, for example:

```json
"Ollama": {
  "Enabled": true,
  "BaseUrl": "http://localhost:11434",
  "Model": "llama3.2:latest"
}
```

4. Restart PatchGuard (config is read at startup).

Tips:

- Smaller models (3B–8B) answer faster; larger models may follow JSON verdicts better.
- The council makes **many** chat calls per guidance run — expect higher latency than OpenAI.
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

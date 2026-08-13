# PatchGuard — ML Report (Sprint 4)

**Status:** Done · **Date:** 2026-08-13  
**Constraint:** inference-only — **no** user-facing “Train model” UI or training mode in Settings.

## Goal

Detect unusual CPU/GPU/RAM sensor patterns from rolling history (`ISensorHistoryService`) and surface them as findings with **confidence %** and a plain-language explanation.

## Models shipped

| Artifact | Path | Role |
|----------|------|------|
| Isolation Forest (JSON) | `PatchGuard/Models/Ml/isolation-forest-v1.json` | Primary multivariate anomaly score (Liu et al. 2008) |
| Microsoft.ML RandomizedPCA (zip) | `PatchGuard/Models/Ml/sensor-anomaly-rpca.zip` | ML.NET-native anomaly trainer bundle |
| Z-score baseline | in-process (`ZScoreAnomalyDetector`) | Always-on fallback + explanation text (μ, σ, z) |

### Why Isolation Forest is not `Microsoft.ML.Trainers.IsolationForest`

Microsoft.ML **5.0.0** (GA) does not expose Isolation Forest. An experimental trainer was contributed toward ML.NET 6.x and **cannot reliably `Model.Save()`** in that design. PatchGuard therefore:

1. Implements Isolation Forest in C# (`IsolationForestModel`) for offline train + bundled inference.
2. Still uses **Microsoft.ML 5.0.0** `AnomalyDetection.Trainers.RandomizedPca` for a checked-in `.zip` model.
3. Product detector (`MlNetAnomalyDetector`) prefers Isolation Forest, then RPCA, then Z-score.

## Features

Five numeric inputs (no PII / no device names):

1. CPU temperature (°C)  
2. CPU load (%)  
3. GPU temperature (°C)  
4. GPU load (%)  
5. RAM load (%)

## Training (offline only)

```powershell
$env:PATCHGUARD_REGEN_ML='1'
dotnet test PatchGuard.Tests/PatchGuard.Tests.csproj --filter "FullyQualifiedName~RegenBundledModels"
```

`MlOfflineTrainer.TrainAndSaveArtifacts` generates synthetic healthy samples (Gaussian around typical idle/load ranges), fits Isolation Forest (contamination 0.05) and RandomizedPCA (rank 2), and writes artifacts under `PatchGuard/Models/Ml/`. Artifacts are copied to the app output directory at build time.

**Never** wire this into Settings or Monitor UI.

## Inference flow

1. Live Monitor / scan module loads recent SQLite snapshots.
2. `MlNetAnomalyDetector` scores the latest sample.
3. Per-sensor **z-score** explanations are attached when univariate spikes exist, e.g.  
   `CPU temperature 97°C vs baseline μ=55.0 σ=5.0 (z=8.4)`.
4. `AnomalyDiagnosticModule` emits `Finding` titles with confidence %.
5. Monitor shows an **ML anomaly** banner (separate from threshold alerts).

## Test metrics (synthetic evaluation set)

Fixed set from `MlOfflineTrainer.CreateEvaluationSet(seed: 7)`: 80 normal + 20 spike vectors.

| Metric | Result | Floor (asserted) |
|--------|--------|------------------|
| Precision | **0.833** | ≥ 0.80 |
| Recall | **1.000** | ≥ 0.80 |
| F1 | **0.909** | ≥ 0.80 |

Also covered:

- Z-score spike detection and no false positive on flat noise.
- Diagnostic module finding text includes confidence + `z=`.
- Fallback to Z-score when model directory is empty.
- Normal IF training set false-positive rate bounded near contamination.

## Limitations

- Synthetic training data ≠ every real PC idle/load profile; expect more false positives after unusual workloads until history is rich.
- Needs ≥ **20** snapshots; open Live Monitor to collect.
- Isolation Forest threshold is calibrated on synthetic contamination; real-world recalibration requires a **new offline** train + ship, not in-app training.
- RandomizedPCA is complementary; explanations still lean on z-score for readability.
- Pre-existing NuGet warning: `SQLitePCLRaw.lib.e_sqlite3` (EF Core transitive) — unrelated to Microsoft.ML.

## Security / privacy

- Model inputs are numeric sensor aggregates only (same policy as `SensorSnapshotRecord`).
- No cloud upload for ML scoring.
- No secrets in model artifacts.

## Key code

| Piece | Location |
|-------|----------|
| `IAnomalyDetector` | `PatchGuard/Services/Ml/IAnomalyDetector.cs` |
| Z-score | `PatchGuard/Services/Ml/ZScoreAnomalyDetector.cs` |
| Isolation Forest | `PatchGuard/Services/Ml/IsolationForestModel.cs` |
| ML.NET detector | `PatchGuard/Services/Ml/MlNetAnomalyDetector.cs` |
| Offline trainer | `PatchGuard/Services/Ml/MlOfflineTrainer.cs` |
| Diagnostic module | `PatchGuard/Services/Diagnostics/AnomalyDiagnosticModule.cs` |
| Tests | `PatchGuard.Tests/AnomalyDetectorTests.cs` |

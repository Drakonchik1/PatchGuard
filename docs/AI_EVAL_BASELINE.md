# AI Evaluation Baseline

## Purpose

PatchGuard now records one aggregate evaluation row for each completed AI council session.
The goal is to measure whether prompt, agent, or model changes improve guidance quality
without storing raw scan text, prompts, or other private content.

## What is stored

Each `CouncilEvaluationRecord` stores:

- `EvaluatedAt`
- `Scenario`
- `Source` (`Local`, `AI`, `AI+Web`, `Web`)
- `LatencyMs`
- `FixStepCount`
- `CouncilMessageCount`
- `ActionabilityScore`
- `ConsistencyScore`

This record intentionally excludes finding details, hostnames, file paths, and full
guide text. It is safe for local trend analysis and Settings UI history.

## How scoring works

`CouncilEvaluator` computes two lightweight structural metrics:

- `ActionabilityScore`: how complete the fix steps are
  - non-empty title
  - sufficiently detailed instructions
  - supporting artifact such as `CopyText` or `LinkUrl`
- `ConsistencyScore`: how internally coherent the guide is
  - summary present
  - chief verdict present
  - `Local` source retained
  - council discussion present
  - step titles are distinct
  - provenance is coherent and all links are safe

These are not “truth” metrics. They are fast baseline metrics that help catch regressions
in output shape before deeper qualitative review.

## Golden baseline

The baseline is defined by 5 curated golden fixtures in
`PatchGuard.Tests/Fixtures/GoldenScenarios`.

Current verified averages:

- Average actionability: `90.0%`
- Average consistency: `93.3%`

Per-fixture expected scores:

- `after-update-service-recovery`: `100.0 / 100.0`
- `driver-remediation-with-web`: `100.0 / 100.0`
- `manual-verification-only`: `66.7 / 100.0`
- `duplicate-step-regression`: `100.0 / 83.3`
- `web-only-provenance-drift`: `83.3 / 83.3`

Format: `ActionabilityScore / ConsistencyScore`

## Validation

Validated with:

```powershell
dotnet test PatchGuard.Tests/PatchGuard.Tests.csproj --filter "FullyQualifiedName~AiPrivacyAndProvenanceTests|FullyQualifiedName~CouncilEvaluatorTests|FullyQualifiedName~CouncilEvaluationServiceTests|FullyQualifiedName~GoldenScenarioTests"
```

The focused baseline suite passed with 19/19 tests.

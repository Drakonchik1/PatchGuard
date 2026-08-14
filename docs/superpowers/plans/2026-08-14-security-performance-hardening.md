# PatchGuard Security and Performance Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Sprint 6 working tree commit-ready by fixing validated security, UI responsiveness, cancellation, and dependency findings.

**Architecture:** Harden trust boundaries at provider, URI, file, and process edges. Move blocking work behind existing async service boundaries without changing user-facing Rules/local Ollama behavior or introducing a privileged helper.

**Tech Stack:** .NET 10, WPF, xUnit, EF Core SQLite, IHttpClientFactory, Windows DPAPI.

## Global Constraints

- Preserve Rules and loopback Ollama behavior.
- Cloud providers require consent and HTTPS.
- No new privileged helper and no full history-database encryption.
- Every behavior change follows red-green TDD.
- Do not commit; leave a verified working tree for the user.

---

### Task 1: Cloud endpoint and output trust boundaries

**Files:**
- Modify: `PatchGuard/Services/Ai/AzureOpenAiChatProvider.cs`
- Modify: `PatchGuard/Services/Ai/OllamaChatProvider.cs`
- Modify: `PatchGuard/Services/Ai/ChatProviderResolver.cs`
- Modify: `PatchGuard/Services/Ai/ExternalUrlPolicy.cs`
- Modify: `PatchGuard/Services/Ai/LaunchUriPolicy.cs`
- Modify: `PatchGuard/Services/Ai/FixStepVerifier.cs`
- Test: `PatchGuard.Tests/AzureAndSecretTests.cs`
- Test: `PatchGuard.Tests/ChatProviderResolverTests.cs`
- Test: existing URI/fix verifier tests

**Interfaces:**
- Produce: `AzureOpenAiChatProvider.TryNormalizeEndpoint(string?, out Uri)` accepts only official Azure HTTPS endpoints.
- Produce: `OllamaChatProvider.IsLoopbackEndpoint(string?)`.
- Produce: fix verification rejects unsafe `CopyText`.

- [ ] Add failing tests proving Azure rejects HTTP/non-Azure hosts and uses a newly changed endpoint without restart.
- [ ] Add failing tests proving consent-free Ollama accepts loopback only.
- [ ] Add failing tests proving HTTP/private links and unsafe copied commands are rejected.
- [ ] Run focused tests and confirm expected failures.
- [ ] Build absolute Azure request URIs from current options on each call:

```csharp
var endpoint = ValidateEndpoint(_options.AzureEndpoint);
var requestUri = new Uri(endpoint, relativePath);
using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
```

- [ ] Add explicit HTTPS/host/loopback policy and include `CopyText` in `FixStepVerifier`.
- [ ] Run focused tests until green.

### Task 2: Atomic local persistence and safe filesystem/process operations

**Files:**
- Modify: `PatchGuard/Services/Security/DpapiSecretStorageService.cs`
- Modify: `PatchGuard/Services/Settings/JsonUserSettingsStore.cs`
- Modify: `PatchGuard/Services/Optimization/Steps/TempFilesCleanStep.cs`
- Modify: `PatchGuard/Services/Optimization/Steps/DnsFlushStep.cs`
- Modify: `PatchGuard/Services/Optimization/Steps/ExplorerRestartStep.cs`
- Modify: `PatchGuard/Services/Performance/PresentMonFpsCaptureService.cs`
- Test: `PatchGuard.Tests/AzureAndSecretTests.cs`
- Test: optimizer/platform test files

**Interfaces:**
- Produce: atomic file replacement helper local to each store.
- Produce: temp traversal skips every reparse-point path.
- Produce: process steps rethrow `OperationCanceledException` and fail closed without canonical executable paths.

- [ ] Add failing persistence tests for corrupt DPAPI status and replacement-safe writes.
- [ ] Add a failing Windows-only temp-cleanup test that creates a directory junction
      with `cmd /c mklink /J`, and skip only when junction creation is denied.
- [ ] Add cancellation/fail-closed process tests.
- [ ] Run focused tests and confirm expected failures.
- [ ] Implement temp-file writes followed by `File.Move(temp, target, true)` and cleanup on failure.
- [ ] Reject reparse points before enumeration/deletion and immediately before each delete.
- [ ] Reverify PresentMon immediately before launch; remove PATH fallbacks; rethrow cancellation.
- [ ] Run focused tests until green.

### Task 3: Dependency and startup hardening

**Files:**
- Modify: `PatchGuard/PatchGuard.csproj`
- Modify: `PatchGuard/App.xaml.cs`
- Test: build and package audit

**Interfaces:**
- Produce: patched SQLite native dependency graph.
- Produce: asynchronous `OnStartup` awaiting `DatabaseSchemaInitializer.InitializeAsync`.

- [ ] Record current failing NuGet audit showing `SQLitePCLRaw.lib.e_sqlite3 2.1.11`.
- [ ] Upgrade the supported EF Core SQLite/native chain to a patched compatible version.
- [ ] Remove forced `ConcurrentGarbageCollection=false`.
- [ ] Replace sync-over-async startup:

```csharp
protected override async void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);
    await _services.GetRequiredService<DatabaseSchemaInitializer>().InitializeAsync();
}
```

- [ ] Run build, startup-related tests, and NuGet audit until clean.

### Task 4: UI-thread and lifecycle responsiveness

**Files:**
- Modify: `PatchGuard/ViewModels/MonitorViewModel.cs`
- Modify: `PatchGuard/Services/Diagnostics/DiagnosticOrchestrator.cs`
- Modify: `PatchGuard/Services/Optimization/SystemOptimizerService.cs`
- Modify: `PatchGuard/ViewModels/FpsViewModel.cs`
- Modify: `PatchGuard/ViewModels/OptimizeViewModel.cs`
- Modify: `PatchGuard/ViewModels/FindingsViewModel.cs`
- Modify: `PatchGuard/ViewModels/GuideViewModel.cs`
- Modify: `PatchGuard/Services/Navigation/NavigationService.cs`
- Test: monitor, diagnostic, optimization, and navigation tests

**Interfaces:**
- Produce: one lifecycle CTS per active view.
- Produce: non-overlapping background hardware capture and sensor persistence.
- Produce: blocking service work executes off WPF dispatcher while ordered progress remains intact.

- [ ] Add failing tests for cancellation on navigation and bounded navigation history.
- [ ] Add failing tests for non-overlapping monitor persistence/capture.
- [ ] Run focused tests and confirm expected failures.
- [ ] Replace dispatcher work with one cancellation-bound loop:

```csharp
while (await timer.WaitForNextTickAsync(token))
{
    var snapshot = await Task.Run(_hardware.Capture, token);
    await dispatcher.InvokeAsync(() => Apply(snapshot));
}
```

- [ ] Wrap blocking diagnostic/optimizer boundaries in cancellable worker execution.
- [ ] Scope back history to the diagnostic journey and cancel discarded view operations.
- [ ] Move sensor retention to a coarse cadence and enforce one in-flight save.
- [ ] Run focused tests until green.

### Task 5: AI latency and HTTP response bounds

**Files:**
- Modify: `PatchGuard/Services/Ai/CouncilAgentGraph.cs`
- Modify: `PatchGuard/Services/Ai/LocalCouncilSession.cs`
- Modify: chat provider HTTP implementations
- Test: `PatchGuard.Tests/Phase3AgenticGraphTests.cs`
- Test: provider tests

**Interfaces:**
- Produce: bounded transcript rendered once per phase.
- Produce: no artificial service-layer sleeps.
- Produce: providers request `ResponseHeadersRead` and reject oversized bodies.

- [ ] Add failing tests for transcript duplication and response-size limits.
- [ ] Run focused tests and confirm expected failures.
- [ ] Remove recursive prompt history and service-layer delays.
- [ ] Add a shared bounded-response stream/helper with a conservative byte limit.
- [ ] Use `HttpCompletionOption.ResponseHeadersRead` for chat requests.
- [ ] Run focused tests until green.

### Task 6: Final verification and documentation

**Files:**
- Modify: `README.md`
- Modify: `docs/CLOUD_ARCHITECTURE.md`
- Modify: `HANDOFF.md`
- Review: all uncommitted files

- [ ] Document Azure official-host and Ollama-loopback restrictions.
- [ ] Run `dotnet build PatchGuard.slnx`; expected exit code 0.
- [ ] Run `dotnet test PatchGuard.Tests/PatchGuard.Tests.csproj`; expected 0 failed.
- [ ] Run `dotnet list PatchGuard.slnx package --vulnerable --include-transitive`; expected no vulnerable packages.
- [ ] Run IDE lint diagnostics for changed files.
- [ ] Perform final secret/input/auth/file/process/dependency review.
- [ ] Inspect `git diff --check`, `git status --short`, and final diff; do not stage or commit.

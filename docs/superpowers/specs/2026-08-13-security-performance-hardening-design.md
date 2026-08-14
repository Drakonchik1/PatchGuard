# PatchGuard security and performance hardening

## Goal

Bring the current Sprint 6 working tree to a commit-ready state by fixing validated
high/medium security findings and P0/P1 performance or reliability issues without
introducing a separate privileged helper process or encrypting the complete history
database.

## Security design

- Azure OpenAI accepts HTTPS endpoints only, with no credentials/query/fragment and
  an allowlist of official Azure OpenAI host suffixes. Each request builds an absolute
  URI from the current validated endpoint so endpoint and key changes take effect
  without restart.
- Ollama remains consent-free only for loopback endpoints. Non-loopback configuration
  is rejected instead of silently sending local diagnostics to a remote host.
- External links require HTTPS and reject loopback, private, link-local, and embedded
  credentials. Model-generated `CopyText` is included in fix-step safety validation.
- Secret and settings files use temporary-file plus atomic replacement. DPAPI status
  validates decryption rather than file existence only.
- Temp cleanup rejects reparse points throughout traversal and fails closed when the
  target cannot be proven to remain under the selected temp root.
- PresentMon is reverified immediately before launch; executable-name fallbacks for
  privileged system commands are removed.
- Plain configuration key migration remains backward compatible, but tracked examples
  contain no keys and documentation states that source plaintext must be removed.
- The vulnerable SQLite native dependency is upgraded through a compatible supported
  dependency version and verified with NuGet's vulnerability audit.

## Performance and reliability design

- Hardware polling moves from `DispatcherTimer` work to a lifecycle-bound,
  non-overlapping background loop. Only view-model property updates return to WPF.
- Diagnostic and optimization boundaries move blocking WMI, event log, native, and
  filesystem work off the UI thread while preserving ordered progress and cancellation.
- Startup awaits database initialization asynchronously and reports initialization
  failures without sync-over-async.
- AI council history is included once, bounded per phase, and artificial service-layer
  delays are removed. Cancellation remains observable.
- Sidebar navigation no longer grows an unbounded transient view-model stack. Long
  operations receive lifecycle cancellation tokens and stale completions are ignored.
- Sensor persistence permits one write at a time and performs retention on a coarse
  cadence rather than per sample.
- Forced non-concurrent GC configuration is removed. HTTP chat responses use
  `ResponseHeadersRead` and bounded response streams where practical.

## Compatibility

- Rules and local loopback Ollama behavior remain unchanged.
- Existing Azure, OpenAI, RAG, diagnostics, guided-fix, and history tests remain valid.
- Remote Ollama and non-official Azure hosts intentionally stop working because they
  violate the product's documented privacy boundary.
- No database schema migration is required unless a minimal retention setting proves
  necessary during implementation.

## Verification

1. Add regression tests for endpoint changes, HTTPS/host restrictions, loopback-only
   Ollama, unsafe links/CopyText, atomic secret/settings behavior, cancellation, and
   non-overlapping sensor persistence.
2. Run focused security, provider, optimization, navigation, and lifecycle tests.
3. Run `dotnet build PatchGuard.slnx`.
4. Run `dotnet test PatchGuard.Tests/PatchGuard.Tests.csproj`.
5. Run `dotnet list PatchGuard.slnx package --vulnerable --include-transitive`; expected
   result: no known vulnerable packages.
6. Review the final diff for secret leakage, unsafe file/process operations, and
   regressions to Rules/Ollama paths.

## Deferred architecture

- A minimal privileged helper replacing whole-app elevation.
- Full DPAPI-backed encryption and migration of existing scan-history records.
- Major PresentMon percentile/parser redesign beyond bounded streaming.
- Broad UI redesign or Sprint 7/8 product features.

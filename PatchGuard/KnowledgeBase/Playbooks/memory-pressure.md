# Memory pressure and commit charge

## Symptom
Memory diagnostic shows high RAM usage or commit charge near limit. Games hitch, browsers thrash, or the system feels sluggish under load.

## Safe checks
Identify top consumers in Task Manager. Distinguish a temporary spike (game launch) from a sustained leak. Confirm page file is system-managed unless policy requires otherwise.

## Recovery steps
1. Close unused browser profiles and background Electron apps.
2. Use PatchGuard Optimize working-set trim as a reversible relief step.
3. Rescan; if pressure remains above ~85% at idle, investigate a specific leaking process.

## Avoid
Do not disable the page file to “free disk”. Do not install untrusted RAM “boost” utilities.

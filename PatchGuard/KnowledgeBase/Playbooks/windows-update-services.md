# Windows Update services recovery

## Symptom
Windows Update stuck, pending forever, or error codes when installing patches. Diagnostic module reports `wuauserv` or `BITS` not running.

## Safe checks
Open `services.msc` and confirm Windows Update and Background Intelligent Transfer Service are set to Automatic (or Manual for BITS) and are Running. Do not change service account identities.

## Recovery steps
1. Restart `wuauserv` and `BITS` when an administrator is available.
2. Run Windows Update troubleshooter from Settings → System → Troubleshoot.
3. Re-scan with PatchGuard After Windows Update scenario to verify services stay up.

## Avoid
Do not download random `.bat` “update fixers”. Do not delete `SoftwareDistribution` unless Microsoft guidance for that exact error is confirmed.

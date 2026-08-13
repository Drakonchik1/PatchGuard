# High CPU load troubleshooting

## Symptom
Diagnostic reports sustained CPU load above comfortable interactive thresholds while the PC feels sluggish.

## Safe checks
Confirm Task Manager shows which process dominates. Prefer identifying user apps over killing system processes. Note whether load is single-core or multi-core.

## Recovery steps
1. Close the heaviest non-essential apps shown in Task Manager.
2. Open Startup Apps and disable unused launchers.
3. Re-run the PatchGuard scan to confirm load dropped.

## Avoid
Do not end `csrss.exe`, `wininit.exe`, or antivirus processes. Do not disable Windows Update services to chase CPU usage.

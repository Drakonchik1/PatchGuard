# Display driver reset (safe path)

## Symptom
Black screens, TDR recoveries, or GPU driver instability without clear thermal cause.

## Safe checks
Note whether the issue follows a recent game or driver update. Prefer Windows Settings reset over third-party force wipe initially.

## Recovery steps
1. Open Display settings and confirm refresh rate matches the monitor.
2. Use Windows Update optional drivers only from Microsoft catalog if offered.
3. If problems persist, use the vendor control panel clean-install tool after a restore point.

## Avoid
Do not delete driver files from `C:\Windows\System32\DriverStore` manually.

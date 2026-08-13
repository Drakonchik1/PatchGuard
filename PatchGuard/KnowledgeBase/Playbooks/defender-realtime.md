# Microsoft Defender real-time protection checks

## Symptom
Security findings or unusual CPU from antimalware service without user-initiated scans.

## Safe checks
Confirm Defender is the active antivirus. Prefer Windows Security UI over PowerShell exclusions.

## Recovery steps
1. Open Windows Security → Virus & threat protection and run a quick scan.
2. Review protection history for false positives on known-good apps.
3. If a trusted app is blocked, add an exclusion only for that exact path.

## Avoid
Do not turn off real-time protection permanently. Do not blanket-exclude entire user profiles.

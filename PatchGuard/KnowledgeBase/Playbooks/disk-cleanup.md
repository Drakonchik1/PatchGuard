# Disk space cleanup for updates

## Symptom
Diagnostic reports low free space (often under ~10–20 GB). Cumulative updates and feature packs need headroom for staging files.

## Safe checks
Confirm the warning refers to the system volume (usually `C:`), not a recovery partition. Check Recycle Bin size and large unused apps before touching system folders.

## Recovery steps
1. Empty Recycle Bin.
2. Run Storage Sense or clear Temp under user-profile temp paths only.
3. Uninstall two largest unused apps, then re-run the scan.

## Avoid
Do not wipe `Windows\WinSxS`, do not disable System Restore as a first step, and do not delete files from other users’ profiles.

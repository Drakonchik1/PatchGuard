# Unexpected shutdown and Kernel-Power 41

## Symptom
Event Log shows unexpected shutdown (6008) or Kernel-Power 41. The PC lost power, hard-reset, or crashed before Windows could write a clean shutdown record.

## Safe checks
Check power cable / laptop battery, recent driver installs, and whether Fast Startup is enabled. Note if the event aligns with a Windows Update reboot attempt.

## Recovery steps
1. Finish any pending updates when power is stable.
2. Temporarily disable Fast Startup, then verify a clean reboot completes.
3. Rescan; if Kernel-Power 41 repeats, collect minidumps and check PSU / overheating next.

## Avoid
Do not force more optional updates while instability continues. Do not ignore repeated 41 events as “just logs”.

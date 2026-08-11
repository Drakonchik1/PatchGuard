# Event Log DCOM noise (10016 / 10010)

## Symptom
Event Log module surfaces DistributedCOM warnings 10016 or 10010 after Windows builds. Titles look alarming but apps usually still launch.

## Safe checks
Confirm whether any application actually fails to open at the same timestamps. Correlate with recent feature updates. Treat as noise when only DCOM entries appear without crashes.

## Recovery steps
1. Deprioritize DCOM-only warnings versus disk, service, or power findings.
2. If a specific app fails, repair or reinstall that app rather than editing COM permissions first.
3. Rescan after the next clean reboot to see if the flood stops.

## Avoid
Do not follow forum guides that grant Everyone full COM launch permissions. That is a security regression for a cosmetic log entry.

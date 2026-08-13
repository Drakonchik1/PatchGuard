# Network connectivity basics

## Symptom
Name resolution failures, intermittent connectivity, or failed update downloads that look network-related.

## Safe checks
Confirm Wi-Fi/Ethernet link light and that other devices on the same network work. Prefer Flush DNS and renew lease before changing adapter drivers.

## Recovery steps
1. Flush DNS cache from PatchGuard Optimize or `ipconfig /flushdns`.
2. Toggle airplane mode or unplug/replug Ethernet.
3. Open Network settings and forget/rejoin Wi-Fi if the SSID is stale.

## Avoid
Do not reset the entire network stack as a first step. Do not delete VPN profiles you still need.

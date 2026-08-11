# DNS cache flush for connectivity

## Symptom
Apps fail to resolve hostnames after network changes, VPN disconnects, or ISP DNS hiccups. Browser may show intermittent name-resolution errors.

## Safe checks
Confirm the PC has a valid IP (`ipconfig`) and that the issue is name resolution rather than no link. Note whether VPN or corporate DNS is involved.

## Recovery steps
1. Flush DNS with `ipconfig /flushdns` (PatchGuard Optimize can do this safely).
2. Renew lease with `ipconfig /renew` if DHCP is used.
3. Retry the failing app; if still broken, switch temporarily to a known public DNS only with user consent.

## Avoid
Do not edit `hosts` with third-party “optimizer” lists. Do not disable firewall to “test DNS”.

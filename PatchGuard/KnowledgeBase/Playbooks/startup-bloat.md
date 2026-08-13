# Startup app bloat

## Symptom
Slow logon and high early CPU/disk activity after boot from many auto-start programs.

## Safe checks
Open Startup Apps in Settings and list publishers you do not recognize. Prefer disabling, not uninstalling, on the first pass.

## Recovery steps
1. Open `ms-settings:startupapps` and disable unused chat/updaters.
2. Reboot once and measure time-to-desktop.
3. Re-enable only apps you still need daily.

## Avoid
Do not disable Microsoft Security or OEM thermal helpers without understanding their role.

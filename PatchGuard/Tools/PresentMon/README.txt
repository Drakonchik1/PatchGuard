PatchGuard game FPS capture uses Intel PresentMon (MIT licensed).

HOW IT WORKS
------------
PatchGuard's FPS feature looks for a PresentMon console executable in this
folder (next to the app after build) named one of:

    PresentMon.exe
    PresentMon-x64.exe
    PresentMon-2.x-x64.exe   (any version suffix is accepted)

If found, PatchGuard runs a short, timed capture against the selected game
process and computes Average / 1% low / 0.1% low FPS from the frame times.

If no PresentMon executable is present, the FPS screen still works but shows a
friendly message explaining the tool is missing, with the download link.

INSTALL (one-time)
------------------
1. Download the latest PresentMon console release (PresentMon-X.Y.Z-x64.exe):
   https://github.com/GameTechDev/PresentMon/releases/latest
2. Copy that .exe into this folder.
3. Rebuild / relaunch PatchGuard. The file is copied to:
       <app>\Tools\PresentMon\

NOTES
-----
- Capturing another process's frame data can require running PatchGuard as
  administrator. Use the "Run as admin" prompt on the Monitor screen.
- PresentMon is a separate MIT-licensed project by Intel; see LICENSE.txt.

# iFakeLocation

![](https://i.imgur.com/ELFifkA.png)

Simulate your iDevice's GPS location over USB — either a single fixed point, or a real
road-following route between two addresses that the device is walked/driven along at a chosen
speed, with pause/resume/stop controls.

This is a modernized rewrite of the original [master131/iFakeLocation](https://github.com/master131/iFakeLocation):
a .NET 10 Minimal API backend + a Next.js/shadcn/mapcn frontend, replacing the original's raw
`HttpListener` + jQuery/Bootstrap/Leaflet page. See [ARCHITECTURE.md](ARCHITECTURE.md) for what
changed and why.

## Requirements

None of the published builds require a separately-installed .NET or Python runtime -- they're
fully self-contained on every platform (the app bundles a frozen
[pymobiledevice3](https://github.com/doronz88/pymobiledevice3) executable for device
communication; see [ARCHITECTURE.md](ARCHITECTURE.md) for why and how).

* **Windows / macOS:** iTunes (or Apple Mobile Device Support) installed, so the device shows up
  over USB.
* **Linux:** `usbmuxd` installed and running (most distros package this; it's what lets any tool,
  including this one, see USB-connected iOS devices at all).

## Download

See the [Releases](https://github.com/master131/iFakeLocation/releases) page, or build from
source (below).

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0),
[Node.js](https://nodejs.org/) (LTS or newer), and Python 3.9+ on the machine doing the build --
**not** on the end user's machine, since both the frontend and the pymobiledevice3 helper are
built ahead of time into static/frozen artifacts.

```shell
# 1. Build the frontend (produces frontend/out/, then copies it into iFakeLocation/wwwroot/)
cd frontend
npm ci
npm run build
cd ..

# 2. Freeze the pymobiledevice3 helper for your platform (produces
#    iFakeLocation/pmd3-dist/<rid>/pmd3[.exe], picked up by the matching publish profile below).
#    IMPORTANT: PyInstaller cannot cross-compile -- run this ON a machine matching the target RID.
cd pymobiledevice3-build
./build.sh win-x64     # on Windows: .\build.ps1 win-x64
./build.sh osx-x64      # on an Intel Mac, or an Apple Silicon Mac via `arch -x86_64`
./build.sh linux-x64
cd ..

# 3. Publish the backend for your platform (bundles the frontend + pmd3 helper automatically)
dotnet publish iFakeLocation/iFakeLocation.csproj -c Release -p:PublishProfile=Windows-x64
dotnet publish iFakeLocation/iFakeLocation.csproj -c Release -p:PublishProfile=OSX-x64
dotnet publish iFakeLocation/iFakeLocation.csproj -c Release -p:PublishProfile=Linux-x64
```

Each profile publishes a self-contained build to `iFakeLocation/bin/publish/<platform>/`. Since
step 2 can't cross-compile, producing all three platforms' builds realistically needs a CI matrix
(one runner per OS), not a single contributor's machine -- see [ARCHITECTURE.md](ARCHITECTURE.md).

For local development, you can skip step 2 and instead point the backend at a plain
`pip install pymobiledevice3` virtualenv via configuration (environment variable
`PyMobileDevice__ExecutablePathOverride=/path/to/venv/bin/pymobiledevice3`, or the equivalent key
in `appsettings.Development.json`) -- run the backend directly with
`dotnet run --project iFakeLocation` (after building the frontend once) and it'll serve whatever's
currently in `wwwroot/`; run `npm run dev` inside `frontend/` for a hot-reloading frontend dev
server against a separately running backend (update `frontend/lib/api-client.ts`'s base URL, or
use a proxy, if the two aren't on the same origin during development).

To run the backend's tests: `dotnet test iFakeLocation.Tests`. To run the frontend's:
`cd frontend && npm run test` (and `npm run typecheck` / `npm run lint`).

## Running

### Windows
Run `iFakeLocation.exe`.

### macOS
Open the DMG (or the published folder) and run `iFakeLocation`.

### Linux
```shell
chmod +x ./iFakeLocation
./iFakeLocation
```

## How to use

* Connect your iDevice to your computer. Click "Refresh" and select your iDevice from the list.
* **Fixed location:** search for a place (or double-click the map, or drag the pin), then click
  "Set Fake Location". "Stop Fake Location" reverts it.
* **Route simulation:** enter a start and end address under "Route Planning", click "Calculate
  Route" to draw the road-following path, optionally drag a point on the line to insert a
  waypoint (the route recalculates through it), pick a speed, then "Follow Route" to start the
  simulated walk/drive. Pause/Resume/Stop controls appear while a route is running.
* The first time you set a location on a device, the tool needs to download and mount a developer
  disk image -- a "Preparing device..." dialog shows while that happens (no progress percentage;
  see [ARCHITECTURE.md](ARCHITECTURE.md) for why).
* Confirm the fake location using Apple Maps, Google Maps, etc. If your device is still stuck at
  the faked location after stopping, turn Location Services off and back on in Settings > Privacy.
* Your device will show a Developer menu in Settings afterward; a restart clears it.
* **iOS 17+** works the same way as older versions from the UI's perspective -- the app handles
  the newer tunnel-based connection Apple requires internally, without ever asking for admin/root
  privileges on any platform.

## Known limitations

* The classic (iOS < 17) location-simulation path is implemented but has not been verified against
  real pre-17 hardware while building this rewrite (see [ARCHITECTURE.md](ARCHITECTURE.md)) -- if
  you hit an issue setting a location on an older device specifically, please report it.
* Loop mode (route simulation) restarts the route from the beginning rather than reversing
  direction back to the start.
* On iOS 17+, a fixed **Set Fake Location** can slowly drift back to your real position after a
  couple of minutes of being left untouched. If that happens, click "Set Fake Location" again to
  refresh it, or use Route Planning instead (it re-applies the position continuously and doesn't
  have this issue). See [ARCHITECTURE.md](ARCHITECTURE.md) for why, and why a naive
  "auto-refresh" fix was tried and reverted (it caused worse, visibly flickering behavior).
* Some apps with dedicated anti-spoofing detection (notably Pokémon GO) may refuse to report a
  location at all even while this tool is working correctly (confirmed via Apple Maps) -- that's
  the app's own anti-cheat rejecting a detected spoofing tool, not a bug in this tool, and there
  isn't a fix for it here (see ARCHITECTURE.md). Using GPS spoofing against a game's anti-cheat
  also risks action on your account under that game's terms of service.

## Help

**My device doesn't show up on the list?**
Ensure it's plugged in, you've trusted this computer on the device, and it's visible in iTunes/
Finder. On Linux, make sure `usbmuxd` is installed and running.

**It says it can't mount the image, or some other generic error?**
Make sure your iDevice trusts this computer. A reboot of the device often resolves transient
issues.

**Building from source: "pmd3 executable not found" or device features fail immediately**
The pymobiledevice3 helper wasn't built (or wasn't built for the RID you're publishing) -- see
step 2 of [Building from source](#building-from-source). For local `dotnet run` development, make
sure `PyMobileDevice:ExecutablePathOverride` points at a working `pip install pymobiledevice3`
virtualenv instead.

## Special Thanks

* [idevicelocation by JonGabilondoAngulo](https://github.com/JonGabilondoAngulo/idevicelocation)
* [pymobiledevice3 by doronz88](https://github.com/doronz88/pymobiledevice3) for the actively
  maintained, cross-platform device-communication layer this rewrite runs on (including iOS 17+
  support the original app never had)
* [mapcn](https://www.mapcn.dev/) and [shadcn/ui](https://ui.shadcn.com/) for the frontend components
* [OSRM](https://project-osrm.org/) and [OpenStreetMap Nominatim](https://nominatim.org/) for routing/geocoding

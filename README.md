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

None of the published builds require a separately-installed .NET runtime -- they're
self-contained on every platform.

* **Windows / macOS:** iTunes (or Apple Mobile Device Support) installed, so the device shows up
  over USB.
* **macOS / Linux:** see the [OpenSSL 1.1 known limitation](#known-limitations) below --
  device-touching features currently need `libssl`/`libcrypto` 1.1 available on the system.

## Download

See the [Releases](https://github.com/master131/iFakeLocation/releases) page, or build from
source (below).

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and
[Node.js](https://nodejs.org/) (LTS or newer) on the machine doing the build -- **not** on the
end user's machine, since the frontend is compiled to a static export ahead of time.

```shell
# 1. Build the frontend (produces frontend/out/, then copies it into iFakeLocation/wwwroot/)
cd frontend
npm ci
npm run build
cd ..

# 2. Publish the backend for your platform (bundles the just-built frontend automatically)
dotnet publish iFakeLocation/iFakeLocation.csproj -c Release -p:PublishProfile=Windows-x64
dotnet publish iFakeLocation/iFakeLocation.csproj -c Release -p:PublishProfile=OSX-x64
dotnet publish iFakeLocation/iFakeLocation.csproj -c Release -p:PublishProfile=Linux-x64
```

Each profile publishes a self-contained build to `iFakeLocation/bin/publish/<platform>/`.

For local development, run the backend directly with `dotnet run --project iFakeLocation` (after
building the frontend once) and it'll serve whatever's currently in `wwwroot/`; run
`npm run dev` inside `frontend/` for a hot-reloading frontend dev server against a separately
running backend (update `frontend/lib/api-client.ts`'s base URL, or use a proxy, if the two aren't
on the same origin during development).

To run the backend's tests: `dotnet test iFakeLocation.Tests`. To run the frontend's:
`cd frontend && npm run test` (and `npm run typecheck` / `npm run lint`).

## Running

### Windows
Run `iFakeLocation.exe`.

### macOS
Open the DMG (or the published folder) and run `iFakeLocation`. See the OpenSSL note below if
device features fail to load.

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
* If it's the first time setting a location on this device, the tool needs to download some files
  to enable Developer Mode on your iDevice -- a progress dialog shows while that happens.
* Confirm the fake location using Apple Maps, Google Maps, etc. If your device is still stuck at
  the faked location after stopping, turn Location Services off and back on in Settings > Privacy.
* Your device will show a Developer menu in Settings afterward; a restart clears it.

### Manually installing developer images

If the automatic download doesn't work, create a `DeveloperImages/<iOS version>/` folder next to
the executable (e.g. `DeveloperImages/16.4/`) and place the matching `DeveloperDiskImage.dmg` +
`DeveloperDiskImage.dmg.signature` (iOS < 17) or `Image.dmg` + `BuildManifest.plist` +
`Image.dmg.trustcache` (iOS 17+) inside it. See
[xcode-developer-disk-image-all-platforms](https://github.com/haikieu/xcode-developer-disk-image-all-platforms/tree/master/DiskImages/iPhoneOS.platform/DeviceSupport)
for a source of these files.

## Known limitations

* **OpenSSL 1.1 on macOS/Linux:** the native `libimobiledevice` binary this app depends on links
  against OpenSSL 1.1, which Homebrew has removed (EOL). Until upstream ships a fixed build,
  device-touching features (device list, set/stop location, route simulation) will fail to load on
  a system without `libssl.1.1`/`libcrypto.1.1` (or the Linux equivalent) available -- obtain it
  from a third-party tap/archive and place it where the dynamic linker can find it. This is a
  pre-existing upstream issue, not specific to this rewrite -- see
  [ARCHITECTURE.md](ARCHITECTURE.md) for details.
* **iOS 17+ location simulation is unsupported**, same as the original app.
* **Apple Silicon + `dotnet run`:** `iMobileDevice-net` only ships an `x64` native macOS build (no
  `arm64`). A self-contained `osx-x64` publish works fine on M1/M2/M3 Macs (the whole process runs
  under Rosetta 2 transparently), but running the backend directly via `dotnet run` on Apple
  Silicon launches a native `arm64` process that cannot load that `x64` library at all -- same
  reason the original app's README insisted on "the x64 version, even if you have an M1/M2 Mac."
  Publish-and-run instead of `dotnet run` when testing device features on Apple Silicon.
* Loop mode restarts the route from the beginning rather than reversing direction back to the
  start.

## Help

**My device doesn't show up on the list?**
Ensure it's plugged in, you've trusted this computer on the device, and it's visible in iTunes/
Finder.

**It says it can't mount the image, or some other generic error?**
Make sure your iDevice trusts this computer. A reboot of the device often resolves transient
issues.

**"Unable to load shared library 'imobiledevice' or one of its dependencies"**
See [Known limitations](#known-limitations) above (OpenSSL 1.1). For local development via
`dotnet run` (not a published self-contained build), native assets land under
`bin/Debug/net10.0/runtimes/<rid>/native/` rather than next to the executable -- either publish
instead of `dotnet run`, or point the dynamic linker at that folder (`DYLD_LIBRARY_PATH` on macOS,
`LD_LIBRARY_PATH` on Linux).

## Special Thanks

* [idevicelocation by JonGabilondoAngulo](https://github.com/JonGabilondoAngulo/idevicelocation)
* [Xcode-iOS-Developer-Disk-Image by xushuduo](https://github.com/xushuduo/Xcode-iOS-Developer-Disk-Image/)
* [mapcn](https://www.mapcn.dev/) and [shadcn/ui](https://ui.shadcn.com/) for the frontend components
* [OSRM](https://project-osrm.org/) and [OpenStreetMap Nominatim](https://nominatim.org/) for routing/geocoding

# Architecture: iFakeLocation modernization

This document tracks what changed between the original iFakeLocation (a .NET console app with a
raw `HttpListener` + jQuery/Bootstrap/Leaflet frontend) and this rewrite (.NET 10 ASP.NET Core
Minimal API backend + Next.js/shadcn/mapcn frontend), and why. It is written incrementally as the
rewrite proceeds.

## 1. Backend: .NET 10 Minimal API

### 1.1 Framework and packaging

- Single `net10.0` target framework, replacing the original's `net48`/`net6.0` dual-targeting.
  `Sdk="Microsoft.NET.Sdk.Web"` instead of the plain SDK + hand-rolled `HttpListener`.
- Publish profiles are self-contained per-RID (`win-x64`, `osx-x64`, `linux-x64`) so end users need
  no separately-installed .NET runtime on any platform -- an improvement over the original, which
  required a system-installed .NET 6 runtime on macOS/Linux (Windows was already self-contained).
  `win-x86` and native Apple Silicon (`osx-arm64`) are not produced, matching the original's platform
  coverage.
- Dropped NuGet packages: `Newtonsoft.Json` (replaced by `System.Text.Json`, built into ASP.NET
  Core), `SharpZipLib` (replaced by `System.IO.Compression.ZipFile`, which is sufficient since the
  only requirement is reading a handful of known-named entries out of a zip), `MimeTypes` (replaced
  by ASP.NET Core's built-in static file middleware), `System.Net.Http` (built into the framework).
  Kept: `iMobileDevice-net` (native device access, see 1.4) and `plist-cil` (plist parsing).

### 1.2 Static file serving

`Resources/` was renamed to `wwwroot/` (the ASP.NET Core convention) and is now served via the
built-in `UseStaticFiles()`/`UseDefaultFiles()` middleware instead of a manual `File.Exists` +
MIME-type-lookup loop. The Next.js static export's `out/` directory is copied here at frontend
build time (see section 2).

### 1.3 Port binding and process lifecycle

- The original app manually scanned ports 49215-65535 for a free `HttpListener` binding. This is
  replaced by binding Kestrel to `127.0.0.1:0` (OS-assigned free port), read back after startup via
  `IServerAddressesFeature` -- simpler and can't exhaust a fixed range.
- `GET /exit` (a GET request with a destructive side effect, calling `Environment.Exit(0)`) is
  replaced by `POST /api/exit`, which calls `IHostApplicationLifetime.StopApplication()` for a
  graceful shutdown that still flushes the HTTP response, instead of hard-killing the process.

### 1.4 Native device access (iMobileDevice-net)

The original's P/Invoke layer talking to libimobiledevice/libplist (device enumeration, lockdown
services, image mounting, TSS/IMG4 personalization for iOS 17+) is preserved almost verbatim --
this protocol-level code was already correct and is orthogonal to the web-framework rewrite. It now
lives under `Services/{Devices,Location,Mount,Restore}/` instead of a handful of top-level files.

**Two real, pre-existing native-library-resolution problems were found and fixed while verifying
the rewrite actually runs (not just compiles) — both existed in the original app too, just
undocumented:**

1. **Version-suffixed native library names aren't resolved on macOS/Linux.** iMobileDevice-net
   ships its native binaries as `libimobiledevice-1.0.dylib`/`.so` and `libplist-2.0.dylib`/`.so`,
   but the package's own internal native-library resolver (registered lazily, the first time any
   of its P/Invoke classes is touched) only probes the *bare* `imobiledevice`/`plist` names on
   these platforms, and never finds the versioned files. We can't register a competing resolver
   for that assembly ourselves to fix this directly: .NET only allows one `DllImportResolver` per
   assembly, and the package's own registration (which always runs, unconditionally) throws
   `InvalidOperationException` if one is already set. The fix (`Infrastructure/NativeLibraryBootstrap.cs`):
   at startup, before touching any P/Invoke, create an unversioned alias copy of each known native
   library (`libimobiledevice.dylib` -> copy of `libimobiledevice-1.0.dylib`, etc.) next to the
   versioned original, so the package's own (otherwise-correct) bare-name probing succeeds. This
   runs automatically and is a no-op on Windows (whose bundled assets are already unversioned) and
   a no-op if the aliases already exist.
2. **The bundled macOS/Linux `libimobiledevice` binary itself depends on OpenSSL 1.1**, which
   Homebrew has removed entirely (EOL) -- see "Known limitations" below. This is unrelated to (1)
   and not fixable from our side at all; it's baked into the compiled native binary shipped inside
   the `iMobileDevice-net` 1.3.17 NuGet package (the latest available version -- no newer release
   fixes this). It is a documented, longstanding upstream issue
   ([libimobiledevice-win32/imobiledevice-net#200](https://github.com/libimobiledevice-win32/imobiledevice-net/issues/200)).

We evaluated switching to alternative packages: `Netimobiledevice` (a pure C# reimplementation with
no native binaries at all, which would sidestep both problems above) is younger and its coverage of
disk-image mounting, TSS/IMG4 personalization, and location simulation is unconfirmed -- given the
existing `iMobileDevice-net` integration already has all of that protocol code working and ported,
we kept it and documented the OpenSSL limitation instead of taking on a rewrite-the-device-layer
risk.

### 1.5 REST API contract (old -> new)

The old frontend is being fully replaced, so there is no backward-compatibility constraint on the
wire format -- the contract was redesigned into idiomatic REST with typed DTOs (`Contracts/*.cs`,
`System.Text.Json`) and RFC 7807 `ProblemDetails` for errors, replacing ad-hoc `{"error": "..."}`
JSON blobs.

| Old | New | Method | Notes |
|---|---|---|---|
| `GET /version` | `GET /api/version` | GET | `{ "version": "2.0.0" }` |
| `GET /home_country` | `GET /api/home-country` | GET | `{ "displayName": "..." }` |
| `GET /get_devices` | `GET /api/devices` | GET | `{ "devices": [{name, displayName, udid, isNetwork}] }` |
| `POST /set_location` | `POST /api/devices/{udid}/location` | POST | body `{lat, lng}`; `204` on success |
| `POST /stop_location` | `DELETE /api/devices/{udid}/location` | DELETE | `204` on success |
| `POST /has_dependencies` | `POST /api/devices/{udid}/dependencies/check` | POST | `{hasDependencies, iosVersion}` |
| `POST /get_progress` (plain-text body) | `GET /api/downloads/{iosVersion}/progress` | GET | identifier moved into the URL, matching GET semantics |
| `GET /exit` | `POST /api/exit` | POST | GET with a side effect -> POST (see 1.3) |
| *(new)* | `POST /api/devices/{udid}/route/start` \| `/pause` \| `/resume` \| `/stop`, `GET /route/status` | — | see section 3 |

### 1.6 Deliberate behavior changes (not regressions -- documented here per the "no silent
regressions" requirement)

- **Device lookup is always fresh, not cached.** The original's `Devices` list was only populated
  by a prior call to `/get_devices`; calling `/set_location` before ever refreshing the device list
  would incorrectly report "device not found" even for a connected device. `IDeviceService.GetDeviceOrThrowAsync`
  now always re-enumerates.
- **Stop Location now also checks the developer-mode-toggle state.** In the original, `/set_location`
  checked `GetDeveloperModeToggleState()`/`EnableDeveloperModeToggle()` before proceeding, but
  `/stop_location` did not -- an asymmetry that looks like an oversight rather than an intentional
  design choice (there's no reason stopping a simulated location should behave differently from
  starting one with respect to developer-mode gating). `ILocationSimulationService.EnsureReadyAsync`
  is now called identically by both the set and the stop/delete path.
- **Duplicate concurrent downloads for the same iOS version no longer race.** The original always
  called `state.Start()` on a freshly constructed `DownloadState` even when one already existed for
  that version (only the dictionary *entry* was deduplicated, not the download itself), wastefully
  starting a second concurrent download into the same destination files. `DownloadStateStore.GetOrStart`
  is now properly idempotent.
- **iOS 17+ location simulation remains unsupported.** The original's `DvtSimulateLocation` was
  (and still is, ported as-is for documentation purposes) an empty stub; `DeviceInformation.SetLocation`
  threw `NotImplementedException` for iOS >= 17 without ever reaching it. This rewrite preserves the
  limitation exactly, just surfaced as a clean `501` via `UnsupportedIosLocationException` instead of
  an unhandled exception.

## 2. Frontend: Next.js + shadcn/ui + Tailwind + mapcn

*(To be completed once the frontend phase lands.)*

## 3. New feature: road-network route simulation

*(To be completed once the route-simulation phase lands -- backend service design: `RouteMath`
pure interpolation, `RouteSimulationSession` per-UDID state machine, `RouteSimulationService`
orchestration reusing `ILocationSimulationService`. Frontend/routing decisions: OSRM + Nominatim
called directly from the browser, no backend proxy; status delivered via ~1s polling rather than
SSE/WebSocket.)*

## 4. Testing strategy and hardware limitations

- Pure logic (`RouteMath`, `RouteSimulationSession.AdvanceTick`, `DownloadState`/`DownloadStateStore`,
  `DeveloperImageService`'s version parsing and image-path search) has xUnit coverage in
  `iFakeLocation.Tests/`.
- **Cannot be tested without physical hardware, under any circumstances, in this environment:**
  device enumeration actually finding a real iDevice, developer-mode toggling, disk-image/personalized-image
  mounting, TSS ticket exchange with Apple's servers, and confirming a device's displayed location
  actually changes. These all sit behind interfaces (`IDeviceService`, `ILocationSimulationService`,
  `IDeveloperModeService`, `IDeveloperImageService`) so the *orchestration* logic around them is
  testable against fakes, but the real implementations can only be reviewed for protocol
  correctness, not executed end-to-end, without a connected device. This is a hard limitation of an
  AI coding agent building this in a sandboxed environment -- final on-device verification is a
  manual step for the user.

## 5. Known limitations

- **OpenSSL 1.1 dependency on macOS/Linux** (see 1.4) -- the bundled `libimobiledevice` native
  binary requires `libssl.1.1.dylib`/`libcrypto.1.1.dylib` (macOS) or the distro equivalent (Linux)
  to be present on the system. Homebrew has removed the `openssl@1.1` formula entirely. Until
  upstream ships a build linked against a current OpenSSL, affected users need to either obtain
  OpenSSL 1.1 from a third-party tap/binary and place it where the dynamic linker can find it, or
  (Linux) install their distro's legacy `libssl1.1` package if still archived. This affects the
  original app identically; it is not a regression introduced by this rewrite.
- iOS 17+ location simulation is unsupported (see 1.6) -- unchanged from the original.

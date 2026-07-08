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
- **`linux-x64` publishing needed a workaround for a RID-graph break.** `iMobileDevice-net` only
  ships native Linux binaries under the legacy, version-specific `ubuntu.16.04-x64` RID folder.
  .NET 8+ dropped that RID from its built-in graph entirely -- not just deprioritized it: the SDK
  now outright rejects `ubuntu.16.04-x64` as a `RuntimeIdentifier` (`NETSDK1083`), and a plain
  `dotnet publish -r linux-x64` silently publishes *without* any native assets at all (only a
  build-time warning, `NETSDK1206`, hints at this). `iFakeLocation.csproj` adds a small MSBuild
  target (`CopyLegacyLinuxNativeAssets`, `AfterTargets="Publish"`, conditioned on the RID starting
  with `linux`) that copies the `.so` files straight out of the resolved NuGet package cache path
  (`$(PkgiMobileDevice-net)`, via `GeneratePathProperty="true"` on the `PackageReference`) into the
  publish output. Verified end-to-end: a `linux-x64` self-contained publish correctly bundles
  `libimobiledevice-1.0.so`/`libplist-2.0.so`/etc. after this fix, whereas without it the folder
  contains none of them.
- **Publish profiles** (`Properties/PublishProfiles/`): `Windows-x64.pubxml`, `OSX-x64.pubxml`,
  `Linux-x64.pubxml` (renamed from `Ubuntu.pubxml`) -- all `net10.0`, all `SelfContained=true`.
  `Windows-x86.pubxml` was removed (dropping 32-bit support along with `net48`).

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

- **Next.js 16 (App Router), static export.** `frontend/next.config.ts` sets `output: 'export'`
  and `images.unoptimized: true` -- there is no Node.js runtime at launch, only the .NET process
  serving pre-built static files, so a server-rendering or ISR mode was never an option. The whole
  app is a single page (`app/page.tsx` renders one `AppShell` client component) rather than a
  multi-route Next.js app, matching the original's single-HTML-file structure and sidestepping
  static-export routing/rewrite concerns entirely.
- **Framework version note:** Next.js 16 ships an in-repo warning (`node_modules/next/dist/docs/`,
  surfaced via an auto-generated `AGENTS.md`/`CLAUDE.md` in `frontend/`) that its APIs have moved
  on significantly since most model training data. Those docs were consulted directly (static
  export guide, config reference) rather than relying on prior knowledge, and the generated
  `AGENTS.md`/`CLAUDE.md` files were kept as-is (accurate, low-risk, and genuinely useful to future
  contributors/agents) rather than deleted.
- **shadcn/ui on Base UI, not Radix.** The shadcn CLI's current default primitive library is Base
  UI (`@base-ui/react`), not Radix as in most existing shadcn docs/examples; component APIs differ
  in places (e.g. `Dialog`'s non-dismissible-modal pattern is `disablePointerDismissal` + omitting
  `onOpenChange`, not Radix's `onInteractOutside`/`onEscapeKeyDown`). `components/ui/**` is
  generated vendor code, installed via `npx shadcn@latest add ...` and never hand-edited (also
  excluded from `eslint` for this reason -- its lint findings aren't ours to fix).
- **mapcn**, installed via `npx shadcn@latest add @mapcn/map` (the mission's suggested literal
  `https://mapcn.dev/maps/map.json` URL is stale; the current registry shorthand is
  `@mapcn/map`, resolved through a `"@mapcn"` registry entry the CLI adds to `components.json`).
  This single component file (`components/ui/map.tsx`) bundles everything needed -- `Map`,
  `MapMarker` (draggable, drag events), `MapRoute` (GeoJSON line layer) -- so no separate
  markers/routes/controls registry items were needed.
- **Map viewport control is deliberately uncontrolled.** mapcn's `Map` supports a fully controlled
  `viewport`/`onViewportChange` pair, but this app only ever needs one-shot "fly/jump to this
  point" actions (initial home-country centering, search results) -- not continuous pan/zoom state
  sync. Driving those imperatively through the exposed `MapRef` (`mapRef.current.jumpTo(...)`/
  `.flyTo(...)`) avoided a real bug hit during verification: initializing `viewport` as `undefined`
  until the async home-country lookup resolved caused the map to construct uncontrolled, and a
  later controlled-mode sync effect could silently no-op if it raced the map's own internal
  settle/move events. The imperative-ref approach sidesteps that class of issue entirely.
- **Route-editing drag handles are placed via nearest-point snapping, not a second per-leg OSRM
  call.** `lib/route-geometry.ts`'s `computeSegmentHandles` places one draggable "insert a
  waypoint here" marker per leg (between two consecutive named waypoints), positioned at that
  leg's straight-line midpoint then snapped to the closest point on the already-fetched
  road-following polyline. This avoids doubling OSRM request volume against the ~1req/s-limited
  public demo server for what is a purely cosmetic handle-placement decision -- verified visually
  to sit convincingly on the drawn route.
- **Testing:** Vitest covers the pure logic (`lib/route-waypoints.ts` waypoint ordering/insertion,
  `lib/route-geometry.ts` nearest-point-snap and handle placement) -- run via `npm run test`.
  `npm run typecheck` (strict TypeScript, no `any`) and `npm run lint` are both clean.
- **Verified end-to-end**, not just built: the static export was served by a real `dotnet run`
  instance and driven in an actual browser (device panel, map rendering/tile-provider toggle,
  location search + Set/Stop buttons, and the full route-planning flow including dragging a
  mid-route handle to insert a via-point and watching the road-following route recalculate through
  it). This caught two real bugs before they could ship: the viewport-control issue above, and a
  Nominatim result-ordering bug (below).

## 3. New feature: road-network route simulation

### Routing and geocoding (frontend)

- **OSRM**, called directly from the browser against the public demo server
  (`router.project-osrm.org`) -- CORS-enabled, no API key, and the same ecosystem as the existing
  Nominatim geocoder. No backend proxy: simplest option, and the public instance's ~1 req/s cap is
  adequate for a single interactive user planning one route at a time. `lib/osrm-client.ts`
  documents the self-hosting escape hatch (swap the base URL, a build-time constant since a static
  export has no server to read a runtime env var from).
- The demo server hosts all three of OSRM's `car`/`foot`/`bike` profiles (not just driving, as
  initially assumed) -- `resolveOsrmProfile()` maps the chosen simulated speed to the closest
  matching profile (≤8 km/h → foot, ≤25 km/h → bike, else driving) so the drawn route follows
  paths appropriate to the pace without a separate profile selector in the UI.
- **Geocoding result ordering bug found and fixed:** Nominatim's default `/search` result order is
  *not* reliably sorted by relevance. Searching "Switzerland" for the home-country map centering
  returned "Switzerland County, Indiana" (`importance: 0.53`) ahead of the country "Suisse"
  (`importance: 0.89`) during verification, centering the map on the American Midwest instead of
  Switzerland. `lib/nominatim-client.ts`'s `geocode()` now explicitly re-sorts every result by
  Nominatim's own `importance` score before returning -- this affects every geocoding call site
  (home-country centering, the location search box, and route start/end address lookup), not just
  the one that surfaced it.
- Draggable via-point editing: `lib/route-waypoints.ts` (`insertViaPointAtSegment`) is the pure
  ordering logic; `components/map/route-layer.tsx` renders the actual drag handles and calls back
  into it. Dropping a handle recalculates the OSRM route through start → via-points (in insertion
  order) → end.

### Simulation (backend, reused by the frontend's polling)

`RouteMath` pure interpolation, `RouteSimulationSession` per-UDID state machine, and
`RouteSimulationService` orchestration reusing `ILocationSimulationService` (see section 1.5's
contract table for the `/route/*` endpoints). Status delivery is ~1s polling
(`lib/use-route-status-poll.ts`) rather than SSE/WebSocket, for consistency with the existing
download-progress polling pattern and because it needs zero extra infrastructure with a
statically-exported frontend. SSE/WebSocket push is a possible future improvement if polling
latency ever becomes a real problem, not implemented now.

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

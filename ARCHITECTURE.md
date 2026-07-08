# Architecture: iFakeLocation modernization

This document tracks what changed between the original iFakeLocation (a .NET console app with a
raw `HttpListener` + jQuery/Bootstrap/Leaflet frontend) and this rewrite (.NET 10 ASP.NET Core
Minimal API backend + Next.js/shadcn/mapcn frontend), and why. It is written incrementally as the
rewrite proceeds.

## 0. Pivot: iMobileDevice-net (P/Invoke) -> pymobiledevice3 (subprocess)

**Why.** The native `libimobiledevice` binary bundled by `iMobileDevice-net` (the last release,
1.3.17, is unmaintained since ~January 2024) links against OpenSSL 1.1 on macOS/Linux, which
Homebrew has removed entirely. There is no newer package release that fixes this, and every
mitigation available to us (unversioned-alias resolver fix, RID-graph workaround for Linux
publishing) got the app right up to that dead end and no further -- it's not something a
downstream consumer of the package can patch around. Rather than accept a real dependency on a
legacy OpenSSL build the user has to hunt down themselves, the P/Invoke layer is being replaced
entirely with [pymobiledevice3](https://github.com/doronz88/pymobiledevice3), a pure-Python,
actively maintained (weekly-ish releases), cross-platform reimplementation of the same device
protocols -- invoked as a subprocess rather than P/Invoked as a native library. As a side effect,
it also lifts the original's iOS 17+ location-simulation limitation (see the open question below).

### 0.1 What becomes obsolete

Every file that touches the native P/Invoke layer is superseded -- none of this is ported, it is
deleted outright:

- `Interop/NativeMethods.cs`, `Interop/NativeLibraryResolver.cs`, `Interop/PlistHelper.cs` --
  P/Invoke declarations, the DllImportResolver alias-file workaround, and plist marshaling. There
  is no P/Invoke left once nothing calls into `iMobileDevice-net`/`plist-cil` directly.
- `Infrastructure/NativeLibraryBootstrap.cs` -- the entire native-library-loading/alias-creation
  dance (`NativeLibraries.Load()`, the OS-specific unversioned-alias workaround) has nothing left
  to bootstrap.
- `Services/Mount/{IDeveloperModeService's mount half, DeveloperDiskImageMounter,
  PersonalizedImageMounter}.cs` -- pymobiledevice3's `mounter auto-mount` (CLI) /
  `auto_mount_developer()`/`auto_mount_personalized()` (Python API) already handles both the
  classic (pre-iOS 17) and personalized/TSS-signed (iOS 17+) mount flows internally, including
  locating or downloading the correct image. We call one command; none of the mount-protocol code
  is ours to maintain anymore.
- `Services/Restore/TSSRequest.cs` -- the Apple Tatsu Signing Server exchange for personalized
  images is handled inside pymobiledevice3's own `auto_mount_personalized()`. We never construct
  or send a TSS request ourselves.
- `Services/DeveloperImages/{IDeveloperImageService, DeveloperImageService, DownloadState,
  DownloadStateStore, DownloadStateNotFoundException}.cs` and `Options/DeveloperImageOptions.cs`
  -- the entire custom GitHub-scraping/fallback-URL-chain download system, its zip extraction, and
  its byte-level progress tracking are superseded by pymobiledevice3's own image
  resolution/download (from its own bundled/maintained image repository, not the GitHub repos the
  original scraped). **Contract consequence:** we lose byte-level download-percentage reporting --
  pymobiledevice3 doesn't expose that granularity over a CLI invocation. The
  `/dependencies/check` + `/downloads/{version}/progress` polling pair is replaced by a single
  "preparing device" state exposed while the `mounter auto-mount` subprocess is in flight (see 0.3).
- `Services/Location/{DtSimulateLocation, DvtSimulateLocation, ILocationSimulationService's DT
  implementation}.cs` -- superseded by `pymobiledevice3 developer simulate-location` (iOS < 17) and
  `pymobiledevice3 developer dvt simulate-location` (iOS >= 17, previously unsupported).
- `Services/Devices/DeviceService.cs`'s P/Invoke enumeration body -- superseded by
  `pymobiledevice3 usbmux list` / the equivalent Python API. `DeviceRecord` and
  `ProductNameCatalog` (the ProductType -> marketing-name table) are **kept**: pymobiledevice3
  returns the same raw lockdown properties (`ProductType`, `ProductVersion`, etc.), not a
  marketing name, so the lookup table is still needed.
- NuGet packages `iMobileDevice-net` and `plist-cil` are dropped entirely.

### 0.2 What is unaffected

This is the payoff of having built the backend around interfaces
(`IDeviceService`/`IDeveloperModeService`/`ILocationSimulationService`) rather than calling
P/Invoke code directly from endpoints: **`Endpoints/*.cs`, the entire `Services/RouteSimulation/`
feature (`RouteMath`, `RouteSimulationSession`, `RouteSimulationService`), `Contracts/*.cs`, and
the whole frontend are untouched.** Only the concrete service *implementations* change; the
interfaces, the REST contract, and everything that consumes them stay exactly as they are. The
route-simulation loop still calls `ILocationSimulationService.PushLocationAsync` once per tick --
it now shells out to `pymobiledevice3 developer [dvt] simulate-location set` under the hood instead
of sending DT-protocol bytes directly, but nothing above that interface boundary needs to know.

### 0.3 Integration mechanism: PyInstaller-frozen bundle, invoked as a subprocess

Three options, evaluated against the already-committed self-contained/zero-prerequisite goal:

| Option | Zero-prerequisite? | Verdict |
|---|---|---|
| Require system Python 3 + `pip install pymobiledevice3` | No -- reintroduces exactly the kind of external dependency this rewrite has been trying to eliminate | Rejected |
| Invoke a system-installed `pymobiledevice3` CLI found on PATH | No, same problem, just deferred to "if it happens to be there" | Rejected |
| **Freeze pymobiledevice3 (PyInstaller) into a standalone executable, bundled per-OS in our own publish output, invoked via `Process.Start`** | Yes -- ships inside our own self-contained package, no Python needed on the end-user machine | **Chosen** |

Implementation shape: a new `Services/PyMobileDevice/` (name TBD) wraps `Process.Start` calls to a
bundled `pmd3` executable (Windows: `pmd3.exe`, macOS/Linux: `pmd3`), one per publish RID, parsing
stdout/exit codes/JSON where available. This executable is **not** produced by `dotnet
publish`/`npm run build` -- it needs its own build step (`pip install pyinstaller pymobiledevice3`
+ a PyInstaller spec, run **on each target OS**, since PyInstaller cannot cross-compile). This adds
a third build leg alongside the .NET and Next.js ones, and realistically means a CI matrix
(GitHub Actions `windows-latest`/`macos-latest`/`ubuntu-latest`) rather than a single contributor's
machine producing all three -- flagged here explicitly since it changes "building from source" from
a two-step to a three-step (and partially CI-dependent) process; the README will be updated to
match once this lands.

**Known risk, not a solved problem:** freezing pymobiledevice3 with PyInstaller is documented as
genuinely finicky upstream (multiple open issues:
[#1038](https://github.com/doronz88/pymobiledevice3/issues/1038),
[#865](https://github.com/doronz88/pymobiledevice3/issues/865),
[#1047](https://github.com/doronz88/pymobiledevice3/issues/1047)) -- it needs explicit
`--hidden-import`/`--copy-metadata` flags for `ipsw_parser`, `zeroconf`, `pyimg4`,
`apple_compress`, `readchar`, and the Windows build specifically has an open issue with the
`pytun_pmd3`/`wintun` tunnel module. Budgeted as real engineering effort during implementation, not
assumed to be a one-line `pyinstaller main.py` away from working, especially for the Windows tunnel
path (relevant to the iOS 17+ question below).

**Licensing:** both this project and pymobiledevice3 are GPL-3.0 -- there is no license
compatibility question either way, whether pymobiledevice3 is invoked as an arm's-length subprocess
(the chosen approach, which wouldn't require GPL compatibility with our code regardless) or
hypothetically vendored more tightly.

### 0.4 iOS 17+ support: resolved via `--userspace`, no elevation needed

Initial concern: iOS 17+ moved developer-service access to Apple's RemoteXPC/CoreDevice transport,
which pymobiledevice3 normally reaches through a *tunnel*, and a standard kernel-level tunnel
(`pymobiledevice3 remote tunneld` / `start-tunnel`) needs a TUN/TAP virtual network interface --
admin/sudo on every OS. That would have meant either a real elevation-prompt implementation (UAC/
AppleScript-admin/pkexec) with a genuine trust/security posture change for a GPS-spoofing tool, or
punting the whole feature.

**Superseded by a better option found during CLI inspection:** every command that needs an iOS 17+
tunnel (`mounter auto-mount`, `developer dvt simulate-location {set,clear,play}`, etc.) accepts a
`--userspace` flag that establishes the tunnel **in-process, via a pure-Python userspace network
stack, requiring no root/admin privileges at all**. The tradeoff is throughput: host->device
transfers (mounting the DDI, pushing files) run slower than a kernel tunnel, since send segments
are kept small for reliability. This is irrelevant for our use: the DDI mount happens once per
device per session (a one-time, acceptable slowdown), and `simulate-location set` sends a handful
of bytes per call -- there is no meaningful per-tick cost during route simulation.

**Decided:** always pass `--userspace` for iOS 17+ operations. No `TunneldService`, no per-OS
elevation code, no admin prompt shown to the user, ever. Pre-17 devices are unaffected either way
(no tunnel involved at all).

**Verified live against a real, connected iOS 26 device** (with the user's explicit permission,
restoring the real location with `clear` immediately after each test):

- `developer dvt simulate-location clear --userspace` reliably completes end-to-end (tunnel setup
  + DVT connect + command + clean process exit) in **~2 seconds**.
- `developer dvt simulate-location set --userspace` reaches success internally just as fast --
  verbose logs show userspace RSD established, DVT capabilities exchanged, and the
  `simulateLocationWithLatitude:longitude:` call answered `OK`, all within the same second -- but
  the CLI process then **hangs indefinitely afterward instead of exiting** (confirmed reproducible,
  not a one-off).
- A mid-command USB disconnect fails fast and cleanly (`"Device is not connected"`, no hang) rather
  than needing a timeout to detect -- good, maps directly to our existing `DeviceNotFoundException`.

**Correction (found later, via real usage rather than protocol testing):** the first pass of this
rewrite concluded from the above that killing the hung `set` process was safe -- Apple Maps kept
showing the faked position afterward, which looked like confirmation that the location "stays
pinned" once set, independent of the process. **That conclusion was wrong**, and the mistake is
instructive: Maps' blue dot doesn't necessarily re-poll CoreLocation continuously, so it can go on
showing a stale cached fix indefinitely. Pokemon GO does poll continuously, and reported "Impossible
de détecter ton emplacement" / "Signal GPS introuvable" seconds after a `set` that had reported
success -- i.e. the device really had reverted to its real GPS (which had no fix, being indoors).

Reading pymobiledevice3's own source resolved it for certain
(`cli/developer/dvt/simulate_location.py`): the CLI's `set` command deliberately calls
`OSUTILS.wait_return()` after applying the location, which on Linux/macOS blocks on
`signal.sigwait([SIGINT, SIGTERM])` forever -- i.e. the process is *designed* to stay alive
indefinitely, precisely because it holds open the DTX/Instruments channel that keeps the simulated
location in effect (`clear`'s `stop_location_simulation` is a separate, explicit call on that same
kind of channel). This mirrors Xcode's own "Simulate Location" feature exactly: it too only lasts as
long as the debug session holding that channel is attached. Killing the process the instant we see
success tears the channel down, and the device reverts to real GPS within moments -- invisible to a
one-off read, very visible to anything polling continuously (a mobile game's anti-spoofing checks
being an extreme example of the latter).

**Design conclusion (revised):** `PushLocationAsync`'s DVT `set` path (`LocationSimulationService.cs`)
must keep the subprocess running after success is detected, not kill it --
`IPyMobileDeviceRunner.StartPersistentAsync` tracks one live process per device UDID
(`PyMobileDeviceRunner`'s `persistentProcesses` dictionary), replacing (kill-then-restart) on a new
`set` for the same device, and killed explicitly by `StopPersistent` on `clear`/stop, or by
`PyMobileDeviceRunner.Dispose()` on app shutdown (so a killed/crashed app doesn't strand a phone
mid-fake-location with no process left to `clear` it). The classic iOS<17 `developer
simulate-location set` path is unaffected: its CLI command exits cleanly on success (no
`wait_return()` in its source), and the lockdown-based simulation is a persistent server-side toggle
that doesn't depend on any connection staying open -- confirmed by reading its source, not verified
live (no pre-17 hardware available; see limitations).

One consequence worth calling out: `RouteSimulationService`'s per-tick `PushLocationAsync` calls
still replace the persistent process every tick on iOS 17+ (there's no protocol to push a new
coordinate through an already-open channel) -- each tick briefly closes the old channel and reopens
a new one, rather than truly holding one continuous channel open for an entire route. This is
strictly better than the pre-fix behavior (which reverted to real GPS *between* ticks too, just
less visibly), but a genuinely persistent single channel across a whole route -- e.g. driving the
device via pymobiledevice3's own `developer dvt simulate-location play <gpx-file>`, which replays a
whole route through one long-lived channel -- remains a plausible future optimization, not
implemented here.

**Known remaining limitation, confirmed live: a fixed Set can still slowly drift back to the real
GPS fix after roughly a couple of minutes,** even with the channel held open per the fix above.
Root cause not fully pinned down (possibly CoreLocation itself expects some form of periodic
re-affirmation of a simulated fix, distinct from the DTX channel merely staying open) -- flagged
here rather than guessed at further without more evidence.

**Keep-alive re-push: tried and reverted.** The natural fix for the drift above is to periodically
re-send the same coordinate rather than pushing once and leaving the channel idle. Implemented as a
per-UDID background loop (`PeriodicTimer`, initially a 10s interval) re-invoking the same `set` path
on a timer, serialized through a per-device `SemaphoreSlim` (`LocationSimulationService.deviceLocks`)
so it could never overlap a manual Set/Stop or a route-simulation tick. **Confirmed live this made
things worse, not better:** every refresh has to close the existing channel and open a new one (the
CLI's `set` has no protocol for pushing an updated coordinate into an already-open session -- see
above), and during that ~2-4s gap the device has *no* simulated location at all and snaps back to
real GPS, then flips back once the new channel succeeds. Observed on the device as continuous,
visible oscillation ("going back and forth") every refresh cycle -- strictly worse UX than the slow
one-time drift it was meant to fix. **Rolled back** (see git history); `PushLocationAsync` now only
pushes once per Set call, same as documented above, with the drift limitation stated plainly rather
than papered over with a worse workaround.

The `SemaphoreSlim`-based per-UDID serialization introduced for that attempt was kept even after the
revert -- it's independently valuable: overlapping DVT/userspace-tunnel setups for the same device
were observed to genuinely fight each other (single calls stretching from ~2s to 25-57s) rather than
politely queue, which is worth preventing regardless of the keep-alive question (e.g. a user
double-clicking "Set Fake Location", or a manual Set racing a route-simulation tick).

A gap-free fix remains a plausible future direction: encode a fixed point as a single-point (or
long-duration) GPX route and drive it through pymobiledevice3's own
`developer dvt simulate-location play <gpx-file>`, which holds one continuous channel open for the
whole duration rather than restarting per refresh -- not attempted here (would need real
investigation of `play_gpx_file`'s timing/looping semantics before trusting it live against a
device), left as-is rather than implemented speculatively.

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
  Core), `SharpZipLib` (replaced by `System.IO.Compression.ZipFile`), `MimeTypes` (replaced by
  ASP.NET Core's built-in static file middleware), `System.Net.Http` (built into the framework),
  and -- as of the pivot in section 0 -- `iMobileDevice-net` and `plist-cil` (native device access
  is now a subprocess call to a bundled pymobiledevice3 executable; see 1.4). The backend has zero
  P/Invoke and zero `AllowUnsafeBlocks` left.
- **Publish profiles** (`Properties/PublishProfiles/`): `Windows-x64.pubxml`, `OSX-x64.pubxml`,
  `Linux-x64.pubxml` (renamed from `Ubuntu.pubxml`) -- all `net10.0`, all `SelfContained=true`.
  `Windows-x86.pubxml` was removed (dropping 32-bit support along with `net48`). The earlier RID-
  graph workaround needed for `iMobileDevice-net`'s Linux native assets (a legacy `ubuntu.16.04-x64`
  RID folder that .NET 8+'s RID graph no longer resolves for a `linux-x64` publish) no longer
  applies now that package is gone -- pymobiledevice3 is pure Python, frozen once per OS/arch by
  PyInstaller (see 1.4), with no RID-graph involvement at all.

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

### 1.4 Native device access (pymobiledevice3 subprocess, superseding iMobileDevice-net)

**This whole section describes the architecture *after* the pivot documented in section 0.** The
original plan (and this rewrite's first pass) kept `iMobileDevice-net`'s P/Invoke layer, ported
near-verbatim into `Services/{Devices,Location,Mount,Restore}/`. That layer hit a genuine dead end
(OpenSSL 1.1, removed from Homebrew, no fixed package release coming -- see section 0) and was
replaced entirely. `Services/Mount/`, `Services/Restore/`, `Services/DeveloperImages/`,
`Interop/`, and `Infrastructure/NativeLibraryBootstrap.cs` are deleted outright, not archived; the
P/Invoke-resolution bugs that section previously documented (unversioned-alias workarounds, RID-
graph issues) are moot since there is no P/Invoke left at all.

**Current architecture:**

- `Services/PyMobileDevice/`: `IPyMobileDeviceRunner`/`PyMobileDeviceRunner` wraps `Process.Start`
  against a bundled `pmd3`/`pmd3.exe` executable (a PyInstaller-frozen pymobiledevice3 CLI --
  see below). Two call shapes: `RunAsync` for commands that exit on their own (device queries,
  mounting, `clear`/stop), and `RunFireAndForgetAsync` for `simulate-location set`, which -- verified
  live against a real device -- reaches success internally in ~1-2s but then **does not exit on its
  own**; the runner detects success from a DVT response-line marker (or a clean exit, for the non-
  DVT iOS<17 path) and kills the process itself once detected, rather than waiting for it to end.
- `Services/DeveloperMode/{IDeveloperModeService,DeveloperModeService}`: developer-mode-toggle
  query/reveal (`amfi developer-mode-status`/`reveal-developer-mode`) and mounting
  (`mounter auto-mount`, with `--userspace` for iOS 17+ -- see section 0.4) -- one CLI call each,
  replacing the original's few-hundred-line native mount-protocol/TSS implementation, since
  pymobiledevice3 resolves, downloads, and mounts the correct image (classic or personalized)
  internally.
- `Services/Devices/DeviceService`: `usbmux list` returns clean JSON on stdout (verified: `-v`
  verbose logging goes to stderr, never pollutes stdout) mapping directly to `DeviceRecord`.
  `ProductNameCatalog` (ProductType -> marketing name) is unchanged -- pymobiledevice3 returns the
  same raw `ProductType` string, not a friendly name.
  **Also fixed in passing:** the original's `Devices` list was only ever populated by a prior
  `/get_devices` call; `GetDeviceOrThrowAsync` always re-enumerates fresh instead of trusting a
  cache that might never have been populated.
- `Services/Location/LocationSimulationService`: dispatches to `developer simulate-location`
  (iOS < 17) or `developer dvt simulate-location` (iOS >= 17, `--userspace`), both against the same
  `PushLocationAsync`/`EnsureReadyAsync` interface `RouteSimulationService` already depended on --
  **no changes needed above the interface boundary** (see 0.2).

**Packaging (`pymobiledevice3-build/`):** `build.sh`/`build.ps1` freeze pymobiledevice3 (pinned
version, `requirements.txt`) via PyInstaller into `iFakeLocation/pmd3-dist/<rid>/pmd3[.exe]`, which
`iFakeLocation.csproj` copies into the publish output under `pmd3/` for the RID being built (a
no-op before that build has run for a given RID). This needs real, non-default PyInstaller flags
(`--copy-metadata pymobiledevice3 --copy-metadata ipsw-parser --copy-metadata readchar
--hidden-import ipsw_parser --hidden-import pyimg4 --hidden-import apple_compress
--hidden-import readchar --collect-submodules pymobiledevice3`) -- discovered by iterating against
real failures (`PackageNotFoundError` for `readchar`'s self-version-check, missing hidden imports
for `ipsw_parser`/`pyimg4`/`apple_compress`), not assembled from guesswork. **Verified end-to-end**:
the frozen binary was built and run against a real, connected device (macOS arm64 in this
environment; the RID label passed to the build script does not itself cross-compile -- see the
scripts' own comments) for `usbmux list` and `amfi developer-mode-status`, both succeeding.
PyInstaller cannot cross-compile, so `win-x64`/`linux-x64` builds must run on a matching OS/arch
(a CI matrix, realistically) -- this is a real, budgeted engineering cost, not a solved problem;
Windows specifically has an open upstream issue with the `pytun_pmd3`/`wintun` tunnel module
([doronz88/pymobiledevice3#1047](https://github.com/doronz88/pymobiledevice3/issues/1047)) that
should be re-checked when a Windows build is actually produced.

**Licensing:** both this project and pymobiledevice3 are GPL-3.0 -- no compatibility question
either way, whether invoked as an arm's-length subprocess (chosen here, and wouldn't require GPL
compatibility with our code regardless) or vendored more tightly.

We evaluated `Netimobiledevice` (a pure C# reimplementation, which would have avoided a Python
runtime/subprocess dependency entirely) but its coverage of disk-image mounting, TSS/IMG4
personalization, and location simulation was unconfirmed against pymobiledevice3's proven, actively
maintained implementation of exactly those flows -- see section 0 for the full evaluation.

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
| `POST /set_location` | `POST /api/devices/{udid}/location` | POST | body `{lat, lng}`; `204` on success; gates on device readiness internally (see below) |
| `POST /stop_location` | `DELETE /api/devices/{udid}/location` | DELETE | `204` on success |
| `GET /exit` | `POST /api/exit` | POST | GET with a side effect -> POST (see 1.3) |
| *(new)* | `POST /api/devices/{udid}/route/start` \| `/pause` \| `/resume` \| `/stop`, `GET /route/status` | — | see section 3 |

**`POST /has_dependencies` and `POST /get_progress` (plain-text body) have no replacement --
removed, not renamed.** Every location/route endpoint already calls
`ILocationSimulationService.EnsureReadyAsync` (developer-mode-toggle check + image mount)
internally before acting, exactly as before; the only thing that changed is *how* that readiness
check is implemented (one `mounter auto-mount` subprocess call instead of our own GitHub-scraping
download chain). Since pymobiledevice3 doesn't expose byte-level mount/download progress over a
CLI invocation either way, there was nothing left for a separate check-then-poll endpoint pair to
add -- the frontend just shows an indeterminate "Preparing device..." dialog for the duration of
the single `location`/`route/start` call instead of polling a percentage. Simpler to maintain, and
not a capability regression: the original's progress bar was cosmetic in the sense that it couldn't
be interacted with or cancelled either.

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
- **iOS 17+ location simulation is now supported** -- a genuine capability improvement, not just a
  rewrite. The original (and this rewrite's first, `iMobileDevice-net`-based pass) left
  `DvtSimulateLocation` as an empty stub and threw `NotImplementedException`/a typed 501 for
  iOS >= 17 instead. The pymobiledevice3 pivot (section 0) lifts this limitation entirely, verified
  live against a real iOS 26 device, with no admin/sudo privileges required (`--userspace`, section
  0.4). The DT (iOS < 17) path could not be verified against real hardware in this environment (no
  device available) -- see section 4's testing-limitations note.

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

- Pure logic (`RouteMath`, `RouteSimulationSession.AdvanceTick`, `lib/route-waypoints.ts`,
  `lib/route-geometry.ts`) has unit test coverage (`iFakeLocation.Tests/`, frontend Vitest).
- **Unusually for this kind of project, a real, connected iOS device was available in this
  environment for parts of the pymobiledevice3 pivot (with the user's explicit permission for each
  mutating action, always restored to the real GPS location afterward).** This let several things
  actually be verified end-to-end rather than just reviewed for protocol correctness: device
  listing, developer-mode-status query, `mounter auto-mount`, `simulate-location set`/`clear`
  (both directly via the CLI and through the full HTTP API against a running, published build), and
  a live route simulation with progressing status. This is *not* the normal state of affairs and
  should not be assumed for future changes.
- **Still cannot be verified without matching hardware/OS, even with a device available:** the
  classic DT (iOS < 17) `simulate-location` path (the connected test device was iOS 26) -- the
  runner's success-detection logic falls back to "clean process exit" for that path (see 1.4),
  which is a reasonable inference from the protocol's design but unconfirmed; the `win-x64`/
  `linux-x64` PyInstaller-frozen builds (this environment is macOS arm64 only, and PyInstaller
  cannot cross-compile -- see 1.4); and the Windows tunnel module issue
  ([doronz88/pymobiledevice3#1047](https://github.com/doronz88/pymobiledevice3/issues/1047)) that
  may affect iOS 17+ support specifically on Windows. All of the pymobiledevice3-touching logic
  sits behind `IDeviceService`/`IDeveloperModeService`/`ILocationSimulationService`, so the
  *orchestration* logic is testable against fakes regardless -- but final verification of anything
  requiring hardware or a specific OS this environment doesn't have remains a manual step for the
  user.

## 5. Known limitations

- **iOS 17+ location simulation is now supported** (see 0.4, 1.4, 1.6) -- this is the one
  limitation from the original app this rewrite actually lifts, not just documents.
- The classic DT (iOS < 17) `simulate-location set` success-detection path is implemented but
  unverified against real hardware in this environment (see section 4).
- `win-x64`/`linux-x64` `pmd3` builds must be produced on a matching OS/architecture (realistically
  a CI matrix) -- PyInstaller cannot cross-compile; only the macOS arm64 build path was actually
  exercised here (see 1.4).
- Loop mode (route simulation) restarts from the beginning rather than reversing direction back to
  the start -- a documented simplification, not a bug.

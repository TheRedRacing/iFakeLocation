# Freezes pymobiledevice3 into a standalone "pmd3.exe" via PyInstaller and drops it into
# iFakeLocation/pmd3-dist/<rid>/, where the backend's csproj picks it up for that RID's publish.
#
# IMPORTANT: PyInstaller cannot cross-compile -- this must run on Windows x64 to produce our
# chosen win-x64 RID (see ARCHITECTURE.md). It also has a documented open issue with the
# pytun_pmd3/wintun tunnel module (github.com/doronz88/pymobiledevice3/issues/1047) -- if the
# frozen exe fails specifically on iOS 17+ tunnel-requiring commands (mounter auto-mount,
# developer dvt simulate-location) despite working for plain queries (usbmux list, version),
# that issue is the first thing to check.
#
# Usage: .\build.ps1 <rid>   (e.g. .\build.ps1 win-x64)

param(
    [Parameter(Mandatory = $true)]
    [string]$Rid
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$OutDir = Join-Path $ScriptDir "..\iFakeLocation\pmd3-dist\$Rid"
$VenvDir = Join-Path $ScriptDir ".venv-$Rid"
$BuildDir = Join-Path $ScriptDir ".build-$Rid"

Write-Host "Building pmd3.exe for RID=$Rid using $(python --version)"

python -m venv $VenvDir
& "$VenvDir\Scripts\pip.exe" install -q --upgrade pip
& "$VenvDir\Scripts\pip.exe" install -q -r (Join-Path $ScriptDir "requirements.txt")

Remove-Item -Recurse -Force $BuildDir -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force $OutDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

& "$VenvDir\Scripts\pyinstaller.exe" `
  --onefile `
  --name pmd3 `
  --distpath $OutDir `
  --workpath (Join-Path $BuildDir "build") `
  --specpath $BuildDir `
  --copy-metadata pymobiledevice3 `
  --copy-metadata ipsw-parser `
  --copy-metadata readchar `
  --hidden-import ipsw_parser `
  --hidden-import pyimg4 `
  --hidden-import apple_compress `
  --hidden-import readchar `
  --collect-submodules pymobiledevice3 `
  (Join-Path $ScriptDir "entry.py")

Write-Host "Built: $OutDir\pmd3.exe"
& "$OutDir\pmd3.exe" version

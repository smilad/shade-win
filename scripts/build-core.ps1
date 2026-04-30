# Build shade-core.exe (PyInstaller onefile) from the upstream Python sources.
# Run from a Windows PowerShell with Python 3.10+ on PATH.
#
#   .\scripts\build-core.ps1
#   .\scripts\build-core.ps1 -UpstreamRoot D:\src\shade-upstream
#
# Output: windows-app\dist\shade-core.exe
#         windows-app\src\Shade\Resources\shade-core.exe   (copied for app bundling)
#
# By default expects the upstream g3ntrix/Shade sources at ..\shade_macos
# (the sibling layout used in this monorepo). Override with -UpstreamRoot
# in CI where the upstream repo is cloned to a different path.

param(
    [string]$Python = "python",
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")),
    [string]$UpstreamRoot = ""
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path $RepoRoot

# Resolve upstream root: explicit > sibling shade_macos (monorepo) > sibling shade-upstream (CI)
if ([string]::IsNullOrEmpty($UpstreamRoot)) {
    $candidates = @(
        (Join-Path $root.Path "..\shade_macos"),
        (Join-Path $root.Path "..\shade-upstream"),
        (Join-Path $root.Path "..\Shade")
    )
    foreach ($c in $candidates) {
        if (Test-Path (Join-Path $c "src\proxy_server.py")) { $UpstreamRoot = (Resolve-Path $c).Path; break }
    }
}
if ([string]::IsNullOrEmpty($UpstreamRoot)) {
    throw "Could not locate the upstream g3ntrix/Shade Python sources. Pass -UpstreamRoot <path>."
}

$upstreamSrc = Join-Path $UpstreamRoot "src"
$upstreamMain = Join-Path $UpstreamRoot "main.py"
$bootstrap   = Join-Path $root.Path "core\shade_core.py"
$dist        = Join-Path $root.Path "dist"
$bundleDest  = Join-Path $root.Path "src\Shade\Resources"

Write-Host "==> upstream root : $UpstreamRoot"
Write-Host "==> upstream src  : $upstreamSrc"
Write-Host "==> bootstrap     : $bootstrap"

foreach ($p in @($upstreamSrc, $upstreamMain, $bootstrap)) {
    if (!(Test-Path $p)) { throw "Missing: $p" }
}

# Stage a build dir with shade_core.py + main.py + src/* together so PyInstaller
# can find both `from main import main` and `from mitm import CA_CERT_FILE`.
$stage = Join-Path $root.Path "build\stage"
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

Copy-Item $upstreamMain (Join-Path $stage "main.py")
Copy-Item $bootstrap    (Join-Path $stage "shade_core.py")
Copy-Item $upstreamSrc  -Destination (Join-Path $stage "src") -Recurse

# Install build deps. PyInstaller is intentionally NOT in this list — CI
# may have installed a source-built version (with a fresh bootloader, to
# reduce Windows Defender / SmartScreen false positives), and reinstalling
# from PyPI here would overwrite it with the heavily-fingerprinted
# prebuilt bootloader. Only fall back to PyPI if PyInstaller isn't there.
& $Python -m pip install --upgrade pip cryptography h2 certifi brotli zstandard
& $Python -c "import PyInstaller" 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "==> PyInstaller not installed — pulling from PyPI."
    & $Python -m pip install pyinstaller
} else {
    Write-Host "==> PyInstaller already installed (likely source-built, keeping it)."
}

Push-Location $stage
try {
    & $Python -m PyInstaller `
        --onefile `
        --name shade-core `
        --paths "src" `
        --add-data "src;src" `
        --collect-submodules cryptography `
        --collect-submodules h2 `
        shade_core.py

    if (!(Test-Path "dist\shade-core.exe")) { throw "PyInstaller failed: dist\shade-core.exe missing" }

    if (Test-Path $dist) { Remove-Item -Recurse -Force $dist }
    Move-Item dist $dist

    if (!(Test-Path $bundleDest)) { New-Item -ItemType Directory -Force -Path $bundleDest | Out-Null }
    Copy-Item (Join-Path $dist "shade-core.exe") (Join-Path $bundleDest "shade-core.exe") -Force

    Write-Host "==> Built: $($dist)\shade-core.exe"
    Write-Host "==> Copied to bundle: $bundleDest\shade-core.exe"
}
finally {
    Pop-Location
}

# Build shade-core.exe (PyInstaller onefile) from the upstream Python sources.
# Run from a Windows PowerShell with Python 3.10+ on PATH.
#
#   .\scripts\build-core.ps1
#
# Output: windows-app\dist\shade-core.exe
#         windows-app\src\Shade\Resources\shade-core.exe   (copied for app bundling)

param(
    [string]$Python = "python",
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot ".."))
)

$ErrorActionPreference = "Stop"

$root        = Resolve-Path $RepoRoot
$upstream    = Join-Path $root.Path ".."  | Resolve-Path        # shade-windows root
$srcDir      = Join-Path $upstream.Path "shade_macos\src"
$mainPy      = Join-Path $upstream.Path "shade_macos\main.py"
$bootstrap   = Join-Path $root.Path     "core\shade_core.py"
$dist        = Join-Path $root.Path     "dist"
$bundleDest  = Join-Path $root.Path     "src\Shade\Resources"

Write-Host "==> upstream src: $srcDir"
Write-Host "==> bootstrap   : $bootstrap"

# Sanity checks
foreach ($p in @($srcDir, $mainPy, $bootstrap)) {
    if (!(Test-Path $p)) { throw "Missing: $p" }
}

# Stage a build dir with shade_core.py + main.py + src/* together so PyInstaller
# can find both `from main import main` and `from mitm import CA_CERT_FILE`.
$stage = Join-Path $root.Path "build\stage"
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

Copy-Item $mainPy     (Join-Path $stage "main.py")
Copy-Item $bootstrap  (Join-Path $stage "shade_core.py")
Copy-Item $srcDir     -Destination (Join-Path $stage "src") -Recurse

# Install build deps
& $Python -m pip install --upgrade pip pyinstaller cryptography h2 certifi brotli zstandard

Push-Location $stage
try {
    # PyInstaller: onefile, include `src/` so the upstream flat imports work.
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

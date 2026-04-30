# Build the Windows Shade.exe app.
#
# Prereqs (Windows):
#   - .NET 8 SDK
#   - Already ran scripts\build-core.ps1 once so shade-core.exe is bundled
#
# Output:
#   windows-app\publish\Shade.exe
#   windows-app\publish\shade-core.exe   (copied next to Shade.exe)

param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$root      = Resolve-Path (Join-Path $PSScriptRoot "..")
$proj      = Join-Path $root.Path "src\Shade\Shade.csproj"
$publish   = Join-Path $root.Path "publish"
$coreSrc   = Join-Path $root.Path "src\Shade\Resources\shade-core.exe"

if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
New-Item -ItemType Directory -Path $publish | Out-Null

Write-Host "==> Restoring..."
dotnet restore $proj

Write-Host "==> Publishing $Configuration $Runtime..."
dotnet publish $proj `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publish

# Copy bundled core next to Shade.exe (in case PublishSingleFile didn't include it)
if (Test-Path $coreSrc) {
    Copy-Item $coreSrc (Join-Path $publish "shade-core.exe") -Force
    Write-Host "==> shade-core.exe copied to $publish"
} else {
    Write-Warning "shade-core.exe missing at $coreSrc — run scripts\build-core.ps1 first."
}

Write-Host "==> Done. App at: $(Join-Path $publish 'Shade.exe')"

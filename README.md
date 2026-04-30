# Shade for Windows

A Windows port of [g3ntrix/Shade](https://github.com/g3ntrix/Shade) — itself
a fork of [masterking32/MasterHttpRelayVPN](https://github.com/masterking32/MasterHttpRelayVPN).

The macOS app's design (dark gradient, glass cards, sidebar + 5 tabs, system-tray
popover, status orb, power button, live speed meter, profile picker, IP scanner,
setup wizard) is mirrored here in WPF on .NET 8. The Python core is reused
unchanged — it already supports Windows.

## What's in this folder

```
windows-app/
├── src/Shade/                  WPF .NET 8 app (Shade.exe)
│   ├── Models/                 AppSettings, AppState, Credential, LogLine
│   ├── Services/               ConfigStore, CoreManager, SystemProxy, CertManager, PortAvailability
│   ├── Views/                  Sidebar, Dashboard, Setup, Settings, Logs, About, TrayPopup, Credential dialogs
│   ├── Themes/Generic.xaml     Dark theme + glass card + brand brushes (purple accent)
│   ├── Resources/              Icons + bundled shade-core.exe
│   └── Shade.csproj
├── core/shade_core.py          Bootstrap wrapper (PyInstaller entry)
├── scripts/
│   ├── build-core.ps1          Builds shade-core.exe via PyInstaller
│   └── build-app.ps1           Builds Shade.exe via dotnet publish
├── installer/Shade.iss         Inno Setup installer script
└── README.md
```

## Download

Pre-built `.exe` files for each release are on the
[Releases page](https://github.com/smilad/shade-win/releases):

- **`ShadeSetup.exe`** — installer (recommended).
- **`Shade.exe`** — portable single-file build, no install needed.

CI builds them on every tag push (see `.github/workflows/release.yml`).

## Cut a release

```powershell
git tag v1.1.1
git push origin v1.1.1
```

GitHub Actions builds the core, the app, and the installer, then publishes
them on the Releases page. Manual runs (workflow_dispatch) build but do not
release — artifacts are attached to the run page instead.

## Build locally

Run on a Windows machine. You need:

- **Python 3.10+** with pip (only required for the one-time core build).
- **.NET 8 SDK** for the app itself.
- **Inno Setup 6+** if you want the installer (optional).

Two-step build:

```powershell
# 1. Build the bundled Python core (shade-core.exe). Run once,
#    or re-run when the upstream Python sources change.
.\scripts\build-core.ps1

# 2. Build the .NET app. Produces publish\Shade.exe + shade-core.exe.
.\scripts\build-app.ps1

# 3. (Optional) Build a setup installer.
iscc installer\Shade.iss     # → installer\Output\ShadeSetup.exe
```

## Run

After building, launch `publish\Shade.exe` (or install via `ShadeSetup.exe`).

First run:
1. **Setup Guide** tab walks you through deploying `Code.gs` to Google
   Apps Script and grabbing a Deployment ID.
2. Paste your Deployment ID and the matching `AUTH_KEY` into a profile on
   the Dashboard (➕ button).
3. Hit **START**. The MITM root certificate auto-installs to the per-user
   Trusted Root store via `certutil` — no UAC prompt.
4. Toggle **Set as system proxy** to push HTTP/HTTPS traffic system-wide.

## System proxy: scope and limitations

The toggle wires the **WinINet HTTP proxy** under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings`, then
broadcasts `INTERNET_OPTION_SETTINGS_CHANGED` so listening apps pick it up
immediately. Apps that respect it: Edge, Chrome, IE, Office, .NET
`HttpClient` / `WebRequest`, most installers.

Apps that **don't**: Firefox (uses its own proxy config) and any tool that
doesn't read WinINet. SOCKS5-only apps need to point at
`127.0.0.1:<socksPort>` manually (shown on the Dashboard's listener row).

True system-wide SOCKS5 routing requires a TUN device (wintun + tun2socks).
That's a future enhancement.

## Where things live at runtime

- `%APPDATA%\Shade\settings.json` — UI settings, credentials.
- `%APPDATA%\Shade\config.json`   — the JSON the Python core consumes.
- `%APPDATA%\Shade\ca\ca.crt`     — generated MITM root CA.

## Reusing the Python core

Everything under `..\shade_macos\src\` is shared verbatim. The Windows
bootstrap (`core/shade_core.py`) only differs from the macOS one in the
`%APPDATA%\Shade` path it points at.

Cert installation is handled by the upstream Python core's
`src/cert_installer.py`, which already supports Windows
(`certutil -addstore -user Root` — no admin needed). The C# `CertManager`
is a 30-line shim that just runs `shade-core.exe --install-cert`.

## Differences from the macOS app

- **System proxy** is HTTP-only, not SOCKS5 (Windows architectural reality).
- **No TUN mode** (the macOS app's TUN code was already deprecated upstream).
- Some flourish animations from the SwiftUI views (ClusterPulse, the LB
  strategy picker's animated swap) are not 1:1 — the layout, components,
  colors, and behavior are.

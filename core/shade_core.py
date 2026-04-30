#!/usr/bin/env python3
"""
Bootstrap wrapper for Shade.exe (Windows).

PyInstaller freezes this file into `shade-core.exe`. It re-points the MITM
CA directory to a writable spot under %APPDATA%\\Shade, fixes the SSL CA
bundle path so a frozen binary can verify TLS, and hands off to main.main().

The patching has to happen BEFORE main is imported, because main.py does
`from mitm import CA_CERT_FILE` which captures the constant at import time.
"""

import os
import sys


def _resource_dir() -> str:
    meipass = getattr(sys, "_MEIPASS", None)
    if meipass:
        return meipass
    return os.path.dirname(os.path.abspath(__file__))


def _writable_app_dir() -> str:
    base = os.environ.get("APPDATA") or os.path.expanduser("~")
    path = os.path.join(base, "Shade")
    os.makedirs(path, exist_ok=True)
    return path


def _bootstrap_ssl_ca_bundle() -> None:
    """Make the frozen exe trust the same CA bundle Python ships with.

    On Windows the system cert store is normally fine, but a frozen
    PyInstaller binary that imports `ssl` may fall back to a cafile
    path that doesn't exist on the user's machine. Setting SSL_CERT_FILE
    forces ssl.create_default_context() to use a known-good bundle.

    On Windows we default to the system store (no certifi pin) because
    requirements.txt skips certifi on win32; if it IS available we use it.
    """
    try:
        import certifi
        cafile = certifi.where()
        if cafile and os.path.exists(cafile):
            os.environ.setdefault("SSL_CERT_FILE", cafile)
            os.environ.setdefault("SSL_CERT_DIR", os.path.dirname(cafile))
            os.environ.setdefault("REQUESTS_CA_BUNDLE", cafile)
            print(f"[bootstrap] SSL CA bundle: {cafile}", flush=True)
            return
    except Exception:
        pass
    print("[bootstrap] using system CA store", flush=True)


def _patch_ca_paths() -> None:
    """Redirect mitm.CA_DIR (and friends) into our writable app-data dir."""
    app_dir = _writable_app_dir()
    ca_dir = os.path.join(app_dir, "ca")
    print(f"[bootstrap] ensuring ca_dir exists: {ca_dir}", flush=True)
    os.makedirs(ca_dir, exist_ok=True)

    res = _resource_dir()
    if res not in sys.path:
        sys.path.insert(0, res)

    print("[bootstrap] importing mitm module...", flush=True)
    import mitm
    mitm.CA_DIR = ca_dir
    mitm.CA_KEY_FILE = os.path.join(ca_dir, "ca.key")
    mitm.CA_CERT_FILE = os.path.join(ca_dir, "ca.crt")
    print("[bootstrap] ca paths patched", flush=True)


def main() -> None:
    print("[bootstrap] shade-core starting...", flush=True)
    try:
        app_dir = _writable_app_dir()
        os.chdir(app_dir)
        print(f"[bootstrap] CWD set to: {app_dir}", flush=True)

        _bootstrap_ssl_ca_bundle()
        _patch_ca_paths()

        print("[bootstrap] loading main module", flush=True)
        from main import main as run
        run()
    except Exception as e:
        print(f"[bootstrap] critical crash: {e}", flush=True)
        import traceback
        traceback.print_exc()
        sys.exit(1)


if __name__ == "__main__":
    main()

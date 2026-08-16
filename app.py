# -*- coding: utf-8 -*-
"""iHateCards — standalone desktop app.

Wraps the existing HTML interface in a native window (pywebview) and replaces
the in-browser PDF export with a Python backend that outputs a true CMYK PDF
in the US Web Coated (SWOP) color space — no separate conversion step.
"""

import os
import sys
import json
import base64

# WebView2 requires a *writable* user-data folder. When installed in Program
# Files the default location is read-only and CoreWebView2 initialization hangs
# on first launch (hard freeze). Point it at %LOCALAPPDATA% explicitly BEFORE
# importing webview so the WebView2 loader picks it up.
_DATA_DIR = os.path.join(os.environ.get("LOCALAPPDATA") or os.path.expanduser("~"), "iHateCards")
_UDF = os.path.join(_DATA_DIR, "WebView2")
try:
    os.makedirs(_UDF, exist_ok=True)
    os.environ.setdefault("WEBVIEW2_USER_DATA_FOLDER", _UDF)
except Exception:
    _UDF = None


def _startup_log(msg):
    try:
        with open(os.path.join(_DATA_DIR, "startup.log"), "a", encoding="utf-8") as f:
            f.write(msg + "\n")
    except Exception:
        pass


import webview

import cmyk_export
import winprint


def _settings_path():
    base = os.environ.get("LOCALAPPDATA") or app_dir()
    d = os.path.join(base, "iHateCards")
    try:
        os.makedirs(d, exist_ok=True)
    except Exception:
        d = app_dir()
    return os.path.join(d, "settings.json")


def app_dir():
    if getattr(sys, "frozen", False):
        return os.path.dirname(sys.executable)
    return os.path.dirname(os.path.abspath(__file__))


def resource(name):
    base = getattr(sys, "_MEIPASS", app_dir())
    return os.path.join(base, name)


class Api:
    def __init__(self):
        self.window = None

    def icc_status(self):
        return cmyk_export.find_icc() or ""

    def list_printers(self):
        try:
            return {"ok": True, "printers": winprint.list_printers(),
                    "default": winprint.get_default_printer()}
        except Exception as e:  # noqa: BLE001
            return {"ok": False, "error": str(e)}

    def print_cmyk(self, pages, opts=None):
        """Print the laid-out pages.

        opts['postscript'] True  -> RAW DeviceCMYK PostScript (true CMYK, only
                                     for real PostScript printers, e.g. Xerox).
        opts['postscript'] False -> render rasters through the Windows GDI driver
                                     (universal — EPSON L805 and any printer)."""
        opts = opts or {}
        name = opts.get("printer") or winprint.get_default_printer()
        if not name:
            return {"ok": False, "error": "Принтер не найден"}
        try:
            if opts.get("postscript"):
                ps = cmyk_export.build_cmyk_ps(pages, opts)
                ip = (opts.get("ip") or "").strip()
                if ip:
                    # Direct to the printer over TCP/9100 — works even when the
                    # printer is on a WSD port (where spooler RAW jobs vanish).
                    winprint.raw_print_tcp(ip, ps)
                    mode = "CMYK / PostScript (IP %s:9100)" % ip
                else:
                    winprint.raw_print(name, ps, "iHateCards")
                    mode = "CMYK / PostScript (спулер)"
            else:
                # Non-PostScript printers (EPSON etc.): build a PDF of the
                # project and print the file via Ghostscript/the PDF handler.
                mm = [(float(pg["w_mm"]), float(pg["h_mm"])) for pg in pages]
                fit = bool(opts.get("fit"))
                scale = 1.0 if fit else (float(opts.get("scale", 1.0)) or 1.0)
                dpi = int(opts.get("dpi") or 300)
                imgs = [cmyk_export.resample_to_dpi(cmyk_export.page_to_rgb(pg, opts),
                                                    mm[i][0], mm[i][1], dpi)
                        for i, pg in enumerate(pages)]
                pdf = cmyk_export.build_rgb_pdf(imgs, mm, scale)
                tmp = os.path.join(_DATA_DIR, "print_job.pdf")
                with open(tmp, "wb") as f:
                    f.write(pdf)
                used = winprint.print_pdf(name, tmp, copies=int(opts.get("copies", 1)),
                                          fit=fit, dpi=dpi)
                mode = used
        except Exception as e:  # noqa: BLE001
            return {"ok": False, "error": str(e)}
        return {"ok": True, "printer": name, "mode": mode}

    def printer_ip(self, name):
        try:
            return {"ok": True, "ip": winprint.printer_ip_guess(name)}
        except Exception as e:  # noqa: BLE001
            return {"ok": False, "error": str(e), "ip": ""}

    def tcp_hosts(self):
        try:
            return {"ok": True, "hosts": winprint.list_tcp_hosts()}
        except Exception as e:  # noqa: BLE001
            return {"ok": False, "error": str(e), "hosts": []}

    def printer_properties(self, printer=""):
        name = printer or winprint.get_default_printer()
        if not name:
            return {"ok": False, "error": "Принтер не найден"}
        try:
            winprint.open_printer_properties(name)
        except Exception as e:  # noqa: BLE001
            return {"ok": False, "error": str(e)}
        return {"ok": True}

    def load_settings(self):
        try:
            p = _settings_path()
            if os.path.exists(p):
                with open(p, "r", encoding="utf-8") as f:
                    return {"ok": True, "settings": json.load(f)}
            return {"ok": True, "settings": {}}
        except Exception as e:  # noqa: BLE001
            return {"ok": False, "error": str(e), "settings": {}}

    def save_settings(self, settings):
        try:
            with open(_settings_path(), "w", encoding="utf-8") as f:
                json.dump(settings, f, ensure_ascii=False)
            return {"ok": True}
        except Exception as e:  # noqa: BLE001
            return {"ok": False, "error": str(e)}

    def decode_raster(self, b64, name=""):
        """Decode TIFF / other large raster formats in Python (offline)."""
        try:
            return {"ok": True, "images": cmyk_export.decode_raster(b64, name)}
        except Exception as e:  # noqa: BLE001
            return {"ok": False, "error": str(e)}

    def softproof(self, dataurl):
        """Return an sRGB soft-proof (how it will look in CMYK SWOP)."""
        try:
            return {"ok": True, "dataUrl": cmyk_export.softproof(dataurl)}
        except Exception as e:  # noqa: BLE001
            return {"ok": False, "error": str(e)}

    def save_pdf(self, datauri, suggested="document.pdf"):
        """Save an already-built PDF (passed as base64 / data URI) via a
        native Save As dialog. Used e.g. for the calibration target."""
        try:
            b64 = datauri.split(",", 1)[1] if "," in datauri else datauri
            raw = base64.b64decode(b64)
        except Exception as e:  # noqa: BLE001
            return {"ok": False, "error": "decode: %s" % e}
        try:
            result = self.window.create_file_dialog(
                webview.SAVE_DIALOG,
                save_filename=suggested,
                file_types=("PDF files (*.pdf)",),
            )
        except Exception as e:  # noqa: BLE001
            return {"ok": False, "error": "dialog: %s" % e}
        if not result:
            return {"ok": False, "error": "cancelled"}
        path = result if isinstance(result, str) else result[0]
        if not path.lower().endswith(".pdf"):
            path += ".pdf"
        try:
            with open(path, "wb") as f:
                f.write(raw)
        except Exception as e:  # noqa: BLE001
            return {"ok": False, "error": "save: %s" % e}
        return {"ok": True, "path": path}

    def export_cmyk(self, pages, suggested="cards.pdf"):
        try:
            pdf = cmyk_export.build_cmyk_pdf(pages)
        except Exception as e:  # noqa: BLE001
            return {"ok": False, "error": "conversion: %s" % e}
        try:
            result = self.window.create_file_dialog(
                webview.SAVE_DIALOG,
                save_filename=suggested,
                file_types=("PDF files (*.pdf)",),
            )
        except Exception as e:  # noqa: BLE001
            return {"ok": False, "error": "dialog: %s" % e}
        if not result:
            return {"ok": False, "error": "cancelled"}
        path = result if isinstance(result, str) else result[0]
        if not path.lower().endswith(".pdf"):
            path += ".pdf"
        try:
            with open(path, "wb") as f:
                f.write(pdf)
        except Exception as e:  # noqa: BLE001
            return {"ok": False, "error": "save: %s" % e}
        return {"ok": True, "path": path, "icc": cmyk_export.find_icc() or ""}


def main():
    _startup_log("=== start; UDF=%s ===" % _UDF)
    html = resource("iHateCards.html")
    if not os.path.exists(html):
        _startup_log("ERROR: html not found at %s" % html)
        sys.stderr.write("iHateCards.html not found next to the app\n")
        sys.exit(1)
    api = Api()
    _startup_log("creating window")
    win = webview.create_window(
        "iHateCards",
        url=html,
        js_api=api,
        width=1480,
        height=920,
        min_size=(1024, 700),
    )
    api.window = win
    _startup_log("starting webview (storage=%s)" % _UDF)
    try:
        if _UDF:
            webview.start(storage_path=_UDF, private_mode=False)
        else:
            webview.start()
    except Exception as e:  # noqa: BLE001
        _startup_log("webview.start error: %s" % e)
        raise
    _startup_log("webview ended")


if __name__ == "__main__":
    main()

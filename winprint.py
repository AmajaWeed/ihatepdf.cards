# -*- coding: utf-8 -*-
"""Direct RAW printing to a Windows print queue via the spooler API (ctypes).

Sends bytes (PostScript/PCL/PDF) straight to the printer with datatype "RAW",
bypassing the GDI/RGB print path. For a PostScript printer the bytes are
interpreted by the printer's RIP — so DeviceCMYK PostScript prints as real CMYK.
"""

import ctypes
from ctypes import wintypes

winspool = ctypes.WinDLL("winspool.drv")
gdi32 = ctypes.WinDLL("gdi32")

gdi32.CreateDCW.restype = wintypes.HDC
gdi32.CreateDCW.argtypes = [wintypes.LPCWSTR, wintypes.LPCWSTR, wintypes.LPCWSTR, ctypes.c_void_p]
gdi32.DeleteDC.argtypes = [wintypes.HDC]
gdi32.StartDocW.restype = ctypes.c_int
gdi32.StartDocW.argtypes = [wintypes.HDC, ctypes.c_void_p]
gdi32.StartPage.argtypes = [wintypes.HDC]
gdi32.EndPage.argtypes = [wintypes.HDC]
gdi32.EndDoc.argtypes = [wintypes.HDC]
gdi32.AbortDoc.argtypes = [wintypes.HDC]
gdi32.GetDeviceCaps.restype = ctypes.c_int
gdi32.GetDeviceCaps.argtypes = [wintypes.HDC, ctypes.c_int]
gdi32.SetStretchBltMode.argtypes = [wintypes.HDC, ctypes.c_int]
gdi32.StretchDIBits.restype = ctypes.c_int
gdi32.StretchDIBits.argtypes = [wintypes.HDC, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int,
                                ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int,
                                ctypes.c_void_p, ctypes.c_void_p, ctypes.c_uint, wintypes.DWORD]
gdi32.ExtEscape.restype = ctypes.c_int
gdi32.ExtEscape.argtypes = [wintypes.HDC, ctypes.c_int, ctypes.c_int, ctypes.c_void_p,
                            ctypes.c_int, ctypes.c_void_p]


def is_postscript(printer_name):
    """True if the printer driver speaks PostScript (so RAW PS is safe).
    Non-PS printers (EPSON L805 etc.) would print raw PS as garbage."""
    hdc = gdi32.CreateDCW("WINSPOOL", printer_name, None, None)
    if not hdc:
        return False
    try:
        QUERYESCSUPPORT = 8
        POSTSCRIPT_PASSTHROUGH = 4115
        GETTECHNOLOGY = 20
        code = ctypes.c_int(POSTSCRIPT_PASSTHROUGH)
        if gdi32.ExtEscape(hdc, QUERYESCSUPPORT, ctypes.sizeof(code), ctypes.byref(code), 0, None) > 0:
            return True
        buf = ctypes.create_string_buffer(64)
        if gdi32.ExtEscape(hdc, QUERYESCSUPPORT, 4, ctypes.byref(ctypes.c_int(GETTECHNOLOGY)), 0, None) > 0:
            if gdi32.ExtEscape(hdc, GETTECHNOLOGY, 0, None, 64, buf) > 0:
                if b"PostScript" in buf.raw or b"POSTSCRIPT" in buf.raw:
                    return True
        return False
    finally:
        gdi32.DeleteDC(hdc)


class _BITMAPINFOHEADER(ctypes.Structure):
    _fields_ = [
        ("biSize", wintypes.DWORD), ("biWidth", ctypes.c_long), ("biHeight", ctypes.c_long),
        ("biPlanes", wintypes.WORD), ("biBitCount", wintypes.WORD), ("biCompression", wintypes.DWORD),
        ("biSizeImage", wintypes.DWORD), ("biXPelsPerMeter", ctypes.c_long),
        ("biYPelsPerMeter", ctypes.c_long), ("biClrUsed", wintypes.DWORD), ("biClrImportant", wintypes.DWORD),
    ]


class _DOCINFO(ctypes.Structure):
    _fields_ = [
        ("cbSize", ctypes.c_int), ("lpszDocName", wintypes.LPCWSTR),
        ("lpszOutput", wintypes.LPCWSTR), ("lpszDatatype", wintypes.LPCWSTR), ("fwType", wintypes.DWORD),
    ]


def gdi_print(printer_name, images, opts, pages_mm, doc_name="iHateCards", output=None):
    """Print RGB rasters through the Windows GDI driver (for non-PostScript
    printers). The driver handles the printer language + color. images: list of
    PIL RGB images; pages_mm: list of (w_mm, h_mm). output: optional file path
    (for file-based printers like 'Microsoft Print to PDF')."""
    opts = opts or {}
    copies = max(1, int(opts.get("copies", 1)))
    fit = bool(opts.get("fit", False))
    mode = "fit" if fit else opts.get("scaleMode", "real")
    scale = float(opts.get("scale", 1.0)) or 1.0
    SRCCOPY = 0x00CC0020
    COLORONCOLOR = 3
    LOGPIXELSX, LOGPIXELSY, HORZRES, VERTRES = 88, 90, 8, 10

    hdc = gdi32.CreateDCW("WINSPOOL", printer_name, output, None)
    if not hdc:
        raise OSError("CreateDC failed for %r" % printer_name)
    try:
        di = _DOCINFO()
        di.cbSize = ctypes.sizeof(di)
        di.lpszDocName = doc_name
        di.lpszOutput = output
        di.lpszDatatype = None
        di.fwType = 0
        if gdi32.StartDocW(hdc, ctypes.byref(di)) <= 0:
            raise OSError("StartDoc failed")
        dpix = gdi32.GetDeviceCaps(hdc, LOGPIXELSX) or 300
        dpiy = gdi32.GetDeviceCaps(hdc, LOGPIXELSY) or 300
        printW = gdi32.GetDeviceCaps(hdc, HORZRES)
        printH = gdi32.GetDeviceCaps(hdc, VERTRES)
        for _ in range(copies):
            for idx, im in enumerate(images):
                wmm, hmm = pages_mm[idx]
                cw = wmm / 25.4 * dpix
                ch = hmm / 25.4 * dpiy
                if mode == "fit":
                    s = min(printW / cw, printH / ch)
                elif mode == "percent":
                    s = scale
                else:
                    s = 1.0
                tw = max(1, int(cw * s))
                th = max(1, int(ch * s))
                tx = int((printW - tw) / 2)
                ty = int((printH - th) / 2)
                im2 = im.convert("RGB")
                W, H = im2.size
                rowsize = (W * 3 + 3) & ~3  # each scanline padded to 4 bytes
                # 24-bit, bottom-up (positive biHeight), BGR, padded — the most
                # widely supported DIB format for printer device contexts.
                data = im2.tobytes("raw", "BGR", rowsize, -1)
                buf = ctypes.create_string_buffer(data, len(data))
                bmi = _BITMAPINFOHEADER()
                bmi.biSize = ctypes.sizeof(bmi)
                bmi.biWidth = W
                bmi.biHeight = H
                bmi.biPlanes = 1
                bmi.biBitCount = 24
                bmi.biCompression = 0  # BI_RGB
                bmi.biSizeImage = rowsize * H
                if gdi32.StartPage(hdc) <= 0:
                    raise OSError("StartPage failed")
                gdi32.SetStretchBltMode(hdc, COLORONCOLOR)
                r = gdi32.StretchDIBits(hdc, tx, ty, tw, th, 0, 0, W, H,
                                        buf, ctypes.byref(bmi), 0, SRCCOPY)
                if r == 0:
                    gdi32.AbortDoc(hdc)
                    raise OSError("StretchDIBits failed (W=%d H=%d rows=%d bytes=%d dpi=%d)"
                                  % (W, H, rowsize, len(data), dpix))
                gdi32.EndPage(hdc)
        gdi32.EndDoc(hdc)
    finally:
        gdi32.DeleteDC(hdc)


class DOC_INFO_1(ctypes.Structure):
    _fields_ = [
        ("pDocName", wintypes.LPWSTR),
        ("pOutputFile", wintypes.LPWSTR),
        ("pDatatype", wintypes.LPWSTR),
    ]


class PRINTER_INFO_4(ctypes.Structure):
    _fields_ = [
        ("pPrinterName", wintypes.LPWSTR),
        ("pServerName", wintypes.LPWSTR),
        ("Attributes", wintypes.DWORD),
    ]


def get_default_printer():
    size = wintypes.DWORD(0)
    winspool.GetDefaultPrinterW(None, ctypes.byref(size))
    if size.value == 0:
        return ""
    buf = ctypes.create_unicode_buffer(size.value)
    if winspool.GetDefaultPrinterW(buf, ctypes.byref(size)):
        return buf.value
    return ""


def list_printers():
    PRINTER_ENUM_LOCAL = 0x00000002
    PRINTER_ENUM_CONNECTIONS = 0x00000004
    flags = PRINTER_ENUM_LOCAL | PRINTER_ENUM_CONNECTIONS
    needed = wintypes.DWORD(0)
    returned = wintypes.DWORD(0)
    winspool.EnumPrintersW(flags, None, 4, None, 0,
                           ctypes.byref(needed), ctypes.byref(returned))
    if needed.value == 0:
        return []
    buf = ctypes.create_string_buffer(needed.value)
    if not winspool.EnumPrintersW(flags, None, 4, buf, needed.value,
                                  ctypes.byref(needed), ctypes.byref(returned)):
        return []
    names = []
    arr = ctypes.cast(buf, ctypes.POINTER(PRINTER_INFO_4))
    for i in range(returned.value):
        if arr[i].pPrinterName:
            names.append(arr[i].pPrinterName)
    return names


def find_ghostscript():
    import glob
    import shutil
    for exe in ("gswin64c.exe", "gswin32c.exe"):
        p = shutil.which(exe)
        if p:
            return p
    pats = []
    for base in (r"C:\Program Files\gs", r"C:\Program Files (x86)\gs"):
        pats += glob.glob(base + r"\*\bin\gswin64c.exe")
        pats += glob.glob(base + r"\*\bin\gswin32c.exe")
    return pats[0] if pats else None


def print_pdf(printer_name, pdf_path, copies=1, fit=False, dpi=None):
    """Print a PDF file to the given printer. Prefers Ghostscript (mswinpr2 —
    renders the PDF and feeds the driver correctly on any printer, incl. EPSON).
    Falls back to the shell 'printto' verb if Ghostscript is absent.
    `dpi` lowers the rasterization resolution → smaller spool (helps WSD ports)."""
    import subprocess
    gs = find_ghostscript()
    if gs:
        args = [gs, "-dNOSAFER", "-dBATCH", "-dNOPAUSE", "-dPrinted", "-dNoCancel", "-q"]
        if dpi and int(dpi) > 0:
            args.append("-r%d" % int(dpi))
        args += ["-sDEVICE=mswinpr2", "-sOutputFile=%printer%" + printer_name]
        if fit:
            args.append("-dPDFFitPage")
        args.append(pdf_path)
        for _ in range(max(1, int(copies))):
            r = subprocess.run(args, capture_output=True,
                               creationflags=0x08000000)  # CREATE_NO_WINDOW
            if r.returncode != 0:
                msg = (r.stderr or b"").decode("mbcs", "replace")[:400]
                raise OSError("Ghostscript: " + (msg or "exit %d" % r.returncode))
        return "PDF (Ghostscript)"
    # Fallback: default PDF handler via ShellExecute 'printto'
    res = ctypes.windll.shell32.ShellExecuteW(None, "printto", pdf_path, '"%s"' % printer_name, None, 0)
    if int(res) <= 32:
        raise OSError("Ghostscript не найден, и системный обработчик PDF не смог напечатать "
                      "(код %d). Установите Ghostscript: https://ghostscript.com/releases/gsdnld.html"
                      % int(res))
    return "PDF (системный обработчик)"


def raw_print_tcp(host, data, port=9100, timeout=30):
    """Send raw bytes straight to the printer over TCP (JetDirect / port 9100).
    Bypasses the Windows spooler/port entirely — needed for PostScript printers
    sitting on a WSD port, where RAW spooler jobs don't reach the device."""
    import socket
    if isinstance(data, str):
        data = data.encode("latin-1")
    s = socket.create_connection((host, int(port)), timeout)
    try:
        s.sendall(data)
    finally:
        try:
            s.close()
        except Exception:
            pass
    return len(data)


def printer_ip_guess(name):
    """Best-effort resolve a printer's IP from its name. Many network printers
    embed their MAC suffix in the queue name, e.g. 'Xerox AltaLink (9F:7B:3E)';
    we match that against the ARP/neighbor table to find the IP. Lets us print
    PostScript over TCP/9100 even when the printer is on a WSD port."""
    import re
    import subprocess
    m = re.search(r"\(?([0-9A-Fa-f]{2}(?:[:\-][0-9A-Fa-f]{2}){1,5})\)?", name or "")
    if not m:
        return ""
    suffix = "-".join(p.upper() for p in re.split(r"[:\-]", m.group(1)))
    try:
        r = subprocess.run(
            ["powershell", "-NoProfile", "-Command",
             "Get-NetNeighbor -AddressFamily IPv4 | "
             "ForEach-Object {$_.IPAddress + ' ' + $_.LinkLayerAddress}"],
            capture_output=True, text=True, creationflags=0x08000000)
        for line in (r.stdout or "").splitlines():
            parts = line.split()
            if len(parts) >= 2:
                ip, mac = parts[0], parts[1].replace(":", "-").upper()
                if mac.endswith(suffix) and ip.count(".") == 3:
                    return ip
    except Exception:
        pass
    return ""


def list_tcp_hosts():
    """Return host addresses of configured Standard TCP/IP printer ports —
    handy suggestions for the direct port-9100 PostScript path."""
    hosts = []
    try:
        import subprocess
        r = subprocess.run(
            ["powershell", "-NoProfile", "-Command",
             "Get-PrinterPort | Where-Object {$_.PrinterHostAddress} | "
             "ForEach-Object {$_.PrinterHostAddress}"],
            capture_output=True, text=True, creationflags=0x08000000)
        for line in (r.stdout or "").splitlines():
            line = line.strip()
            if line and line not in hosts:
                hosts.append(line)
    except Exception:
        pass
    return hosts


class _PRINTER_DEFAULTS(ctypes.Structure):
    _fields_ = [
        ("pDatatype", wintypes.LPWSTR),
        ("pDevMode", ctypes.c_void_p),
        ("DesiredAccess", wintypes.DWORD),
    ]


class _PRINTER_INFO_9(ctypes.Structure):
    _fields_ = [("pDevMode", ctypes.c_void_p)]


def open_printer_properties(printer_name):
    """Open the native printer-properties dialog AND persist the user's choices
    as the per-user default (SetPrinter level 9 — no admin needed). Without this
    the dialog opens but nothing is saved."""
    DM_OUT_BUFFER = 2
    DM_IN_BUFFER = 8
    DM_IN_PROMPT = 4
    PRINTER_ACCESS_USE = 0x00000008

    winspool.DocumentPropertiesW.restype = ctypes.c_long
    winspool.DocumentPropertiesW.argtypes = [wintypes.HWND, wintypes.HANDLE, wintypes.LPWSTR,
                                             ctypes.c_void_p, ctypes.c_void_p, wintypes.DWORD]
    winspool.SetPrinterW.argtypes = [wintypes.HANDLE, wintypes.DWORD, ctypes.c_void_p, wintypes.DWORD]

    hPrinter = wintypes.HANDLE()
    defaults = _PRINTER_DEFAULTS(None, None, PRINTER_ACCESS_USE)
    if not winspool.OpenPrinterW(printer_name, ctypes.byref(hPrinter), ctypes.byref(defaults)):
        raise OSError("OpenPrinter failed for %r" % printer_name)
    try:
        need = winspool.DocumentPropertiesW(0, hPrinter, printer_name, None, None, 0)
        if need < 0:
            raise OSError("DocumentProperties (size) failed")
        buf = ctypes.create_string_buffer(need)
        pbuf = ctypes.cast(buf, ctypes.c_void_p)
        # load current default DEVMODE, then show the dialog (in=current, out=edited)
        winspool.DocumentPropertiesW(0, hPrinter, printer_name, pbuf, None, DM_OUT_BUFFER)
        rc = winspool.DocumentPropertiesW(0, hPrinter, printer_name, pbuf, pbuf,
                                          DM_IN_PROMPT | DM_IN_BUFFER | DM_OUT_BUFFER)
        if rc == 1:  # IDOK — save as the per-user default so it persists
            info = _PRINTER_INFO_9(pbuf)
            winspool.SetPrinterW(hPrinter, 9, ctypes.byref(info), 0)
        return int(rc)
    finally:
        winspool.ClosePrinter(hPrinter)


def raw_print(printer_name, data, doc_name="iHateCards"):
    if isinstance(data, str):
        data = data.encode("latin-1")
    hPrinter = wintypes.HANDLE()
    if not winspool.OpenPrinterW(printer_name, ctypes.byref(hPrinter), None):
        raise OSError("OpenPrinter failed for %r" % printer_name)
    try:
        doc = DOC_INFO_1(doc_name, None, "RAW")
        job = winspool.StartDocPrinterW(hPrinter, 1, ctypes.byref(doc))
        if job == 0:
            raise OSError("StartDocPrinter failed")
        if not winspool.StartPagePrinter(hPrinter):
            raise OSError("StartPagePrinter failed")
        written = wintypes.DWORD(0)
        cbuf = ctypes.create_string_buffer(data, len(data))
        if not winspool.WritePrinter(hPrinter, cbuf, len(data), ctypes.byref(written)):
            raise OSError("WritePrinter failed")
        winspool.EndPagePrinter(hPrinter)
        winspool.EndDocPrinter(hPrinter)
        return written.value
    finally:
        winspool.ClosePrinter(hPrinter)

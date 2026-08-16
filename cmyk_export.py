# -*- coding: utf-8 -*-
"""RGB -> CMYK (US Web Coated SWOP) conversion and lossless CMYK PDF writer.

The HTML front-end renders each print page to a full-resolution RGB raster
(PNG data URL). Here we convert every page to DeviceCMYK using the SWOP ICC
profile (LittleCMS via Pillow.ImageCms) and assemble a multi-page PDF where
each page is one DeviceCMYK image compressed losslessly with FlateDecode.
"""

import io
import os
import sys
import base64
import zlib
import hashlib
import datetime

from PIL import Image, ImageCms

ICC_CANDIDATES = [
    r"C:\Program Files (x86)\Common Files\Adobe\Color\Profiles\Recommended\USWebCoatedSWOP.icc",
    r"C:\Program Files\Common Files\Adobe\Color\Profiles\Recommended\USWebCoatedSWOP.icc",
    r"C:\Windows\System32\spool\drivers\color\USWebCoatedSWOP.icc",
]


def _bundled_icc():
    base = getattr(sys, "_MEIPASS", os.path.dirname(os.path.abspath(__file__)))
    return os.path.join(base, "USWebCoatedSWOP.icc")


def find_icc():
    for c in [_bundled_icc()] + ICC_CANDIDATES:
        if c and os.path.exists(c):
            return c
    return None


_srgb = ImageCms.createProfile("sRGB")
_xform = None
_icc_path = None


def get_transform():
    global _xform, _icc_path
    if _xform is None:
        icc = find_icc()
        if not icc:
            raise RuntimeError("ICC profile USWebCoatedSWOP.icc not found")
        _icc_path = icc
        swop = ImageCms.getOpenProfile(icc)
        # renderingIntent: 0=Perceptual 1=Relative Colorimetric 2=Saturation 3=Absolute
        _xform = ImageCms.buildTransform(
            _srgb, swop, "RGB", "CMYK", renderingIntent=1,
            flags=ImageCms.Flags.BLACKPOINTCOMPENSATION,
        )
    return _xform


def _dataurl_to_rgb(durl):
    raw = base64.b64decode(durl.split(",", 1)[1])
    return Image.open(io.BytesIO(raw)).convert("RGB")


def rgb_to_cmyk(img_rgb):
    return ImageCms.applyTransform(img_rgb, get_transform())


def _strip_b64(s):
    return s.split(",", 1)[1] if "," in s else s


def decode_raster(b64, name=""):
    """Decode a raster file (TIFF/BMP/PSD/GIF/PNG/JPG/WEBP ...) losslessly to
    PNG data URLs. Multi-page TIFF returns one entry per frame. Runs entirely
    in Pillow — no internet/CDN needed."""
    raw = base64.b64decode(_strip_b64(b64))
    im = Image.open(io.BytesIO(raw))
    frames = []
    try:
        idx = 0
        while True:
            im.seek(idx)
            frames.append(im.convert("RGB").copy())
            idx += 1
    except EOFError:
        pass
    if not frames:
        frames = [im.convert("RGB")]
    out = []
    for fr in frames:
        b = io.BytesIO()
        fr.save(b, "PNG", compress_level=1)  # lossless, light zlib
        out.append("data:image/png;base64," + base64.b64encode(b.getvalue()).decode("ascii"))
    return out


_proof_xform = None


def get_proof_transform():
    global _proof_xform
    if _proof_xform is None:
        icc = find_icc()
        if not icc:
            raise RuntimeError("ICC profile USWebCoatedSWOP.icc not found")
        swop = ImageCms.getOpenProfile(icc)
        srgb = ImageCms.createProfile("sRGB")
        flags = ImageCms.Flags.SOFTPROOFING | ImageCms.Flags.BLACKPOINTCOMPENSATION
        _proof_xform = ImageCms.buildProofTransform(
            srgb, srgb, swop, "RGB", "RGB",
            renderingIntent=1, proofRenderingIntent=1, flags=flags,
        )
    return _proof_xform


def softproof(dataurl, maxpx=1100):
    """Simulate on screen how the image will look once converted to CMYK SWOP
    (sRGB -> SWOP -> sRGB soft proof). Downscaled copy, for preview only."""
    raw = base64.b64decode(_strip_b64(dataurl))
    im = Image.open(io.BytesIO(raw)).convert("RGB")
    if max(im.size) > maxpx:
        im.thumbnail((maxpx, maxpx))
    proofed = ImageCms.applyTransform(im, get_proof_transform())
    b = io.BytesIO()
    proofed.save(b, "PNG", compress_level=3)
    return "data:image/png;base64," + base64.b64encode(b.getvalue()).decode("ascii")


def resample_to_dpi(im, w_mm, h_mm, dpi):
    """Downscale an image so its print resolution is at most `dpi` (smaller
    spool → helps flaky ports like WSD). Never upscales."""
    if not dpi or dpi <= 0:
        return im
    tw = max(1, int(round(float(w_mm) / 25.4 * dpi)))
    th = max(1, int(round(float(h_mm) / 25.4 * dpi)))
    if tw < im.size[0] or th < im.size[1]:
        im = im.resize((tw, th), Image.LANCZOS)
    return im


def build_rgb_pdf(images, pages_mm, scale=1.0):
    """Plain DeviceRGB PDF from already-rendered page images (bw/toner already
    applied). Used as the file we hand to the printer for non-PostScript
    devices (EPSON etc.). pages_mm: list of (w_mm, h_mm)."""
    out = io.BytesIO()
    out.write(b"%PDF-1.4\n%\xe2\xe3\xcf\xd3\n")
    offsets = {}

    def wo(n, body):
        offsets[n] = out.tell()
        out.write(("%d 0 obj\n" % n).encode("latin-1"))
        out.write(body)
        out.write(b"\nendobj\n")

    specs = []
    for im, (wmm, hmm) in zip(images, pages_mm):
        rgb = im.convert("RGB")
        w, h = rgb.size
        comp = zlib.compress(rgb.tobytes(), 9)
        wpt = float(wmm) * scale / 25.4 * 72.0
        hpt = float(hmm) * scale / 25.4 * 72.0
        specs.append((w, h, comp, wpt, hpt))

    num = 3
    triples = []
    page_nums = []
    for sp in specs:
        c_, i_, p_ = num, num + 1, num + 2
        num += 3
        triples.append((c_, i_, p_, sp))
        page_nums.append(p_)
    maxnum = num - 1

    wo(1, b"<< /Type /Catalog /Pages 2 0 R >>")
    wo(2, ("<< /Type /Pages /Count %d /Kids [%s] >>"
           % (len(page_nums), " ".join("%d 0 R" % n for n in page_nums))).encode("latin-1"))
    for (c_, i_, p_, sp) in triples:
        w, h, comp, wpt, hpt = sp
        content = ("q\n%.4f 0 0 %.4f 0 0 cm\n/Im0 Do\nQ\n" % (wpt, hpt)).encode("latin-1")
        cc = zlib.compress(content, 9)
        wo(c_, b"<< /Length %d /Filter /FlateDecode >>\nstream\n" % len(cc) + cc + b"\nendstream")
        ih = ("<< /Type /XObject /Subtype /Image /Width %d /Height %d "
              "/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /Length %d >>\nstream\n"
              % (w, h, len(comp))).encode("latin-1")
        wo(i_, ih + comp + b"\nendstream")
        pb = ("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 %.4f %.4f] "
              "/Resources << /XObject << /Im0 %d 0 R >> >> /Contents %d 0 R >>"
              % (wpt, hpt, i_, c_)).encode("latin-1")
        wo(p_, pb)

    xref = out.tell()
    out.write(("xref\n0 %d\n" % (maxnum + 1)).encode("latin-1"))
    out.write(b"0000000000 65535 f \n")
    for n in range(1, maxnum + 1):
        out.write(("%010d 00000 n \n" % offsets[n]).encode("latin-1"))
    out.write(("trailer\n<< /Size %d /Root 1 0 R >>\nstartxref\n%d\n%%%%EOF"
               % (maxnum + 1, xref)).encode("latin-1"))
    return out.getvalue()


def page_to_rgb(pg, opts=None):
    """Decode a page to an RGB PIL image for GDI (driver) printing, applying
    black&white and toner-economy if requested."""
    opts = opts or {}
    tf = float(opts.get("tonerFactor", 0.75))
    im = _dataurl_to_rgb(pg["dataUrl"])
    if opts.get("bw"):
        im = im.convert("L")
        if opts.get("toner"):
            im = im.point(lambda v: int(255 - (255 - v) * tf))
        im = im.convert("RGB")
    elif opts.get("toner"):
        im = im.point(lambda v: int(255 - (255 - v) * tf))
    return im


def build_cmyk_ps(pages, opts=None):
    """Build DeviceCMYK (or DeviceGray for B/W) PostScript Level 3 for direct RAW
    printing to a PostScript printer (e.g. Xerox AltaLink). Honors print options:
    copies, duplex/tumble, scale, black&white, toner economy. No GDI/RGB."""
    opts = opts or {}
    copies = max(1, int(opts.get("copies", 1)))
    duplex = bool(opts.get("duplex", False))
    tumble = bool(opts.get("tumble", False))
    scale = float(opts.get("scale", 1.0)) or 1.0
    fit = bool(opts.get("fit", False))
    bw = bool(opts.get("bw", False))
    toner = bool(opts.get("toner", False))
    tf = float(opts.get("tonerFactor", 0.75))

    xform = get_transform()
    buf = io.BytesIO()
    buf.write(b"%!PS-Adobe-3.0\n%%Creator: iHateCards\n%%LanguageLevel: 3\n")
    buf.write(("%%%%Pages: %d\n" % len(pages)).encode("latin-1"))
    buf.write(b"%%EndComments\n")
    if copies > 1:
        buf.write(("/#copies %d def\n" % copies).encode("latin-1"))
    if duplex:
        buf.write(("<< /Duplex true /Tumble %s >> setpagedevice\n"
                   % ("true" if tumble else "false")).encode("latin-1"))
    else:
        buf.write(b"<< /Duplex false >> setpagedevice\n")

    for i, pg in enumerate(pages, 1):
        rgb = _dataurl_to_rgb(pg["dataUrl"])
        w, h = rgb.size
        if bw:
            img = rgb.convert("L")
            if toner:
                img = img.point(lambda v: int(255 - (255 - v) * tf))  # lighter
            comp = zlib.compress(img.tobytes(), 9)
            cs = b"/DeviceGray setcolorspace\n"
            decode = "[0 1]"
        else:
            img = ImageCms.applyTransform(rgb, xform)
            if toner:
                img = img.point(lambda v: int(v * tf))  # less ink per channel
            comp = zlib.compress(img.tobytes(), 9)
            cs = b"/DeviceCMYK setcolorspace\n"
            decode = "[0 1 0 1 0 1 0 1]"
        cwpt = float(pg["w_mm"]) / 25.4 * 72.0   # content size in points (1:1)
        chpt = float(pg["h_mm"]) / 25.4 * 72.0
        img_dict = ("<< /ImageType 1 /Width %d /Height %d /BitsPerComponent 8 "
                    "/Decode %s /ImageMatrix [%d 0 0 %d 0 %d] "
                    "/DataSource currentfile /FlateDecode filter >> image\n"
                    % (w, h, decode, w, -h, h)).encode("latin-1")
        buf.write(("%%%%Page: %d %d\n" % (i, i)).encode("latin-1"))
        if fit:
            # Acrobat-style "Fit": use the printer's own page size and scale the
            # content into it (aspect preserved, centered) — works for any paper
            # the printer has loaded (A6/A5/A4/A3/SRA3…).
            buf.write(b"gsave\n")
            buf.write(b"currentpagedevice /PageSize get aload pop /ph exch def /pw exch def\n")
            buf.write(("/cw %.4f def /ch %.4f def\n" % (cwpt, chpt)).encode("latin-1"))
            buf.write(b"pw cw div ph ch div 2 copy gt { exch } if pop /sc exch def\n")
            buf.write(b"pw cw sc mul sub 2 div  ph ch sc mul sub 2 div  translate\n")
            buf.write(b"cw sc mul ch sc mul scale\n")
            buf.write(cs)
            buf.write(img_dict)
        else:
            wpt = cwpt * scale
            hpt = chpt * scale
            buf.write(("<< /PageSize [%.2f %.2f] >> setpagedevice\n" % (wpt, hpt)).encode("latin-1"))
            buf.write(b"gsave\n")
            buf.write(("%.2f %.2f scale\n" % (wpt, hpt)).encode("latin-1"))
            buf.write(cs)
            buf.write(img_dict)
        buf.write(comp)
        buf.write(b"\ngrestore\nshowpage\n")
    buf.write(b"%%EOF\n")
    return buf.getvalue()


def build_cmyk_pdf(pages):
    """pages: list of {dataUrl, w_mm, h_mm}.

    Returns a PDF/X-1a:2003 file: all images DeviceCMYK (SWOP), lossless
    FlateDecode, with the SWOP ICC profile embedded as the OutputIntent
    (DestOutputProfile), TrimBox per page, Trapped/False and a document /ID —
    a print-ready, standards-compliant file.
    """
    xform = get_transform()
    icc_path = find_icc()
    icc_bytes = open(icc_path, "rb").read() if icc_path else b""
    icc_comp = zlib.compress(icc_bytes, 9)

    out = io.BytesIO()
    out.write(b"%PDF-1.4\n%\xe2\xe3\xcf\xd3\n")
    offsets = {}

    def write_obj(num, body):
        offsets[num] = out.tell()
        out.write(("%d 0 obj\n" % num).encode("latin-1"))
        out.write(body)
        out.write(b"\nendobj\n")

    # Decode every page to DeviceCMYK once.
    specs = []
    for pg in pages:
        rgb = _dataurl_to_rgb(pg["dataUrl"])
        cmyk = ImageCms.applyTransform(rgb, xform)
        w, h = cmyk.size
        comp = zlib.compress(cmyk.tobytes(), 9)
        wpt = float(pg["w_mm"]) / 25.4 * 72.0
        hpt = float(pg["h_mm"]) / 25.4 * 72.0
        specs.append((w, h, comp, wpt, hpt))

    # Fixed objects: 1 Catalog, 2 Pages, 3 Info, 4 ICC stream, 5 OutputIntent.
    CAT, PAGES, INFO, ICC, OI = 1, 2, 3, 4, 5
    num = 6
    triples = []
    page_nums = []
    for sp in specs:
        c_, i_, p_ = num, num + 1, num + 2
        num += 3
        triples.append((c_, i_, p_, sp))
        page_nums.append(p_)
    maxnum = num - 1

    kids = " ".join("%d 0 R" % n for n in page_nums)
    write_obj(CAT, ("<< /Type /Catalog /Pages %d 0 R /OutputIntents [%d 0 R] >>"
                    % (PAGES, OI)).encode("latin-1"))
    write_obj(PAGES, ("<< /Type /Pages /Count %d /Kids [%s] >>"
                      % (len(page_nums), kids)).encode("latin-1"))

    date = datetime.datetime.now().strftime("D:%Y%m%d%H%M%S")
    info = ("<< /Title (iHateCards) /Creator (iHateCards) /Producer (iHateCards) "
            "/GTS_PDFXVersion (PDF/X-1a:2003) /GTS_PDFXConformance (PDF/X-1a:2003) "
            "/Trapped /False /CreationDate (%s) /ModDate (%s) >>" % (date, date)).encode("latin-1")
    write_obj(INFO, info)

    write_obj(ICC, b"<< /N 4 /Length %d /Filter /FlateDecode >>\nstream\n" % len(icc_comp)
              + icc_comp + b"\nendstream")

    oi = ("<< /Type /OutputIntent /S /GTS_PDFX "
          "/OutputConditionIdentifier (CGATS TR 001) /OutputCondition (SWOP) "
          "/Info (U.S. Web Coated \\(SWOP\\) v2) /RegistryName (http://www.color.org) "
          "/DestOutputProfile %d 0 R >>" % ICC).encode("latin-1")
    write_obj(OI, oi)

    for (c_, i_, p_, sp) in triples:
        w, h, comp, wpt, hpt = sp
        content = ("q\n%.4f 0 0 %.4f 0 0 cm\n/Im0 Do\nQ\n" % (wpt, hpt)).encode("latin-1")
        ccomp = zlib.compress(content, 9)
        write_obj(c_, b"<< /Length %d /Filter /FlateDecode >>\nstream\n" % len(ccomp)
                  + ccomp + b"\nendstream")
        img_hdr = ("<< /Type /XObject /Subtype /Image /Width %d /Height %d "
                   "/ColorSpace /DeviceCMYK /BitsPerComponent 8 "
                   "/Filter /FlateDecode /Length %d >>\nstream\n" % (w, h, len(comp))).encode("latin-1")
        write_obj(i_, img_hdr + comp + b"\nendstream")
        page_body = ("<< /Type /Page /Parent %d 0 R /MediaBox [0 0 %.4f %.4f] "
                     "/TrimBox [0 0 %.4f %.4f] "
                     "/Resources << /XObject << /Im0 %d 0 R >> >> /Contents %d 0 R >>"
                     % (PAGES, wpt, hpt, wpt, hpt, i_, c_)).encode("latin-1")
        write_obj(p_, page_body)

    xref_pos = out.tell()
    out.write(("xref\n0 %d\n" % (maxnum + 1)).encode("latin-1"))
    out.write(b"0000000000 65535 f \n")
    for n in range(1, maxnum + 1):
        out.write(("%010d 00000 n \n" % offsets[n]).encode("latin-1"))
    doc_id = hashlib.md5(out.getvalue()).hexdigest().upper()
    out.write(("trailer\n<< /Size %d /Root %d 0 R /Info %d 0 R /ID [<%s> <%s>] >>\n"
               "startxref\n%d\n%%%%EOF"
               % (maxnum + 1, CAT, INFO, doc_id, doc_id, xref_pos)).encode("latin-1"))
    return out.getvalue()

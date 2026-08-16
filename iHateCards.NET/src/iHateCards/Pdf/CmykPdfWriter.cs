using System.Security.Cryptography;

namespace iHateCards.Pdf;

/// <summary>Одна страница для CMYK-PDF: сырые DeviceCMYK-пиксели (w*h*4 байта).</summary>
public sealed record CmykPage(byte[] Cmyk, int Width, int Height, double WidthMm, double HeightMm);

/// <summary>
/// PDF/X-1a:2003 писатель — прямой порт build_cmyk_pdf из cmyk_export.py.
/// Все изображения DeviceCMYK (SWOP), FlateDecode без потерь, SWOP ICC как
/// OutputIntent (DestOutputProfile), TrimBox на каждой странице, Trapped/False,
/// /ID документа.
/// </summary>
public static class CmykPdfWriter
{
    public static byte[] Build(IReadOnlyList<CmykPage> pages, byte[] iccBytes)
    {
        var w = new PdfObjectWriter();
        byte[] iccComp = PdfUtil.Zlib(iccBytes);

        // Фиксированные объекты: 1 Catalog, 2 Pages, 3 Info, 4 ICC, 5 OutputIntent.
        const int CAT = 1, PAGES = 2, INFO = 3, ICC = 4, OI = 5;
        int num = 6;
        var triples = new List<(int C, int I, int P, CmykPage Pg)>();
        foreach (var pg in pages)
        {
            triples.Add((num, num + 1, num + 2, pg));
            num += 3;
        }
        var pageNums = triples.Select(t => t.P).ToList();

        string kids = string.Join(" ", pageNums.Select(n => $"{n} 0 R"));
        w.WriteObj(CAT, $"<< /Type /Catalog /Pages {PAGES} 0 R /OutputIntents [{OI} 0 R] >>");
        w.WriteObj(PAGES, $"<< /Type /Pages /Count {pageNums.Count} /Kids [{kids}] >>");

        string date = DateTime.Now.ToString("'D:'yyyyMMddHHmmss");
        w.WriteObj(INFO,
            "<< /Title (iHateCards) /Creator (iHateCards) /Producer (iHateCards) " +
            "/GTS_PDFXVersion (PDF/X-1a:2003) /GTS_PDFXConformance (PDF/X-1a:2003) " +
            $"/Trapped /False /CreationDate ({date}) /ModDate ({date}) >>");

        var iccHdr = PdfUtil.Latin1($"<< /N 4 /Length {iccComp.Length} /Filter /FlateDecode >>\nstream\n");
        w.WriteObj(ICC, Concat(iccHdr, iccComp, PdfUtil.Latin1("\nendstream")));

        w.WriteObj(OI,
            "<< /Type /OutputIntent /S /GTS_PDFX " +
            "/OutputConditionIdentifier (CGATS TR 001) /OutputCondition (SWOP) " +
            "/Info (U.S. Web Coated \\(SWOP\\) v2) /RegistryName (http://www.color.org) " +
            $"/DestOutputProfile {ICC} 0 R >>");

        foreach (var (c, i, p, pg) in triples)
        {
            double wpt = pg.WidthMm / 25.4 * 72.0;
            double hpt = pg.HeightMm / 25.4 * 72.0;
            byte[] comp = PdfUtil.Zlib(pg.Cmyk);

            byte[] content = PdfUtil.Latin1($"q\n{PdfUtil.Num(wpt)} 0 0 {PdfUtil.Num(hpt)} 0 0 cm\n/Im0 Do\nQ\n");
            byte[] ccomp = PdfUtil.Zlib(content);
            w.WriteObj(c, Concat(
                PdfUtil.Latin1($"<< /Length {ccomp.Length} /Filter /FlateDecode >>\nstream\n"),
                ccomp, PdfUtil.Latin1("\nendstream")));

            w.WriteObj(i, Concat(
                PdfUtil.Latin1(
                    $"<< /Type /XObject /Subtype /Image /Width {pg.Width} /Height {pg.Height} " +
                    "/ColorSpace /DeviceCMYK /BitsPerComponent 8 " +
                    $"/Filter /FlateDecode /Length {comp.Length} >>\nstream\n"),
                comp, PdfUtil.Latin1("\nendstream")));

            w.WriteObj(p,
                $"<< /Type /Page /Parent {PAGES} 0 R /MediaBox [0 0 {PdfUtil.Num(wpt)} {PdfUtil.Num(hpt)}] " +
                $"/TrimBox [0 0 {PdfUtil.Num(wpt)} {PdfUtil.Num(hpt)}] " +
                $"/Resources << /XObject << /Im0 {i} 0 R >> >> /Contents {c} 0 R >>");
        }

        long xrefPos = w.WriteXref();
        string docId = Convert.ToHexString(MD5.HashData(w.CurrentBytes()));
        return w.FinishWithTrailer(
            $"<< /Size {w.MaxNum + 1} /Root {CAT} 0 R /Info {INFO} 0 R /ID [<{docId}> <{docId}>] >>",
            xrefPos);
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var res = new byte[parts.Sum(p => p.Length)];
        int off = 0;
        foreach (var p in parts) { Buffer.BlockCopy(p, 0, res, off, p.Length); off += p.Length; }
        return res;
    }
}

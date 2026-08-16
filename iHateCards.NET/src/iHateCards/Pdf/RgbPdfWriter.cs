namespace iHateCards.Pdf;

/// <summary>Страница для RGB-PDF: сырые RGB-пиксели (w*h*3 байта).</summary>
public sealed record RgbPage(byte[] Rgb, int Width, int Height, double WidthMm, double HeightMm);

/// <summary>
/// Обычный DeviceRGB PDF из уже отрендеренных страниц (ч/б и экономия тонера
/// уже применены) — порт build_rgb_pdf. Используется как файл для печати через
/// Ghostscript на не-PostScript принтерах и для PDF-мишени калибровки.
/// </summary>
public static class RgbPdfWriter
{
    public static byte[] Build(IReadOnlyList<RgbPage> pages, double scale = 1.0)
    {
        var w = new PdfObjectWriter();

        int num = 3;
        var triples = new List<(int C, int I, int P, RgbPage Pg)>();
        foreach (var pg in pages)
        {
            triples.Add((num, num + 1, num + 2, pg));
            num += 3;
        }
        var pageNums = triples.Select(t => t.P).ToList();

        w.WriteObj(1, "<< /Type /Catalog /Pages 2 0 R >>");
        w.WriteObj(2, $"<< /Type /Pages /Count {pageNums.Count} /Kids [{string.Join(" ", pageNums.Select(n => $"{n} 0 R"))}] >>");

        foreach (var (c, i, p, pg) in triples)
        {
            double wpt = pg.WidthMm * scale / 25.4 * 72.0;
            double hpt = pg.HeightMm * scale / 25.4 * 72.0;
            byte[] comp = PdfUtil.Zlib(pg.Rgb);

            byte[] content = PdfUtil.Latin1($"q\n{PdfUtil.Num(wpt)} 0 0 {PdfUtil.Num(hpt)} 0 0 cm\n/Im0 Do\nQ\n");
            byte[] ccomp = PdfUtil.Zlib(content);
            w.WriteObj(c, Concat(
                PdfUtil.Latin1($"<< /Length {ccomp.Length} /Filter /FlateDecode >>\nstream\n"),
                ccomp, PdfUtil.Latin1("\nendstream")));

            w.WriteObj(i, Concat(
                PdfUtil.Latin1(
                    $"<< /Type /XObject /Subtype /Image /Width {pg.Width} /Height {pg.Height} " +
                    "/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode " +
                    $"/Length {comp.Length} >>\nstream\n"),
                comp, PdfUtil.Latin1("\nendstream")));

            w.WriteObj(p,
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PdfUtil.Num(wpt)} {PdfUtil.Num(hpt)}] " +
                $"/Resources << /XObject << /Im0 {i} 0 R >> >> /Contents {c} 0 R >>");
        }

        long xrefPos = w.WriteXref();
        return w.FinishWithTrailer($"<< /Size {w.MaxNum + 1} /Root 1 0 R >>", xrefPos);
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var res = new byte[parts.Sum(p => p.Length)];
        int off = 0;
        foreach (var p in parts) { Buffer.BlockCopy(p, 0, res, off, p.Length); off += p.Length; }
        return res;
    }
}

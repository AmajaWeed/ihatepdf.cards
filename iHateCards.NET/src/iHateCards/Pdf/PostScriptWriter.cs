using System.Text;

namespace iHateCards.Pdf;

/// <summary>Страница для PostScript: DeviceCMYK (w*h*4) или DeviceGray (w*h).</summary>
public sealed record PsPage(byte[] Data, bool Gray, int Width, int Height, double WidthMm, double HeightMm);

public sealed record PsOptions(int Copies, bool Duplex, bool Tumble, double Scale, bool Fit);

/// <summary>
/// DeviceCMYK (или DeviceGray для ч/б) PostScript Level 3 для прямой RAW-печати
/// на PostScript-принтер — порт build_cmyk_ps из cmyk_export.py. Уважает опции:
/// копии, duplex/tumble, масштаб, fit. Без GDI/RGB.
/// </summary>
public static class PostScriptWriter
{
    public static byte[] Build(IReadOnlyList<PsPage> pages, PsOptions opts)
    {
        var buf = new MemoryStream();
        void W(string s) => buf.Write(Encoding.Latin1.GetBytes(s));
        void WB(byte[] b) => buf.Write(b, 0, b.Length);

        W("%!PS-Adobe-3.0\n%%Creator: iHateCards\n%%LanguageLevel: 3\n");
        W($"%%Pages: {pages.Count}\n");
        W("%%EndComments\n");
        int copies = Math.Max(1, opts.Copies);
        if (copies > 1)
            W($"/#copies {copies} def\n");
        if (opts.Duplex)
            W($"<< /Duplex true /Tumble {(opts.Tumble ? "true" : "false")} >> setpagedevice\n");
        else
            W("<< /Duplex false >> setpagedevice\n");

        int pageNo = 0;
        foreach (var pg in pages)
        {
            pageNo++;
            byte[] comp = PdfUtil.Zlib(pg.Data);
            string cs = pg.Gray ? "/DeviceGray setcolorspace\n" : "/DeviceCMYK setcolorspace\n";
            string decode = pg.Gray ? "[0 1]" : "[0 1 0 1 0 1 0 1]";
            double cwpt = pg.WidthMm / 25.4 * 72.0;   // размер содержимого в pt (1:1)
            double chpt = pg.HeightMm / 25.4 * 72.0;
            string imgDict =
                $"<< /ImageType 1 /Width {pg.Width} /Height {pg.Height} /BitsPerComponent 8 " +
                $"/Decode {decode} /ImageMatrix [{pg.Width} 0 0 {-pg.Height} 0 {pg.Height}] " +
                "/DataSource currentfile /FlateDecode filter >> image\n";

            W($"%%Page: {pageNo} {pageNo}\n");
            if (opts.Fit)
            {
                // «Подогнать» в стиле Acrobat: берём размер бумаги самого принтера
                // и вписываем содержимое (с сохранением пропорций, по центру).
                W("gsave\n");
                W("currentpagedevice /PageSize get aload pop /ph exch def /pw exch def\n");
                W($"/cw {PdfUtil.Num(cwpt)} def /ch {PdfUtil.Num(chpt)} def\n");
                W("pw cw div ph ch div 2 copy gt { exch } if pop /sc exch def\n");
                W("pw cw sc mul sub 2 div  ph ch sc mul sub 2 div  translate\n");
                W("cw sc mul ch sc mul scale\n");
                W(cs);
                W(imgDict);
            }
            else
            {
                double scale = opts.Scale == 0 ? 1.0 : opts.Scale;
                double wpt = cwpt * scale;
                double hpt = chpt * scale;
                W($"<< /PageSize [{PdfUtil.Num2(wpt)} {PdfUtil.Num2(hpt)}] >> setpagedevice\n");
                W("gsave\n");
                W($"{PdfUtil.Num2(wpt)} {PdfUtil.Num2(hpt)} scale\n");
                W(cs);
                W(imgDict);
            }
            WB(comp);
            W("\ngrestore\nshowpage\n");
        }
        W("%%EOF\n");
        return buf.ToArray();
    }
}

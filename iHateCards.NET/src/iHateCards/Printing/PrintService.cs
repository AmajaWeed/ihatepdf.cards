using iHateCards.Core;
using iHateCards.Imaging;
using iHateCards.Pdf;
using SkiaSharp;

namespace iHateCards.Printing;

public sealed class PrintOptions
{
    public string Printer = "";
    public int Copies = 1;
    public string ScaleMode = "real";   // real | fit | percent
    public double Scale = 1.0;
    public bool Fit;
    public bool Bw;
    public bool Toner;
    public double TonerFactor = 0.75;
    public bool PostScript;
    public int Dpi = 300;
    public string Ip = "";
    public bool Duplex;
    public bool Tumble;
}

public sealed record PrintResult(bool Ok, string Printer, string Mode, string? Error = null);

/// <summary>Оркестратор печати — порт Api.print_cmyk из app.py.</summary>
public static class PrintService
{
    /// <summary>Рендерит все страницы раскладки (как buildPages в app_inject.js).</summary>
    public static List<(SKBitmap Bmp, double WMm, double HMm)> RenderAllPages(AppState s, double dpi)
    {
        var pages = new List<(SKBitmap, double, double)>();
        var paper = s.Paper;
        if (s.DuplexMode && s.HasAnyBack())
        {
            for (int i = 0; i < s.TotalPages; i++)
            {
                pages.Add((PageRenderer.Render(s, i, "front", dpi), paper.Width, paper.Height));
                pages.Add((PageRenderer.Render(s, i, "back", dpi), paper.Width, paper.Height));
            }
        }
        else
        {
            for (int i = 0; i < s.TotalPages; i++)
                pages.Add((PageRenderer.Render(s, i, "front", dpi), paper.Width, paper.Height));
        }
        return pages;
    }

    /// <summary>Список принтеров и принтер по умолчанию для текущей ОС.</summary>
    public static (List<string> Printers, string Default) ListPrinters()
    {
        if (OperatingSystem.IsWindows()) return WinSpool.ListPrinters();
        if (OperatingSystem.IsMacOS()) return MacPrint.ListPrinters();
        return (new List<string>(), "");
    }

    public static bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    public static PrintResult Print(AppState s, PrintOptions o)
    {
        if (!IsSupported)
            return new PrintResult(false, o.Printer, "", "Печать поддерживается в Windows- и macOS-версиях");

        string name = o.Printer;
        if (string.IsNullOrEmpty(name)) name = ListPrinters().Default;
        if (string.IsNullOrEmpty(name))
            return new PrintResult(false, "", "", "Принтер не найден");

        try
        {
            string mode;
            if (o.PostScript)
            {
                var pages = RenderAllPages(s, LayoutConfig.ExportDpiFor(s.Paper));
                try
                {
                    var psPages = new List<PsPage>();
                    foreach (var (bmp, wMm, hMm) in pages)
                    {
                        if (o.Bw)
                        {
                            byte[] gray = SkiaUtil.GetGray(bmp);
                            if (o.Toner) SkiaUtil.TonerLighten(gray, o.TonerFactor);
                            psPages.Add(new PsPage(gray, true, bmp.Width, bmp.Height, wMm, hMm));
                        }
                        else
                        {
                            byte[] cmyk = CmykPipeline.ToCmyk(bmp);
                            if (o.Toner) SkiaUtil.TonerScale(cmyk, o.TonerFactor);
                            psPages.Add(new PsPage(cmyk, false, bmp.Width, bmp.Height, wMm, hMm));
                        }
                    }
                    byte[] ps = PostScriptWriter.Build(psPages,
                        new PsOptions(o.Copies, o.Duplex, o.Tumble, o.Fit ? 1.0 : o.Scale, o.Fit));

                    string ip = o.Ip.Trim();
                    if (ip.Length > 0)
                    {
                        // Напрямую на принтер по TCP/9100 — работает даже когда принтер
                        // на WSD-порту (где RAW-задания спулера пропадают).
                        NetPrint.RawPrintTcp(ip, ps);
                        mode = $"CMYK / PostScript (IP {ip}:9100)";
                    }
                    else if (OperatingSystem.IsWindows())
                    {
                        WinSpool.RawPrint(name, ps);
                        mode = "CMYK / PostScript (спулер)";
                    }
                    else
                    {
                        MacPrint.RawPrint(name, ps);
                        mode = "CMYK / PostScript (CUPS)";
                    }
                }
                finally
                {
                    foreach (var p in pages) p.Bmp.Dispose();
                }
            }
            else
            {
                // Не-PostScript принтеры (EPSON и т.п.): собираем PDF проекта и
                // печатаем файл через Ghostscript / системный обработчик PDF.
                var pages = RenderAllPages(s, LayoutConfig.ExportDpiFor(s.Paper));
                try
                {
                    bool fit = o.Fit;
                    double scale = fit ? 1.0 : (o.Scale == 0 ? 1.0 : o.Scale);
                    int dpi = o.Dpi > 0 ? o.Dpi : 300;
                    var rgbPages = new List<RgbPage>();
                    foreach (var (bmp, wMm, hMm) in pages)
                    {
                        using var resampled = SkiaUtil.ResampleToDpi(bmp, wMm, hMm, dpi);
                        byte[] rgb;
                        if (o.Bw)
                        {
                            byte[] gray = SkiaUtil.GetGray(resampled);
                            if (o.Toner) SkiaUtil.TonerLighten(gray, o.TonerFactor);
                            rgb = new byte[gray.Length * 3];
                            for (int i = 0; i < gray.Length; i++)
                            {
                                rgb[i * 3] = gray[i]; rgb[i * 3 + 1] = gray[i]; rgb[i * 3 + 2] = gray[i];
                            }
                        }
                        else
                        {
                            rgb = SkiaUtil.GetRgb(resampled);
                            if (o.Toner) SkiaUtil.TonerLighten(rgb, o.TonerFactor);
                        }
                        rgbPages.Add(new RgbPage(rgb, resampled.Width, resampled.Height, wMm, hMm));
                    }
                    byte[] pdf = RgbPdfWriter.Build(rgbPages, scale);
                    string tmp = Path.Combine(SettingsPaths.DataDir, "print_job.pdf");
                    File.WriteAllBytes(tmp, pdf);
                    mode = OperatingSystem.IsWindows()
                        ? GhostscriptPrinter.PrintPdf(name, tmp, o.Copies, fit, dpi)
                        : MacPrint.PrintPdf(name, tmp, o.Copies, fit, o.Duplex, o.Tumble);
                }
                finally
                {
                    foreach (var p in pages) p.Bmp.Dispose();
                }
            }
            return new PrintResult(true, name, mode);
        }
        catch (Exception e)
        {
            return new PrintResult(false, name, "", e.Message);
        }
    }
}

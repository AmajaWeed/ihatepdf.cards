using iHateCards.Core;
using iHateCards.Imaging;
using iHateCards.Pdf;
using SkiaSharp;

namespace iHateCards;

/// <summary>
/// `iHateCards --selftest [путь_к_эталону_cmyk]` — проверка ядра без GUI:
/// математика раскладки против эталонных значений JS-версии, сборка PDF,
/// CMYK-конвейер (сравнение с выводом Pillow/LittleCMS), .hate round-trip.
/// </summary>
public static class SelfTest
{
    private static int _fails;

    /// <summary>`--render <outdir>` — рендер демонстрационных страниц в PNG
    /// (визуальная проверка раскладки без GUI).</summary>
    public static int RenderDemo(string outDir)
    {
        Directory.CreateDirectory(outDir);

        ImageEntry Card(SKColor a, SKColor b, int w = 600, int h = 900)
        {
            var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Opaque);
            using (var c = new SKCanvas(bmp))
            {
                using var paint = new SKPaint
                {
                    Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(w, h),
                        new[] { a, b }, SKShaderTileMode.Clamp)
                };
                c.DrawRect(new SKRect(0, 0, w, h), paint);
                using var border = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 12 };
                c.DrawRect(new SKRect(6, 6, w - 6, h - 6), border);
            }
            return new ImageEntry
            {
                Name = "demo.png",
                OriginalBytes = SkiaUtil.EncodePng(bmp),
                Extension = ".png",
                Bitmap = bmp
            };
        }

        void Save(SKBitmap bmp, string name)
        {
            File.WriteAllBytes(Path.Combine(outDir, name), SkiaUtil.EncodePng(bmp));
            bmp.Dispose();
            Console.WriteLine("written " + name);
        }

        // 1) Обычная раскладка A4 с метками, 5 карт (частично заполненный лист)
        var s = new AppState { Bleed = 2 };
        var c1 = Card(SKColors.Crimson, SKColors.Orange); c1.Quantity = 3;
        var c2 = Card(SKColors.Teal, SKColors.Navy); c2.Quantity = 2;
        s.Images.Add(c1); s.Images.Add(c2);
        LayoutEngine.CalculateLayout(s);
        Save(PageRenderer.Render(s, 0, "front", 150, new PageRenderer.Options(true, false)), "demo-a4-front.png");

        // 2) Оборот с зеркалированием и смещением
        s.DuplexMode = true;
        s.BackImage = Card(SKColors.DarkSlateBlue, SKColors.MediumPurple);
        s.OffsetX = 3; s.OffsetY = 1.5;
        Save(PageRenderer.Render(s, 0, "back", 150, new PageRenderer.Options(true, false)), "demo-a4-back.png");

        // 3) Полароид на A4
        var sp = new AppState();
        var pc = Card(SKColors.Goldenrod, SKColors.SaddleBrown, 800, 800); pc.Quantity = 6;
        sp.Images.Add(pc);
        sp.PolaroidMode = true;
        LayoutEngine.ApplyPolaroidDefaults(sp);
        LayoutEngine.CalculateLayout(sp);
        Save(PageRenderer.Render(sp, 0, "front", 150, new PageRenderer.Options(true, false)), "demo-polaroid.png");

        // 4) A3 с инструкцией 70% и калибровочным поворотом
        var sa = new AppState { PaperSizeKey = "a3", CalibMode = true };
        sa.CalibFront = new CalibSide { Dx = 2, Dy = -1, Angle = 1.2 };
        var ac = Card(SKColors.SeaGreen, SKColors.YellowGreen); ac.Quantity = 16;
        sa.Images.Add(ac);
        LayoutEngine.CalculateLayout(sa);
        Save(PageRenderer.Render(sa, 0, "front", 100, new PageRenderer.Options(true, false)), "demo-a3-calib.png");

        // 5) Мишень калибровки
        Save(CalibrationDetector.RenderTargetPage(PaperSizes.A4, "ЛИЦО", "a4", 100), "demo-target.png");

        // 6) Детектор калибровки на собственной мишени (синтетический скан)
        using (var target = CalibrationDetector.RenderTargetPage(PaperSizes.A4, "ЛИЦО", "a4", 100))
        {
            var det = CalibrationDetector.Detect(target, PaperSizes.A4);
            Console.WriteLine(det == null
                ? "calib detect: FAIL (метки не найдены)"
                : $"calib detect на идеальном скане: dx={det.Dx:0.###} dy={det.Dy:0.###} angle={det.Angle:0.###} (должно быть ~0)");
        }
        return 0;
    }

    public static int Run(string[] args)
    {
        TestLayoutMath();
        TestCropMarks();
        TestPolaroid();
        TestPdfStructure();
        TestPostScript();
        TestCmykAgainstReference(args);
        TestSoftproof();
        TestHateRoundTrip();
        Console.WriteLine(_fails == 0 ? "SELFTEST OK" : $"SELFTEST FAILED: {_fails} провал(ов)");
        return _fails == 0 ? 0 : 1;
    }

    private static void Check(bool cond, string name, string? detail = null)
    {
        if (cond) Console.WriteLine($"  ok  {name}");
        else { _fails++; Console.WriteLine($"FAIL  {name}" + (detail != null ? $" — {detail}" : "")); }
    }

    private static AppState BaseState()
    {
        var s = new AppState();
        LayoutEngine.CalculateLayout(s);
        return s;
    }

    private static ImageEntry MakeEntry(int w, int h, SKColor color)
    {
        var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using (var c = new SKCanvas(bmp)) c.Clear(color);
        return new ImageEntry
        {
            Name = "test.png",
            OriginalBytes = SkiaUtil.EncodePng(bmp),
            Extension = ".png",
            Bitmap = bmp,
            Brightness = SkiaUtil.Brightness(bmp)
        };
    }

    private static void TestLayoutMath()
    {
        Console.WriteLine("LayoutEngine:");
        // A4, карта 65×90, поля 4.5: work=201×288 → 3×3 = 9 карт (эталон JS)
        var s = BaseState();
        Check(s.CardsPerRow == 3 && s.CardsPerCol == 3 && s.CardsPerPage == 9,
            "A4 65×90 → 3×3", $"{s.CardsPerRow}×{s.CardsPerCol}");
        var (bx, by) = LayoutEngine.BaseOffset(s);
        Check(Math.Abs(bx - 7.5) < 1e-9 && Math.Abs(by - 13.5) < 1e-9,
            "A4 baseOffset (7.5, 13.5)", $"({bx}, {by})");

        // A3: instruction 8мм; work: 288×403 → карта 65×90: 4 колонки, 4 ряда
        s.PaperSizeKey = "a3";
        LayoutEngine.CalculateLayout(s);
        Check(s.CardsPerRow == 4 && s.CardsPerCol == 4, "A3 65×90 → 4×4",
            $"{s.CardsPerRow}×{s.CardsPerCol}");
        Check(LayoutEngine.InstructionText(s) == "70%", "A3 инструкция 70%");

        // Фото-режим A5, 4 фото: карта = work/2 × work/2
        s = new AppState { PaperSizeKey = "a5", PhotoLayout = 4 };
        LayoutEngine.CalculateLayout(s);
        Check(s.CardsPerRow == 2 && s.CardsPerCol == 2 && Math.Abs(s.CardWidth - 69.5) < 1e-9
            && Math.Abs(s.CardHeight - 100.5) < 1e-9 && s.Bleed == 0,
            "A5 фото-4: 69.5×100.5", $"{s.CardWidth}×{s.CardHeight}");

        // borderless
        s = new AppState { Borderless = true };
        LayoutEngine.CalculateLayout(s);
        Check(s.CardsPerRow == 3 && s.CardsPerCol == 3, "borderless A4 → 3×3");
        Check(LayoutEngine.EffectiveMargin(s) == 0, "borderless margin 0");

        // TotalPages: 10 карт по 9 на листе → 2 листа
        s = BaseState();
        var e = MakeEntry(10, 10, SKColors.Red);
        e.Quantity = 10;
        s.Images.Add(e);
        LayoutEngine.CalculateLayout(s);
        Check(s.TotalPages == 2, "10 карт → 2 листа", s.TotalPages.ToString());
        Check(s.ExpandedImages().Count == 10, "ExpandedImages по количеству");
    }

    private static void TestCropMarks()
    {
        Console.WriteLine("Метки реза:");
        // Одна карта в позиции (0,0) из сетки 3×3 на A4: cut на (7.5+0, 13.5+0), 65×90 (блид 0)
        var segs = LayoutEngine.BuildCropMarks(7.5, 13.5, 65, 90, 0, 0, 3, 3, 210, 297);
        Check(segs.Count == 8, "8 сегментов", segs.Count.ToString());
        // Первая колонка → левые метки до края (x=0, len=cutX-offset=6.5)
        var left = segs.First(sg => sg.Dir == SegDir.H && sg.X == 0);
        Check(Math.Abs(left.Len - 6.5) < 1e-9, "левая метка до края (len 6.5)", left.Len.ToString());
        // Не крайняя правая → короткий ус длиной 3
        var right = segs.First(sg => sg.Dir == SegDir.H && sg.X > 70);
        Check(Math.Abs(right.Len - 3) < 1e-9, "правый ус (len 3)", right.Len.ToString());
        // Первый ряд → верхние метки до края (y=0, len=cutY-offset=12.5)
        var up = segs.First(sg => sg.Dir == SegDir.V && sg.Y == 0);
        Check(Math.Abs(up.Len - 12.5) < 1e-9, "верхняя метка до края (len 12.5)", up.Len.ToString());
    }

    private static void TestPolaroid()
    {
        Console.WriteLine("Полароид:");
        var s = new AppState();
        LayoutEngine.ApplyPolaroidDefaults(s);
        // 65 * 0.67 = 43.55 → округление до 0.5 → 43.5; фото 43.5−6=37.5; H=37.5+3+15=55.5
        Check(Math.Abs(s.CardWidth - 43.5) < 1e-9 && Math.Abs(s.CardHeight - 55.5) < 1e-9,
            "дефолт −33%: 43.5×55.5", $"{s.CardWidth}×{s.CardHeight}");
        var photo = LayoutEngine.PolaroidPhotoArea(s);
        Check(Math.Abs(photo.W - photo.H) < 1e-9, "фото квадратное", $"{photo.W}×{photo.H}");
        LayoutEngine.RestorePrePolaroidDimensions(s);
        Check(s.CardWidth == 65 && s.CardHeight == 90, "восстановление размеров");
    }

    private static void TestPdfStructure()
    {
        Console.WriteLine("PDF-писатели:");
        var cmyk = new byte[8 * 8 * 4];
        var pdf = CmykPdfWriter.Build(
            new[] { new CmykPage(cmyk, 8, 8, 210, 297) }, CmykPipeline.SwopIcc);
        string head = System.Text.Encoding.Latin1.GetString(pdf, 0, Math.Min(2000, pdf.Length));
        string tail = System.Text.Encoding.Latin1.GetString(pdf, Math.Max(0, pdf.Length - 400), Math.Min(400, pdf.Length));
        Check(head.StartsWith("%PDF-1.4"), "заголовок %PDF-1.4");
        Check(head.Contains("/GTS_PDFXVersion (PDF/X-1a:2003)"), "маркер PDF/X-1a");
        Check(head.Contains("/OutputIntents"), "OutputIntent");
        Check(head.Contains("/DeviceCMYK") || System.Text.Encoding.Latin1.GetString(pdf).Contains("/DeviceCMYK"), "DeviceCMYK");
        Check(tail.Contains("startxref") && tail.Contains("%%EOF"), "xref/EOF");
        Check(tail.Contains("/ID [<"), "/ID документа");

        var rgbPdf = RgbPdfWriter.Build(new[] { new RgbPage(new byte[4 * 4 * 3], 4, 4, 100, 100) });
        Check(System.Text.Encoding.Latin1.GetString(rgbPdf).Contains("/DeviceRGB"), "RGB-PDF DeviceRGB");
    }

    private static void TestPostScript()
    {
        Console.WriteLine("PostScript:");
        var ps = PostScriptWriter.Build(
            new[] { new PsPage(new byte[4 * 4 * 4], false, 4, 4, 210, 297) },
            new PsOptions(2, true, false, 1.0, false));
        string text = System.Text.Encoding.Latin1.GetString(ps);
        Check(text.StartsWith("%!PS-Adobe-3.0"), "заголовок PS");
        Check(text.Contains("/#copies 2 def"), "копии");
        Check(text.Contains("/Duplex true"), "duplex");
        Check(text.Contains("/DeviceCMYK setcolorspace"), "DeviceCMYK");
        Check(text.Contains("%%Page: 1 1"), "страница");

        var psFit = PostScriptWriter.Build(
            new[] { new PsPage(new byte[4], true, 2, 2, 100, 100) },
            new PsOptions(1, false, false, 1.0, true));
        Check(System.Text.Encoding.Latin1.GetString(psFit).Contains("currentpagedevice /PageSize get"),
            "fit через PageSize принтера");
    }

    private static void TestCmykAgainstReference(string[] args)
    {
        Console.WriteLine("CMYK-конвейер:");
        string? refDir = args.Skip(1).FirstOrDefault();
        if (refDir == null || !File.Exists(Path.Combine(refDir, "test_cmyk.raw")))
        {
            Console.WriteLine("  (эталон не указан — пропуск сравнения с Pillow)");
            // всё равно проверим базовые свойства
            var white = MakeEntry(4, 4, SKColors.White);
            var cw = CmykPipeline.ToCmyk(white.Bitmap);
            Check(cw.Length == 4 * 4 * 4, "размер CMYK-буфера");
            Check(cw[0] == 0 && cw[1] == 0 && cw[2] == 0 && cw[3] == 0, "белый → 0,0,0,0",
                $"{cw[0]},{cw[1]},{cw[2]},{cw[3]}");
            white.Dispose();
            return;
        }

        using var rgbBmp = SKBitmap.Decode(Path.Combine(refDir, "test_rgb.png"));
        using var norm = SkiaUtil.Normalize(rgbBmp);
        byte[] actual = CmykPipeline.ToCmyk(norm);
        byte[] expected = File.ReadAllBytes(Path.Combine(refDir, "test_cmyk.raw"));
        Check(actual.Length == expected.Length, "длина буфера совпадает");
        long maxDiff = 0, sumDiff = 0;
        int n = Math.Min(actual.Length, expected.Length);
        for (int i = 0; i < n; i++)
        {
            int d = Math.Abs(actual[i] - expected[i]);
            maxDiff = Math.Max(maxDiff, d);
            sumDiff += d;
        }
        double avg = (double)sumDiff / n;
        Console.WriteLine($"  расхождение с Pillow/LittleCMS: max={maxDiff}, avg={avg:0.###}");
        Check(maxDiff <= 3, "max diff ≤ 3 уровней", maxDiff.ToString());
        Check(avg <= 0.5, "avg diff ≤ 0.5", avg.ToString("0.###"));
    }

    private static void TestSoftproof()
    {
        Console.WriteLine("Софтпруф:");
        var vivid = MakeEntry(8, 8, new SKColor(0, 255, 0)); // за пределами охвата SWOP
        using var proof = CmykPipeline.Softproof(vivid.Bitmap);
        var c = proof.GetPixel(4, 4);
        Check(proof.Width == 8 && proof.Height == 8, "размер сохранён");
        Check(c.Green < 240 && (c.Red > 10 || c.Blue > 10),
            "насыщенный зелёный сместился к охвату SWOP", $"{c.Red},{c.Green},{c.Blue}");
        var white = MakeEntry(8, 8, SKColors.White);
        using var proofW = CmykPipeline.Softproof(white.Bitmap);
        var cw = proofW.GetPixel(4, 4);
        Check(cw.Red > 245 && cw.Green > 245 && cw.Blue > 245, "белый остался белым",
            $"{cw.Red},{cw.Green},{cw.Blue}");
        vivid.Dispose();
        white.Dispose();
    }

    private static void TestHateRoundTrip()
    {
        Console.WriteLine(".hate round-trip:");
        string tmp = Path.Combine(Path.GetTempPath(), $"selftest-{Guid.NewGuid():N}.hate");
        try
        {
            var s = BaseState();
            var e1 = MakeEntry(32, 48, SKColors.Crimson);
            e1.Quantity = 3;
            var e2 = MakeEntry(48, 32, SKColors.Teal);
            e2.BackImage = MakeEntry(32, 32, SKColors.Navy);
            s.Images.Add(e1);
            s.Images.Add(e2);
            s.BackImage = MakeEntry(16, 16, SKColors.Gold);
            s.DuplexMode = true;
            s.IndividualBacks = false;
            s.Bleed = 2;
            s.OffsetX = 1.5;
            s.CalibFront = new CalibSide { Dx = 0.7, Dy = -0.3, Angle = 0.25 };
            LayoutEngine.CalculateLayout(s);

            byte[] pdfBefore = ExportCmyk(s);
            HateFile.Save(s, tmp);
            var restored = HateFile.Open(tmp);

            Check(restored.Images.Count == 2 && restored.Images[0].Quantity == 3,
                "карты и количество восстановлены");
            Check(restored.Images[1].BackImage != null, "индивидуальная рубашка восстановлена");
            Check(restored.BackImage != null, "общая рубашка восстановлена");
            Check(Math.Abs(restored.Bleed - 2) < 1e-9 && Math.Abs(restored.OffsetX - 1.5) < 1e-9,
                "числовые параметры");
            Check(Math.Abs(restored.CalibFront.Dx - 0.7) < 1e-9
                && Math.Abs(restored.CalibFront.Angle - 0.25) < 1e-9, "калибровка");
            Check(restored.Images[0].OriginalBytes.SequenceEqual(s.Images[0].OriginalBytes),
                "оригинальные байты без пересжатия");

            byte[] pdfAfter = ExportCmyk(restored);
            Check(PdfPixelPayloadEqual(pdfBefore, pdfAfter),
                "CMYK-экспорт идентичен после round-trip");
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    /// <summary>Экспорт как в MainWindow.BuildCmykPdfBytes (без GUI).</summary>
    private static byte[] ExportCmyk(AppState s)
    {
        var pages = Printing.PrintService.RenderAllPages(s, 96 /* быстрее для теста */);
        try
        {
            var cmykPages = pages
                .Select(p => new CmykPage(CmykPipeline.ToCmyk(p.Bmp), p.Bmp.Width, p.Bmp.Height, p.WMm, p.HMm))
                .ToList();
            return CmykPdfWriter.Build(cmykPages, CmykPipeline.SwopIcc);
        }
        finally
        {
            foreach (var p in pages) p.Bmp.Dispose();
        }
    }

    /// <summary>Сравнение PDF без учёта дат/ID: вырезаем CreationDate/ModDate и /ID.</summary>
    private static bool PdfPixelPayloadEqual(byte[] a, byte[] b)
    {
        static string Strip(byte[] pdf)
        {
            string s = Convert.ToHexString(pdf);
            // проще сравнить длину и содержимое, вырезав переменные места в latin1-представлении
            string t = System.Text.Encoding.Latin1.GetString(pdf);
            t = System.Text.RegularExpressions.Regex.Replace(t, @"/CreationDate \(D:\d+\)", "");
            t = System.Text.RegularExpressions.Regex.Replace(t, @"/ModDate \(D:\d+\)", "");
            t = System.Text.RegularExpressions.Regex.Replace(t, @"/ID \[<[0-9A-F]+> <[0-9A-F]+>\]", "");
            return t;
        }
        return Strip(a) == Strip(b);
    }
}

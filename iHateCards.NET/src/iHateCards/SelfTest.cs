using iHateCards.Core;
using iHateCards.Imaging;
using iHateCards.Pdf;
using iHateCards.Update;
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

        // 5) Развороты: карта 90×65 с явной стрелкой «верх» — лицо и оборот.
        //    После мысленного переворота листа влево-вправо стрелки на обороте
        //    должны смотреть так же, как на лице (не вверх ногами).
        ImageEntry Arrow(SKColor bg, string label)
        {
            var bmp = new SKBitmap(900, 600, SKColorType.Rgba8888, SKAlphaType.Opaque);
            using (var c = new SKCanvas(bmp))
            {
                c.Clear(bg);
                using var ink = new SKPaint { Color = SKColors.White, IsAntialias = true, StrokeWidth = 26, Style = SKPaintStyle.Stroke };
                c.DrawLine(450, 520, 450, 120, ink);          // ствол стрелки
                c.DrawLine(450, 120, 340, 240, ink);          // левое перо
                c.DrawLine(450, 120, 560, 240, ink);          // правое перо
                using var font = new SKFont(AppFonts.PrintTypeface, 90);
                using var text = new SKPaint { Color = SKColors.White, IsAntialias = true };
                c.DrawText(label, 450, 580, SKTextAlign.Center, font, text);
            }
            return new ImageEntry { Name = label, OriginalBytes = SkiaUtil.EncodePng(bmp), Extension = ".png", Bitmap = bmp };
        }

        var sr = new AppState { CardWidth = 90, CardHeight = 65, AutoRotateFrame = true, DuplexMode = true, ShowCropMarks = true };
        var face = Arrow(new SKColor(0x8B, 0x1A, 0x2B), "ЛИЦО"); face.Quantity = 6;
        sr.Images.Add(face);
        sr.BackImage = Arrow(new SKColor(0x1A, 0x3B, 0x8B), "ОБОРОТ");
        LayoutEngine.CalculateLayout(sr);
        Save(PageRenderer.Render(sr, 0, "front", 120, new PageRenderer.Options(true, false)), "demo-rot-front.png");
        Save(PageRenderer.Render(sr, 0, "back", 120, new PageRenderer.Options(true, false)), "demo-rot-back.png");
        Console.WriteLine($"разворот кадра: {sr.FrameRotated}, ячейка {sr.CellWidth}×{sr.CellHeight}, карт/лист {sr.CardsPerPage}");

        // 6) Мишень калибровки
        Save(CalibrationDetector.RenderTargetPage(PaperSizes.A4, "ЛИЦО", "a4", 100), "demo-target.png");

        // 7) Детектор калибровки на собственной мишени (синтетический скан)
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
        TestCustomPaper();
        TestCropMarks();
        TestPolaroid();
        TestPdfStructure();
        TestPostScript();
        TestAutoRotate();
        TestCmykAgainstReference(args);
        TestSoftproof();
        TestUpdates();
        if (!OperatingSystem.IsWindows()) TestUpdateApply();
        TestPrintBackend();
        TestPrinterProfile();
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

    private static void TestCustomPaper()
    {
        Console.WriteLine("Свой размер бумаги:");
        // Широкоформатный лист 700×1000 мм, карта 65×90
        var s = new AppState { PaperSizeKey = PaperSizes.CustomKey, CustomPaperWidth = 700, CustomPaperHeight = 1000 };
        LayoutEngine.CalculateLayout(s);
        Check(Math.Abs(s.Paper.Width - 700) < 1e-9 && Math.Abs(s.Paper.Height - 1000) < 1e-9,
            "размер листа берётся из своих значений", $"{s.Paper.Width}×{s.Paper.Height}");
        // work = 691 × 991 → 10 колонок × 11 рядов
        Check(s.CardsPerRow == 10 && s.CardsPerCol == 11, "700×1000 → 10×11 карт",
            $"{s.CardsPerRow}×{s.CardsPerCol}");

        // Границы: значения вне диапазона зажимаются
        var big = new AppState { PaperSizeKey = PaperSizes.CustomKey, CustomPaperWidth = 99999, CustomPaperHeight = 1 };
        Check(Math.Abs(big.Paper.Width - PaperSizes.MaxCustom) < 1e-9
            && Math.Abs(big.Paper.Height - PaperSizes.MinCustom) < 1e-9,
            "размеры зажимаются в допустимые пределы", $"{big.Paper.Width}×{big.Paper.Height}");

        // Разрешение: обычный лист печатается в 300 dpi, огромный — мельче,
        // чтобы растр не разросся до гигабайтов
        Check(LayoutConfig.ExportDpiFor(PaperSizes.A4) == 300, "A4 — 300 dpi");
        int wideDpi = LayoutConfig.ExportDpiFor(s.Paper);
        Check(wideDpi is > 30 and < 300, $"700×1000 мм — {wideDpi} dpi (понижено)");
        long px = (long)(s.Paper.Width / 25.4 * wideDpi) * (long)(s.Paper.Height / 25.4 * wideDpi);
        Check(px <= LayoutConfig.MaxPagePixels, "растр укладывается в предел памяти",
            $"{px / 1_000_000} Мпикс");
        Check(LayoutConfig.PreviewDpiFor(s.Paper) < LayoutConfig.PreviewDpiFor(PaperSizes.A4),
            "превью большого листа мельче");

        // Реальный рендер большого листа не падает
        var card = MakeEntry(60, 90, SKColors.SlateBlue);
        card.Quantity = 4;
        s.Images.Add(card);
        LayoutEngine.CalculateLayout(s);
        using var bmp = PageRenderer.Render(s, 0, "front", LayoutConfig.PreviewDpiFor(s.Paper),
            new PageRenderer.Options(true, false));
        Check(bmp.Width > 0 && bmp.Height > 0, $"страница отрисована: {bmp.Width}×{bmp.Height} px");
        card.Dispose();
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

        // Носитель: тип, плотность и лоток должны попадать в задание — RAW-печать
        // идёт мимо драйвера, и без этого принтер берёт бумагу по умолчанию
        var psMedia = PostScriptWriter.Build(
            new[] { new PsPage(new byte[4], true, 2, 2, 100, 100) },
            new PsOptions(1, false, false, 1.0, false,
                MediaType: "Heavyweight", MediaWeight: 250, MediaPosition: 4, ManualFeed: true));
        string mediaText = System.Text.Encoding.Latin1.GetString(psMedia);
        Check(mediaText.Contains("/MediaType (Heavyweight)"), "тип бумаги в задании");
        Check(mediaText.Contains("/MediaWeight 250"), "плотность бумаги");
        Check(mediaText.Contains("/MediaPosition 4"), "номер лотка");
        Check(mediaText.Contains("/ManualFeed true"), "ручная подача");
        Check(mediaText.IndexOf("setpagedevice", StringComparison.Ordinal)
              < mediaText.IndexOf("%%Page:", StringComparison.Ordinal),
            "носитель задан до первой страницы");

        var psNoMedia = PostScriptWriter.Build(
            new[] { new PsPage(new byte[4], true, 2, 2, 100, 100) },
            new PsOptions(1, false, false, 1.0, false));
        Check(!System.Text.Encoding.Latin1.GetString(psNoMedia).Contains("/MediaType"),
            "без выбора носителя решает принтер");

        // Скобки в названии не должны ломать строку PostScript
        var psEscape = PostScriptWriter.Build(
            new[] { new PsPage(new byte[4], true, 2, 2, 100, 100) },
            new PsOptions(1, false, false, 1.0, false, MediaType: "Плотная (250 г/м²)"));
        Check(System.Text.Encoding.Latin1.GetString(psEscape).Contains(@"\(250"),
            "скобки в названии экранированы");

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

    private static void TestAutoRotate()
    {
        Console.WriteLine("Авто-развороты:");

        // Кадр: карта 90×65 (альбомная) на A4 — в развёрнутом виде помещается больше
        var s = new AppState { CardWidth = 90, CardHeight = 65, AutoRotateFrame = false };
        LayoutEngine.CalculateLayout(s);
        int normal = s.CardsPerPage;
        s.AutoRotateFrame = true;
        LayoutEngine.CalculateLayout(s);
        Check(s.FrameRotated && s.CardsPerPage > normal,
            $"кадр развёрнут: {normal} → {s.CardsPerPage} карт", $"rotated={s.FrameRotated}");
        Check(Math.Abs(s.CellWidth - 65) < 1e-9 && Math.Abs(s.CellHeight - 90) < 1e-9,
            "ячейка стала 65×90", $"{s.CellWidth}×{s.CellHeight}");

        // Портретная карта на A4 — разворот не выгоден, кадр остаётся как есть
        var p = new AppState { CardWidth = 65, CardHeight = 90, AutoRotateFrame = true };
        LayoutEngine.CalculateLayout(p);
        Check(!p.FrameRotated && p.CardsPerPage == 9, "портретная карта не разворачивается",
            $"rotated={p.FrameRotated}, {p.CardsPerPage}");

        // Оборот: угол инвертируется по длинной стороне, 180−угол по короткой
        Check(LayoutEngine.BackRotation(0, false) == 0, "0° → 0° (длинная сторона)");
        Check(LayoutEngine.BackRotation(90, false) == 270, "90° → 270° (длинная сторона)");
        Check(LayoutEngine.BackRotation(180, false) == 180, "180° → 180° (длинная сторона)");
        Check(LayoutEngine.BackRotation(0, true) == 180, "0° → 180° (короткая сторона)");
        Check(LayoutEngine.BackRotation(90, true) == 90, "90° → 90° (короткая сторона)");
        // Двойное применение возвращает исходный угол — признак корректной инверсии
        Check(LayoutEngine.BackRotation(LayoutEngine.BackRotation(90, false), false) == 90,
            "инверсия обратима (длинная)");
        Check(LayoutEngine.BackRotation(LayoutEngine.BackRotation(90, true), true) == 90,
            "инверсия обратима (короткая)");

        // Авто-разворот изображения — по флагу карты, а не глобально
        Check(LayoutEngine.NeedsAutoRotate(true, 200, 100, 65, 90), "альбомное фото в портретной ячейке");
        Check(!LayoutEngine.NeedsAutoRotate(false, 200, 100, 65, 90), "выключенный флаг не разворачивает");
        Check(!LayoutEngine.NeedsAutoRotate(true, 100, 200, 65, 90), "совпадающие ориентации не трогаем");
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

    private static void TestUpdates()
    {
        Console.WriteLine("Обновления:");
        Check(AppVersion.Compare("2.1.0", "2.0.9") > 0, "2.1.0 новее 2.0.9");
        Check(AppVersion.Compare("2.0.0", "2.0.0") == 0, "равные версии");
        Check(AppVersion.Compare("2.0.0", "10.0.0") < 0, "числовое сравнение, не строковое");
        Check(AppVersion.IsNewer("2.1.0", "2.0.0") && !AppVersion.IsNewer("2.0.0", "2.1.0"),
            "IsNewer в обе стороны");
        Check(AppVersion.Compare("1.0.0b", "1.0.0") > 0, "1.0.0b новее 1.0.0");
        Check(AppVersion.Compare("1.0.0b", "1.0.1") < 0, "1.0.0b старее 1.0.1");
        Check(AppVersion.Compare("1.0.0c", "1.0.0b") > 0, "буквы сравниваются по порядку");
        Check(AppVersion.Rid.Contains('-'), "RID платформы: " + AppVersion.Rid);
        Check(AppVersion.Current != "0.0.0", "версия сборки читается: " + AppVersion.Current);

        const string manifest = """
        { "latest": "9.9.9", "published": "2026-08-18",
          "notes": ["Первое", "Второе"],
          "packages": {
            "win-x64":   { "url": "https://example/win.zip", "sha256": "AA", "size": 10 },
            "osx-arm64": { "url": "https://example/osx.zip", "sha256": "BB", "size": 20 } } }
        """;
        var win = UpdateChecker.Parse(manifest, "win-x64");
        Check(win != null && win.Version == "9.9.9" && win.Package.Url.EndsWith("win.zip"),
            "разбор манифеста для win-x64");
        Check(win!.Notes.Length == 2 && win.Notes[0] == "Первое", "патчноут разобран");
        var osx = UpdateChecker.Parse(manifest, "osx-arm64");
        Check(osx != null && osx.Package.Sha256 == "BB", "выбор пакета по платформе");
        Check(UpdateChecker.Parse(manifest, "linux-x64") == null, "нет пакета — нет обновления");
        Check(UpdateChecker.Parse("{}", "win-x64") == null, "пустой манифест не ломает проверку");

        // Контрольная сумма файла
        string tmp = Path.Combine(Path.GetTempPath(), $"selftest-{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllText(tmp, "iHateCards");
            string sha = UpdateInstaller.Sha256(tmp);
            Check(sha.Length == 64 && sha == UpdateInstaller.Sha256(tmp), "SHA-256 считается стабильно");
        }
        finally { File.Delete(tmp); }
    }

    /// <summary>Проверка подмены файлов: скрипт обновления должен заменить
    /// «установленную» версию новой и убрать резервную копию.</summary>
    private static void TestUpdateApply()
    {
        Console.WriteLine("Применение обновления:");
        string root = Path.Combine(Path.GetTempPath(), $"selftest-upd-{Guid.NewGuid():N}");
        string target = Path.Combine(root, "app");
        string staging = Path.Combine(root, "staging");
        try
        {
            Directory.CreateDirectory(target);
            Directory.CreateDirectory(staging);
            File.WriteAllText(Path.Combine(target, "version.txt"), "2.1.0");
            File.WriteAllText(Path.Combine(target, "old-file.txt"), "должен исчезнуть");
            File.WriteAllText(Path.Combine(staging, "version.txt"), "2.2.0");

            // PID заведомо несуществующего процесса — скрипт не ждёт
            UpdateInstaller.LaunchApplier(staging, target, waitPid: 999999);

            string version = "";
            for (int i = 0; i < 100; i++)
            {
                Thread.Sleep(100);
                string vf = Path.Combine(target, "version.txt");
                if (File.Exists(vf))
                {
                    version = File.ReadAllText(vf).Trim();
                    if (version == "2.2.0") break;
                }
            }
            Check(version == "2.2.0", "файлы заменены новой версией", version);
            Check(!File.Exists(Path.Combine(target, "old-file.txt")), "лишние файлы старой версии убраны");
            Check(!Directory.Exists(target + ".old"), "резервная копия удалена после успеха");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
            try { Directory.Delete(target + ".old", recursive: true); } catch { }
        }
    }

    /// <summary>Только чтение: список очередей печати текущей ОС.
    /// Заданий не отправляем — это делается пользователем из интерфейса.</summary>
    private static void TestPrintBackend()
    {
        Console.WriteLine("Подсистема печати:");
        Check(Printing.PrintService.IsSupported == (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()),
            "бэкенд доступен на этой ОС");
        if (!Printing.PrintService.IsSupported) return;

        var (printers, def) = Printing.PrintService.ListPrinters();
        Console.WriteLine($"  найдено очередей: {printers.Count}" +
                          (def.Length > 0 ? $", по умолчанию: {def}" : ""));
        Check(printers.Count == 0 || printers.All(p => p.Length > 0), "имена очередей непустые");
        Check(def.Length == 0 || printers.Contains(def), "принтер по умолчанию есть в списке");

        // Реальные варианты бумаги у драйвера — критично для «Xerox печатает
        // не на той бумаге»: список должен приходить от самого принтера
        // (значения PPD/IPP, которые понимает `lp -o media-type=`), а не быть
        // общей заглушкой из нескольких слов на любой случай.
        if (def.Length > 0)
        {
            var media = Printing.PrinterMedia.MediaTypes(def);
            bool isFallback = media.Count == Printing.PrinterMedia.CommonTypes.Length
                && media.Select(m => m.Name).SequenceEqual(Printing.PrinterMedia.CommonTypes);
            Console.WriteLine($"  типы бумаги '{def}': {string.Join(", ", media.Select(m => m.Name))}"
                + (isFallback ? "  (заглушка — драйвер не ответил)" : "  (от драйвера)"));
            Check(media.Count > 0, "список типов бумаги не пуст");
        }
    }

    private static void TestPrinterProfile()
    {
        Console.WriteLine("Профиль принтера (.hateprn):");
        var p = new PrinterProfile { Printer = "Xerox AltaLink C8045" };
        p.Duplex.FlipEdge = "short";
        p.Duplex.OffsetX = 0.4; p.Duplex.OffsetY = -0.2;
        p.Print.PostScript = true; p.Print.Ip = "192.168.1.3"; p.Print.Dpi = 360;
        p.SetAutoCalibration("front", new CalibSide { Dx = 0.15, Dy = -0.11, Angle = 0.02 });
        p.SetAutoCalibration("back", new CalibSide { Dx = -0.2, Dy = 0.05, Angle = -0.03 });
        p.Calibration.ManualFront = new CalibSide { Dx = 1, Dy = 2, Angle = 3 };

        string json = p.ToJson().ToJsonString();
        var r = PrinterProfile.Read(json, "fallback");
        Check(r.Printer == p.Printer && r.Duplex.FlipEdge == "short", "принтер и сторона переворота");
        Check(Math.Abs(r.Duplex.OffsetX - 0.4) < 1e-9 && Math.Abs(r.Duplex.OffsetY + 0.2) < 1e-9, "смещения оборота");
        Check(r.Print.PostScript && r.Print.Ip == "192.168.1.3" && r.Print.Dpi == 360, "настройки печати");
        Check(Math.Abs(r.Calibration.AutoFront.Dx - 0.15) < 1e-9
            && Math.Abs(r.Calibration.AutoBack.Angle + 0.03) < 1e-9, "авто-калибровка");
        Check(Math.Abs(r.Calibration.ManualFront.Dx - 1) < 1e-9, "ручные значения хранятся отдельно");
        Check(r.Calibration.MeasuredAt != null, "дата измерения записана");

        // Режим авто/ручной выбирает, что применяется к раскладке
        var st = new AppState();
        r.ApplyTo(st);
        Check(st.DuplexFlipEdge == "short" && Math.Abs(st.CalibFront.Dx - 0.15) < 1e-9,
            "авто-режим применяется к раскладке");
        r.Calibration.UseAuto = false;
        r.ApplyTo(st);
        Check(Math.Abs(st.CalibFront.Dx - 1) < 1e-9, "ручной режим применяется к раскладке");

        // Файл читается с диска
        string tmp = Path.Combine(Path.GetTempPath(), $"selftest-{Guid.NewGuid():N}{PrinterProfile.Extension}");
        try
        {
            p.SaveAs(tmp);
            var imported = PrinterProfile.Import(tmp);
            Check(imported.Duplex.FlipEdge == "short" && imported.Print.Dpi == 360, "импорт файла профиля");
            Check(File.ReadAllText(tmp).Contains("\"flipEdge\""), "файл человекочитаемый (JSON с отступами)");
        }
        finally { File.Delete(tmp); }
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

using iHateCards.Imaging;
using SkiaSharp;

namespace iHateCards.Core;

/// <summary>
/// Единый растровый рендерер страницы (порт renderExportCanvas из app_inject.js
/// + превью-элементы из renderPage). Один и тот же код рисует превью на экране
/// и 300-dpi страницы для экспорта/печати — WYSIWYG гарантирован.
/// </summary>
public static class PageRenderer
{
    public sealed record Options(bool Preview, bool Softproof);

    private static readonly SKSamplingOptions Sampling =
        new(SKCubicResampler.Mitchell);

    public static SKBitmap Render(AppState s, int pageIndex, string side, double dpi, Options? opt = null)
    {
        opt ??= new Options(false, false);
        var paper = s.Paper;
        bool isBack = side == "back";
        double scale = dpi / 25.4;

        int pxW = (int)Math.Round(paper.Width * scale);
        int pxH = (int)Math.Round(paper.Height * scale);
        var bmp = new SKBitmap(pxW, pxH, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.White);

        var calib = s.ActiveCalib(side);
        if (calib.Angle != 0)
            canvas.RotateDegrees((float)calib.Angle, pxW / 2f, pxH / 2f);

        double margin = LayoutEngine.EffectiveMargin(s);
        double instrH = LayoutEngine.InstructionHeight(s);
        var (bx, by) = LayoutEngine.BaseOffset(s);
        double baseX = bx, baseY = by;
        if (isBack) { baseX += s.OffsetX; baseY += s.OffsetY; }
        baseX += calib.Dx; baseY += calib.Dy;

        string? instrText = LayoutEngine.InstructionText(s);
        if (instrText != null)
            DrawCenteredText(canvas, instrText, paper.Width / 2 * scale, (margin + 5) * scale,
                (float)(3.2 * scale), new SKColor(0x66, 0x66, 0x66));

        var expanded = s.ExpandedImages();
        int startIdx = pageIndex * s.CardsPerPage;
        var pageImages = expanded.Skip(startIdx).Take(s.CardsPerPage).ToList();
        double cutW = s.CutWidth, cutH = s.CutHeight;
        var filled = new List<(int DisplayCol, int Row)>();

        for (int row = 0; row < s.CardsPerCol; row++)
        {
            for (int col = 0; col < s.CardsPerRow; col++)
            {
                int idx = row * s.CardsPerRow + col;
                int displayCol = isBack ? (s.CardsPerRow - 1 - col) : col;
                double x = baseX + displayCol * s.CardWidth;
                double y = baseY + row * s.CardHeight;

                ImageEntry? data = null;
                if (idx < pageImages.Count)
                {
                    data = isBack
                        ? (s.IndividualBacks ? pageImages[idx].BackImage : s.BackImage)
                        : pageImages[idx];
                }

                if (data == null)
                {
                    if (opt.Preview)
                        DrawEmptySlot(canvas, x, y, s.CardWidth, s.CardHeight, scale, idx + 1);
                    continue;
                }

                var img = data.DisplayBitmap(opt.Softproof);
                if (s.PolaroidMode)
                {
                    using var white = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
                    canvas.DrawRect(Rect(x, y, s.CardWidth, s.CardHeight, scale), white);
                    var photo = LayoutEngine.PolaroidPhotoArea(s);
                    DrawFitted(canvas, s, img, x + s.PolaroidSide, y + s.PolaroidTop,
                        photo.W, photo.H, scale);
                }
                else
                {
                    DrawFitted(canvas, s, img, x, y, s.CardWidth, s.CardHeight, scale);
                }
                filled.Add((displayCol, row));

                if (opt.Preview && s.Bleed > 0)
                    DrawCutLine(canvas, x + s.Bleed, y + s.Bleed, cutW, cutH, scale);
            }
        }

        if (s.ShowCropMarks && filled.Count > 0)
            DrawCropMarks(canvas, s, filled, baseX, baseY, cutW, cutH, scale);

        return bmp;
    }

    /// <summary>Порт drawFitted: cover/contain + авто-поворот 90°, с клипом по ячейке.</summary>
    private static void DrawFitted(SKCanvas canvas, AppState s, SKBitmap img,
        double dxMm, double dyMm, double dwMm, double dhMm, double scale)
    {
        float dx = (float)(dxMm * scale), dy = (float)(dyMm * scale);
        float dw = (float)(dwMm * scale), dh = (float)(dhMm * scale);
        bool autoRot = LayoutEngine.NeedsAutoRotate(s, img.Width, img.Height, dwMm, dhMm);

        canvas.Save();
        canvas.ClipRect(new SKRect(dx, dy, dx + dw, dy + dh));
        if (s.FitImage)
        {
            using var white = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
            canvas.DrawRect(new SKRect(dx, dy, dx + dw, dy + dh), white);
        }
        canvas.Translate(dx + dw / 2, dy + dh / 2);
        if (autoRot) canvas.RotateDegrees(90);

        float boxW = autoRot ? dh : dw, boxH = autoRot ? dw : dh;
        double ar = (double)img.Width / img.Height;
        double bar = boxW / (double)boxH;
        float drawW, drawH;
        if (s.FitImage)  // contain
        {
            if (ar > bar) { drawW = boxW; drawH = (float)(boxW / ar); }
            else { drawH = boxH; drawW = (float)(boxH * ar); }
        }
        else             // cover
        {
            if (ar > bar) { drawH = boxH; drawW = (float)(boxH * ar); }
            else { drawW = boxW; drawH = (float)(boxW / ar); }
        }
        SkiaUtil.DrawScaled(canvas, img, new SKRect(-drawW / 2, -drawH / 2, drawW / 2, drawH / 2), Sampling);
        canvas.Restore();
    }

    private static void DrawCropMarks(SKCanvas canvas, AppState s,
        List<(int DisplayCol, int Row)> filled, double baseX, double baseY,
        double cutW, double cutH, double scale)
    {
        var paper = s.Paper;
        // Толщины как в jsPDF-экспорте оригинала: чёрная 0.2 мм, гало +0.9 мм;
        // на экранных масштабах не тоньше 1 px.
        float blackPx = Math.Max(1f, (float)(0.2 * scale));
        float haloPx = Math.Max(blackPx + 1.6f, (float)(1.1 * scale));

        var allSegs = new List<CropSeg>();
        foreach (var f in filled)
        {
            double mx = baseX + f.DisplayCol * s.CardWidth + s.Bleed;
            double my = baseY + f.Row * s.CardHeight + s.Bleed;
            allSegs.AddRange(LayoutEngine.BuildCropMarks(mx, my, cutW, cutH,
                f.Row, f.DisplayCol, s.CardsPerCol, s.CardsPerRow, paper.Width, paper.Height));
        }

        void Layer(SKColor color, float thick)
        {
            using var paint = new SKPaint
            {
                Color = color,
                StrokeWidth = thick,
                Style = SKPaintStyle.Stroke,
                StrokeCap = SKStrokeCap.Butt,
                IsAntialias = true
            };
            using var path = new SKPath();
            foreach (var sg in allSegs)
            {
                float x = (float)(sg.X * scale), y = (float)(sg.Y * scale);
                path.MoveTo(x, y);
                if (sg.Dir == SegDir.H) path.LineTo((float)((sg.X + sg.Len) * scale), y);
                else path.LineTo(x, (float)((sg.Y + sg.Len) * scale));
            }
            canvas.DrawPath(path, paint);
        }

        if (!s.NoHaloMarks) Layer(SKColors.White, haloPx);
        Layer(SKColors.Black, blackPx);
    }

    private static void DrawEmptySlot(SKCanvas canvas, double x, double y, double w, double h,
        double scale, int number)
    {
        var rect = Rect(x, y, w, h, scale);
        using var bg = new SKPaint { Color = new SKColor(0xf8, 0xf8, 0xf8), Style = SKPaintStyle.Fill };
        canvas.DrawRect(rect, bg);
        float fontPx = (float)(5.5 * scale); // ~11px на масштабе превью из HTML
        DrawCenteredText(canvas, number.ToString(), (x + w / 2) * scale, (y + h / 2) * scale + fontPx / 3,
            fontPx, new SKColor(0xbb, 0xbb, 0xbb));
    }

    private static void DrawCutLine(SKCanvas canvas, double x, double y, double w, double h, double scale)
    {
        using var paint = new SKPaint
        {
            Color = new SKColor(255, 0, 0, 77),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            PathEffect = SKPathEffect.CreateDash(new float[] { 4, 3 }, 0),
            IsAntialias = true
        };
        canvas.DrawRect(Rect(x, y, w, h, scale), paint);
    }

    private static SKRect Rect(double x, double y, double w, double h, double scale) =>
        new((float)(x * scale), (float)(y * scale), (float)((x + w) * scale), (float)((y + h) * scale));

    private static readonly SKTypeface SansTypeface = ResolveSans();

    private static SKTypeface ResolveSans()
    {
        // Нужен шрифт с кириллицей (подписи на мишени калибровки).
        var tf = SKFontManager.Default.MatchCharacter('Я');
        return tf ?? SKTypeface.Default;
    }

    internal static void DrawCenteredText(SKCanvas canvas, string text, double cx, double baselineY,
        float sizePx, SKColor color)
    {
        using var font = new SKFont(SansTypeface, sizePx);
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawText(text, (float)cx, (float)baselineY, SKTextAlign.Center, font, paint);
    }
}

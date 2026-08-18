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
        bool shortEdge = LayoutEngine.IsShortEdgeFlip(s);
        var filled = new List<(int Col, int Row)>();

        for (int row = 0; row < s.CardsPerCol; row++)
        {
            for (int col = 0; col < s.CardsPerRow; col++)
            {
                int idx = row * s.CardsPerRow + col;
                // Оборот зеркалится: по длинной стороне переворота — колонки,
                // по короткой (tumble) — ряды.
                int displayCol = (isBack && !shortEdge) ? (s.CardsPerRow - 1 - col) : col;
                int displayRow = (isBack && shortEdge) ? (s.CardsPerCol - 1 - row) : row;
                double x = baseX + displayCol * s.CellWidth;
                double y = baseY + displayRow * s.CellHeight;

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
                        DrawEmptySlot(canvas, x, y, s.CellWidth, s.CellHeight, scale, idx + 1);
                    continue;
                }

                DrawCard(canvas, s, data.DisplayBitmap(opt.Softproof), data.AutoRotateImage,
                    x, y, scale, isBack, shortEdge);
                filled.Add((displayCol, displayRow));

                if (opt.Preview && s.Bleed > 0)
                    DrawCutLine(canvas, x + s.Bleed, y + s.Bleed, s.CellCutWidth, s.CellCutHeight, scale);
            }
        }

        if (s.ShowCropMarks && filled.Count > 0)
            DrawCropMarks(canvas, s, filled, baseX, baseY, scale);

        return bmp;
    }

    /// <summary>
    /// Рисует содержимое одной ячейки: белую рамку полароида (если включена) и
    /// изображение с учётом двух независимых разворотов — кадра на листе и
    /// изображения внутри кадра.
    ///
    /// На обороте все углы инвертируются (а при перевороте по короткой стороне
    /// ячейка дополнительно разворачивается на 180°), иначе после переворота
    /// листа рубашка окажется вверх ногами относительно лица. Само изображение
    /// при этом НЕ зеркалится — зеркалится только расположение ячеек.
    /// </summary>
    private static void DrawCard(SKCanvas canvas, AppState s, SKBitmap img, bool autoRotateImage,
        double cellX, double cellY, double scale, bool isBack, bool shortEdge)
    {
        float cx = (float)((cellX + s.CellWidth / 2) * scale);
        float cy = (float)((cellY + s.CellHeight / 2) * scale);

        canvas.Save();
        canvas.ClipRect(Rect(cellX, cellY, s.CellWidth, s.CellHeight, scale));
        canvas.Translate(cx, cy);
        if (isBack && shortEdge) canvas.RotateDegrees(180);
        float sign = isBack ? -1f : 1f;
        if (s.FrameRotated) canvas.RotateDegrees(90 * sign);

        // Дальше — координаты самой карты: прямоугольник CardWidth × CardHeight
        // вокруг начала координат (в мм, умноженных на scale).
        float halfW = (float)(s.CardWidth / 2 * scale);
        float halfH = (float)(s.CardHeight / 2 * scale);

        if (s.PolaroidMode)
        {
            using var white = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
            canvas.DrawRect(new SKRect(-halfW, -halfH, halfW, halfH), white);
            var photo = LayoutEngine.PolaroidPhotoArea(s);
            // Фото-окно смещено относительно центра карты (низ полароида шире верха)
            double photoCx = s.PolaroidSide + photo.W / 2 - s.CardWidth / 2;
            double photoCy = s.PolaroidTop + photo.H / 2 - s.CardHeight / 2;
            DrawImageBox(canvas, s, img, autoRotateImage,
                (float)(photoCx * scale), (float)(photoCy * scale),
                photo.W, photo.H, scale, sign);
        }
        else
        {
            DrawImageBox(canvas, s, img, autoRotateImage, 0, 0, s.CardWidth, s.CardHeight, scale, sign);
        }

        canvas.Restore();
    }

    /// <summary>Вписывает изображение в прямоугольник (cover/contain), с авто-поворотом
    /// на 90°, если ориентация изображения не совпадает с ориентацией окна.</summary>
    private static void DrawImageBox(SKCanvas canvas, AppState s, SKBitmap img, bool autoRotateImage,
        float boxCx, float boxCy, double boxWmm, double boxHmm, double scale, float sign)
    {
        float bw = (float)(boxWmm * scale), bh = (float)(boxHmm * scale);
        bool imgRot = LayoutEngine.NeedsAutoRotate(autoRotateImage, img.Width, img.Height, boxWmm, boxHmm);

        canvas.Save();
        canvas.ClipRect(new SKRect(boxCx - bw / 2, boxCy - bh / 2, boxCx + bw / 2, boxCy + bh / 2));
        if (s.FitImage)
        {
            using var white = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
            canvas.DrawRect(new SKRect(boxCx - bw / 2, boxCy - bh / 2, boxCx + bw / 2, boxCy + bh / 2), white);
        }
        canvas.Translate(boxCx, boxCy);
        if (imgRot) canvas.RotateDegrees(90 * sign);

        float fitW = imgRot ? bh : bw, fitH = imgRot ? bw : bh;
        double ar = (double)img.Width / img.Height;
        double bar = fitW / (double)fitH;
        float drawW, drawH;
        if (s.FitImage)  // contain
        {
            if (ar > bar) { drawW = fitW; drawH = (float)(fitW / ar); }
            else { drawH = fitH; drawW = (float)(fitH * ar); }
        }
        else             // cover
        {
            if (ar > bar) { drawH = fitH; drawW = (float)(fitH * ar); }
            else { drawW = fitW; drawH = (float)(fitW / ar); }
        }
        SkiaUtil.DrawScaled(canvas, img, new SKRect(-drawW / 2, -drawH / 2, drawW / 2, drawH / 2), Sampling);
        canvas.Restore();
    }

    private static void DrawCropMarks(SKCanvas canvas, AppState s,
        List<(int Col, int Row)> filled, double baseX, double baseY, double scale)
    {
        var paper = s.Paper;
        // Толщины как в jsPDF-экспорте оригинала: чёрная 0.2 мм, гало +0.9 мм;
        // на экранных масштабах не тоньше 1 px.
        float blackPx = Math.Max(1f, (float)(0.2 * scale));
        float haloPx = Math.Max(blackPx + 1.6f, (float)(1.1 * scale));

        var allSegs = new List<CropSeg>();
        foreach (var f in filled)
        {
            double mx = baseX + f.Col * s.CellWidth + s.Bleed;
            double my = baseY + f.Row * s.CellHeight + s.Bleed;
            allSegs.AddRange(LayoutEngine.BuildCropMarks(mx, my, s.CellCutWidth, s.CellCutHeight,
                f.Row, f.Col, s.CardsPerCol, s.CardsPerRow, paper.Width, paper.Height));
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

    internal static void DrawCenteredText(SKCanvas canvas, string text, double cx, double baselineY,
        float sizePx, SKColor color)
    {
        using var font = new SKFont(AppFonts.PrintTypeface, sizePx);
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawText(text, (float)cx, (float)baselineY, SKTextAlign.Center, font, paint);
    }
}

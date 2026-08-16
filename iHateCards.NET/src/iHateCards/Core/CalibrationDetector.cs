using SkiaSharp;

namespace iHateCards.Core;

/// <summary>
/// Калибровка двух сторон: рисование мишени и определение смещения/угла по
/// скану — порт drawCalibTarget / detectCalibration из iHateCards.html.
/// </summary>
public static class CalibrationDetector
{
    /// <summary>Порт detectCalibration: ищет тёмные центроиды в четырёх углах
    /// скана (принятого за целый лист), возвращает компенсирующие dx/dy/angle
    /// или null, если метки не найдены.</summary>
    public static CalibSide? Detect(SKBitmap scan, PaperSize paper)
    {
        const int MaxW = 700;
        double sc = Math.Min(1.0, MaxW / (double)scan.Width);
        int w = (int)Math.Round(scan.Width * sc);
        int h = (int)Math.Round(scan.Height * sc);

        using var small = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(small))
        {
            canvas.Clear(SKColors.White);
            Imaging.SkiaUtil.DrawScaled(canvas, scan, new SKRect(0, 0, w, h),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        }

        var px = small.Pixels; // SKColor[]
        double Lum(int x, int y)
        {
            var c = px[y * w + x];
            return c.Red * 0.299 + c.Green * 0.587 + c.Blue * 0.114;
        }

        // Зоны поиска по углам (доли изображения): TL, TR, BR, BL
        var regs = new (double X0, double Y0, double X1, double Y1)[]
        {
            (0, 0, 0.4, 0.4), (0.6, 0, 1, 0.4), (0.6, 0.6, 1, 1), (0, 0.6, 0.4, 1)
        };

        var det = new List<(double X, double Y)>();
        foreach (var r in regs)
        {
            int ax0 = (int)Math.Floor(r.X0 * w), ax1 = (int)Math.Floor(r.X1 * w);
            int ay0 = (int)Math.Floor(r.Y0 * h), ay1 = (int)Math.Floor(r.Y1 * h);
            double mn = 255;
            for (int y = ay0; y < ay1; y++)
                for (int x = ax0; x < ax1; x++)
                { double l = Lum(x, y); if (l < mn) mn = l; }
            double thr = Math.Min(150, mn + 55);
            double sx = 0, sy = 0, sw = 0;
            for (int y = ay0; y < ay1; y++)
                for (int x = ax0; x < ax1; x++)
                {
                    double l = Lum(x, y);
                    if (l < thr) { double wgt = thr - l; sx += x * wgt; sy += y * wgt; sw += wgt; }
                }
            if (sw < 30) return null;
            det.Add((sx / sw, sy / sw));
        }

        // Обнаруженные точки в мм (скан считается обрезанным по листу)
        var dP = det.Select(d => (X: d.X / w * paper.Width, Y: d.Y / h * paper.Height)).ToArray();
        var iP = LayoutEngine.CalibIdealPoints(paper);

        (double X, double Y) Centroid((double X, double Y)[] pts) =>
            (pts.Average(p => p.X), pts.Average(p => p.Y));

        var cD = Centroid(dP);
        var cI = Centroid(iP);
        double num = 0, den = 0;
        for (int k = 0; k < 4; k++)
        {
            double ix = iP[k].X - cI.X, iy = iP[k].Y - cI.Y;
            double dx = dP[k].X - cD.X, dy = dP[k].Y - cD.Y;
            num += ix * dy - iy * dx;
            den += ix * dx + iy * dy;
        }
        double phi = Math.Atan2(num, den) * 180 / Math.PI; // ideal -> detected

        return new CalibSide
        {
            Dx = -(cD.X - cI.X),
            Dy = -(cD.Y - cI.Y),
            Angle = -phi
        };
    }

    /// <summary>Рисует страницу мишени (порт drawCalibTarget) в растр указанного DPI.</summary>
    public static SKBitmap RenderTargetPage(PaperSize paper, string label, string paperKey, double dpi)
    {
        double scale = dpi / 25.4;
        int pxW = (int)Math.Round(paper.Width * scale);
        int pxH = (int)Math.Round(paper.Height * scale);
        var bmp = new SKBitmap(pxW, pxH, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.White);

        using var stroke = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = (float)(0.6 * scale),
            IsAntialias = true
        };
        using var fill = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Fill, IsAntialias = true };

        var pts = LayoutEngine.CalibIdealPoints(paper);
        var dirs = new (int X, int Y)[] { (1, 1), (-1, 1), (-1, -1), (1, -1) };
        for (int i = 0; i < 4; i++)
        {
            var (x, y) = pts[i];
            float fx = (float)(x * scale), fy = (float)(y * scale);
            canvas.DrawLine(fx, fy, (float)((x + dirs[i].X * LayoutConfig.CalibArm) * scale), fy, stroke);
            canvas.DrawLine(fx, fy, fx, (float)((y + dirs[i].Y * LayoutConfig.CalibArm) * scale), stroke);
            canvas.DrawCircle(fx, fy, (float)(1.2 * scale), fill);
        }

        double cx = paper.Width / 2, cy = paper.Height / 2;
        stroke.StrokeWidth = (float)(0.3 * scale);
        canvas.DrawLine((float)((cx - 6) * scale), (float)(cy * scale),
                        (float)((cx + 6) * scale), (float)(cy * scale), stroke);
        canvas.DrawLine((float)(cx * scale), (float)((cy - 6) * scale),
                        (float)(cx * scale), (float)((cy + 6) * scale), stroke);

        // Размеры текста в jsPDF заданы в pt: 11pt и 8pt → мм ≈ pt/72*25.4
        float px11 = (float)(11.0 / 72 * 25.4 * scale);
        float px8 = (float)(8.0 / 72 * 25.4 * scale);
        PageRenderer.DrawCenteredText(canvas, label, cx * scale, (cy - 8) * scale, px11, SKColors.Black);
        PageRenderer.DrawCenteredText(canvas, "iHateCards — мишень калибровки " + paperKey.ToUpperInvariant(),
            cx * scale, (cy + 12) * scale, px8, SKColors.Black);
        PageRenderer.DrawCenteredText(canvas, "Печать 100% / без масштабирования",
            cx * scale, (cy + 17) * scale, px8, SKColors.Black);
        return bmp;
    }
}

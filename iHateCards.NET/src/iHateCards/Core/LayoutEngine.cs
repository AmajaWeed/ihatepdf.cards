namespace iHateCards.Core;

public enum SegDir { H, V }

public sealed record CropSeg(SegDir Dir, double X, double Y, double Len);

/// <summary>Вся математика раскладки — порт функций из iHateCards.html.</summary>
public static class LayoutEngine
{
    public static double EffectiveMargin(AppState s) => s.Borderless ? 0 : LayoutConfig.Margin;

    /// <summary>Высота строки-инструкции сверху листа (A3/SRA3).</summary>
    public static double InstructionHeight(AppState s) =>
        s.PaperSizeKey is "a3" or "sra3" ? 8 : 0;

    public static string? InstructionText(AppState s) => s.PaperSizeKey switch
    {
        "a3" => "70%",
        "sra3" => "66%",
        _ => null
    };

    /// <summary>Порт calculateLayout: заполняет CardsPerRow/Col/PerPage/TotalPages.
    /// В фото-режиме (A5/A6) переопределяет размеры карты и обнуляет блид.</summary>
    public static void CalculateLayout(AppState s)
    {
        var paper = s.Paper;
        double margin = EffectiveMargin(s);
        double instrH = InstructionHeight(s);
        double workW = paper.Width - margin * 2;
        double workH = paper.Height - margin * 2 - instrH;

        if (s.PhotoLayout > 0 && s.PaperSizeKey is "a5" or "a6")
        {
            if (s.PhotoLayout == 2)
            {
                s.CardsPerRow = 1; s.CardsPerCol = 2;
                s.CardWidth = workW; s.CardHeight = workH / 2;
            }
            else if (s.PhotoLayout == 4)
            {
                s.CardsPerRow = 2; s.CardsPerCol = 2;
                s.CardWidth = workW / 2; s.CardHeight = workH / 2;
            }
            s.Bleed = 0;
            s.FrameRotated = false;
        }
        else
        {
            // Авто-разворот кадра: считаем обе ориентации ячейки и берём ту,
            // в которой на лист влезает больше карт (при равенстве — исходную).
            int normal = Fit(workW, s.CardWidth) * Fit(workH, s.CardHeight);
            int rotated = Fit(workW, s.CardHeight) * Fit(workH, s.CardWidth);
            s.FrameRotated = s.AutoRotateFrame && rotated > normal;

            s.CardsPerRow = Fit(workW, s.CellWidth);
            s.CardsPerCol = Fit(workH, s.CellHeight);
        }

        s.CardsPerPage = s.CardsPerRow * s.CardsPerCol;
        int totalCards = s.TotalCardCount();
        s.TotalPages = s.CardsPerPage > 0
            ? Math.Max(1, (int)Math.Ceiling(totalCards / (double)s.CardsPerPage))
            : 1;
        if (s.CurrentPage >= s.TotalPages)
            s.CurrentPage = Math.Max(0, s.TotalPages - 1);
    }

    /// <summary>Левый верхний угол сетки карт на листе (без дуплекс/калибровочных сдвигов).</summary>
    public static (double X, double Y) BaseOffset(AppState s)
    {
        var paper = s.Paper;
        double margin = EffectiveMargin(s);
        double instrH = InstructionHeight(s);
        double workW = paper.Width - margin * 2;
        double workH = paper.Height - margin * 2 - instrH;
        double usedW = s.CardsPerRow * s.CellWidth;
        double usedH = s.CardsPerCol * s.CellHeight;
        return (margin + (workW - usedW) / 2, margin + instrH + (workH - usedH) / 2);
    }

    // ---- Полароид ----

    public static (double W, double H) PolaroidPhotoArea(AppState s)
    {
        double w = s.CardWidth - 2 * s.PolaroidSide;
        double h = s.CardHeight - s.PolaroidTop - s.PolaroidBottom;
        return (Math.Max(10, w), Math.Max(10, h));
    }

    /// <summary>По ширине карты — высота, при которой фото-область квадратная.</summary>
    public static (double W, double H) PolaroidCardFromWidth(AppState s, double w)
    {
        double photoSide = w - 2 * s.PolaroidSide;
        double h = photoSide + s.PolaroidTop + s.PolaroidBottom;
        return (w, Math.Max(20, h));
    }

    public static (double W, double H) PolaroidCardFromHeight(AppState s, double h)
    {
        double photoH = h - s.PolaroidTop - s.PolaroidBottom;
        double w = photoH + 2 * s.PolaroidSide;
        return (Math.Max(20, w), h);
    }

    /// <summary>Включение полароида: −33% от текущей ширины, высота под квадратное фото.</summary>
    public static void ApplyPolaroidDefaults(AppState s)
    {
        s.PrePolaroidWidth = s.CardWidth;
        s.PrePolaroidHeight = s.CardHeight;
        double scaledW = Math.Round(s.PrePolaroidWidth * 0.67 * 2) / 2;
        var dims = PolaroidCardFromWidth(s, scaledW);
        s.CardWidth = dims.W;
        s.CardHeight = Math.Round(dims.H * 2) / 2;
    }

    public static void RestorePrePolaroidDimensions(AppState s)
    {
        s.CardWidth = s.PrePolaroidWidth;
        s.CardHeight = s.PrePolaroidHeight;
    }

    // ---- Авто-поворот ----

    public static bool NeedsAutoRotate(bool enabled, double imgW, double imgH, double cellW, double cellH)
    {
        if (!enabled) return false;
        if (imgW <= 0 || imgH <= 0) return false;
        bool imgLandscape = imgW > imgH;
        bool cellLandscape = cellW > cellH;
        return imgLandscape != cellLandscape;
    }

    private static int Fit(double avail, double size) =>
        size > 0 ? (int)Math.Floor(avail / size) : 0;

    /// <summary>Переворот по короткой стороне (tumble) — зеркалим ряды, а не колонки.</summary>
    public static bool IsShortEdgeFlip(AppState s) =>
        string.Equals(s.DuplexFlipEdge, "short", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Угол, под которым содержимое нужно НАПЕЧАТАТЬ на обороте, чтобы после
    /// переворота листа оно совпало по ориентации с лицом.
    ///
    /// Переворот по длинной стороне (зеркало по вертикали) инвертирует угол:
    /// напечатанные +90° видны как −90°, поэтому печатаем −90°. Переворот по
    /// короткой стороне (tumble) дополнительно разворачивает лист на 180°.
    /// Без поворота (0°) обе формулы дают привычный результат — как раньше.
    /// </summary>
    public static double BackRotation(double frontDeg, bool shortEdge)
    {
        double d = shortEdge ? 180 - frontDeg : -frontDeg;
        d %= 360;
        if (d < 0) d += 360;
        return d;
    }

    // ---- Метки реза ----

    /// <summary>Порт buildCropMarks: 8 сегментов вокруг линии реза одной карты.
    /// Краевые метки продлеваются до края листа, внутренние — короткие «усы».
    /// Координаты в мм.</summary>
    public static List<CropSeg> BuildCropMarks(double cutX, double cutY, double w, double h,
        int row, int col, int rows, int cols, double paperW, double paperH)
    {
        double offset = LayoutConfig.CropMarkOffset;
        double stub = LayoutConfig.CropMarkLength;
        bool firstCol = col == 0, lastCol = col == cols - 1;
        bool firstRow = row == 0, lastRow = row == rows - 1;

        CropSeg Left(double lineY) => firstCol
            ? new CropSeg(SegDir.H, 0, lineY, cutX - offset)
            : new CropSeg(SegDir.H, cutX - offset - stub, lineY, stub);

        CropSeg Right(double lineY)
        {
            double sx = cutX + w + offset;
            return lastCol
                ? new CropSeg(SegDir.H, sx, lineY, paperW - sx)
                : new CropSeg(SegDir.H, sx, lineY, stub);
        }

        CropSeg Up(double lineX) => firstRow
            ? new CropSeg(SegDir.V, lineX, 0, cutY - offset)
            : new CropSeg(SegDir.V, lineX, cutY - offset - stub, stub);

        CropSeg Down(double lineX)
        {
            double sy = cutY + h + offset;
            return lastRow
                ? new CropSeg(SegDir.V, lineX, sy, paperH - sy)
                : new CropSeg(SegDir.V, lineX, sy, stub);
        }

        var segs = new List<CropSeg>
        {
            Left(cutY),     Up(cutX),
            Right(cutY),    Up(cutX + w),
            Left(cutY + h), Down(cutX),
            Right(cutY + h), Down(cutX + w)
        };
        segs.RemoveAll(sg => sg.Len <= 0.2);
        return segs;
    }

    // ---- Калибровка ----

    /// <summary>Идеальные позиции четырёх уголков мишени (TL, TR, BR, BL), мм.</summary>
    public static (double X, double Y)[] CalibIdealPoints(PaperSize p) => new[]
    {
        (LayoutConfig.CalibInset, LayoutConfig.CalibInset),
        (p.Width - LayoutConfig.CalibInset, LayoutConfig.CalibInset),
        (p.Width - LayoutConfig.CalibInset, p.Height - LayoutConfig.CalibInset),
        (LayoutConfig.CalibInset, p.Height - LayoutConfig.CalibInset)
    };

    /// <summary>Поворот точки вокруг центра, экранные координаты (y вниз, по часовой +).</summary>
    public static (double X, double Y) RotatePt(double px, double py, double cx, double cy, double deg)
    {
        double r = deg * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
        double dx = px - cx, dy = py - cy;
        return (cx + dx * c - dy * s, cy + dx * s + dy * c);
    }
}

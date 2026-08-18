namespace iHateCards.Core;

public sealed record PaperSize(string Key, double Width, double Height, string Label);

public static class PaperSizes
{
    public static readonly PaperSize A6 = new("a6", 105, 148, "A6 — 105 × 148 мм");
    public static readonly PaperSize A5 = new("a5", 148, 210, "A5 — 148 × 210 мм");
    public static readonly PaperSize A4 = new("a4", 210, 297, "A4 — 210 × 297 мм");
    public static readonly PaperSize A3 = new("a3", 297, 420, "A3 — 297 × 420 мм");
    public static readonly PaperSize SRA3 = new("sra3", 320, 450, "SRA3 — 320 × 450 мм");

    public static readonly PaperSize[] All = { A6, A5, A4, A3, SRA3 };

    /// <summary>Ключ пользовательского размера (широкоформатная печать).</summary>
    public const string CustomKey = "custom";

    /// <summary>Минимум и максимум стороны листа, мм. Верхняя граница взята
    /// с запасом под рулонную широкоформатную печать.</summary>
    public const double MinCustom = 50;
    public const double MaxCustom = 5000;

    public static PaperSize Custom(double width, double height)
    {
        double w = Math.Clamp(width, MinCustom, MaxCustom);
        double h = Math.Clamp(height, MinCustom, MaxCustom);
        return new PaperSize(CustomKey, w, h, $"Свой размер — {w:0.#} × {h:0.#} мм");
    }

    public static PaperSize ByKey(string key) =>
        Array.Find(All, p => p.Key == key) ?? A4;
}

public static class LayoutConfig
{
    public const double Margin = 4.5;          // мм, поля листа
    public const double CropMarkLength = 3;    // мм, короткий «ус» метки реза
    public const double CropMarkOffset = 1;    // мм, отступ метки от линии реза
    public const double CalibInset = 10;       // мм, вершина уголка мишени от края
    public const double CalibArm = 16;         // мм, длина плеча уголка мишени
    public const int ExportDpi = 300;

    /// <summary>Предел размера растра страницы, пикселей. Нужен для широких
    /// форматов: при экспорте на пиксель приходится 3 байта RGB плюс 4 байта
    /// CMYK, поэтому 60 Мпикс — это уже около 400 МБ оперативной памяти на
    /// лист. Всё, что больше, печатается с пропорционально меньшим dpi.</summary>
    public const long MaxPagePixels = 60_000_000;

    /// <summary>Разрешение экспорта/печати: 300 dpi, но для больших листов
    /// понижается так, чтобы растр остался в пределах MaxPagePixels.</summary>
    public static int ExportDpiFor(PaperSize paper) => DpiFor(paper, ExportDpi, MaxPagePixels);

    /// <summary>Разрешение превью: мельче экспорта и с более жёстким пределом —
    /// на экране всё равно не видно больше.</summary>
    public static double PreviewDpiFor(PaperSize paper) => DpiFor(paper, 150, 4_000_000);

    private static int DpiFor(PaperSize paper, int maxDpi, long maxPixels)
    {
        double wIn = paper.Width / 25.4, hIn = paper.Height / 25.4;
        double area = wIn * hIn;
        if (area <= 0) return maxDpi;
        int fit = (int)Math.Floor(Math.Sqrt(maxPixels / area));
        return Math.Clamp(Math.Min(maxDpi, fit), 30, maxDpi);
    }
}

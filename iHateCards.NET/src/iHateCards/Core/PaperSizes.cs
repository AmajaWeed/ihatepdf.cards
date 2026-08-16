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
}

using SkiaSharp;

namespace iHateCards.Imaging;

/// <summary>Импорт PDF: рендер страниц в ~300 dpi через PDFium (замена pdf.js).</summary>
public static class PdfImporter
{
    public static List<SKBitmap> RenderPages(byte[] pdfBytes, int dpi = 300)
    {
        var result = new List<SKBitmap>();
#pragma warning disable CA1416 // PDFtoImage поддерживает win/osx/linux
        var options = new PDFtoImage.RenderOptions(
            Dpi: dpi,
            WithAnnotations: true,
            WithFormFill: true,
            BackgroundColor: SKColors.White);
        foreach (var bmp in PDFtoImage.Conversion.ToImages(pdfBytes, options: options))
            result.Add(bmp);
#pragma warning restore CA1416
        return result;
    }
}

using ImageMagick;
using SkiaSharp;

namespace iHateCards.Imaging;

/// <summary>
/// Декодирование импортируемых файлов: PNG/JPG/WEBP/GIF/BMP — Skia,
/// TIFF (в т.ч. многостраничный)/PSD/TGA/JP2 и прочее — Magick.NET (офлайн,
/// как decode_raster в Python-версии). PDF — PDFium (PdfImporter).
/// </summary>
public static class RasterDecoder
{
    public static readonly string[] AllExtensions =
    {
        ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp",
        ".tif", ".tiff", ".psd", ".tga", ".jp2", ".j2k", ".pdf"
    };

    private static readonly string[] SkiaExtensions =
        { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp" };

    public sealed record DecodedFrame(SKBitmap Bitmap, byte[] StoreBytes, string StoreExtension, string Label);

    /// <summary>Декодирует файл в один или несколько кадров. Для «родных» Skia
    /// форматов StoreBytes = оригинальный файл без изменений; для TIFF/PSD и
    /// страниц PDF — PNG-рендер кадра (без потерь), как в текущей версии.</summary>
    public static List<DecodedFrame> Decode(byte[] fileBytes, string fileName)
    {
        string ext = Path.GetExtension(fileName).ToLowerInvariant();
        var frames = new List<DecodedFrame>();

        if (ext == ".pdf")
        {
            var pages = PdfImporter.RenderPages(fileBytes);
            for (int i = 0; i < pages.Count; i++)
            {
                string label = pages.Count > 1 ? $"{fileName} ({i + 1})" : fileName;
                frames.Add(new DecodedFrame(pages[i], SkiaUtil.EncodePng(pages[i]), ".png", label));
            }
            return frames;
        }

        if (SkiaExtensions.Contains(ext))
        {
            var bmp = SKBitmap.Decode(fileBytes);
            if (bmp != null)
            {
                var norm = SkiaUtil.Normalize(bmp);
                bmp.Dispose();
                frames.Add(new DecodedFrame(norm, fileBytes, ext == ".jpeg" ? ".jpg" : ext, fileName));
                return frames;
            }
            // Skia не смог — падаем на Magick ниже.
        }

        using var coll = new MagickImageCollection();
        coll.Read(fileBytes);
        int idx = 0;
        foreach (var mi in coll)
        {
            idx++;
            mi.Alpha(AlphaOption.Remove);       // на белый фон
            mi.ColorSpace = ColorSpace.sRGB;
            using var pixels = mi.GetPixels();
            byte[] rgb = pixels.ToByteArray(PixelMapping.RGB)
                ?? throw new InvalidOperationException("decode failed: " + fileName);
            var bmp = SkiaUtil.FromRgb(rgb, (int)mi.Width, (int)mi.Height);
            string label = coll.Count > 1 ? $"{fileName} ({idx})" : fileName;
            bool keepOriginal = coll.Count == 1 && SkiaExtensions.Contains(ext);
            frames.Add(new DecodedFrame(
                bmp,
                keepOriginal ? fileBytes : SkiaUtil.EncodePng(bmp),
                keepOriginal ? ext : ".png",
                label));
        }
        return frames;
    }

    /// <summary>Создаёт ImageEntry из кадра (яркость — как computeBrightness).</summary>
    public static ImageEntry ToEntry(DecodedFrame f) => new()
    {
        Name = f.Label,
        OriginalBytes = f.StoreBytes,
        Extension = f.StoreExtension,
        Bitmap = f.Bitmap,
        Brightness = SkiaUtil.Brightness(f.Bitmap),
        Quantity = 1
    };
}

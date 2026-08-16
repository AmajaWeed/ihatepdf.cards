using SkiaSharp;

namespace iHateCards.Imaging;

/// <summary>
/// Одна карта (или рубашка): оригинальные байты файла (для .hate и точного
/// CMYK-экспорта) + декодированный растр + опциональный софтпруф.
/// </summary>
public sealed class ImageEntry : IDisposable
{
    public required string Name { get; set; }

    /// <summary>Оригинальный файл как есть (без пересжатия). Для страниц PDF и
    /// кадров TIFF — PNG-рендер кадра (аналогично текущей версии).</summary>
    public required byte[] OriginalBytes { get; set; }

    /// <summary>Расширение исходника для assets/ в .hate (".png", ".jpg", ...).</summary>
    public string Extension { get; set; } = ".png";

    public required SKBitmap Bitmap { get; set; }

    /// <summary>Софтпруф-версия (sRGB→SWOP→sRGB), считается лениво.</summary>
    public SKBitmap? ProofBitmap { get; set; }

    public int NaturalWidth => Bitmap.Width;
    public int NaturalHeight => Bitmap.Height;

    public double Brightness { get; set; } = 200;

    public int Quantity { get; set; } = 1;

    /// <summary>Индивидуальная рубашка этой карты (режим individualBacks).</summary>
    public ImageEntry? BackImage { get; set; }

    /// <summary>Что показывать (превью/список): пруф при включённом софтпруфе.</summary>
    public SKBitmap DisplayBitmap(bool softproof) =>
        softproof && ProofBitmap != null ? ProofBitmap : Bitmap;

    public void Dispose()
    {
        Bitmap.Dispose();
        ProofBitmap?.Dispose();
        BackImage?.Dispose();
    }
}

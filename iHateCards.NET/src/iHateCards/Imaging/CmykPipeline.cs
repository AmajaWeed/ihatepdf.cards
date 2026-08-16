using ImageMagick;
using SkiaSharp;

namespace iHateCards.Imaging;

/// <summary>
/// ICC-конвейер sRGB → US Web Coated (SWOP): экспортная трансформация и
/// экранный софтпруф — замена ImageCms (LittleCMS) из cmyk_export.py.
/// Intent: Relative Colorimetric + Black Point Compensation, как в оригинале.
/// </summary>
public static class CmykPipeline
{
    private static readonly Lazy<byte[]> IccBytes = new(() =>
    {
        using var s = typeof(CmykPipeline).Assembly
            .GetManifestResourceStream("iHateCards.Assets.USWebCoatedSWOP.icc")
            ?? throw new InvalidOperationException("Встроенный ICC-профиль USWebCoatedSWOP.icc не найден");
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    });

    public static byte[] SwopIcc => IccBytes.Value;

    private static readonly Lazy<ColorProfile> SwopProfile = new(() => new ColorProfile(IccBytes.Value));

    private static MagickImage FromRgb(byte[] rgb, int w, int h)
    {
        var settings = new PixelReadSettings((uint)w, (uint)h, StorageType.Char, PixelMapping.RGB);
        var img = new MagickImage();
        img.ReadPixels(rgb, settings);
        return img;
    }

    /// <summary>RGB-растр → DeviceCMYK-буфер (w*h*4), SWOP.</summary>
    public static byte[] ToCmyk(SKBitmap bmp)
    {
        byte[] rgb = SkiaUtil.GetRgb(bmp);
        using var img = FromRgb(rgb, bmp.Width, bmp.Height);
        img.SetProfile(ColorProfile.SRGB);
        img.RenderingIntent = RenderingIntent.Relative;
        img.BlackPointCompensation = true;
        img.SetProfile(SwopProfile.Value);
        using var pixels = img.GetPixels();
        return pixels.ToByteArray(PixelMapping.CMYK)
            ?? throw new InvalidOperationException("CMYK conversion failed");
    }

    /// <summary>Софтпруф: sRGB → SWOP → sRGB (как будет выглядеть после печати).
    /// Уменьшенная копия для превью (maxPx по большей стороне).</summary>
    public static SKBitmap Softproof(SKBitmap src, int maxPx = 1100)
    {
        SKBitmap work = src;
        bool scaled = false;
        int mx = Math.Max(src.Width, src.Height);
        if (mx > maxPx)
        {
            double k = maxPx / (double)mx;
            int tw = Math.Max(1, (int)Math.Round(src.Width * k));
            int th = Math.Max(1, (int)Math.Round(src.Height * k));
            work = new SKBitmap(tw, th, SKColorType.Rgba8888, SKAlphaType.Opaque);
            using var canvas = new SKCanvas(work);
            canvas.Clear(SKColors.White);
            SkiaUtil.DrawScaled(canvas, src, new SKRect(0, 0, tw, th), new SKSamplingOptions(SKCubicResampler.Mitchell));
            scaled = true;
        }

        try
        {
            byte[] rgb = SkiaUtil.GetRgb(work);
            using var img = FromRgb(rgb, work.Width, work.Height);
            img.SetProfile(ColorProfile.SRGB);
            img.RenderingIntent = RenderingIntent.Relative;
            img.BlackPointCompensation = true;
            img.SetProfile(SwopProfile.Value);          // → CMYK (SWOP)
            img.SetProfile(ColorProfile.SRGB);          // → обратно в sRGB
            using var pixels = img.GetPixels();
            byte[] outRgb = pixels.ToByteArray(PixelMapping.RGB)
                ?? throw new InvalidOperationException("softproof failed");
            return SkiaUtil.FromRgb(outRgb, work.Width, work.Height);
        }
        finally
        {
            if (scaled) work.Dispose();
        }
    }
}

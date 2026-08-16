using System.Runtime.InteropServices;
using SkiaSharp;

namespace iHateCards.Imaging;

public static class SkiaUtil
{
    /// <summary>Рисует битмап в прямоугольник с указанным сэмплингом
    /// (в SkiaSharp 3 sampling-перегрузка есть только у SKImage).</summary>
    public static void DrawScaled(SKCanvas canvas, SKBitmap bmp, SKRect dest, SKSamplingOptions sampling)
    {
        using var img = SKImage.FromBitmap(bmp);
        canvas.DrawImage(img, dest, sampling);
    }

    /// <summary>Плотный RGB-буфер (w*h*3) из SKBitmap любого формата.</summary>
    public static byte[] GetRgb(SKBitmap bmp)
    {
        using var norm = Normalize(bmp);
        var src = norm.GetPixelSpan();
        var rgb = new byte[norm.Width * norm.Height * 3];
        for (int i = 0, o = 0; i < src.Length; i += 4, o += 3)
        {
            rgb[o] = src[i];
            rgb[o + 1] = src[i + 1];
            rgb[o + 2] = src[i + 2];
        }
        return rgb;
    }

    /// <summary>SKBitmap из плотного RGB-буфера.</summary>
    public static SKBitmap FromRgb(byte[] rgb, int w, int h)
    {
        var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Opaque);
        var rgba = new byte[w * h * 4];
        for (int i = 0, o = 0; o < rgb.Length; i += 4, o += 3)
        {
            rgba[i] = rgb[o];
            rgba[i + 1] = rgb[o + 1];
            rgba[i + 2] = rgb[o + 2];
            rgba[i + 3] = 255;
        }
        Marshal.Copy(rgba, 0, bmp.GetPixels(), rgba.Length);
        return bmp;
    }

    /// <summary>Приводит к RGBA8888 поверх белого фона (как convert("RGB") в Pillow).</summary>
    public static SKBitmap Normalize(SKBitmap bmp)
    {
        var res = new SKBitmap(bmp.Width, bmp.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(res);
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(bmp, 0, 0);
        return res;
    }

    /// <summary>Ч/б (ITU-R 601, как Pillow convert("L")), плотный буфер w*h.</summary>
    public static byte[] GetGray(SKBitmap bmp)
    {
        using var norm = Normalize(bmp);
        var src = norm.GetPixelSpan();
        var gray = new byte[norm.Width * norm.Height];
        for (int i = 0, o = 0; i < src.Length; i += 4, o++)
            gray[o] = (byte)Math.Clamp((int)(src[i] * 0.299 + src[i + 1] * 0.587 + src[i + 2] * 0.114), 0, 255);
        return gray;
    }

    /// <summary>Средняя яркость по 32×32 (порт computeBrightness).</summary>
    public static double Brightness(SKBitmap bmp)
    {
        using var small = new SKBitmap(32, 32, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(small))
        {
            canvas.Clear(SKColors.White);
            DrawScaled(canvas, bmp, new SKRect(0, 0, 32, 32),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        }
        var px = small.GetPixelSpan();
        double sum = 0;
        for (int i = 0; i < px.Length; i += 4)
            sum += px[i] * 0.299 + px[i + 1] * 0.587 + px[i + 2] * 0.114;
        return sum / (32 * 32);
    }

    /// <summary>Даунскейл, чтобы разрешение печати было не выше dpi (порт resample_to_dpi).
    /// Никогда не апскейлит.</summary>
    public static SKBitmap ResampleToDpi(SKBitmap bmp, double wMm, double hMm, int dpi)
    {
        if (dpi <= 0) return bmp.Copy();
        int tw = Math.Max(1, (int)Math.Round(wMm / 25.4 * dpi));
        int th = Math.Max(1, (int)Math.Round(hMm / 25.4 * dpi));
        if (tw >= bmp.Width && th >= bmp.Height) return bmp.Copy();
        var res = new SKBitmap(tw, th, bmp.ColorType, bmp.AlphaType);
        using var canvas = new SKCanvas(res);
        DrawScaled(canvas, bmp, new SKRect(0, 0, tw, th), new SKSamplingOptions(SKCubicResampler.Mitchell));
        return res;
    }

    /// <summary>«Экономия тонера» для RGB/Gray: v → 255 − (255 − v)·tf (светлее).</summary>
    public static void TonerLighten(byte[] channels, double tf)
    {
        for (int i = 0; i < channels.Length; i++)
            channels[i] = (byte)Math.Clamp((int)(255 - (255 - channels[i]) * tf), 0, 255);
    }

    /// <summary>«Экономия тонера» для CMYK: v → v·tf (меньше краски на канал).</summary>
    public static void TonerScale(byte[] channels, double tf)
    {
        for (int i = 0; i < channels.Length; i++)
            channels[i] = (byte)Math.Clamp((int)(channels[i] * tf), 0, 255);
    }

    public static byte[] EncodePng(SKBitmap bmp)
    {
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}

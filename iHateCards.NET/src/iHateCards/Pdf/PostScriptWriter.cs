using System.Text;

namespace iHateCards.Pdf;

/// <summary>Страница для PostScript: DeviceCMYK (w*h*4) или DeviceGray (w*h).</summary>
public sealed record PsPage(byte[] Data, bool Gray, int Width, int Height, double WidthMm, double HeightMm);

/// <summary>
/// Параметры задания PostScript. Носитель (тип, плотность, лоток) приходится
/// указывать здесь: RAW-задание идёт мимо драйвера принтера, поэтому выбранная
/// в его диалоге «плотная бумага» до RIP не доезжает — принтер взял бы обычную.
/// </summary>
public sealed record PsOptions(
    int Copies,
    bool Duplex,
    bool Tumble,
    double Scale,
    bool Fit,
    string? MediaType = null,     // напр. "Heavyweight", "Cardstock" — имя из списка принтера
    int MediaWeight = 0,          // г/м², 0 — не указывать
    int MediaPosition = -1,       // номер лотка (из /InputAttributes драйвера), -1 — не указывать
    bool ManualFeed = false,      // обходной лоток с ручной подачей
    string? TrayName = null);     // название лотка у драйвера, напр. "Лоток 5" — для PJL INPUTTRAY

/// <summary>
/// DeviceCMYK (или DeviceGray для ч/б) PostScript Level 3 для прямой RAW-печати
/// на PostScript-принтер — порт build_cmyk_ps из cmyk_export.py. Уважает опции:
/// копии, duplex/tumble, масштаб, fit. Без GDI/RGB.
/// </summary>
public static class PostScriptWriter
{
    /// <summary>
    /// Собирает словарь setpagedevice с выбранным носителем (пустая строка,
    /// если ничего не задано — тогда решает принтер).
    ///
    /// Лоток выбирается через <c>/InputAttributes</c> — это единственный
    /// документированный в спецификации Adobe PostScript способ указать
    /// конкретный физический лоток: словарь с числовым индексом лотка (тем
    /// самым, что драйвер возвращает как номер лотка) и вложенным
    /// <c>/Priority 1</c>, плюс <c>/Policies /InputAttributes 0</c>, чтобы RIP
    /// не подменял лоток автоматически. Прежняя версия писала несуществующий
    /// ключ <c>/MediaPosition N setpagedevice</c> — это не операторPostScript,
    /// и принтер молча его игнорировал (неизвестные ключи в setpagedevice не
    /// вызывают ошибку, а просто ничего не делают) — печать успешно уходила,
    /// но не из того лотка.
    /// </summary>
    internal static string MediaDict(PsOptions opts)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(opts.MediaType))
            parts.Add($"/MediaType ({EscapePsString(opts.MediaType!.Trim())})");
        if (opts.MediaWeight > 0)
            parts.Add($"/MediaWeight {opts.MediaWeight}");
        if (opts.ManualFeed)
            parts.Add("/ManualFeed true");
        if (opts.MediaPosition >= 0)
        {
            parts.Add($"/InputAttributes << {opts.MediaPosition} << /Priority 1 >> >>");
            parts.Add("/Policies << /InputAttributes 0 >>");
        }
        return parts.Count == 0 ? "" : "<< " + string.Join(" ", parts) + " >>";
    }

    /// <summary>Экранирует строку PostScript: скобки и обратный слэш.</summary>
    private static string EscapePsString(string s) => s
        .Replace("\\", "\\\\")
        .Replace("(", "\\(")
        .Replace(")", "\\)");

    /// <summary>
    /// Обёртка PJL (Printer Job Language) вокруг PostScript-задания.
    ///
    /// На сетевых МФУ (Xerox AltaLink и подобных) `setpagedevice` внутри самого
    /// PostScript на практике не всегда срабатывает для RAW-заданий (спулер,
    /// порт 9100): контроллер принтера согласовывает лоток и тип носителя ДО
    /// того, как передаст байты интерпретатору PostScript, и ждёт эту
    /// информацию в виде команд PJL перед языковым переключателем. Без такой
    /// обёртки запрошенный тип бумаги молча игнорируется, и печать уходит на
    /// бумагу, физически заправленную в лоток по умолчанию — это и есть баг,
    /// который сообщил пользователь на сборке 1.0.2 (там был только setpagedevice).
    ///
    /// Задание оборачивается UEL (Universal Exit Language, ESC%-12345X) — эту
    /// последовательность понимают практически все современные сетевые
    /// принтеры вне зависимости от того, используют они сами PJL, поэтому
    /// обёртка безопасна даже для принтеров без PJL.
    /// </summary>
    internal static byte[] PjlPrefix(PsOptions opts)
    {
        if (!HasMedia(opts)) return Array.Empty<byte>();

        const string Esc = "\u001B";
        var sb = new StringBuilder();
        sb.Append(Esc).Append("%-12345X@PJL JOB NAME=\"iHateCards\"\n");
        if (!string.IsNullOrWhiteSpace(opts.MediaType))
            sb.Append("@PJL SET MEDIATYPE=\"").Append(PjlEscape(opts.MediaType!.Trim())).Append("\"\n");
        if (opts.MediaWeight > 0)
            sb.Append("@PJL SET PAPERWEIGHT=").Append(opts.MediaWeight).Append('\n');
        if (opts.ManualFeed)
            sb.Append("@PJL SET INPUTTRAY=MANUALFEED\n");
        else if (InputTrayKeyword(opts) is { } tray)
            sb.Append("@PJL SET INPUTTRAY=").Append(tray).Append('\n');
        sb.Append("@PJL ENTER LANGUAGE=POSTSCRIPT\n");
        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    internal static byte[] PjlSuffix(PsOptions opts)
    {
        if (!HasMedia(opts)) return Array.Empty<byte>();
        const string Esc = "\u001B";
        return Encoding.Latin1.GetBytes(Esc + "%-12345X@PJL EOJ\n" + Esc + "%-12345X");
    }

    private static bool HasMedia(PsOptions opts) =>
        !string.IsNullOrWhiteSpace(opts.MediaType) || opts.MediaWeight > 0
        || opts.ManualFeed || opts.MediaPosition >= 0;

    /// <summary>
    /// Значение для PJL INPUTTRAY — второй, независимый от /InputAttributes
    /// способ выбрать лоток. Контроллер МФУ (Xerox и подобные) согласовывает
    /// лоток на уровне PJL до PostScript, а числовые индексы /InputAttributes
    /// в PPD принтера — своя, отдельная от Windows нумерация лотков, поэтому
    /// голый номер драйвера может просто не совпасть с ожидаемым PPD индексом.
    /// Если известно имя лотка у драйвера (напр. «Лоток 5»), вытаскиваем из
    /// него номер и собираем стандартный ключ TRAYn — его понимает
    /// подавляющее большинство PJL-принтеров вне зависимости от вендора. Без
    /// имени — тот же приём по числовому /InputAttributes индексу (сработает,
    /// если он и правда совпадает с порядковым номером лотка).
    /// </summary>
    internal static string? InputTrayKeyword(PsOptions opts)
    {
        if (!string.IsNullOrWhiteSpace(opts.TrayName))
        {
            var digits = new string(opts.TrayName!.Where(char.IsDigit).ToArray());
            if (digits.Length > 0) return "TRAY" + digits;
        }
        if (opts.MediaPosition > 0 && opts.MediaPosition <= 20)
            return "TRAY" + opts.MediaPosition;
        return null;
    }

    /// <summary>PJL-значения не переносят кавычки — заменяем на одинарные.</summary>
    private static string PjlEscape(string s) => s.Replace("\"", "'");

    public static byte[] Build(IReadOnlyList<PsPage> pages, PsOptions opts)
    {
        var buf = new MemoryStream();
        void W(string s) => buf.Write(Encoding.Latin1.GetBytes(s));
        void WB(byte[] b) => buf.Write(b, 0, b.Length);

        WB(PjlPrefix(opts));
        W("%!PS-Adobe-3.0\n%%Creator: iHateCards\n%%LanguageLevel: 3\n");
        W($"%%Pages: {pages.Count}\n");
        W("%%EndComments\n");
        int copies = Math.Max(1, opts.Copies);
        if (copies > 1)
            W($"/#copies {copies} def\n");
        if (opts.Duplex)
            W($"<< /Duplex true /Tumble {(opts.Tumble ? "true" : "false")} >> setpagedevice\n");
        else
            W("<< /Duplex false >> setpagedevice\n");

        // Носитель: тип, плотность и лоток. Без этого RIP печатает на бумаге
        // по умолчанию, что бы ни было выбрано в драйвере — RAW идёт мимо него.
        string media = MediaDict(opts);
        if (media.Length > 0)
            W(media + " setpagedevice\n");

        int pageNo = 0;
        foreach (var pg in pages)
        {
            pageNo++;
            byte[] comp = PdfUtil.Zlib(pg.Data);
            string cs = pg.Gray ? "/DeviceGray setcolorspace\n" : "/DeviceCMYK setcolorspace\n";
            string decode = pg.Gray ? "[0 1]" : "[0 1 0 1 0 1 0 1]";
            double cwpt = pg.WidthMm / 25.4 * 72.0;   // размер содержимого в pt (1:1)
            double chpt = pg.HeightMm / 25.4 * 72.0;
            string imgDict =
                $"<< /ImageType 1 /Width {pg.Width} /Height {pg.Height} /BitsPerComponent 8 " +
                $"/Decode {decode} /ImageMatrix [{pg.Width} 0 0 {-pg.Height} 0 {pg.Height}] " +
                "/DataSource currentfile /FlateDecode filter >> image\n";

            W($"%%Page: {pageNo} {pageNo}\n");
            if (opts.Fit)
            {
                // «Подогнать» в стиле Acrobat: берём размер бумаги самого принтера
                // и вписываем содержимое (с сохранением пропорций, по центру).
                W("gsave\n");
                W("currentpagedevice /PageSize get aload pop /ph exch def /pw exch def\n");
                W($"/cw {PdfUtil.Num(cwpt)} def /ch {PdfUtil.Num(chpt)} def\n");
                W("pw cw div ph ch div 2 copy gt { exch } if pop /sc exch def\n");
                W("pw cw sc mul sub 2 div  ph ch sc mul sub 2 div  translate\n");
                W("cw sc mul ch sc mul scale\n");
                W(cs);
                W(imgDict);
            }
            else
            {
                double scale = opts.Scale == 0 ? 1.0 : opts.Scale;
                double wpt = cwpt * scale;
                double hpt = chpt * scale;
                W($"<< /PageSize [{PdfUtil.Num2(wpt)} {PdfUtil.Num2(hpt)}] >> setpagedevice\n");
                W("gsave\n");
                W($"{PdfUtil.Num2(wpt)} {PdfUtil.Num2(hpt)} scale\n");
                W(cs);
                W(imgDict);
            }
            WB(comp);
            W("\ngrestore\nshowpage\n");
        }
        W("%%EOF\n");
        WB(PjlSuffix(opts));
        return buf.ToArray();
    }
}

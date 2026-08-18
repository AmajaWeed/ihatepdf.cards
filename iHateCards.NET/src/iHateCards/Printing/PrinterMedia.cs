using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace iHateCards.Printing;

/// <summary>Тип носителя, поддерживаемый принтером.</summary>
public sealed record MediaOption(string Name, int Id);

/// <summary>
/// Список типов бумаги и лотков принтера.
///
/// Нужен потому, что прямая печать CMYK идёт RAW-заданием мимо драйвера:
/// выбранный в его диалоге тип носителя до принтера не доходит, и печать
/// уходит на бумагу по умолчанию. Поэтому тип бумаги передаётся в самом
/// PostScript, а список допустимых значений берётся у драйвера.
/// </summary>
public static class PrinterMedia
{
    /// <summary>Типовые названия для PostScript-принтеров, если список у драйвера
    /// получить не удалось (или система не Windows).</summary>
    public static readonly string[] CommonTypes =
    {
        "Plain", "Bond", "Recycled", "Heavyweight", "Extra Heavyweight",
        "Cardstock", "Lightweight", "Labels", "Transparency", "Envelope", "Coated"
    };

    /// <summary>Типы носителя, о которых сообщает драйвер принтера.</summary>
    public static List<MediaOption> MediaTypes(string printer)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var names = QueryStrings(printer, DC_MEDIATYPENAMES, 64);
                var ids = QueryDwords(printer, DC_MEDIATYPES);
                if (names.Count > 0)
                    return names.Select((n, i) => new MediaOption(n, i < ids.Count ? ids[i] : 0)).ToList();
            }
            catch
            {
                // драйвер не отвечает — ниже вернём типовой список
            }
        }
        if (OperatingSystem.IsMacOS())
        {
            var real = CupsChoices(printer, "MediaType");
            if (real.Count > 0) return real.Select(n => new MediaOption(n, 0)).ToList();
        }
        return CommonTypes.Select(t => new MediaOption(t, 0)).ToList();
    }

    /// <summary>Лотки принтера: название и номер для /MediaPosition.</summary>
    public static List<MediaOption> Trays(string printer)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var names = QueryStrings(printer, DC_BINNAMES, 24);
                var ids = QueryWords(printer, DC_BINS);
                if (names.Count > 0)
                    return names.Select((n, i) => new MediaOption(n, i < ids.Count ? ids[i] : -1)).ToList();
            }
            catch
            {
                // ниже — пустой список, лоток выберет принтер
            }
        }
        if (OperatingSystem.IsMacOS())
        {
            var real = CupsChoices(printer, "InputSlot");
            if (real.Count > 0) return real.Select(n => new MediaOption(n, 0)).ToList();
        }
        return new List<MediaOption>();
    }

    // ---------------------------------------------------------------- macOS (CUPS)

    /// <summary>
    /// Реальные варианты PPD-опции конкретного принтера — то же, что видит
    /// нативная системная панель печати (её показывают Cocoa-приложения вроде
    /// Clip Studio через NSPrintPanel; у нас такого доступа нет, но сами
    /// значения можно прочитать у CUPS тем же способом, каким их получает она).
    /// Формат строки `lpoptions -l`: "Keyword/UI Text: choice1 *default choice3"
    /// — раскладка звёздочки у выбора по умолчанию нам не важна, берём имена.
    /// </summary>
    private static List<string> CupsChoices(string printer, string keyword)
    {
        try
        {
            var psi = new ProcessStartInfo("lpoptions")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            psi.ArgumentList.Add("-p"); psi.ArgumentList.Add(printer);
            psi.ArgumentList.Add("-l");
            using var proc = Process.Start(psi);
            if (proc == null) return new List<string>();
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(10000);

            foreach (var line in output.Split('\n'))
            {
                int slash = line.IndexOf('/');
                int colon = line.IndexOf(':');
                if (slash < 0 || colon < slash) continue;
                string key = line[..slash].Trim();
                if (!string.Equals(key, keyword, StringComparison.OrdinalIgnoreCase)) continue;

                return line[(colon + 1)..].Trim()
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(tok => tok.TrimStart('*'))
                    .Where(tok => tok.Length > 0)
                    .ToList();
            }
        }
        catch
        {
            // CUPS недоступен или принтер не отвечает — вызывающий код сам
            // подставит типовой список
        }
        return new List<string>();
    }

    // ---------------------------------------------------------------- Windows

    private const int DC_BINS = 6;
    private const int DC_BINNAMES = 12;
    private const int DC_MEDIATYPENAMES = 34;
    private const int DC_MEDIATYPES = 35;

    [SupportedOSPlatform("windows")]
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, EntryPoint = "DeviceCapabilitiesW")]
    private static extern int DeviceCapabilities(string device, string? port, int capability,
        IntPtr output, IntPtr devMode);

    [SupportedOSPlatform("windows")]
    private static List<string> QueryStrings(string printer, int capability, int itemChars)
    {
        var result = new List<string>();
        int count = DeviceCapabilities(printer, null, capability, IntPtr.Zero, IntPtr.Zero);
        if (count <= 0) return result;

        IntPtr buf = Marshal.AllocHGlobal(count * itemChars * sizeof(char));
        try
        {
            if (DeviceCapabilities(printer, null, capability, buf, IntPtr.Zero) <= 0) return result;
            for (int i = 0; i < count; i++)
            {
                string s = Marshal.PtrToStringUni(buf + i * itemChars * sizeof(char), itemChars) ?? "";
                s = s.TrimEnd('\0').Trim();
                if (s.Length > 0) result.Add(s);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
        return result;
    }

    [SupportedOSPlatform("windows")]
    private static List<int> QueryDwords(string printer, int capability)
    {
        var result = new List<int>();
        int count = DeviceCapabilities(printer, null, capability, IntPtr.Zero, IntPtr.Zero);
        if (count <= 0) return result;

        IntPtr buf = Marshal.AllocHGlobal(count * sizeof(int));
        try
        {
            if (DeviceCapabilities(printer, null, capability, buf, IntPtr.Zero) <= 0) return result;
            for (int i = 0; i < count; i++) result.Add(Marshal.ReadInt32(buf, i * sizeof(int)));
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
        return result;
    }

    [SupportedOSPlatform("windows")]
    private static List<int> QueryWords(string printer, int capability)
    {
        var result = new List<int>();
        int count = DeviceCapabilities(printer, null, capability, IntPtr.Zero, IntPtr.Zero);
        if (count <= 0) return result;

        IntPtr buf = Marshal.AllocHGlobal(count * sizeof(short));
        try
        {
            if (DeviceCapabilities(printer, null, capability, buf, IntPtr.Zero) <= 0) return result;
            for (int i = 0; i < count; i++) result.Add(Marshal.ReadInt16(buf, i * sizeof(short)));
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
        return result;
    }
}

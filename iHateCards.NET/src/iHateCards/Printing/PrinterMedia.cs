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
        return new List<MediaOption>();
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

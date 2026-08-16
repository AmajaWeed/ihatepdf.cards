using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace iHateCards.Printing;

/// <summary>
/// Прямая RAW-печать в очередь Windows через спулер (порт winprint.py).
/// Байты (PostScript/PCL/PDF) уходят на принтер с datatype "RAW", минуя
/// GDI/RGB-путь: для PostScript-принтера их интерпретирует RIP самого
/// принтера — DeviceCMYK печатается как настоящий CMYK.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WinSpool
{
    private const string Winspool = "winspool.drv";
    private const string Gdi32 = "gdi32.dll";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DOC_INFO_1
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string pDocName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pOutputFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string pDatatype;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PRINTER_DEFAULTS
    {
        public IntPtr pDatatype;
        public IntPtr pDevMode;
        public uint DesiredAccess;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PRINTER_INFO_9
    {
        public IntPtr pDevMode;
    }

    [DllImport(Winspool, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinterW(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

    [DllImport(Winspool, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinterW(string pPrinterName, out IntPtr phPrinter, ref PRINTER_DEFAULTS pDefault);

    [DllImport(Winspool, SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport(Winspool, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint StartDocPrinterW(IntPtr hPrinter, int level, ref DOC_INFO_1 di);

    [DllImport(Winspool, SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport(Winspool, SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport(Winspool, SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport(Winspool, SetLastError = true)]
    private static extern bool WritePrinter(IntPtr hPrinter, byte[] pBytes, uint dwCount, out uint dwWritten);

    [DllImport(Winspool, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetDefaultPrinterW(char[]? pszBuffer, ref uint pcchBuffer);

    [DllImport(Winspool, CharSet = CharSet.Unicode)]
    private static extern long DocumentPropertiesW(IntPtr hWnd, IntPtr hPrinter,
        string pDeviceName, IntPtr pDevModeOutput, IntPtr pDevModeInput, uint fMode);

    [DllImport(Winspool, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetPrinterW(IntPtr hPrinter, uint level, ref PRINTER_INFO_9 pPrinter, uint command);

    [DllImport(Gdi32, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateDCW(string? driver, string device, string? output, IntPtr devMode);

    [DllImport(Gdi32)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport(Gdi32)]
    private static extern int ExtEscape(IntPtr hdc, int nEscape, int cbInput, byte[]? lpszInData,
        int cbOutput, byte[]? lpszOutData);

    /// <summary>Список принтеров + принтер по умолчанию.</summary>
    public static (List<string> Printers, string Default) ListPrinters()
    {
        var printers = new List<string>();
        foreach (string? p in System.Drawing.Printing.PrinterSettings.InstalledPrinters)
            if (!string.IsNullOrEmpty(p)) printers.Add(p);
        return (printers, GetDefaultPrinter());
    }

    public static string GetDefaultPrinter()
    {
        uint size = 0;
        GetDefaultPrinterW(null, ref size);
        if (size == 0) return "";
        var buf = new char[size];
        if (GetDefaultPrinterW(buf, ref size))
            return new string(buf, 0, (int)size - 1);
        return "";
    }

    /// <summary>RAW-печать байтов в очередь (порт raw_print).</summary>
    public static uint RawPrint(string printerName, byte[] data, string docName = "iHateCards")
    {
        if (!OpenPrinterW(printerName, out IntPtr h, IntPtr.Zero))
            throw new IOException($"OpenPrinter failed for '{printerName}'");
        try
        {
            var doc = new DOC_INFO_1 { pDocName = docName, pOutputFile = null, pDatatype = "RAW" };
            if (StartDocPrinterW(h, 1, ref doc) == 0)
                throw new IOException("StartDocPrinter failed");
            if (!StartPagePrinter(h))
                throw new IOException("StartPagePrinter failed");
            if (!WritePrinter(h, data, (uint)data.Length, out uint written))
                throw new IOException("WritePrinter failed");
            EndPagePrinter(h);
            EndDocPrinter(h);
            return written;
        }
        finally
        {
            ClosePrinter(h);
        }
    }

    /// <summary>True, если драйвер принтера понимает PostScript (RAW PS безопасен).
    /// Не-PS принтеры (EPSON L805 и т.п.) напечатали бы сырой PS как мусор.</summary>
    public static bool IsPostScript(string printerName)
    {
        IntPtr hdc = CreateDCW("WINSPOOL", printerName, null, IntPtr.Zero);
        if (hdc == IntPtr.Zero) return false;
        try
        {
            const int QUERYESCSUPPORT = 8;
            const int POSTSCRIPT_PASSTHROUGH = 4115;
            const int GETTECHNOLOGY = 20;
            byte[] code = BitConverter.GetBytes(POSTSCRIPT_PASSTHROUGH);
            if (ExtEscape(hdc, QUERYESCSUPPORT, code.Length, code, 0, null) > 0)
                return true;
            byte[] tech = BitConverter.GetBytes(GETTECHNOLOGY);
            if (ExtEscape(hdc, QUERYESCSUPPORT, 4, tech, 0, null) > 0)
            {
                var buf = new byte[64];
                if (ExtEscape(hdc, GETTECHNOLOGY, 0, null, 64, buf) > 0)
                {
                    string s = System.Text.Encoding.ASCII.GetString(buf);
                    if (s.Contains("PostScript", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }
        finally
        {
            DeleteDC(hdc);
        }
    }

    /// <summary>Открывает системный диалог свойств принтера и сохраняет выбор
    /// пользователя как пер-пользовательский дефолт (SetPrinter level 9 — без
    /// прав администратора). Порт open_printer_properties.</summary>
    public static int OpenPrinterProperties(string printerName)
    {
        const uint DM_OUT_BUFFER = 2;
        const uint DM_IN_PROMPT = 4;
        const uint DM_IN_BUFFER = 8;
        const uint PRINTER_ACCESS_USE = 0x00000008;

        var defaults = new PRINTER_DEFAULTS { DesiredAccess = PRINTER_ACCESS_USE };
        if (!OpenPrinterW(printerName, out IntPtr h, ref defaults))
            throw new IOException($"OpenPrinter failed for '{printerName}'");
        try
        {
            long need = DocumentPropertiesW(IntPtr.Zero, h, printerName, IntPtr.Zero, IntPtr.Zero, 0);
            if (need < 0) throw new IOException("DocumentProperties (size) failed");
            IntPtr buf = Marshal.AllocHGlobal((int)need);
            try
            {
                // текущий DEVMODE → показать диалог (in=текущий, out=изменённый)
                DocumentPropertiesW(IntPtr.Zero, h, printerName, buf, IntPtr.Zero, DM_OUT_BUFFER);
                long rc = DocumentPropertiesW(IntPtr.Zero, h, printerName, buf, buf,
                    DM_IN_PROMPT | DM_IN_BUFFER | DM_OUT_BUFFER);
                if (rc == 1) // IDOK — сохранить как дефолт пользователя
                {
                    var info = new PRINTER_INFO_9 { pDevMode = buf };
                    SetPrinterW(h, 9, ref info, 0);
                }
                return (int)rc;
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }
        finally
        {
            ClosePrinter(h);
        }
    }
}

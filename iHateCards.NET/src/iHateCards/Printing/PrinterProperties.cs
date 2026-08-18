using System.Diagnostics;

namespace iHateCards.Printing;

/// <summary>Системные параметры принтера (диалог драйвера).</summary>
public static class PrinterProperties
{
    /// <summary>Открывает настройки принтера средствами системы: на Windows —
    /// диалог драйвера (DEVMODE) с сохранением выбора, на macOS — системные
    /// настройки «Принтеры и сканеры».</summary>
    public static void Open(string printer)
    {
        if (OperatingSystem.IsWindows())
        {
            WinSpool.OpenPrinterProperties(printer);
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            // Ventura и новее — панель настроек; на старых системах — prefPane
            if (TryStart("open", "x-apple.systempreferences:com.apple.Print-Scan-Settings.extension")) return;
            if (TryStart("open", "/System/Library/PreferencePanes/PrintAndFax.prefPane")) return;
            throw new PlatformNotSupportedException("Не удалось открыть настройки принтеров");
        }

        throw new PlatformNotSupportedException("Системные параметры принтера недоступны на этой ОС");
    }

    private static bool TryStart(string exe, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            return p != null;
        }
        catch
        {
            return false;
        }
    }
}

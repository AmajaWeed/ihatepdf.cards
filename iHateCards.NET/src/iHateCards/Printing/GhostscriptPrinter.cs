using System.Diagnostics;
using System.Runtime.Versioning;

namespace iHateCards.Printing;

/// <summary>Печать PDF через Ghostscript (mswinpr2) — порт print_pdf/find_ghostscript.</summary>
[SupportedOSPlatform("windows")]
public static class GhostscriptPrinter
{
    public static string? FindGhostscript()
    {
        foreach (var exe in new[] { "gswin64c.exe", "gswin32c.exe" })
        {
            var path = Environment.GetEnvironmentVariable("PATH")?
                .Split(Path.PathSeparator)
                .Select(dir => Path.Combine(dir.Trim(), exe))
                .FirstOrDefault(File.Exists);
            if (path != null) return path;
        }
        foreach (var baseDir in new[] { @"C:\Program Files\gs", @"C:\Program Files (x86)\gs" })
        {
            if (!Directory.Exists(baseDir)) continue;
            foreach (var ver in Directory.GetDirectories(baseDir))
            {
                foreach (var exe in new[] { "gswin64c.exe", "gswin32c.exe" })
                {
                    string p = Path.Combine(ver, "bin", exe);
                    if (File.Exists(p)) return p;
                }
            }
        }
        return null;
    }

    /// <summary>Печатает PDF-файл на принтер. Предпочитает Ghostscript (mswinpr2 —
    /// рендерит PDF и корректно кормит драйвер на любом принтере, вкл. EPSON).
    /// Fallback — системный обработчик PDF через ShellExecute 'printto'.
    /// dpi понижает разрешение растеризации → меньше спул (помогает WSD).</summary>
    public static string PrintPdf(string printerName, string pdfPath, int copies = 1,
        bool fit = false, int? dpi = null)
    {
        string? gs = FindGhostscript();
        if (gs != null)
        {
            var args = new List<string> { "-dNOSAFER", "-dBATCH", "-dNOPAUSE", "-dPrinted", "-dNoCancel", "-q" };
            if (dpi is > 0) args.Add($"-r{dpi}");
            args.Add("-sDEVICE=mswinpr2");
            args.Add("-sOutputFile=%printer%" + printerName);
            if (fit) args.Add("-dPDFFitPage");
            args.Add(pdfPath);

            for (int i = 0; i < Math.Max(1, copies); i++)
            {
                var psi = new ProcessStartInfo { FileName = gs, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                foreach (var a in args) psi.ArgumentList.Add(a);
                using var proc = Process.Start(psi)!;
                string err = proc.StandardError.ReadToEnd();
                proc.WaitForExit();
                if (proc.ExitCode != 0)
                {
                    string msg = err.Length > 400 ? err[..400] : err;
                    throw new IOException("Ghostscript: " + (msg.Length > 0 ? msg : $"exit {proc.ExitCode}"));
                }
            }
            return "PDF (Ghostscript)";
        }

        // Fallback: системный обработчик PDF
        var res = ShellExecuteW(IntPtr.Zero, "printto", pdfPath, $"\"{printerName}\"", null, 0);
        if (res.ToInt64() <= 32)
            throw new IOException(
                $"Ghostscript не найден, и системный обработчик PDF не смог напечатать (код {res}). " +
                "Установите Ghostscript: https://ghostscript.com/releases/gsdnld.html");
        return "PDF (системный обработчик)";
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr ShellExecuteW(IntPtr hwnd, string lpOperation, string lpFile,
        string? lpParameters, string? lpDirectory, int nShowCmd);
}

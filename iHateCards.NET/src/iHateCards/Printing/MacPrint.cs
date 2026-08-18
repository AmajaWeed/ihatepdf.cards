using System.Diagnostics;
using System.Runtime.Versioning;

namespace iHateCards.Printing;

/// <summary>
/// Печать на macOS через CUPS (утилиты lp/lpstat). CUPS сам понимает RAW
/// PostScript, поэтому прямой вывод DeviceCMYK работает без Ghostscript;
/// не-PostScript принтеры получают PDF, который CUPS растеризует драйвером.
/// </summary>
[SupportedOSPlatform("macos")]
public static class MacPrint
{
    /// <summary>Список очередей печати и очередь по умолчанию.</summary>
    public static (List<string> Printers, string Default) ListPrinters()
    {
        var printers = new List<string>();
        foreach (var line in Run("lpstat", "-e").Split('\n'))
        {
            string p = line.Trim();
            if (p.Length > 0) printers.Add(p);
        }

        string def = "";
        string d = Run("lpstat", "-d");
        int idx = d.IndexOf(':');
        if (idx >= 0) def = d[(idx + 1)..].Trim();
        if (def.Length == 0 && printers.Count > 0) def = printers[0];
        return (printers, def);
    }

    /// <summary>RAW-печать: байты уходят на принтер без обработки —
    /// DeviceCMYK PostScript печатается «как есть» RIP'ом принтера.</summary>
    public static void RawPrint(string printer, byte[] data, string docName = "iHateCards")
    {
        var psi = new ProcessStartInfo("lp")
        {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("-d"); psi.ArgumentList.Add(printer);
        psi.ArgumentList.Add("-t"); psi.ArgumentList.Add(docName);
        psi.ArgumentList.Add("-o"); psi.ArgumentList.Add("raw");

        using var proc = Process.Start(psi) ?? throw new IOException("Не удалось запустить lp");
        using (var stdin = proc.StandardInput.BaseStream)
            stdin.Write(data, 0, data.Length);
        string err = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        if (proc.ExitCode != 0)
            throw new IOException("lp: " + (err.Length > 0 ? err.Trim() : $"код {proc.ExitCode}"));
    }

    /// <summary>Печать готового PDF через CUPS (драйвер принтера сам растеризует).</summary>
    public static string PrintPdf(string printer, string pdfPath, int copies, bool fit,
        bool duplex, bool tumble)
    {
        var psi = new ProcessStartInfo("lp")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("-d"); psi.ArgumentList.Add(printer);
        psi.ArgumentList.Add("-n"); psi.ArgumentList.Add(Math.Max(1, copies).ToString());
        if (fit) { psi.ArgumentList.Add("-o"); psi.ArgumentList.Add("fit-to-page"); }
        if (duplex)
        {
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add(tumble ? "sides=two-sided-short-edge" : "sides=two-sided-long-edge");
        }
        else
        {
            psi.ArgumentList.Add("-o"); psi.ArgumentList.Add("sides=one-sided");
        }
        psi.ArgumentList.Add(pdfPath);

        using var proc = Process.Start(psi) ?? throw new IOException("Не удалось запустить lp");
        string err = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        if (proc.ExitCode != 0)
            throw new IOException("lp: " + (err.Length > 0 ? err.Trim() : $"код {proc.ExitCode}"));
        return "PDF (CUPS)";
    }

    private static string Run(string exe, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var proc = Process.Start(psi);
            if (proc == null) return "";
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(10000);
            return output;
        }
        catch
        {
            return "";
        }
    }
}

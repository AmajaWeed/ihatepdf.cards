using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace iHateCards.Printing;

/// <summary>Сетевые пути печати: TCP/9100 и определение IP принтера (порт winprint.py).</summary>
public static class NetPrint
{
    /// <summary>Сырые байты напрямую на принтер по TCP (JetDirect / порт 9100).
    /// Полностью минует спулер/порт Windows — нужно для PostScript-принтеров на
    /// WSD-порту, где RAW-задания спулера не доходят до устройства.</summary>
    public static int RawPrintTcp(string host, byte[] data, int port = 9100, int timeoutSec = 30)
    {
        using var client = new TcpClient();
        if (!client.ConnectAsync(host, port).Wait(TimeSpan.FromSeconds(timeoutSec)))
            throw new IOException($"Не удалось подключиться к {host}:{port}");
        using var stream = client.GetStream();
        stream.WriteTimeout = timeoutSec * 1000;
        stream.Write(data, 0, data.Length);
        stream.Flush();
        return data.Length;
    }

    /// <summary>Пытается определить IP принтера по имени очереди: многие сетевые
    /// принтеры включают суффикс MAC в имя, напр. «Xerox AltaLink (9F:7B:3E)» —
    /// сверяем его с ARP-таблицей. Порт printer_ip_guess.</summary>
    [SupportedOSPlatform("windows")]
    public static string PrinterIpGuess(string name)
    {
        var m = Regex.Match(name ?? "", @"\(?([0-9A-Fa-f]{2}(?:[:\-][0-9A-Fa-f]{2}){1,5})\)?");
        if (!m.Success) return "";
        string suffix = string.Join("-",
            Regex.Split(m.Groups[1].Value, "[:\\-]").Select(p => p.ToUpperInvariant()));
        try
        {
            string output = RunPowerShell(
                "Get-NetNeighbor -AddressFamily IPv4 | " +
                "ForEach-Object {$_.IPAddress + ' ' + $_.LinkLayerAddress}");
            foreach (var line in output.Split('\n'))
            {
                var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    string ip = parts[0];
                    string mac = parts[1].Replace(":", "-").ToUpperInvariant();
                    if (mac.EndsWith(suffix) && ip.Count(ch => ch == '.') == 3)
                        return ip;
                }
            }
        }
        catch
        {
            // best effort
        }
        return "";
    }

    /// <summary>Адреса настроенных Standard TCP/IP портов принтеров — подсказки
    /// для прямого пути PostScript на порт 9100. Порт list_tcp_hosts.</summary>
    [SupportedOSPlatform("windows")]
    public static List<string> ListTcpHosts()
    {
        var hosts = new List<string>();
        try
        {
            string output = RunPowerShell(
                "Get-PrinterPort | Where-Object {$_.PrinterHostAddress} | " +
                "ForEach-Object {$_.PrinterHostAddress}");
            foreach (var line in output.Split('\n'))
            {
                string h = line.Trim();
                if (h.Length > 0 && !hosts.Contains(h)) hosts.Add(h);
            }
        }
        catch
        {
            // best effort
        }
        return hosts;
    }

    [SupportedOSPlatform("windows")]
    private static string RunPowerShell(string command)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            ArgumentList = { "-NoProfile", "-Command", command },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi)!;
        string output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(15000);
        return output;
    }
}

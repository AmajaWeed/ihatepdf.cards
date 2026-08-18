using System.Reflection;

namespace iHateCards.Update;

/// <summary>Версия приложения и сравнение версий (SemVer-подмножество: x.y.z).</summary>
public static class AppVersion
{
    private static string? _current;

    /// <summary>Текущая версия из сборки (<Version> в csproj).</summary>
    public static string Current
    {
        get
        {
            if (_current != null) return _current;
            var info = typeof(AppVersion).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            // InformationalVersion может нести суффикс сборки: 2.1.0+abc1234
            string v = info?.Split('+')[0] ?? typeof(AppVersion).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            return _current = v;
        }
    }

    /// <summary>Идентификатор платформы для выбора пакета обновления.</summary>
    public static string Rid
    {
        get
        {
            string os = OperatingSystem.IsWindows() ? "win"
                : OperatingSystem.IsMacOS() ? "osx"
                : "linux";
            string arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
            {
                System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
                System.Runtime.InteropServices.Architecture.X64 => "x64",
                System.Runtime.InteropServices.Architecture.X86 => "x86",
                _ => "x64"
            };
            return $"{os}-{arch}";
        }
    }

    /// <summary>Сравнение версий: &lt;0 если a старее b, 0 если равны, &gt;0 если новее.</summary>
    public static int Compare(string a, string b)
    {
        var pa = Parse(a);
        var pb = Parse(b);
        for (int i = 0; i < 4; i++)
        {
            int c = pa[i].CompareTo(pb[i]);
            if (c != 0) return c;
        }
        return 0;
    }

    public static bool IsNewer(string candidate, string current) => Compare(candidate, current) > 0;

    private static int[] Parse(string v)
    {
        var parts = (v ?? "").Split('-')[0].Split('.');
        var res = new int[4];
        for (int i = 0; i < 4; i++)
            res[i] = i < parts.Length && int.TryParse(parts[i], out int n) ? n : 0;
        return res;
    }
}

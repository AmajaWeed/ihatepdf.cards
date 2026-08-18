using System.Text.Json.Nodes;

namespace iHateCards.Diagnostics;

/// <summary>
/// Режим разработчика: подробный журнал действий приложения для отладки и
/// последующего исправления багов (печать, обновления, сохранение/открытие
/// проектов). Выключен по умолчанию — включается в диалоге «Диагностика…»,
/// состояние хранится в settings.json.
///
/// Файлы — по одному на день, в %LOCALAPPDATA%\iHateCards\logs\ (или аналог
/// на macOS/Linux). Ничего секретного не пишем: имена файлов, опции печати,
/// сообщения исключений — этого достаточно, чтобы воспроизвести проблему.
/// </summary>
public static class DevLog
{
    private static readonly object Lock = new();
    private static bool? _enabled;

    public static string LogDir
    {
        get
        {
            string d = Path.Combine(SettingsPaths.DataDir, "logs");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    public static string CurrentLogFile => Path.Combine(LogDir, $"{DateTime.Now:yyyy-MM-dd}.log");

    public static bool Enabled
    {
        get
        {
            if (_enabled != null) return _enabled.Value;
            try { _enabled = SettingsStore.Load()["debug"]?["enabled"]?.GetValue<bool>() ?? false; }
            catch { _enabled = false; }
            return _enabled.Value;
        }
        set
        {
            bool wasEnabled = _enabled == true;
            _enabled = value;
            var settings = SettingsStore.Load();
            var dbg = settings["debug"] as JsonObject ?? new JsonObject();
            dbg["enabled"] = value;
            settings["debug"] = dbg;
            SettingsStore.Save(settings);
            if (value && !wasEnabled)
                WriteLine("Debug", $"Режим разработчика включён. iHateCards {Update.AppVersion.Current}, "
                    + $"{Update.AppVersion.Rid}, {Environment.OSVersion}");
        }
    }

    /// <summary>Пишет строку в журнал текущего дня, только если режим включён.</summary>
    public static void Log(string category, string message)
    {
        if (!Enabled) return;
        WriteLine(category, message);
    }

    public static void LogException(string category, string message, Exception ex)
    {
        if (!Enabled) return;
        WriteLine(category, message + "\n" + ex);
    }

    private static void WriteLine(string category, string message)
    {
        try
        {
            lock (Lock)
            {
                string line = $"[{DateTime.Now:HH:mm:ss.fff}] [{category}] {message}";
                File.AppendAllText(CurrentLogFile, line + Environment.NewLine);
            }
        }
        catch
        {
            // журнал не должен ронять приложение, даже если диск недоступен
        }
    }
}

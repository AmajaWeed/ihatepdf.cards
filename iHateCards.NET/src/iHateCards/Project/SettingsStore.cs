using System.Text.Json;
using System.Text.Json.Nodes;

namespace iHateCards;

public static class SettingsPaths
{
    /// <summary>%LOCALAPPDATA%\iHateCards (или аналог на других ОС).</summary>
    public static string DataDir
    {
        get
        {
            string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string d = Path.Combine(baseDir, "iHateCards");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
}

/// <summary>settings.json — та же схема, что в Python-версии (раздел "print").</summary>
public static class SettingsStore
{
    public static JsonObject Load()
    {
        try
        {
            if (File.Exists(SettingsPaths.SettingsFile))
                return JsonNode.Parse(File.ReadAllText(SettingsPaths.SettingsFile)) as JsonObject ?? new JsonObject();
        }
        catch
        {
            // повреждённый файл — начинаем с чистых настроек
        }
        return new JsonObject();
    }

    public static void Save(JsonObject settings)
    {
        File.WriteAllText(SettingsPaths.SettingsFile,
            settings.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
    }
}

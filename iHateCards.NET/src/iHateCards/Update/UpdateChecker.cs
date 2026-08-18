using System.Text.Json.Nodes;

namespace iHateCards.Update;

/// <summary>Пакет обновления под конкретную платформу.</summary>
public sealed record UpdatePackage(string Url, string Sha256, long Size);

/// <summary>Найденное обновление: версия, краткий патчноут, пакет для этой ОС.</summary>
public sealed record UpdateInfo(string Version, string[] Notes, string Published, UpdatePackage Package);

/// <summary>
/// Проверка обновлений: тянет манифест updates.json с публичного адреса,
/// сравнивает версии и отдаёт пакет под текущую платформу.
///
/// Адрес можно переопределить переменной окружения IHATECARDS_UPDATE_URL —
/// используется для локальной проверки обновления без публикации релиза.
/// </summary>
public static class UpdateChecker
{
    public const string DefaultManifestUrl =
        "https://github.com/AmajaWeed/ihatecards-updates/releases/latest/download/updates.json";

    public static string ManifestUrl =>
        Environment.GetEnvironmentVariable("IHATECARDS_UPDATE_URL") is { Length: > 0 } u ? u : DefaultManifestUrl;

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("iHateCards/" + AppVersion.Current);
        return c;
    }

    /// <summary>Проверяет наличие обновления. Возвращает null, если обновления нет,
    /// версия пропущена пользователем, нет пакета под эту ОС или сеть недоступна.</summary>
    public static async Task<UpdateInfo?> CheckAsync(bool ignoreSkipped = false, CancellationToken ct = default)
    {
        Diagnostics.DevLog.Log("Update", $"проверка: url='{ManifestUrl}', текущая версия={AppVersion.Current}, rid={AppVersion.Rid}");
        try
        {
            string json = await Http.GetStringAsync(ManifestUrl, ct);
            var info = Parse(json, AppVersion.Rid);
            if (info == null)
            {
                Diagnostics.DevLog.Log("Update", "манифест разобран, но пакета под эту платформу нет");
                return null;
            }
            if (!AppVersion.IsNewer(info.Version, AppVersion.Current))
            {
                Diagnostics.DevLog.Log("Update", $"последняя версия в манифесте {info.Version} не новее текущей");
                return null;
            }
            if (!ignoreSkipped && IsSkipped(info.Version))
            {
                Diagnostics.DevLog.Log("Update", $"версия {info.Version} пропущена пользователем ранее");
                return null;
            }
            RememberCheck();
            Diagnostics.DevLog.Log("Update", $"найдено обновление: {info.Version}, пакет={info.Package.Url}");
            return info;
        }
        catch (Exception ex)
        {
            // Нет сети / недоступен манифест — тихо продолжаем работу
            Diagnostics.DevLog.LogException("Update", "проверка обновления не удалась", ex);
            return null;
        }
    }

    /// <summary>Разбор манифеста (вынесен отдельно — проверяется в selftest).</summary>
    public static UpdateInfo? Parse(string json, string rid)
    {
        var o = JsonNode.Parse(json) as JsonObject;
        string? latest = o?["latest"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(latest)) return null;

        var packages = o?["packages"] as JsonObject;
        var node = packages?[rid] as JsonObject;
        if (node == null) return null;   // под эту платформу пакета нет

        string url = node["url"]?.GetValue<string>() ?? "";
        if (url.Length == 0) return null;
        string sha = node["sha256"]?.GetValue<string>() ?? "";
        long size = 0;
        try { size = node["size"]?.GetValue<long>() ?? 0; } catch { }

        var notes = new List<string>();
        if (o?["notes"] is JsonArray arr)
            foreach (var n in arr)
                if (n?.GetValue<string>() is { Length: > 0 } line) notes.Add(line);

        string published = o?["published"]?.GetValue<string>() ?? "";
        return new UpdateInfo(latest!, notes.ToArray(), published, new UpdatePackage(url, sha, size));
    }

    // ---------------------------------------------------------------- настройки

    public static bool IsSkipped(string version)
    {
        var arr = SettingsStore.Load()["update"]?["skippedVersions"] as JsonArray;
        if (arr == null) return false;
        foreach (var n in arr)
            if (n?.GetValue<string>() == version) return true;
        return false;
    }

    public static void Skip(string version)
    {
        var settings = SettingsStore.Load();
        var upd = settings["update"] as JsonObject ?? new JsonObject();
        var arr = upd["skippedVersions"] as JsonArray ?? new JsonArray();
        bool exists = arr.Any(n => n?.GetValue<string>() == version);
        if (!exists) arr.Add(version);
        upd["skippedVersions"] = arr;
        settings["update"] = upd;
        SettingsStore.Save(settings);
    }

    private static void RememberCheck()
    {
        var settings = SettingsStore.Load();
        var upd = settings["update"] as JsonObject ?? new JsonObject();
        upd["lastCheck"] = DateTime.UtcNow.ToString("o");
        settings["update"] = upd;
        SettingsStore.Save(settings);
    }

    /// <summary>Автопроверка при запуске (можно отключить в settings.json).</summary>
    public static bool AutoCheckEnabled
    {
        get
        {
            try { return SettingsStore.Load()["update"]?["autoCheck"]?.GetValue<bool>() ?? true; }
            catch { return true; }
        }
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;
using iHateCards.Core;

namespace iHateCards;

/// <summary>Параметры двухсторонней печати конкретного принтера.</summary>
public sealed class DuplexConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>Сторона переворота листа: long (по длинной) | short (tumble).
    /// От неё зависит и зеркалирование раскладки, и угол поворота на обороте.</summary>
    public string FlipEdge { get; set; } = "long";

    /// <summary>Механическое смещение оборота относительно лица, мм.</summary>
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
}

/// <summary>Калибровка сторон: авто-значения (со сканов мишени) и ручные.</summary>
public sealed class CalibrationConfig
{
    /// <summary>Использовать авто-значения; правка любого числа переводит в ручной режим.</summary>
    public bool UseAuto { get; set; } = true;

    public DateTime? MeasuredAt { get; set; }
    public CalibSide AutoFront { get; set; } = new();
    public CalibSide AutoBack { get; set; } = new();
    public CalibSide ManualFront { get; set; } = new();
    public CalibSide ManualBack { get; set; } = new();

    public CalibSide EffectiveFront => UseAuto ? AutoFront : ManualFront;
    public CalibSide EffectiveBack => UseAuto ? AutoBack : ManualBack;
}

/// <summary>Прочие настройки печати, запоминаемые для принтера.</summary>
public sealed class PrintConfig
{
    public bool PostScript { get; set; }
    public string Ip { get; set; } = "";
    public int Dpi { get; set; } = 300;
    public string ScaleMode { get; set; } = "real";
    public double ScalePct { get; set; } = 100;
    public int Copies { get; set; } = 1;
    public bool Bw { get; set; }
    public bool Toner { get; set; }

    /// <summary>Тип носителя для прямой печати PostScript, напр. «Heavyweight».
    /// Пусто — принтер решает сам. Настройки драйвера в этом режиме не
    /// применяются: RAW-задание идёт мимо драйвера.</summary>
    public string MediaType { get; set; } = "";

    /// <summary>Плотность бумаги, г/м² (0 — не указывать).</summary>
    public int MediaWeight { get; set; }

    /// <summary>Номер лотка (-1 — выбирает принтер).</summary>
    public int MediaPosition { get; set; } = -1;

    /// <summary>Название лотка у драйвера (напр. «Лоток 5») — вторая, более
    /// надёжная опора для выбора лотка через PJL INPUTTRAY, поскольку
    /// числовой MediaPosition — это индекс из Windows DeviceCapabilities,
    /// который не обязан совпадать с нумерацией лотков в PPD принтера.</summary>
    public string MediaTrayName { get; set; } = "";

    /// <summary>Ручная подача (обходной лоток).</summary>
    public bool ManualFeed { get; set; }
}

/// <summary>
/// Профиль принтера — файл конфигурации двухсторонней печати (.hateprn).
/// Человекочитаемый JSON: правится и в приложении, и в текстовом редакторе.
/// Хранится в %LOCALAPPDATA%\iHateCards\printers\, переносится между машинами
/// через «Экспорт…/Импорт…».
/// </summary>
public sealed class PrinterProfile
{
    public const int FormatVersion = 1;
    public const string Extension = ".hateprn";

    public string Printer { get; set; } = "";
    public DuplexConfig Duplex { get; set; } = new();
    public CalibrationConfig Calibration { get; set; } = new();
    public PrintConfig Print { get; set; } = new();

    // ---------------------------------------------------------------- хранилище

    public static string ProfilesDir
    {
        get
        {
            string d = Path.Combine(SettingsPaths.DataDir, "printers");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    public static string PathFor(string printer)
    {
        var safe = new string((printer ?? "printer")
            .Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray());
        if (safe.Length == 0) safe = "printer";
        return Path.Combine(ProfilesDir, safe + Extension);
    }

    /// <summary>Профиль принтера: с диска, иначе новый с миграцией старых
    /// настроек из settings.json (printerPS/printerIP и общие параметры).</summary>
    public static PrinterProfile Load(string printer)
    {
        string path = PathFor(printer);
        if (File.Exists(path))
        {
            try { return Read(File.ReadAllText(path), printer); }
            catch { /* повреждённый профиль — начинаем с чистого */ }
        }
        return Migrate(printer);
    }

    public void Save() => File.WriteAllText(PathFor(Printer), ToJson().ToJsonString(Indented));

    public void SaveAs(string path) => File.WriteAllText(path, ToJson().ToJsonString(Indented));

    public static PrinterProfile Import(string path) =>
        Read(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    // ---------------------------------------------------------------- JSON

    public JsonObject ToJson() => new()
    {
        ["formatVersion"] = FormatVersion,
        ["printer"] = Printer,
        ["duplex"] = new JsonObject
        {
            ["enabled"] = Duplex.Enabled,
            ["flipEdge"] = Duplex.FlipEdge,
            ["offsetX"] = Duplex.OffsetX,
            ["offsetY"] = Duplex.OffsetY
        },
        ["calibration"] = new JsonObject
        {
            ["useAuto"] = Calibration.UseAuto,
            ["measuredAt"] = Calibration.MeasuredAt?.ToString("o"),
            ["auto"] = new JsonObject
            {
                ["front"] = Side(Calibration.AutoFront),
                ["back"] = Side(Calibration.AutoBack)
            },
            ["manual"] = new JsonObject
            {
                ["front"] = Side(Calibration.ManualFront),
                ["back"] = Side(Calibration.ManualBack)
            }
        },
        ["print"] = new JsonObject
        {
            ["postscript"] = Print.PostScript,
            ["ip"] = Print.Ip,
            ["dpi"] = Print.Dpi,
            ["scaleMode"] = Print.ScaleMode,
            ["scalePct"] = Print.ScalePct,
            ["copies"] = Print.Copies,
            ["bw"] = Print.Bw,
            ["toner"] = Print.Toner,
            ["mediaType"] = Print.MediaType,
            ["mediaWeight"] = Print.MediaWeight,
            ["mediaPosition"] = Print.MediaPosition,
            ["mediaTrayName"] = Print.MediaTrayName,
            ["manualFeed"] = Print.ManualFeed
        }
    };

    public static PrinterProfile Read(string json, string fallbackPrinter)
    {
        var o = JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidDataException("Файл не является профилем принтера (.hateprn)");
        var p = new PrinterProfile { Printer = Str(o["printer"]) ?? fallbackPrinter };

        var d = o["duplex"];
        p.Duplex = new DuplexConfig
        {
            Enabled = B(d?["enabled"], true),
            FlipEdge = Str(d?["flipEdge"]) == "short" ? "short" : "long",
            OffsetX = D(d?["offsetX"], 0),
            OffsetY = D(d?["offsetY"], 0)
        };

        var c = o["calibration"];
        p.Calibration = new CalibrationConfig
        {
            UseAuto = B(c?["useAuto"], true),
            MeasuredAt = DateTime.TryParse(Str(c?["measuredAt"]), out var t) ? t : null,
            AutoFront = SideFrom(c?["auto"]?["front"]),
            AutoBack = SideFrom(c?["auto"]?["back"]),
            ManualFront = SideFrom(c?["manual"]?["front"]),
            ManualBack = SideFrom(c?["manual"]?["back"])
        };

        var pr = o["print"];
        p.Print = new PrintConfig
        {
            PostScript = B(pr?["postscript"], false),
            Ip = Str(pr?["ip"]) ?? "",
            Dpi = (int)D(pr?["dpi"], 300),
            ScaleMode = Str(pr?["scaleMode"]) ?? "real",
            ScalePct = D(pr?["scalePct"], 100),
            Copies = Math.Max(1, (int)D(pr?["copies"], 1)),
            Bw = B(pr?["bw"], false),
            Toner = B(pr?["toner"], false),
            MediaType = Str(pr?["mediaType"]) ?? "",
            MediaWeight = (int)D(pr?["mediaWeight"], 0),
            MediaPosition = (int)D(pr?["mediaPosition"], -1),
            MediaTrayName = Str(pr?["mediaTrayName"]) ?? "",
            ManualFeed = B(pr?["manualFeed"], false)
        };
        return p;
    }

    /// <summary>Первый запуск после обновления: переносим то, что раньше лежало
    /// в settings.json (раздел print с картами printerPS/printerIP).</summary>
    private static PrinterProfile Migrate(string printer)
    {
        var p = new PrinterProfile { Printer = printer };
        try
        {
            var s = SettingsStore.Load()["print"] as JsonObject;
            if (s == null) return p;
            p.Print.PostScript = B((s["printerPS"] as JsonObject)?[printer], false);
            p.Print.Ip = Str((s["printerIP"] as JsonObject)?[printer]) ?? "";
            p.Print.Dpi = (int)D(s["dpi"], 300);
            p.Print.ScaleMode = Str(s["scaleMode"]) ?? "real";
            p.Print.ScalePct = D(s["scalePct"], 100);
            p.Print.Copies = Math.Max(1, (int)D(s["copies"], 1));
            p.Print.Bw = B(s["bw"], false);
            p.Print.Toner = B(s["toner"], false);
        }
        catch
        {
            // настроек нет или они битые — профиль остаётся со значениями по умолчанию
        }
        return p;
    }

    // ---------------------------------------------------------------- связь с состоянием

    /// <summary>Применяет профиль к раскладке (сторона переворота, смещения, калибровка).</summary>
    public void ApplyTo(AppState s)
    {
        s.DuplexFlipEdge = Duplex.FlipEdge;
        s.OffsetX = Duplex.OffsetX;
        s.OffsetY = Duplex.OffsetY;
        s.CalibFront = Calibration.EffectiveFront.Clone();
        s.CalibBack = Calibration.EffectiveBack.Clone();
    }

    /// <summary>Записывает результат авто-калибровки (сканы мишени) в профиль.</summary>
    public void SetAutoCalibration(string side, CalibSide value)
    {
        if (side == "back") Calibration.AutoBack = value.Clone();
        else Calibration.AutoFront = value.Clone();
        Calibration.MeasuredAt = DateTime.UtcNow;
        Calibration.UseAuto = true;
    }

    private static JsonObject Side(CalibSide c) =>
        new() { ["dx"] = c.Dx, ["dy"] = c.Dy, ["angle"] = c.Angle };

    private static CalibSide SideFrom(JsonNode? n) => new()
    {
        Dx = D(n?["dx"], 0),
        Dy = D(n?["dy"], 0),
        Angle = D(n?["angle"], 0)
    };

    private static string? Str(JsonNode? n)
    {
        try { return n?.GetValue<string>(); } catch { return null; }
    }

    private static double D(JsonNode? n, double def)
    {
        try { return n?.GetValue<double>() ?? def; } catch { }
        try { return n?.GetValue<int>() ?? def; } catch { return def; }
    }

    private static bool B(JsonNode? n, bool def)
    {
        try { return n?.GetValue<bool>() ?? def; } catch { return def; }
    }
}

using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using iHateCards.Core;
using iHateCards.Imaging;

namespace iHateCards;

/// <summary>
/// Формат проекта .hate — ZIP-контейнер (как .docx):
///   manifest.json       — formatVersion, метаданные
///   layout.json         — вся раскладка + ссылки на ассеты
///   print-settings.json — текущие настройки печати
///   assets/             — оригинальные изображения карт без пересжатия
/// </summary>
public static class HateFile
{
    public const int FormatVersion = 1;

    public static void Save(AppState s, string path)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var assets = new Dictionary<ImageEntry, string>();
            int n = 0;

            string AddAsset(ImageEntry e, string role)
            {
                if (assets.TryGetValue(e, out var existing)) return existing;
                n++;
                string name = $"assets/card-{n:000}-{role}{e.Extension}";
                assets[e] = name;
                var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
                using var st = entry.Open();
                st.Write(e.OriginalBytes);
                return name;
            }

            var images = new JsonArray();
            foreach (var img in s.Images)
            {
                var jo = new JsonObject
                {
                    ["name"] = img.Name,
                    ["asset"] = AddAsset(img, "face"),
                    ["quantity"] = img.Quantity
                };
                if (img.BackImage != null)
                {
                    jo["back"] = new JsonObject
                    {
                        ["name"] = img.BackImage.Name,
                        ["asset"] = AddAsset(img.BackImage, "back")
                    };
                }
                images.Add(jo);
            }

            JsonObject? backJo = null;
            if (s.BackImage != null)
            {
                backJo = new JsonObject
                {
                    ["name"] = s.BackImage.Name,
                    ["asset"] = AddAsset(s.BackImage, "backside")
                };
            }

            var layout = new JsonObject
            {
                ["paperSize"] = s.PaperSizeKey,
                ["cardWidth"] = s.CardWidth,
                ["cardHeight"] = s.CardHeight,
                ["bleed"] = s.Bleed,
                ["photoLayout"] = s.PhotoLayout,
                ["duplexMode"] = s.DuplexMode,
                ["showCropMarks"] = s.ShowCropMarks,
                ["noHaloMarks"] = s.NoHaloMarks,
                ["borderless"] = s.Borderless,
                ["calibMode"] = s.CalibMode,
                ["calibration"] = new JsonObject
                {
                    ["front"] = CalibJson(s.CalibFront),
                    ["back"] = CalibJson(s.CalibBack)
                },
                ["fitImage"] = s.FitImage,
                ["autoRotate"] = s.AutoRotate,
                ["offsetX"] = s.OffsetX,
                ["offsetY"] = s.OffsetY,
                ["polaroidMode"] = s.PolaroidMode,
                ["polaroidSide"] = s.PolaroidSide,
                ["polaroidTop"] = s.PolaroidTop,
                ["polaroidBottom"] = s.PolaroidBottom,
                ["prePolaroidWidth"] = s.PrePolaroidWidth,
                ["prePolaroidHeight"] = s.PrePolaroidHeight,
                ["individualBacks"] = s.IndividualBacks,
                ["images"] = images,
                ["backImage"] = backJo
            };

            WriteJson(zip, "manifest.json", new JsonObject
            {
                ["formatVersion"] = FormatVersion,
                ["app"] = "iHateCards",
                ["created"] = DateTime.UtcNow.ToString("o"),
                ["modified"] = DateTime.UtcNow.ToString("o")
            });
            WriteJson(zip, "layout.json", layout);
            WriteJson(zip, "print-settings.json", SettingsStore.Load());
        }
        File.WriteAllBytes(path, ms.ToArray());
    }

    public static AppState Open(string path)
    {
        using var zip = ZipFile.OpenRead(path);

        var manifest = ReadJson(zip, "manifest.json")
            ?? throw new InvalidDataException("Файл не является проектом iHateCards (.hate): нет manifest.json");
        int ver = manifest["formatVersion"]?.GetValue<int>() ?? 0;
        if (ver < 1)
            throw new InvalidDataException("Неизвестная версия формата .hate");
        // formatVersion > 1: открываем что понимаем (совместимость вперёд)

        var layout = ReadJson(zip, "layout.json")
            ?? throw new InvalidDataException("В проекте нет layout.json");

        var s = new AppState
        {
            PaperSizeKey = layout["paperSize"]?.GetValue<string>() ?? "a4",
            CardWidth = D(layout["cardWidth"], 65),
            CardHeight = D(layout["cardHeight"], 90),
            Bleed = D(layout["bleed"], 0),
            PhotoLayout = (int)D(layout["photoLayout"], 0),
            DuplexMode = B(layout["duplexMode"]),
            ShowCropMarks = B(layout["showCropMarks"], true),
            NoHaloMarks = B(layout["noHaloMarks"]),
            Borderless = B(layout["borderless"]),
            CalibMode = B(layout["calibMode"]),
            FitImage = B(layout["fitImage"]),
            AutoRotate = B(layout["autoRotate"]),
            OffsetX = D(layout["offsetX"], 0),
            OffsetY = D(layout["offsetY"], 0),
            PolaroidMode = B(layout["polaroidMode"]),
            PolaroidSide = D(layout["polaroidSide"], 3),
            PolaroidTop = D(layout["polaroidTop"], 3),
            PolaroidBottom = D(layout["polaroidBottom"], 15),
            PrePolaroidWidth = D(layout["prePolaroidWidth"], 65),
            PrePolaroidHeight = D(layout["prePolaroidHeight"], 90),
            IndividualBacks = B(layout["individualBacks"])
        };

        var calib = layout["calibration"];
        s.CalibFront = CalibFromJson(calib?["front"]);
        s.CalibBack = CalibFromJson(calib?["back"]);

        ImageEntry LoadAsset(JsonNode node)
        {
            string name = node["name"]?.GetValue<string>() ?? "card";
            string asset = node["asset"]?.GetValue<string>()
                ?? throw new InvalidDataException("В layout.json нет ссылки на ассет");
            var entry = zip.GetEntry(asset)
                ?? throw new InvalidDataException($"В проекте нет файла {asset}");
            using var st = entry.Open();
            using var ms = new MemoryStream();
            st.CopyTo(ms);
            byte[] bytes = ms.ToArray();
            var frames = RasterDecoder.Decode(bytes, name + Path.GetExtension(asset));
            if (frames.Count == 0)
                throw new InvalidDataException($"Не удалось декодировать {asset}");
            // Оригинальные байты сохраняем как есть — для последующих пересохранений
            var e = RasterDecoder.ToEntry(frames[0]);
            e.Name = name;
            e.OriginalBytes = bytes;
            e.Extension = Path.GetExtension(asset);
            return e;
        }

        foreach (var node in layout["images"]?.AsArray() ?? new JsonArray())
        {
            if (node == null) continue;
            var img = LoadAsset(node);
            img.Quantity = Math.Clamp((int)D(node["quantity"], 1), 1, 99);
            if (node["back"] is JsonNode backNode)
                img.BackImage = LoadAsset(backNode);
            s.Images.Add(img);
        }
        if (layout["backImage"] is JsonNode bi)
            s.BackImage = LoadAsset(bi);

        // Настройки печати проекта — мягко вливаем в текущие настройки
        if (ReadJson(zip, "print-settings.json") is JsonObject ps && ps["print"] != null)
        {
            var settings = SettingsStore.Load();
            settings["print"] = ps["print"]!.DeepClone();
            SettingsStore.Save(settings);
        }

        LayoutEngine.CalculateLayout(s);
        return s;
    }

    private static JsonObject CalibJson(CalibSide c) =>
        new() { ["dx"] = c.Dx, ["dy"] = c.Dy, ["angle"] = c.Angle };

    private static CalibSide CalibFromJson(JsonNode? node) => new()
    {
        Dx = D(node?["dx"], 0),
        Dy = D(node?["dy"], 0),
        Angle = D(node?["angle"], 0)
    };

    private static double D(JsonNode? n, double def)
    {
        try { return n?.GetValue<double>() ?? def; }
        catch { return def; }
    }

    private static bool B(JsonNode? n, bool def = false)
    {
        try { return n?.GetValue<bool>() ?? def; }
        catch { return def; }
    }

    private static void WriteJson(ZipArchive zip, string name, JsonObject obj)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var st = entry.Open();
        using var writer = new Utf8JsonWriter(st, new JsonWriterOptions { Indented = true });
        obj.WriteTo(writer);
    }

    private static JsonObject? ReadJson(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name);
        if (entry == null) return null;
        using var st = entry.Open();
        using var ms = new MemoryStream();
        st.CopyTo(ms);
        return JsonNode.Parse(ms.ToArray()) as JsonObject;
    }
}

using SkiaSharp;

namespace iHateCards;

/// <summary>
/// Выбор шрифта интерфейса и печатных страниц: основной — Century Gothic
/// (поставляется с MS Office, есть не везде), запасной — вшитый Jost
/// (SIL OFL, геометрический гротеск с кириллицей).
/// </summary>
public static class AppFonts
{
    public const string Primary = "Century Gothic";
    public const string FallbackName = "Jost";

    /// <summary>Ресурс запасного шрифта для Avalonia.</summary>
    public const string FallbackAvares = "avares://iHateCards/Assets/Fonts#Jost";

    private static bool? _hasCenturyGothic;

    /// <summary>Установлен ли Century Gothic в системе.</summary>
    public static bool HasCenturyGothic
    {
        get
        {
            _hasCenturyGothic ??= SKFontManager.Default.FontFamilies
                .Any(f => string.Equals(f, Primary, StringComparison.OrdinalIgnoreCase));
            return _hasCenturyGothic.Value;
        }
    }

    /// <summary>Строка FontFamily для интерфейса.</summary>
    public static string UiFontFamily =>
        HasCenturyGothic ? $"{Primary}, {FallbackAvares}" : FallbackAvares;

    private static SKTypeface? _printTypeface;

    /// <summary>Шрифт для текста на печатных страницах (инструкция «70%»,
    /// подписи мишени калибровки) — тот же, что в интерфейсе.</summary>
    public static SKTypeface PrintTypeface
    {
        get
        {
            if (_printTypeface != null) return _printTypeface;

            if (HasCenturyGothic)
            {
                var cg = SKTypeface.FromFamilyName(Primary);
                // Century Gothic без кириллицы бесполезен для наших подписей
                if (cg != null && cg.GetGlyph('Я') != 0)
                    return _printTypeface = cg;
            }

            using var stream = typeof(AppFonts).Assembly
                .GetManifestResourceStream("iHateCards.Assets.Fonts.Jost-Regular.ttf");
            if (stream != null)
            {
                var tf = SKTypeface.FromStream(stream);
                if (tf != null) return _printTypeface = tf;
            }

            // Последний рубеж: любой системный шрифт с кириллицей
            return _printTypeface = SKFontManager.Default.MatchCharacter('Я') ?? SKTypeface.Default;
        }
    }
}

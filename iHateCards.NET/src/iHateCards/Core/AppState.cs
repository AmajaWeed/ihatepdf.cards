using iHateCards.Imaging;

namespace iHateCards.Core;

public sealed class CalibSide
{
    public double Dx { get; set; }
    public double Dy { get; set; }
    public double Angle { get; set; }

    public bool IsZero => Dx == 0 && Dy == 0 && Angle == 0;
    public CalibSide Clone() => new() { Dx = Dx, Dy = Dy, Angle = Angle };
}

/// <summary>Состояние всей раскладки — порт объекта `state` из iHateCards.html.</summary>
public sealed class AppState
{
    public string PaperSizeKey = "a4";
    public double CardWidth = 65;
    public double CardHeight = 90;
    public double Bleed = 0;
    public int PhotoLayout = 0;              // 0 = обычный режим, 2/4 — фото-раскладка на A5/A6
    public bool DuplexMode = false;
    public bool ShowCropMarks = true;
    public bool NoHaloMarks = false;
    public bool Borderless = false;
    public bool CalibMode = false;
    public CalibSide CalibFront = new();
    public CalibSide CalibBack = new();
    public bool FitImage = false;
    /// <summary>Авто-разворот изображения внутри кадра — значение по умолчанию
    /// для новых карт (у каждой карты есть свой чекбокс).</summary>
    public bool AutoRotate = false;
    /// <summary>Авто-разворот самого кадра на листе: если карта, повёрнутая на
    /// 90°, помещается на лист в большем количестве — раскладка разворачивается.</summary>
    public bool AutoRotateFrame = false;
    /// <summary>Сторона переворота при двухсторонней печати: long | short.
    /// Берётся из профиля принтера (.hateprn), влияет на зеркалирование и
    /// на угол поворота содержимого оборота.</summary>
    public string DuplexFlipEdge = "long";
    public double OffsetX = 0;
    public double OffsetY = 0;
    public string CurrentSide = "front";
    public bool PolaroidMode = false;
    public double PolaroidSide = 3;
    public double PolaroidTop = 3;
    public double PolaroidBottom = 15;
    public double PrePolaroidWidth = 65;
    public double PrePolaroidHeight = 90;
    public List<ImageEntry> Images = new();
    public ImageEntry? BackImage;
    public bool IndividualBacks = false;
    public int CurrentPage = 0;

    // Вычисляется CalculateLayout()
    /// <summary>Кадр развёрнут на 90° (решает авто-разворот кадра).</summary>
    public bool FrameRotated;
    public int CardsPerRow;
    public int CardsPerCol;
    public int CardsPerPage;
    public int TotalPages = 1;

    public PaperSize Paper => PaperSizes.ByKey(PaperSizeKey);

    /// <summary>Размер реза самой карты (логический, для показа в интерфейсе).</summary>
    public double CutWidth => CardWidth - Bleed * 2;
    public double CutHeight => CardHeight - Bleed * 2;

    /// <summary>Размер ячейки на листе — с учётом разворота кадра.</summary>
    public double CellWidth => FrameRotated ? CardHeight : CardWidth;
    public double CellHeight => FrameRotated ? CardWidth : CardHeight;
    public double CellCutWidth => CellWidth - Bleed * 2;
    public double CellCutHeight => CellHeight - Bleed * 2;

    public CalibSide GetCalib(string side) => side == "back" ? CalibBack : CalibFront;

    public CalibSide ActiveCalib(string side) =>
        CalibMode ? GetCalib(side) : new CalibSide();

    public bool HasAnyBack()
    {
        if (IndividualBacks) return Images.Any(i => i.BackImage != null);
        return BackImage != null;
    }

    public int TotalCardCount() => Images.Sum(i => i.Quantity);

    /// <summary>Раскрывает список карт по количеству копий (порт getExpandedImages).</summary>
    public List<ImageEntry> ExpandedImages()
    {
        var list = new List<ImageEntry>();
        foreach (var img in Images)
            for (int i = 0; i < img.Quantity; i++)
                list.Add(img);
        return list;
    }
}

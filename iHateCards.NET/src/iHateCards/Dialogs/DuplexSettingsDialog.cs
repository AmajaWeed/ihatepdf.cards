using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using iHateCards.Core;

namespace iHateCards.Dialogs;

/// <summary>
/// Настройки двухсторонней печати выбранного принтера (профиль .hateprn):
/// сторона переворота, смещения оборота и калибровка сторон — авто-значения
/// со сканов мишени или заданные вручную. Вынесено из окна печати, чтобы там
/// оставалось только самое нужное.
/// </summary>
public sealed class DuplexSettingsDialog : Window
{
    private readonly ComboBox _flipEdge;
    private readonly NumericUpDown _offsetX, _offsetY;
    private readonly CheckBox _useAuto;
    private readonly NumericUpDown _fDx, _fDy, _fAng, _bDx, _bDy, _bAng;
    private readonly TextBlock _measuredAt, _profilePath;
    private PrinterProfile _profile;
    private bool _loading;

    /// <summary>Изменённый профиль (сохраняется вызывающей стороной).</summary>
    public PrinterProfile Profile => _profile;

    public DuplexSettingsDialog(PrinterProfile profile)
    {
        _profile = profile;
        Title = "Двухсторонняя печать";
        SizeToContent = SizeToContent.Height;
        Width = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        Background = new SolidColorBrush(Color.Parse("#1d1d1d"));

        _flipEdge = new ComboBox
        {
            ItemsSource = new[] { "По длинной стороне (обычно)", "По короткой стороне (tumble)" },
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _offsetX = Num(-50, 50, "0.##", 90);
        _offsetY = Num(-50, 50, "0.##", 90);
        _useAuto = new CheckBox { Content = "Авто-настройка (значения со сканов мишени)" };
        _fDx = Num(-50, 50, "0.##", 80); _fDy = Num(-50, 50, "0.##", 80); _fAng = Num(-50, 50, "0.##", 80);
        _bDx = Num(-50, 50, "0.##", 80); _bDy = Num(-50, 50, "0.##", 80); _bAng = Num(-50, 50, "0.##", 80);
        _measuredAt = Hint("");
        _profilePath = Hint("");

        foreach (var n in new[] { _fDx, _fDy, _fAng, _bDx, _bDy, _bAng })
            n.ValueChanged += (_, _) => { if (!_loading) _useAuto.IsChecked = false; };
        _useAuto.IsCheckedChanged += (_, _) => { if (!_loading) SyncCalibFields(); };

        var exportBtn = Small("Экспорт…");
        var importBtn = Small("Импорт…");
        exportBtn.Click += async (_, _) => await ExportProfile();
        importBtn.Click += async (_, _) => await ImportProfile();

        var okBtn = new Button { Content = "Готово", Padding = new Thickness(18, 8) };
        okBtn.Classes.Add("accent");
        okBtn.Click += (_, _) => { SaveFromUi(); _profile.Save(); Close(); };
        var cancelBtn = new Button { Content = "Отмена", Padding = new Thickness(18, 8) };
        cancelBtn.Classes.Add("secondary");
        cancelBtn.Click += (_, _) => Close();

        LoadToUi();

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10,
            Children =
            {
                Label("Переворот листа:"),
                _flipEdge,
                Hint("От стороны переворота зависит и зеркалирование раскладки, и разворот рубашки — иначе она печатается вверх ногами."),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8,
                    Children = { Label("Смещение оборота, мм:"), _offsetX, _offsetY }
                },
                _useAuto,
                Hint("Снимите галку, чтобы задать поправки вручную; правка любого поля тоже переключает в ручной режим."),
                CalibRow("Лицо:", _fDx, _fDy, _fAng),
                CalibRow("Оборот:", _bDx, _bDy, _bAng),
                _measuredAt,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { exportBtn, importBtn } },
                _profilePath,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancelBtn, okBtn }
                }
            }
        };
    }

    private void LoadToUi()
    {
        _loading = true;
        _flipEdge.SelectedIndex = _profile.Duplex.FlipEdge == "short" ? 1 : 0;
        _offsetX.Value = (decimal)_profile.Duplex.OffsetX;
        _offsetY.Value = (decimal)_profile.Duplex.OffsetY;
        _useAuto.IsChecked = _profile.Calibration.UseAuto;
        _measuredAt.Text = _profile.Calibration.MeasuredAt is { } t
            ? $"Авто-значения измерены: {t.ToLocalTime():dd.MM.yyyy HH:mm}"
            : "Авто-значения ещё не измерены (загрузите сканы мишени в разделе «Калибровка»).";
        _profilePath.Text = "Файл профиля: " + PrinterProfile.PathFor(_profile.Printer);
        _loading = false;
        SyncCalibFields();
    }

    private void SyncCalibFields()
    {
        _loading = true;
        bool auto = _useAuto.IsChecked == true;
        var f = auto ? _profile.Calibration.AutoFront : _profile.Calibration.ManualFront;
        var b = auto ? _profile.Calibration.AutoBack : _profile.Calibration.ManualBack;
        _fDx.Value = (decimal)f.Dx; _fDy.Value = (decimal)f.Dy; _fAng.Value = (decimal)f.Angle;
        _bDx.Value = (decimal)b.Dx; _bDy.Value = (decimal)b.Dy; _bAng.Value = (decimal)b.Angle;
        _loading = false;
    }

    private void SaveFromUi()
    {
        _profile.Duplex.FlipEdge = _flipEdge.SelectedIndex == 1 ? "short" : "long";
        _profile.Duplex.OffsetX = (double)(_offsetX.Value ?? 0);
        _profile.Duplex.OffsetY = (double)(_offsetY.Value ?? 0);

        bool auto = _useAuto.IsChecked == true;
        _profile.Calibration.UseAuto = auto;
        var front = auto ? _profile.Calibration.AutoFront : _profile.Calibration.ManualFront;
        var back = auto ? _profile.Calibration.AutoBack : _profile.Calibration.ManualBack;
        front.Dx = (double)(_fDx.Value ?? 0); front.Dy = (double)(_fDy.Value ?? 0); front.Angle = (double)(_fAng.Value ?? 0);
        back.Dx = (double)(_bDx.Value ?? 0); back.Dy = (double)(_bDy.Value ?? 0); back.Angle = (double)(_bAng.Value ?? 0);
    }

    private async Task ExportProfile()
    {
        SaveFromUi();
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Экспорт профиля принтера",
            SuggestedFileName = _profile.Printer + PrinterProfile.Extension,
            FileTypeChoices = new[] { ProfileType }
        });
        var path = file?.TryGetLocalPath();
        if (path == null) return;
        if (!path.EndsWith(PrinterProfile.Extension, StringComparison.OrdinalIgnoreCase))
            path += PrinterProfile.Extension;
        _profile.SaveAs(path);
    }

    private async Task ImportProfile()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Импорт профиля принтера",
            AllowMultiple = false,
            FileTypeFilter = new[] { ProfileType }
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path == null) return;
        try
        {
            var imported = PrinterProfile.Import(path);
            imported.Printer = _profile.Printer;   // применяем к текущему принтеру
            _profile = imported;
            LoadToUi();
        }
        catch (Exception ex)
        {
            await Msg.Show(this, "Ошибка импорта", ex.Message);
        }
    }

    private static FilePickerFileType ProfileType =>
        new("Профиль принтера iHateCards") { Patterns = new[] { "*" + PrinterProfile.Extension } };

    private static NumericUpDown Num(double min, double max, string fmt, double width) => new()
    {
        Minimum = (decimal)min,
        Maximum = (decimal)max,
        Increment = (decimal)0.1,
        FormatString = fmt,
        Width = width,
        ShowButtonSpinner = false
    };

    private static Control CalibRow(string label, NumericUpDown dx, NumericUpDown dy, NumericUpDown ang)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        sp.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Width = 62 });
        foreach (var (cap, ctl) in new[] { ("dx", dx), ("dy", dy), ("угол", ang) })
        {
            sp.Children.Add(new TextBlock { Text = cap, VerticalAlignment = VerticalAlignment.Center, Foreground = Gray() });
            sp.Children.Add(ctl);
        }
        return sp;
    }

    private static Button Small(string text)
    {
        var b = new Button { Content = text, Padding = new Thickness(12, 4) };
        b.Classes.Add("secondary");
        return b;
    }

    private static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center };

    private static IBrush Gray() => new SolidColorBrush(Color.Parse("#9a9ea8"));

    private static TextBlock Hint(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 11,
        Foreground = new SolidColorBrush(Color.Parse("#9a9ea8"))
    };
}

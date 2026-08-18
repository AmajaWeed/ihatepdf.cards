using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using iHateCards.Core;
using iHateCards.Printing;

namespace iHateCards.Dialogs;

/// <summary>
/// Диалог печати: принтер, копии, размер, качество, PostScript/IP — и
/// редактор профиля двухсторонней печати (.hateprn): сторона переворота,
/// смещения оборота, авто-калибровка со сканов с возможностью правки.
/// Настройки хранятся по принтерам в профилях, а не в общем settings.json.
/// </summary>
public sealed class PrintDialog : Window
{
    private readonly ComboBox _printerCombo;
    private readonly NumericUpDown _copies;
    private readonly RadioButton _modeReal, _modeFit, _modePercent;
    private readonly NumericUpDown _pct;
    private readonly CheckBox _bw, _toner, _ps;
    private readonly ComboBox _dpi;
    private readonly TextBox _ip;
    private readonly StackPanel _ipRow;
    private readonly bool _duplex;

    // Профиль принтера
    private readonly ComboBox _flipEdge;
    private readonly NumericUpDown _offsetX, _offsetY;
    private readonly CheckBox _useAuto;
    private readonly NumericUpDown _fDx, _fDy, _fAng, _bDx, _bDy, _bAng;
    private readonly TextBlock _profilePath, _measuredAt;
    private PrinterProfile _profile;
    private bool _loadingProfile;

    public PrintOptions? Result { get; private set; }

    /// <summary>Профиль выбранного принтера — применяется к раскладке перед печатью.</summary>
    public PrinterProfile Profile => _profile;

    public PrintDialog(List<string> printers, string defaultPrinter, bool duplex)
    {
        _duplex = duplex;
        Title = "Печать";
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        Background = new SolidColorBrush(Color.Parse("#1d1d1d"));

        string startPrinter = printers.Contains(defaultPrinter) ? defaultPrinter
            : (printers.FirstOrDefault() ?? defaultPrinter);
        _profile = PrinterProfile.Load(startPrinter);

        _printerCombo = new ComboBox
        {
            ItemsSource = printers,
            SelectedItem = startPrinter,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var propsBtn = new Button { Content = "Параметры принтера…", IsVisible = OperatingSystem.IsWindows() };
        propsBtn.Classes.Add("secondary");
        propsBtn.Click += (_, _) =>
        {
            if (OperatingSystem.IsWindows() && CurrentPrinter() is { Length: > 0 } p)
            {
                try { WinSpool.OpenPrinterProperties(p); } catch { /* необязательно */ }
            }
        };

        _copies = Num(1, 999, 1, "0", 90);
        _modeReal = new RadioButton { Content = "Реальный размер", GroupName = "mode" };
        _modeFit = new RadioButton { Content = "Подогнать (под размер бумаги принтера)", GroupName = "mode" };
        _modePercent = new RadioButton { Content = "Свой размер (%)", GroupName = "mode" };
        _pct = Num(1, 400, 100, "0.##", 100);
        _pct.GotFocus += (_, _) => _modePercent.IsChecked = true;

        var b70 = Small("70%"); var b66 = Small("66%");
        b70.Click += (_, _) => { _modePercent.IsChecked = true; _pct.Value = 70; };
        b66.Click += (_, _) => { _modePercent.IsChecked = true; _pct.Value = 66; };

        _bw = new CheckBox { Content = "Чёрно-белая печать" };
        _toner = new CheckBox { Content = "Экономия тонера" };
        _dpi = new ComboBox
        {
            ItemsSource = new[] { "Высокое (360 dpi)", "Стандарт (300 dpi)", "Эконом (200 dpi)", "Черновик (150 dpi)" },
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _ps = new CheckBox { Content = "Принтер PostScript — печать напрямую в CMYK" };
        _ip = new TextBox { Watermark = "напр. 192.168.1.3", Width = 160 };

        var hosts = OperatingSystem.IsWindows() ? NetPrint.ListTcpHosts() : new List<string>();
        string hostHint = hosts.Count > 0 ? "найдены: " + string.Join(", ", hosts) : "";
        _ipRow = new StackPanel
        {
            Spacing = 4,
            Children = { Row("IP принтера (порт 9100):", _ip), Hint("Нужно, если PostScript-принтер на порту WSD. " + hostHint) }
        };

        // ---- профиль двухсторонней печати ----
        _flipEdge = new ComboBox
        {
            ItemsSource = new[] { "По длинной стороне (обычно)", "По короткой стороне (tumble)" },
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _offsetX = Num(-50, 50, 0, "0.##", 90);
        _offsetY = Num(-50, 50, 0, "0.##", 90);
        _useAuto = new CheckBox { Content = "Авто-настройка (значения со сканов мишени)" };
        _fDx = CalibNum(); _fDy = CalibNum(); _fAng = CalibNum();
        _bDx = CalibNum(); _bDy = CalibNum(); _bAng = CalibNum();
        _profilePath = Hint("");
        _measuredAt = Hint("");

        // Правка любого числа калибровки переводит профиль в ручной режим
        foreach (var n in new[] { _fDx, _fDy, _fAng, _bDx, _bDy, _bAng })
            n.ValueChanged += (_, _) => { if (!_loadingProfile) _useAuto.IsChecked = false; };
        _useAuto.IsCheckedChanged += (_, _) => { if (!_loadingProfile) SyncCalibFields(); };

        var exportBtn = Small("Экспорт…");
        var importBtn = Small("Импорт…");
        exportBtn.Click += async (_, _) => await ExportProfile();
        importBtn.Click += async (_, _) => await ImportProfile();

        var duplexPanel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Двухсторонняя печать (профиль принтера)", FontWeight = FontWeight.Bold, Foreground = Gray() },
                Row("Переворот листа:", null), _flipEdge,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children =
                    { new TextBlock { Text = "Смещение оборота, мм:", VerticalAlignment = VerticalAlignment.Center }, _offsetX, _offsetY } },
                _useAuto,
                Hint("Снимите галку, чтобы задать поправки вручную; правка любого поля тоже переключает в ручной режим."),
                CalibRow("Лицо:", _fDx, _fDy, _fAng),
                CalibRow("Оборот:", _bDx, _bDy, _bAng),
                _measuredAt,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { exportBtn, importBtn } },
                _profilePath
            }
        };

        _printerCombo.SelectionChanged += (_, _) =>
        {
            SaveProfileFromUi();
            _profile = PrinterProfile.Load(CurrentPrinter());
            LoadProfileToUi();
            AutofillIp();
        };
        _ps.IsCheckedChanged += (_, _) => { _ipRow.IsVisible = _ps.IsChecked == true; AutofillIp(); };

        LoadProfileToUi();
        AutofillIp();

        var okBtn = new Button { Content = "Печать", Padding = new Thickness(18, 8) };
        okBtn.Classes.Add("accent");
        var cancelBtn = new Button { Content = "Отмена", Padding = new Thickness(18, 8) };
        cancelBtn.Classes.Add("secondary");
        cancelBtn.Click += (_, _) => { Result = null; Close(); };
        okBtn.Click += (_, _) =>
        {
            Result = CollectOptions();
            SaveProfileFromUi();
            _profile.Save();
            RememberLastPrinter();
            Close();
        };

        Content = new ScrollViewer
        {
            MaxHeight = 820,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 12,
                Width = 440,
                Children =
                {
                    new TextBlock { Text = "Печать", FontWeight = FontWeight.Bold, FontSize = 15 },
                    Row("Принтер:", null), _printerCombo, propsBtn,
                    Row("Копии:", _copies),
                    new TextBlock { Text = "Размер", FontWeight = FontWeight.Bold, Foreground = Gray() },
                    _modeReal, _modeFit,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _modePercent, _pct } },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { b70, b66 } },
                    new TextBlock { Text = "Расширенные настройки", FontWeight = FontWeight.Bold, Foreground = Gray(), Margin = new Thickness(0, 8, 0, 0) },
                    _bw, _toner,
                    Row("Качество печати:", null), _dpi,
                    Hint("Ниже dpi — меньше размер задания (помогает при «ошибке порта» WSD)."),
                    _ps,
                    Hint("Выкл. — печать через драйвер (универсально: EPSON и любой принтер). Вкл. — только для PostScript-принтеров (Xerox); иначе иероглифы."),
                    _ipRow,
                    new Border { BorderBrush = new SolidColorBrush(Color.Parse("#363636")), BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 8, 0, 0) },
                    duplexPanel,
                    Hint("Стороны: " + (duplex ? "двухсторонняя" : "односторонняя") + " (по настройке приложения)"),
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Margin = new Thickness(0, 8, 0, 0),
                        Children = { cancelBtn, okBtn }
                    }
                }
            }
        };
    }

    // ---------------------------------------------------------------- профиль

    private void LoadProfileToUi()
    {
        _loadingProfile = true;
        var p = _profile;
        _copies.Value = p.Print.Copies;
        _pct.Value = (decimal)p.Print.ScalePct;
        _modeReal.IsChecked = p.Print.ScaleMode == "real";
        _modeFit.IsChecked = p.Print.ScaleMode == "fit";
        _modePercent.IsChecked = p.Print.ScaleMode == "percent";
        _bw.IsChecked = p.Print.Bw;
        _toner.IsChecked = p.Print.Toner;
        _dpi.SelectedIndex = p.Print.Dpi switch { 360 => 0, 300 => 1, 200 => 2, 150 => 3, _ => 1 };
        _ps.IsChecked = p.Print.PostScript || GuessPostScript(p.Printer);
        _ip.Text = p.Print.Ip;
        _ipRow.IsVisible = _ps.IsChecked == true;

        _flipEdge.SelectedIndex = p.Duplex.FlipEdge == "short" ? 1 : 0;
        _offsetX.Value = (decimal)p.Duplex.OffsetX;
        _offsetY.Value = (decimal)p.Duplex.OffsetY;
        _useAuto.IsChecked = p.Calibration.UseAuto;
        _measuredAt.Text = p.Calibration.MeasuredAt is { } t
            ? $"Авто-значения измерены: {t.ToLocalTime():dd.MM.yyyy HH:mm}"
            : "Авто-значения ещё не измерены (загрузите сканы мишени в разделе «Калибровка»).";
        _profilePath.Text = "Файл профиля: " + PrinterProfile.PathFor(p.Printer);
        _loadingProfile = false;
        SyncCalibFields();
    }

    /// <summary>Показывает в полях авто- или ручные значения (в зависимости от режима).</summary>
    private void SyncCalibFields()
    {
        _loadingProfile = true;
        bool auto = _useAuto.IsChecked == true;
        var f = auto ? _profile.Calibration.AutoFront : _profile.Calibration.ManualFront;
        var b = auto ? _profile.Calibration.AutoBack : _profile.Calibration.ManualBack;
        _fDx.Value = (decimal)f.Dx; _fDy.Value = (decimal)f.Dy; _fAng.Value = (decimal)f.Angle;
        _bDx.Value = (decimal)b.Dx; _bDy.Value = (decimal)b.Dy; _bAng.Value = (decimal)b.Angle;
        _loadingProfile = false;
    }

    private void SaveProfileFromUi()
    {
        var p = _profile;
        p.Printer = CurrentPrinter();
        p.Print.Copies = Math.Max(1, (int)(_copies.Value ?? 1));
        p.Print.ScalePct = (double)(_pct.Value ?? 100);
        p.Print.ScaleMode = _modeFit.IsChecked == true ? "fit" : _modePercent.IsChecked == true ? "percent" : "real";
        p.Print.Bw = _bw.IsChecked == true;
        p.Print.Toner = _toner.IsChecked == true;
        p.Print.Dpi = _dpi.SelectedIndex switch { 0 => 360, 2 => 200, 3 => 150, _ => 300 };
        p.Print.PostScript = _ps.IsChecked == true;
        p.Print.Ip = (_ip.Text ?? "").Trim();

        p.Duplex.FlipEdge = _flipEdge.SelectedIndex == 1 ? "short" : "long";
        p.Duplex.OffsetX = (double)(_offsetX.Value ?? 0);
        p.Duplex.OffsetY = (double)(_offsetY.Value ?? 0);

        bool auto = _useAuto.IsChecked == true;
        p.Calibration.UseAuto = auto;
        var target = auto
            ? (p.Calibration.AutoFront, p.Calibration.AutoBack)
            : (p.Calibration.ManualFront, p.Calibration.ManualBack);
        target.Item1.Dx = (double)(_fDx.Value ?? 0);
        target.Item1.Dy = (double)(_fDy.Value ?? 0);
        target.Item1.Angle = (double)(_fAng.Value ?? 0);
        target.Item2.Dx = (double)(_bDx.Value ?? 0);
        target.Item2.Dy = (double)(_bDy.Value ?? 0);
        target.Item2.Angle = (double)(_bAng.Value ?? 0);
    }

    private async Task ExportProfile()
    {
        SaveProfileFromUi();
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Экспорт профиля принтера",
            SuggestedFileName = _profile.Printer + PrinterProfile.Extension,
            FileTypeChoices = new[] { ProfileFileType }
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
            FileTypeFilter = new[] { ProfileFileType }
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path == null) return;
        try
        {
            var imported = PrinterProfile.Import(path);
            imported.Printer = CurrentPrinter();   // применяем к выбранному принтеру
            _profile = imported;
            LoadProfileToUi();
        }
        catch (Exception ex)
        {
            await Msg.Show(this, "Ошибка импорта", ex.Message);
        }
    }

    private static FilePickerFileType ProfileFileType =>
        new("Профиль принтера iHateCards") { Patterns = new[] { "*" + PrinterProfile.Extension } };

    // ---------------------------------------------------------------- прочее

    private string CurrentPrinter() => _printerCombo.SelectedItem as string ?? "";

    private static bool GuessPostScript(string name) =>
        Regex.IsMatch(name ?? "", @"xerox|altalink|postscript|adobe pdf|\bps\b", RegexOptions.IgnoreCase);

    private void AutofillIp()
    {
        if (_ps.IsChecked != true || !OperatingSystem.IsWindows()) return;
        if (!string.IsNullOrWhiteSpace(_ip.Text)) return;
        string name = CurrentPrinter();
        Task.Run(() =>
        {
            string ip = NetPrint.PrinterIpGuess(name);
            if (ip.Length > 0)
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (string.IsNullOrWhiteSpace(_ip.Text)) _ip.Text = ip;
                });
        });
    }

    private PrintOptions CollectOptions()
    {
        string mode = _modeFit.IsChecked == true ? "fit"
            : _modePercent.IsChecked == true ? "percent" : "real";
        double pct = (double)(_pct.Value ?? 100);
        return new PrintOptions
        {
            Printer = CurrentPrinter(),
            Copies = Math.Max(1, (int)(_copies.Value ?? 1)),
            ScaleMode = mode,
            Scale = mode == "percent" ? pct / 100 : 1.0,
            Fit = mode == "fit",
            Bw = _bw.IsChecked == true,
            Toner = _toner.IsChecked == true,
            PostScript = _ps.IsChecked == true,
            Dpi = _dpi.SelectedIndex switch { 0 => 360, 2 => 200, 3 => 150, _ => 300 },
            Ip = (_ip.Text ?? "").Trim(),
            Duplex = _duplex,
            Tumble = _flipEdge.SelectedIndex == 1
        };
    }

    /// <summary>Последний выбранный принтер — чтобы приложение знало, чей профиль
    /// обновлять результатами калибровки.</summary>
    private void RememberLastPrinter()
    {
        var settings = SettingsStore.Load();
        settings["lastPrinter"] = CurrentPrinter();
        SettingsStore.Save(settings);
    }

    private static NumericUpDown Num(double min, double max, double val, string fmt, double width) => new()
    {
        Minimum = (decimal)min,
        Maximum = (decimal)max,
        Value = (decimal)val,
        Increment = 1,
        FormatString = fmt,
        Width = width,
        ShowButtonSpinner = false
    };

    private static NumericUpDown CalibNum() => Num(-50, 50, 0, "0.##", 80);

    private static Control CalibRow(string label, NumericUpDown dx, NumericUpDown dy, NumericUpDown ang)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        sp.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Width = 60 });
        sp.Children.Add(new TextBlock { Text = "dx", VerticalAlignment = VerticalAlignment.Center, Foreground = Gray() });
        sp.Children.Add(dx);
        sp.Children.Add(new TextBlock { Text = "dy", VerticalAlignment = VerticalAlignment.Center, Foreground = Gray() });
        sp.Children.Add(dy);
        sp.Children.Add(new TextBlock { Text = "угол", VerticalAlignment = VerticalAlignment.Center, Foreground = Gray() });
        sp.Children.Add(ang);
        return sp;
    }

    private static Button Small(string text)
    {
        var b = new Button { Content = text, Padding = new Thickness(12, 4) };
        b.Classes.Add("secondary");
        return b;
    }

    private static IBrush Gray() => new SolidColorBrush(Color.Parse("#9a9ea8"));

    private static Control Row(string label, Control? right)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        sp.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        if (right != null) sp.Children.Add(right);
        return sp;
    }

    private static TextBlock Hint(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 11,
        Foreground = new SolidColorBrush(Color.Parse("#9a9ea8"))
    };
}

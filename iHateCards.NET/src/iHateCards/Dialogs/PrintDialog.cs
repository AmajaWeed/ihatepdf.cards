using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using iHateCards.Core;
using iHateCards.Printing;
using SkiaSharp;

namespace iHateCards.Dialogs;

/// <summary>
/// Окно печати: слева параметры, справа предпросмотр листа — как в привычных
/// диалогах печати. Настройки двухсторонней печати вынесены в отдельное окно
/// (кнопка «Двухсторонняя печать…»), системные параметры драйвера — в «Свойства».
/// </summary>
public sealed class PrintDialog : Window
{
    private readonly AppState _state;
    private readonly ComboBox _printerCombo;
    private readonly NumericUpDown _copies;
    private readonly RadioButton _modeFit, _modeReal, _modePercent;
    private readonly NumericUpDown _pct;
    private readonly CheckBox _bw, _toner, _ps;
    private readonly ComboBox _dpi;
    private readonly TextBox _ip;
    private readonly StackPanel _ipRow;
    private readonly ComboBox _mediaType, _tray;
    private readonly NumericUpDown _mediaWeight;
    private readonly TextBlock _mediaHint;
    private List<MediaOption> _mediaTypes = new();
    private List<MediaOption> _trays = new();
    private readonly bool _duplex;
    private PrinterProfile _profile;
    private bool _loading;

    public PrintOptions? Result { get; private set; }

    /// <summary>Профиль выбранного принтера — применяется к раскладке перед печатью.</summary>
    public PrinterProfile Profile => _profile;

    public PrintDialog(List<string> printers, string defaultPrinter, AppState state)
    {
        _state = state;
        _duplex = state.DuplexMode && state.HasAnyBack();

        Title = "Печать";
        SizeToContent = SizeToContent.Height;
        Width = 900;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        Background = new SolidColorBrush(Color.Parse("#1d1d1d"));

        string startPrinter = printers.Contains(defaultPrinter) ? defaultPrinter
            : (printers.FirstOrDefault() ?? defaultPrinter);
        _profile = PrinterProfile.Load(startPrinter);

        // --- принтер ---
        _printerCombo = new ComboBox
        {
            ItemsSource = printers,
            SelectedItem = startPrinter,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var propsBtn = Secondary("Свойства");
        ToolTip.SetTip(propsBtn, OperatingSystem.IsWindows()
            ? "Системный диалог драйвера принтера"
            : "Системные настройки «Принтеры и сканеры»");
        propsBtn.Click += async (_, _) =>
        {
            try { PrinterProperties.Open(CurrentPrinter()); }
            catch (Exception ex) { await Msg.Show(this, "Свойства принтера", ex.Message); }
        };

        var duplexBtn = Secondary("Двухсторонняя печать…");
        duplexBtn.IsEnabled = _duplex;
        ToolTip.SetTip(duplexBtn, _duplex
            ? "Сторона переворота, смещения оборота и калибровка"
            : "Включите двухстороннюю печать и добавьте рубашку");
        duplexBtn.Click += async (_, _) =>
        {
            SaveProfileFromUi();
            var dlg = new DuplexSettingsDialog(_profile);
            await dlg.ShowDialog(this);
            _profile = dlg.Profile;
        };

        // --- копии и цвет ---
        _copies = Num(1, 999, 1, "0", 80);
        _bw = new CheckBox { Content = "Печать в градациях серого (чёрно-белая)" };
        _toner = new CheckBox { Content = "Экономия чернил/тонера" };

        // --- размер ---
        _modeFit = new RadioButton { Content = "Подогнать", GroupName = "mode" };
        _modeReal = new RadioButton { Content = "Реальный размер", GroupName = "mode" };
        _modePercent = new RadioButton { Content = "Пользовательский масштаб:", GroupName = "mode" };
        _pct = Num(1, 400, 100, "0.##", 90);
        _pct.GotFocus += (_, _) => _modePercent.IsChecked = true;

        // --- качество и PostScript ---
        _dpi = new ComboBox
        {
            ItemsSource = new[] { "Высокое (360 dpi)", "Стандарт (300 dpi)", "Эконом (200 dpi)", "Черновик (150 dpi)" },
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _ps = new CheckBox { Content = "Принтер PostScript — печать напрямую в CMYK" };
        _ip = new TextBox { Watermark = "напр. 192.168.1.3", Width = 170 };
        var hosts = OperatingSystem.IsWindows() ? NetPrint.ListTcpHosts() : new List<string>();
        _ipRow = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                Row("IP принтера (порт 9100):", _ip),
                Hint("Нужно, если PostScript-принтер на порту WSD. " +
                     (hosts.Count > 0 ? "Найдены: " + string.Join(", ", hosts) : ""))
            }
        };
        _ps.IsCheckedChanged += (_, _) => { _ipRow.IsVisible = _ps.IsChecked == true; AutofillIp(); };

        // --- бумага ---
        _mediaType = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        _tray = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        _mediaWeight = Num(0, 600, 0, "0", 90);
        _mediaHint = Hint("");
        ReloadMediaLists();

        _printerCombo.SelectionChanged += (_, _) =>
        {
            SaveProfileFromUi();
            _profile = PrinterProfile.Load(CurrentPrinter());
            ReloadMediaLists();
            LoadProfileToUi();
            AutofillIp();
        };
        _ps.IsCheckedChanged += (_, _) => UpdateMediaHint();

        LoadProfileToUi();
        AutofillIp();

        // --- кнопки ---
        var okBtn = new Button { Content = "Печать", Padding = new Thickness(24, 8) };
        okBtn.Classes.Add("accent");
        okBtn.Click += (_, _) =>
        {
            Result = CollectOptions();
            SaveProfileFromUi();
            _profile.Save();
            RememberLastPrinter();
            Close();
        };
        var cancelBtn = new Button { Content = "Отмена", Padding = new Thickness(24, 8) };
        cancelBtn.Classes.Add("secondary");
        cancelBtn.Click += (_, _) => { Result = null; Close(); };

        // --- компоновка ---
        var left = new StackPanel
        {
            Spacing = 10,
            Width = 500,
            Children =
            {
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                    Children =
                    {
                        Cell(new TextBlock { Text = "Принтер:", VerticalAlignment = VerticalAlignment.Center }, 0),
                        Cell(_printerCombo, 1, new Thickness(8, 0)),
                        Cell(propsBtn, 2)
                    }
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 16,
                    Children = { Row("Копий:", _copies), _bw }
                },
                _toner,
                Section("Настройка размера и обработка страниц", new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        new Grid
                        {
                            ColumnDefinitions = new ColumnDefinitions("*,*"),
                            Children = { Cell(_modeFit, 0), Cell(_modeReal, 1) }
                        },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal, Spacing = 8,
                            Children = { _modePercent, _pct, new TextBlock { Text = "%", VerticalAlignment = VerticalAlignment.Center } }
                        }
                    }
                }),
                Section("Качество и цвет", new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        _dpi,
                        Hint("Ниже dpi — меньше размер задания (помогает при «ошибке порта» WSD)."),
                        _ps,
                        Hint("Выкл. — печать через драйвер (универсально: EPSON и любой принтер). Вкл. — только для PostScript-принтеров (Xerox); иначе иероглифы."),
                        _ipRow
                    }
                }),
                Section("Бумага", new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        Row("Тип носителя:", null), _mediaType,
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal, Spacing = 8,
                            Children = { Row("Плотность, г/м²:", _mediaWeight) }
                        },
                        Row("Лоток:", null), _tray,
                        _mediaHint
                    }
                }),
                Section("Стороны", new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = _duplex ? "Двухсторонняя (лицо + рубашка)" : "Односторонняя",
                            Foreground = new SolidColorBrush(Color.Parse("#9a9ea8"))
                        },
                        duplexBtn
                    }
                })
            }
        };

        var right = BuildPreview();

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = "Печать", FontWeight = FontWeight.Bold, FontSize = 15 },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20, Children = { left, right } },
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

    /// <summary>Предпросмотр листа — то же изображение, что уйдёт на принтер.</summary>
    private Control BuildPreview()
    {
        var paper = _state.Paper;
        Control preview;
        try
        {
            using var bmp = PageRenderer.Render(_state, _state.CurrentPage, "front", 96,
                new PageRenderer.Options(Preview: true, Softproof: false));
            preview = new Image
            {
                Source = new Bitmap(PixelFormat.Rgba8888, AlphaFormat.Opaque, bmp.GetPixels(),
                    new PixelSize(bmp.Width, bmp.Height), new Vector(96, 96), bmp.RowBytes),
                Stretch = Stretch.Uniform,
                Height = 380
            };
        }
        catch
        {
            preview = new TextBlock { Text = "Предпросмотр недоступен", Foreground = new SolidColorBrush(Color.Parse("#9a9ea8")) };
        }

        return new StackPanel
        {
            Width = 320,
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = $"Документ: {paper.Width:0.#} × {paper.Height:0.#} мм",
                    Foreground = new SolidColorBrush(Color.Parse("#9a9ea8"))
                },
                new Border
                {
                    Background = Brushes.White,
                    Padding = new Thickness(6),
                    CornerRadius = new CornerRadius(4),
                    Child = preview
                },
                new TextBlock
                {
                    Text = $"Лист {_state.CurrentPage + 1} из {(_duplex ? _state.TotalPages * 2 : _state.TotalPages)}"
                           + $" · карт на листе: {_state.CardsPerPage}",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = new SolidColorBrush(Color.Parse("#9a9ea8")),
                    FontSize = 12
                }
            }
        };
    }

    // ---------------------------------------------------------------- профиль

    private void LoadProfileToUi()
    {
        _loading = true;
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

        _mediaType.SelectedIndex = IndexOfMedia(_mediaTypes, p.Print.MediaType);
        _mediaWeight.Value = p.Print.MediaWeight;
        _tray.SelectedIndex = IndexOfTray(p.Print.MediaPosition);
        _loading = false;
        UpdateMediaHint();
    }

    private void SaveProfileFromUi()
    {
        if (_loading) return;
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

        // Первый пункт списков — «как решит принтер»
        p.Print.MediaType = _mediaType.SelectedIndex > 0
            ? _mediaTypes[_mediaType.SelectedIndex - 1].Name
            : "";
        p.Print.MediaWeight = (int)(_mediaWeight.Value ?? 0);
        p.Print.MediaPosition = _tray.SelectedIndex > 0
            ? _trays[_tray.SelectedIndex - 1].Id
            : -1;
    }

    /// <summary>Списки типов бумаги и лотков берём у драйвера выбранного принтера.</summary>
    private void ReloadMediaLists()
    {
        _mediaTypes = PrinterMedia.MediaTypes(CurrentPrinter());
        _trays = PrinterMedia.Trays(CurrentPrinter());
        _mediaType.ItemsSource = new[] { "По умолчанию принтера" }
            .Concat(_mediaTypes.Select(m => m.Name)).ToList();
        _tray.ItemsSource = new[] { "Выбирает принтер" }
            .Concat(_trays.Select(t => t.Name)).ToList();
        _mediaType.SelectedIndex = 0;
        _tray.SelectedIndex = 0;
    }

    private int IndexOfMedia(List<MediaOption> list, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return 0;
        int i = list.FindIndex(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 ? i + 1 : 0;
    }

    private int IndexOfTray(int id)
    {
        if (id < 0) return 0;
        int i = _trays.FindIndex(t => t.Id == id);
        return i >= 0 ? i + 1 : 0;
    }

    /// <summary>Объясняет, почему тип бумаги задаётся здесь, а не в драйвере.</summary>
    private void UpdateMediaHint()
    {
        _mediaHint.Text = _ps.IsChecked == true
            ? "Прямая печать CMYK идёт мимо драйвера принтера, поэтому выбранная в его "
              + "диалоге бумага не применяется — тип носителя нужно задать здесь. "
              + "В принтере лоток с такой бумагой должен быть настроен."
            : "Печать идёт через драйвер: можно оставить «по умолчанию принтера» и выбрать "
              + "бумагу в «Свойствах», либо задать тип здесь.";
    }

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
            Tumble = _profile.Duplex.FlipEdge == "short",
            MediaType = _mediaType.SelectedIndex > 0 ? _mediaTypes[_mediaType.SelectedIndex - 1].Name : "",
            MediaWeight = (int)(_mediaWeight.Value ?? 0),
            MediaPosition = _tray.SelectedIndex > 0 ? _trays[_tray.SelectedIndex - 1].Id : -1,
            ManualFeed = _profile.Print.ManualFeed
        };
    }

    private void RememberLastPrinter()
    {
        var settings = SettingsStore.Load();
        settings["lastPrinter"] = CurrentPrinter();
        SettingsStore.Save(settings);
    }

    // ---------------------------------------------------------------- мелочи

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

    private static Button Secondary(string text)
    {
        var b = new Button { Content = text, Padding = new Thickness(14, 6) };
        b.Classes.Add("secondary");
        return b;
    }

    private static Control Section(string title, Control body) => new Border
    {
        BorderBrush = new SolidColorBrush(Color.Parse("#363636")),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(12),
        Child = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = title,
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(Color.Parse("#9a9ea8"))
                },
                body
            }
        }
    };

    private static Control Cell(Control c, int column, Thickness? margin = null)
    {
        Grid.SetColumn(c, column);
        if (margin is { } m) c.Margin = m;
        return c;
    }

    private static Control Row(string label, Control right) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        Children = { new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }, right }
    };

    private static TextBlock Hint(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 11,
        Foreground = new SolidColorBrush(Color.Parse("#9a9ea8"))
    };
}

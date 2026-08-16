using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using iHateCards.Printing;

namespace iHateCards.Dialogs;

/// <summary>
/// Диалог печати — порт showPrintDialog из app_inject.js: принтер, свойства,
/// копии, размер (реальный/подогнать/%/пресеты 70·66), ч/б, экономия тонера,
/// качество (DPI), PostScript-режим с IP:9100. Настройки сохраняются в
/// settings.json (раздел "print") между запусками.
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
    private readonly JsonObject _psMap, _ipMap;
    private readonly bool _duplex;

    public PrintOptions? Result { get; private set; }

    public PrintDialog(List<string> printers, string defaultPrinter, bool duplex)
    {
        _duplex = duplex;
        Title = "Печать";
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        Background = new SolidColorBrush(Color.Parse("#1d1d1d"));

        var settings = SettingsStore.Load();
        var s = settings["print"] as JsonObject ?? new JsonObject();
        _psMap = s["printerPS"] as JsonObject ?? new JsonObject();
        _ipMap = s["printerIP"] as JsonObject ?? new JsonObject();

        string savedPrinter = s["printer"]?.GetValue<string>() ?? defaultPrinter;
        if (!printers.Contains(savedPrinter)) savedPrinter = defaultPrinter;
        string mode = s["scaleMode"]?.GetValue<string>() ?? "real";
        double pct = GetD(s["scalePct"], 100);
        int dpi = (int)GetD(s["dpi"], 300);

        _printerCombo = new ComboBox { ItemsSource = printers, SelectedItem = savedPrinter, HorizontalAlignment = HorizontalAlignment.Stretch };
        if (_printerCombo.SelectedItem == null && printers.Count > 0) _printerCombo.SelectedIndex = 0;

        var propsBtn = new Button { Content = "Параметры принтера…" };
        propsBtn.Classes.Add("secondary");
        propsBtn.Click += (_, _) =>
        {
            if (OperatingSystem.IsWindows() && CurrentPrinter() is { Length: > 0 } p)
            {
                try { WinSpool.OpenPrinterProperties(p); } catch { /* необязательно */ }
            }
        };

        _copies = new NumericUpDown { Minimum = 1, Maximum = 999, Increment = 1, Value = (decimal)GetD(s["copies"], 1), FormatString = "0", Width = 90 };

        _modeReal = new RadioButton { Content = "Реальный размер", GroupName = "mode", IsChecked = mode == "real" };
        _modeFit = new RadioButton { Content = "Подогнать (под размер бумаги принтера)", GroupName = "mode", IsChecked = mode == "fit" };
        _modePercent = new RadioButton { Content = "Свой размер (%)", GroupName = "mode", IsChecked = mode == "percent" };
        _pct = new NumericUpDown { Minimum = 1, Maximum = 400, Increment = 1, Value = (decimal)pct, FormatString = "0.##", Width = 100 };
        _pct.GotFocus += (_, _) => _modePercent.IsChecked = true;

        var b70 = new Button { Content = "70%" }; b70.Classes.Add("secondary");
        var b66 = new Button { Content = "66%" }; b66.Classes.Add("secondary");
        b70.Click += (_, _) => { _modePercent.IsChecked = true; _pct.Value = 70; };
        b66.Click += (_, _) => { _modePercent.IsChecked = true; _pct.Value = 66; };

        _bw = new CheckBox { Content = "Чёрно-белая печать", IsChecked = GetB(s["bw"]) };
        _toner = new CheckBox { Content = "Экономия тонера", IsChecked = GetB(s["toner"]) };

        _dpi = new ComboBox
        {
            ItemsSource = new[] { "Высокое (360 dpi)", "Стандарт (300 dpi)", "Эконом (200 dpi)", "Черновик (150 dpi)" },
            SelectedIndex = dpi switch { 360 => 0, 300 => 1, 200 => 2, 150 => 3, _ => 1 },
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        _ps = new CheckBox { Content = "Принтер PostScript — печать напрямую в CMYK", IsChecked = PsFor(savedPrinter) };
        _ip = new TextBox { Text = IpFor(savedPrinter), Watermark = "напр. 192.168.1.3", Width = 160 };

        var hosts = OperatingSystem.IsWindows() ? NetPrint.ListTcpHosts() : new List<string>();
        string hostHint = hosts.Count > 0 ? "найдены: " + string.Join(", ", hosts) : "";
        _ipRow = new StackPanel
        {
            Spacing = 4,
            IsVisible = _ps.IsChecked == true,
            Children =
            {
                Row("IP принтера (порт 9100):", _ip),
                Hint("Нужно, если PostScript-принтер на порту WSD. " + hostHint)
            }
        };

        _printerCombo.SelectionChanged += (_, _) =>
        {
            string p = CurrentPrinter();
            _ps.IsChecked = PsFor(p);
            _ip.Text = IpFor(p);
            _ipRow.IsVisible = _ps.IsChecked == true;
            AutofillIp();
        };
        _ps.IsCheckedChanged += (_, _) => { _ipRow.IsVisible = _ps.IsChecked == true; AutofillIp(); };
        AutofillIp();

        var okBtn = new Button { Content = "Печать", Padding = new Thickness(18, 8) };
        okBtn.Classes.Add("accent");
        var cancelBtn = new Button { Content = "Отмена", Padding = new Thickness(18, 8) };
        cancelBtn.Classes.Add("secondary");
        cancelBtn.Click += (_, _) => { Result = null; Close(); };
        okBtn.Click += (_, _) => { Result = CollectOptions(); SavePrefs(); Close(); };

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Width = 420,
            Children =
            {
                new TextBlock { Text = "Печать", FontWeight = FontWeight.Bold, FontSize = 15 },
                Row("Принтер:", null),
                _printerCombo,
                propsBtn,
                Row("Копии:", _copies),
                new TextBlock { Text = "Размер", FontWeight = FontWeight.Bold, Foreground = Gray() },
                _modeReal, _modeFit,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _modePercent, _pct } },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { b70, b66 } },
                new TextBlock { Text = "Расширенные настройки", FontWeight = FontWeight.Bold, Foreground = Gray(), Margin = new Thickness(0, 8, 0, 0) },
                _bw, _toner,
                Row("Качество печати:", null),
                _dpi,
                Hint("Ниже dpi — меньше размер задания (помогает при «ошибке порта» WSD)."),
                _ps,
                Hint("Выкл. — печать через драйвер (универсально: EPSON и любой принтер). Вкл. — только для PostScript-принтеров (Xerox); иначе иероглифы."),
                _ipRow,
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
        };
    }

    private string CurrentPrinter() => _printerCombo.SelectedItem as string ?? "";

    private bool PsFor(string name)
    {
        if (_psMap[name] != null) return GetB(_psMap[name]);
        return Regex.IsMatch(name ?? "", @"xerox|altalink|postscript|adobe pdf|\bps\b", RegexOptions.IgnoreCase);
    }

    private string IpFor(string name) => _ipMap[name]?.GetValue<string>() ?? "";

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
        int dpi = _dpi.SelectedIndex switch { 0 => 360, 2 => 200, 3 => 150, _ => 300 };
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
            Dpi = dpi,
            Ip = (_ip.Text ?? "").Trim(),
            Duplex = _duplex,
            Tumble = false
        };
    }

    private void SavePrefs()
    {
        var o = CollectOptions();
        _psMap[o.Printer] = o.PostScript;
        _ipMap[o.Printer] = o.Ip;
        var settings = SettingsStore.Load();
        settings["print"] = new JsonObject
        {
            ["printer"] = o.Printer,
            ["copies"] = o.Copies,
            ["scaleMode"] = o.ScaleMode,
            ["scalePct"] = (double)(_pct.Value ?? 100),
            ["bw"] = o.Bw,
            ["toner"] = o.Toner,
            ["dpi"] = o.Dpi,
            ["printerPS"] = _psMap.DeepClone(),
            ["printerIP"] = _ipMap.DeepClone()
        };
        SettingsStore.Save(settings);
    }

    private static double GetD(JsonNode? n, double def)
    {
        try { return n?.GetValue<double>() ?? def; } catch { }
        try { return n?.GetValue<int>() ?? def; } catch { }
        return def;
    }

    private static bool GetB(JsonNode? n)
    {
        try { return n?.GetValue<bool>() ?? false; } catch { return false; }
    }

    private static IBrush Gray() => new SolidColorBrush(Color.Parse("#9a9ea8"));

    private static Control Row(string label, Control? right)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        sp.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        if (right != null) sp.Children.Add(right);
        return sp;
    }

    private static Control Hint(string text) => new TextBlock
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 11,
        Foreground = new SolidColorBrush(Color.Parse("#9a9ea8"))
    };
}

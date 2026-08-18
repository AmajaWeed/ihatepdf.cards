using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using iHateCards.Core;
using iHateCards.Printing;

namespace iHateCards.Dialogs;

/// <summary>
/// Настройки бумаги выбранного принтера (профиль .hateprn): тип носителя,
/// плотность и лоток. Вынесено из окна печати в отдельное окно — по тому же
/// принципу, что и «Двухсторонняя печать…».
/// </summary>
public sealed class PaperSettingsDialog : Window
{
    private readonly ComboBox _mediaType, _tray;
    private readonly NumericUpDown _mediaWeight;
    private readonly TextBlock _hint;
    private readonly List<MediaOption> _mediaTypes;
    private readonly List<MediaOption> _trays;
    private readonly PrinterProfile _profile;

    public PaperSettingsDialog(PrinterProfile profile, List<MediaOption> mediaTypes, List<MediaOption> trays)
    {
        _profile = profile;
        _mediaTypes = mediaTypes;
        _trays = trays;

        Title = "Бумага";
        SizeToContent = SizeToContent.Height;
        Width = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        Background = new SolidColorBrush(Color.Parse("#1d1d1d"));

        _mediaType = new ComboBox
        {
            ItemsSource = new[] { "По умолчанию принтера" }.Concat(_mediaTypes.Select(m => m.Name)).ToList(),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _tray = new ComboBox
        {
            ItemsSource = new[] { "Выбирает принтер" }.Concat(_trays.Select(t => t.Name)).ToList(),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _mediaWeight = new NumericUpDown
        {
            Minimum = 0, Maximum = 600, Increment = 1, FormatString = "0", Width = 90, ShowButtonSpinner = false
        };
        _hint = Hint(_profile.Print.PostScript
            ? "Прямая печать CMYK идёт мимо драйвера принтера, поэтому выбранная в его "
              + "диалоге бумага не применяется — тип носителя нужно задать здесь. "
              + "В принтере лоток с такой бумагой должен быть настроен."
            : "Печать идёт через драйвер: можно оставить «по умолчанию принтера» и выбрать "
              + "бумагу в «Свойствах», либо задать тип здесь.");

        LoadToUi();

        var okBtn = new Button { Content = "Готово", Padding = new Thickness(18, 8) };
        okBtn.Classes.Add("accent");
        okBtn.Click += (_, _) => { SaveFromUi(); Close(); };
        var cancelBtn = new Button { Content = "Отмена", Padding = new Thickness(18, 8) };
        cancelBtn.Classes.Add("secondary");
        cancelBtn.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10,
            Children =
            {
                Label("Тип носителя:"),
                _mediaType,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8,
                    Children = { Label("Плотность, г/м²:"), _mediaWeight }
                },
                Label("Лоток:"),
                _tray,
                _hint,
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
        var p = _profile.Print;
        _mediaType.SelectedIndex = IndexOfMedia(p.MediaType);
        _mediaWeight.Value = p.MediaWeight;
        _tray.SelectedIndex = IndexOfTray(p.MediaPosition);
    }

    private void SaveFromUi()
    {
        var p = _profile.Print;
        p.MediaType = _mediaType.SelectedIndex > 0 ? _mediaTypes[_mediaType.SelectedIndex - 1].Name : "";
        p.MediaWeight = (int)(_mediaWeight.Value ?? 0);
        if (_tray.SelectedIndex > 0)
        {
            var t = _trays[_tray.SelectedIndex - 1];
            p.MediaPosition = t.Id;
            p.MediaTrayName = t.Name;
        }
        else
        {
            p.MediaPosition = -1;
            p.MediaTrayName = "";
        }
    }

    private int IndexOfMedia(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return 0;
        int i = _mediaTypes.FindIndex(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 ? i + 1 : 0;
    }

    private int IndexOfTray(int id)
    {
        if (id < 0) return 0;
        int i = _trays.FindIndex(t => t.Id == id);
        return i >= 0 ? i + 1 : 0;
    }

    private static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center };

    private static TextBlock Hint(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 11,
        Foreground = new SolidColorBrush(Color.Parse("#9a9ea8"))
    };
}

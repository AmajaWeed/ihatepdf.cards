using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using iHateCards.Update;

namespace iHateCards.Dialogs;

/// <summary>О программе: версия и дата сборки — видно, обновилась ли программа
/// на самом деле после автообновления. На Windows то же самое показывает
/// подпись в углу главного окна; здесь — отдельное окно для macOS, где команды
/// живут в системной строке меню.</summary>
public sealed class AboutDialog : Window
{
    public AboutDialog()
    {
        Title = "О программе";
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        Background = new SolidColorBrush(Color.Parse("#1d1d1d"));

        var closeBtn = new Button { Content = "Готово", Padding = new Thickness(18, 8) };
        closeBtn.Classes.Add("accent");
        closeBtn.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 10,
            Width = 320,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = "iHateCards",
                    FontSize = 20,
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(Color.Parse("#52a8e0")),
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                new TextBlock
                {
                    Text = "Раскладка карт для печати · CMYK US Web Coated (SWOP)",
                    FontSize = 12,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Color.Parse("#9a9ea8"))
                },
                new Border
                {
                    BorderBrush = new SolidColorBrush(Color.Parse("#363636")),
                    BorderThickness = new Thickness(0, 1, 0, 0),
                    Margin = new Thickness(0, 6)
                },
                Row("Версия:", AppVersion.Current),
                Row("Сборка от:", AppVersion.BuildDate),
                Row("Платформа:", AppVersion.Rid),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 10, 0, 0),
                    Children = { closeBtn }
                }
            }
        };
    }

    private static Control Row(string label, string value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var l = new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.Parse("#9a9ea8")),
            Margin = new Thickness(0, 0, 8, 0)
        };
        var v = new TextBlock { Text = value, FontWeight = FontWeight.Medium };
        Grid.SetColumn(v, 1);
        grid.Children.Add(l);
        grid.Children.Add(v);
        return grid;
    }
}

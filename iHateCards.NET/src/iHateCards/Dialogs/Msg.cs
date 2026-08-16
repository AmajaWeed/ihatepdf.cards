using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace iHateCards.Dialogs;

/// <summary>Простые модальные окна сообщений (замена alert/confirm).</summary>
public static class Msg
{
    public static Task Show(Window owner, string title, string text) =>
        ShowButtons(owner, title, text, new[] { ("OK", "ok") });

    public static async Task<bool> Confirm(Window owner, string title, string text)
    {
        var r = await ShowButtons(owner, title, text, new[] { ("Да", "yes"), ("Отмена", "cancel") });
        return r == "yes";
    }

    /// <summary>Да / Нет / Отмена (для «сохранить перед закрытием?»).</summary>
    public static Task<string?> YesNoCancel(Window owner, string title, string text) =>
        ShowButtons(owner, title, text, new[] { ("Сохранить", "yes"), ("Не сохранять", "no"), ("Отмена", "cancel") });

    private static async Task<string?> ShowButtons(Window owner, string title, string text,
        (string Label, string Result)[] buttons)
    {
        var dlg = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = new SolidColorBrush(Color.Parse("#1d1d1d")),
            MaxWidth = 560
        };
        string? result = null;

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        foreach (var (label, res) in buttons)
        {
            var b = new Button { Content = label, Padding = new Thickness(16, 8) };
            if (res == buttons[0].Result) b.Classes.Add("accent"); else b.Classes.Add("secondary");
            b.Click += (_, _) => { result = res; dlg.Close(); };
            btnRow.Children.Add(b);
        }

        dlg.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = text,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Color.Parse("#eef0f6"))
                },
                btnRow
            }
        };
        await dlg.ShowDialog(owner);
        return result;
    }
}

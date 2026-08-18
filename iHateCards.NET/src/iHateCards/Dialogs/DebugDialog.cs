using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using iHateCards.Diagnostics;

namespace iHateCards.Dialogs;

/// <summary>
/// Режим разработчика: включает подробный журнал (печать, обновления,
/// сохранение проектов) и даёт быстро открыть папку с логами — чтобы
/// приложить их к описанию бага.
/// </summary>
public sealed class DebugDialog : Window
{
    public DebugDialog()
    {
        Title = "Диагностика";
        SizeToContent = SizeToContent.WidthAndHeight;
        Width = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        Background = new SolidColorBrush(Color.Parse("#1d1d1d"));

        var toggle = new CheckBox { Content = "Включить журнал отладки", IsChecked = DevLog.Enabled };
        toggle.IsCheckedChanged += (_, _) => DevLog.Enabled = toggle.IsChecked == true;

        var pathText = new TextBlock
        {
            Text = DevLog.LogDir,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.Parse("#9a9ea8"))
        };

        var openBtn = new Button { Content = "Открыть папку с логами" };
        openBtn.Classes.Add("secondary");
        openBtn.Click += (_, _) => OpenFolder(DevLog.LogDir);

        var closeBtn = new Button { Content = "Готово", Padding = new Thickness(18, 8) };
        closeBtn.Classes.Add("accent");
        closeBtn.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            // Ширина задана и здесь, не только на Window: при SizeToContent
            // текст с переносом меряется с бесконечной шириной, если её
            // ограничивает только окно, а не сам контейнер контента.
            Width = 380,
            Children =
            {
                new TextBlock { Text = "Режим разработчика", FontWeight = FontWeight.Bold, FontSize = 15 },
                new TextBlock
                {
                    Text = "Пишет подробный журнал того, что делает программа: печать (какие "
                        + "опции выбраны, какой путь печати сработал), проверка обновлений, "
                        + "сохранение и открытие проектов. Полезно, если что-то работает не так, "
                        + "как ожидалось, — приложите файл лога к описанию проблемы.",
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.Parse("#c9ccd4"))
                },
                toggle,
                new TextBlock { Text = "Папка с логами:", FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#9a9ea8")) },
                pathText,
                openBtn,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { closeBtn }
                }
            }
        };
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = false });
            else if (OperatingSystem.IsMacOS())
                Process.Start(new ProcessStartInfo("open", $"\"{path}\"") { UseShellExecute = false });
            else
                Process.Start(new ProcessStartInfo("xdg-open", $"\"{path}\"") { UseShellExecute = false });
        }
        catch
        {
            // необязательное действие — путь и так показан в диалоге
        }
    }
}

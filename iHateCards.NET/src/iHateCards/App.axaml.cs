using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace iHateCards;

public partial class App : Application
{
    /// <summary>Путь .hate из аргументов запуска (двойной клик по файлу).</summary>
    public static string? StartupProject;

    /// <summary>Служебный режим: снять скриншот окна в PNG и выйти (--uishot).</summary>
    public static string? UiShotPath;

    /// <summary>Изображения для автоимпорта в служебном режиме скриншота.</summary>
    public static List<string> UiShotImports = new();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var win = new MainWindow();
            desktop.MainWindow = win;
            if (StartupProject != null && File.Exists(StartupProject))
            {
                string path = StartupProject;
                win.Opened += async (_, _) => await win.OpenProject(path);
            }
            if (UiShotPath != null)
            {
                string shot = UiShotPath;
                win.Opened += async (_, _) =>
                {
                    if (UiShotImports.Count > 0)
                        win.ImportFiles(UiShotImports);
                    await Task.Delay(2500); // дождаться импорта и первого рендера
                    var size = new Avalonia.PixelSize((int)win.Bounds.Width, (int)win.Bounds.Height);
                    using var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Avalonia.Vector(96, 96));
                    rtb.Render(win);
                    rtb.Save(shot);
                    Environment.Exit(0);
                };
            }
            SingleInstance.StartServer(path =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                {
                    win.Activate();
                    if (File.Exists(path)) await win.OpenProject(path);
                });
            });
        }
        base.OnFrameworkInitializationCompleted();
    }
}

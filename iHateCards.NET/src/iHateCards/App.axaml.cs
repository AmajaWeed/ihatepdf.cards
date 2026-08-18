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

    /// <summary>Служебный режим: снять скриншот уведомления об обновлении (--toastshot).</summary>
    public static string? ToastShotPath;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Шрифт интерфейса: Century Gothic, если он есть в системе, иначе вшитый Jost
        Resources["AppFontFamily"] = Avalonia.Media.FontFamily.Parse(AppFonts.UiFontFamily);

        if (ToastShotPath is { } toastShot && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime toastLifetime)
        {
            // Демонстрационное уведомление для скриншота (без обращения к сети)
            var demo = new Update.UpdateInfo("2.2.0",
                new[]
                {
                    "Обновление по сети без прав администратора",
                    "Авто-разворот кадра — больше карт на лист",
                    "Профиль двухсторонней печати принтера (.hateprn)",
                    "Шрифт Century Gothic",
                    "Поддержка macOS"
                },
                "18.08.2026",
                new Update.UpdatePackage("https://example/pkg.zip", "", 0));
            var toast = new Update.UpdateToast(demo);
            toastLifetime.MainWindow = toast;
            toast.Opened += async (_, _) =>
            {
                await Task.Delay(1200);
                var size = new Avalonia.PixelSize((int)toast.Bounds.Width, (int)toast.Bounds.Height);
                using var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Avalonia.Vector(96, 96));
                rtb.Render(toast);
                rtb.Save(toastShot);
                Environment.Exit(0);
            };
            toast.Show();
            base.OnFrameworkInitializationCompleted();
            return;
        }

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

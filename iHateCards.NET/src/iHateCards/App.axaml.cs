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

    /// <summary>Служебный режим: показать на скриншоте свой размер бумаги.</summary>
    public static bool UiShotCustomPaper;

    /// <summary>Служебный режим: снять скриншот уведомления об обновлении (--toastshot).</summary>
    public static string? ToastShotPath;

    /// <summary>Служебный режим: снять скриншот диалога печати (--dialogshot).</summary>
    public static string? DialogShotPath;

    /// <summary>Какой именно диалог снимать: print (по умолчанию) или duplex.</summary>
    public static string DialogShotKind = "print";

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Шрифт интерфейса: Century Gothic, если он есть в системе, иначе вшитый Jost
        Resources["AppFontFamily"] = Avalonia.Media.FontFamily.Parse(AppFonts.UiFontFamily);

        if (DialogShotPath is { } dialogShot && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime dialogLifetime)
        {
            var (printers, def) = Printing.PrintService.ListPrinters();
            if (printers.Count == 0) { printers = new List<string> { "Xerox AltaLink C8155" }; def = printers[0]; }
            // Демонстрационная раскладка для скриншота диалога
            var demoState = new Core.AppState { DuplexMode = true };
            Core.LayoutEngine.CalculateLayout(demoState);
            Avalonia.Controls.Window dlg = DialogShotKind == "duplex"
                ? new Dialogs.DuplexSettingsDialog(PrinterProfile.Load(def))
                : new Dialogs.PrintDialog(printers, def, demoState);
            dialogLifetime.MainWindow = dlg;
            dlg.Opened += async (_, _) =>
            {
                await Task.Delay(1500);
                var size = new Avalonia.PixelSize((int)dlg.Bounds.Width, (int)dlg.Bounds.Height);
                using var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Avalonia.Vector(96, 96));
                rtb.Render(dlg);
                rtb.Save(dialogShot);

                // Вторым кадром — низ диалога (редактор профиля дуплекса)
                if (dlg.Content is Avalonia.Controls.ScrollViewer sv)
                {
                    sv.ScrollToEnd();
                    await Task.Delay(600);
                    using var rtb2 = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Avalonia.Vector(96, 96));
                    rtb2.Render(dlg);
                    rtb2.Save(System.IO.Path.ChangeExtension(dialogShot, null) + "-duplex.png");
                }
                Environment.Exit(0);
            };
            dlg.Show();
            base.OnFrameworkInitializationCompleted();
            return;
        }

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
                    if (UiShotCustomPaper)
                        win.SelectCustomPaper(700, 1000);
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

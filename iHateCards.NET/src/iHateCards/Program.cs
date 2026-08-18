using Avalonia;

namespace iHateCards;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--update-now")
        {
            Environment.Exit(RunUpdateFromCommandLine());
            return;
        }
        if (args.Length > 0 && args[0] == "--selftest")
        {
            Environment.Exit(SelfTest.Run(args));
            return;
        }
        if (args.Length > 1 && args[0] == "--render")
        {
            Environment.Exit(SelfTest.RenderDemo(args[1]));
            return;
        }
        if (args.Length > 1 && args[0] == "--dialogshot")
            App.DialogShotPath = args[1];
        if (args.Length > 1 && args[0] == "--toastshot")
            App.ToastShotPath = args[1];
        if (args.Length > 1 && args[0] == "--uishot")
        {
            App.UiShotPath = args[1];
            App.UiShotImports = args.Skip(2).Where(File.Exists).ToList();
        }

        string? project = args.FirstOrDefault(a =>
            a.EndsWith(".hate", StringComparison.OrdinalIgnoreCase) && File.Exists(a));

        if (!SingleInstance.TryClaim(project))
            return; // путь передан уже запущенному экземпляру

        App.StartupProject = project;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Тихое обновление из командной строки (без интерфейса):
    /// проверяет манифест, скачивает пакет, сверяет SHA-256 и запускает подмену
    /// файлов. Прав администратора не требует — программа стоит в папке
    /// пользователя.</summary>
    private static int RunUpdateFromCommandLine()
    {
        try
        {
            var info = Update.UpdateChecker.CheckAsync(ignoreSkipped: true).GetAwaiter().GetResult();
            if (info == null)
            {
                Console.WriteLine($"Обновлений нет (установлена {Update.AppVersion.Current}).");
                return 0;
            }
            Console.WriteLine($"Доступна версия {info.Version}; текущая {Update.AppVersion.Current}.");
            foreach (var note in info.Notes) Console.WriteLine("  • " + note);

            var progress = new Progress<double>(v => Console.Write($"\rЗагрузка… {v * 100:0}%   "));
            Update.UpdateInstaller.PrepareAndApplyAsync(info, progress).GetAwaiter().GetResult();
            Console.WriteLine("\nОбновление подготовлено, применяется после выхода из программы.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Ошибка обновления: " + ex.Message);
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}

using Avalonia;
using iHateCards.Diagnostics;

namespace iHateCards;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Падения приложения — самое ценное для отладки, поэтому пишутся в
        // журнал, если режим разработчика включён (DevLog сам это проверяет).
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            DevLog.LogException("Crash", "необработанное исключение", e.ExceptionObject as Exception
                ?? new Exception(e.ExceptionObject?.ToString() ?? "неизвестная ошибка"));
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            DevLog.LogException("Crash", "необработанное исключение в фоновой задаче", e.Exception);
            e.SetObserved();
        };

        if (args.Length > 1 && args[0] == "--test-print")
        {
            Environment.Exit(RunTestPrint(args));
            return;
        }
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
        {
            App.DialogShotPath = args[1];
            if (args.Length > 2) App.DialogShotKind = args[2];
        }
        if (args.Length > 1 && args[0] == "--toastshot")
            App.ToastShotPath = args[1];
        if (args.Length > 1 && args[0] == "--uishot")
        {
            App.UiShotPath = args[1];
            App.UiShotImports = args.Skip(2).Where(File.Exists).ToList();
            App.UiShotCustomPaper = args.Contains("custom");
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

    /// <summary>
    /// Служебный режим: печатает тестовую страницу через настоящий пайплайн
    /// приложения (тот же PostScriptWriter/PrintService), запрашивая
    /// конкретный лоток — для проверки на реальном принтере, что новый
    /// /InputAttributes (взамен несуществующего /MediaPosition) действительно
    /// приводит к печати из нужного лотка.
    ///
    /// Использование: --test-print &lt;принтер&gt; &lt;номер_лотка&gt;
    /// </summary>
    private static int RunTestPrint(string[] args)
    {
        string printer = args[1];
        int tray = args.Length > 2 && int.TryParse(args[2], out int t) ? t : 5;

        var bmp = new SkiaSharp.SKBitmap(600, 850, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Opaque);
        using (var c = new SkiaSharp.SKCanvas(bmp))
        {
            c.Clear(SkiaSharp.SKColors.White);
            using var border = new SkiaSharp.SKPaint
                { Color = SkiaSharp.SKColors.Black, Style = SkiaSharp.SKPaintStyle.Stroke, StrokeWidth = 8 };
            c.DrawRect(new SkiaSharp.SKRect(20, 20, 580, 830), border);
            using var font = new SkiaSharp.SKFont(SkiaSharp.SKTypeface.Default, 220);
            using var text = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.Black, IsAntialias = true };
            c.DrawText(tray.ToString(), 300, 480, SkiaSharp.SKTextAlign.Center, font, text);
            using var small = new SkiaSharp.SKFont(SkiaSharp.SKTypeface.Default, 24);
            c.DrawText("iHateCards --test-print", 300, 600, SkiaSharp.SKTextAlign.Center, small, text);
            c.DrawText($"запрошен лоток {tray}", 300, 640, SkiaSharp.SKTextAlign.Center, small, text);
        }

        var page = new Pdf.PsPage(Imaging.CmykPipeline.ToCmyk(bmp), false, bmp.Width, bmp.Height, 210, 297);
        byte[] ps = Pdf.PostScriptWriter.Build(new[] { page },
            new Pdf.PsOptions(1, false, false, 1.0, false, MediaPosition: tray));

        Console.WriteLine($"Принтер: {printer}");
        Console.WriteLine($"Запрошен лоток: {tray}");
        Console.WriteLine($"Размер задания: {ps.Length} байт");
        try
        {
            if (OperatingSystem.IsMacOS()) Printing.MacPrint.RawPrint(printer, ps, "iHateCards test-print");
            else if (OperatingSystem.IsWindows()) Printing.WinSpool.RawPrint(printer, ps, "iHateCards test-print");
            else throw new PlatformNotSupportedException();
            Console.WriteLine("Отправлено на печать.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Ошибка печати: " + ex.Message);
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}

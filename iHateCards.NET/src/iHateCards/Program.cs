using Avalonia;

namespace iHateCards;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
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

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}

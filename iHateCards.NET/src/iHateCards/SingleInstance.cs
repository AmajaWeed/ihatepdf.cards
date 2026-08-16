using System.IO.Pipes;
using System.Text;

namespace iHateCards;

/// <summary>
/// Один экземпляр приложения: второй запуск передаёт путь .hate первому через
/// именованный канал и завершается (двойной клик по файлу открывает проект в
/// уже запущенном окне).
/// </summary>
public static class SingleInstance
{
    private const string PipeName = "iHateCards-project-pipe";
    private static Mutex? _mutex;

    /// <summary>True — мы первый экземпляр; false — путь передан существующему.</summary>
    public static bool TryClaim(string? projectPath)
    {
        _mutex = new Mutex(true, "iHateCards-single-instance", out bool createdNew);
        if (createdNew) return true;

        // Уже запущено — передаём путь (или просто активируем) и выходим
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000);
            var data = Encoding.UTF8.GetBytes(projectPath ?? "");
            client.Write(data, 0, data.Length);
        }
        catch
        {
            // первый экземпляр не отвечает — запускаемся сами
            return true;
        }
        return false;
    }

    public static void StartServer(Action<string> onProject)
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In);
                    server.WaitForConnection();
                    using var ms = new MemoryStream();
                    server.CopyTo(ms);
                    string path = Encoding.UTF8.GetString(ms.ToArray()).Trim();
                    onProject(path);
                }
                catch
                {
                    Thread.Sleep(500);
                }
            }
        })
        { IsBackground = true, Name = "hate-pipe-server" };
        thread.Start();
    }
}

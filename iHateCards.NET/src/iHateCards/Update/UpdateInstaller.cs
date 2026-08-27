using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace iHateCards.Update;

/// <summary>
/// Установка обновления без прав администратора: программа живёт в папке
/// пользователя, поэтому может заменить свои файлы сама.
///
/// Порядок: скачать ZIP → сверить SHA-256 → распаковать во временную папку →
/// запустить скрипт подмены и выйти. Скрипт ждёт завершения процесса,
/// переименовывает старую папку в резервную, ставит новую на её место и
/// запускает программу снова; при сбое возвращает старую версию обратно.
/// </summary>
public static class UpdateInstaller
{
    public static string UpdatesDir
    {
        get
        {
            string d = Path.Combine(SettingsPaths.DataDir, "updates");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    /// <summary>Скачивает пакет и проверяет контрольную сумму.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo info, IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        string file = Path.Combine(UpdatesDir, $"iHateCards-{info.Version}-{AppVersion.Rid}.zip");
        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) })
        using (var resp = await http.GetAsync(info.Package.Url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? info.Package.Size;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(file);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total > 0) progress?.Report(done / (double)total);
            }
        }

        if (!string.IsNullOrWhiteSpace(info.Package.Sha256))
        {
            string actual = Sha256(file);
            if (!string.Equals(actual, info.Package.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(file);
                throw new InvalidDataException(
                    "Контрольная сумма файла обновления не совпала — загрузка отклонена.");
            }
        }
        return file;
    }

    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>Каталог, который нужно заменить: на Windows — папка программы,
    /// на macOS — весь бандл .app.</summary>
    public static string TargetDirectory()
    {
        string baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        if (OperatingSystem.IsMacOS())
        {
            var dir = new DirectoryInfo(baseDir);
            while (dir != null)
            {
                if (dir.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) return dir.FullName;
                dir = dir.Parent;
            }
        }
        return baseDir;
    }

    /// <summary>Распаковывает пакет и определяет корень новой версии.</summary>
    public static string ExtractStaging(string zipPath)
    {
        string staging = Path.Combine(UpdatesDir, "staging");
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);
        ZipFile.ExtractToDirectory(zipPath, staging);

        if (OperatingSystem.IsMacOS())
        {
            var app = Directory.GetDirectories(staging, "*.app", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (app != null) return app;
        }
        // ZIP может содержать один каталог верхнего уровня — берём его
        var entries = Directory.GetFileSystemEntries(staging);
        if (entries.Length == 1 && Directory.Exists(entries[0])) return entries[0];
        return staging;
    }

    /// <summary>Готовит и запускает скрипт подмены; после вызова приложение
    /// должно немедленно завершиться.</summary>
    public static void LaunchApplier(string stagingRoot, string? targetDir = null, int? waitPid = null)
    {
        string target = targetDir ?? TargetDirectory();
        string backup = target + ".old";
        int pid = waitPid ?? Environment.ProcessId;
        string exe = OperatingSystem.IsWindows()
            ? Path.Combine(target, "iHateCards.exe")
            : Path.Combine(target, "Contents", "MacOS", "iHateCards");

        string scriptPath = Path.Combine(UpdatesDir,
            OperatingSystem.IsWindows() ? "apply-update.ps1" : "apply-update.sh");

        if (OperatingSystem.IsWindows())
        {
            string logPath = Path.Combine(UpdatesDir, "apply-update.log");
            // Move-Item сразу после закрытия программы иногда натыкается на файл,
            // ещё на секунду занятый антивирусом/индексатором (сама программа к
            // этому моменту уже закрыта — Wait-Process это гарантирует) — раньше
            // единственная попытка в этом случае молча проваливалась, откатывала
            // папку и подмена так и не происходила. Несколько попыток с паузой —
            // стандартное лекарство от такой гонки при самообновлении на Windows.
            File.WriteAllText(scriptPath, $@"
$ErrorActionPreference = 'Stop'
$target  = '{target}'
$staging = '{stagingRoot}'
$backup  = '{backup}'
$log     = '{logPath}'

function Log($msg) {{
    ""$(Get-Date -Format o)  $msg"" | Out-File -FilePath $log -Append -Encoding UTF8
}}

function Move-WithRetry($from, $to) {{
    $attempt = 0
    while ($true) {{
        $attempt++
        try {{
            Move-Item -LiteralPath $from -Destination $to -Force
            return
        }} catch {{
            if ($attempt -ge 6) {{ throw }}
            Log ""попытка $attempt переместить '$from' -> '$to' не удалась: $($_.Exception.Message) — повтор""
            Start-Sleep -Milliseconds (500 * $attempt)
        }}
    }}
}}

Log 'запуск apply-update, ожидание завершения процесса {pid}'
try {{ Wait-Process -Id {pid} -Timeout 120 -ErrorAction SilentlyContinue }} catch {{}}
Start-Sleep -Milliseconds 1000

if (Test-Path $backup) {{ Remove-Item $backup -Recurse -Force -ErrorAction SilentlyContinue }}
try {{
    Move-WithRetry $target $backup
    Move-WithRetry $staging $target
    Log 'подмена выполнена успешно'
}} catch {{
    Log ""подмена не удалась: $($_.Exception.Message)""
    # Откат: возвращаем прежнюю версию
    if ((Test-Path $backup) -and -not (Test-Path $target)) {{
        Move-Item -LiteralPath $backup -Destination $target -Force
        Log 'откат к прежней версии выполнен'
    }}
    Start-Process -FilePath '{exe}'
    throw
}}
Remove-Item $backup -Recurse -Force -ErrorAction SilentlyContinue
Start-Process -FilePath '{exe}'
", System.Text.Encoding.UTF8);

            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell",
                ArgumentList = { "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", scriptPath },
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        else
        {
            // Без set -e: обе mv-команды разбираются вручную, чтобы при сбое
            // всё равно перезапустить программу (пусть и старой версией) —
            // раньше при сбое скрипт просто останавливался, и пользователь
            // оставался без запущенной программы вовсе, что на баг-репорте
            // выглядит так же, как «обновление не сработало».
            File.WriteAllText(scriptPath, $@"#!/bin/sh
target='{target}'
staging='{stagingRoot}'
backup='{backup}'

# Дождаться закрытия программы
i=0
while kill -0 {pid} 2>/dev/null && [ $i -lt 120 ]; do sleep 1; i=$((i+1)); done
sleep 1

relaunch() {{
    if [ -d ""$target/Contents"" ]; then
        # Обновлённый бандл переподписываем (ad-hoc), иначе macOS откажется запускать
        codesign --force --deep --sign - ""$target"" 2>/dev/null || true
        open ""$target""
    else
        chmod +x '{exe}' 2>/dev/null || true
        '{exe}' &
    fi
}}

rm -rf ""$backup""
if ! mv ""$target"" ""$backup""; then
    relaunch   # первый mv не удался — старая версия так и осталась на месте
    exit 1
fi
if ! mv ""$staging"" ""$target""; then
    mv ""$backup"" ""$target""   # откат
    relaunch                    # старая версия должна остаться рабочей
    exit 1
fi
rm -rf ""$backup""
relaunch
");
            Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/sh",
                ArgumentList = { scriptPath },
                UseShellExecute = false
            });
        }
    }

    /// <summary>Полный цикл: скачать, проверить, распаковать, запустить подмену.</summary>
    public static async Task PrepareAndApplyAsync(UpdateInfo info, IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        string zip = await DownloadAsync(info, progress, ct);
        string staging = ExtractStaging(zip);
        LaunchApplier(staging);
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace iHateCards.Update;

/// <summary>Что выбрал пользователь в уведомлении об обновлении.</summary>
public enum UpdateChoice { None, Update, Later, Skip }

/// <summary>
/// Уведомление об обновлении в правом нижнем углу экрана: версия, краткий
/// патчноут и три кнопки — «Обновить», «Не сейчас», «Пропустить версию».
/// Окно без рамки, поверх остальных, работе не мешает.
/// </summary>
public sealed class UpdateToast : Window
{
    private const int MaxNotes = 5;

    private readonly ProgressBar _progress;
    private readonly TextBlock _status;
    private readonly StackPanel _buttons;
    private readonly UpdateInfo _info;

    public UpdateChoice Choice { get; private set; } = UpdateChoice.None;

    public UpdateToast(UpdateInfo info)
    {
        _info = info;
        SystemDecorations = WindowDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        SizeToContent = SizeToContent.Height;
        Width = 380;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };

        var notes = new StackPanel { Spacing = 2 };
        foreach (var line in info.Notes.Take(MaxNotes))
            notes.Children.Add(new TextBlock
            {
                Text = "• " + line,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse("#c9ccd4"))
            });
        if (info.Notes.Length > MaxNotes)
            notes.Children.Add(new TextBlock
            {
                Text = $"…и ещё {info.Notes.Length - MaxNotes}",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.Parse("#9a9ea8"))
            });

        var updateBtn = new Button { Content = "Обновить" };
        updateBtn.Classes.Add("accent");
        var laterBtn = new Button { Content = "Не сейчас" };
        laterBtn.Classes.Add("secondary");
        var skipBtn = new Button { Content = "Пропустить версию" };
        skipBtn.Classes.Add("secondary");

        updateBtn.Click += (_, _) => { Choice = UpdateChoice.Update; BeginUpdate(); };
        laterBtn.Click += (_, _) => { Choice = UpdateChoice.Later; Close(); };
        skipBtn.Click += (_, _) =>
        {
            Choice = UpdateChoice.Skip;
            UpdateChecker.Skip(_info.Version);
            Close();
        };

        _buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { updateBtn, laterBtn, skipBtn }
        };

        _progress = new ProgressBar { Minimum = 0, Maximum = 1, Value = 0, IsVisible = false, Height = 6 };
        _status = new TextBlock
        {
            IsVisible = false,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.Parse("#9a9ea8"))
        };

        Content = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#1d1d1d")),
            BorderBrush = new SolidColorBrush(Color.Parse("#363636")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"Доступна версия {info.Version}",
                        FontWeight = FontWeight.Bold,
                        FontSize = 14,
                        Foreground = new SolidColorBrush(Color.Parse("#eef0f6"))
                    },
                    new TextBlock
                    {
                        Text = $"Установлена {AppVersion.Current}" +
                               (info.Published.Length > 0 ? $" · обновление от {info.Published}" : ""),
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.Parse("#9a9ea8"))
                    },
                    notes,
                    _progress,
                    _status,
                    _buttons
                }
            }
        };

        Opened += (_, _) => PlaceBottomRight();
    }

    /// <summary>Ставит окно в правый нижний угол рабочей области экрана.</summary>
    private void PlaceBottomRight()
    {
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen == null) return;
        var area = screen.WorkingArea;
        double scaling = screen.Scaling;
        int w = (int)(Bounds.Width * scaling);
        int h = (int)(Bounds.Height * scaling);
        const int margin = 24;
        Position = new PixelPoint(
            area.X + area.Width - w - (int)(margin * scaling),
            area.Y + area.Height - h - (int)(margin * scaling));
    }

    private async void BeginUpdate()
    {
        _buttons.IsVisible = false;
        _progress.IsVisible = true;
        _status.IsVisible = true;
        _status.Text = "Загрузка обновления…";

        var progress = new Progress<double>(v => Dispatcher.UIThread.Post(() =>
        {
            _progress.Value = v;
            _status.Text = $"Загрузка обновления… {v * 100:0}%";
        }));

        try
        {
            await Task.Run(() => UpdateInstaller.PrepareAndApplyAsync(_info, progress));
            _status.Text = "Перезапуск…";
            // Скрипт подмены ждёт завершения процесса — выходим
            if (Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
            else
                Environment.Exit(0);
        }
        catch (Exception ex)
        {
            _progress.IsVisible = false;
            _status.Text = "Не удалось обновить: " + ex.Message;
            _buttons.IsVisible = true;
        }
    }
}

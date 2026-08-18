using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using iHateCards.Core;
using iHateCards.Dialogs;
using iHateCards.Diagnostics;
using iHateCards.Imaging;
using iHateCards.Pdf;
using iHateCards.Printing;
using iHateCards.Update;
using SkiaSharp;

namespace iHateCards;

public partial class MainWindow : Window
{
    private AppState _s = new();
    private bool _softproof;
    private bool _updating;          // защита от рекурсии при синхронизации инпутов
    private string? _projectPath;
    private bool _dirty;
    private ImageEntry? _pendingBackFor;
    private Bitmap? _previewBitmap;

    public MainWindow()
    {
        InitializeComponent();
        InitControls();
        Wire();
        Recalc();
        UpdateTitle();
        BuildMacMenu();
        if (!OperatingSystem.IsMacOS())
        {
            VersionLabel.Text = $"iHateCards {AppVersion.Current} · сборка {AppVersion.BuildDate}";
            ToolTip.SetTip(VersionLabel, "О программе: подтверждение версии после обновления");
        }
        Opened += (_, _) => _ = CheckForUpdatesAsync(silent: true);
        AddHandler(DragDrop.DropEvent, OnWindowDrop);
        AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = DragDropEffects.Copy);
    }

    // ---------------------------------------------------------------- init

    private void InitControls()
    {
        PaperSizeCombo.ItemsSource = PaperSizes.All.Select(p => p.Label)
            .Append("Свой размер (широкий формат)…").ToList();
        PaperSizeCombo.SelectedIndex = 2; // A4
        PhotoLayoutCombo.ItemsSource = new[] { "Ручная (карты)", "2 фото на листе", "4 фото на листе" };
        PhotoLayoutCombo.SelectedIndex = 0;
        DragDrop.SetAllowDrop(this, true);
    }

    private void Wire()
    {
        PaperSizeCombo.SelectionChanged += (_, _) =>
        {
            if (_updating) return;
            int idx = Math.Max(0, PaperSizeCombo.SelectedIndex);
            _s.PaperSizeKey = idx >= PaperSizes.All.Length
                ? PaperSizes.CustomKey
                : PaperSizes.All[idx].Key;
            CustomPaperSection.IsVisible = _s.PaperSizeKey == PaperSizes.CustomKey;
            bool isSmall = _s.PaperSizeKey is "a5" or "a6";
            PhotoLayoutSection.IsVisible = isSmall;
            if (!isSmall && _s.PhotoLayout != 0)
            {
                _s.PhotoLayout = 0;
                _updating = true;
                PhotoLayoutCombo.SelectedIndex = 0;
                CardSizeSection.IsVisible = true;
                _updating = false;
            }
            MarkDirty();
            Recalc();
        };

        void CustomPaperChanged()
        {
            if (_updating) return;
            _s.CustomPaperWidth = (double)(PaperWidthInput.Value ?? 320);
            _s.CustomPaperHeight = (double)(PaperHeightInput.Value ?? 450);
            MarkDirty();
            Recalc();
        }
        PaperWidthInput.ValueChanged += (_, _) => CustomPaperChanged();
        PaperHeightInput.ValueChanged += (_, _) => CustomPaperChanged();

        PhotoLayoutCombo.SelectionChanged += (_, _) =>
        {
            if (_updating) return;
            _s.PhotoLayout = PhotoLayoutCombo.SelectedIndex switch { 1 => 2, 2 => 4, _ => 0 };
            bool photoMode = _s.PhotoLayout > 0;
            CardSizeSection.IsVisible = !photoMode;
            if (photoMode)
            {
                _s.Bleed = 0;
                _s.ShowCropMarks = false;
                _updating = true;
                CropMarksToggle.IsChecked = false;
                CropMarksSubOptions.IsVisible = false;
                _updating = false;
            }
            MarkDirty();
            Recalc();
        };

        CardWidthInput.ValueChanged += (_, _) =>
        {
            if (_updating) return;
            _s.CardWidth = Math.Clamp((double)(CardWidthInput.Value ?? 65), 10, 420);
            if (_s.PolaroidMode)
            {
                var dims = LayoutEngine.PolaroidCardFromWidth(_s, _s.CardWidth);
                _s.CardHeight = Math.Round(dims.H * 2) / 2;
                SyncCardInputs();
                UpdatePolaroidInfo();
            }
            UpdateCutSize();
            MarkDirty();
            Recalc();
        };

        CardHeightInput.ValueChanged += (_, _) =>
        {
            if (_updating) return;
            _s.CardHeight = Math.Clamp((double)(CardHeightInput.Value ?? 90), 10, 420);
            if (_s.PolaroidMode)
            {
                var dims = LayoutEngine.PolaroidCardFromHeight(_s, _s.CardHeight);
                _s.CardWidth = Math.Round(dims.W * 2) / 2;
                SyncCardInputs();
                UpdatePolaroidInfo();
            }
            UpdateCutSize();
            MarkDirty();
            Recalc();
        };

        BleedInput.ValueChanged += (_, _) =>
        {
            if (_updating) return;
            _s.Bleed = Math.Clamp((double)(BleedInput.Value ?? 0), 0, 10);
            UpdateCutSize();
            MarkDirty();
            RenderPreview();
        };

        DuplexToggle.IsCheckedChanged += (_, _) =>
        {
            if (_updating) return;
            _s.DuplexMode = DuplexToggle.IsChecked == true;
            DuplexOptions.IsVisible = _s.DuplexMode;
            if (!_s.DuplexMode) _s.CurrentSide = "front";
            SyncCalibVisibility();
            MarkDirty();
            Recalc();
        };

        CropMarksToggle.IsCheckedChanged += (_, _) =>
        {
            if (_updating) return;
            _s.ShowCropMarks = CropMarksToggle.IsChecked == true;
            CropMarksSubOptions.IsVisible = _s.ShowCropMarks;
            MarkDirty();
            RenderPreview();
        };

        NoHaloToggle.IsCheckedChanged += (_, _) =>
        {
            if (_updating) return;
            _s.NoHaloMarks = NoHaloToggle.IsChecked == true;
            MarkDirty();
            RenderPreview();
        };

        FitImageToggle.IsCheckedChanged += (_, _) =>
        {
            if (_updating) return;
            _s.FitImage = FitImageToggle.IsChecked == true;
            MarkDirty();
            RenderPreview();
        };

        AutoRotateToggle.IsCheckedChanged += (_, _) =>
        {
            if (_updating) return;
            _s.AutoRotate = AutoRotateToggle.IsChecked == true;
            // общий переключатель задаёт значение всем картам и новым по умолчанию
            foreach (var e in AllEntries()) e.AutoRotateImage = _s.AutoRotate;
            MarkDirty();
            RenderCardList();
            RenderPreview();
        };

        AutoRotateFrameToggle.IsCheckedChanged += (_, _) =>
        {
            if (_updating) return;
            _s.AutoRotateFrame = AutoRotateFrameToggle.IsChecked == true;
            MarkDirty();
            Recalc();
        };

        BorderlessToggle.IsCheckedChanged += (_, _) =>
        {
            if (_updating) return;
            _s.Borderless = BorderlessToggle.IsChecked == true;
            MarkDirty();
            Recalc();
        };

        PolaroidToggle.IsCheckedChanged += (_, _) =>
        {
            if (_updating) return;
            _s.PolaroidMode = PolaroidToggle.IsChecked == true;
            PolaroidOptions.IsVisible = _s.PolaroidMode;
            if (_s.PolaroidMode) LayoutEngine.ApplyPolaroidDefaults(_s);
            else LayoutEngine.RestorePrePolaroidDimensions(_s);
            SyncCardInputs();
            UpdateCutSize();
            UpdatePolaroidInfo();
            MarkDirty();
            Recalc();
        };

        void PolaroidParamChanged()
        {
            if (_updating || !_s.PolaroidMode) return;
            var dims = LayoutEngine.PolaroidCardFromWidth(_s, _s.CardWidth);
            _s.CardHeight = Math.Round(dims.H * 2) / 2;
            SyncCardInputs();
            UpdateCutSize();
            UpdatePolaroidInfo();
            MarkDirty();
            Recalc();
        }

        PolaroidSideInput.ValueChanged += (_, _) =>
        {
            if (_updating) return;
            _s.PolaroidSide = Math.Clamp((double)(PolaroidSideInput.Value ?? 3), 1, 20);
            PolaroidParamChanged();
        };
        PolaroidTopInput.ValueChanged += (_, _) =>
        {
            if (_updating) return;
            _s.PolaroidTop = Math.Clamp((double)(PolaroidTopInput.Value ?? 3), 1, 20);
            PolaroidParamChanged();
        };
        PolaroidBottomInput.ValueChanged += (_, _) =>
        {
            if (_updating) return;
            _s.PolaroidBottom = Math.Clamp((double)(PolaroidBottomInput.Value ?? 15), 2, 40);
            PolaroidParamChanged();
        };

        OffsetXInput.ValueChanged += (_, _) =>
        {
            if (_updating) return;
            _s.OffsetX = (double)(OffsetXInput.Value ?? 0);
            MarkDirty();
            if (_s.CurrentSide == "back") RenderPreview();
        };
        OffsetYInput.ValueChanged += (_, _) =>
        {
            if (_updating) return;
            _s.OffsetY = (double)(OffsetYInput.Value ?? 0);
            MarkDirty();
            if (_s.CurrentSide == "back") RenderPreview();
        };

        CalibModeToggle.IsCheckedChanged += (_, _) =>
        {
            if (_updating) return;
            _s.CalibMode = CalibModeToggle.IsChecked == true;
            CalibOptions.IsVisible = _s.CalibMode;
            MarkDirty();
            RenderPreview();
        };

        IndividualBacksToggle.IsCheckedChanged += (_, _) =>
        {
            if (_updating) return;
            _s.IndividualBacks = IndividualBacksToggle.IsChecked == true;
            SingleBackArea.IsVisible = !_s.IndividualBacks;
            IndividualBackHint.IsVisible = _s.IndividualBacks;
            if (_s.IndividualBacks && !_s.DuplexMode)
            {
                _s.DuplexMode = true;
                _updating = true;
                DuplexToggle.IsChecked = true;
                _updating = false;
                DuplexOptions.IsVisible = true;
                SyncCalibVisibility();
            }
            MarkDirty();
            RenderCardList();
            Recalc();
        };

        // Дропзоны и клики
        DropFront.PointerPressed += async (_, _) => await PickAndAddImages();
        DropBack.PointerPressed += async (_, _) => await PickBackImage();
        WireZoneDrop(DropFront, files => AddFiles(files));
        WireZoneDrop(DropBack, async files =>
        {
            if (files.Count > 0) await SetBackImageFromFile(files[0]);
        });
        BackRemoveBtn.Click += (_, _) =>
        {
            _s.BackImage = null;
            MarkDirty();
            RenderBackPreview();
            RenderPreview();
        };

        // Калибровка
        CalibTargetBtn.Click += async (_, _) => await SaveCalibTarget();
        CalibDropFront.PointerPressed += async (_, _) => await PickCalibScan("front");
        CalibDropBack.PointerPressed += async (_, _) => await PickCalibScan("back");
        WireZoneDrop(CalibDropFront, async files => { if (files.Count > 0) await DetectCalibration(files[0], "front"); });
        WireZoneDrop(CalibDropBack, async files => { if (files.Count > 0) await DetectCalibration(files[0], "back"); });
        CalibResetBtn.Click += (_, _) =>
        {
            _s.CalibFront = new CalibSide();
            _s.CalibBack = new CalibSide();
            CalibResultFront.IsVisible = false;
            CalibResultBack.IsVisible = false;
            MarkDirty();
            RenderPreview();
        };

        // Навигация
        PrevPageBtn.Click += (_, _) =>
        {
            if (_s.CurrentPage > 0) { _s.CurrentPage--; UpdateStats(); RenderPreview(); }
        };
        NextPageBtn.Click += (_, _) =>
        {
            if (_s.CurrentPage < _s.TotalPages - 1) { _s.CurrentPage++; UpdateStats(); RenderPreview(); }
        };
        PageTypeBtn.Click += (_, _) =>
        {
            if (!_s.DuplexMode) return;
            _s.CurrentSide = _s.CurrentSide == "back" ? "front" : "back";
            RenderPreview();
        };

        SoftproofBtn.Click += async (_, _) => await ToggleSoftproof();
        ExportBtn.Click += async (_, _) => await ExportCmyk();
        PrintBtn.Click += async (_, _) => await PrintFlow();
        ClearBtn.Click += (_, _) =>
        {
            foreach (var im in _s.Images) im.Dispose();
            _s.Images.Clear();
            _s.BackImage?.Dispose();
            _s.BackImage = null;
            _s.CurrentPage = 0;
            MarkDirty();
            RenderCardList();
            RenderBackPreview();
            Recalc();
        };

        UpdateBtn.Click += async (_, _) => await CheckForUpdatesAsync(silent: false);
        DebugBtn.Click += async (_, _) => await new DebugDialog().ShowDialog(this);
        SaveProjectBtn.Click += async (_, _) => await SaveProject(saveAs: false);
        OpenProjectBtn.Click += async (_, _) => await OpenProjectViaDialog();

        // Горячие клавиши
        KeyDown += async (_, e) =>
        {
            bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
            if (!ctrl) return;
            switch (e.Key)
            {
                case Key.S:
                    e.Handled = true;
                    await SaveProject(saveAs: e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                    break;
                case Key.O:
                    e.Handled = true;
                    await OpenProjectViaDialog();
                    break;
                case Key.V:
                    e.Handled = true;
                    await PasteFromClipboard();
                    break;
            }
        };

        Closing += async (_, e) =>
        {
            if (!_dirty) return;
            e.Cancel = true;
            var r = await Msg.YesNoCancel(this, "Несохранённые изменения",
                "Сохранить проект перед закрытием?");
            if (r == "cancel" || r == null) return;
            if (r == "yes")
            {
                bool saved = await SaveProject(saveAs: false);
                if (!saved) return;
            }
            _dirty = false;
            Close();
        };
    }

    private void WireZoneDrop(Control zone, Action<List<string>> handler) =>
        WireZoneDrop(zone, files => { handler(files); return Task.CompletedTask; });

    private void WireZoneDrop(Control zone, Func<List<string>, Task> handler)
    {
        DragDrop.SetAllowDrop(zone, true);
        zone.AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            var files = ExtractFiles(e);
            if (files.Count > 0)
            {
                e.Handled = true;
                await handler(files);
            }
        });
    }

    private static List<string> ExtractFiles(DragEventArgs e)
    {
        var list = new List<string>();
        var items = e.DataTransfer.TryGetFiles();
        if (items != null)
            foreach (var it in items)
            {
                var p = it.TryGetLocalPath();
                if (p != null) list.Add(p);
            }
        return list;
    }

    private async void OnWindowDrop(object? sender, DragEventArgs e)
    {
        if (e.Handled) return;
        var files = ExtractFiles(e);
        if (files.Count == 0) return;
        if (files.Count == 1 && files[0].EndsWith(".hate", StringComparison.OrdinalIgnoreCase))
        {
            await OpenProject(files[0]);
            return;
        }
        AddFiles(files);
    }

    // ---------------------------------------------------------------- импорт

    private static FilePickerFileType ImageTypes => new("Изображения и PDF")
    {
        Patterns = RasterDecoder.AllExtensions.Select(e => "*" + e).ToList()
    };

    private async Task PickAndAddImages()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Добавить лицевые стороны",
            AllowMultiple = true,
            FileTypeFilter = new[] { ImageTypes }
        });
        AddFiles(files.Select(f => f.TryGetLocalPath()).Where(p => p != null).Cast<string>().ToList());
    }

    /// <summary>Выбирает свой размер листа (широкий формат) — используется
    /// служебным режимом скриншота и как программная точка входа.</summary>
    internal void SelectCustomPaper(double widthMm, double heightMm)
    {
        PaperWidthInput.Value = (decimal)widthMm;
        PaperHeightInput.Value = (decimal)heightMm;
        PaperSizeCombo.SelectedIndex = PaperSizes.All.Length;
    }

    /// <summary>Импорт списка файлов (используется также служебным режимом --uishot).</summary>
    internal void ImportFiles(List<string> paths) => AddFiles(paths);

    private async void AddFiles(List<string> paths)
    {
        if (paths.Count == 0) return;
        await WithOverlay("Импорт...", () => Task.Run(() =>
        {
            var newEntries = new List<ImageEntry>();
            foreach (var path in paths)
            {
                try
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    foreach (var frame in RasterDecoder.Decode(bytes, Path.GetFileName(path)))
                    {
                        var e = RasterDecoder.ToEntry(frame);
                        e.AutoRotateImage = _s.AutoRotate;
                        newEntries.Add(e);
                    }
                }
                catch (Exception ex)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        _ = Msg.Show(this, "Ошибка импорта",
                            $"Не удалось декодировать {Path.GetFileName(path)}: {ex.Message}"));
                }
            }
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _s.Images.AddRange(newEntries);
                if (_softproof) _ = EnsureProofsAndRefresh();
                MarkDirty();
                RenderCardList();
                Recalc();
            });
        }));
    }

    private async Task PickBackImage()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Выбрать рубашку",
            AllowMultiple = false,
            FileTypeFilter = new[] { ImageTypes }
        });
        var p = files.FirstOrDefault()?.TryGetLocalPath();
        if (p != null) await SetBackImageFromFile(p);
    }

    private async Task SetBackImageFromFile(string path)
    {
        await WithOverlay("Импорт...", () => Task.Run(() =>
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                var frames = RasterDecoder.Decode(bytes, Path.GetFileName(path));
                if (frames.Count == 0) return;
                var entry = RasterDecoder.ToEntry(frames[0]);
                entry.AutoRotateImage = _s.AutoRotate;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _s.BackImage?.Dispose();
                    _s.BackImage = entry;
                    if (_softproof) _ = EnsureProofsAndRefresh();
                    MarkDirty();
                    RenderBackPreview();
                    RenderPreview();
                });
            }
            catch (Exception ex)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    _ = Msg.Show(this, "Ошибка", "Не удалось декодировать рубашку: " + ex.Message));
            }
        }));
    }

    private async Task PickCardBack(ImageEntry entry)
    {
        _pendingBackFor = entry;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Рубашка для карты",
            AllowMultiple = false,
            FileTypeFilter = new[] { ImageTypes }
        });
        var p = files.FirstOrDefault()?.TryGetLocalPath();
        if (p == null || _pendingBackFor == null) return;
        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(p);
            var frames = RasterDecoder.Decode(bytes, Path.GetFileName(p));
            if (frames.Count > 0)
            {
                _pendingBackFor.BackImage?.Dispose();
                _pendingBackFor.BackImage = RasterDecoder.ToEntry(frames[0]);
                _pendingBackFor.BackImage.AutoRotateImage = _s.AutoRotate;
                if (_softproof) _ = EnsureProofsAndRefresh();
                MarkDirty();
                RenderCardList();
                RenderPreview();
            }
        }
        catch (Exception ex)
        {
            await Msg.Show(this, "Ошибка", ex.Message);
        }
        _pendingBackFor = null;
    }

    private async Task PasteFromClipboard()
    {
        var cb = Clipboard;
        if (cb == null) return;
        try
        {
            using var dt = await cb.TryGetDataAsync();
            if (dt == null) return;

            var items = await dt.TryGetFilesAsync();
            if (items != null)
            {
                var paths = items.Select(i => i.TryGetLocalPath()).Where(p => p != null).Cast<string>().ToList();
                if (paths.Count > 0) { AddFiles(paths); return; }
            }

            var pastedBitmap = await dt.TryGetBitmapAsync();
            if (pastedBitmap != null)
            {
                using var ms = new MemoryStream();
                pastedBitmap.Save(ms);
                var frames = RasterDecoder.Decode(ms.ToArray(), "clipboard.png");
                if (frames.Count > 0)
                {
                    _s.Images.Add(RasterDecoder.ToEntry(frames[0]));
                    MarkDirty();
                    RenderCardList();
                    Recalc();
                }
            }
        }
        catch
        {
            // буфер обмена недоступен/пуст — молча
        }
    }

    // ---------------------------------------------------------------- рендер

    private void Recalc()
    {
        LayoutEngine.CalculateLayout(_s);
        UpdateStats();
        RenderPreview();
    }

    private void RenderPreview()
    {
        var bmp = PageRenderer.Render(_s, _s.CurrentPage, EffectiveSide(), LayoutConfig.PreviewDpiFor(_s.Paper),
            new PageRenderer.Options(Preview: true, Softproof: _softproof));
        var old = _previewBitmap;
        _previewBitmap = ToAvaloniaBitmap(bmp);
        bmp.Dispose();
        PreviewImage.Source = _previewBitmap;
        old?.Dispose();
        UpdatePageTypeIndicator();
        UpdateNavigation();
    }

    private string EffectiveSide() => _s.DuplexMode && _s.CurrentSide == "back" ? "back" : "front";

    private static Bitmap ToAvaloniaBitmap(SKBitmap bmp) =>
        new(PixelFormat.Rgba8888, AlphaFormat.Opaque, bmp.GetPixels(),
            new PixelSize(bmp.Width, bmp.Height), new Vector(96, 96), bmp.RowBytes);

    private void UpdateStats()
    {
        int total = _s.TotalCardCount();
        StatCardsPerPage.Text = _s.CardsPerPage.ToString();
        StatTotalPages.Text = (_s.DuplexMode ? _s.TotalPages * 2 : _s.TotalPages).ToString();
        StatTotalCards.Text = total.ToString();
        StatFormat.Text = _s.PaperSizeKey == PaperSizes.CustomKey
            ? $"{_s.Paper.Width:0.#}×{_s.Paper.Height:0.#}"
            : _s.PaperSizeKey.ToUpperInvariant();
        if (CustomPaperSection.IsVisible)
        {
            int dpi = LayoutConfig.ExportDpiFor(_s.Paper);
            CustomPaperInfo.Text = dpi >= LayoutConfig.ExportDpi
                ? $"Лист {_s.Paper.Width:0.#} × {_s.Paper.Height:0.#} мм, экспорт 300 dpi"
                : $"Лист {_s.Paper.Width:0.#} × {_s.Paper.Height:0.#} мм — большой формат, "
                  + $"экспорт автоматически в {dpi} dpi (иначе растр не поместится в памяти)";
        }
        PageIndicator.Text = $"Лист {_s.CurrentPage + 1} из {_s.TotalPages}";
        ExportBtn.IsEnabled = total > 0;
        PrintBtn.IsEnabled = total > 0;
        if (_s.PhotoLayout > 0 && _s.PaperSizeKey is "a5" or "a6")
        {
            PhotoLayoutInfo.IsVisible = true;
            PhotoLayoutInfo.Text = $"Размер фото: {_s.CardWidth:0.#} × {_s.CardHeight:0.#} мм · без полей";
        }
        else PhotoLayoutInfo.IsVisible = false;
    }

    private void UpdateNavigation()
    {
        PrevPageBtn.IsEnabled = _s.CurrentPage > 0;
        NextPageBtn.IsEnabled = _s.CurrentPage < _s.TotalPages - 1;
    }

    private void UpdatePageTypeIndicator()
    {
        bool isBack = _s.DuplexMode && _s.CurrentSide == "back";
        PageTypeBtn.Content = isBack ? "ОБОРОТ" : "ЛИЦО";
        PageTypeBtn.Foreground = new SolidColorBrush(Color.Parse(isBack ? "#7a8290" : "#52a8e0"));
    }

    private void UpdateCutSize() =>
        CutSizeDisplay.Text = $"Размер реза: {_s.CutWidth:0.#} × {_s.CutHeight:0.#} мм";

    private void UpdatePolaroidInfo()
    {
        var photo = LayoutEngine.PolaroidPhotoArea(_s);
        PolaroidInfo.Text = $"Фото: {photo.W:0.#} × {photo.H:0.#} мм\nКарточка: {_s.CardWidth:0.#} × {_s.CardHeight:0.#} мм";
    }

    private void SyncCardInputs()
    {
        _updating = true;
        CardWidthInput.Value = (decimal)_s.CardWidth;
        CardHeightInput.Value = (decimal)_s.CardHeight;
        BleedInput.Value = (decimal)_s.Bleed;
        _updating = false;
    }

    private void SyncCalibVisibility()
    {
        CalibModeRow.IsVisible = _s.DuplexMode;
        if (!_s.DuplexMode && _s.CalibMode)
        {
            _s.CalibMode = false;
            _updating = true;
            CalibModeToggle.IsChecked = false;
            _updating = false;
            CalibOptions.IsVisible = false;
        }
    }

    private void RenderBackPreview()
    {
        if (_s.BackImage != null)
        {
            BackPreviewBox.IsVisible = true;
            BackPreviewName.Text = _s.BackImage.Name;
            BackPreviewImage.Source = ThumbOf(_s.BackImage);
        }
        else
        {
            BackPreviewBox.IsVisible = false;
            BackPreviewImage.Source = null;
        }
    }

    private Bitmap ThumbOf(ImageEntry e, int max = 72)
    {
        var src = e.DisplayBitmap(_softproof);
        double k = Math.Min(1.0, max / (double)Math.Max(src.Width, src.Height));
        int w = Math.Max(1, (int)(src.Width * k)), h = Math.Max(1, (int)(src.Height * k));
        using var small = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using (var c = new SKCanvas(small))
        {
            c.Clear(SKColors.White);
            SkiaUtil.DrawScaled(c, src, new SKRect(0, 0, w, h), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        }
        return ToAvaloniaBitmap(small);
    }

    private void RenderCardList()
    {
        ImageListPanel.Children.Clear();
        for (int i = 0; i < _s.Images.Count; i++)
        {
            var img = _s.Images[i];
            var border = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#1f1f1f")),
                BorderBrush = new SolidColorBrush(Color.Parse("#363636")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8)
            };
            var root = new StackPanel { Spacing = 4 };

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("40,*,Auto,Auto,Auto") };
            var thumb = new Image { Width = 36, Height = 36, Stretch = Stretch.UniformToFill, Source = ThumbOf(img) };
            Grid.SetColumn(thumb, 0);
            var name = new TextBlock
            {
                Text = img.Name,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = 12
            };
            Grid.SetColumn(name, 1);

            var rotate = new CheckBox
            {
                IsChecked = img.AutoRotateImage,
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 0,
                Padding = new Thickness(0)
            };
            ToolTip.SetTip(rotate, "Авто-разворот изображения в кадре");
            rotate.IsCheckedChanged += (_, _) =>
            {
                img.AutoRotateImage = rotate.IsChecked == true;
                MarkDirty();
                RenderPreview();
            };

            var qty = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            var minus = SmallBtn("−");
            var qtyText = new TextBlock
            {
                Text = img.Quantity.ToString(),
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 22,
                TextAlignment = TextAlignment.Center
            };
            var plus = SmallBtn("+");
            minus.Click += (_, _) => ChangeQty(img, -1);
            plus.Click += (_, _) => ChangeQty(img, +1);
            qty.Children.Add(minus);
            qty.Children.Add(qtyText);
            qty.Children.Add(plus);
            Grid.SetColumn(rotate, 2);
            Grid.SetColumn(qty, 3);

            var remove = SmallBtn("×");
            remove.Foreground = new SolidColorBrush(Color.Parse("#f87171"));
            remove.Click += (_, _) =>
            {
                _s.Images.Remove(img);
                img.Dispose();
                MarkDirty();
                RenderCardList();
                Recalc();
            };
            Grid.SetColumn(remove, 4);

            row.Children.Add(thumb);
            row.Children.Add(name);
            row.Children.Add(rotate);
            row.Children.Add(qty);
            row.Children.Add(remove);
            root.Children.Add(row);

            if (_s.IndividualBacks)
            {
                var backRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                if (img.BackImage != null)
                {
                    var backThumb = new Image { Width = 26, Height = 26, Stretch = Stretch.UniformToFill, Source = ThumbOf(img.BackImage) };
                    var bt = new Button { Content = backThumb, Padding = new Thickness(1), Background = Brushes.Transparent };
                    ToolTip.SetTip(bt, "Сменить рубашку");
                    bt.Click += async (_, _) => await PickCardBack(img);
                    backRow.Children.Add(bt);
                    backRow.Children.Add(new TextBlock
                    {
                        Text = img.BackImage.Name,
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.Parse("#9a9ea8")),
                        VerticalAlignment = VerticalAlignment.Center,
                        MaxWidth = 130,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });
                    var backRm = SmallBtn("×");
                    backRm.Foreground = new SolidColorBrush(Color.Parse("#f87171"));
                    backRm.Click += (_, _) =>
                    {
                        img.BackImage?.Dispose();
                        img.BackImage = null;
                        MarkDirty();
                        RenderCardList();
                        RenderPreview();
                    };
                    backRow.Children.Add(backRm);
                }
                else
                {
                    var addBack = new Button { Content = "+ Рубашка", FontSize = 11, Padding = new Thickness(8, 3) };
                    addBack.Classes.Add("secondary");
                    addBack.Click += async (_, _) => await PickCardBack(img);
                    backRow.Children.Add(addBack);
                }
                root.Children.Add(backRow);
            }

            border.Child = root;
            ImageListPanel.Children.Add(border);
        }
    }

    private static Button SmallBtn(string text) => new()
    {
        Content = text,
        Padding = new Thickness(7, 2),
        Background = new SolidColorBrush(Color.Parse("#2a2a2a")),
        CornerRadius = new CornerRadius(4)
    };

    private void ChangeQty(ImageEntry img, int delta)
    {
        img.Quantity = Math.Clamp(img.Quantity + delta, 1, 99);
        MarkDirty();
        RenderCardList();
        Recalc();
    }

    // ---------------------------------------------------------------- софтпруф

    private async Task ToggleSoftproof()
    {
        _softproof = !_softproof;
        SoftproofBtn.Content = _softproof ? "Просм. CMYK" : "Просм. RGB";
        if (_softproof) await EnsureProofsAndRefresh();
        else
        {
            RenderCardList();
            RenderBackPreview();
            RenderPreview();
        }
    }

    private IEnumerable<ImageEntry> AllEntries()
    {
        foreach (var im in _s.Images)
        {
            yield return im;
            if (im.BackImage != null) yield return im.BackImage;
        }
        if (_s.BackImage != null) yield return _s.BackImage;
    }

    private async Task EnsureProofsAndRefresh()
    {
        var need = AllEntries().Where(e => e.ProofBitmap == null).ToList();
        if (need.Count > 0)
        {
            await WithOverlay("Софтпруф CMYK...", () => Task.Run(() =>
            {
                foreach (var e in need)
                    e.ProofBitmap = CmykPipeline.Softproof(e.Bitmap);
            }));
        }
        RenderCardList();
        RenderBackPreview();
        RenderPreview();
    }

    // ---------------------------------------------------------------- экспорт

    private async Task ExportCmyk()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Экспорт PDF (CMYK)",
            SuggestedFileName = "cards.pdf",
            FileTypeChoices = new[] { new FilePickerFileType("PDF") { Patterns = new[] { "*.pdf" } } }
        });
        var path = file?.TryGetLocalPath();
        if (path == null) return;
        if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) path += ".pdf";

        string? error = null;
        await WithOverlay("Экспорт CMYK...", () => Task.Run(() =>
        {
            try
            {
                File.WriteAllBytes(path, BuildCmykPdfBytes());
                DevLog.Log("Export", $"успех: '{path}', листов={_s.TotalPages}, формат={_s.Paper.Width}x{_s.Paper.Height}мм");
            }
            catch (Exception ex)
            {
                error = ex.Message;
                DevLog.LogException("Export", $"ошибка экспорта: '{path}'", ex);
            }
        }));
        if (error != null) await Msg.Show(this, "Ошибка экспорта", error);
        else await Msg.Show(this, "Экспорт завершён", "Сохранено:\n" + path);
    }

    /// <summary>Рендер всех страниц (300 dpi) → CMYK SWOP → PDF/X-1a.</summary>
    private byte[] BuildCmykPdfBytes()
    {
        var pages = PrintService.RenderAllPages(_s, LayoutConfig.ExportDpiFor(_s.Paper));
        try
        {
            var cmykPages = pages
                .Select(p => new CmykPage(CmykPipeline.ToCmyk(p.Bmp), p.Bmp.Width, p.Bmp.Height, p.WMm, p.HMm))
                .ToList();
            return CmykPdfWriter.Build(cmykPages, CmykPipeline.SwopIcc);
        }
        finally
        {
            foreach (var p in pages) p.Bmp.Dispose();
        }
    }

    // ---------------------------------------------------------------- печать

    private async Task PrintFlow()
    {
        if (!PrintService.IsSupported)
        {
            await Msg.Show(this, "Печать", "Печать поддерживается в Windows- и macOS-версиях.");
            return;
        }
        var (printers, defaultPrinter) = PrintService.ListPrinters();
        if (printers.Count == 0)
        {
            await Msg.Show(this, "Печать", "Принтеры не найдены");
            return;
        }
        var dlg = new PrintDialog(printers, defaultPrinter, _s);
        await dlg.ShowDialog(this);
        if (dlg.Result is not { } opts) return;

        // Профиль принтера задаёт сторону переворота, смещения оборота и калибровку
        dlg.Profile.ApplyTo(_s);
        SyncDuplexInputs();
        RenderPreview();

        PrintResult? result = null;
        await WithOverlay("Печать...", () => Task.Run(() => { result = PrintService.Print(_s, opts); }));
        if (result is { Ok: true })
            await Msg.Show(this, "Печать", $"Отправлено на печать ({result.Mode}):\n{result.Printer}");
        else
            await Msg.Show(this, "Ошибка печати", result?.Error ?? "Неизвестная ошибка");
    }

    // ---------------------------------------------------------------- калибровка

    private async Task SaveCalibTarget()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Сохранить мишень калибровки",
            SuggestedFileName = $"calibration-{_s.PaperSizeKey}-duplex.pdf",
            FileTypeChoices = new[] { new FilePickerFileType("PDF") { Patterns = new[] { "*.pdf" } } }
        });
        var path = file?.TryGetLocalPath();
        if (path == null) return;
        if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) path += ".pdf";

        string? error = null;
        await WithOverlay("Мишень...", () => Task.Run(() =>
        {
            try
            {
                var paper = _s.Paper;
                var rgbPages = new List<RgbPage>();
                foreach (var label in new[] { "ЛИЦО", "ОБОРОТ" })
                {
                    using var bmp = CalibrationDetector.RenderTargetPage(paper, label, _s.PaperSizeKey, LayoutConfig.ExportDpi);
                    rgbPages.Add(new RgbPage(SkiaUtil.GetRgb(bmp), bmp.Width, bmp.Height, paper.Width, paper.Height));
                }
                File.WriteAllBytes(path, RgbPdfWriter.Build(rgbPages));
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
        }));
        if (error != null) await Msg.Show(this, "Ошибка", error);
        else await Msg.Show(this, "Мишень сохранена", "Сохранено:\n" + path);
    }

    private async Task PickCalibScan(string side)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = side == "back" ? "Скан: ОБОРОТ" : "Скан: ЛИЦО",
            AllowMultiple = false,
            FileTypeFilter = new[] { ImageTypes }
        });
        var p = files.FirstOrDefault()?.TryGetLocalPath();
        if (p != null) await DetectCalibration(p, side);
    }

    private async Task DetectCalibration(string path, string side)
    {
        CalibSide? calib = null;
        await WithOverlay("Анализ скана...", () => Task.Run(() =>
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                var frames = RasterDecoder.Decode(bytes, Path.GetFileName(path));
                if (frames.Count > 0)
                {
                    calib = CalibrationDetector.Detect(frames[0].Bitmap, _s.Paper);
                    foreach (var f in frames) f.Bitmap.Dispose();
                }
            }
            catch
            {
                calib = null;
            }
        }));

        var resultLabel = side == "back" ? CalibResultBack : CalibResultFront;
        resultLabel.IsVisible = true;
        if (calib == null)
        {
            resultLabel.Text = "Метки не найдены — загрузите чёткий скан мишени.";
            return;
        }
        if (side == "back") _s.CalibBack = calib; else _s.CalibFront = calib;
        SaveCalibrationToProfile(side, calib);
        resultLabel.Text = $"Смещение: {calib.Dx:0.##} × {calib.Dy:0.##} мм · Угол: {calib.Angle:0.##}°";
        MarkDirty();
        RenderPreview();
    }

    /// <summary>Сохраняет измеренную калибровку в профиль принтера (.hateprn),
    /// чтобы она пережила перезапуск и переносилась вместе с профилем.</summary>
    private void SaveCalibrationToProfile(string side, CalibSide calib)
    {
        try
        {
            string printer = SettingsStore.Load()["lastPrinter"]?.GetValue<string>() ?? "";
            if (string.IsNullOrEmpty(printer)) return;
            var profile = PrinterProfile.Load(printer);
            profile.SetAutoCalibration(side, calib);
            profile.Save();
        }
        catch
        {
            // профиль не обязателен — калибровка всё равно применена к текущей раскладке
        }
    }

    /// <summary>Синхронизирует поля смещения оборота после применения профиля.</summary>
    private void SyncDuplexInputs()
    {
        _updating = true;
        OffsetXInput.Value = (decimal)_s.OffsetX;
        OffsetYInput.Value = (decimal)_s.OffsetY;
        _updating = false;
    }

    // ---------------------------------------------------------------- .hate

    private async Task<bool> SaveProject(bool saveAs)
    {
        string? path = _projectPath;
        if (saveAs || path == null)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Сохранить проект",
                SuggestedFileName = Path.GetFileName(_projectPath) ?? "project.hate",
                FileTypeChoices = new[] { new FilePickerFileType("Проект iHateCards") { Patterns = new[] { "*.hate" } } }
            });
            path = file?.TryGetLocalPath();
            if (path == null) return false;
            if (!path.EndsWith(".hate", StringComparison.OrdinalIgnoreCase)) path += ".hate";
        }

        string? error = null;
        string savePath = path;
        await WithOverlay("Сохранение...", () => Task.Run(() =>
        {
            try { HateFile.Save(_s, savePath); DevLog.Log("Project", $"сохранено: '{savePath}'"); }
            catch (Exception ex) { error = ex.Message; DevLog.LogException("Project", $"ошибка сохранения: '{savePath}'", ex); }
        }));
        if (error != null)
        {
            await Msg.Show(this, "Ошибка сохранения", error);
            return false;
        }
        _projectPath = savePath;
        _dirty = false;
        UpdateTitle();
        return true;
    }

    private async Task OpenProjectViaDialog()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Открыть проект",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Проект iHateCards") { Patterns = new[] { "*.hate" } } }
        });
        var p = files.FirstOrDefault()?.TryGetLocalPath();
        if (p != null) await OpenProject(p);
    }

    public async Task OpenProject(string path)
    {
        if (_dirty)
        {
            var r = await Msg.YesNoCancel(this, "Несохранённые изменения",
                "Сохранить текущий проект перед открытием другого?");
            if (r == "cancel" || r == null) return;
            if (r == "yes" && !await SaveProject(saveAs: false)) return;
        }

        AppState? loaded = null;
        string? error = null;
        await WithOverlay("Открытие...", () => Task.Run(() =>
        {
            try { loaded = HateFile.Open(path); DevLog.Log("Project", $"открыто: '{path}'"); }
            catch (Exception ex) { error = ex.Message; DevLog.LogException("Project", $"ошибка открытия: '{path}'", ex); }
        }));
        if (loaded == null)
        {
            await Msg.Show(this, "Ошибка открытия", error ?? "Не удалось открыть проект");
            return;
        }

        foreach (var im in _s.Images) im.Dispose();
        _s.BackImage?.Dispose();
        _s = loaded;
        _projectPath = path;
        _dirty = false;
        _softproof = false;
        SoftproofBtn.Content = "Просм. RGB";
        SyncAllControlsFromState();
        RenderCardList();
        RenderBackPreview();
        Recalc();
        UpdateTitle();
    }

    /// <summary>После открытия проекта — привести все контролы к состоянию.</summary>
    private void SyncAllControlsFromState()
    {
        _updating = true;
        PaperSizeCombo.SelectedIndex = _s.PaperSizeKey == PaperSizes.CustomKey
            ? PaperSizes.All.Length
            : Array.FindIndex(PaperSizes.All, p => p.Key == _s.PaperSizeKey);
        CustomPaperSection.IsVisible = _s.PaperSizeKey == PaperSizes.CustomKey;
        PaperWidthInput.Value = (decimal)_s.CustomPaperWidth;
        PaperHeightInput.Value = (decimal)_s.CustomPaperHeight;
        PhotoLayoutCombo.SelectedIndex = _s.PhotoLayout switch { 2 => 1, 4 => 2, _ => 0 };
        PhotoLayoutSection.IsVisible = _s.PaperSizeKey is "a5" or "a6";
        CardSizeSection.IsVisible = _s.PhotoLayout == 0;
        CardWidthInput.Value = (decimal)_s.CardWidth;
        CardHeightInput.Value = (decimal)_s.CardHeight;
        BleedInput.Value = (decimal)_s.Bleed;
        DuplexToggle.IsChecked = _s.DuplexMode;
        DuplexOptions.IsVisible = _s.DuplexMode;
        CropMarksToggle.IsChecked = _s.ShowCropMarks;
        CropMarksSubOptions.IsVisible = _s.ShowCropMarks;
        NoHaloToggle.IsChecked = _s.NoHaloMarks;
        FitImageToggle.IsChecked = _s.FitImage;
        AutoRotateToggle.IsChecked = _s.AutoRotate;
        AutoRotateFrameToggle.IsChecked = _s.AutoRotateFrame;
        PolaroidToggle.IsChecked = _s.PolaroidMode;
        PolaroidOptions.IsVisible = _s.PolaroidMode;
        PolaroidSideInput.Value = (decimal)_s.PolaroidSide;
        PolaroidTopInput.Value = (decimal)_s.PolaroidTop;
        PolaroidBottomInput.Value = (decimal)_s.PolaroidBottom;
        BorderlessToggle.IsChecked = _s.Borderless;
        CalibModeRow.IsVisible = _s.DuplexMode;
        CalibModeToggle.IsChecked = _s.CalibMode;
        CalibOptions.IsVisible = _s.CalibMode;
        OffsetXInput.Value = (decimal)_s.OffsetX;
        OffsetYInput.Value = (decimal)_s.OffsetY;
        IndividualBacksToggle.IsChecked = _s.IndividualBacks;
        SingleBackArea.IsVisible = !_s.IndividualBacks;
        IndividualBackHint.IsVisible = _s.IndividualBacks;
        _updating = false;
        UpdateCutSize();
        UpdatePolaroidInfo();
    }

    private void MarkDirty()
    {
        if (!_dirty)
        {
            _dirty = true;
            UpdateTitle();
        }
    }

    private void UpdateTitle()
    {
        string name = _projectPath != null ? Path.GetFileName(_projectPath) : "новый проект";
        Title = $"iHateCards — {name}{(_dirty ? " *" : "")}";
    }

    // ---------------------------------------------------------------- меню macOS

    /// <summary>На macOS команды живут в системной строке меню (как принято в
    /// системе), а одноимённые кнопки из правой панели убираются, чтобы не
    /// дублировать одно и то же действие в двух местах.</summary>
    private void BuildMacMenu()
    {
        if (!OperatingSystem.IsMacOS()) return;

        NativeMenuItem Item(string header, string? gesture, Func<Task> action)
        {
            var item = new NativeMenuItem(header);
            if (gesture != null) item.Gesture = KeyGesture.Parse(gesture);
            item.Click += (_, _) => _ = action();
            return item;
        }

        var file = new NativeMenuItem("Файл") { Menu = new NativeMenu() };
        file.Menu.Add(Item("Открыть…", "Cmd+O", () => OpenProjectViaDialog()));
        file.Menu.Add(Item("Сохранить", "Cmd+S", () => SaveProject(saveAs: false)));
        file.Menu.Add(Item("Сохранить как…", "Shift+Cmd+S", () => SaveProject(saveAs: true)));
        file.Menu.Add(new NativeMenuItemSeparator());
        file.Menu.Add(Item("Экспорт PDF (CMYK)…", "Cmd+E", () => ExportCmyk()));
        file.Menu.Add(Item("Печать…", "Cmd+P", () => PrintFlow()));

        var app = new NativeMenuItem("iHateCards") { Menu = new NativeMenu() };
        app.Menu.Add(Item("О программе iHateCards", null, () => new Dialogs.AboutDialog().ShowDialog(this)));
        app.Menu.Add(new NativeMenuItemSeparator());
        app.Menu.Add(Item("Проверить обновления…", null, () => CheckForUpdatesAsync(silent: false)));
        app.Menu.Add(Item("Диагностика…", null, () => new DebugDialog().ShowDialog(this)));

        var menu = new NativeMenu();
        menu.Add(file);
        menu.Add(app);
        NativeMenu.SetMenu(this, menu);

        // Те же действия в правой панели больше не нужны
        ExportBtn.IsVisible = false;
        PrintBtn.IsVisible = false;
        SaveProjectBtn.IsVisible = false;
        OpenProjectBtn.IsVisible = false;
        UpdateBtn.IsVisible = false;
        DebugBtn.IsVisible = false;
        FileButtonsRow.IsVisible = false;
    }

    // ---------------------------------------------------------------- обновления

    private UpdateToast? _updateToast;

    /// <summary>Проверка обновлений: при запуске — молча (ошибки сети игнорируются),
    /// по кнопке — с сообщением, если обновлений нет.</summary>
    private async Task CheckForUpdatesAsync(bool silent)
    {
        if (App.UiShotPath != null && UpdateChecker.ManifestUrl == UpdateChecker.DefaultManifestUrl)
            return;                                      // служебный режим скриншота
        if (silent && !UpdateChecker.AutoCheckEnabled) return;
        if (silent) await Task.Delay(TimeSpan.FromSeconds(3));

        var info = await UpdateChecker.CheckAsync(ignoreSkipped: !silent);
        if (info == null)
        {
            if (!silent)
                await Msg.Show(this, "Обновления",
                    $"Установлена последняя версия ({AppVersion.Current}).");
            return;
        }

        _updateToast?.Close();
        _updateToast = new UpdateToast(info);
        _updateToast.Show(this);
    }

    // ---------------------------------------------------------------- утилиты

    private async Task WithOverlay(string text, Func<Task> action)
    {
        LoadingText.Text = text;
        LoadingOverlay.IsVisible = true;
        try { await action(); }
        finally { LoadingOverlay.IsVisible = false; }
    }
}

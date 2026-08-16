# iHateCards (.NET)

Раскладка карт для печати с прямым выводом в **CMYK (US Web Coated SWOP)** —
нативное десктоп-приложение на **C# / .NET 10 + Avalonia** (переписано с нуля
с Python/pywebview-версии, без HTML/WebView).

## Возможности

- Раскладка карт на листе: A6/A5/A4/A3/SRA3, размер карты, блид, метки реза, полароид-рамка.
- Двухсторонняя печать (лицо/рубашка), индивидуальные рубашки, калибровка двух сторон по сканам мишени.
- Импорт **PNG/JPG/WEBP/GIF/BMP/TIFF (многостраничный)/PSD/PDF** — офлайн (Magick.NET, PDFium).
- **Экспорт в PDF/X-1a:2003, CMYK US Web Coated (SWOP)** со встроенным ICC-профилем,
  без потерь (FlateDecode), 300 dpi. PDF собирается вручную побайтово.
- Софтпруф CMYK в превью (sRGB → SWOP → sRGB, LittleCMS внутри Magick.NET).
- Печать (Windows):
  - **PostScript-принтеры (Xerox и т.п.)** — прямой DeviceCMYK PostScript: RAW-спулер или
    IP:9100 (в обход WSD-порта), авто-определение IP по MAC из имени принтера.
  - **Не-PostScript (EPSON и любые)** — PDF + Ghostscript (`mswinpr2`), выбор качества (DPI).
  - Настройки печати (копии, размер: реальный/подогнать/%/70·66, ч/б, экономия тонера)
    сохраняются между запусками в `%LOCALAPPDATA%\iHateCards\settings.json`.
- **Формат проекта `.hate`** — ZIP (manifest.json / layout.json / print-settings.json / assets
  с оригиналами без пересжатия). Save / Open, Ctrl+S / Ctrl+O, ассоциация файлов,
  открытие двойным кликом в уже запущенное окно (single instance), индикатор «*» в заголовке.

## Структура

| Путь | Назначение |
|---|---|
| `src/iHateCards/Core/` | `AppState`, `LayoutEngine` (вся математика раскладки), `PageRenderer` (Skia, единый рендер превью/экспорта), `CalibrationDetector` |
| `src/iHateCards/Imaging/` | Декодеры (Magick.NET, PDFium), `CmykPipeline` (ICC SWOP), Skia-утилиты |
| `src/iHateCards/Pdf/` | Побайтовые писатели: PDF/X-1a (CMYK), RGB-PDF, CMYK PostScript |
| `src/iHateCards/Printing/` | winspool P/Invoke (RAW, свойства принтера), TCP:9100, Ghostscript, определение IP |
| `src/iHateCards/Project/` | Формат `.hate`, настройки |
| `src/iHateCards/Dialogs/` | Диалог печати, сообщения |
| `installer/` | Установщик MSI (`build-installer.sh` + `iHateCards.wixl.wxs`), альтернативно Inno Setup (`iHateCards.iss`) |

## Запуск из исходников (Windows / macOS / Linux)

```
dotnet run --project src/iHateCards
```

Печать доступна только под Windows; остальное (раскладка, импорт, экспорт CMYK,
`.hate`) работает на любой ОС — удобно для разработки.

## Сборка Windows-дистрибутива

```
dotnet publish src/iHateCards/iHateCards.csproj -c Release -r win-x64 --self-contained -o publish/win-x64
```

.NET на машине пользователя не нужен (self-contained), WebView2/браузерных
зависимостей нет. Размер каталога ~140 МБ.

## Установщик (MSI)

```
./installer/build-installer.sh
```

Скрипт публикует приложение и собирает `publish/iHateCards-2.0.0-x64.msi`
(~63 МБ): установка в `Program Files\iHateCards`, ярлыки в меню «Пуск» и на
рабочем столе, **ассоциация файлов `.hate`** (двойной клик открывает проект),
запись в «Программы и компоненты» с корректным удалением.

Сборка работает и не под Windows — нужен `msitools`:
`brew install msitools` (macOS) или `apt install msitools` (Linux).
На Windows тот же `.wxs` собирается WiX v3 (`candle`/`light`).

Альтернатива для Windows: `installer/iHateCards.iss` (Inno Setup) — EXE-установщик
с тем же набором (ярлыки, ассоциация `.hate`); требует установленного Inno Setup.

## Зависимости для печати

Не-PostScript печать использует **Ghostscript** (ищется в PATH и `C:\Program Files\gs`).
Для PostScript-принтеров Ghostscript не нужен.

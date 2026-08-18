# iHateCards (.NET)

Раскладка карт для печати с прямым выводом в **CMYK (US Web Coated SWOP)** —
нативное десктоп-приложение на **C# / .NET 10 + Avalonia** (переписано с нуля
с Python/pywebview-версии, без HTML/WebView).

## Возможности

- Раскладка карт на листе: A6/A5/A4/A3/SRA3, размер карты, блид, метки реза, полароид-рамка.
- **Два независимых авто-разворота**: изображения внутри кадра (чекбокс у каждой карты)
  и самого кадра на листе — если карта, повёрнутая на 90°, помещается в большем
  количестве. На обороте угол автоматически инвертируется, поэтому после переворота
  листа рубашка совпадает с лицом по ориентации, а не встаёт вверх ногами.
- Двухсторонняя печать (лицо/рубашка), индивидуальные рубашки, калибровка двух сторон по сканам мишени.
- Импорт **PNG/JPG/WEBP/GIF/BMP/TIFF (многостраничный)/PSD/PDF** — офлайн (Magick.NET, PDFium).
- **Экспорт в PDF/X-1a:2003, CMYK US Web Coated (SWOP)** со встроенным ICC-профилем,
  без потерь (FlateDecode), 300 dpi. PDF собирается вручную побайтово.
- Софтпруф CMYK в превью (sRGB → SWOP → sRGB, LittleCMS внутри Magick.NET).
- Печать:
  - **PostScript-принтеры (Xerox и т.п.)** — прямой DeviceCMYK PostScript: RAW-спулер или
    IP:9100 (в обход WSD-порта), авто-определение IP по MAC из имени принтера.
  - **Не-PostScript (EPSON и любые)** — PDF + Ghostscript (`mswinpr2`), выбор качества (DPI).
  - **macOS** — печать через CUPS: RAW-PostScript (`lp -o raw`) для PostScript-принтеров,
    PDF через драйвер для остальных, двусторонняя печать через `sides=two-sided-*`.
  - Настройки печати (копии, размер: реальный/подогнать/%/70·66, ч/б, экономия тонера)
    сохраняются между запусками в `%LOCALAPPDATA%\iHateCards\settings.json`.
- **Обновление по сети без прав администратора**: программа ставится в папку пользователя
  и обновляет себя сама. При старте проверяет манифест обновлений; если версия новее —
  показывает уведомление в углу экрана с кратким патчноутом и тремя кнопками:
  **Обновить**, **Не сейчас**, **Пропустить версию**. Пакет проверяется по SHA-256,
  файлы подменяются после выхода из программы с откатом при сбое.
- **Профиль принтера `.hateprn`** — файл конфигурации двухсторонней печати: сторона
  переворота (длинная/короткая), смещения оборота, калибровка сторон с переключателем
  «авто-настройка» (значения со сканов мишени) и ручной правкой, импорт/экспорт.
- **Формат проекта `.hate`** — ZIP (manifest.json / layout.json / print-settings.json / assets
  с оригиналами без пересжатия). Save / Open, Ctrl+S / Ctrl+O, ассоциация файлов,
  открытие двойным кликом в уже запущенное окно (single instance), индикатор «*» в заголовке.

## Структура

| Путь | Назначение |
|---|---|
| `src/iHateCards/Core/` | `AppState`, `LayoutEngine` (вся математика раскладки), `PageRenderer` (Skia, единый рендер превью/экспорта), `CalibrationDetector` |
| `src/iHateCards/Imaging/` | Декодеры (Magick.NET, PDFium), `CmykPipeline` (ICC SWOP), Skia-утилиты |
| `src/iHateCards/Pdf/` | Побайтовые писатели: PDF/X-1a (CMYK), RGB-PDF, CMYK PostScript |
| `src/iHateCards/Printing/` | winspool P/Invoke (RAW, свойства принтера), TCP:9100, Ghostscript, CUPS (macOS), определение IP |
| `src/iHateCards/Project/` | Форматы `.hate` и `.hateprn`, настройки |
| `src/iHateCards/Dialogs/` | Диалог печати, сообщения |
| `src/iHateCards/Update/` | Автообновление: проверка манифеста, уведомление в углу, скачивание и подмена файлов |
| `installer/` | Установщик MSI (`build-installer.sh`), сборка macOS (`build-macos.sh`), релиз с манифестом (`build-release.sh`) |

## Запуск из исходников (Windows / macOS / Linux)

```
dotnet run --project src/iHateCards
```

Печать работает в Windows (winspool/GDI/Ghostscript) и macOS (CUPS); остальное
(раскладка, импорт, экспорт CMYK, `.hate`) — на любой ОС, включая Linux.

## Сборка Windows-дистрибутива

```
dotnet publish src/iHateCards/iHateCards.csproj -c Release -r win-x64 --self-contained -o publish/win-x64
```

.NET на машине пользователя не нужен (self-contained), WebView2/браузерных
зависимостей нет. Размер каталога ~140 МБ.

## Установщик (MSI, без прав администратора)

```
./installer/build-installer.sh
```

Скрипт публикует приложение и собирает `publish/iHateCards-2.1.0-x64.msi`
(~62 МБ): установка в `%LOCALAPPDATA%\Programs\iHateCards` **без запроса прав
администратора** (именно поэтому возможно и обновление по сети), ярлыки в меню
«Пуск» и на рабочем столе, **ассоциация файлов `.hate`** (двойной клик открывает
проект), запись в «Программы и компоненты» с корректным удалением.

Сборка работает и не под Windows — нужен `msitools`:
`brew install msitools` (macOS) или `apt install msitools` (Linux).
На Windows тот же `.wxs` собирается WiX v3 (`candle`/`light`).

Альтернатива для Windows: `installer/iHateCards.iss` (Inno Setup) — EXE-установщик
с тем же набором (ярлыки, ассоциация `.hate`); требует установленного Inno Setup.

## macOS

```
./installer/build-macos.sh 2.1.0 arm64
```

Собирает `iHateCards.app` (Info.plist с ассоциацией `.hate`, иконка `.icns`,
ad-hoc-подпись) и **оформленный DMG**: в окне образа слева программа, справа
папка «Программы» и стрелка между ними — установка перетаскиванием, как принято
в macOS. Оформление задаётся в `installer/dmg-settings.py`, фон генерируется
скриптом `installer/make-dmg-background.py`; нужен `pip install --user dmgbuild`
(без него соберётся простой образ без оформления).

Без сертификата Apple первый запуск — через контекстное меню → «Открыть».
Дальше программа обновляется сама, не спрашивая пароль.

## Релиз и обновления

```
./installer/build-release.sh 2.1.0            # собрать пакеты + updates.json
./installer/build-release.sh 2.1.0 --publish  # и выложить релиз
```

Скрипт собирает пакеты для win-x64/osx-arm64/osx-x64, считает SHA-256 и формирует
`updates.json` (версия, патчноут из `installer/release-notes.txt`, ссылки).
Публикуется в **отдельный публичный репозиторий** (по умолчанию
`AmajaWeed/ihatecards-updates`, меняется переменной `IHATECARDS_UPDATES_REPO`) —
исходный код при этом остаётся закрытым, а обновления скачиваются без токена.

Проверить обновление локально, не публикуя релиз:

```
IHATECARDS_UPDATE_URL=http://127.0.0.1:8777/updates.json dotnet run --project src/iHateCards
```

## Зависимости для печати

Не-PostScript печать использует **Ghostscript** (ищется в PATH и `C:\Program Files\gs`).
Для PostScript-принтеров Ghostscript не нужен.

# iHateCards

Раскладка карт для печати с прямым выводом в **CMYK (US Web Coated SWOP)** — десктоп-приложение (Python + pywebview/WebView2) с интерфейсом на HTML.

## Возможности
- Раскладка карт на листе: A6/A5/A4/A3/SRA3, размер карты, блид, метки реза, полароид-рамка.
- Двухсторонняя печать (лицо/рубашка), индивидуальные рубашки, калибровка двух сторон.
- Импорт **PNG/JPG/WEBP/TIFF/PDF** (TIFF/большие форматы декодируются в Python, офлайн).
- **Экспорт в PDF/X-1a:2003, CMYK US Web Coated (SWOP)** со встроенным ICC-профилем, без потерь (FlateDecode), 300 dpi.
- Софтпруф CMYK в превью.
- Печать:
  - **PostScript-принтеры (Xerox и т.п.)** — прямой CMYK-PostScript на IP:9100 (в обход WSD-порта), авто-определение IP по MAC из имени принтера.
  - **Не-PostScript (EPSON и любые)** — через PDF + Ghostscript (драйвер), выбор качества (DPI).
  - Настройки печати: копии, размер (реальный / подогнать / % / пресеты 70·66), ч/б, экономия тонера; сохраняются между запусками.

## Структура
| Файл | Назначение |
|---|---|
| `app.py` | Окно приложения (pywebview) + бэкенд экспорта/печати |
| `cmyk_export.py` | RGB→CMYK (LittleCMS), сборка PDF/X, CMYK-PostScript, ресемпл |
| `winprint.py` | Печать: RAW-спулер, TCP/9100, GDI, свойства принтера, определение IP |
| `app_inject.js` | Мост между интерфейсом и Python (внедряется в HTML) |
| `iHateCards.html` | Интерфейс (со встроенными шрифтами/лого и внедрённым мостом) |
| `USWebCoatedSWOP.icc` | ICC-профиль CMYK (вшивается в экспорт/приложение) |
| `iHateCards.iss` | Скрипт установщика (Inno Setup) |

## Запуск из исходников
```
pip install pywebview Pillow
python app.py
```

## Сборка .exe
```
pip install pyinstaller
python -m PyInstaller --noconfirm --windowed --name iHateCards --icon setup.ico ^
  --collect-all webview ^
  --add-data "iHateCards.html;." ^
  --add-data "USWebCoatedSWOP.icc;." ^
  --add-data "iHateCards — Расстановка карт для печати_files;iHateCards — Расстановка карт для печати_files" ^
  app.py
```

## Установщик
Собирается через Inno Setup (`iHateCards.iss`); проверяет/ставит WebView2 Runtime.

## Зависимости для печати
- Не-PostScript печать использует **Ghostscript** (mswinpr2). Для PostScript-принтеров Ghostscript не нужен.

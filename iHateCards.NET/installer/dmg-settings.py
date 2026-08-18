# -*- coding: utf-8 -*-
"""Оформление DMG: окно с иконкой программы слева, папкой «Программы» справа
и стрелкой между ними — пользователь перетаскивает приложение для установки.

Собирается через dmgbuild (пишет оформление напрямую, без автоматизации
Finder — работает и в сборочных скриптах):
    dmgbuild -s installer/dmg-settings.py -D app=publish/iHateCards.app \
             "iHateCards" publish/iHateCards.dmg
"""
import os.path

# dmgbuild выполняет этот файл без __file__, поэтому путь передаётся через -D here=…
here = defines.get("here", os.path.join(os.getcwd(), "installer"))                        # noqa: F821
application = defines.get("app", os.path.join(here, "..", "publish", "iHateCards.app"))   # noqa: F821
appname = os.path.basename(application)

# --- содержимое образа ---
files = [application]
symlinks = {"Applications": "/Applications"}          # цель перетаскивания
hide_extension = [appname]

# --- вид окна ---
format = "UDZO"                                        # сжатый образ только для чтения
background = os.path.join(here, "dmg-background.tiff")  # обычный + Retina в одном файле
window_rect = ((200, 180), (640, 400))                 # позиция и размер окна
default_view = "icon-view"
show_icon_preview = False
show_status_bar = False
show_tab_view = False
show_toolbar = False
show_pathbar = False
show_sidebar = False
arrange_by = None
grid_offset = (0, 0)
label_pos = "bottom"
text_size = 13
icon_size = 128

# Позиции совпадают с координатами стрелки на фоне (make-dmg-background.py)
icon_locations = {
    appname: (160, 200),
    "Applications": (480, 200),
}

# Иконка тома — та же, что у приложения
_icns = os.path.join(application, "Contents", "Resources", "iHateCards.icns")
if os.path.exists(_icns):
    icon = _icns

#!/usr/bin/env python3
"""Фон окна DMG: подпись и стрелка от иконки программы к папке «Программы».

Генерирует dmg-background.png (640×400) и @2x, затем собирает из них
dmg-background.tiff — так Finder берёт чёткий вариант на Retina.
Запускается один раз; результат лежит в репозитории.
"""
import os
import subprocess
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
FONT = os.path.join(HERE, "..", "src", "iHateCards", "Assets", "Fonts", "Jost-Regular.ttf")
FONT_BOLD = os.path.join(HERE, "..", "src", "iHateCards", "Assets", "Fonts", "Jost-Bold.ttf")

W, H = 640, 400
ICON_L, ICON_R, ICON_Y = 160, 480, 200     # центры иконок (те же в dmg-settings.py)
BG_TOP, BG_BOTTOM = (27, 27, 27), (18, 18, 18)
ACCENT = (82, 168, 224)
TEXT = (238, 240, 246)
MUTED = (154, 158, 168)


def render(scale: int) -> Image.Image:
    w, h = W * scale, H * scale
    img = Image.new("RGB", (w, h))
    d = ImageDraw.Draw(img)

    # вертикальный градиент фона
    for y in range(h):
        t = y / h
        d.line([(0, y), (w, y)],
               fill=tuple(int(a + (b - a) * t) for a, b in zip(BG_TOP, BG_BOTTOM)))

    title = ImageFont.truetype(FONT_BOLD, 26 * scale)
    sub = ImageFont.truetype(FONT, 15 * scale)
    d.text((w / 2, 52 * scale), "iHateCards", font=title, fill=TEXT, anchor="mm")
    d.text((w / 2, 84 * scale), "Перетащите программу в папку «Программы»",
           font=sub, fill=MUTED, anchor="mm")

    # стрелка между иконками
    y = ICON_Y * scale
    x1, x2 = (ICON_L + 92) * scale, (ICON_R - 92) * scale
    d.line([(x1, y), (x2, y)], fill=ACCENT, width=4 * scale)
    head = 14 * scale
    d.polygon([(x2 + head, y), (x2 - head // 2, y - head), (x2 - head // 2, y + head)], fill=ACCENT)

    d.text((w / 2, (H - 54) * scale), "После установки программа обновляется сама",
           font=ImageFont.truetype(FONT, 12 * scale), fill=MUTED, anchor="mm")
    return img


png = os.path.join(HERE, "dmg-background.png")
png2x = os.path.join(HERE, "dmg-background@2x.png")
render(1).save(png)
render(2).save(png2x)

tiff = os.path.join(HERE, "dmg-background.tiff")
subprocess.run(["tiffutil", "-cathidpicheck", png, png2x, "-out", tiff], check=True,
               stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
os.remove(png2x)
print("готово:", tiff)

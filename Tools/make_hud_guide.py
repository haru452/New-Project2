# HUD の見かたの板の画像: スクリーンショットに番号の丸を描く（番号の意味は板の文字で 3 言語）
#  python Tools/make_hud_guide.py  →  Assets/My script/bridge/hud_guide_1.png / hud_guide_2.png
import sys
from PIL import Image, ImageDraw, ImageFont
SRC = r"C:\Users\haru0\OneDrive\画像\スクリーンショット"
OUT = "Assets/My script/bridge"
F = ImageFont.truetype("C:/Windows/Fonts/arialbd.ttf", 30)
# (番号, 丸の場所, 指す先)
SHOTS = [
    ("スクリーンショット 2026-11-08 160328.png", "hud_guide_1.png", [
        (1, (165, 105), (130, 103)),     # SPEED
        (2, (495, 120), (448, 119)),   # ALTITUDE
        (3, (380, 180), (332, 154)),   # P / R
        (4, (180, 230), (226, 219)),   # HDG
        (5, (340, 290), (295, 262)),   # +
        (6, (520, 70), (640, 160)),    # radar
        (7, (1240, 80), (1240, 128)),  # score
        (8, (880, 240), (1080, 330)),  # THR
        (9, (950, 440), (1040, 460)),  # VS
        (10, (900, 560), (980, 515)),  # FUEL
    ]),
    ("スクリーンショット 2026-11-08 160339.png", "hud_guide_2.png", [
        (11, (120, 300), (300, 330)),  # HP
        (12, (110, 440), (170, 430)),  # weapons
        (13, (450, 245), (520, 245)),  # AoA
        (14, (470, 420), (522, 368)),  # lamps
    ]),
]
for src, out, marks in SHOTS:
    im = Image.open(SRC + "\\" + src).convert("RGB")
    d = ImageDraw.Draw(im)
    for n, (cx, cy), (tx, ty) in marks:
        d.line([(cx, cy), (tx, ty)], fill=(255, 210, 0), width=4)
        d.ellipse([tx - 6, ty - 6, tx + 6, ty + 6], fill=(255, 210, 0))
        r = 24
        d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(255, 210, 0), outline=(0, 0, 0), width=3)
        s = str(n)
        w = d.textlength(s, font=F)
        d.text((cx - w / 2, cy - 17), s, font=F, fill=(0, 0, 0))
    im.save(OUT + "/" + out)
    print(out, im.size)

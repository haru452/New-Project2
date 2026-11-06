# 世界ランキングの注意事項の板（rank_notice_en / ja / zh.png）を作る。
# 使い方: python Tools/make_rank_notice.py   （文章を直したら、もう一度実行する）
from PIL import Image, ImageDraw, ImageFont
import os

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "My script", "drone", "Rank")
FONTS = "C:/Windows/Fonts/"
NOTO = FONTS + "NotoSansJP-VF.ttf"
W, H = 1000, 714
BG = (8, 15, 26)
CYAN = (90, 240, 255)
TITLE = (235, 245, 255)
TEXT = (225, 232, 240)

DATA = {
    "en": ("About the World Ranking", [
        "When ON, only your best score and your chosen title are sent (your name is never sent).",
        "Your IP address is not stored. Only your country is known, and it is shown on the ranking.",
        "It takes 5–7 minutes to appear. Scores are sent only when \"Allow Untrusted URLs\" is ON in VRChat.",
        "Sent scores cannot be removed. You can turn the switch OFF at any time.",
        "For details, contact hichi2025.",
    ]),
    "ja": ("世界ランキングについて", [
        "オンにすると、最高点と選んだ称号だけが送られます（名前は送りません）。",
        "IPアドレスは保存しません。わかるのは国だけで、ランキングに国名が表示されます。",
        "表示まで5〜7分かかります。VRChatの「Allow Untrusted URLs」がオンのときだけ送れます。",
        "送った記録は取り消せません。スイッチはいつでもオフにできます。",
        "くわしくは hichi2025 まで。",
    ]),
    "zh": ("关于世界排行榜", [
        "开启后，只发送你的最高分和选择的称号（不发送名字）。",
        "不保存IP地址。只知道国家，排行榜上会显示国家名。",
        "显示需要约5～7分钟。只有在VRChat中开启“Allow Untrusted URLs”时才能发送。",
        "已发送的记录无法删除。开关可以随时关闭。",
        "详情请联系 hichi2025。",
    ]),
}


def font(lang, size, bold=False):
    if lang == "zh":
        return ImageFont.truetype(FONTS + ("msyhbd.ttc" if bold else "msyh.ttc"), size)
    f = ImageFont.truetype(NOTO, size)
    try:
        f.set_variation_by_axes([700 if bold else 400])
    except Exception:
        pass
    return f


def wrap(d, text, f, width):
    lines, cur = [], ""
    for ch in text:
        if d.textlength(cur + ch, font=f) > width and cur:
            if ch != " " and cur[-1] != " " and " " in cur and ord(ch) < 128:   # 英語は単語の途中で切らない
                k = cur.rfind(" ")
                lines.append(cur[:k]); cur = cur[k + 1:] + ch
            else:
                lines.append(cur); cur = ch.lstrip()
        else:
            cur += ch
    if cur:
        lines.append(cur)
    return lines


def make(lang):
    title, items = DATA[lang]
    im = Image.new("RGB", (W, H), BG)
    d = ImageDraw.Draw(im)
    d.rectangle((6, 6, W - 7, H - 7), outline=CYAN, width=4)
    d.text((50, 82), title, font=font(lang, 50, True), fill=TITLE, anchor="ls")
    d.rectangle((50, 119, W - 50, 121), fill=CYAN)
    f = font(lang, 32)
    y = 160
    for it in items:
        d.ellipse((52, y + 6, 65, y + 19), fill=CYAN)
        for ln in wrap(d, it, f, W - 87 - 70):
            d.text((87, y), ln, font=f, fill=TEXT)
            y += 46
        y += 16
    path = os.path.join(OUT, "rank_notice_" + lang + ".png")
    im.save(path)
    print(path, "y_end", y)


for lg in ("en", "ja", "zh"):
    make(lg)

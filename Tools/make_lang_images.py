# main room の説明の画像を、English / 日本語 / 中文 の 3 言語に分けて作る。
# 使い方: python Tools/make_lang_images.py   （Assets の中に <名前>_en.png / _ja.png / _zh.png を書き出す）
# 文章を直したら、もう一度実行する。
from PIL import Image, ImageDraw, ImageFont
import os

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "My script", "bridge")
FONTS = "C:/Windows/Fonts/"
NOTO = FONTS + "NotoSansJP-VF.ttf"


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
            # 英語は単語の途中で切らない
            if ch != " " and cur[-1] != " " and " " in cur and ord(ch) < 128:
                k = cur.rfind(" ")
                lines.append(cur[:k]); cur = cur[k + 1:] + ch
            else:
                lines.append(cur); cur = ch.lstrip()
        else:
            cur += ch
    if cur:
        lines.append(cur)
    return lines


# ---------------- 操作方法の板 ----------------
BG = (14, 20, 28); ORANGE = (255, 190, 70); KEYBOX = (45, 58, 72); SEP = (60, 78, 95)
KEYTXT = (230, 232, 228); DESC = (240, 240, 236); NOTE = (213, 216, 212)

TITLE = {
    ("pc", "ja"): "R35 操作方法  PC", ("pc", "en"): "R35 CONTROLS  DESKTOP", ("pc", "zh"): "R35 操作说明  电脑",
    ("vr", "ja"): "R35 操作方法  VR", ("vr", "en"): "R35 CONTROLS  VR", ("vr", "zh"): "R35 操作说明  VR",
}

# (キー ja, キー en, キー zh), (説明 ja, en, zh)
PC_ROWS = [
    (("W / S（↑ / ↓）", "W / S (↑ / ↓)", "W / S（↑ / ↓）"), ("機首を下げる / 上げる", "Nose down / up", "机头下压 / 抬起")),
    (("A / D（← / →）", "A / D (← / →)", "A / D（← / →）"), ("左右に傾ける（横転）", "Roll left / right", "向左 / 向右倾斜（横滚）")),
    (("Q / E",) * 3, ("機首を左右に向ける", "Yaw left / right", "机头向左 / 向右偏转")),
    (("左Shift / 左Ctrl", "Left Shift / Left Ctrl", "左Shift / 左Ctrl"), ("スロットル 上げ / 下げ", "Throttle up / down", "油门 加 / 减")),
    (("Space / 左クリック", "Space / Left click", "Space / 鼠标左键"), ("撃つ", "Fire", "开火")),
    (("1 / 2 / 3 / 4",) * 3, ("機銃 / ロケット / ロックオンミサイル / 魚雷", "Gun / Rockets / Lock-on missile / Torpedo", "机枪 / 火箭 / 锁定导弹 / 鱼雷")),
    (("X",) * 3, ("フレア", "Flares", "干扰弹")),
    (("B",) * 3, ("ブレーキ", "Brake", "刹车")),
    (("F",) * 3, ("フラップ", "Flaps", "襟翼")),
    (("G",) * 3, ("脚の出し入れ", "Landing gear", "收放起落架")),
    (("T",) * 3, ("ブースト（自動で上昇→加速）", "Boost (climb, then accelerate)", "加速推进（先自动爬升再加速）")),
    (("H",) * 3, ("計器だけ表示", "Gauges-only view", "仅显示仪表")),
    (("M",) * 3, ("R35 の音声 ON / OFF", "R35 voice on / off", "R35 语音 开 / 关")),
    (("Enter",) * 3, ("機体から降りる", "Exit the aircraft", "离开机体")),
]
PC_NOTE = (
    ("カタパルト: 左Shift でスロットルを 100% にすると発艦。", "乗る: 潜水艦の格納庫の R35 の席に座る。"),
    ("Catapult: hold Left Shift to 100% throttle to launch.", "Board: sit in an R35 seat in the hangar."),
    ("潜艇弹射器：按住左Shift把油门推到100%即可起飞。", "登机：坐到潜艇机库里R35的座位上。"),
)
VR_ROWS = [
    (("右グリップ", "Right grip", "右握把"), ("握る＝操縦桿。握った時の手の向きが中立", "Hold = joystick. Hand angle at grip = neutral", "握住＝操纵杆。握住时的手部角度为中立")),
    (("右手を傾ける", "Tilt right hand", "倾斜右手"), ("前後＝機首の上げ下げ　左右＝横転", "Forward / back = pitch, sideways = roll", "前后＝俯仰　左右＝横滚")),
    (("右手をひねる", "Twist right hand", "扭转右手"), ("機首を左右に向ける（手の位置は関係なし）", "Yaw (only rotation counts, not position)", "偏航（只看旋转，与位置无关）")),
    (("左グリップ", "Left grip", "左握把"), ("握ったまま手を前へ＝スロットル上げ　手前へ＝下げ", "Hold and move hand forward / back = throttle", "握住向前＝加油门　向后＝减油门")),
    (("右トリガー", "Right trigger", "右扳机"), ("撃つ", "Fire", "开火")),
    (("左トリガー", "Left trigger", "左扳机"), ("短く＝武器の切り替え　1秒＝R35 の音声 ON / OFF", "Tap = switch weapon, hold 1 s = R35 voice on / off", "短按＝切换武器　按住1秒＝R35 语音 开 / 关")),
    (("左スティック ↓", "Left stick ↓", "左摇杆 ↓"), ("ブレーキ", "Brake", "刹车")),
    (("左スティック ↑", "Left stick ↑", "左摇杆 ↑"), ("短く＝フラップ　1秒＝ブースト", "Tap = flaps, hold 1 s = boost", "短按＝襟翼　按住1秒＝加速推进")),
    (("左スティック ←", "Left stick ←", "左摇杆 ←"), ("短く＝脚の出し入れ　1秒＝計器だけ表示", "Tap = landing gear, hold 1 s = gauges-only view", "短按＝收放起落架　按住1秒＝仅显示仪表")),
    (("ジャンプ", "Jump", "跳跃键"), ("機体から降りる（飛行中に押さないこと）", "Exit the aircraft (not while flying!)", "离开机体（飞行中不要按）")),
]
VR_NOTE = (
    ("コツ: 右コントローラーを太ももの上で操縦桿のように持つ。", "カタパルト: スロットルを 100% にすると発艦。"),
    ("Tip: rest the right controller on your leg like a joystick.", "Catapult: raise the throttle to 100% to launch."),
    ("提示：把右手柄放在大腿上，当作操纵杆来握。", "潜艇弹射器：把油门推到100%即可起飞。"),
)
LI = {"ja": 0, "en": 1, "zh": 2}


def controls(kind, lang):
    rows = PC_ROWS if kind == "pc" else VR_ROWS
    note = (PC_NOTE if kind == "pc" else VR_NOTE)[LI[lang]]
    W, H = 1536, 1920
    im = Image.new("RGB", (W, H), BG)
    d = ImageDraw.Draw(im)
    d.rectangle([18, 18, W - 19, H - 19], outline=ORANGE, width=6)
    d.text((70, 70), TITLE[(kind, lang)], font=font(lang, 100, True), fill=ORANGE)
    d.line([70, 245, W - 70, 245], fill=ORANGE, width=4)
    top, bottom = 288, 1655
    pitch = (bottom - top) / len(rows)
    bh = pitch - 24
    for i, (keys, descs) in enumerate(rows):
        y = top + i * pitch
        d.rounded_rectangle([70, y, 480, y + bh], radius=16, fill=KEYBOX)
        kt = keys[LI[lang]]
        kf = font(lang, 46, True)
        if d.textlength(kt, font=kf) > 370:
            kf = font(lang, 36, True)
        d.text((275, y + bh / 2), kt, font=kf, fill=KEYTXT, anchor="mm")
        desc = descs[LI[lang]]
        df = font(lang, 50, True)
        while d.textlength(desc, font=df) > W - 70 - 510 and df.size > 30:
            df = font(lang, df.size - 2, True)
        d.text((510, y + bh / 2), desc, font=df, fill=DESC, anchor="lm")
        d.line([70, y + pitch - 12, W - 70, y + pitch - 12], fill=SEP, width=2)
    nf = font(lang, 46)
    for j, line in enumerate(note):
        d.text((70, 1700 + j * 62), line, font=nf, fill=NOTE)
    return im


# ---------------- 島の説明・看板 ----------------
INFO = {
    "Aigis_island": (
        ("アイギス島", "Aigis Island", "艾吉斯岛"),
        ("博物館がある島。この世界の歴史を展示していて、飛行機を降りて中を見学できる。",
         "An island with a museum that shows the history of this world. Get out of your aircraft and look around inside.",
         "设有博物馆的岛屿。馆内展示着这个世界的历史，可以离开飞机进去参观。"), 474),
    "Astral_island": (
        ("アストラル島", "Astral Island", "阿斯特拉岛"),
        ("敵の領地だが、空港に着陸すると補給（燃料・弾薬の補充と修理）ができる。空港のまわりでは攻撃されない。",
         "Enemy territory, but you can land at the airport to resupply (fuel, ammo and repairs). You will not be attacked near the airport.",
         "虽然是敌方领地，但降落在机场就能补给（补充燃料、弹药并维修）。机场周围不会遭到攻击。"), 576),
    "Military_base": (
        ("軍事基地", "Military Base", "军事基地"),
        ("敵の軍事基地がある島。近づくと警告の無線が入り、離れないと攻撃してくる。基地の建物をすべて壊すと大きな得点になる。",
         "An island with an enemy military base. Come close and you will be warned by radio; if you do not leave, they will attack. Destroying every base building gives a big score.",
         "有敌方军事基地的岛屿。靠近时会收到警告无线电，不离开就会遭到攻击。摧毁基地的所有建筑可获得高分。"), 576),
}


def info(key, lang):
    title, body, H = INFO[key][0][LI[lang]], INFO[key][1][LI[lang]], INFO[key][2]
    W = 1000
    im = Image.new("RGB", (W, H), (10, 18, 30))
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, W - 1, H - 1], outline=(90, 240, 255), width=8)
    d.text((50, 50), title, font=font(lang, 64, True), fill=(255, 230, 170))
    d.line([50, 175, 950, 175], fill=(90, 240, 255), width=3)
    size = 40
    while True:
        bf = font(lang, size)
        lines = wrap(d, body, bf, 900)
        if len(lines) * (size * 1.45) <= H - 215 - 20 or size <= 26:
            break
        size -= 2
    for i, ln in enumerate(lines):
        d.text((50, 205 + i * size * 1.45), ln, font=bf, fill=(225, 234, 242))
    return im


def sign(lang):
    t = {"ja": "島の名前にさわると、くわしい説明が見られます",
         "en": "Touch an island name to see more about it.",
         "zh": "触摸岛屿名称，即可查看详细说明。"}[lang]
    W, H = 1200, 330
    im = Image.new("RGB", (W, H), (44, 30, 18))
    d = ImageDraw.Draw(im)
    d.rectangle([8, 8, W - 9, H - 9], outline=(190, 150, 70), width=6)
    size = 72
    f = font(lang, size, True)
    while d.textlength(t, font=f) > W - 120 and size > 30:
        size -= 2; f = font(lang, size, True)
    d.text((W / 2, H / 2), t, font=f, fill=(255, 225, 150), anchor="mm")
    return im


def save(im, rel):
    p = os.path.join(ROOT, rel)
    im.save(p)
    print("wrote", os.path.normpath(p))


for lang in ("ja", "en", "zh"):
    save(controls("pc", lang), "Tex/controls_pc_%s.png" % lang)
    save(controls("vr", lang), "Tex/controls_vr_%s.png" % lang)
    save(sign(lang), "IslandInfo/chart_sign_%s.png" % lang)
    for k in INFO:
        save(info(k, lang), "IslandInfo/info_%s_%s.png" % (k, lang))

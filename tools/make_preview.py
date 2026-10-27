# -*- coding: utf-8 -*-
"""生成 mod 预览图：从 TreasureCatalog 自动读取法宝，最多 4 列 × 3 行。

用法（在工程文件夹「法宝之力」下）：
    python tools/make_preview.py

输出：法宝之力/preview.png（512×512）。法宝超过 12 件时只展示前 12 件，
并在底部注明总数；新增法宝不需要改这个脚本。
素材本身是 448×448 的透明底图，直接叠加到深色底上。
"""

import re
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

# 与占位图一致的深蓝金配色
BG_TOP = (30, 48, 80)
BG_BOTTOM = (9, 15, 28)
GOLD = (214, 179, 104)
GOLD_SOFT = (232, 204, 142)
TITLE_GOLD = (241, 216, 156)
WHITE_WARM = (246, 234, 206)
SUB_COLOR = (150, 172, 208)
FOOT_COLOR = (156, 172, 196)

ROOT = Path(__file__).resolve().parent.parent
RES = ROOT / "resources"
OUT = ROOT / "preview.png"
CATALOG = ROOT / "src" / "Treasures" / "TreasureCatalog.cs"

# 单张预览最多展示多少件（4 列 × 3 行）
MAX_ICONS = 12


def load_treasures():
    """从 TreasureCatalog.cs 读 DisplayName / ResourceName，顺序即注册顺序。

    这样以后加法宝只改代码，不用再维护预览脚本里的名单。
    """
    text = CATALOG.read_text(encoding="utf-8")
    pairs = re.findall(
        r'DisplayName\s*=\s*"([^"]+)"[\s\S]*?ResourceName\s*=\s*"([^"]+)"',
        text,
    )
    if not pairs:
        raise SystemExit(f"没能从 {CATALOG} 解析出法宝列表")
    return pairs


TREASURES = load_treasures()

S = 2                      # 超采样倍数，先画 1024 再缩小，边缘更干净
W = H = 512 * S

FONT_BOLD = Path(r"C:\Windows\Fonts\msyhbd.ttc")
FONT_REG = Path(r"C:\Windows\Fonts\msyh.ttc")


def font(size: int, bold: bool = True) -> ImageFont.FreeTypeFont:
    path = FONT_BOLD if bold else FONT_REG
    try:
        return ImageFont.truetype(str(path), size)
    except OSError:
        # 找不到雅黑时退回黑体
        return ImageFont.truetype(r"C:\Windows\Fonts\simhei.ttf", size)


def background() -> Image.Image:
    """竖向渐变 + 中心柔光，营造与占位图一致的深蓝氛围。"""
    img = Image.new("RGBA", (W, H))
    draw = ImageDraw.Draw(img)
    for y in range(H):
        t = y / (H - 1)
        # 顶部亮、底部暗，中间稍微提亮一点
        color = tuple(int(a + (b - a) * t) for a, b in zip(BG_TOP, BG_BOTTOM))
        draw.line([(0, y), (W, y)], fill=color + (255,))

    # 标题区域的径向柔光（按椭圆蒙版混合，越靠近中心越亮）
    glow = Image.new("L", (W, H), 0)
    gd = ImageDraw.Draw(glow)
    gd.ellipse([W // 2 - 430, -260, W // 2 + 430, 470], fill=255)
    glow = glow.filter(ImageFilter.GaussianBlur(160))

    light = Image.new("RGBA", (W, H), (120, 150, 210, 255))
    light.putalpha(glow.point(lambda v: int(v * 0.22)))
    return Image.alpha_composite(img, light)


def diamond(draw: ImageDraw.ImageDraw, cx: int, cy: int, r: int, color) -> None:
    draw.polygon([(cx, cy - r), (cx + r, cy), (cx, cy + r), (cx - r, cy)], fill=color)


def overlay_shape(img: Image.Image, painter) -> Image.Image:
    """在透明图层上画图形再整体混合。

    ImageDraw 直接往 RGBA 上画带 alpha 的颜色是「替换」而不是「混合」，
    半透明底板会变成白色实心块，所以必须走独立图层。
    """
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    painter(ImageDraw.Draw(layer))
    return Image.alpha_composite(img, layer)


def main() -> None:
    img = background()
    draw = ImageDraw.Draw(img)

    # 外框
    draw.rounded_rectangle(
        [20, 20, W - 20, H - 20],
        radius=28,
        outline=GOLD + (70,),
        width=2 * S,
    )

    # 顶部标题
    title_font = font(52 * S)
    draw.text(
        (W // 2, 34),
        "法宝之力",
        font=title_font,
        fill=TITLE_GOLD + (255,),
        anchor="ma",
    )
    sub_font = font(17 * S, bold=False)
    draw.text(
        (W // 2, 158),
        "M A G I C   T R E A S U R E   P O W E R",
        font=sub_font,
        fill=SUB_COLOR + (255,),
        anchor="ma",
    )

    # 标题下的分割线与菱形装饰
    line_y = 206
    half = 150 * S // 2
    draw.line([(W // 2 - half, line_y), (W // 2 + half, line_y)], fill=GOLD + (140,), width=2)
    diamond(draw, W // 2, line_y, 7 * S, GOLD + (220,))

    # 网格：最多 4 列、3 行；行数由法宝数量决定
    margin = 48
    gap = 20
    total = len(TREASURES)
    shown = TREASURES[:MAX_ICONS]
    cols = 4 if total > 4 else max(1, total)
    rows = (len(shown) + cols - 1) // cols

    cell_w = (W - margin * 2 - gap * (cols - 1)) // cols
    grid_top = 222
    grid_bottom = H - 96

    # 行数越多，格子 / 图标 / 字号越小，保证全部落在标题与底部参数行之间
    if rows <= 1:
        cell_h, icon_size, name_pt = 300, 200, 20
    elif rows == 2:
        cell_h, icon_size, name_pt = 268, 180, 18
    else:
        cell_h, icon_size, name_pt = (grid_bottom - grid_top - gap * (rows - 1)) // rows, 150, 16

    name_font = font(name_pt * S)

    for index, (name, resource) in enumerate(shown):
        row, col = divmod(index, cols)

        # 最后一行不满时居中，避免右下角空出一格
        row_start = row * cols
        row_count = min(cols, len(shown) - row_start)
        offset = (cols - row_count) * (cell_w + gap) / 2

        x = int(margin + offset + col * (cell_w + gap))
        y = grid_top + row * (cell_h + gap)

        # 单元格底板：半透明提亮 + 金色描边（必须在独立图层上混合）
        img = overlay_shape(
            img,
            lambda d, x=x, y=y: (
                d.rounded_rectangle(
                    [x, y, x + cell_w, y + cell_h],
                    radius=20,
                    fill=(120, 145, 190, 26),
                    outline=GOLD + (120,),
                    width=2,
                )
            ),
        )
        draw = ImageDraw.Draw(img)

        icon = Image.open(RES / resource).convert("RGBA")
        icon = icon.resize((icon_size, icon_size), Image.LANCZOS)

        # 暖色辉光：把图标不透明区域染成金色再模糊
        glow_layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        glow_icon = Image.new("RGBA", icon.size, (255, 190, 90, 110))
        glow_icon.putalpha(icon.getchannel("A").point(lambda v: int(v * 0.55)))
        ix = x + (cell_w - icon_size) // 2
        iy = y + 16
        glow_layer.alpha_composite(glow_icon, (ix, iy))
        glow_layer = glow_layer.filter(ImageFilter.GaussianBlur(14 * S // 2))
        img = Image.alpha_composite(img, glow_layer)
        draw = ImageDraw.Draw(img)

        img.alpha_composite(icon, (ix, iy))
        draw = ImageDraw.Draw(img)

        # 名称
        draw.text(
            (x + cell_w // 2, iy + icon_size + 14),
            name,
            font=name_font,
            fill=WHITE_WARM + (255,),
            anchor="ma",
        )

    # 底部参数行（与物品实际数值保持一致）
    foot_font = font(20 * S, bold=False)
    foot = f"{total} 件法宝 · 品质 7 · 价值 999,999 · 重量 0.2"
    if total > MAX_ICONS:
        foot += f"（预览展示前 {MAX_ICONS} 件）"
    draw.text(
        (W // 2, H - 76),
        foot,
        font=foot_font,
        fill=FOOT_COLOR + (255,),
        anchor="ma",
    )

    # 四角小菱形，呼应占位图的装饰
    for cx, cy in ((64, 64), (W - 64, 64), (64, H - 64), (W - 64, H - 64)):
        diamond(draw, cx, cy, 8, GOLD + (150,))

    img = img.convert("RGB").resize((512, 512), Image.LANCZOS)
    img.save(OUT, "PNG", optimize=True)
    print(f"已生成 {OUT}（512×512）")


if __name__ == "__main__":
    main()

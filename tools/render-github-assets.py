"""Render docs/assets/voice-input-product.png from real UI preview captures."""
from __future__ import annotations

from pathlib import Path
from random import Random

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "docs" / "assets"
OUT.mkdir(parents=True, exist_ok=True)

INK = "#101735"
INDIGO = "#4758D7"
INDIGO_DARK = "#2F3EB6"
SIGNAL = "#CFFAF4"
WHITE = "#F8FAFF"
MUTED = "#AAB5D3"
GRID = "#39436C"
PAPER = "#F3F5FA"
PAPER_INK = "#1C2440"
PAPER_MUTED = "#66708B"
PAPER_LINE = "#D8DDEA"
FONT_DIR = Path("C:/Windows/Fonts")
SANS = FONT_DIR / "bahnschrift.ttf"


def font(size: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(str(SANS), size=size)


def tracking(draw: ImageDraw.ImageDraw, xy: tuple[int, int], text: str, face: ImageFont.FreeTypeFont, fill: str, spacing: int) -> None:
    x, y = xy
    for char in text:
        draw.text((x, y), char, font=face, fill=fill)
        x += int(draw.textlength(char, font=face)) + spacing


def tracking_width(draw: ImageDraw.ImageDraw, text: str, face: ImageFont.FreeTypeFont, spacing: int) -> int:
    if not text:
        return 0
    return sum(int(draw.textlength(char, font=face)) for char in text) + spacing * (len(text) - 1)


def noise(size: tuple[int, int], opacity: int = 10) -> Image.Image:
    rng = Random(5101)
    layer = Image.new("RGBA", size, (0, 0, 0, 0))
    px = layer.load()
    for y in range(size[1]):
        for x in range(size[0]):
            n = rng.randrange(opacity + 1)
            px[x, y] = (207, 250, 244, n)
    return layer.filter(ImageFilter.GaussianBlur(0.35))


def grid(draw: ImageDraw.ImageDraw, size: tuple[int, int], margin: int) -> None:
    for x in range(margin, size[0] - margin + 1, 64):
        draw.line((x, margin, x, size[1] - margin), fill=GRID, width=1)
    for y in range(margin, size[1] - margin + 1, 64):
        draw.line((margin, y, size[0] - margin, y), fill=GRID, width=1)


def paste_icon(im: Image.Image, box: tuple[int, int, int, int]) -> None:
    icon = Image.open(ROOT / "assets" / "VoiceInput.png").convert("RGBA")
    target_w = box[2] - box[0]
    target_h = box[3] - box[1]
    icon.thumbnail((target_w, target_h), Image.Resampling.LANCZOS)
    x = box[0] + (target_w - icon.width) // 2
    y = box[1] + (target_h - icon.height) // 2
    shadow = Image.new("RGBA", im.size, (0, 0, 0, 0))
    mask = icon.getchannel("A").filter(ImageFilter.GaussianBlur(20))
    shade = Image.new("RGBA", icon.size, (3, 8, 30, 150))
    shadow.paste(shade, (x + 10, y + 18), mask)
    im.alpha_composite(shadow)
    im.alpha_composite(icon, (x, y))


WAVE_COLOURS = ["#A99BFF", "#D698D8", "#FF8FB1", "#FFC56D"]


def waveform(draw: ImageDraw.ImageDraw, origin: tuple[int, int], heights: list[int], width: int = 18, gap: int = 15) -> None:
    """Волна со значка: от фиолетового к янтарному, в конце ровный белый курсор."""
    x0, cy = origin
    x = x0
    for index, h in enumerate(heights):
        colour = WAVE_COLOURS[min(index, len(WAVE_COLOURS) - 1)]
        draw.rounded_rectangle((x, cy - h // 2, x + width, cy + h // 2), radius=width // 2, fill=colour)
        x += width + gap
    draw.rounded_rectangle((x + 18, cy - 56, x + 18 + width, cy + 56), radius=width // 2, fill=WHITE)


def base(size: tuple[int, int], margin: int) -> Image.Image:
    im = Image.new("RGBA", size, INK)
    draw = ImageDraw.Draw(im)
    grid(draw, size, margin)
    draw.rectangle((0, 0, 12, size[1]), fill=INDIGO)
    draw.rectangle((12, 0, 16, size[1]), fill=SIGNAL)
    return im


BACKDROP = (31, 33, 44)


def overlay_tile(path: Path, size: tuple[int, int]) -> Image.Image:
    """Снимок оверлея на плитке одного размера: окно оверлея прозрачное, высота зависит от состояния."""
    shot = Image.open(path).convert("RGB")
    tile = Image.new("RGB", (size[0] * 2, size[1] * 2), BACKDROP)
    tile.paste(shot, ((tile.width - shot.width) // 2, (tile.height - shot.height) // 2))
    return tile.resize(size, Image.Resampling.LANCZOS)


def rounded(image: Image.Image, radius: int) -> Image.Image:
    mask = Image.new("L", image.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, image.width - 1, image.height - 1), radius=radius, fill=255)
    result = image.convert("RGBA")
    result.putalpha(mask)
    return result


def render_product_demo() -> None:
    """Настоящий интерфейс 1.1: слева шесть состояний оверлея, справа окно настроек."""
    size = (1440, 800)
    im = Image.new("RGB", size, PAPER)
    draw = ImageDraw.Draw(im)

    draw.rectangle((0, 0, size[0], 172), fill=INK)
    draw.rectangle((0, 0, 12, size[1]), fill=INDIGO)
    draw.rectangle((12, 0, 16, size[1]), fill=SIGNAL)
    tracking(draw, (74, 38), "VOICE INPUT 1.1 / WINDOWS 10 И 11", font(17), MUTED, 2)
    draw.text((70, 76), "Тихий оверлей и одно небольшое окно настроек.", font=font(46), fill=WHITE)

    product = OUT / "product"
    # Снимки сделаны в 200 %: плитка ровно вдвое меньше, без повторной интерполяции текста.
    tile_w, tile_h = 416, 125
    columns = (70, 510)
    rows = (214, 387, 560)
    cells = [
        ("01 / СЛУШАЮ", "listening.png"),
        ("02 / РАСПОЗНАЮ", "processing.png"),
        ("03 / ВСТАВЛЯЮ ТЕКСТ", "inserting.png"),
        ("04 / ГОТОВО", "success.png"),
        ("05 / НЕ УСЛЫШАЛ", "nospeech.png"),
        ("06 / ОШИБКА ПРОСТЫМИ СЛОВАМИ", "error.png"),
    ]
    im = im.convert("RGBA")
    draw = ImageDraw.Draw(im)
    for index, (label, filename) in enumerate(cells):
        x, y = columns[index % 2], rows[index // 2]
        tracking(draw, (x, y), label, font(15), PAPER_MUTED, 2)
        im.alpha_composite(rounded(overlay_tile(product / filename, (tile_w, tile_h)), 12), (x, y + 30))

    settings = Image.open(product / "settings.png").convert("RGB")
    settings_h = 471
    settings_w = round(settings.width * settings_h / settings.height)
    settings = settings.resize((settings_w, settings_h), Image.Resampling.LANCZOS)
    sx, sy = 1370 - settings_w, 214
    tracking(draw, (sx, sy), "07 / НАСТРОЙКИ", font(15), PAPER_MUTED, 2)
    shadow = Image.new("RGBA", size, (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle((sx - 6, sy + 36, sx + settings_w + 6, sy + 40 + settings_h), radius=18, fill=(18, 28, 63, 46))
    im = Image.alpha_composite(im, shadow.filter(ImageFilter.GaussianBlur(14)))
    im.alpha_composite(rounded(settings, 10), (sx, sy + 30))
    draw = ImageDraw.Draw(im)

    draw.line((70, 742, 1370, 742), fill=PAPER_LINE, width=2)
    tracking(draw, (70, 760), "ЗАПИСЬ / РАСПОЗНАВАНИЕ / ВСТАВКА", font(16), PAPER_INK, 2)
    footnote = "СНЯТО С НАСТОЯЩИХ ОКОН"
    footnote_x = 1370 - tracking_width(draw, footnote, font(16), 2)
    tracking(draw, (footnote_x, 760), footnote, font(16), PAPER_MUTED, 2)
    im.convert("RGB").save(OUT / "voice-input-product.png", optimize=True, quality=94)


if __name__ == "__main__":
    render_product_demo()
    print("Изображения Voice Input для GitHub отрисованы")

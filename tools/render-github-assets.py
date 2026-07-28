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


def waveform(draw: ImageDraw.ImageDraw, origin: tuple[int, int], heights: list[int], width: int = 18, gap: int = 15) -> None:
    x0, cy = origin
    x = x0
    for h in heights:
        draw.rounded_rectangle((x, cy - h // 2, x + width, cy + h // 2), radius=width // 2, fill=SIGNAL)
        x += width + gap
    draw.rounded_rectangle((x + 18, cy - 56, x + 18 + width, cy + 56), radius=width // 2, fill=WHITE)


def base(size: tuple[int, int], margin: int) -> Image.Image:
    im = Image.new("RGBA", size, INK)
    draw = ImageDraw.Draw(im)
    grid(draw, size, margin)
    draw.rectangle((0, 0, 12, size[1]), fill=INDIGO)
    draw.rectangle((12, 0, 16, size[1]), fill=SIGNAL)
    return im


def render_banner() -> None:
    size = (1600, 520)
    im = base(size, 56)
    draw = ImageDraw.Draw(im)
    tracking(draw, (84, 54), "VOICE INPUT / WINDOWS", font(19), MUTED, 2)
    draw.text((80, 126), "Voice Input", font=font(72), fill=WHITE)
    draw.text((84, 222), "LOCAL DICTATION. NO CLOUD REQUIRED.", font=font(25), fill=SIGNAL)
    draw.text((84, 280), "Hold a hotkey, speak, and keep typing.", font=font(23), fill=MUTED)
    waveform(draw, (90, 394), [34, 74, 126, 84])
    paste_icon(im, (1080, 70, 1480, 470))
    tracking(draw, (1054, 462), "RECORD / TRANSCRIBE / INSERT", font(16), MUTED, 2)
    im = Image.alpha_composite(im, noise(size)).convert("RGB")
    im.save(OUT / "voice-input-banner.png", optimize=True, quality=94)


def render_social() -> None:
    size = (1280, 640)
    im = base(size, 54)
    draw = ImageDraw.Draw(im)
    tracking(draw, (84, 60), "LOCAL-FIRST / WINDOWS", font(18), MUTED, 2)
    draw.text((80, 146), "Voice", font=font(82), fill=WHITE)
    draw.text((80, 230), "Input", font=font(82), fill=WHITE)
    draw.text((84, 348), "Russian speech to the field", font=font(27), fill=SIGNAL)
    draw.text((84, 386), "where recording started.", font=font(27), fill=SIGNAL)
    waveform(draw, (88, 520), [28, 62, 104, 72])
    paste_icon(im, (790, 94, 1160, 464))
    tracking(draw, (790, 492), "OFFLINE / PRIVATE / INSTALLABLE", font(16), MUTED, 1)
    im = Image.alpha_composite(im, noise(size)).convert("RGB")
    im.save(OUT / "voice-input-social-preview.png", optimize=True, quality=94)


def render_product_demo() -> None:
    size = (1440, 620)
    im = Image.new("RGB", size, PAPER)
    draw = ImageDraw.Draw(im)

    draw.rectangle((0, 0, size[0], 172), fill=INK)
    draw.rectangle((0, 0, 12, size[1]), fill=INDIGO)
    draw.rectangle((12, 0, 16, size[1]), fill=SIGNAL)
    tracking(draw, (74, 38), "ACTUAL INSTALLED APP / WINDOWS 11", font(17), MUTED, 2)
    draw.text((70, 76), "Two states. One quiet overlay.", font=font(46), fill=WHITE)

    items = [
        (94, "01 / LISTENING", "installed-listening.png"),
        (782, "02 / TRANSCRIBING", "installed-processing.png"),
    ]
    for x, label, filename in items:
        tracking(draw, (x, 212), label, font(18), PAPER_MUTED, 2)
        screenshot = Image.open(OUT / "product" / filename).convert("RGB").resize((544, 224), Image.Resampling.LANCZOS)
        shadow = Image.new("RGBA", size, (0, 0, 0, 0))
        shadow_draw = ImageDraw.Draw(shadow)
        shadow_draw.rounded_rectangle((x - 12, 266, x + 556, 514), radius=20, fill=(18, 28, 63, 30))
        shadow = shadow.filter(ImageFilter.GaussianBlur(16))
        im = Image.alpha_composite(im.convert("RGBA"), shadow).convert("RGB")
        draw = ImageDraw.Draw(im)
        draw.rounded_rectangle((x - 1, 253, x + 545, 479), radius=16, fill="#FFFFFF", outline=PAPER_LINE, width=1)
        im.paste(screenshot, (x, 254))

    draw.line((70, 546, 1370, 546), fill=PAPER_LINE, width=2)
    tracking(draw, (70, 566), "RECORD / TRANSCRIBE / INSERT", font(16), PAPER_INK, 2)
    tracking(draw, (1054, 566), "LOCAL / PRIVATE", font(16), PAPER_MUTED, 2)
    im.save(OUT / "voice-input-product.png", optimize=True, quality=94)


if __name__ == "__main__":
    render_banner()
    render_social()
    render_product_demo()
    print("Rendered Voice Input GitHub assets")

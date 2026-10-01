"""Draws the Voice Input icon (assets/VoiceInput.ico and assets/VoiceInput.png).

The mark is a luminous waveform that resolves into a steady text cursor, on a deep indigo field.
Large sizes carry a glow and a tonal sweep; sizes up to 32 px drop the glow and use fewer, thicker
bars so the mark still reads in the tray. Run: python tools/render-icon.py
"""
from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "assets"
SIZES = [16, 20, 24, 32, 48, 64, 128, 256]
SS = 4  # supersampling for clean edges

FIELD_TOP_LEFT = (46, 40, 108)
FIELD_BOTTOM_RIGHT = (11, 12, 28)
VIOLET = (169, 155, 255)
ROSE = (255, 143, 177)
AMBER = (255, 197, 109)


def lerp(a: float, b: float, t: float) -> float:
    return a + (b - a) * t


def mix(c1: tuple[int, int, int], c2: tuple[int, int, int], t: float) -> tuple[int, int, int]:
    return tuple(int(round(lerp(c1[i], c2[i], t))) for i in range(3))  # type: ignore[return-value]


def sweep(t: float) -> tuple[int, int, int]:
    """Violet -> rose -> amber along the waveform."""
    return mix(VIOLET, ROSE, t / 0.5) if t < 0.5 else mix(ROSE, AMBER, (t - 0.5) / 0.5)


def squircle_mask(size: int, margin: float, exponent: float = 5.0) -> Image.Image:
    """A superellipse: rounder than a rounded rectangle, closer to the Windows 11 app tile."""
    mask = Image.new("L", (size, size), 0)
    px = mask.load()
    half = (size - 2 * margin) / 2
    centre = size / 2
    for y in range(size):
        dy = abs(y + 0.5 - centre) / half
        for x in range(size):
            dx = abs(x + 0.5 - centre) / half
            if dx**exponent + dy**exponent <= 1:
                px[x, y] = 255
    return mask


def field(size: int) -> Image.Image:
    base = Image.new("RGB", (size, size))
    px = base.load()
    for y in range(size):
        for x in range(size):
            t = (x + y) / (2 * (size - 1))
            px[x, y] = mix(FIELD_TOP_LEFT, FIELD_BOTTOM_RIGHT, t**0.85)
    return base


def bar_layout(small: bool) -> list[tuple[float, float, float]]:
    """(centre-x, height, sweep position) for each bar, as fractions of the tile."""
    if small:
        return [(0.27, 0.30, 0.0), (0.42, 0.56, 0.35), (0.57, 0.78, 0.7), (0.74, 0.62, 1.0)]
    return [
        (0.255, 0.28, 0.0),
        (0.375, 0.50, 0.22),
        (0.495, 0.76, 0.5),
        (0.615, 0.46, 0.78),
    ]


def render(size: int) -> Image.Image:
    small = size <= 32
    big = size * SS
    margin = big * 0.035
    tile_mask = squircle_mask(big, margin)

    tile = field(big).convert("RGBA")

    # A soft highlight at the top edge gives the tile a little depth without gloss.
    highlight = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    hd = ImageDraw.Draw(highlight)
    hd.ellipse((-big * 0.2, -big * 0.75, big * 1.2, big * 0.35), fill=(190, 180, 255, 34))
    tile = Image.alpha_composite(tile, highlight)

    mark = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    md = ImageDraw.Draw(mark)
    glow = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)

    bar_width = big * (0.105 if small else 0.082)
    centre_y = big * 0.5
    for cx, height, position in bar_layout(small):
        colour = sweep(position)
        half_h = big * height / 2
        box = (cx * big - bar_width / 2, centre_y - half_h, cx * big + bar_width / 2, centre_y + half_h)
        md.rounded_rectangle(box, radius=bar_width / 2, fill=colour + (255,))
        gd.rounded_rectangle(box, radius=bar_width / 2, fill=colour + (255,))

    # The cursor: taller, steadier, almost white, with a faint warm core.
    cursor_x = big * (0.77 if small else 0.74)
    cursor_half = big * (0.34 if small else 0.33)
    cursor_width = bar_width * (1.0 if small else 0.86)
    cursor_box = (cursor_x - cursor_width / 2, centre_y - cursor_half, cursor_x + cursor_width / 2, centre_y + cursor_half)
    if small:
        cursor_box = (big * 0.80 - cursor_width / 2, centre_y - cursor_half, big * 0.80 + cursor_width / 2, centre_y + cursor_half)
    md.rounded_rectangle(cursor_box, radius=cursor_width / 2, fill=(250, 248, 255, 255))
    gd.rounded_rectangle(cursor_box, radius=cursor_width / 2, fill=(255, 235, 215, 255))

    if not small:
        halo = glow.filter(ImageFilter.GaussianBlur(big * 0.035))
        halo.putalpha(halo.getchannel("A").point(lambda a: int(a * 0.62)))
        wide = glow.filter(ImageFilter.GaussianBlur(big * 0.09))
        wide.putalpha(wide.getchannel("A").point(lambda a: int(a * 0.32)))
        tile = Image.alpha_composite(tile, wide)
        tile = Image.alpha_composite(tile, halo)
    tile = Image.alpha_composite(tile, mark)

    # Hairline rim keeps the tile edge readable on both light and dark taskbars.
    rim = ImageChops.subtract(tile_mask, tile_mask.filter(ImageFilter.MinFilter(max(3, int(big * 0.012)) | 1)))
    rim_layer = Image.new("RGBA", (big, big), (255, 255, 255, 0))
    rim_layer.putalpha(rim.point(lambda a: int(a * 0.20)))
    tile = Image.alpha_composite(tile, rim_layer)

    tile.putalpha(tile_mask)
    return tile.resize((size, size), Image.Resampling.LANCZOS)


def main() -> None:
    ASSETS.mkdir(exist_ok=True)
    images = {size: render(size) for size in SIZES}
    large = render(1024)
    large.save(ASSETS / "VoiceInput.png", optimize=True)
    images[256].save(
        ASSETS / "VoiceInput.ico",
        format="ICO",
        sizes=[(size, size) for size in SIZES],
        append_images=[images[size] for size in SIZES if size != 256],
    )
    # A contact sheet for visual review.
    sheet = Image.new("RGBA", (256 * 3 + 40, 256 + 90), (30, 31, 40, 255))
    sheet.alpha_composite(images[256], (10, 10))
    x = 276
    for size in (128, 64, 48):
        sheet.alpha_composite(images[size], (x, 10 + (128 - size) // 2 if size < 128 else 10))
        x += size + 20
    x = 276
    for size in (32, 24, 20, 16):
        sheet.alpha_composite(images[size], (x, 160))
        sheet.alpha_composite(images[size].resize((size * 3, size * 3), Image.Resampling.NEAREST), (x, 200) if size < 32 else (x, 200))
        x += size * 3 + 16
    sheet.save(ROOT / "_recovered" / "icon-sheet.png")
    print("Rendered", ", ".join(map(str, SIZES)))


if __name__ == "__main__":
    main()

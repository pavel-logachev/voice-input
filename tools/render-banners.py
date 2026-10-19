"""Render docs/assets/voice-input-banner.png, voice-input-banner-mobile.png and voice-input-social-preview.png.

The banners share one layout with the other pictures of the GitHub account, so the layout lives in the
generator of the profile repository (github.com/pavel-logachev/pavel-logachev, design/generator/build.py).
This script runs that generator on a temporary copy and changes one thing: the settings window comes from
this repository's own capture, docs/assets/product/settings.png (UiPreview, 200 %). The window of 1.2 is
taller than the frame the banners give it, so the picture shows its top — the recognition engine card and
the «Распознавание» block — and the bottom of the window fades out, as in voice-input-product.png.
The overlay pictures, texts, fonts and positions stay those of the generator.

    python tools/render-banners.py --generator <pavel-logachev>/design/generator

Needs what the generator needs: Python 3.10+ with Pillow, PyYAML and NumPy, and the Playwright CLI
(--playwright or PLAYWRIGHT_CLI; the generator's default is the DeepSeek Harness wrapper).
"""
from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "docs" / "assets"
SETTINGS = ASSETS / "product" / "settings.png"
BANNERS = ("voice-input-banner.png", "voice-input-banner-mobile.png", "voice-input-social-preview.png")

# The generator's call for the settings window: left, top and width in the 600 × 420 picture zone.
WINDOW_CALL = 'window("voice-settings.png", 362, 12, 238, 1)'
LEFT, TOP, WIDTH = 362, 12, 238
# The 1.1 window filled 238 × 313 of the zone (928 × 1220 px). The 1.2 picture keeps that frame.
FRAME_RATIO = 1220 / 928
# In the 200 % capture the «Распознавание» block ends at 1014 px; below it the window fades out.
FADE_FROM_PX = 1030
# Room for the window's own shadow (0 30px 60px) inside the fading layer.
PAD = 100


def settings_source(target: Path) -> tuple[int, int]:
    """The top of the real window, in the 1.1 window's proportions."""
    shot = Image.open(SETTINGS).convert("RGB")
    height = round(shot.width * FRAME_RATIO)
    if height > shot.height:
        raise SystemExit(f"{SETTINGS} is shorter than the banner frame: {shot.height} < {height} px")
    if FADE_FROM_PX >= height:
        raise SystemExit("The fade starts below the bottom of the frame")
    shot.crop((0, 0, shot.width, height)).save(target, optimize=True)
    return shot.width, height


def faded_window(width_px: int, height_px: int) -> str:
    """The generator's window markup inside a layer whose mask fades the window and its shadow to nothing."""
    height = WIDTH * height_px / width_px
    fade_from = WIDTH * FADE_FROM_PX / width_px
    solid = PAD + fade_from
    gone = PAD + height
    stops = ", ".join(
        f"rgba(0,0,0,{(1 - t) ** 1.6:.3f}) {solid + (gone - solid) * t:.1f}px" for t in (0, .25, .5, .75, 1)
    )
    mask = f"linear-gradient(to bottom, #000 0px, {stops})"
    inner = WINDOW_CALL.replace(f"{LEFT}, {TOP},", f"{PAD}, {PAD},")
    return (
        f"(f'<div style=\"position:absolute;left:{LEFT - PAD}px;top:{TOP - PAD}px;width:{WIDTH + 2 * PAD}px;"
        f"height:{height + 2 * PAD:.1f}px;z-index:1;-webkit-mask-image:{mask};mask-image:{mask}\">'"
        f" + {inner} + '</div>')"
    )


def main() -> None:
    for stream in (sys.stdout, sys.stderr):
        stream.reconfigure(encoding="utf-8", errors="replace")
    parser = argparse.ArgumentParser(description="Render the Voice Input banners with the profile generator.")
    parser.add_argument("--generator", type=Path, default=os.environ.get("PAVEL_LOGACHEV_GENERATOR"),
                        help="design/generator of a pavel-logachev checkout (or PAVEL_LOGACHEV_GENERATOR)")
    parser.add_argument("--playwright", default=None, help="Playwright CLI, passed to the generator")
    parser.add_argument("--session", default="voice-input-banners", help="Playwright CLI session name")
    parser.add_argument("--out", type=Path, default=ASSETS, help="where the three PNG files go (default docs/assets)")
    args = parser.parse_args()
    if not args.generator or not (Path(args.generator) / "build.py").is_file():
        raise SystemExit("Give --generator <pavel-logachev>/design/generator (build.py not found)")

    with tempfile.TemporaryDirectory(prefix="voice-input-banners-") as temp:
        generator = Path(temp) / "generator"
        shutil.copytree(args.generator, generator, ignore=shutil.ignore_patterns("out", "preview", "__pycache__"))
        width_px, height_px = settings_source(generator / "sources" / "voice-settings.png")

        build = generator / "build.py"
        code = build.read_text(encoding="utf-8")
        if code.count(WINDOW_CALL) != 1:
            raise SystemExit(f"The generator no longer places the settings window with {WINDOW_CALL}; update this script")
        build.write_text(code.replace(WINDOW_CALL, faded_window(width_px, height_px)), encoding="utf-8")

        out = Path(temp) / "out"
        command = [sys.executable, str(build), "--out", str(out), "--only", "voice-input-", "--session", args.session]
        if args.playwright:
            command += ["--playwright", args.playwright]
        result = subprocess.run(command)
        if result.returncode:
            raise SystemExit(f"The generator failed with exit code {result.returncode}")

        args.out.mkdir(parents=True, exist_ok=True)
        for name in BANNERS:
            shutil.copyfile(out / "voice-input" / "docs" / "assets" / name, args.out / name)
            with Image.open(args.out / name) as im:
                print(f"{args.out / name}  {im.width}×{im.height}  {(args.out / name).stat().st_size} bytes")


if __name__ == "__main__":
    main()

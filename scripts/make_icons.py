#!/usr/bin/env python3
"""Builds the app icons for both platforms from one square PNG.

Usage: python3 scripts/make_icons.py [source.png]    (default: Packaging/AppIcon.png)

Writes Packaging/AppIcon.icns for macOS and
windows/src/StorageOptimizer.App/Assets/AppIcon.ico for Windows. Needs Pillow
(pip install pillow). Use a 1024x1024 source with a transparent background.
"""
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
ICNS = ROOT / "Packaging" / "AppIcon.icns"
ICO = ROOT / "windows" / "src" / "StorageOptimizer.App" / "Assets" / "AppIcon.ico"
# Apple's icon grid: the artwork is 824 pixels wide on a 1024-pixel canvas, which leaves room
# for the shadow and keeps the icon the same size as other apps in the Dock.
MAC_CANVAS, MAC_ARTWORK = 1024, 824
# Windows picks the closest size for the title bar, taskbar, Start menu and File Explorer.
ICO_SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]


def centered(image: Image.Image, canvas: int, artwork: int) -> Image.Image:
    """The visible part of the image, scaled to `artwork` pixels and centered on a transparent canvas."""
    visible = image.crop(image.getchannel("A").getbbox())
    scale = artwork / max(visible.size)
    visible = visible.resize((round(visible.width * scale), round(visible.height * scale)), Image.Resampling.LANCZOS)
    result = Image.new("RGBA", (canvas, canvas))
    result.paste(visible, ((canvas - visible.width) // 2, (canvas - visible.height) // 2))
    return result


source = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "Packaging" / "AppIcon.png"
image = Image.open(source).convert("RGBA")
if image.width != image.height:
    sys.exit(f"{source} must be square, not {image.width}x{image.height}.")
if image.width < 1024:
    print(f"Note: {source.name} is {image.width}x{image.height}, so the larger icon sizes are scaled up and look soft.")

centered(image, MAC_CANVAS, MAC_ARTWORK).save(ICNS)  # Pillow writes every size macOS uses, up to 1024 pixels.

ICO.parent.mkdir(parents=True, exist_ok=True)
frames = [image.resize((size, size), Image.Resampling.LANCZOS) for size in ICO_SIZES]
# BMP frames, which every Windows and WPF icon reader accepts.
frames[-1].save(ICO, sizes=[(size, size) for size in ICO_SIZES], append_images=frames[:-1], bitmap_format="bmp")

print(f"Wrote {ICNS.relative_to(ROOT)} and {ICO.relative_to(ROOT)}.")

#!/usr/bin/env python3
"""Builds the app icons for both platforms from one square PNG.

Usage: python3 scripts/make_icons.py [source.png]    (default: Packaging/AppIcon.png)

Writes Packaging/AppIcon.icns for macOS and
windows/src/StorageOptimizer.App/Assets/AppIcon.ico for Windows. Needs Pillow
(pip install pillow). A 1024x1024 source gives sharp icons at every size.
"""
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
ICNS = ROOT / "Packaging" / "AppIcon.icns"
ICO = ROOT / "windows" / "src" / "StorageOptimizer.App" / "Assets" / "AppIcon.ico"
# Windows picks the closest size for the title bar, taskbar, Start menu and File Explorer.
ICO_SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]

source = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "Packaging" / "AppIcon.png"
image = Image.open(source).convert("RGBA")
if image.width != image.height:
    sys.exit(f"{source} must be square, not {image.width}x{image.height}.")
if image.width < 1024:
    print(f"Note: {source.name} is {image.width}x{image.height}, so the larger icon sizes are scaled up and look soft.")

# Smaller sizes are made from the original, larger ones from a scaled-up copy.
large = image if image.width >= 1024 else image.resize((1024, 1024), Image.Resampling.LANCZOS)
large.save(ICNS)  # Pillow writes every size macOS uses, from 16 to 1024 pixels.

ICO.parent.mkdir(parents=True, exist_ok=True)
frames = [(image if size <= image.width else large).resize((size, size), Image.Resampling.LANCZOS) for size in ICO_SIZES]
# BMP frames, which every Windows and WPF icon reader accepts.
frames[-1].save(ICO, sizes=[(size, size) for size in ICO_SIZES], append_images=frames[:-1], bitmap_format="bmp")

print(f"Wrote {ICNS.relative_to(ROOT)} and {ICO.relative_to(ROOT)}.")

"""Значок TenBoost: морда Фера из кадра «отдыхает» в круге на фоне Ink.

art/mascot/raw/off-relax-1.png → app/TenBoost.App/Assets/TenBoost.ico и tray.png.

    python -I tools/make_icon.py
"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

# python -I не кладёт папку скрипта в sys.path.
sys.path.insert(0, str(Path(__file__).resolve().parent))
from cut_mascot import cut_magenta  # noqa: E402

REPO = Path(__file__).resolve().parent.parent
ASSETS = REPO / "app" / "TenBoost.App" / "Assets"
# Голова на исходнике 1024×1024.
HEAD = (612, 136, 884, 408)
INK = (12, 16, 14, 255)
SIZE = 512


def main() -> None:
    rgb = np.array(Image.open(REPO / "art" / "mascot" / "raw" / "off-relax-1.png").convert("RGB"))
    head = Image.fromarray(cut_magenta(rgb), "RGBA").crop(HEAD).resize((SIZE, SIZE), Image.LANCZOS)
    face = Image.new("RGBA", (SIZE, SIZE), INK)
    face.alpha_composite(head)
    mask = Image.new("L", (SIZE * 4, SIZE * 4), 0)
    ImageDraw.Draw(mask).ellipse((0, 0, SIZE * 4 - 1, SIZE * 4 - 1), fill=255)
    face.putalpha(mask.resize((SIZE, SIZE), Image.LANCZOS))

    face.resize((64, 64), Image.LANCZOS).save(ASSETS / "tray.png", optimize=True)
    face.save(ASSETS / "TenBoost.ico", sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (256, 256)])
    print("TenBoost.ico, tray.png")


if __name__ == "__main__":
    main()

"""Вырезает хорька из пурпурного фона Gemini для окна TenBoost.

art/mascot/raw/<имя>.png → app/TenBoost.App/Assets/Mascot/<имя>.png (RGBA).

Кадры одной анимации должны совпадать пиксель в пиксель, поэтому все файлы
режутся одной общей рамкой (объединение рамок всех кадров) и ужимаются до
одной ширины. Ключ по цвету — тот же, что у стикеров роликов
(tenvpn-shorts/tools/stickers-cut.py): цвет фона снимается с краёв и
вычитается из полупрозрачной каймы, чтобы на белой обводке не оставался
розовый ореол.

    python -I tools/cut_mascot.py                все файлы raw/
    python -I tools/cut_mascot.py off-relax-1    только эти
"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image

REPO = Path(__file__).resolve().parent.parent
RAW = REPO / "art" / "mascot" / "raw"
OUT = REPO / "app" / "TenBoost.App" / "Assets" / "Mascot"

# @2x для окна: картинка выводится до 320 px, брендбук — «больше 640 не нужно».
WIDTH = 640
PAD = 8


def border(shape) -> np.ndarray:
    b = np.zeros(shape, bool)
    b[0, :] = b[-1, :] = b[:, 0] = b[:, -1] = True
    return b


def dilate(mask: np.ndarray, r: int) -> np.ndarray:
    out = mask.copy()
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            if dy * dy + dx * dx <= r * r:
                out |= np.roll(np.roll(mask, dy, 0), dx, 1)
    return out


def cut_magenta(rgb: np.ndarray) -> np.ndarray:
    f = rgb.astype(np.float32)
    key = np.median(rgb[border(rgb.shape[:2])], axis=0).astype(np.float32)
    r, g, b = key
    if not (r > 140 and b > 140 and g < 110):
        raise SystemExit(f"фон не пурпурный: {key}")
    # Доля фона в пикселе: 1 у цвета ключа, 0 начиная с расстояния 200
    # (белая обводка, мех и худи все дальше).
    d = np.sqrt(((f - key) ** 2).sum(-1))
    m = np.clip((200 - d) / 150, 0, 1)
    strong = m > 0.9
    # Мягкие пиксели считаем фоном только рядом с настоящим фоном,
    # чтобы розовый нос и уши внутри рисунка остались.
    m = np.where(strong | (dilate(strong, 3) & (m > 0.05)), m, 0)
    alpha = 1 - m
    safe = np.maximum(alpha, 1e-3)[..., None]
    unmixed = np.clip((f - m[..., None] * key) / safe, 0, 255)
    return np.dstack([unmixed, alpha * 255]).astype(np.uint8)


def bbox(rgba: np.ndarray) -> tuple[int, int, int, int]:
    ys, xs = np.nonzero(rgba[..., 3] > 8)
    return xs.min(), ys.min(), xs.max() + 1, ys.max() + 1


def main() -> None:
    names = sys.argv[1:] or sorted(p.stem for p in RAW.glob("*.png"))
    cut = {}
    for name in names:
        rgb = np.array(Image.open(RAW / f"{name}.png").convert("RGB"))
        cut[name] = cut_magenta(rgb)

    sizes = {a.shape for a in cut.values()}
    if len(sizes) != 1:
        raise SystemExit(f"кадры разного размера: {sizes}")

    boxes = np.array([bbox(a) for a in cut.values()])
    h, w = next(iter(sizes))[:2]
    x0, y0 = max(boxes[:, 0].min() - PAD, 0), max(boxes[:, 1].min() - PAD, 0)
    x1, y1 = min(boxes[:, 2].max() + PAD, w), min(boxes[:, 3].max() + PAD, h)
    height = round((y1 - y0) * WIDTH / (x1 - x0))

    OUT.mkdir(parents=True, exist_ok=True)
    for name, rgba in cut.items():
        img = Image.fromarray(rgba[y0:y1, x0:x1], "RGBA").resize((WIDTH, height), Image.LANCZOS)
        img.save(OUT / f"{name}.png", optimize=True)
        print(f"{name:16} {WIDTH}x{height}")


if __name__ == "__main__":
    main()

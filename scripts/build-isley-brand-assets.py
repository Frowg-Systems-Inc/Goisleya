"""Build deterministic Isley PNG and ICO assets from the transparent master mark."""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image


ICON_SIZES = (16, 20, 24, 32, 40, 48, 64, 128, 256)


def build_square_master(source: Path) -> Image.Image:
    image = Image.open(source).convert("RGBA")
    alpha_bounds = image.getchannel("A").getbbox()
    if alpha_bounds is None:
        raise ValueError("The source mark contains no visible pixels.")

    subject = image.crop(alpha_bounds)
    longest_side = max(subject.size)
    padding = max(10, round(longest_side * 0.055))
    canvas_size = longest_side + (padding * 2)
    canvas = Image.new("RGBA", (canvas_size, canvas_size), (0, 0, 0, 0))
    canvas.alpha_composite(
        subject,
        ((canvas_size - subject.width) // 2, (canvas_size - subject.height) // 2),
    )
    return canvas


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--png", type=Path, required=True)
    parser.add_argument("--ico", type=Path, required=True)
    args = parser.parse_args()

    master = build_square_master(args.source)
    app_png = master.resize((1024, 1024), Image.Resampling.LANCZOS)
    args.png.parent.mkdir(parents=True, exist_ok=True)
    app_png.save(args.png, format="PNG", optimize=True)

    icon_source = master.resize((256, 256), Image.Resampling.LANCZOS)
    icon_source.save(args.ico, format="ICO", sizes=[(size, size) for size in ICON_SIZES])

    print(
        f"Built {args.png} ({app_png.width}x{app_png.height}) and {args.ico} "
        f"({len(ICON_SIZES)} embedded sizes)."
    )


if __name__ == "__main__":
    main()

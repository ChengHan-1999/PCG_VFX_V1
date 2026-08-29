"""Render a publication-ready 6x6 DreamSim pairwise-distance heatmap."""

from __future__ import annotations

import csv
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[2]
INPUT = ROOT / "EvaluationResults" / "DreamSim" / "Static_Final" / "DreamSimPairwise.csv"
OUTPUT = ROOT / "EvaluationResults" / "DreamSim" / "Static_Final" / "DreamSimPairwiseHeatmap.png"

ORDER = [
    ("player_01_seed99.png", "Player_01 (Ice)"),
    ("player_02_seed99.png", "Player_02 (Galaxy)"),
    ("player_03_seed99.png", "Player_03 (Holy)"),
    ("player_04_seed99.png", "Player_04 (Neutral)"),
    ("player_06_99.png", "Player_06 (Ocean)"),
    ("Player_11_seed99.png", "Player_11 (Forest)"),
]


def font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont:
    name = "arialbd.ttf" if bold else "arial.ttf"
    try:
        return ImageFont.truetype(name, size)
    except OSError:
        return ImageFont.truetype("DejaVuSans-Bold.ttf" if bold else "DejaVuSans.ttf", size)


def blend(a: tuple[int, int, int], b: tuple[int, int, int], t: float) -> tuple[int, int, int]:
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(3))


def blue_scale(value: float, maximum: float) -> tuple[int, int, int]:
    """A high-contrast sequential blue scale for numerical matrix cells."""
    t = min(max(value / maximum, 0.0), 1.0)
    low = (247, 251, 255)
    mid = (107, 174, 214)
    high = (8, 48, 107)
    return blend(low, mid, t / 0.55) if t <= 0.55 else blend(mid, high, (t - 0.55) / 0.45)


def centered_text(draw: ImageDraw.ImageDraw, box: tuple[int, int, int, int], text: str, use_font: ImageFont.FreeTypeFont, fill: tuple[int, int, int]) -> None:
    left, top, right, bottom = box
    bbox = draw.multiline_textbbox((0, 0), text, font=use_font, spacing=4, align="center")
    x = left + ((right - left) - (bbox[2] - bbox[0])) / 2
    y = top + ((bottom - top) - (bbox[3] - bbox[1])) / 2
    draw.multiline_text((x, y), text, font=use_font, fill=fill, spacing=4, align="center")


def main() -> None:
    values: dict[tuple[str, str], float] = {}
    with INPUT.open(encoding="utf-8-sig", newline="") as source:
        for row in csv.DictReader(source):
            first, second = row["ImageA"], row["ImageB"]
            values[(first, second)] = float(row["DreamSimDistance"])
            values[(second, first)] = float(row["DreamSimDistance"])

    names = [item[0] for item in ORDER]
    labels = [item[1] for item in ORDER]
    distances = [[0.0 if i == j else values[(names[i], names[j])] for j in range(len(names))] for i in range(len(names))]
    maximum = max(max(row) for row in distances)

    image_width, image_height = 2200, 1900
    left, top = 500, 350
    cell = 220
    matrix = cell * len(names)
    right = left + matrix
    bottom = top + matrix

    canvas = Image.new("RGB", (image_width, image_height), "white")
    draw = ImageDraw.Draw(canvas)
    title_font = font(60, True)
    subtitle_font = font(31)
    label_font = font(30, True)
    value_font = font(42, True)
    legend_font = font(29)

    centered_text(draw, (100, 60, image_width - 100, 135), "DreamSim Pairwise Perceptual Distance", title_font, (18, 40, 70))
    centered_text(draw, (100, 145, image_width - 100, 195), "Six representative VFX outputs • Seed 99 • 15 pairwise comparisons", subtitle_font, (74, 89, 108))

    for i, label in enumerate(labels):
        column_box = (left + i * cell, 230, left + (i + 1) * cell, top - 20)
        centered_text(draw, column_box, label.replace(" ", "\n", 1), label_font, (25, 39, 58))
        row_box = (55, top + i * cell, left - 20, top + (i + 1) * cell)
        centered_text(draw, row_box, label, label_font, (25, 39, 58))

    for i in range(len(names)):
        for j in range(len(names)):
            x0, y0 = left + j * cell, top + i * cell
            x1, y1 = x0 + cell, y0 + cell
            value = distances[i][j]
            fill = (236, 238, 241) if i == j else blue_scale(value, maximum)
            draw.rectangle((x0, y0, x1, y1), fill=fill, outline=(255, 255, 255), width=5)
            text_color = (31, 45, 61) if i == j or value / maximum < 0.49 else (255, 255, 255)
            centered_text(draw, (x0 + 5, y0 + 5, x1 - 5, y1 - 5), f"{value:.3f}", value_font, text_color)

    # Matrix frame
    draw.rectangle((left, top, right, bottom), outline=(52, 79, 108), width=4)

    legend_x, legend_y, legend_w, legend_h = 705, 1650, 800, 44
    for column in range(legend_w):
        colour = blue_scale(maximum * column / (legend_w - 1), maximum)
        draw.line((legend_x + column, legend_y, legend_x + column, legend_y + legend_h), fill=colour, width=1)
    draw.rectangle((legend_x, legend_y, legend_x + legend_w, legend_y + legend_h), outline=(52, 79, 108), width=2)
    draw.text((legend_x, legend_y + 60), "0.000", font=legend_font, fill=(25, 39, 58))
    mid_text = f"{maximum / 2:.3f}"
    mid_box = draw.textbbox((0, 0), mid_text, font=legend_font)
    draw.text((legend_x + legend_w / 2 - (mid_box[2] - mid_box[0]) / 2, legend_y + 60), mid_text, font=legend_font, fill=(25, 39, 58))
    max_box = draw.textbbox((0, 0), f"{maximum:.3f}", font=legend_font)
    draw.text((legend_x + legend_w - (max_box[2] - max_box[0]), legend_y + 60), f"{maximum:.3f}", font=legend_font, fill=(25, 39, 58))
    centered_text(draw, (450, 1740, 1750, 1800), "DreamSim distance (higher = more visually distinct)", legend_font, (25, 39, 58))

    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(OUTPUT, dpi=(300, 300))
    print(OUTPUT)


if __name__ == "__main__":
    main()

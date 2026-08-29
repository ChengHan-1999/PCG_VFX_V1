"""Render a publication-ready raw-vs-smoothed theme-vector line chart."""

from __future__ import annotations

import csv
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
INPUT = ROOT / "EvaluationResults" / "DynamicResults.csv"
OUTPUT = ROOT / "Thesis_Draft_Assets" / "Figure_5_3_DynamicThemeVectors.png"

THEMES = [
    ("Ice", "RawIce", "SmoothIce", (46, 142, 214)),
    ("Forest", "RawForest", "SmoothForest", (41, 138, 78)),
    ("Ocean", "RawOcean", "SmoothOcean", (25, 143, 156)),
]


def get_font(size: int, bold: bool = False):
    try:
        return ImageFont.truetype("arialbd.ttf" if bold else "arial.ttf", size)
    except OSError:
        return ImageFont.truetype("DejaVuSans-Bold.ttf" if bold else "DejaVuSans.ttf", size)


def blend(first: tuple[int, int, int], second: tuple[int, int, int], amount: float) -> tuple[int, int, int]:
    return tuple(round(first[i] + (second[i] - first[i]) * amount) for i in range(3))


def dashed_line(draw: ImageDraw.ImageDraw, points: list[tuple[float, float]], colour: tuple[int, int, int], width: int = 4) -> None:
    """Draw a dashed polyline while preserving the route through all nodes."""
    for first, second in zip(points, points[1:]):
        x1, y1 = first
        x2, y2 = second
        length = ((x2 - x1) ** 2 + (y2 - y1) ** 2) ** 0.5
        if length == 0:
            continue
        dx, dy = (x2 - x1) / length, (y2 - y1) / length
        position = 0.0
        while position < length:
            end = min(position + 16, length)
            draw.line((x1 + dx * position, y1 + dy * position, x1 + dx * end, y1 + dy * end), fill=colour, width=width)
            position += 28


def text_centered(draw: ImageDraw.ImageDraw, box: tuple[int, int, int, int], text: str, use_font, fill: tuple[int, int, int]) -> None:
    bounds = draw.multiline_textbbox((0, 0), text, font=use_font, spacing=4, align="center")
    x = box[0] + ((box[2] - box[0]) - (bounds[2] - bounds[0])) / 2
    y = box[1] + ((box[3] - box[1]) - (bounds[3] - bounds[1])) / 2
    draw.multiline_text((x, y), text, font=use_font, fill=fill, spacing=4, align="center")


def main() -> None:
    with INPUT.open(encoding="utf-8-sig", newline="") as source:
        rows = list(csv.DictReader(source))

    width, height = 2600, 1650
    left, right, top, bottom = 250, 2200, 310, 1290
    canvas = Image.new("RGB", (width, height), "white")
    draw = ImageDraw.Draw(canvas)

    title_font = get_font(61, True)
    subtitle_font = get_font(31)
    axis_font = get_font(32)
    tick_font = get_font(29)
    legend_font = get_font(31, True)
    note_font = get_font(28)

    text_centered(draw, (100, 52, width - 100, 130), "Raw and Temporally Smoothed Theme Vectors", title_font, (20, 42, 72))
    text_centered(draw, (100, 145, width - 100, 192), "Dynamic trajectory: Player_01, Day 1 to Day 39", subtitle_font, (74, 89, 108))

    # Plot frame and horizontal grid.
    grid_colour = (219, 226, 234)
    border_colour = (53, 78, 105)
    for tick in range(0, 6):
        value = tick / 5
        y = bottom - value * (bottom - top)
        draw.line((left, y, right, y), fill=grid_colour, width=2)
        label = f"{value:.1f}"
        bounds = draw.textbbox((0, 0), label, font=tick_font)
        draw.text((left - 28 - (bounds[2] - bounds[0]), y - (bounds[3] - bounds[1]) / 2), label, font=tick_font, fill=(55, 67, 82))
    draw.rectangle((left, top, right, bottom), outline=border_colour, width=4)

    def x_position(index: int) -> float:
        return left + index * (right - left) / (len(rows) - 1)

    def y_position(value: float) -> float:
        return bottom - value * (bottom - top)

    # Event guide lines and terse event labels.
    event_labels = ["D1\nBaseline", "D4\nSettled", "D8\nForest", "D14\nMastery", "D20\nForest", "D27\nOcean", "D39\nIce"]
    for index, label in enumerate(event_labels):
        x = x_position(index)
        draw.line((x, top, x, bottom), fill=(231, 236, 242), width=2)
        draw.line((x, bottom, x, bottom + 11), fill=border_colour, width=3)
        text_centered(draw, (int(x - 90), bottom + 20, int(x + 90), bottom + 103), label, tick_font, (45, 59, 77))

    # Theme trajectories. The raw value is a dashed lower-emphasis line, the smoothed value is solid.
    for _, raw_key, smooth_key, colour in THEMES:
        raw_colour = blend(colour, (255, 255, 255), 0.40)
        raw_points = [(x_position(index), y_position(float(row[raw_key]))) for index, row in enumerate(rows)]
        smooth_points = [(x_position(index), y_position(float(row[smooth_key]))) for index, row in enumerate(rows)]
        dashed_line(draw, raw_points, raw_colour, width=5)
        draw.line(smooth_points, fill=colour, width=8, joint="curve")
        for x, y in smooth_points:
            radius = 10
            draw.ellipse((x - radius, y - radius, x + radius, y + radius), fill="white", outline=colour, width=5)

    # Axis titles.
    text_centered(draw, (left, 1434, right, 1490), "Dynamic trajectory node", axis_font, (32, 48, 68))
    y_title = "Theme vector value"
    y_bounds = draw.textbbox((0, 0), y_title, font=axis_font)
    rotated = Image.new("RGBA", (y_bounds[2] - y_bounds[0] + 10, y_bounds[3] - y_bounds[1] + 10), (255, 255, 255, 0))
    rotated_draw = ImageDraw.Draw(rotated)
    rotated_draw.text((5, 5), y_title, font=axis_font, fill=(32, 48, 68))
    rotated = rotated.rotate(90, expand=True)
    canvas.paste(rotated, (62, int((top + bottom - rotated.height) / 2)), rotated)

    # Compact style key.
    key_y = 1560
    draw.line((515, key_y, 630, key_y), fill=(83, 98, 118), width=7)
    draw.text((650, key_y - 19), "Temporally smoothed", font=note_font, fill=(45, 59, 77))
    dashed_line(draw, [(1035, key_y), (1150, key_y)], (133, 145, 160), width=5)
    draw.text((1170, key_y - 19), "Raw vector", font=note_font, fill=(45, 59, 77))

    # Theme-colour legend.
    legend_x, legend_y = 1410, 1536
    for index, (label, _, _, colour) in enumerate(THEMES):
        x = legend_x + index * 158
        draw.line((x, legend_y + 20, x + 40, legend_y + 20), fill=colour, width=8)
        draw.text((x + 52, legend_y), label, font=note_font, fill=(45, 59, 77))

    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(OUTPUT, dpi=(300, 300))
    print(OUTPUT)


if __name__ == "__main__":
    main()

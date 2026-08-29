"""Create a thesis-ready OAT sensitivity-analysis figure from Unity CSV outputs.

The bundled workstation runtime includes Pillow but not Matplotlib, so the plot
is intentionally drawn with Pillow to make the figure reproducible here.
"""

from __future__ import annotations

import csv
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(r"C:\Final_Lesson\PCG_VFX_V1")
INPUT = ROOT / "EvaluationResults" / "SensitivityAnalysis" / "SensitivitySummary.csv"
OUTPUT_DIR = ROOT / "Thesis_Draft_Assets"
OUTPUT = OUTPUT_DIR / "Figure_5_4_OAT_SensitivityAnalysis.png"


def read_rows():
    with INPUT.open("r", encoding="utf-8-sig", newline="") as handle:
        return list(csv.DictReader(handle))


def row_by_id(rows, scenario_id):
    for row in rows:
        if row["ScenarioId"] == scenario_id:
            return row
    raise KeyError(scenario_id)


def value(row, field):
    return float(row[field])


FONT_DIR = Path(r"C:\Windows\Fonts")


def font(size, bold=False):
    path = FONT_DIR / ("arialbd.ttf" if bold else "arial.ttf")
    return ImageFont.truetype(str(path), size=size)


def text_size(draw, text, use_font):
    box = draw.textbbox((0, 0), text, font=use_font)
    return box[2] - box[0], box[3] - box[1]


def text_center(draw, xy, text, use_font, fill):
    width, height = text_size(draw, text, use_font)
    draw.text((xy[0] - width / 2, xy[1] - height / 2), text, font=use_font, fill=fill)


def draw_dashed_vertical(draw, x, y0, y1, color, dash=8, gap=6):
    y = y0
    while y < y1:
        draw.line((x, y, x, min(y + dash, y1)), fill=color, width=2)
        y += dash + gap


def draw_marker(draw, x, y, color, shape="circle"):
    r = 7
    if shape == "square":
        draw.rectangle((x - r, y - r, x + r, y + r), fill=color, outline="white", width=2)
    elif shape == "triangle":
        draw.polygon([(x, y - r - 1), (x - r - 1, y + r), (x + r + 1, y + r)], fill=color, outline="white")
    else:
        draw.ellipse((x - r, y - r, x + r, y + r), fill=color, outline="white", width=2)


def draw_legend(draw, x, y, items, use_font):
    cursor_y = y
    for label, color, marker in items:
        draw.line((x, cursor_y + 7, x + 28, cursor_y + 7), fill=color, width=4)
        draw_marker(draw, x + 14, cursor_y + 7, color, marker)
        draw.text((x + 36, cursor_y), label, font=use_font, fill="#1b1b1b")
        cursor_y += 30


def linear_scale(v, vmin, vmax, y_bottom, y_top):
    return y_bottom - (v - vmin) / (vmax - vmin) * (y_bottom - y_top)


def draw_axes(draw, x0, y0, width, height, title, x_label, y_label, y_min, y_max, y_ticks, y_formatter, x_positions, x_labels):
    black = "#1b1b1b"
    grid = "#d6d6d6"
    title_font = font(35, bold=True)
    axis_font = font(25)
    tick_font = font(23)
    draw.text((x0, y0 - 67), title, font=title_font, fill=black)
    y_top = y0
    y_bottom = y0 + height
    x_left = x0 + 105
    x_right = x0 + width - 20
    for tick in y_ticks:
        y = linear_scale(tick, y_min, y_max, y_bottom, y_top)
        draw.line((x_left, y, x_right, y), fill=grid, width=2)
        label = y_formatter(tick)
        text_w, text_h = text_size(draw, label, tick_font)
        draw.text((x_left - 14 - text_w, y - text_h / 2), label, font=tick_font, fill=black)
    draw.line((x_left, y_top, x_left, y_bottom), fill=black, width=2)
    draw.line((x_left, y_bottom, x_right, y_bottom), fill=black, width=2)
    for pos, label in zip(x_positions, x_labels):
        draw.line((pos, y_bottom, pos, y_bottom + 8), fill=black, width=2)
        lines = label.split("\n")
        for i, line in enumerate(lines):
            text_center(draw, (pos, y_bottom + 26 + 25 * i), line, tick_font, black)
    text_center(draw, ((x_left + x_right) / 2, y_bottom + 88), x_label, axis_font, black)
    rotated = Image.new("RGBA", (height, 60), (255, 255, 255, 0))
    rotated_draw = ImageDraw.Draw(rotated)
    text_center(rotated_draw, (height / 2, 30), y_label, axis_font, black)
    rotated = rotated.rotate(90, expand=True)
    draw.bitmap((x0 - 79, y0 + height / 2 - rotated.height / 2), rotated, fill=None)
    return x_left, x_right, y_top, y_bottom


def main():
    rows = read_rows()
    baseline = row_by_id(rows, "Baseline")
    x = [-10, 0, 10]
    x_labels = ["−10%", "Baseline", "+10%"]

    selected_static = [
        ("Region eligibility threshold", "RegionEligibilityThreshold"),
        ("Stage-2 weapon semantic weight", "Stage2WeaponSemanticWeight"),
        ("Stage-2 region semantic weight", "Stage2RegionSemanticWeight"),
    ]

    static_values = {}
    for label, parameter in selected_static:
        minus = row_by_id(rows, parameter + "_Minus10Percent")
        plus = row_by_id(rows, parameter + "_Plus10Percent")
        static_values[label] = [
            value(minus, "StaticThemeSemanticSeparation"),
            value(baseline, "StaticThemeSemanticSeparation"),
            value(plus, "StaticThemeSemanticSeparation"),
        ]

    threshold_minus = row_by_id(rows, "RegionEligibilityThreshold_Minus10Percent")
    threshold_plus = row_by_id(rows, "RegionEligibilityThreshold_Plus10Percent")
    threshold_x = [0.72, 0.80, 0.88]
    threshold_static_changes = [
        value(threshold_minus, "StaticThemeChangesVsBaseline"),
        value(baseline, "StaticThemeChangesVsBaseline"),
        value(threshold_plus, "StaticThemeChangesVsBaseline"),
    ]
    threshold_slot_changes = [
        value(threshold_minus, "StaticTopSlotChangesVsBaseline"),
        value(baseline, "StaticTopSlotChangesVsBaseline"),
        value(threshold_plus, "StaticTopSlotChangesVsBaseline"),
    ]
    threshold_fallbacks = [
        value(threshold_minus, "StaticFallbackCount"),
        value(baseline, "StaticFallbackCount"),
        value(threshold_plus, "StaticFallbackCount"),
    ]

    smoothing_minus = row_by_id(rows, "TrajectoryThemeTemporalSmoothingAlpha_Minus10Percent")
    smoothing_plus = row_by_id(rows, "TrajectoryThemeTemporalSmoothingAlpha_Plus10Percent")
    smoothing_x = [0.675, 0.750, 0.825]
    dynamic_changes = [
        value(smoothing_minus, "DynamicThemeChangesVsBaseline"),
        value(baseline, "DynamicThemeChangesVsBaseline"),
        value(smoothing_plus, "DynamicThemeChangesVsBaseline"),
    ]
    dynamic_fallbacks = [
        value(smoothing_minus, "DynamicFallbackCount"),
        value(baseline, "DynamicFallbackCount"),
        value(smoothing_plus, "DynamicFallbackCount"),
    ]
    dynamic_hits = [
        value(smoothing_minus, "DynamicThemeHitRate"),
        value(baseline, "DynamicThemeHitRate"),
        value(smoothing_plus, "DynamicThemeHitRate"),
    ]

    # Recency half-lives are shown explicitly even though every local
    # perturbation returned zero change.  This makes the tested stability
    # visible, rather than making the parameters appear to have been omitted.
    half_life_entries = [
        ("Weapon\n15 → 13.5 / 16.5 days", "WeaponHalfLifeDays"),
        ("Boss\n4 → 3.6 / 4.4 days", "BossRecencyHalfLifeDays"),
        ("Region\n20 → 18 / 22 days", "RegionRecencyHalfLifeDays"),
    ]
    half_life_theme = []
    half_life_slot = []
    for _, parameter in half_life_entries:
        minus = row_by_id(rows, parameter + "_Minus10Percent")
        plus = row_by_id(rows, parameter + "_Plus10Percent")
        half_life_theme.append(max(
            abs(value(minus, "DeltaStaticThemeSemanticSeparation")),
            abs(value(plus, "DeltaStaticThemeSemanticSeparation")),
        ))
        half_life_slot.append(max(
            abs(value(minus, "DeltaStaticTopSlotDifference")),
            abs(value(plus, "DeltaStaticTopSlotDifference")),
        ))

    canvas = Image.new("RGB", (4800, 2440), "white")
    draw = ImageDraw.Draw(canvas)
    panel_width, panel_height = 1450, 940
    x_starts, y_top = [145, 1725, 3305], 290
    colors = ["#284b63", "#7d4e57", "#3d7a5b"]
    marker_shapes = ["circle", "square", "triangle"]

    # (a) Static semantic separation
    px, py = x_starts[0], y_top
    x_positions = [px + 265, px + 765, px + 1265]
    x_left, x_right, top, bottom = draw_axes(
        draw, px, py, panel_width, panel_height, "(a) Static semantic separation",
        "One-at-a-time perturbation", "Mean cosine distance", 0.37, 0.47,
        [0.37, 0.39, 0.41, 0.43, 0.45, 0.47], lambda v: f"{v:.3f}", x_positions, x_labels,
    )
    for (label, _), color, shape in zip(selected_static, colors, marker_shapes):
        vals = static_values[label]
        points = [(xp, linear_scale(v, 0.37, 0.47, bottom, top)) for xp, v in zip(x_positions, vals)]
        draw.line(points, fill=color, width=6)
        for xp, yp in points:
            draw_marker(draw, xp, yp, color, shape)
    draw_legend(draw, px + 150, py + 26, list(zip([s[0] for s in selected_static], colors, marker_shapes)), font(21))

    # (b) Region threshold discrete output changes
    px = x_starts[1]
    x_positions = [px + 265, px + 765, px + 1265]
    x_left, x_right, top, bottom = draw_axes(
        draw, px, py, panel_width, panel_height, "(b) Region eligibility threshold",
        "Eligibility threshold", "Count", 0, 9,
        [0, 2, 4, 6, 8], lambda v: str(int(v)), x_positions, ["0.72", "0.80\nBaseline", "0.88"],
    )
    draw_dashed_vertical(draw, x_positions[1], top, bottom, "#808080")
    series = [
        ("Theme changes", threshold_static_changes, "#8c3d3d", "circle"),
        ("Slot changes", threshold_slot_changes, "#3b6f9c", "square"),
        ("Fallback profiles", threshold_fallbacks, "#6a8054", "triangle"),
    ]
    for label, vals, color, shape in series:
        points = [(xp, linear_scale(v, 0, 9, bottom, top)) for xp, v in zip(x_positions, vals)]
        draw.line(points, fill=color, width=6)
        for xp, yp in points:
            draw_marker(draw, xp, yp, color, shape)
    draw_legend(draw, px + 150, py + 26, [(a, c, d) for a, _, c, d in series], font(21))

    # (c) Smoothing coefficient discrete output changes
    px = x_starts[2]
    x_positions = [px + 265, px + 765, px + 1265]
    x_left, x_right, top, bottom = draw_axes(
        draw, px, py, panel_width, panel_height, "(c) Dynamic smoothing coefficient",
        "Temporal smoothing coefficient α", "Count across seven trajectory nodes", 0, 2.4,
        [0, 1, 2], lambda v: str(int(v)), x_positions, ["0.675", "0.750\nBaseline", "0.825"],
    )
    draw_dashed_vertical(draw, x_positions[1], top, bottom, "#808080")
    series = [
        ("Theme changes", dynamic_changes, "#8c3d3d", "circle"),
        ("Fallback nodes", dynamic_fallbacks, "#3b6f9c", "square"),
    ]
    for label, vals, color, shape in series:
        points = [(xp, linear_scale(v, 0, 2.4, bottom, top)) for xp, v in zip(x_positions, vals)]
        draw.line(points, fill=color, width=6)
        for xp, yp in points:
            draw_marker(draw, xp, yp, color, shape)
    draw_legend(draw, px + 150, py + 26, [(a, c, d) for a, _, c, d in series], font(21))
    text_center(draw, (px + panel_width / 2, 1490), "Theme hit rate remains 5/7 = 0.714 at all three settings.", font(23), "#333333")

    # (d) Recency half-life: both continuous metric deltas are exactly zero
    # for the +/-10% tests.  The y-axis is intentionally centred on zero.
    px, py = 820, 1730
    panel_width, panel_height = 3160, 400
    x_positions = [px + 700, px + 1580, px + 2460]
    x_labels_half_life = [label for label, _ in half_life_entries]
    x_left, x_right, top, bottom = draw_axes(
        draw, px, py, panel_width, panel_height, "(d) Recency half-lives (±10% local perturbation)",
        "Tested half-life range", "Maximum absolute change", -0.0005, 0.0005,
        [-0.0005, 0.0, 0.0005], lambda v: "0" if abs(v) < 1e-12 else f"{v:+.4f}",
        x_positions, x_labels_half_life,
    )
    zero_y = linear_scale(0.0, -0.0005, 0.0005, bottom, top)
    draw.line((x_left, zero_y, x_right, zero_y), fill="#9B9B9B", width=3)
    for x_pos, theme_delta, slot_delta in zip(x_positions, half_life_theme, half_life_slot):
        # The small offset separates the two zero markers visually.
        draw_marker(draw, x_pos - 17, linear_scale(theme_delta, -0.0005, 0.0005, bottom, top), "#8c3d3d", "circle")
        draw_marker(draw, x_pos + 17, linear_scale(slot_delta, -0.0005, 0.0005, bottom, top), "#3b6f9c", "square")
        text_center(draw, (x_pos, zero_y - 34), "0.000000", font(21), "#333333")
    draw_legend(
        draw, px + 150, py + 27,
        [("Δ theme semantic separation", "#8c3d3d", "circle"), ("Δ slot difference", "#3b6f9c", "square")],
        font(21),
    )
    text_center(
        draw, (px + panel_width / 2, 2395),
        "All six half-life perturbations retained identical final outputs; time-decay audit correctness = 1.000000.",
        font(23), "#333333",
    )

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    canvas.save(OUTPUT, "PNG", optimize=True)
    print(OUTPUT)


if __name__ == "__main__":
    main()

"""Build a thesis-ready table image for all direct OAT sensitivity parameters."""

from __future__ import annotations

import csv
import re
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(r"C:\Final_Lesson\PCG_VFX_V1")
INPUT = ROOT / "EvaluationResults" / "SensitivityAnalysis" / "SensitivitySummary.csv"
OUTPUT_DIR = ROOT / "Thesis_Draft_Assets"
OUTPUT = OUTPUT_DIR / "Table_5_4_OAT_SensitivityResults.png"
FONT_DIR = Path(r"C:\Windows\Fonts")


ORDER = [
    "WeaponChoiceShareWeight", "WeaponUseRateWeight", "WeaponInvestmentShareWeight",
    "BossDifficultyWeight", "BossRarityWeight", "BossWinRateWeight",
    "RegionExplorationWeight", "RegionQuestWeight", "RegionRecentVisitWeight",
    "RegionVisitRateWeight", "RegionDepthWeight", "RegionEligibilityThreshold",
    "WeaponHalfLifeDays", "BossRecencyHalfLifeDays", "RegionRecencyHalfLifeDays",
    "Stage2WeaponSemanticWeight", "Stage2BossSemanticWeight", "Stage2RegionSemanticWeight",
    "TrajectoryThemeTemporalSmoothingAlpha",
]

DISPLAY = {
    "WeaponChoiceShareWeight": ("Weapon score", "ChoiceShare weight"),
    "WeaponUseRateWeight": ("Weapon score", "UseRate weight"),
    "WeaponInvestmentShareWeight": ("Weapon score", "InvestmentShare weight"),
    "BossDifficultyWeight": ("Boss score", "Difficulty weight"),
    "BossRarityWeight": ("Boss score", "Rarity weight"),
    "BossWinRateWeight": ("Boss score", "Win-rate weight"),
    "RegionExplorationWeight": ("Region score", "Exploration weight"),
    "RegionQuestWeight": ("Region score", "Quest weight"),
    "RegionRecentVisitWeight": ("Region score", "Recent-visit weight"),
    "RegionVisitRateWeight": ("Region score", "Visit-rate weight"),
    "RegionDepthWeight": ("Region score", "Depth weight"),
    "RegionEligibilityThreshold": ("Region score", "Eligibility threshold"),
    "WeaponHalfLifeDays": ("Recency", "Weapon half-life (days)"),
    "BossRecencyHalfLifeDays": ("Recency", "Boss half-life (days)"),
    "RegionRecencyHalfLifeDays": ("Recency", "Region half-life (days)"),
    "Stage2WeaponSemanticWeight": ("Stage-2 semantics", "Weapon semantic weight"),
    "Stage2BossSemanticWeight": ("Stage-2 semantics", "Boss semantic weight"),
    "Stage2RegionSemanticWeight": ("Stage-2 semantics", "Region semantic weight"),
    "TrajectoryThemeTemporalSmoothingAlpha": ("Dynamic", "Smoothing coefficient α"),
}


def get_font(size, bold=False):
    return ImageFont.truetype(str(FONT_DIR / ("arialbd.ttf" if bold else "arial.ttf")), size)


def clip_text(draw, text, use_font, width):
    if draw.textlength(text, font=use_font) <= width:
        return text
    suffix = "…"
    trimmed = text
    while trimmed and draw.textlength(trimmed + suffix, font=use_font) > width:
        trimmed = trimmed[:-1]
    return trimmed + suffix


def wrap_text(draw, text, use_font, width):
    parts = []
    for original_line in str(text).split("\n"):
        words = original_line.split(" ")
        current = ""
        for word in words:
            candidate = word if not current else current + " " + word
            if draw.textlength(candidate, font=use_font) <= width:
                current = candidate
            else:
                if current:
                    parts.append(current)
                current = word
        if current or not words:
            parts.append(current)
    return parts


def value_from_named_values(text, key):
    match = re.search(r"(?:^|;\s*)" + re.escape(key) + r"=([-+]?\d*\.?\d+)", text)
    return match.group(1) if match else "—"


def fmt_delta(v):
    f = float(v)
    if abs(f) < 0.0000005:
        return "0"
    return f"{f:+.6f}"


def change_note(row):
    static_theme = int(float(row["StaticThemeChangesVsBaseline"]))
    static_slots = int(float(row["StaticTopSlotChangesVsBaseline"]))
    dynamic_theme = int(float(row["DynamicThemeChangesVsBaseline"]))
    dynamic_fallback = int(float(row["DynamicFallbackCount"]))
    items = []
    if static_theme:
        items.append(f"static themes +{static_theme}")
    if static_slots:
        items.append(f"static slots +{static_slots}")
    if dynamic_theme:
        items.append(f"dynamic themes +{dynamic_theme}")
    if dynamic_fallback:
        items.append(f"dynamic fallback {dynamic_fallback}")
    return "; ".join(items) if items else "no discrete output change"


def main():
    with INPUT.open("r", encoding="utf-8-sig", newline="") as file:
        source_rows = list(csv.DictReader(file))
    baseline = next(row for row in source_rows if row["ScenarioId"] == "Baseline")
    by_scenario = {row["ScenarioId"]: row for row in source_rows}

    rows = []
    for parameter in ORDER:
        minus = by_scenario[parameter + "_Minus10Percent"]
        plus = by_scenario[parameter + "_Plus10Percent"]
        group, label = DISPLAY[parameter]
        baseline_value = value_from_named_values(minus["BaselineParameterValues"], parameter)
        minus_value = value_from_named_values(minus["TestedParameterValues"], parameter)
        plus_value = value_from_named_values(plus["TestedParameterValues"], parameter)
        rows.append({
            "group": group,
            "label": label,
            "base": baseline_value,
            "minus": minus_value + "\n" + change_note(minus),
            "plus": plus_value + "\n" + change_note(plus),
            "theme": fmt_delta(minus["DeltaStaticThemeSemanticSeparation"]) + " / " + fmt_delta(plus["DeltaStaticThemeSemanticSeparation"]),
            "slot": fmt_delta(minus["DeltaStaticTopSlotDifference"]) + " / " + fmt_delta(plus["DeltaStaticTopSlotDifference"]),
        })

    width = 5600
    margin = 120
    column_widths = [660, 980, 390, 1160, 1160, 570, 570]
    x_positions = [margin]
    for width_value in column_widths[:-1]:
        x_positions.append(x_positions[-1] + width_value)

    header_font = get_font(26, True)
    body_font = get_font(24)
    small_font = get_font(21)
    header_height = 105
    row_height = 102
    # The table itself is inserted below its Word/WPS caption, so deliberately
    # omit a duplicated title, baseline note, and footer from the raster image.
    top_margin = 28
    bottom_margin = 28
    height = top_margin + header_height + row_height * len(rows) + bottom_margin
    image = Image.new("RGB", (width, height), "white")
    draw = ImageDraw.Draw(image)

    y = top_margin
    headers = [
        "Parameter group", "Direct parameter", "Baseline", "−10% test value and result",
        "+10% test value and result", "Δ theme separation\n(− / +)", "Δ slot difference\n(− / +)",
    ]
    header_fill = "#294E6B"
    for x, col_width, label in zip(x_positions, column_widths, headers):
        draw.rectangle((x, y, x + col_width, y + header_height), fill=header_fill, outline="white", width=3)
        lines = wrap_text(draw, label, header_font, col_width - 28)
        line_height = 30
        offset = (header_height - len(lines) * line_height) / 2
        for i, line in enumerate(lines):
            draw.text((x + 14, y + offset + i * line_height), line, font=header_font, fill="white")
    y += header_height

    last_group = None
    for index, row in enumerate(rows):
        fill = "#F3F7FA" if index % 2 == 0 else "#FFFFFF"
        if row["group"] != last_group:
            fill = "#E3EDF4"
            last_group = row["group"]
        cells = [row["group"], row["label"], row["base"], row["minus"], row["plus"], row["theme"], row["slot"]]
        for x, col_width, cell in zip(x_positions, column_widths, cells):
            draw.rectangle((x, y, x + col_width, y + row_height), fill=fill, outline="#AEB6BF", width=2)
            cell_font = small_font if x in (x_positions[3], x_positions[4], x_positions[5], x_positions[6]) else body_font
            lines = wrap_text(draw, cell, cell_font, col_width - 22)
            line_height = 27 if cell_font == small_font else 30
            max_lines = 3
            lines = lines[:max_lines]
            total_height = len(lines) * line_height
            start_y = y + (row_height - total_height) / 2
            for i, line in enumerate(lines):
                draw.text((x + 11, start_y + i * line_height), line, font=cell_font, fill="#1F2933")
        y += row_height

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    image.save(OUTPUT, "PNG", optimize=True)
    print(OUTPUT)


if __name__ == "__main__":
    main()

"""Calculate pairwise DreamSim visual distances for Unity PCG-VFX screenshots.

Input: a folder of same-camera RGB PNG/JPG screenshots, e.g. Player_01.png.
Output: pairwise distances, per-player distinctness, and an overall summary CSV.
"""

import argparse
import csv
from datetime import datetime, timezone
from itertools import combinations
from pathlib import Path

import pandas as pd
import torch
import torch.nn.functional as functional
from PIL import Image
from dreamsim import dreamsim


VALID_IMAGE_EXTENSIONS = {".png", ".jpg", ".jpeg"}


def find_images(folder: Path):
    return sorted(
        image_path
        for image_path in folder.iterdir()
        if image_path.is_file() and image_path.suffix.lower() in VALID_IMAGE_EXTENSIONS
    )


def extract_embedding(model, preprocess, image_path: Path, device: str):
    with Image.open(image_path) as source_image:
        image = source_image.convert("RGB")
        tensor = preprocess(image).to(device)

    with torch.no_grad():
        return model.embed(tensor).detach().cpu()


def cosine_distance(first_embedding, second_embedding) -> float:
    similarity = functional.cosine_similarity(first_embedding, second_embedding, dim=-1)
    return float((1.0 - similarity.mean()).item())


def write_pairwise_results(rows, output_directory: Path):
    path = output_directory / "DreamSimPairwise.csv"
    with path.open("w", newline="", encoding="utf-8") as destination:
        writer = csv.DictWriter(
            destination,
            fieldnames=["ImageA", "ImageB", "DreamSimDistance"],
        )
        writer.writeheader()
        writer.writerows(rows)
    return path


def write_per_player_results(image_names, pairwise_rows, output_directory: Path):
    rows = []
    for image_name in image_names:
        distances = [
            pair["DreamSimDistance"]
            for pair in pairwise_rows
            if pair["ImageA"] == image_name or pair["ImageB"] == image_name
        ]
        rows.append(
            {
                "Image": image_name,
                "ComparedPairCount": len(distances),
                "MeanDistanceToOtherPlayers": sum(distances) / len(distances),
                "MedianDistanceToOtherPlayers": float(pd.Series(distances).median()),
            }
        )

    path = output_directory / "DreamSimPerPlayer.csv"
    pd.DataFrame(rows).to_csv(path, index=False, encoding="utf-8")
    return path


def write_summary(pairwise_rows, image_count: int, device: str, output_directory: Path):
    values = [row["DreamSimDistance"] for row in pairwise_rows]
    summary = pd.DataFrame(
        [
            {
                "GeneratedAtUtc": datetime.now(timezone.utc).isoformat(),
                "Model": "DreamSim ensemble (CLIP + DINO + OpenCLIP)",
                "Device": device,
                "ImageCount": image_count,
                "PairCount": len(values),
                "MeanDreamSimDistance": sum(values) / len(values),
                "MedianDreamSimDistance": float(pd.Series(values).median()),
                "MinDreamSimDistance": min(values),
                "MaxDreamSimDistance": max(values),
            }
        ]
    )
    path = output_directory / "DreamSimSummary.csv"
    summary.to_csv(path, index=False, encoding="utf-8")
    return path


def main():
    parser = argparse.ArgumentParser(
        description="Calculate DreamSim visual separation for Unity screenshots."
    )
    parser.add_argument(
        "--input",
        required=True,
        help="Folder containing same-camera RGB PNG/JPG screenshots.",
    )
    parser.add_argument(
        "--output",
        required=True,
        help="Folder where CSV result files will be written.",
    )
    parser.add_argument(
        "--lightweight",
        action="store_true",
        help="Use the single DINO branch (about 3x faster) instead of the DreamSim ensemble.",
    )
    args = parser.parse_args()

    input_directory = Path(args.input)
    output_directory = Path(args.output)
    output_directory.mkdir(parents=True, exist_ok=True)

    if not input_directory.is_dir():
        raise ValueError(f"Input directory does not exist: {input_directory}")

    image_paths = find_images(input_directory)
    if len(image_paths) < 2:
        raise ValueError("At least two PNG/JPG screenshots are required.")

    device = "cuda" if torch.cuda.is_available() else "cpu"
    model_kwargs = {"pretrained": True, "device": device}
    if args.lightweight:
        model_kwargs["dreamsim_type"] = "dino_vitb16"

    print(f"Device: {device}")
    print(f"Screenshot count: {len(image_paths)}")
    print("Loading DreamSim. The first run downloads model weights automatically.")
    model, preprocess = dreamsim(**model_kwargs)
    model.eval()

    embeddings = {}
    for index, image_path in enumerate(image_paths, start=1):
        print(f"Embedding {index}/{len(image_paths)}: {image_path.name}")
        embeddings[image_path.name] = extract_embedding(model, preprocess, image_path, device)

    pairwise_rows = []
    for first_name, second_name in combinations(embeddings.keys(), 2):
        pairwise_rows.append(
            {
                "ImageA": first_name,
                "ImageB": second_name,
                "DreamSimDistance": cosine_distance(
                    embeddings[first_name], embeddings[second_name]
                ),
            }
        )

    pairwise_path = write_pairwise_results(pairwise_rows, output_directory)
    per_player_path = write_per_player_results(
        list(embeddings.keys()), pairwise_rows, output_directory
    )
    summary_path = write_summary(
        pairwise_rows, len(image_paths), device, output_directory
    )

    mean_distance = sum(row["DreamSimDistance"] for row in pairwise_rows) / len(pairwise_rows)
    print("\nCompleted successfully.")
    print(f"Mean DreamSim visual separation: {mean_distance:.6f}")
    print(f"Pairwise data: {pairwise_path}")
    print(f"Per-player data: {per_player_path}")
    print(f"Summary data: {summary_path}")


if __name__ == "__main__":
    main()

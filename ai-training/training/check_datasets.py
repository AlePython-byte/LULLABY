"""Comprueba los tres conjuntos de una versión antes de entrenar (solo lee; no elige ni ajusta nada).

    ai-training/.venv/Scripts/python.exe ai-training/training/check_datasets.py --version 2

  - Semillas efectivas en común entre entrenamiento, prueba y confirmación: tiene que ser 0 (error si no).
  - Vectores de características idénticos entre conjuntos (niveles distintos con los mismos valores): informativo.
  - SHA-256 de cada CSV y meta.json, para comparar con una segunda generación (determinismo).
Escribe reports/data_checks.json (con --version 2, reports/v2/data_checks.json).
"""

from __future__ import annotations

import argparse
import sys

from common import (CONFIRM_FILES, DATASETS, REPORTS, TEST_FILES, TRAIN_FILES, DataError, add_version_argument,
                    feature_meta, identical_vectors, load_confirm, load_test, load_train, sha256, use_utf8_console,
                    write_json)


def main() -> int:
    use_utf8_console()
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    add_version_argument(parser)
    parser.parse_args()

    version, names = feature_meta()
    splits = {"train": load_train(), "test": load_test(), "confirm": load_confirm()}
    print(f"Características versión {version} ({len(names)}): " + ", ".join(f"{name} {len(split)} filas"
                                                                   for name, split in splits.items()))

    pairs = (("train", "test"), ("train", "confirm"), ("test", "confirm"))
    seeds = {name: set(split.seeds.tolist()) for name, split in splits.items()}
    overlap = {}
    for left, right in pairs:
        shared = len(seeds[left] & seeds[right])
        identical = identical_vectors(splits[right], splits[left])
        overlap[f"{left}_{right}"] = {"shared_effective_seeds": shared,
                                      f"{right}_rows_with_identical_vector_in_{left}": identical}
        print(f"  {left} / {right}: semillas efectivas en común {shared}; filas de {right} con vector idéntico a "
              f"alguno de {left}: {identical}")

    files = {}
    for group in (TRAIN_FILES, TEST_FILES, CONFIRM_FILES):
        features, labels, meta = group
        for file in (features, labels, meta, labels.replace(".csv", ".meta.json")):
            files[file] = sha256(DATASETS / file)

    write_json(REPORTS / "data_checks.json", {
        "feature_set_version": version,
        "rows": {name: len(split) for name, split in splits.items()},
        "effective_seed_ranges": {name: [int(split.seeds.min()), int(split.seeds.max())] for name, split in splits.items()},
        "overlap": overlap,
        "sha256": files,
    })
    print(f"Escrito {REPORTS.name}/data_checks.json.")

    if any(entry["shared_effective_seeds"] for entry in overlap.values()):
        raise DataError("hay semillas efectivas compartidas entre conjuntos.")
    return 0


if __name__ == "__main__":
    sys.exit(main())

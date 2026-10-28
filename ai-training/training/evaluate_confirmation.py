"""Evaluación de CONFIRMACIÓN del modelo final, sobre semillas que no se usaron ni para entrenar ni para la prueba.

    ai-training/.venv/Scripts/python.exe ai-training/training/evaluate_confirmation.py

Se ejecuta UNA sola vez y no se ajusta nada con su resultado. Evalúa exactamente el JSON que usa el juego
(resources/ai/difficulty_v1.json), no un reajuste en Python, con el evaluador de export_model.py que solo lee el JSON.
Antes comprueba que no hay solapamiento por semilla efectiva con el entrenamiento ni con la prueba (error si lo hay) y
cuenta los vectores de características idénticos (informativo). Deja un marcador (reports/.confirmation_evaluated) y se
niega a repetirse sin --force.
Escribe reports/confirmation_metrics.json.
"""

from __future__ import annotations

import argparse
import sys

from common import (MAIN, REPORTS, ROOT, SECONDARY, TARGETS, DataError, environment, feature_meta, load_confirm,
                    load_test, load_train, read_json, regression_metrics, sha256, use_utf8_console, write_json)
from export_model import predict_from_json

MARKER = REPORTS / ".confirmation_evaluated"
GAME_MODEL = ROOT.parent / "resources" / "ai" / "difficulty_v1.json"


def identical_vectors(left, right) -> int:
    import numpy as np
    known = {tuple(row) for row in np.round(right.X, 6)}
    return int(sum(tuple(row) in known for row in np.round(left.X, 6)))


def main() -> int:
    use_utf8_console()
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--force", action="store_true", help="repetir aunque la confirmación ya se haya evaluado")
    args = parser.parse_args()

    if MARKER.exists() and not args.force:
        print("La confirmación ya se evaluó (marcador reports/.confirmation_evaluated). No se repite: no se ajusta nada con ella.")
        return 2

    version, names = feature_meta()
    train, test, confirm = load_train(), load_test(), load_confirm()

    seeds = {"train": set(train.seeds.tolist()), "test": set(test.seeds.tolist()), "confirm": set(confirm.seeds.tolist())}
    overlap = {
        "confirm_and_train_effective_seeds": len(seeds["confirm"] & seeds["train"]),
        "confirm_and_test_effective_seeds": len(seeds["confirm"] & seeds["test"]),
        "confirm_rows_with_identical_vector_in_train": identical_vectors(confirm, train),
        "confirm_rows_with_identical_vector_in_test": identical_vectors(confirm, test),
    }
    print(f"Solapamiento por semilla efectiva: con entrenamiento {overlap['confirm_and_train_effective_seeds']}, "
          f"con prueba {overlap['confirm_and_test_effective_seeds']} "
          f"({len(confirm)} filas de confirmación; semillas {int(confirm.seeds.min())}..{int(confirm.seeds.max())}).")
    print(f"Vectores de características idénticos (informativo): con entrenamiento "
          f"{overlap['confirm_rows_with_identical_vector_in_train']}, con prueba {overlap['confirm_rows_with_identical_vector_in_test']}.")
    if overlap["confirm_and_train_effective_seeds"] or overlap["confirm_and_test_effective_seeds"]:
        raise DataError("el conjunto de confirmación comparte semillas efectivas con entrenamiento o prueba.")

    model_document = read_json(GAME_MODEL)
    if model_document["feature_names"] != names or model_document["feature_set_version"] != version:
        raise DataError("el modelo del juego no coincide con las características del meta.")

    results, test_reference = {}, read_json(REPORTS / "metrics.json")["test"]
    print(f"\nModelo evaluado: {GAME_MODEL.relative_to(ROOT.parent)} (sha256 {sha256(GAME_MODEL)[:16]}…), {len(confirm)} niveles.")
    print(f"{'etiqueta':<20}{'conjunto':<16}{'MAE':>8}{'RMSE':>8}{'R²':>8}{'Spearman':>10}")
    for target in TARGETS:
        predicted = predict_from_json(model_document["models"][target], confirm.X)
        metrics = regression_metrics(confirm.y[target], predicted)
        results[target] = {"metrics": metrics, "test_metrics_for_reference": test_reference[target]["chosen"]["metrics"]}
        for label, row in (("confirmación", metrics), ("prueba (ref.)", test_reference[target]["chosen"]["metrics"])):
            print(f"{target:<20}{label:<16}{row['mae']:8.3f}{row['rmse']:8.3f}{row['r2']:8.3f}{row['spearman']:10.3f}")

    write_json(REPORTS / "confirmation_metrics.json", {
        "environment": environment(),
        "model_file": str(GAME_MODEL.relative_to(ROOT.parent)).replace("\\", "/"),
        "model_sha256": sha256(GAME_MODEL),
        "n_confirm": len(confirm),
        "effective_seed_range": [int(confirm.seeds.min()), int(confirm.seeds.max())],
        "overlap": overlap,
        "main_target": MAIN,
        "secondary_target": SECONDARY,
        "results": results,
        "note": "Evaluación única del modelo ya fijado; no se ajustó nada con este resultado.",
    })
    MARKER.write_text(f"Confirmación evaluada con el modelo sha256={sha256(GAME_MODEL)}\n", encoding="utf-8")
    print("\nEscrito reports/confirmation_metrics.json.")
    return 0


if __name__ == "__main__":
    sys.exit(main())

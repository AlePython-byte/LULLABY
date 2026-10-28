"""Exporta los modelos elegidos a JSON para C# y guarda las predicciones de Python sobre las filas de prueba.

    ai-training/.venv/Scripts/python.exe ai-training/training/export_model.py

Escribe:
  - models/difficulty_v1.json: versión de las características, sus nombres en orden y un modelo por etiqueta.
  - reports/predictions_test_python.csv: predicciones de Python sobre las 1000 filas de prueba, con 17 cifras
    significativas, para compararlas luego con las de C#.

Formato de cada modelo (campo "kind"):
  linear / ridge   prediction = intercept + sum_i coefficients[i] * (x[i] - normalization.mean[i]) / normalization.scale[i]
                   (también se dan raw_coefficients y raw_intercept, ya sin normalización: intercept + sum raw[i] * x[i]).
  gradient_boosting  prediction = init + learning_rate * sum_t leaf_value(tree_t, x)
  random_forest      prediction = mean_t leaf_value(tree_t, x)
Cada árbol son arrays paralelos por nodo: feature (-1 en las hojas), threshold, left, right (-1 en las hojas) y value.
Se baja por el hijo izquierdo si x[feature] <= threshold.

IMPORTANTE para C#: scikit-learn compara los árboles en float32. El JSON lo indica en tree_feature_dtype: hay que
convertir cada característica a float (32 bits) antes de compararla con el umbral (que es double). Los modelos lineales
usan double. Antes de escribir, este script reproduce las predicciones leyendo solo el JSON (sin scikit-learn) y exige
una diferencia máxima menor que TOLERANCE respecto a scikit-learn.
"""

from __future__ import annotations

import sys

import numpy as np

from common import (MAIN, MODELS, RANDOM_STATE, REPORTS, TARGETS, feature_meta, load_test, load_train, read_json,
                    use_utf8_console, write_json)
from models import make_estimator

TOLERANCE = 1e-9
FORMAT_VERSION = 1


def tree_to_dict(tree) -> dict:
    leaf = tree.children_left == -1
    return {
        "feature": np.where(leaf, -1, tree.feature).tolist(),
        "threshold": np.where(leaf, 0.0, tree.threshold).tolist(),
        "left": tree.children_left.tolist(),
        "right": tree.children_right.tolist(),
        "value": tree.value[:, 0, 0].tolist(),
    }


def export_model(family: str, model, names: list[str]) -> dict:
    if family in ("linear", "ridge"):
        scaler, linear = model.named_steps["scale"], model.named_steps["model"]
        raw = linear.coef_ / scaler.scale_
        return {
            "kind": family,
            "normalization": {"mean": scaler.mean_.tolist(), "scale": scaler.scale_.tolist()},
            "coefficients": linear.coef_.tolist(),
            "intercept": float(linear.intercept_),
            "raw_coefficients": raw.tolist(),
            "raw_intercept": float(linear.intercept_ - np.sum(linear.coef_ * scaler.mean_ / scaler.scale_)),
        }

    if family == "gradient_boosting":
        return {
            "kind": family,
            "init": float(model.init_.constant_[0, 0]),
            "learning_rate": float(model.learning_rate),
            "trees": [tree_to_dict(stage[0].tree_) for stage in model.estimators_],
        }

    if family == "random_forest":
        return {"kind": family, "trees": [tree_to_dict(estimator.tree_) for estimator in model.estimators_]}

    raise ValueError(family)


def apply_tree(tree: dict, X32: np.ndarray) -> np.ndarray:
    feature = np.array(tree["feature"])
    threshold = np.array(tree["threshold"], dtype=np.float64)
    left, right = np.array(tree["left"]), np.array(tree["right"])
    value = np.array(tree["value"], dtype=np.float64)

    rows = np.arange(len(X32))
    node = np.zeros(len(X32), dtype=np.int64)
    while True:
        internal = feature[node] >= 0
        if not internal.any():
            return value[node]
        column = np.where(internal, feature[node], 0)
        go_left = X32[rows, column] <= threshold[node]  # float32 frente a float64, como hace scikit-learn
        node = np.where(internal, np.where(go_left, left[node], right[node]), node)


def predict_from_json(model: dict, X: np.ndarray) -> np.ndarray:
    """Evaluador independiente de scikit-learn: lo único que ve es el JSON."""
    kind = model["kind"]
    if kind in ("linear", "ridge"):
        mean = np.array(model["normalization"]["mean"])
        scale = np.array(model["normalization"]["scale"])
        return model["intercept"] + ((X - mean) / scale) @ np.array(model["coefficients"])

    X32 = X.astype(np.float32)
    if kind == "gradient_boosting":
        total = np.full(len(X), model["init"], dtype=np.float64)
        for tree in model["trees"]:
            total += model["learning_rate"] * apply_tree(tree, X32)
        return total

    if kind == "random_forest":
        return np.mean([apply_tree(tree, X32) for tree in model["trees"]], axis=0)

    raise ValueError(kind)


def verify_tree_export(train) -> None:
    """Los modelos elegidos hoy son lineales, así que el código de árboles no se ejercita con ellos. Esta comprobación
    ajusta un Gradient Boosting y un Random Forest pequeños (no se exportan), los exporta a JSON y exige que el
    evaluador que solo lee el JSON reproduzca a scikit-learn sobre las filas de entrenamiento."""
    small = {
        "gradient_boosting": {"n_estimators": 60, "max_depth": 3, "learning_rate": 0.1},
        "random_forest": {"n_estimators": 25, "min_samples_leaf": 20, "max_features": 0.7},
    }
    for family, params in small.items():
        model = make_estimator(family, **params).fit(train.X, train.y[MAIN])
        difference = float(np.max(np.abs(predict_from_json(export_model(family, model, train.names), train.X)
                                         - model.predict(train.X))))
        print(f"  comprobación de árboles ({family}, {params}): máxima diferencia = {difference:.2e}")
        if difference >= TOLERANCE:
            raise SystemExit(f"La exportación de {family} no reproduce a scikit-learn (diferencia {difference:.2e}).")


def main() -> int:
    use_utf8_console()
    version, names = feature_meta()
    cv = read_json(REPORTS / "cv_results.json")
    if cv["feature_names"] != names or cv["feature_set_version"] != version:
        raise SystemExit("cv_results.json se generó con otras características: vuelve a ejecutar train.py.")

    train, test = load_train(), load_test()
    verify_tree_export(train)
    exported, sklearn_predictions, json_predictions = {}, {}, {}
    for target in TARGETS:
        selection = cv["targets"][target]["selection"]
        family = selection["chosen_family"]
        model = make_estimator(family, **selection["chosen_params"]).fit(train.X, train.y[target])

        exported[target] = {**export_model(family, model, names), "params": selection["chosen_params"]}
        sklearn_predictions[target] = model.predict(test.X)
        json_predictions[target] = predict_from_json(exported[target], test.X)

        difference = float(np.max(np.abs(json_predictions[target] - sklearn_predictions[target])))
        print(f"{target}: {family}, máxima diferencia JSON frente a scikit-learn en prueba = {difference:.2e}")
        if difference >= TOLERANCE:
            raise SystemExit(f"La exportación de {target} no reproduce a scikit-learn (diferencia {difference:.2e}).")

    path = MODELS / "difficulty_v1.json"
    write_json(path, {
        "format_version": FORMAT_VERSION,
        "feature_set_version": version,
        "feature_names": names,
        "tree_feature_dtype": "float32",
        "trained_on": {"file": "levels_v1.csv + levels_v1_labels.csv", "rows": len(train), "random_state": RANDOM_STATE},
        "main_target": MAIN,
        "models": exported,
    }, digits=15, indent=None)
    print(f"Escrito {path.relative_to(path.parents[2])} ({path.stat().st_size / 1024:.1f} KiB).")

    lines = ["effective_seed," + ",".join(f"pred_{target}" for target in TARGETS)]
    for index, seed in enumerate(test.seeds):
        lines.append(f"{seed}," + ",".join(format(float(sklearn_predictions[target][index]), ".17g") for target in TARGETS))
    predictions_path = REPORTS / "predictions_test_python.csv"
    predictions_path.write_bytes(("\n".join(lines) + "\n").encode("utf-8"))
    print(f"Escrito {predictions_path.relative_to(predictions_path.parents[2])} ({len(test)} filas, 17 cifras significativas).")
    return 0


if __name__ == "__main__":
    sys.exit(main())

"""Entrena y compara modelos de dificultad con validación cruzada anidada. Solo usa el conjunto de ENTRENAMIENTO.

    ai-training/.venv/Scripts/python.exe ai-training/training/train.py
    ai-training/.venv/Scripts/python.exe ai-training/training/train.py --version 2   # IA v2: levels_v2*, reports/v2/

Para cada etiqueta (damage_total_mean y time_total_mean) y cada familia (media, lineal, Ridge, Gradient Boosting,
Random Forest):
  1. Validación cruzada ANIDADA: 5 particiones externas; en cada una, los hiperparámetros se eligen con una
     validación interna de 3 particiones sobre las otras 4/5. Las métricas "oof" salen de juntar las predicciones de
     las 5 particiones externas, así que no están infladas por haber elegido hiperparámetros sobre los mismos datos.
  2. Hiperparámetros finales: búsqueda de 5 particiones sobre las 5000 filas (la puntuación de esa búsqueda es
     optimista y se guarda solo como referencia).
  3. Elección del modelo final por etiqueta con la regla del error estándar: entre los modelos cuyo RMSE de
     validación cruzada está a menos de 1 error estándar (de las 5 particiones) del mejor, gana el más simple según
     el orden de models.FAMILIES.
Escribe reports/cv_results.json y reports/cv_model_comparison.png.
"""

from __future__ import annotations

import sys
import time
import warnings

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np
from joblib import Parallel, delayed
from sklearn.model_selection import GridSearchCV, KFold

from common import (RANDOM_STATE, REPORTS, TARGETS, environment, feature_meta, load_train, regression_metrics,
                    use_utf8_console, write_json)
from models import BASELINE, FAMILIES, GRIDS, LABELS, complexity, make_estimator

OUTER_FOLDS = 5
INNER_FOLDS = 3
FINAL_FOLDS = 5
SCORING = "neg_root_mean_squared_error"


def inner_fit(family: str, X: np.ndarray, y: np.ndarray):
    """Ajusta una familia eligiendo hiperparámetros con validación interna de INNER_FOLDS particiones."""
    estimator = make_estimator(family)
    grid = GRIDS[family]
    if not grid:
        return estimator.fit(X, y), {}

    search = GridSearchCV(estimator, grid, cv=KFold(INNER_FOLDS, shuffle=True, random_state=RANDOM_STATE),
                          scoring=SCORING, n_jobs=1, refit=True)
    search.fit(X, y)
    return search.best_estimator_, search.best_params_


def outer_task(family: str, X_train: np.ndarray, y_train: np.ndarray, X_test: np.ndarray):
    model, params = inner_fit(family, X_train, y_train)
    return model.predict(X_test), params


def choose(models_cv: dict) -> dict:
    """Regla del error estándar sobre el RMSE de validación cruzada, con preferencia por la familia más simple."""
    candidates = {family: models_cv[family] for family in FAMILIES}
    best = min(candidates, key=lambda family: candidates[family]["oof"]["rmse"])
    threshold = candidates[best]["oof"]["rmse"] + candidates[best]["rmse_se"]
    within = [family for family in FAMILIES if candidates[family]["oof"]["rmse"] <= threshold]
    chosen = within[0]  # FAMILIES va de simple a compleja

    if chosen == best:
        reason = (f"{LABELS[chosen]} tiene el menor RMSE de validación cruzada ({candidates[best]['oof']['rmse']:.4f}) "
                  f"y ningún modelo más simple queda dentro de 1 error estándar ({candidates[best]['rmse_se']:.4f}).")
    else:
        reason = (f"El mejor por RMSE es {LABELS[best]} ({candidates[best]['oof']['rmse']:.4f}), pero {LABELS[chosen]} "
                  f"({candidates[chosen]['oof']['rmse']:.4f}) queda a menos de 1 error estándar "
                  f"({candidates[best]['rmse_se']:.4f}) y es más simple.")

    return {
        "best_family_by_rmse": best,
        "rmse_threshold": threshold,
        "within_one_standard_error": within,
        "chosen_family": chosen,
        "chosen_params": candidates[chosen]["final_params"],
        "reason": reason,
    }


def plot_comparison(results: dict) -> None:
    fig, axes = plt.subplots(1, 2, figsize=(11, 4.2))
    for axis, target in zip(axes, TARGETS):
        families = (BASELINE,) + FAMILIES
        rmse = [results[target]["models"][family]["oof"]["rmse"] for family in families]
        error = [results[target]["models"][family]["rmse_se"] for family in families]
        chosen = results[target]["selection"]["chosen_family"]
        colors = ["#c44e52" if family == chosen else "#8c9aa8" for family in families]
        axis.bar([LABELS[family] for family in families], rmse, yerr=error, color=colors, capsize=4)
        axis.set_title(f"{target}\nRMSE en validación cruzada anidada (rojo = elegido)")
        axis.set_ylabel("RMSE (barra de error = 1 error estándar)")
        axis.tick_params(axis="x", rotation=25)
        for position, value in enumerate(rmse):
            axis.text(position, value, f"{value:.2f}", ha="center", va="bottom", fontsize=8)
    fig.tight_layout()
    fig.savefig(REPORTS / "cv_model_comparison.png", dpi=130)
    plt.close(fig)


def main() -> int:
    use_utf8_console()
    started = time.time()
    warnings.filterwarnings("ignore", category=UserWarning)
    version, names = feature_meta()
    train = load_train()
    REPORTS.mkdir(parents=True, exist_ok=True)
    print(f"Entrenamiento: {len(train)} niveles × {len(names)} características (versión {version}).")

    outer = list(KFold(OUTER_FOLDS, shuffle=True, random_state=RANDOM_STATE).split(train.X))
    families = (BASELINE,) + FAMILIES

    print(f"Validación cruzada anidada ({OUTER_FOLDS} externas × {INNER_FOLDS} internas) en paralelo...")
    tasks = [(target, family, fold) for target in TARGETS for family in families for fold in range(OUTER_FOLDS)]
    outputs = Parallel(n_jobs=-1)(
        delayed(outer_task)(family, train.X[outer[fold][0]], train.y[target][outer[fold][0]], train.X[outer[fold][1]])
        for target, family, fold in tasks)

    results: dict = {}
    for target in TARGETS:
        results[target] = {"models": {}}
        for family in families:
            predictions = np.zeros(len(train))
            fold_rmse, fold_params = [], []
            for fold in range(OUTER_FOLDS):
                prediction, params = outputs[tasks.index((target, family, fold))]
                test_index = outer[fold][1]
                predictions[test_index] = prediction
                fold_rmse.append(float(np.sqrt(np.mean((prediction - train.y[target][test_index]) ** 2))))
                fold_params.append(params)

            results[target]["models"][family] = {
                "oof": regression_metrics(train.y[target], predictions),
                "fold_rmse": fold_rmse,
                "rmse_se": float(np.std(fold_rmse, ddof=1) / np.sqrt(OUTER_FOLDS)),
                "params_per_outer_fold": fold_params,
            }

    print("Hiperparámetros finales (búsqueda de 5 particiones sobre todo el entrenamiento)...")
    for target in TARGETS:
        for family in FAMILIES:
            grid = GRIDS[family]
            if grid:
                search = GridSearchCV(make_estimator(family), grid,
                                      cv=KFold(FINAL_FOLDS, shuffle=True, random_state=RANDOM_STATE),
                                      scoring=SCORING, n_jobs=-1, refit=True)
                search.fit(train.X, train.y[target])
                params, search_rmse, model = search.best_params_, -search.best_score_, search.best_estimator_
            else:
                params, search_rmse = {}, None
                model = make_estimator(family).fit(train.X, train.y[target])

            entry = results[target]["models"][family]
            entry["final_params"] = params
            entry["final_search_rmse_optimistic"] = search_rmse
            entry["complexity"] = complexity(model, family, len(names))

        results[target]["selection"] = choose(results[target]["models"])

    write_json(REPORTS / "cv_results.json", {
        "random_state": RANDOM_STATE,
        "feature_set_version": version,
        "feature_names": names,
        "n_train": len(train),
        "outer_folds": OUTER_FOLDS,
        "inner_folds": INNER_FOLDS,
        "final_search_folds": FINAL_FOLDS,
        "scoring": SCORING,
        "environment": environment(),
        "targets": results,
    })
    plot_comparison({target: results[target] for target in TARGETS})

    print(f"\n{'etiqueta':<20}{'modelo':<26}{'MAE':>8}{'RMSE':>8}{'R²':>8}{'Spearman':>10}{'RMSE ±EE':>11}{'parámetros':>12}")
    for target in TARGETS:
        for family in families:
            entry = results[target]["models"][family]
            oof = entry["oof"]
            marker = " <-- elegido" if family == results[target]["selection"]["chosen_family"] else ""
            parameters = entry.get("complexity", {}).get("parameters", 1)
            rho = "  nan" if oof["spearman"] != oof["spearman"] else f"{oof['spearman']:.3f}"
            print(f"{target:<20}{LABELS[family]:<26}{oof['mae']:8.3f}{oof['rmse']:8.3f}{oof['r2']:8.3f}{rho:>10}"
                  f"{entry['rmse_se']:11.3f}{parameters:>12}{marker}")
        print(f"  -> {results[target]['selection']['reason']}")

    written = REPORTS.relative_to(REPORTS.parents[1] if REPORTS.name != "reports" else REPORTS.parent).as_posix()
    print(f"\nTiempo: {time.time() - started:.0f} s. Escrito {written}/cv_results.json y cv_model_comparison.png.")
    return 0


if __name__ == "__main__":
    sys.exit(main())

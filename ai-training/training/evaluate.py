"""Evaluación FINAL sobre el conjunto de prueba. Se ejecuta una sola vez, con los modelos que train.py ya eligió.

    ai-training/.venv/Scripts/python.exe ai-training/training/evaluate.py
    ai-training/.venv/Scripts/python.exe ai-training/training/evaluate.py --force   # solo para reproducir el MISMO resultado

El conjunto de prueba no se usa para elegir nada: este script lee la elección de reports/cv_results.json, reajusta
ese modelo con las 5000 filas de entrenamiento y mide en las 1000 filas de prueba. Deja un marcador
(reports/.test_evaluated) y se niega a repetirse sin --force, para no acabar ajustando a la prueba.

Métrica del director: 2000 grupos de 30 niveles de la prueba (semilla fija). Para cada objetivo de dificultad (percentiles
25, 50 y 75 de damage_total_mean del ENTRENAMIENTO) cada estrategia elige un nivel del grupo y se mide
|dificultad REAL del nivel elegido - objetivo|:
  - azar: nivel al azar (se calcula exacto: la media de los 30 errores del grupo, sin muestreo);
  - modelo (mejor): el nivel cuya predicción está más cerca del objetivo;
  - modelo con azar entre los 5 mejores: uno al azar entre los 5 niveles con la predicción más cercana (la estrategia
    del juego, que debe dar niveles distintos cada vez; también exacto: la media de los 5 errores);
  - oráculo: el nivel cuya dificultad real está más cerca del objetivo.
Los 2000 grupos salen de los mismos 1000 niveles, así que no son independientes entre sí.

Escribe reports/metrics.json y las gráficas en reports/.
"""

from __future__ import annotations

import argparse
import sys

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np
from sklearn.inspection import permutation_importance

from common import (DATASETS, MAIN, RANDOM_STATE, REPORTS, SECONDARY, TARGETS, check_no_overlap, clean, environment,
                    feature_meta, load_test, load_train, read_json, regression_metrics, sha256, use_utf8_console,
                    write_json)
from models import LABELS, make_estimator

MARKER = REPORTS / ".test_evaluated"
GROUPS = 2000
GROUP_SIZE = 30
TOP_K = 5
RANKS_REPORTED = 10
BOOTSTRAP_RESAMPLES = 200
PERCENTILES = (25, 50, 75)
IMPORTANCE_REPEATS = 20
BIN_EDGES = (0.2, 0.4, 0.6, 0.8)

# Estimación APROXIMADA (no un límite exacto) de la parte de la varianza que las 14 características pueden explicar, a
# partir de la varianza dentro de los grupos de niveles con vector de características idéntico en el entrenamiento.
R2_CEILING = {MAIN: 0.73, SECONDARY: 0.96}

STRATEGIES = ("random", "model_top5_random", "model_best", "oracle")
STRATEGY_LABELS = {
    "random": "azar",
    "model_top5_random": f"modelo, azar entre los {TOP_K} mejores (juego)",
    "model_best": "modelo, siempre el mejor",
    "oracle": "oráculo",
}


def fit_final(family: str, params: dict, X: np.ndarray, y: np.ndarray):
    return make_estimator(family, **params).fit(X, y)


def director_metrics(predicted: np.ndarray, real: np.ndarray, targets: dict[str, float]) -> dict:
    rng = np.random.default_rng(RANDOM_STATE)
    indices = np.stack([rng.choice(len(real), size=GROUP_SIZE, replace=False) for _ in range(GROUPS)])
    group_pred = predicted[indices]
    group_real = real[indices]

    per_target: dict[str, dict] = {}
    for name, target in targets.items():
        error_real = np.abs(group_real - target)
        order = np.argsort(np.abs(group_pred - target), axis=1, kind="stable")
        ranked = np.take_along_axis(error_real, order, axis=1)
        per_group = {
            "random": error_real.mean(axis=1),
            "model_top5_random": ranked[:, :TOP_K].mean(axis=1),
            "model_best": ranked[:, 0],
            "oracle": error_real.min(axis=1),
        }
        per_target[name] = {
            "target": target,
            "mean_error": {key: float(value.mean()) for key, value in per_group.items()},
            # Error medio del nivel que ocupa la posición 1, 2, ... del ranking del modelo (diagnóstico).
            "mean_error_by_model_rank": ranked[:, :RANKS_REPORTED].mean(axis=0).tolist(),
        }

    overall = {key: float(np.mean([per_target[name]["mean_error"][key] for name in targets])) for key in STRATEGIES}
    per_target["overall"] = {
        "mean_error": overall,
        "mean_error_by_model_rank": np.mean([per_target[name]["mean_error_by_model_rank"] for name in targets],
                                            axis=0).tolist(),
    }

    for entry in per_target.values():
        errors = entry["mean_error"]
        random_error, oracle_error = errors["random"], errors["oracle"]
        entry["reduction_vs_random"] = {
            key: 1.0 - errors[key] / random_error for key in STRATEGIES if key != "random"}
        entry["fraction_of_random_to_oracle_gap_closed"] = {
            key: (random_error - errors[key]) / (random_error - oracle_error) for key in STRATEGIES if key != "random"}
        entry["top5_loss_vs_best"] = {
            "absolute": errors["model_top5_random"] - errors["model_best"],
            "relative": errors["model_top5_random"] / errors["model_best"] - 1.0,
            "reduction_points_lost": entry["reduction_vs_random"]["model_best"]
            - entry["reduction_vs_random"]["model_top5_random"],
        }

    return {"groups": GROUPS, "group_size": GROUP_SIZE, "top_k": TOP_K, "seed": RANDOM_STATE,
            "targets_from": "percentiles del entrenamiento de " + MAIN, "results": per_target}


def director_bootstrap(predicted: np.ndarray, real: np.ndarray, targets: dict[str, float]) -> dict:
    """Variabilidad por el muestreo de los 1000 niveles de prueba: se remuestrean los NIVELES con reemplazo y se repite
    toda la métrica del director. Recoge la dependencia entre los 2000 grupos (que comparten niveles)."""
    rng = np.random.default_rng(RANDOM_STATE + 1)
    rows = []
    for _ in range(BOOTSTRAP_RESAMPLES):
        picked = rng.integers(0, len(real), size=len(real))
        overall = director_metrics(predicted[picked], real[picked], targets)["results"]["overall"]
        rows.append([overall["reduction_vs_random"]["model_best"], overall["reduction_vs_random"]["model_top5_random"],
                     overall["top5_loss_vs_best"]["absolute"]])

    columns = np.array(rows)
    names = ("reduction_model_best", "reduction_model_top5_random", "top5_minus_best_error")
    return {"resamples": BOOTSTRAP_RESAMPLES, "interval": "percentiles 2.5 / 50 / 97.5",
            **{name: np.percentile(columns[:, index], [2.5, 50, 97.5]).tolist() for index, name in enumerate(names)}}


def error_by_difficulty(real: np.ndarray, predicted: np.ndarray, baseline: np.ndarray, edges: np.ndarray) -> list[dict]:
    bins = np.digitize(real, edges)
    rows = []
    for index in range(len(edges) + 1):
        mask = bins == index
        if not mask.any():
            continue
        rows.append({
            "bin": index,
            "real_from": float(edges[index - 1]) if index > 0 else float(real.min()),
            "real_to": float(edges[index]) if index < len(edges) else float(real.max()),
            "n": int(mask.sum()),
            "mae_model": float(np.mean(np.abs(predicted[mask] - real[mask]))),
            "bias_model": float(np.mean(predicted[mask] - real[mask])),
            "mae_mean_baseline": float(np.mean(np.abs(baseline[mask] - real[mask]))),
        })
    return rows


def plot_predicted_vs_actual(data: dict) -> None:
    fig, axes = plt.subplots(1, 2, figsize=(11, 5))
    for axis, target in zip(axes, TARGETS):
        real, predicted, metrics, family = data[target]
        axis.scatter(real, predicted, s=8, alpha=0.35, color="#4c72b0")
        low, high = min(real.min(), predicted.min()), max(real.max(), predicted.max())
        axis.plot([low, high], [low, high], color="#c44e52", linewidth=1.2, label="predicción perfecta")
        axis.set_xlabel(f"{target} real (medido por el bot)")
        axis.set_ylabel(f"{target} predicho")
        axis.set_title(f"{LABELS[family]} en prueba (1000 niveles)\n"
                       f"R²={metrics['r2']:.3f} (techo aprox. {R2_CEILING[target]})  "
                       f"MAE={metrics['mae']:.2f}  Spearman={metrics['spearman']:.3f}", fontsize=10)
        axis.legend(loc="upper left")
    fig.tight_layout()
    fig.savefig(REPORTS / "predicted_vs_actual.png", dpi=130)
    plt.close(fig)


def plot_error_by_difficulty(rows_by_target: dict) -> None:
    fig, axes = plt.subplots(2, 2, figsize=(11, 7), sharex="col")
    for column, target in enumerate(TARGETS):
        rows = rows_by_target[target]
        labels = [f"{row['real_from']:.0f}–{row['real_to']:.0f}\n(n={row['n']})" for row in rows]
        positions = np.arange(len(rows))
        axes[0, column].bar(positions - 0.2, [row["mae_model"] for row in rows], 0.4, label="modelo elegido", color="#4c72b0")
        axes[0, column].bar(positions + 0.2, [row["mae_mean_baseline"] for row in rows], 0.4, label="predecir la media", color="#b0b8c0")
        axes[0, column].set_ylabel("MAE")
        axes[0, column].set_title(f"{target}: error por tramo de dificultad REAL")
        axes[0, column].set_ylim(0, axes[0, column].get_ylim()[1] * 1.3)  # hueco para la leyenda
        axes[0, column].legend(loc="upper center", ncol=2)
        axes[1, column].bar(positions, [row["bias_model"] for row in rows], 0.5, color="#dd8452")
        axes[1, column].axhline(0, color="black", linewidth=0.8)
        axes[1, column].set_ylabel("sesgo medio (predicho − real)")
        axes[1, column].set_xticks(positions, labels)
        axes[1, column].set_xlabel(f"tramo de {target} real (quintiles del entrenamiento)")
    fig.tight_layout()
    fig.savefig(REPORTS / "error_by_difficulty.png", dpi=130)
    plt.close(fig)


def plot_director(director: dict) -> None:
    names = [*(f"objetivo p{percentile}" for percentile in PERCENTILES), "promedio"]
    keys = [f"p{percentile}" for percentile in PERCENTILES] + ["overall"]
    colors = {"random": "#b0b8c0", "model_top5_random": "#4c72b0", "model_best": "#55a868", "oracle": "#c44e52"}

    fig, axis = plt.subplots(figsize=(11, 5))
    width = 0.2
    positions = np.arange(len(keys))
    for offset, strategy in enumerate(STRATEGIES):
        values = [director["results"][key]["mean_error"][strategy] for key in keys]
        bars = axis.bar(positions + (offset - 1.5) * width, values, width, label=STRATEGY_LABELS[strategy], color=colors[strategy])
        for bar, value in zip(bars, values):
            axis.text(bar.get_x() + bar.get_width() / 2, value, f"{value:.2f}", ha="center", va="bottom", fontsize=8)
    overall = director["results"]["overall"]
    axis.set_xticks(positions, names)
    axis.set_ylabel(f"error medio |{MAIN} real del nivel elegido − objetivo|")
    axis.set_title(f"Director: elegir 1 nivel de {GROUP_SIZE} ({GROUPS} grupos de la prueba)\n"
                   f"reducción del error frente al azar (promedio): modelo, mejor {overall['reduction_vs_random']['model_best']:.0%}; "
                   f"azar entre los {TOP_K} mejores {overall['reduction_vs_random']['model_top5_random']:.0%}; "
                   f"oráculo {overall['reduction_vs_random']['oracle']:.0%}", fontsize=10)
    axis.set_ylim(0, axis.get_ylim()[1] * 1.3)  # hueco para la leyenda
    axis.legend(loc="upper center", ncol=2)
    fig.tight_layout()
    fig.savefig(REPORTS / "director_strategies.png", dpi=130)
    plt.close(fig)


def plot_importance(importance: dict, names: list[str]) -> None:
    fig, axes = plt.subplots(1, 2, figsize=(12, 5.5))
    for axis, target in zip(axes, TARGETS):
        means = np.array([importance[target][name]["mean"] for name in names])
        stds = np.array([importance[target][name]["std"] for name in names])
        order = np.argsort(means)
        axis.barh([names[i] for i in order], means[order], xerr=stds[order], color="#4c72b0")
        axis.set_xlabel("caída de R² al permutar la característica (prueba)")
        axis.set_title(target)
    fig.tight_layout()
    fig.savefig(REPORTS / "permutation_importance.png", dpi=130)
    plt.close(fig)


def main() -> int:
    use_utf8_console()
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--force", action="store_true", help="repetir aunque el conjunto de prueba ya se haya usado")
    args = parser.parse_args()

    if MARKER.exists() and not args.force:
        print("El conjunto de prueba ya se evaluó (marcador reports/.test_evaluated).\n"
              "Repetirlo para elegir otro modelo sería ajustar a la prueba. Usa --force solo para reproducir el mismo resultado.")
        return 2

    cv_path = REPORTS / "cv_results.json"
    cv = read_json(cv_path)
    version, names = feature_meta()
    if cv["feature_names"] != names or cv["feature_set_version"] != version:
        raise SystemExit("cv_results.json se generó con otras características: vuelve a ejecutar train.py.")

    train, test = load_train(), load_test()
    overlap = check_no_overlap(train, test)
    print(f"Datos: {len(train)} entrenamiento, {len(test)} prueba; semillas efectivas en común: 0; "
          f"filas de prueba con vector idéntico a alguno de entrenamiento: "
          f"{overlap['test_rows_with_identical_train_feature_vector']}.")

    selection = {target: cv["targets"][target]["selection"] for target in TARGETS}
    final_models, baselines, predictions, baseline_predictions = {}, {}, {}, {}
    for target in TARGETS:
        chosen = selection[target]
        final_models[target] = fit_final(chosen["chosen_family"], chosen["chosen_params"], train.X, train.y[target])
        predictions[target] = final_models[target].predict(test.X)
        baselines[target] = {family: fit_final(family, {}, train.X, train.y[target]) for family in ("mean", "linear")}
        baseline_predictions[target] = {family: model.predict(test.X) for family, model in baselines[target].items()}

    test_results: dict = {}
    print(f"\n{'etiqueta':<20}{'modelo':<26}{'MAE':>8}{'RMSE':>8}{'R²':>8}{'Spearman':>10}")
    for target in TARGETS:
        family = selection[target]["chosen_family"]
        entry = {
            "chosen": {"family": family, "params": selection[target]["chosen_params"],
                       "metrics": regression_metrics(test.y[target], predictions[target])},
            "baseline_mean": regression_metrics(test.y[target], baseline_predictions[target]["mean"]),
            "baseline_linear": regression_metrics(test.y[target], baseline_predictions[target]["linear"]),
            "r2_ceiling_estimate": R2_CEILING[target],
            "cv_r2_of_chosen": cv["targets"][target]["models"][family]["oof"]["r2"],
        }
        test_results[target] = entry
        for label, metrics in (("media (base)", entry["baseline_mean"]), ("lineal (base)", entry["baseline_linear"]),
                               (LABELS[family] + " (elegido)", entry["chosen"]["metrics"])):
            rho = "nan" if metrics["spearman"] != metrics["spearman"] else f"{metrics['spearman']:.3f}"
            print(f"{target:<20}{label:<26}{metrics['mae']:8.3f}{metrics['rmse']:8.3f}{metrics['r2']:8.3f}{rho:>10}")

    edges = {target: np.quantile(train.y[target], BIN_EDGES) for target in TARGETS}
    rows = {target: error_by_difficulty(test.y[target], predictions[target], baseline_predictions[target]["mean"],
                                        edges[target]) for target in TARGETS}
    for target in TARGETS:
        test_results[target]["error_by_difficulty"] = rows[target]

    director_targets = {f"p{percentile}": float(np.percentile(train.y[MAIN], percentile)) for percentile in PERCENTILES}
    director = director_metrics(predictions[MAIN], test.y[MAIN], director_targets)

    print("\nDirector (error medio |dificultad real − objetivo|; menos es mejor):")
    print(f"{'objetivo':<22}" + "".join(f"{STRATEGY_LABELS[key][:22]:>24}" for key in STRATEGIES))
    for key, entry in director["results"].items():
        label = key if key == "overall" else f"{key} (= {entry['target']:.1f})"
        print(f"{label:<22}" + "".join(f"{entry['mean_error'][strategy]:24.3f}" for strategy in STRATEGIES))
    overall = director["results"]["overall"]
    print(f"Reducción del error frente al azar (promedio): modelo mejor {overall['reduction_vs_random']['model_best']:.1%}, "
          f"azar entre los {TOP_K} mejores {overall['reduction_vs_random']['model_top5_random']:.1%}, "
          f"oráculo {overall['reduction_vs_random']['oracle']:.1%}.")
    loss = overall["top5_loss_vs_best"]
    print(f"Coste de la variedad (top-{TOP_K} al azar menos siempre el mejor): {loss['absolute']:+.3f} de error "
          f"({loss['relative']:+.1%}); {loss['reduction_points_lost'] * 100:+.1f} puntos de reducción perdidos "
          f"(negativo = la variedad MEJORA el resultado).")
    print("Error real medio según la posición en el ranking del modelo (1 = la predicción más cercana al objetivo):")
    print("  " + "  ".join(f"#{rank + 1}: {value:.3f}" for rank, value in enumerate(overall["mean_error_by_model_rank"])))

    director["bootstrap_over_test_levels"] = director_bootstrap(predictions[MAIN], test.y[MAIN], director_targets)
    boot = director["bootstrap_over_test_levels"]
    print(f"Bootstrap sobre los niveles de prueba ({boot['resamples']} remuestreos; percentiles 2.5 / 50 / 97.5):")
    print("  reducción frente al azar, modelo mejor:        " + " / ".join(f"{v:.1%}" for v in boot["reduction_model_best"]))
    print("  reducción frente al azar, azar entre top-5:    " + " / ".join(f"{v:.1%}" for v in boot["reduction_model_top5_random"]))
    print("  error(top-5 al azar) − error(siempre el mejor): " + " / ".join(f"{v:+.3f}" for v in boot["top5_minus_best_error"]))

    importance: dict = {}
    for target in TARGETS:
        result = permutation_importance(final_models[target], test.X, test.y[target], scoring="r2",
                                        n_repeats=IMPORTANCE_REPEATS, random_state=RANDOM_STATE, n_jobs=1)
        importance[target] = {name: {"mean": float(result.importances_mean[i]), "std": float(result.importances_std[i])}
                              for i, name in enumerate(names)}
        top = sorted(importance[target].items(), key=lambda item: -item[1]["mean"])[:4]
        print(f"Importancia por permutación ({target}), 4 mayores: "
              + ", ".join(f"{name} {value['mean']:.3f}" for name, value in top))

    plot_predicted_vs_actual({target: (test.y[target], predictions[target], test_results[target]["chosen"]["metrics"],
                                       selection[target]["chosen_family"]) for target in TARGETS})
    plot_error_by_difficulty(rows)
    plot_director(director)
    plot_importance(importance, names)

    write_json(REPORTS / "metrics.json", {
        "random_state": RANDOM_STATE,
        "feature_set_version": version,
        "feature_names": names,
        "environment": environment(),
        "data_checks": {"n_train": len(train), "n_test": len(test), **overlap},
        "selected_models": selection,
        "cross_validation": cv["targets"],
        "test": test_results,
        "director": director,
        "permutation_importance": importance,
        "r2_ceiling_note": "Estimación aproximada, no un límite exacto: sale de la varianza dentro de los grupos de "
                           "niveles con vector de características idéntico en el entrenamiento (47 grupos).",
    })
    MARKER.write_text(f"Conjunto de prueba evaluado con cv_results.json sha256={sha256(cv_path)}\n", encoding="utf-8")
    print("\nEscrito reports/metrics.json y las gráficas (predicted_vs_actual, error_by_difficulty, "
          "director_strategies, permutation_importance).")
    return 0


if __name__ == "__main__":
    sys.exit(main())

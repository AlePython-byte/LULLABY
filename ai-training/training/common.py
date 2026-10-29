"""Utilidades compartidas por los scripts de entrenamiento (solo desarrollo; el juego no depende de esto).

Reglas:
  - Todo es reproducible: RANDOM_STATE fijo y ningún resultado depende del orden de diccionarios o de hilos.
  - Los datos se validan contra los .meta.json: los nombres y el orden de las características tienen que coincidir
    exactamente con feature_names, o se aborta con DataError.
  - load_test() solo se llama desde evaluate.py y export_model.py. train.py no lo importa ni lee archivos de prueba:
    el conjunto de prueba no interviene en ninguna elección.

Versiones de la IA: todos los scripts aceptan --version N (por defecto 1).
  - 1: generador v1 y 14 características. Archivos levels_v1*, informes en reports/, modelo difficulty_v1.json. Es lo de
       siempre: sin --version nada cambia.
  - 2: generador v2 (tipos de enemigo) y 22 características. Archivos levels_v2*, informes en reports/v2/, modelo
       difficulty_v2.json; marcadores de evaluación propios en reports/v2/.
"""

from __future__ import annotations

import hashlib
import json
import math
import platform
import sys
from dataclasses import dataclass
from pathlib import Path

import numpy as np
import pandas as pd
import scipy
import sklearn
from scipy import stats
from sklearn.metrics import r2_score

RANDOM_STATE = 42
KNOWN_VERSIONS = (1, 2)


def _version_from_argv(argv: list[str]) -> int:
    """--version N o --version=N en la línea de comandos (por defecto 1). Se lee al importar, así cada script elige
    archivos, informes y modelo antes de nada."""
    for index, argument in enumerate(argv):
        value = None
        if argument.startswith("--version="):
            value = argument.split("=", 1)[1]
        elif argument == "--version" and index + 1 < len(argv):
            value = argv[index + 1]
        if value is not None:
            version = int(value)
            if version not in KNOWN_VERSIONS:
                raise SystemExit(f"--version {version} desconocida (válidas: {KNOWN_VERSIONS}).")
            return version
    return 1


VERSION = _version_from_argv(sys.argv)
TAG = f"v{VERSION}"

ROOT = Path(__file__).resolve().parents[1]  # ai-training/
DATASETS = ROOT / "datasets"
REPORTS = ROOT / "reports" if VERSION == 1 else ROOT / "reports" / TAG
MODELS = ROOT / "models"
MODEL_FILE = f"difficulty_{TAG}.json"
GAME_MODEL = ROOT.parent / "resources" / "ai" / MODEL_FILE

MAIN = "damage_total_mean"
SECONDARY = "time_total_mean"
TARGETS = (MAIN, SECONDARY)

JOIN_KEY = "effective_seed"
FEATURE_META = f"levels_{TAG}.meta.json"
LABEL_META = f"levels_{TAG}_labels.meta.json"

# (CSV de características, CSV de etiquetas, meta de características)
TRAIN_FILES = (f"levels_{TAG}.csv", f"levels_{TAG}_labels.csv", f"levels_{TAG}.meta.json")
TEST_FILES = (f"levels_{TAG}_test.csv", f"levels_{TAG}_test_labels.csv", f"levels_{TAG}_test.meta.json")
CONFIRM_FILES = (f"levels_{TAG}_confirm.csv", f"levels_{TAG}_confirm_labels.csv", f"levels_{TAG}_confirm.meta.json")


def add_version_argument(parser) -> None:
    """Para los scripts con argparse: --version ya lo leyó common al importarse; aquí solo se declara."""
    parser.add_argument("--version", type=int, default=1, choices=KNOWN_VERSIONS,
                        help="versión de la IA: 1 (generador v1, 14 características) o 2 (generador v2, 22)")


def use_utf8_console() -> None:
    """La consola de Windows usa cp1252 por defecto y rompe los acentos."""
    import sys
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8")


class DataError(RuntimeError):
    """Los datos no coinciden con lo que declaran sus meta.json."""


@dataclass
class Split:
    name: str
    seeds: np.ndarray
    names: list[str]
    X: np.ndarray
    y: dict[str, np.ndarray]

    def __len__(self) -> int:
        return len(self.seeds)


def read_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def feature_meta() -> tuple[int, list[str]]:
    """(versión de LevelMetrics, nombres de las características en orden)."""
    meta = read_json(DATASETS / FEATURE_META)
    return int(meta["metrics_version"]), list(meta["feature_names"])


def _load(files: tuple[str, str, str], split_name: str) -> Split:
    features_file, labels_file, meta_file = files
    version, names = feature_meta()

    meta = read_json(DATASETS / meta_file)
    if list(meta["feature_names"]) != names or int(meta["metrics_version"]) != version:
        raise DataError(f"{meta_file}: feature_names o metrics_version distintos de {FEATURE_META}.")

    features = pd.read_csv(DATASETS / features_file)
    labels = pd.read_csv(DATASETS / labels_file)

    expected = [JOIN_KEY] + names
    if list(features.columns) != expected:
        raise DataError(f"{features_file}: las columnas no coinciden en nombre y orden con feature_names.\n"
                        f"  esperadas: {expected}\n  encontradas: {list(features.columns)}")
    missing = [column for column in (JOIN_KEY, *TARGETS) if column not in labels.columns]
    if missing:
        raise DataError(f"{labels_file}: faltan columnas {missing}.")
    if len(features) != int(meta["level_count"]):
        raise DataError(f"{features_file}: {len(features)} filas pero el meta declara {meta['level_count']}.")
    if len(features) != len(labels) or not np.array_equal(features[JOIN_KEY].to_numpy(), labels[JOIN_KEY].to_numpy()):
        raise DataError(f"{features_file} y {labels_file} no tienen las mismas semillas efectivas en el mismo orden.")
    if features[JOIN_KEY].duplicated().any():
        raise DataError(f"{features_file}: hay semillas efectivas repetidas.")

    merged = features.merge(labels[[JOIN_KEY, *TARGETS]], on=JOIN_KEY, how="inner", validate="one_to_one")
    if len(merged) != len(features):
        raise DataError(f"{split_name}: la unión por {JOIN_KEY} perdió filas.")

    values = merged[names + list(TARGETS)].to_numpy(dtype=float)
    if not np.isfinite(values).all():
        raise DataError(f"{split_name}: hay valores no finitos.")

    return Split(
        name=split_name,
        seeds=merged[JOIN_KEY].to_numpy(),
        names=names,
        X=merged[names].to_numpy(dtype=float),
        y={target: merged[target].to_numpy(dtype=float) for target in TARGETS},
    )


def load_train() -> Split:
    return _load(TRAIN_FILES, "entrenamiento")


def load_test() -> Split:
    """Solo para evaluate.py y export_model.py."""
    return _load(TEST_FILES, "prueba")


def load_confirm() -> Split:
    """Solo para evaluate_confirmation.py (conjunto de confirmación, semillas 200001..201000)."""
    return _load(CONFIRM_FILES, "confirmación")


def identical_vectors(left: Split, right: Split) -> int:
    """Filas de left cuyo vector de características (a 6 decimales) aparece en right."""
    known = {tuple(row) for row in np.round(right.X, 6)}
    return int(sum(tuple(row) in known for row in np.round(left.X, 6)))


def check_no_overlap(train: Split, test: Split) -> dict:
    """Error si comparten semillas efectivas; devuelve cuántos vectores de características son idénticos."""
    shared = set(train.seeds.tolist()) & set(test.seeds.tolist())
    if shared:
        raise DataError(f"entrenamiento y prueba comparten {len(shared)} semillas efectivas.")

    train_vectors = {tuple(row) for row in np.round(train.X, 6)}
    identical = sum(tuple(row) in train_vectors for row in np.round(test.X, 6))
    return {"shared_effective_seeds": 0, "test_rows_with_identical_train_feature_vector": int(identical)}


def regression_metrics(y_true: np.ndarray, y_pred: np.ndarray) -> dict:
    y_true = np.asarray(y_true, dtype=float)
    y_pred = np.asarray(y_pred, dtype=float)
    error = y_pred - y_true
    # np.std de un vector constante puede no ser exactamente 0 por redondeo; el rango sí lo es.
    rho = float("nan") if np.ptp(y_pred) == 0.0 else float(stats.spearmanr(y_true, y_pred).statistic)
    return {
        "mae": float(np.mean(np.abs(error))),
        "rmse": float(np.sqrt(np.mean(error ** 2))),
        "r2": float(r2_score(y_true, y_pred)),
        "spearman": rho,
    }


def clean(obj, digits: int = 8):
    """Convierte a tipos de Python y redondea los reales (NaN/inf pasan a null; -0 pasa a 0)."""
    if isinstance(obj, dict):
        return {str(key): clean(value, digits) for key, value in obj.items()}
    if isinstance(obj, (list, tuple)):
        return [clean(value, digits) for value in obj]
    if isinstance(obj, np.ndarray):
        return clean(obj.tolist(), digits)
    if isinstance(obj, (np.floating, float)):
        value = float(obj)
        return round(value, digits) + 0.0 if math.isfinite(value) else None
    if isinstance(obj, np.integer):
        return int(obj)
    if isinstance(obj, np.bool_):
        return bool(obj)
    return obj


def write_json(path: Path, obj, digits: int = 8, indent: int | None = 2) -> None:
    """JSON UTF-8 sin BOM, saltos de línea LF, claves ordenadas, sin timestamps (salida determinista)."""
    separators = None if indent is not None else (",", ":")
    text = json.dumps(clean(obj, digits), indent=indent, sort_keys=True, ensure_ascii=False,
                      allow_nan=False, separators=separators)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes((text + "\n").encode("utf-8"))


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def environment() -> dict:
    return {
        "python": platform.python_version(),
        "numpy": np.__version__,
        "pandas": pd.__version__,
        "scipy": scipy.__version__,
        "scikit_learn": sklearn.__version__,
    }

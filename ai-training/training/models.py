"""Familias de modelos, rejillas de hiperparámetros y conteo de complejidad.

Las familias se ordenan de más simple a más compleja (FAMILIES); la elección final usa ese orden con la regla del
error estándar. Todos los estimadores usan n_jobs=1 y random_state fijo: el paralelismo se hace por fuera (entre
particiones y candidatos), de modo que los resultados no dependen del número de hilos.
"""

from __future__ import annotations

from sklearn.dummy import DummyRegressor
from sklearn.ensemble import GradientBoostingRegressor, RandomForestRegressor
from sklearn.linear_model import LinearRegression, Ridge
from sklearn.pipeline import Pipeline
from sklearn.preprocessing import StandardScaler

from common import RANDOM_STATE

BASELINE = "mean"

# De más simple a más compleja (por número de parámetros exportables).
FAMILIES = ("linear", "ridge", "gradient_boosting", "random_forest")

LABELS = {
    "mean": "media del entrenamiento",
    "linear": "regresión lineal",
    "ridge": "Ridge",
    "gradient_boosting": "Gradient Boosting",
    "random_forest": "Random Forest",
}

GRIDS = {
    "mean": {},
    "linear": {},
    "ridge": {"model__alpha": [0.01, 0.1, 1.0, 10.0, 100.0, 1000.0]},
    "gradient_boosting": {
        "learning_rate": [0.03, 0.1],
        "max_depth": [2, 3, 4],
        "n_estimators": [150, 400],
    },
    "random_forest": {
        "min_samples_leaf": [3, 10, 25],
        "max_features": [0.4, 0.7, 1.0],
    },
}


def make_estimator(family: str, **params):
    if family == BASELINE:
        estimator = DummyRegressor(strategy="mean")
    elif family == "linear":
        estimator = Pipeline([("scale", StandardScaler()), ("model", LinearRegression())])
    elif family == "ridge":
        estimator = Pipeline([("scale", StandardScaler()), ("model", Ridge())])
    elif family == "gradient_boosting":
        estimator = GradientBoostingRegressor(subsample=0.8, min_samples_leaf=10, random_state=RANDOM_STATE)
    elif family == "random_forest":
        estimator = RandomForestRegressor(n_estimators=200, n_jobs=1, random_state=RANDOM_STATE)
    else:
        raise ValueError(f"familia desconocida: {family}")

    return estimator.set_params(**params) if params else estimator


def complexity(model, family: str, n_features: int) -> dict:
    """Cuántos números hay que exportar: coeficientes (lineales) o nodos de árbol."""
    if family in ("linear", "ridge"):
        # coeficientes + intercepto + media y escala de cada característica
        return {"parameters": n_features + 1 + 2 * n_features}

    if family == "gradient_boosting":
        trees = [stage[0].tree_ for stage in model.estimators_]
    elif family == "random_forest":
        trees = [estimator.tree_ for estimator in model.estimators_]
    else:
        return {"parameters": 1}

    return {"parameters": int(sum(tree.node_count for tree in trees)), "trees": len(trees)}

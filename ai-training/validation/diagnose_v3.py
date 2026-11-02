"""Diagnostico exploratorio de ruido y residuos sobre resultados v3 ya medidos.

No modifica el protocolo, no excluye observaciones, no entrena y no ejecuta partidas.
Las repeticiones usan semillas distintas del bot: no son repeticiones de una misma
secuencia de entradas. El ICC y su proxy de techo son aproximaciones de Pearson,
no cotas demostradas de Spearman sobre medias de tres partidas.
"""
from __future__ import annotations

import argparse
import hashlib
import itertools
import json
import math
import random
import zipfile
from collections import Counter
from pathlib import Path
from statistics import mean, median

from v3_fidelity import percentile, spearman


def load_data(archive_path):
    with zipfile.ZipFile(archive_path) as archive:
        index = json.loads(archive.read("audit/index.json"))
        for name, expected in index["files"].items():
            if hashlib.sha256(archive.read(name)).hexdigest() != expected:
                raise ValueError("Archivo alterado: " + name)
        status = json.loads(archive.read("status.json"))
        for name, expected in status["data_sha256"].items():
            if hashlib.sha256(archive.read(name)).hexdigest() != expected:
                raise ValueError("Cierre alterado: " + name)
        if status["completed_pairs"] != 90:
            raise ValueError("Se necesitan los 90 pares originales, sin exclusiones")
        levels = []
        for seed in range(600001, 600031):
            level = json.loads(archive.read(f"level_{seed}.json"))
            level["results"] = {}
            for environment in ("sim", "real"):
                results = []
                for run in range(3):
                    row = json.loads(archive.read(f"{environment}_{seed}_{run}.json"))
                    if (row["requested_seed"], row["effective_seed"], row["run"]) != (seed, level["effective_seed"], run):
                        raise ValueError("Emparejamiento incorrecto")
                    results.append(row["result"])
                level["results"][environment] = results
            levels.append(level)
        return levels, archive.read("audit/runtime.log").decode("utf-8"), json.loads(archive.read("manifest.json"))


def icc(matrix):
    """ICC(1,1) e ICC(1,k), modelo aditivo de errores independientes, sin truncar."""
    n, k = len(matrix), len(matrix[0])
    means = [mean(row) for row in matrix]
    grand = mean(means)
    between = k * sum((value - grand) ** 2 for value in means) / (n - 1)
    within = sum((value - means[i]) ** 2 for i, row in enumerate(matrix) for value in row) / (n * (k - 1))
    return {"single": (between - within) / (between + (k - 1) * within) if between + (k - 1) * within else None,
            "mean_of_three": (between - within) / between if between else None,
            "between_mean_square": between, "within_mean_square": within}


def noise(levels, resamples=2000, seed=20261031):
    result = {}
    for environment in ("sim", "real"):
        matrix = [[row["damage_total"] for row in level["results"][environment]] for level in levels]
        comparisons = []
        for a, b in itertools.combinations(range(3), 2):
            x, y = [row[a] for row in matrix], [row[b] for row in matrix]
            rng = random.Random(seed)
            samples = []
            for _ in range(resamples):
                indices = [rng.randrange(len(matrix)) for _ in matrix]
                value = spearman([x[i] for i in indices], [y[i] for i in indices])
                if value is not None:
                    samples.append(value)
            comparisons.append({"runs": [a, b], "spearman": spearman(x, y),
                                "ci95": [percentile(samples, 0.025), percentile(samples, 0.975)]})
        held_out = [{"held_out_run": run,
                     "spearman": spearman([row[run] for row in matrix],
                                          [mean(row[i] for i in range(3) if i != run) for row in matrix])}
                    for run in range(3)]
        result[environment] = {"run_pairs": comparisons, "mean_pairwise_spearman": mean(c["spearman"] for c in comparisons),
                               "one_run_vs_other_two": held_out, "icc_approximation": icc(matrix),
                               "damage_runs": matrix}
    reliabilities = [result[e]["icc_approximation"]["mean_of_three"] for e in ("sim", "real")]
    result["independent_noise_pearson_proxy"] = (math.sqrt(math.prod(reliabilities))
                                                if all(r is not None and 0 <= r <= 1 for r in reliabilities) else None)
    result["proxy_limitations"] = "Modelo aditivo, errores independientes, misma dificultad latente; proxy de Pearson, no techo estricto de Spearman. Solo tres repeticiones por nivel."
    return result


def composition(level):
    rooms = level["map"]["rooms"]
    combat = [r for r in rooms if r["spawns"]]
    counts = Counter(s["kind"] for r in rooms for s in r["spawns"])
    sizes = Counter(f"{r['size'][0]:g}x{r['size'][1]:g}" for r in combat)
    return {"room_count": len(rooms), "combat_room_count": len(combat), "total_enemies": sum(counts.values()),
            "enemy_counts": dict(sorted(counts.items())), "combat_room_sizes": dict(sorted(sizes.items())),
            "combat_area_mean": mean(r["size"][0] * r["size"][1] for r in combat),
            "combat_area_min": min(r["size"][0] * r["size"][1] for r in combat),
            "combat_area_max": max(r["size"][0] * r["size"][1] for r in combat),
            "flyer_room_count": sum("with_flyer" in r["categories"] for r in rooms),
            "digger_room_count": sum("with_digger" in r["categories"] for r in rooms)}


def residuals(levels):
    rows = []
    for level in levels:
        sim = [r["damage_total"] for r in level["results"]["sim"]]
        real = [r["damage_total"] for r in level["results"]["real"]]
        rooms = []
        for room in level["map"]["rooms"]:
            cell = room["cell"]
            damage = {e: [next(v["damage"] for v in result["visits"] if v["cell"] == cell)
                          for result in level["results"][e]] for e in ("sim", "real")}
            if sum(damage["sim"]) != sum(damage["real"]):
                rooms.append({"cell": cell, "template_id": room["template_id"], "size": room["size"],
                              "kinds": dict(Counter(s["kind"] for s in room["spawns"])),
                              "mean_sim": mean(damage["sim"]), "mean_real": mean(damage["real"]),
                              "delta": mean(damage["sim"]) - mean(damage["real"])})
        rows.append({"requested_seed": level["requested_seed"], "effective_seed": level["effective_seed"],
                     "damage_runs_sim": sim, "damage_runs_real": real, "mean_sim": mean(sim), "mean_real": mean(real),
                     "delta": (sum(sim) - sum(real)) / 3, "composition": composition(level),
                     "room_residuals": sorted(rooms, key=lambda r: (-abs(r["delta"]), r["cell"]))})
    # Orden exacto del numerador entero; evita desempates por redondeo binario de medias.
    rows.sort(key=lambda r: (-abs(sum(r["damage_runs_sim"]) - sum(r["damage_runs_real"])), r["requested_seed"]))
    common = {}
    for name, selected in (("top_five", rows[:5]), ("all_thirty", rows)):
        common[name] = {"levels": len(selected),
                        "median_rooms": median(r["composition"]["room_count"] for r in selected),
                        "median_total_enemies": median(r["composition"]["total_enemies"] for r in selected),
                        "median_combat_area": median(r["composition"]["combat_area_mean"] for r in selected),
                        "levels_with_all_six": sum(len(r["composition"]["enemy_counts"]) == 6 for r in selected),
                        "median_flyers": median(r["composition"]["enemy_counts"].get("Flyer", 0) for r in selected),
                        "median_diggers": median(r["composition"]["enemy_counts"].get("Digger", 0) for r in selected)}
    return {"top_five": rows[:5], "all_levels": rows, "descriptive_comparison": common,
            "warning": "Seleccion posterior por residuo; coincidencias descriptivas, no causas ni evidencia de importancia de features."}


def log_diagnostic(log):
    lines = log.splitlines()
    exceptions = []
    last_pair = None
    for i, line in enumerate(lines):
        if line.startswith("Par "):
            last_pair = line
        if line.startswith("ERROR:"):
            j = i + 1
            while j < len(lines) and not lines[j].startswith(("ERROR:", "Par ", "Depuración")):
                j += 1
            exceptions.append({"line": i + 1, "header": line, "last_completed_pair": last_pair,
                               "block": "\n".join(lines[i:j])})
    return {"gchandle_lines": [{"line": i + 1, "text": l} for i, l in enumerate(lines) if "gchandle" in l.lower()],
            "illegal_instruction_lines": [{"line": i + 1, "text": l} for i, l in enumerate(lines) if "illegal instruction" in l.lower()],
            "errors": exceptions}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    levels, log, manifest = load_data(args.archive)
    payload = {"diagnostic_only": True, "input_sha256": hashlib.sha256(args.archive.read_bytes()).hexdigest(),
               "protocol_sha256": manifest["protocol_sha256"], "levels": len(levels), "repetitions": 3,
               "noise": noise(levels), "residuals": residuals(levels), "log": log_diagnostic(log)}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("x", encoding="utf-8", newline="\n") as stream:
        json.dump(payload, stream, indent=2, ensure_ascii=False, allow_nan=False)
        stream.write("\n")
    print(json.dumps({"output": str(args.output), "noise": {e: payload["noise"][e]["run_pairs"] for e in ("sim", "real")},
                      "icc": {e: payload["noise"][e]["icc_approximation"] for e in ("sim", "real")},
                      "proxy": payload["noise"]["independent_noise_pearson_proxy"],
                      "top_five": [{k: r[k] for k in ("requested_seed", "mean_sim", "mean_real", "delta", "composition")}
                                   for r in payload["residuals"]["top_five"]]}, ensure_ascii=True))


if __name__ == "__main__":
    main()

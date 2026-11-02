"""Curva diagnostica de medias prefijadas 3,5,10; bootstrap pareado por nivel.

No cambia criterios ni decide aceptacion. Solo procesa un lote nuevo completo de
30 x 10, sin elegir combinaciones de repeticiones por su correlacion.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import random
from pathlib import Path
from statistics import mean

from diagnose_v3 import load_data
from v3_fidelity import PROTOCOL_FILE, PROTOCOL_SHA256, percentile, spearman, validate_result

ROOT = Path(__file__).resolve().parents[2]
PLAN_FILE = ROOT / "ai-training/protocols/v3_repetition_curve_plan.json"


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def curve_statistics(data, plan):
    seeds = sorted(data)
    prefixes = plan["mean_prefixes"]
    labels = plan["statistics"]["labels"]
    series = {(k, label): ([mean(r[label] for r in data[s]["sim"][:k]) for s in seeds],
                           [mean(r[label] for r in data[s]["real"][:k]) for s in seeds])
              for k in prefixes for label in labels}
    points = {key: spearman(*value) for key, value in series.items()}
    samples = {key: [] for key in series}
    differences = {(label, high, low): [] for label in labels for high, low in ((5, 3), (10, 3), (10, 5))}
    rng = random.Random(plan["statistics"]["bootstrap_seed"])
    for _ in range(plan["statistics"]["bootstrap_resamples"]):
        indices = [rng.randrange(len(seeds)) for _ in seeds]
        values = {}
        for key, (x, y) in series.items():
            value = spearman([x[i] for i in indices], [y[i] for i in indices])
            values[key] = value
            if value is not None:
                samples[key].append(value)
        for (label, high, low), sample in differences.items():
            if values[(high, label)] is not None and values[(low, label)] is not None:
                sample.append(values[(high, label)] - values[(low, label)])
    tail = (1 - plan["statistics"]["confidence"]) / 2

    def interval(sample):
        return {"low": percentile(sample, tail), "high": percentile(sample, 1 - tail),
                "valid_resamples": len(sample), "undefined_resamples": plan["statistics"]["bootstrap_resamples"] - len(sample),
                "unit": "level", "method": "paired_percentile_bootstrap"}

    rows = []
    for k in prefixes:
        rows.append({"repetitions": k, **{label: {"spearman": points[(k, label)],
                                                 "ci95": interval(samples[(k, label)]),
                                                 "mean_sim": mean(series[(k, label)][0]),
                                                 "mean_real": mean(series[(k, label)][1])} for label in labels}})
    deltas = []
    for label, high, low in differences:
        high_point, low_point = points[(high, label)], points[(low, label)]
        deltas.append({"label": label, "prefix_difference": f"{high}-{low}",
                       "delta_spearman": high_point - low_point if high_point is not None and low_point is not None else None,
                       "ci95": interval(differences[(label, high, low)])})
    damage10 = points[(10, "damage_total")]
    return {"diagnostic_only": True, "levels": len(seeds), "curve": rows, "paired_differences": deltas,
            "damage_at_10_reaches_target": damage10 is not None and damage10 >= plan["decision_rule"]["damage_spearman_at_10_min"],
            "not_an_acceptance_verdict": True}


def load_curve(folder, plan):
    manifest = json.loads((folder / "manifest.json").read_text(encoding="utf-8"))
    status = json.loads((folder / "status.json").read_text(encoding="utf-8"))
    if (manifest["plan"] != plan or manifest["plan_sha256"] != digest(PLAN_FILE)
            or manifest["protocol_sha256"] != PROTOCOL_SHA256 or manifest["worker_count"] != 1
            or manifest["concurrent_environments"] or not manifest["diagnostic_only"]
            or manifest["mode"] != "development_repetition_curve_diagnostic"):
        raise ValueError("Plan, protocolo o ejecucion incompatibles")
    if status["status"] != "repetition_curve_complete_pending_analysis" or status["completed_pairs"] != 300 or (folder / "failure.json").exists():
        raise ValueError("Curva incompleta o abortada; no publicar una curva completa")
    required = {p.name for p in folder.glob("*.json") if p.name not in ("status.json", "analysis.json")}
    if set(status["data_sha256"]) != required:
        raise ValueError("Cierre incompleto")
    for name, expected in status["data_sha256"].items():
        if Path(name).name != name or digest(folder / name) != expected:
            raise ValueError("Artefacto alterado: " + name)
    data = {}
    for seed in range(plan["first_seed"], plan["first_seed"] + plan["level_count"]):
        level = json.loads((folder / f"level_{seed}.json").read_text(encoding="utf-8"))
        data[seed] = {env: [] for env in ("sim", "real")}
        for run in plan["run_indices"]:
            bot_seed = ((level["effective_seed"] & 0xFFFFFFFF) * 0x9E3779B97F4A7C15 + run + 1) & 0xFFFFFFFFFFFFFFFF
            for env in ("sim", "real"):
                row = json.loads((folder / f"{env}_{seed}_{run}.json").read_text(encoding="utf-8"))
                if (row["requested_seed"], row["effective_seed"], row["run"], row["bot_seed"]) != (seed, level["effective_seed"], run, str(bot_seed)):
                    raise ValueError("Par o semilla incoherente")
                validate_result(row["result"], level["map"])
                if row["result"]["timed_out"]:
                    raise ValueError("Timeout")
                data[seed][env].append(row["result"])
    return data, manifest, status


def original_comparison(data):
    archive = ROOT / "ai-training/reports/v3/development_20261031_01/raw_results.zip"
    original, _, _ = load_data(archive)
    old = {level["requested_seed"]: level["results"] for level in original}
    changed = []
    for seed in sorted(data):
        for env in ("sim", "real"):
            for run in range(3):
                if data[seed][env][run] != old[seed][env][run]:
                    changed.append({"seed": seed, "environment": env, "run": run,
                                    "old_damage": old[seed][env][run]["damage_total"], "new_damage": data[seed][env][run]["damage_total"],
                                    "old_time": old[seed][env][run]["time_total"], "new_time": data[seed][env][run]["time_total"]})
    old_points = {label: spearman([mean(old[s]["sim"][r][label] for r in range(3)) for s in sorted(old)],
                                 [mean(old[s]["real"][r][label] for r in range(3)) for s in sorted(old)])
                  for label in ("damage_total", "time_total")}
    return {"original_three": old_points, "original_archive_sha256": digest(archive),
            "changed_full_results_among_first_three": changed,
            "note": "Comparacion descriptiva; el original se conserva y no se sustituye por el prefijo nuevo."}


def log_issues(text):
    active = None
    issues = []
    for line_number, line in enumerate(text.splitlines(), 1):
        if line.startswith("CURVE_BEGIN "):
            active = line
        if line.startswith("ERROR:") or "illegal instruction" in line.lower():
            issues.append({"line": line_number, "text": line, "active_pair": active,
                           "class": "certificate_store" if "certificate store" in line.lower() else "runtime_error"})
    return {"issues": issues, "runtime_error_count": sum(i["class"] == "runtime_error" for i in issues),
            "gchandle_lines": sum("gchandle" in line.lower() for line in text.splitlines()),
            "illegal_instruction_lines": sum("illegal instruction" in line.lower() for line in text.splitlines())}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("folder", type=Path)
    parser.add_argument("--log-file", type=Path, required=True)
    args = parser.parse_args()
    if digest(PROTOCOL_FILE) != PROTOCOL_SHA256:
        raise SystemExit("El protocolo original fue modificado")
    plan = json.loads(PLAN_FILE.read_text(encoding="utf-8"))
    data, manifest, status = load_curve(args.folder, plan)
    result = curve_statistics(data, plan)
    result.update(plan_sha256=manifest["plan_sha256"], protocol_sha256=manifest["protocol_sha256"],
                  original_comparison=original_comparison(data),
                  runtime=log_issues(args.log_file.read_text(encoding="utf-8")),
                  wall_seconds=status["wall_seconds"], memory=status["memory"])
    with (args.folder / "analysis.json").open("x", encoding="utf-8", newline="\n") as stream:
        json.dump(result, stream, indent=2, ensure_ascii=False, allow_nan=False)
        stream.write("\n")
    print(json.dumps({k: result[k] for k in ("curve", "paired_differences", "damage_at_10_reaches_target", "runtime", "wall_seconds", "memory")}, ensure_ascii=True))


if __name__ == "__main__":
    main()

"""Analisis de fidelidad v3, con biblioteca estandar y criterios preregistrados.

No entrena modelos ni genera etiquetas. Un piloto o una ejecucion incompleta nunca
pueden aprobar la puerta estadistica. Los remuestreos conservan juntas las
repeticiones y salas de cada nivel.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import random
from collections import defaultdict
from pathlib import Path
from statistics import mean

ROOT = Path(__file__).resolve().parents[2]
PROTOCOL_FILE = ROOT / "ai-training/protocols/v3_fidelity.json"
PROTOCOL_SHA256 = "91d0239fad57636476455b66eb06d49a1326b731d6777b2a166fa36aafb1e220"
KINDS = ("Chaser", "Shooter", "Exploder", "Flyer", "Runner", "Digger")
SOURCES = ("contact", "projectile", "explosion")


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def ranks(values):
    order = sorted(range(len(values)), key=values.__getitem__)
    result = [0.0] * len(values)
    start = 0
    while start < len(order):
        end = start + 1
        while end < len(order) and values[order[end]] == values[order[start]]:
            end += 1
        for i in order[start:end]:
            result[i] = (start + end - 1) / 2 + 1
        start = end
    return result


def spearman(x, y):
    if len(x) < 2 or len(x) != len(y):
        return None
    x, y = ranks(x), ranks(y)
    mx, my = mean(x), mean(y)
    vx = sum((v - mx) ** 2 for v in x)
    vy = sum((v - my) ** 2 for v in y)
    if vx == 0 or vy == 0:
        return None
    return sum((a - mx) * (b - my) for a, b in zip(x, y)) / math.sqrt(vx * vy)


def percentile(values, p):
    if not values:
        return None
    values = sorted(values)
    pos = p * (len(values) - 1)
    lo, hi = math.floor(pos), math.ceil(pos)
    return values[lo] + (values[hi] - values[lo]) * (pos - lo)


def bias(sim, real):
    return (sim - real) / real if real > 0 else None


def bootstrap_bias(sim, real, uncertainty):
    random_state = random.Random(uncertainty["seed"])
    samples = []
    for _ in range(uncertainty["resamples"]):
        indices = [random_state.randrange(len(sim)) for _ in sim]
        value = bias(sum(sim[i] for i in indices), sum(real[i] for i in indices))
        if value is not None:
            samples.append(value)
    tail = (1 - uncertainty["confidence"]) / 2
    return {"low": percentile(samples, tail), "high": percentile(samples, 1 - tail),
            "valid_resamples": len(samples), "unit": "level"}


def evaluate(pairs, protocol, complete=False):
    """pairs: diccionarios con seed, run, map, sim y real; sin elegir ni excluir resultados."""
    by_level = defaultdict(list)
    for pair in pairs:
        by_level[pair["seed"]].append(pair)
    seeds = sorted(by_level)
    criteria = protocol["criteria"]
    uncertainty = protocol["uncertainty"]
    failures = []
    correlations = {}
    for label in ("damage_total", "time_total"):
        sim = [mean(p["sim"][label] for p in by_level[s]) for s in seeds]
        real = [mean(p["real"][label] for p in by_level[s]) for s in seeds]
        rho = spearman(sim, real)
        threshold = criteria["spearman"][label + "_mean_min"]
        correlations[label] = {"spearman": rho, "mean_sim": mean(sim) if sim else None,
                               "mean_real": mean(real) if real else None,
                               "bias": bias(sum(sim), sum(real))}
        if rho is None or rho < threshold:
            failures.append("spearman_" + label)

    sources = {}
    for index, source in enumerate(SOURCES):
        sim = [sum(p["sim"]["damage_by_source"][index] for p in by_level[s]) for s in seeds]
        real = [sum(p["real"]["damage_by_source"][index] for p in by_level[s]) for s in seeds]
        sim_hits = sum(p["sim"]["hits_by_source"][index] for p in pairs)
        real_hits = sum(p["real"]["hits_by_source"][index] for p in pairs)
        value = bias(sum(sim), sum(real))
        if source == "contact":
            passed = (real_hits >= criteria["contact"]["minimum_real_hits"] and value is not None
                      and abs(value) <= criteria["contact"]["absolute_relative_bias_max"])
            verdict = "pass" if passed else "fail"
        elif real_hits < criteria["projectile_and_explosion"]["minimum_real_hits_per_source"]:
            verdict = "insufficient_evidence"
        else:
            passed = value is not None and abs(value) <= criteria["projectile_and_explosion"]["absolute_relative_bias_max"]
            verdict = "pass" if passed else "fail"
        if verdict == "fail":
            failures.append("source_" + source)
        sources[source] = {"sim_damage": sum(sim), "real_damage": sum(real), "sim_hits": sim_hits,
                           "real_hits": real_hits, "bias": value, "verdict": verdict,
                           "ci": bootstrap_bias(sim, real, uncertainty) if seeds else None}

    compositions = {}
    for category in criteria["composition_time"]["groups"]:
        sums = {s: [0.0, 0.0, 0.0, 0.0, 0] for s in seeds}
        for pair in pairs:
            rooms = {tuple(r["cell"]): r for r in pair["map"]["rooms"]}
            real_visits = {tuple(v["cell"]): v for v in pair["real"]["visits"]}
            for sim_visit in pair["sim"]["visits"]:
                cell = tuple(sim_visit["cell"])
                real_visit = real_visits.get(cell)
                if category not in rooms[cell]["categories"]:
                    continue
                if not real_visit or sim_visit["clear_time"] < 0 or real_visit["clear_time"] < 0:
                    failures.append("unmatched_or_uncleared_visit")
                    continue
                row = sums[pair["seed"]]
                row[0] += sim_visit["clear_time"] - sim_visit["enter_time"]
                row[1] += real_visit["clear_time"] - real_visit["enter_time"]
                row[2] += sim_visit["damage"]
                row[3] += real_visit["damage"]
                row[4] += 1
        sim, real = [sums[s][0] for s in seeds], [sums[s][1] for s in seeds]
        value = bias(sum(sim), sum(real))
        count = sum(row[4] for row in sums.values())
        passed = count > 0 and value is not None and abs(value) <= criteria["composition_time"]["absolute_relative_bias_max"]
        if not passed:
            failures.append("composition_" + category)
        compositions[category] = {"room_runs": count, "sim_time_sum": sum(sim), "real_time_sum": sum(real),
                                  "time_bias": value, "sim_damage": sum(r[2] for r in sums.values()),
                                  "real_damage": sum(r[3] for r in sums.values()),
                                  "verdict": "pass" if passed else "fail",
                                  "time_bias_ci": bootstrap_bias(sim, real, uncertainty) if seeds else None}

    timeouts = {env: sum(p[env]["timed_out"] for p in pairs) for env in ("sim", "real")}
    if timeouts["sim"] > criteria["timeouts"]["simulator_max"] or timeouts["real"] > criteria["timeouts"]["real_game_max"]:
        failures.append("timeouts")
    contact_by_kind = {kind: {env: sum(v["contact_by_kind"][i] for p in pairs for v in p[env]["visits"])
                              for env in ("sim", "real")} for i, kind in enumerate(KINDS)}
    return {"complete_development": complete, "levels": len(seeds), "pairs": len(pairs),
            "verdict": ("pass" if not failures else "fail") if complete else "incomplete_not_a_quality_gate",
            "failures": sorted(set(failures)), "correlations": correlations, "sources": sources,
            "compositions": compositions, "contact_by_kind": contact_by_kind, "timeouts": timeouts}


def load_experiment(folder, protocol):
    manifest = json.loads((folder / "manifest.json").read_text(encoding="utf-8"))
    if manifest["protocol_sha256"] != PROTOCOL_SHA256 or manifest["protocol"] != protocol:
        raise ValueError("El manifiesto no corresponde al protocolo registrado")
    if manifest["worker_count"] != 1 or manifest["concurrent_environments"]:
        raise ValueError("La ejecucion requiere un solo trabajador sin entornos concurrentes")
    status_file = folder / "status.json"
    status = json.loads(status_file.read_text(encoding="utf-8")) if status_file.exists() else {}
    for name, expected in status.get("data_sha256", {}).items():
        if Path(name).name != name or digest(folder / name) != expected:
            raise ValueError("Artefacto modificado: " + name)
    if status:
        required = {p.name for p in folder.glob("*.json") if p.name not in ("status.json", "analysis.json")}
        if set(status.get("data_sha256", {})) != required:
            raise ValueError("El cierre no contiene los hashes de todos los artefactos")
    pairs = []
    seen = set()
    for real_file in sorted(folder.glob("real_*.json")):
        real = json.loads(real_file.read_text(encoding="utf-8"))
        seed, run = real["requested_seed"], real["run"]
        sim_file = folder / f"sim_{seed}_{run}.json"
        sim = json.loads(sim_file.read_text(encoding="utf-8"))
        level = json.loads((folder / f"level_{seed}.json").read_text(encoding="utf-8"))
        if real_file.name != f"real_{seed}_{run}.json" or (seed, run) in seen:
            raise ValueError("Par repetido o nombre inconsistente")
        seen.add((seed, run))
        expected_bot = ((level["effective_seed"] & 0xFFFFFFFF) * 0x9E3779B97F4A7C15 + run + 1) & 0xFFFFFFFFFFFFFFFF
        for row in (sim, real):
            if (row["requested_seed"], row["effective_seed"], row["run"], row["bot_seed"]) != (seed, level["effective_seed"], run, str(expected_bot)):
                raise ValueError("Semilla o repeticion no pareada")
            validate_result(row["result"], level["map"])
        pairs.append({"seed": seed, "run": run, "map": level["map"], "sim": sim["result"], "real": real["result"]})
    sample = protocol["samples"]["development"]
    expected = {(s, r) for s in range(sample["first_seed"], sample["first_seed"] + sample["count"])
                for r in range(protocol["runs_per_level_per_environment"])}
    complete = (manifest["mode"] == "development" and status.get("status") == "development_complete_pending_analysis"
                and seen == expected and not (folder / "failure.json").exists())
    if status and status.get("completed_pairs") != len(pairs):
        raise ValueError("El cierre no corresponde al numero de pares guardados")
    return pairs, manifest, status, complete


def validate_result(result, level_map):
    expected = {tuple(room["cell"]) for room in level_map["rooms"]}
    visits = [tuple(v["cell"]) for v in result["visits"]]
    if len(visits) != len(set(visits)):
        raise ValueError("Primera visita repetida")
    if not result["timed_out"] and (set(visits) != expected or any(v["clear_time"] < v["enter_time"] for v in result["visits"])):
        raise ValueError("Partida declarada completa con salas pendientes o sin visitar")
    values = [result[key] for key in ("damage_total", "damage_critical", "time_total", "time_to_boss")]
    if any(not math.isfinite(v) for v in values):
        raise ValueError("Medida no finita")
    if len(result["damage_by_source"]) != 3 or len(result["hits_by_source"]) != 3:
        raise ValueError("Fuentes incompletas")
    if not math.isclose(sum(result["damage_by_source"]), result["damage_total"], abs_tol=1e-4):
        raise ValueError("Dano por fuente no suma el total")
    if any(len(v["contact_by_kind"]) != 6 for v in result["visits"]):
        raise ValueError("Faltan tipos de contacto")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("folder", type=Path)
    args = parser.parse_args()
    if digest(PROTOCOL_FILE) != PROTOCOL_SHA256:
        raise SystemExit("El protocolo fue modificado")
    protocol = json.loads(PROTOCOL_FILE.read_text(encoding="utf-8"))
    pairs, manifest, status, complete = load_experiment(args.folder, protocol)
    report = evaluate(pairs, protocol, complete)
    report.update(mode=manifest["mode"], protocol_sha256=PROTOCOL_SHA256,
                  wall_seconds=status.get("wall_seconds"), memory=status.get("memory"),
                  behavior_checks="Ver pruebas funcionales; una puerta estadistica no sustituye las pruebas de comportamiento")
    output = args.folder / "analysis.json"
    with output.open("x", encoding="utf-8", newline="\n") as stream:
        json.dump(report, stream, indent=2, ensure_ascii=False, allow_nan=False)
        stream.write("\n")
    print(json.dumps({"report": str(output), "verdict": report["verdict"], "correlations": report["correlations"],
                      "contact": report["sources"]["contact"], "timeouts": report["timeouts"]}, ensure_ascii=True))


if __name__ == "__main__":
    main()

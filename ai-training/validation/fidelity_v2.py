"""Protocolo v2: validez exacta de logs, cierre de lotes y analisis sin tocar el protocolo original."""
from __future__ import annotations

import argparse
import hashlib
import json
import random
import re
import zipfile
from collections import defaultdict
from pathlib import Path
from statistics import mean

import v3_fidelity as original

ROOT = original.ROOT
PROTOCOL_FILE = ROOT / "ai-training/protocols/v3_fidelity_v2.json"
PROTOCOL_SHA256 = "2104f07db9bfdef73283283210f9bee67bbe2b8ef416dcbe2a0d4cfc1c10108f"
CERTIFICATE_LINE = "ERROR: Failed to read the root certificate store."
digest = original.digest


def load_protocol():
    if digest(PROTOCOL_FILE) != PROTOCOL_SHA256 or digest(original.PROTOCOL_FILE) != original.PROTOCOL_SHA256:
        raise ValueError("Protocolo congelado alterado")
    protocol = json.loads(PROTOCOL_FILE.read_text(encoding="utf-8"))
    parent = json.loads(original.PROTOCOL_FILE.read_text(encoding="utf-8"))
    for field in ("criteria", "uncertainty", "samples"):
        if protocol[field] != parent[field]:
            raise ValueError("Criterios o muestras distintos del original")
    if (protocol["runs_per_level_per_environment"] != 10 or protocol["run_indices"] != list(range(10))
            or protocol["attempt_validity"]["allowlisted_error_messages"] != [CERTIFICATE_LINE]):
        raise ValueError("Version o excepcion de log incompatible")
    return protocol


def scan_log(text, protocol):
    """No normaliza espacios, mayusculas ni prefijos para aplicar la excepcion."""
    lines = text.splitlines()
    starts = [i for i, line in enumerate(lines) if line.startswith("FIDELITY_V2_BEGIN ")]
    allowed = protocol["attempt_validity"]["allowlisted_error_messages"]
    limit = protocol["attempt_validity"]["allowlisted_error_max_occurrences_per_log"]
    counts = defaultdict(int)
    issues, blocking = [], []
    active = None
    for i, line in enumerate(lines):
        match = re.match(r"V2_PAIR_BEGIN seed=(\d+) run=(\d+) phase=(sim|real)$", line)
        if match:
            active = {"seed": int(match[1]), "run": int(match[2]), "phase": match[3]}
        lower = line.lower()
        forbidden = "gchandle" in lower or "illegal instruction" in lower
        error = "error:" in lower
        if forbidden or error:
            exempt = False
            if not forbidden and line in allowed:
                counts[line] += 1
                exempt = len(starts) == 1 and i < starts[0] and counts[line] <= limit
            issue = {"line": i + 1, "text": line, "active_pair": active,
                     "kind": "allowed_startup_certificate" if exempt else "blocking_runtime_error"}
            issues.append(issue)
            if not exempt:
                blocking.append(issue)
        elif line.startswith("WARNING:"):
            issues.append({"line": i + 1, "text": line, "active_pair": active, "kind": "warning"})
    if len(starts) != 1:
        blocking.append({"kind": "invalid_start_marker_count", "count": len(starts)})
    return {"valid": not blocking, "blocking": blocking, "issues": issues,
            "allowed_certificate_lines": sum(i["kind"] == "allowed_startup_certificate" for i in issues),
            "gchandle_lines": sum("gchandle" in line.lower() for line in lines),
            "illegal_instruction_lines": sum("illegal instruction" in line.lower() for line in lines),
            "error_lines": sum("error:" in line.lower() for line in lines)}


def authorization(path, mode):
    if mode == "preflight":
        if path is not None:
            raise ValueError("El preflight no acepta ni concede autorizacion de aceptacion")
        return None
    if mode not in ("acceptance", "reserve") or path is None:
        raise ValueError("Aceptacion/reserva requieren autorizacion externa explicita y equipo libre")
    value = json.loads(Path(path).read_text(encoding="utf-8"))
    if (value.get("mode") != mode or value.get("protocol_sha256") != PROTOCOL_SHA256
            or value.get("user_approved") is not True or value.get("machine_confirmed_free") is not True
            or not isinstance(value.get("approved_at_utc"), str) or not value["approved_at_utc"].strip()):
        raise ValueError("Autorizacion no valida para el protocolo y modo")
    return digest(path)


def verify_sources(protocol):
    evidence = protocol["registration"]["development_evidence"]
    reference = protocol["freeze_contract"]
    archive = ROOT / reference["measurement_reference_archive"]
    if digest(archive) != evidence["raw_archive_sha256"]:
        raise ValueError("Archivo de referencia alterado")
    with zipfile.ZipFile(archive) as source:
        content = source.read(reference["measurement_reference_manifest"])
    if hashlib.sha256(content).hexdigest() != reference["measurement_reference_manifest_sha256"]:
        raise ValueError("Manifiesto de referencia alterado")
    fingerprints = json.loads(content)["fingerprints"]
    for name, expected in fingerprints.items():
        if name != "executed_assembly" and digest(ROOT / name) != expected:
            raise ValueError("Nucleo historico alterado: " + name)
    historical = dict(re.findall(r'\("([^"]+)", "([0-9a-f]{64})"\)',
                                (ROOT / "tests/HistoricalIntegrityTest.cs").read_text(encoding="utf-8")))
    if len(historical) != 20:
        raise ValueError("Inventario historico inesperado")
    for name, expected in historical.items():
        if digest(ROOT / name) != expected:
            raise ValueError("Dato historico alterado: " + name)
    return {"reference_archive_sha256": digest(archive), "historical_sha256": historical,
            "prior_behavior_evidence": "ai-training/reports/v3/stage1.md",
            "prior_behavior_evidence_sha256": digest(ROOT / "ai-training/reports/v3/stage1.md"),
            "measurement_core_unchanged": True, "controlled_behavior_tests_rerun_now": False}


def validate_batch(folder, mode, first, count, runs, protocol):
    if mode == "preflight":
        if (first, count, runs) != (600001, 1, 1):
            raise ValueError("Preflight fuera del unico par autorizado")
    else:
        sample = protocol["samples"][mode]
        if (count != 5 or runs != 10 or first < sample["first_seed"]
                or first > sample["first_seed"] + sample["count"] - count or (first - sample["first_seed"]) % 5):
            raise ValueError("Lote fuera de la muestra congelada")
    manifest = json.loads((folder / "manifest.json").read_text(encoding="utf-8"))
    status = json.loads((folder / "status.json").read_text(encoding="utf-8"))
    if (manifest["schema_version"] != 2 or manifest["bot_policy_version"] != 1
            or manifest.get("acceptance_authorized_by_preflight") is not False
            or manifest["protocol_sha256"] != PROTOCOL_SHA256 or manifest["protocol"] != protocol
            or manifest["mode"] != mode or manifest["worker_count"] != 1 or manifest["concurrent_environments"]
            or manifest["batch_first_seed"] != first or manifest["batch_level_count"] != count or manifest["runs"] != runs
            or manifest["diagnostic_only"] != (mode == "preflight")
            or manifest["physics_ticks_per_second"] != 60 or manifest["time_scale"] != 1
            or manifest["tiered_compilation_environment"] != "unset"):
        raise ValueError("Manifiesto incompatible")
    if (status["status"] != "batch_complete_pending_log_validation" or status["completed_pairs"] != count * runs
            or (folder / "failure.json").exists()):
        raise ValueError("Lote incompleto o con fallo de calidad")
    expected = {"manifest.json", "coverage.json"}
    for seed in range(first, first + count):
        expected.add(f"level_{seed}.json")
        for run in range(runs):
            expected.update(f"{kind}_{seed}_{run}.json" for kind in ("sim", "real", "resources"))
    actual = {p.name for p in folder.glob("*.json")} - {"status.json"}
    if set(status["data_sha256"]) != expected or actual != expected:
        raise ValueError("Cierre con archivos faltantes o adicionales")
    for name, expected_hash in status["data_sha256"].items():
        if Path(name).name != name or digest(folder / name) != expected_hash:
            raise ValueError("Artefacto alterado: " + name)
    coverage = json.loads((folder / "coverage.json").read_text(encoding="utf-8"))
    if mode == "preflight":
        if coverage.get("all_sample_coverage_executed") is not False or coverage.get("reserve_used") is not False:
            raise ValueError("El preflight no puede abrir las otras muestras")
    else:
        samples = coverage.get("samples", [])
        if {s["name"] for s in samples} != {"development", "acceptance", "reserve"} or any(s["errors"] for s in samples):
            raise ValueError("Cobertura ausente o fallida")
        all_effective = []
        for sample in samples:
            spec = protocol["samples"][sample["name"]]
            kinds = sample["levels_by_kind"]
            if (sample["first_seed"] != spec["first_seed"] or sample["count"] != spec["count"]
                    or len(sample["effective_seeds"]) != spec["count"] or len(sample["signatures"]) != spec["count"]
                    or set(kinds) != set(original.KINDS) or any(value < 1 for value in kinds.values())):
                raise ValueError("Cobertura o rango incompatible")
            if sample["name"] == "acceptance" and (kinds["Runner"] < 10 or kinds["Digger"] < 10):
                raise ValueError("Cobertura insuficiente de Runner/Digger")
            all_effective.extend(sample["effective_seeds"])
        if len(set(all_effective)) != len(all_effective):
            raise ValueError("Solapamiento efectivo entre muestras")
    pairs, effective = [], []
    for seed in range(first, first + count):
        level = json.loads((folder / f"level_{seed}.json").read_text(encoding="utf-8"))
        if level["requested_seed"] != seed:
            raise ValueError("Semilla solicitada incompatible")
        if mode != "preflight":
            coverage_sample = next(s for s in coverage["samples"] if s["name"] == mode)
            if level["effective_seed"] != coverage_sample["effective_seeds"][seed - protocol["samples"][mode]["first_seed"]]:
                raise ValueError("Semilla efectiva distinta de la cobertura registrada")
        effective.append(level["effective_seed"])
        for run in range(runs):
            pair = {"seed": seed, "run": run, "map": level["map"]}
            bot = str(((level["effective_seed"] & 0xFFFFFFFF) * 0x9E3779B97F4A7C15 + run + 1) & 0xFFFFFFFFFFFFFFFF)
            for env in ("sim", "real"):
                row = json.loads((folder / f"{env}_{seed}_{run}.json").read_text(encoding="utf-8"))
                if (row["requested_seed"], row["effective_seed"], row["run"], row["bot_seed"]) != (seed, level["effective_seed"], run, bot):
                    raise ValueError("Par o semilla de bot no coincide")
                original.validate_result(row["result"], level["map"])
                if row["result"]["timed_out"]:
                    raise ValueError("Timeout; la calidad no se excluye para aprobar")
                pair[env] = row["result"]
            pairs.append(pair)
    if len(effective) != len(set(effective)):
        raise ValueError("Semillas efectivas repetidas")
    return pairs, manifest, status


def correlation_intervals(pairs, protocol):
    grouped = defaultdict(list)
    for pair in pairs:
        grouped[pair["seed"]].append(pair)
    seeds = sorted(grouped)
    labels = ("damage_total", "time_total")
    series = {label: {env: [mean(p[env][label] for p in grouped[s]) for s in seeds] for env in ("sim", "real")} for label in labels}
    samples = {label: [] for label in labels}
    settings = protocol["uncertainty"]
    rng = random.Random(settings["seed"])
    for _ in range(settings["resamples"]):
        indices = [rng.randrange(len(seeds)) for _ in seeds]
        for label in labels:
            value = original.spearman([series[label]["sim"][i] for i in indices], [series[label]["real"][i] for i in indices])
            if value is not None:
                samples[label].append(value)
    tail = (1 - settings["confidence"]) / 2
    return {label: {"low": original.percentile(samples[label], tail), "high": original.percentile(samples[label], 1 - tail),
                    "valid_resamples": len(samples[label]), "undefined_resamples": settings["resamples"] - len(samples[label]),
                    "unit": "level", "method": "paired_percentile_bootstrap"} for label in labels}


def evaluate_pairs(pairs, protocol, mode, expected_pairs, log_valid, quality_failures=False, exclusions=()):
    if mode == "preflight":
        valid = log_valid and not quality_failures and not exclusions and {(p["seed"], p["run"]) for p in pairs} == {(600001, 0)} and len(pairs) == 1
        return {"verdict": "preflight_clean_not_a_quality_gate" if valid else "preflight_invalid", "preflight_valid": valid,
                "quality_gate_evaluated": False, "levels": len({p["seed"] for p in pairs}), "pairs": len(pairs),
                "pair_results": pairs, "acceptance_authorized_by_preflight": False}
    sample = protocol["samples"][mode]
    required = {(s, r) for s in range(sample["first_seed"], sample["first_seed"] + sample["count"]) for r in range(10)}
    identities = {(p["seed"], p["run"]) for p in pairs}
    complete = (log_valid and not quality_failures and not exclusions and identities == required
                and len(pairs) == len(required) == expected_pairs == 900)
    report = original.evaluate(pairs, protocol, complete=complete)
    report.pop("complete_development", None)
    report.update(complete_sample=complete, quality_gate_evaluated=complete, exclusions=list(exclusions), mode=mode)
    if pairs:
        intervals = correlation_intervals(pairs, protocol)
        for label in intervals:
            report["correlations"][label]["ci95"] = intervals[label]
    if quality_failures:
        report["verdict"] = "fail_quality"
        report["failures"].append("controlled_or_observed_quality_failure")
    return report


def analyze(folder, save):
    protocol = load_protocol()
    state = json.loads((folder / "execution/state.json").read_text(encoding="utf-8"))
    mode = state["config"]["mode"]
    if state["config"]["protocol_sha256"] != PROTOCOL_SHA256:
        raise ValueError("Estado de otra version del protocolo")
    pairs, fingerprints, runtime = [], None, []
    ranges = protocol["samples"].get(mode, {"first_seed": 600001, "count": 1})
    for first, relative in state["completed_batches"].items():
        attempt = next(a for a in state["attempts"] if a["folder"] == relative)
        if attempt["status"] != "complete" or attempt["exit_code"] != 0:
            raise ValueError("Intento no valido seleccionado")
        for channel in ("log", "engine_log"):
            path = folder / attempt[channel]
            if digest(path) != attempt[channel + "_sha256"] or not scan_log(path.read_text(encoding="utf-8", errors="replace"), protocol)["valid"]:
                raise ValueError("Log seleccionado contaminado o alterado")
        batch_pairs, manifest, _ = validate_batch(folder / relative, mode, int(first), 1 if mode == "preflight" else 5, 1 if mode == "preflight" else 10, protocol)
        if fingerprints is not None and fingerprints != manifest["fingerprints"]:
            raise ValueError("Cambio de codigo entre lotes")
        fingerprints = manifest["fingerprints"]
        pairs.extend(batch_pairs)
    for attempt in state["attempts"]:
        record = {"first_seed": attempt["first_seed"], "attempt": attempt["attempt"], "status": attempt["status"], "channels": {}}
        for channel in ("log", "engine_log"):
            path = folder / attempt[channel]
            if path.exists():
                record["channels"][channel] = scan_log(path.read_text(encoding="utf-8", errors="replace"), protocol)
        runtime.append(record)
    report = evaluate_pairs(pairs, protocol, mode, ranges["count"] * (1 if mode == "preflight" else 10),
                            state["status"] in ("complete", "preflight_complete"), state.get("quality_failure", False), state.get("excluded_pairs", []))
    report.update(protocol_sha256=PROTOCOL_SHA256, runtime=runtime, attempts=state["attempts"],
                  provenance=state["provenance"], mode=mode, authorization_sha256=state["config"].get("authorization_sha256"),
                  unstarted_pairs=state.get("unstarted_pairs", []), source_fingerprints=fingerprints,
                  log_exception_preserved_not_suppressed=True)
    save(folder / "analysis.json", report)
    lines = ["# Fidelidad v2: " + mode, "", f'Resultado: {report["verdict"]}. Pares seleccionados: {len(pairs)}.',
             "Un trabajador, solo headless, simulador y juego real secuenciales; nucleo original intacto.",
             "El preflight no ejecuta ni autoriza aceptacion o reserva y no estima fidelidad con un solo nivel.", "",
             "Excepcion exacta conservada: `" + CERTIFICATE_LINE + "`. Maximo una por canal, antes de FIDELITY_V2_BEGIN.",
             "Cualquier otro ERROR:, GCHandle, Illegal instruction, salida fallida o cierre incompleto invalida.", ""]
    for record in runtime:
        for channel, result in record["channels"].items():
            lines.append(f'- Lote {record["first_seed"]}, intento {record["attempt"]}, canal {channel}: certificado permitido {result["allowed_certificate_lines"]}; incidencias bloqueantes {len(result["blocking"])}.')
    if mode != "preflight":
        lines += ["", "Se requieren los 90 niveles y 900 pares limpios. Las exclusiones no permiten aprobar una muestra reducida."]
    lines += ["", "analysis.json conserva resultados, procedencia, reglas, autorizacion y todos los incidentes por canal."]
    (folder / "README.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("folder", type=Path)
    arguments = parser.parse_args()
    from run_fidelity_v2 import save
    result = analyze(arguments.folder.resolve(), save)
    print(json.dumps({"verdict": result["verdict"], "pairs": result["pairs"]}, ensure_ascii=False))

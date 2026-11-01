"""Regla diagnostica de intentos limpios; no modifica el protocolo congelado."""
import json
from pathlib import Path

import repetition_curve_v3 as curve

POLICY = {
    "version": 2, "max_attempts": 3, "validation_unit": "entire_batch_process_log",
    "invalid_markers_case_insensitive": ["gchandle", "illegal instruction", "error: system."],
    "selection": "first_complete_clean_batch_attempt",
    "exhaustion": "exclude_every_pair_in_batch_explicitly",
    "means": "fixed_first_3_5_10_on_common_cohort_with_all_ten_clean_pairs; no_imputation_or_replacement",
}


def log_counts(text):
    lines = [line.lower() for line in text.splitlines()]
    return {"gchandle_lines": sum("gchandle" in line for line in lines),
            "illegal_instruction_lines": sum("illegal instruction" in line for line in lines),
            "system_error_lines": sum("error: system." in line for line in lines),
            "invalidating_lines": sum(any(marker in line for marker in POLICY["invalid_markers_case_insensitive"]) for line in lines)}


def rejection_reasons(text, code):
    reasons = [marker for marker in POLICY["invalid_markers_case_insensitive"] if marker in text.lower()]
    if isinstance(code, int) and code != 0:
        reasons.append(f"exit_code={code}")
    if code == "supervisor_interrupted":
        reasons.append("supervisor_interrupted_without_verified_successful_exit")
    return reasons


def selected_batches(folder, state, validate):
    if state["config"].get("validity_policy") != POLICY:
        raise ValueError("El estado no aplica la regla de intentos limpios")
    result = {}
    for first, relative in state["completed_batches"].items():
        attempt = next(a for a in state["attempts"] if a["folder"] == relative)
        log = folder / attempt["log"]
        text = log.read_text(encoding="utf-8", errors="replace")
        if (attempt["status"] != "complete" or attempt.get("exit_code") != 0
                or attempt.get("log_sha256") != curve.digest(log) or rejection_reasons(text, 0)):
            raise ValueError("Intento seleccionado contaminado o alterado: " + relative)
        validate(folder / relative, int(first))
        result[int(first)] = folder / relative
    return result


def exclusions(first, attempts):
    history = [{"attempt": a["attempt"], "log": a["log"], "status": a["status"],
                "reason": a.get("rejection_reasons", []), "active_pair": a.get("active_pair"),
                "exit_code": a.get("exit_code")} for a in attempts]
    return [{"seed": seed, "run": run, "reason": "no_complete_clean_batch_after_three_attempts",
             "attempts": history} for seed in range(first, first + 5) for run in range(10)]


def load_selected_data(batches):
    data = {}
    fingerprints = None
    for batch in batches.values():
        manifest = json.loads((batch / "manifest.json").read_text(encoding="utf-8"))
        if fingerprints is not None and manifest["fingerprints"] != fingerprints:
            raise ValueError("Codigo o recursos distintos entre lotes")
        fingerprints = manifest["fingerprints"]
        for path in sorted(batch.glob("level_*.json")):
            level = json.loads(path.read_text(encoding="utf-8"))
            seed = level["requested_seed"]
            data[seed] = {env: [] for env in ("sim", "real")}
            for run in range(10):
                expected_bot = str(((level["effective_seed"] & 0xFFFFFFFF) * 0x9E3779B97F4A7C15 + run + 1) & 0xFFFFFFFFFFFFFFFF)
                for env in ("sim", "real"):
                    row = json.loads((batch / f"{env}_{seed}_{run}.json").read_text(encoding="utf-8"))
                    if (row["requested_seed"], row["effective_seed"], row["run"], row["bot_seed"]) != (seed, level["effective_seed"], run, expected_bot):
                        raise ValueError("Par o semilla incoherente")
                    curve.validate_result(row["result"], level["map"])
                    if row["result"]["timed_out"]:
                        raise ValueError("Timeout en intento seleccionado")
                    data[seed][env].append(row["result"])
    return data


def attempt_totals(folder, attempts):
    totals = {key: 0 for key in log_counts("")}
    for attempt in attempts:
        for key, value in log_counts((folder / attempt["log"]).read_text(encoding="utf-8", errors="replace")).items():
            totals[key] += value
    return totals


def report_lines(state):
    lines = ["## Validez, exclusiones y medias", "",
             "Se acepta el primer intento completo cuyo log entero no contiene GCHandle, Illegal instruction ni ERROR: System. (sin distinguir mayusculas), con salida cero.",
             "Un error invalida todo el intento del lote de cinco niveles. Se conservan todos los logs y resultados; maximo tres intentos por lote.",
             "Los 50 pares de un lote sin intento limpio se excluyen explicitamente. Incluso los pares anteriores al error quedan invalidados por la regla del log completo.",
             "Las medias usan exactamente las primeras 3, 5 y 10 repeticiones de cada nivel incluido. No se reduce el denominador, no se imputan datos y no se eligen otras semillas de bot.",
             "Se usa la misma cohorte de niveles con diez pares validos para los tres prefijos y sus diferencias bootstrap. Las exclusiones reducen la muestra y pueden sesgarla; el resultado describe solo la cohorte retenida.", ""]
    excluded = state.get("excluded_pairs", [])
    lines.append(f'Pares excluidos: {len(excluded)}. Niveles incluidos: {len(state["completed_batches"]) * 5} de {state["config"]["levels"]}.')
    for pair in excluded:
        lines.append(f'- {pair["seed"]}:{pair["run"]}: {pair["reason"]}; intentos: ' + json.dumps(pair["attempts"], ensure_ascii=False))
    lines += ["", "## Intentos e incidentes", ""]
    for attempt in state["attempts"]:
        lines.append(f'- Lote {attempt["first_seed"]}..{attempt["last_seed"]}, intento {attempt["attempt"]}: {attempt["status"]}; salida {attempt.get("exit_code")}; motivos {attempt.get("rejection_reasons", [])}; log `{attempt["log"]}`.')
        for issue in attempt.get("incidents", []):
            lines.append(f'  - Linea {issue["line"]}, par {issue["active_pair"]}: `{issue["text"]}`')
    return lines


def write_report(folder, state, validate, save):
    batches = selected_batches(folder, state, validate)
    data = load_selected_data(batches)
    plan = json.loads(curve.PLAN_FILE.read_text(encoding="utf-8"))
    if curve.digest(curve.PROTOCOL_FILE) != curve.PROTOCOL_SHA256:
        raise ValueError("El protocolo original cambio")
    analysis = curve.curve_statistics(data, plan) if len(data) >= 2 else {
        "diagnostic_only": True, "levels": len(data), "curve": [], "paired_differences": [],
        "reason": "insufficient_clean_levels", "not_an_acceptance_verdict": True}
    analysis.update(validity_policy=POLICY, included_levels=sorted(data), excluded_pairs=state.get("excluded_pairs", []),
                    all_attempt_counts=attempt_totals(folder, state["attempts"]),
                    original_comparison=curve.original_comparison(data) if data else None,
                    original_comparison_note="Los puntos originales conservan los 30 niveles originales; no representan la cohorte reducida.",
                    plan_sha256=curve.digest(curve.PLAN_FILE), protocol_sha256=curve.PROTOCOL_SHA256,
                    wall_seconds=state.get("finished", state["started"]) - state["started"],
                    data_sha256={str(p.relative_to(folder)): curve.digest(p) for batch in batches.values() for p in batch.glob("*.json")},
                    log_sha256={a["log"]: curve.digest(folder / a["log"]) for a in state["attempts"]})
    save(folder / "analysis.json", analysis)
    lines = ["# Curva diagnostica con intentos limpios", "",
             f'Configuracion: DOTNET_TieredCompilation={state["config"]["tiered_compilation"]}; un trabajador, solo desarrollo.',
             "El protocolo y plan originales permanecen intactos. Esta regla de validez adicional fue autorizada tras observar contaminacion por excepciones.",
             "IC 95 %: bootstrap pareado por nivel, 2000 remuestreos, semilla 20261031; prefijos fijos.", "",
             "| Repeticiones | Niveles | Spearman dano (IC 95 %) | Spearman tiempo (IC 95 %) |", "|---|---|---|---|"]
    def number(value):
        return "indefinido" if value is None else f"{value:.6f}"
    for row in analysis["curve"]:
        cells = [f'{number(row[label]["spearman"])} [{number(row[label]["ci95"]["low"])}, {number(row[label]["ci95"]["high"])}]' for label in ("damage_total", "time_total")]
        lines.append(f'| {row["repetitions"]} | {len(data)} | ' + " | ".join(cells) + " |")
    if not analysis["curve"]:
        lines += ["", "No hay niveles limpios suficientes para estimar correlaciones."]
    lines += ["", *report_lines(state), "", "analysis.json conserva los IC, diferencias de prefijos, hashes, exclusiones y comparacion descriptiva con los datos originales."]
    (folder / "README.md").write_text("\n".join(lines) + "\n", encoding="utf-8")

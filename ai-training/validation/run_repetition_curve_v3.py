"""Ejecutor secuencial reanudable: cinco niveles y hasta tres intentos limpios."""
from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import time
from pathlib import Path

import repetition_curve_v3 as curve
import clean_repetition_v3 as clean

ROOT = curve.ROOT


def save(path, value):
    temporary = path.with_suffix(".tmp")
    temporary.write_text(json.dumps(value, indent=2, ensure_ascii=False, allow_nan=False) + "\n", encoding="utf-8")
    temporary.replace(path)


def inspect_log(text):
    active = None
    incidents = []
    for number, line in enumerate(text.splitlines(), 1):
        match = re.search(r"CURVE_BEGIN seed=(\d+) run=(\d+) phase=(\w+)", line)
        if match:
            active = dict(zip(("seed", "run", "phase"), (int(match[1]), int(match[2]), match[3])))
        if (line.startswith(("ERROR:", "WARNING:")) or "gchandle" in line.lower()
                or "illegal instruction" in line.lower() or "fatal error" in line.lower()):
            incidents.append({"line": number, "text": line, "active_pair": active})
    return active, incidents


def fatal_exit(code, text):
    # STATUS_ILLEGAL_INSTRUCTION en Windows; SIGILL en Unix.
    return code != 0 and ("gchandle" in text.lower() or "illegal instruction" in text.lower()
                          or code in (-4, 132) or (code & 0xFFFFFFFF) == 0xC000001D)


def assert_godot_idle():
    if os.name == "nt":
        probe = subprocess.run(["powershell.exe", "-NoProfile", "-Command",
                                "@(Get-Process *Godot* -ErrorAction SilentlyContinue).Count"],
                               capture_output=True, text=True, check=True)
        if int(probe.stdout.strip()):
            raise RuntimeError("Hay otra sesion de Godot activa; no se inicia otro proceso.")


def validate_batch(folder, first):
    status = json.loads((folder / "status.json").read_text(encoding="utf-8"))
    if (status.get("status") != "repetition_curve_complete_pending_analysis"
            or status["completed_pairs"] != 50 or (folder / "failure.json").exists()):
        raise ValueError("Lote incompleto")
    expected = {"manifest.json", "coverage.json"}
    for seed in range(first, first + 5):
        expected.add(f"level_{seed}.json")
        for run in range(10):
            expected.update(f"{kind}_{seed}_{run}.json" for kind in ("sim", "real", "resources"))
    if set(status["data_sha256"]) != expected:
        raise ValueError("Cierre de lote incompleto")
    for name, digest in status["data_sha256"].items():
        if Path(name).name != name or curve.digest(folder / name) != digest:
            raise ValueError("Artefacto de lote alterado: " + name)
    return status


def execute(folder, godot, levels=30, tiered="0"):
    if folder.exists() and not (folder / "execution/state.json").exists():
        raise ValueError("Salida existente sin estado de este ejecutor; no se modifica")
    execution = folder / "execution"
    execution.mkdir(parents=True, exist_ok=True)
    # El bloqueo del SO se libera tambien si el supervisor termina abruptamente.
    with (execution / "runner.lock").open("a+b") as lock:
        if os.name == "nt":
            import msvcrt
            lock.seek(0)
            lock.write(b"0")
            lock.flush()
            lock.seek(0)
            msvcrt.locking(lock.fileno(), msvcrt.LK_NBLCK, 1)
        else:
            import fcntl
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        return execute_locked(folder, execution, godot, levels, tiered)


def record_attempt(folder, attempt, code):
    log_path = folder / attempt["log"]
    text = log_path.read_text(encoding="utf-8", errors="replace")
    attempt["exit_code"] = code
    attempt["active_pair"], attempt["incidents"] = inspect_log(text)
    attempt["log_sha256"] = curve.digest(log_path)
    attempt["counts"] = clean.log_counts(text)
    attempt["rejection_reasons"] = clean.rejection_reasons(text, code)
    attempt["status"] = "invalid" if attempt["rejection_reasons"] else "complete"
    return text


def execute_locked(folder, execution, godot, levels, tiered):
    config = {"first_seed": 600001, "levels": levels, "runs": 10, "batch_size": 5,
              "worker_count": 1, "tiered_compilation": tiered, "godot": str(godot),
              "validity_policy": clean.POLICY, "clean_analysis_sha256": curve.digest(clean.__file__),
              "runner_sha256": curve.digest(__file__), "scene_sha256": curve.digest(ROOT / "tests/FidelityV3RepetitionCurve.cs"),
              "plan_sha256": curve.digest(curve.PLAN_FILE), "analyzer_sha256": curve.digest(curve.__file__)}
    state_path = execution / "state.json"
    state = json.loads(state_path.read_text(encoding="utf-8")) if state_path.exists() else {
        "config": config, "status": "running", "attempts": [], "completed_batches": {},
        "excluded_batches": [], "excluded_pairs": [], "started": time.time()}
    if state["config"] != config:
        raise ValueError("La configuracion/codigo de reanudacion difiere")
    save(state_path, state)
    clean.selected_batches(folder, state, validate_batch)
    env = os.environ.copy()
    if tiered == "default":
        env.pop("DOTNET_TieredCompilation", None)
    else:
        env["DOTNET_TieredCompilation"] = "0"
    for first in range(600001, 600001 + levels, 5):
        key = str(first)
        if key in state["completed_batches"] or first in state["excluded_batches"]:
            continue
        previous = [a for a in state["attempts"] if a["first_seed"] == first]
        if previous and previous[-1]["status"] == "running":
            assert_godot_idle()
            attempt = previous[-1]
            # Sin codigo de salida persistido nunca se acepta el intento interrumpido.
            # Aun si su log acaba en CURVE_COMPLETE, el cierre del proceso no esta verificado.
            record_attempt(folder, attempt, "supervisor_interrupted")
            save(state_path, state)
        if previous and previous[-1]["status"] == "complete":
            attempt = previous[-1]
            record_attempt(folder, attempt, attempt["exit_code"])
            if attempt["status"] == "complete":
                validate_batch(folder / attempt["folder"], first)
                state["completed_batches"][key] = attempt["folder"]
                save(state_path, state)
                continue
        if any(a["status"] == "failed" for a in previous):
            raise RuntimeError("Fallo previo ajeno a la regla de validez; revisar antes de reanudar")
        for number in range(len(previous) + 1, clean.POLICY["max_attempts"] + 1):
            assert_godot_idle()
            batch = execution / f"batch_{first}_attempt_{number}"
            log_path = execution / f"batch_{first}_attempt_{number}.log"
            engine_log = execution / f"batch_{first}_attempt_{number}.engine.log"
            attempt = {"first_seed": first, "last_seed": first + 4, "attempt": number,
                       "folder": str(batch.relative_to(folder)), "log": str(log_path.relative_to(folder)), "status": "running"}
            state["attempts"].append(attempt)
            state["status"] = "running"
            save(state_path, state)
            command = [str(godot), "--headless", "--fixed-fps", "60", "--path", str(ROOT),
                       "--log-file", str(engine_log), "res://tests/FidelityV3RepetitionCurve.tscn", "--", f"--out-dir={batch}",
                       f"--first-seed={first}", "--level-count=5"]
            attempt["command"] = command
            save(state_path, state)
            with log_path.open("x", encoding="utf-8") as log, (folder / "progress.log").open("a", encoding="utf-8") as progress:
                progress.write(f"BATCH_BEGIN seed={first} attempt={number} tiered={tiered}\n")
                progress.flush()
                process = subprocess.Popen(command, cwd=ROOT, env=env, stdout=subprocess.PIPE,
                                           stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace")
                for line in process.stdout:
                    log.write(line)
                    log.flush()
                    progress.write(line)
                    progress.flush()
                    if line.startswith(("CURVE_BEGIN", "CURVE_END", "CURVE_COMPLETE")):
                        print(line, end="", flush=True)
                code = process.wait()
            text = record_attempt(folder, attempt, code)
            if code != 0 and not clean.log_counts(text)["invalidating_lines"] and not fatal_exit(code, text):
                attempt["status"] = "failed"
                state["status"] = "failed"
                save(state_path, state)
                raise RuntimeError(f"Godot fallo fuera de la regla reintentable: {code}; {log_path}")
            if attempt["status"] == "complete":
                try:
                    validate_batch(batch, first)
                except Exception:
                    attempt["status"] = "failed"
                    state["status"] = "failed"
                    save(state_path, state)
                    raise
                state["completed_batches"][key] = attempt["folder"]
            save(state_path, state)
            with (folder / "progress.log").open("a", encoding="utf-8") as progress:
                progress.write(f'BATCH_END seed={first} attempt={number} status={attempt["status"]} reasons={attempt["rejection_reasons"]}\n')
            if attempt["status"] == "complete":
                break
        else:
            rejected = [a for a in state["attempts"] if a["first_seed"] == first]
            state["excluded_batches"].append(first)
            state["excluded_pairs"].extend(clean.exclusions(first, rejected))
            save(state_path, state)
            with (folder / "progress.log").open("a", encoding="utf-8") as progress:
                progress.write(f"BATCH_EXCLUDED seed={first} pairs=50 attempts=3\n")
    state["finished"] = time.time()
    state["status"] = "complete_with_exclusions" if state["excluded_pairs"] else "complete"
    save(state_path, state)
    if levels == 30:
        finalize(folder, state)
    else:
        (folder / "README.md").write_text("# Diagnostico de cinco niveles\n\n" + "\n".join(clean.report_lines(state)) + "\n", encoding="utf-8")
    return state


def finalize(folder, state):
    clean.write_report(folder, state, validate_batch, save)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("folder", type=Path)
    parser.add_argument("--godot", type=Path, required=True)
    parser.add_argument("--levels", type=int, choices=(5, 30), default=30)
    parser.add_argument("--tiered", choices=("0", "default"), default="0")
    args = parser.parse_args()
    folder = args.folder.resolve()
    if not folder.is_relative_to(ROOT / "ai-training/reports/v3"):
        parser.error("Salida fuera de informes v3")
    execute(folder, args.godot.resolve(), args.levels, args.tiered)


if __name__ == "__main__":
    main()

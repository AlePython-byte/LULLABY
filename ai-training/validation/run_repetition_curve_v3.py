"""Ejecutor secuencial reanudable: cinco niveles por proceso, dos reintentos fatales."""
from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import time
from pathlib import Path

import repetition_curve_v3 as curve

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
        if (line.startswith("ERROR:") or "gchandle" in line.lower()
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
    if status["completed_pairs"] != 50 or (folder / "failure.json").exists():
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


def execute_locked(folder, execution, godot, levels, tiered):
    config = {"first_seed": 600001, "levels": levels, "runs": 10, "batch_size": 5,
              "worker_count": 1, "tiered_compilation": tiered, "godot": str(godot),
              "runner_sha256": curve.digest(__file__), "scene_sha256": curve.digest(ROOT / "tests/FidelityV3RepetitionCurve.cs"),
              "plan_sha256": curve.digest(curve.PLAN_FILE), "analyzer_sha256": curve.digest(curve.__file__)}
    state_path = execution / "state.json"
    state = json.loads(state_path.read_text(encoding="utf-8")) if state_path.exists() else {
        "config": config, "status": "running", "attempts": [], "completed_batches": {}, "started": time.time()}
    if state["config"] != config:
        raise ValueError("La configuracion/codigo de reanudacion difiere")
    save(state_path, state)
    env = os.environ.copy()
    if tiered == "default":
        env.pop("DOTNET_TieredCompilation", None)
    else:
        env["DOTNET_TieredCompilation"] = "0"
    for first in range(600001, 600001 + levels, 5):
        key = str(first)
        if key in state["completed_batches"]:
            validate_batch(folder / state["completed_batches"][key], first)
            continue
        previous = [a for a in state["attempts"] if a["first_seed"] == first]
        if previous and previous[-1]["status"] == "complete":
            validate_batch(folder / previous[-1]["folder"], first)
            state["completed_batches"][key] = previous[-1]["folder"]
            save(state_path, state)
            continue
        # Un supervisor interrumpido no pierde un proceso ya terminado con cierre valido.
        if previous and previous[-1]["status"] == "running":
            assert_godot_idle()
            attempt = previous[-1]
            batch = folder / attempt["folder"]
            log = (folder / attempt["log"]).read_text(encoding="utf-8", errors="replace")
            attempt["active_pair"], attempt["incidents"] = inspect_log(log)
            if (batch / "status.json").exists() and "CURVE_COMPLETE pairs=50" in log:
                validate_batch(batch, first)
                attempt["status"] = "complete"
                state["completed_batches"][key] = attempt["folder"]
                save(state_path, state)
                continue
            if not any(word in log.lower() for word in ("gchandle", "illegal instruction")):
                raise RuntimeError("Intento interrumpido sin cierre ni evidencia fatal; revisar " + attempt["log"])
            attempt["status"] = "fatal"
            attempt["exit_code"] = "supervisor_interrupted"
            save(state_path, state)
        if any(a["status"] not in ("fatal", "complete") for a in previous):
            raise RuntimeError("Fallo no fatal previo; no se reintenta automaticamente")
        for number in range(len(previous) + 1, 4):
            assert_godot_idle()
            batch = execution / f"batch_{first}_attempt_{number}"
            log_path = execution / f"batch_{first}_attempt_{number}.log"
            attempt = {"first_seed": first, "last_seed": first + 4, "attempt": number,
                       "folder": str(batch.relative_to(folder)), "log": str(log_path.relative_to(folder)), "status": "running"}
            state["attempts"].append(attempt)
            state["status"] = "running"
            save(state_path, state)
            command = [str(godot), "--headless", "--fixed-fps", "60", "--path", str(ROOT),
                       "res://tests/FidelityV3RepetitionCurve.tscn", "--", f"--out-dir={batch}",
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
            text = log_path.read_text(encoding="utf-8")
            attempt["exit_code"] = code
            attempt["active_pair"], attempt["incidents"] = inspect_log(text)
            attempt["status"] = "fatal" if fatal_exit(code, text) else "failed" if code else "complete"
            save(state_path, state)
            if code == 0:
                validate_batch(batch, first)
                state["completed_batches"][key] = attempt["folder"]
                save(state_path, state)
                break
            if attempt["status"] != "fatal":
                raise RuntimeError(f"Godot fallo sin error fatal reintentable: {code}; {log_path}")
        else:
            state["status"] = "failed_retry_limit"
            save(state_path, state)
            raise RuntimeError(f"Lote {first}: agotados tres intentos; {state_path}")
    state["status"] = "batches_complete"
    save(state_path, state)
    if levels == 30:
        finalize(folder, state)
    state["status"] = "complete"
    save(state_path, state)
    return state


def finalize(folder, state):
    statuses = []
    manifest = None
    for first, relative in state["completed_batches"].items():
        batch = folder / relative
        statuses.append(validate_batch(batch, int(first)))
        current = json.loads((batch / "manifest.json").read_text(encoding="utf-8"))
        if manifest is not None and current["fingerprints"] != manifest["fingerprints"]:
            raise ValueError("Codigo o recursos cambiaron entre lotes")
        manifest = current
        for source in batch.glob("*.json"):
            if source.name.startswith(("sim_", "real_", "resources_", "level_")):
                target = folder / source.name
                if target.exists():
                    if curve.digest(target) != curve.digest(source):
                        raise ValueError("Consolidacion incompatible: " + target.name)
                else:
                    shutil.copyfile(source, target)
    manifest.pop("batch_first_seed")
    manifest.pop("batch_level_count")
    save(folder / "manifest.json", manifest)
    first_batch = folder / next(iter(state["completed_batches"].values()))
    shutil.copyfile(first_batch / "coverage.json", folder / "coverage.json")
    save(folder / "status.json", {"status": "repetition_curve_complete_pending_analysis", "completed_pairs": 300,
         "wall_seconds": time.time() - state["started"], "memory": [s["memory"] for s in statuses],
         "data_sha256": {p.name: curve.digest(p) for p in folder.glob("*.json") if p.name not in ("status.json", "analysis.json")}})
    if not (folder / "analysis.json").exists():
        subprocess.run([sys.executable, str(Path(curve.__file__)), str(folder), "--log-file", str(folder / "progress.log")], cwd=ROOT, check=True)
    analysis = json.loads((folder / "analysis.json").read_text(encoding="utf-8"))
    lines = ["# Curva de repeticiones v3", "", "30 niveles (600001..600030), diez repeticiones por entorno, un trabajador.",
             "Seis lotes de cinco niveles; DOTNET_TieredCompilation=0. Diagnostico, sin aceptacion ni reserva.",
             "IC 95 %: bootstrap pareado por nivel, 2000 remuestreos, semilla 20261031; prefijos fijos.", "",
             "| Repeticiones | Spearman dano (IC 95 %) | Spearman tiempo (IC 95 %) |", "|---|---|---|"]
    for row in analysis["curve"]:
        cells = []
        for label in ("damage_total", "time_total"):
            stat = row[label]
            cells.append(f'{stat["spearman"]:.6f} [{stat["ci95"]["low"]:.6f}, {stat["ci95"]["high"]:.6f}]')
        lines.append(f'| {row["repetitions"]} | ' + " | ".join(cells) + " |")
    lines += ["", "## Incidentes y reintentos", "", "Todos los intentos, incluidos los fallidos, se conservan en execution/.",
              "Los reintentos completos de lote fueron autorizados despues del fallo fatal de la curva anterior; el plan y protocolo originales permanecen intactos.", ""]
    for attempt in state["attempts"]:
        lines.append(f'- Lote {attempt["first_seed"]}..{attempt["last_seed"]}, intento {attempt["attempt"]}: {attempt["status"]}; salida {attempt.get("exit_code")}; par activo {attempt.get("active_pair")}; log `{attempt["log"]}`.')
        for issue in attempt.get("incidents", []):
            lines.append(f'  - Linea {issue["line"]}, par {issue["active_pair"]}: `{issue["text"]}`')
    lines += ["", "analysis.json conserva las diferencias pareadas de Spearman, la comparacion con las tres repeticiones originales y los incidentes del log completo.",
              "La salida parcial repetition_curve_20261031_01 permanece intacta; no aporta partidas a esta curva."]
    (folder / "README.md").write_text("\n".join(lines) + "\n", encoding="utf-8")


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

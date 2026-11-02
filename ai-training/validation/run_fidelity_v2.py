"""Orquestacion v2, un trabajador. Preflight de un par; ninguna autorizacion implicita de aceptacion."""
from __future__ import annotations

import argparse
import json
import os
import subprocess
import time
from pathlib import Path

import fidelity_v2 as fidelity

ROOT = fidelity.ROOT


def save(path, value):
    temporary = path.with_suffix(".tmp")
    temporary.write_text(json.dumps(value, indent=2, ensure_ascii=False, allow_nan=False) + "\n", encoding="utf-8")
    temporary.replace(path)


def assert_godot_idle():
    if os.name == "nt":
        result = subprocess.run(["powershell.exe", "-NoProfile", "-Command",
                                 "@(Get-Process *Godot* -ErrorAction SilentlyContinue).Count"],
                                check=True, capture_output=True, text=True)
        if int(result.stdout.strip()):
            raise RuntimeError("Hay otra sesion de Godot activa; no se inicia otro proceso")


def record_attempt(folder, attempt, code, protocol):
    attempt["exit_code"] = code
    attempt["channels"] = {}
    texts = []
    for channel in ("log", "engine_log"):
        path = folder / attempt[channel]
        if path.exists():
            text = path.read_text(encoding="utf-8", errors="replace")
            texts.append(text)
            attempt[channel + "_sha256"] = fidelity.digest(path)
            attempt["channels"][channel] = fidelity.scan_log(text, protocol)
        else:
            attempt["channels"][channel] = {"valid": False, "blocking": [{"kind": "missing_log"}]}
    text = "\n".join(texts)
    failure_file = folder / attempt["folder"] / "failure.json"
    failure = json.loads(failure_file.read_text(encoding="utf-8")) if failure_file.exists() else {}
    attempt["quality_failure"] = "V2_QUALITY_FAILURE" in text or failure.get("quality_failure", False)
    attempt["integrity_failure"] = "V2_INTEGRITY_FAILURE" in text or failure.get("integrity_failure", False)
    attempt["status"] = "complete" if code == 0 and all(c["valid"] for c in attempt["channels"].values()) and not failure else "invalid"
    return attempt["status"] == "complete"


def execute(folder, godot, mode="preflight", authorization_path=None):
    protocol = fidelity.load_protocol()
    # La autorizacion se comprueba antes de crear salida, acceder a muestras o lanzar Godot.
    auth_hash = fidelity.authorization(authorization_path, mode)
    if not folder.is_relative_to(ROOT / "ai-training/reports/v3"):
        raise ValueError("Salida fuera de informes v3")
    if folder.exists() and not (folder / "execution/state.json").exists():
        raise ValueError("Salida existente sin estado; no se modifica")
    execution = folder / "execution"
    execution.mkdir(parents=True, exist_ok=True)
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
        return execute_locked(folder, execution, godot, mode, authorization_path, auth_hash, protocol)


def execute_locked(folder, execution, godot, mode, authorization_path, auth_hash, protocol):
    sample = {"first_seed": 600001, "count": 1} if mode == "preflight" else protocol["samples"][mode]
    count, runs = (1, 1) if mode == "preflight" else (5, 10)
    limit = 1 if mode == "preflight" else protocol["attempt_validity"]["max_attempts_including_first"]
    pause = 0 if mode == "preflight" else protocol["attempt_validity"]["retry_pause_seconds"]
    provenance = fidelity.verify_sources(protocol)
    config = {"mode": mode, "protocol_sha256": fidelity.PROTOCOL_SHA256, "worker_count": 1,
              "first_seed": sample["first_seed"], "levels": sample["count"], "runs": runs,
              "levels_per_batch": count, "max_attempts": limit, "retry_pause_seconds": pause, "tiered_compilation_environment": "unset",
              "authorization_sha256": auth_hash, "godot": str(godot),
              "source_sha256": {name: fidelity.digest(ROOT / name) for name in (
                  "tests/FidelityV3V2Protocol.cs", "tests/FidelityV3V2Check.cs", "tests/FidelityV3V2Check.tscn",
                  "ai-training/validation/fidelity_v2.py", "ai-training/validation/run_fidelity_v2.py",
                  "ai-training/validation/v3_fidelity.py", ".godot/mono/temp/bin/Debug/LULLABY.dll")}}
    state_path = execution / "state.json"
    state = json.loads(state_path.read_text(encoding="utf-8")) if state_path.exists() else {
        "config": config, "provenance": provenance, "status": "running", "started": time.time(), "attempts": [],
        "completed_batches": {}, "excluded_pairs": [], "unstarted_pairs": [], "quality_failure": False}
    if state["config"] != config or state["provenance"] != provenance:
        raise ValueError("Codigo, parametros o autorizacion distintos de la reanudacion")
    save(state_path, state)
    if state["status"] in ("failed_quality", "failed_integrity", "incomplete_retry_limit"):
        raise ValueError("Muestra detenida: no se permite resetear intentos o repetir una muestra abierta")
    env = os.environ.copy()
    env.pop("DOTNET_TieredCompilation", None)
    stopped = False
    firsts = list(range(sample["first_seed"], sample["first_seed"] + sample["count"], count))
    for first in firsts:
        if str(first) in state["completed_batches"]:
            # Revalidacion completa, sin volver a ejecutar lo seleccionado.
            relative = state["completed_batches"][str(first)]
            previous_complete = next(a for a in state["attempts"] if a["folder"] == relative)
            for channel in ("log", "engine_log"):
                if fidelity.digest(folder / previous_complete[channel]) != previous_complete[channel + "_sha256"]:
                    raise ValueError("Log previo alterado")
                if not fidelity.scan_log((folder / previous_complete[channel]).read_text(encoding="utf-8"), protocol)["valid"]:
                    raise ValueError("Log previo no valido")
            fidelity.validate_batch(folder / relative, mode, first, count, runs, protocol)
            continue
        previous = [a for a in state["attempts"] if a["first_seed"] == first]
        if previous and previous[-1]["status"] == "running":
            assert_godot_idle()
            record_attempt(folder, previous[-1], "supervisor_interrupted", protocol)
            save(state_path, state)
        if previous and (previous[-1].get("quality_failure") or previous[-1].get("integrity_failure")):
            state["quality_failure"] = previous[-1].get("quality_failure", False)
            state["status"] = "failed_quality" if state["quality_failure"] else "failed_integrity"
            state["unstarted_pairs"] = [{"seed": seed, "run": run} for seed in range(first + count, sample["first_seed"] + sample["count"]) for run in range(runs)]
            state["finished"] = time.time()
            save(state_path, state)
            return fidelity.analyze(folder, save)
        if previous and previous[-1]["status"] == "complete":
            # Recupera la ventana entre persistir un cierre y seleccionarlo.
            if not record_attempt(folder, previous[-1], previous[-1]["exit_code"], protocol):
                raise ValueError("Cierre previo alterado")
            fidelity.validate_batch(folder / previous[-1]["folder"], mode, first, count, runs, protocol)
            state["completed_batches"][str(first)] = previous[-1]["folder"]
            save(state_path, state)
            continue
        for number in range(len(previous) + 1, limit + 1):
            if number > 1:
                with (folder / "progress.log").open("a", encoding="utf-8") as progress:
                    progress.write(f"RETRY_PAUSE seed={first} attempt={number} seconds={pause} started={time.time()}\n")
                time.sleep(pause)
            assert_godot_idle()
            if fidelity.verify_sources(protocol) != provenance:
                raise ValueError("Nucleo historico alterado antes de lote")
            if fidelity.authorization(authorization_path, mode) != auth_hash:
                raise ValueError("Autorizacion alterada")
            if any(fidelity.digest(ROOT / name) != expected for name, expected in config["source_sha256"].items()):
                raise ValueError("Codigo o ensamblado alterado antes de lote")
            batch = execution / f"batch_{first}_attempt_{number}"
            log_path = execution / f"batch_{first}_attempt_{number}.log"
            engine_log = execution / f"batch_{first}_attempt_{number}.engine.log"
            attempt = {"first_seed": first, "level_count": count, "runs": runs, "attempt": number,
                       "folder": str(batch.relative_to(folder)), "log": str(log_path.relative_to(folder)),
                       "engine_log": str(engine_log.relative_to(folder)), "status": "running"}
            command = [str(godot), "--headless", "--fixed-fps", "60", "--path", str(ROOT), "--log-file", str(engine_log),
                       "res://tests/FidelityV3V2Check.tscn", "--", f"--mode={mode}", f"--out-dir={batch}",
                       f"--first-seed={first}", f"--level-count={count}"]
            if authorization_path:
                command.append(f"--authorization-file={authorization_path}")
            attempt["command"] = command
            state["attempts"].append(attempt)
            state["status"] = "running"
            save(state_path, state)
            with log_path.open("x", encoding="utf-8") as log, (folder / "progress.log").open("a", encoding="utf-8") as progress:
                progress.write(f"BATCH_BEGIN mode={mode} seed={first} attempt={number}\n")
                progress.flush()
                process = subprocess.Popen(command, cwd=ROOT, env=env, stdout=subprocess.PIPE,
                                           stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace")
                for line in process.stdout:
                    log.write(line)
                    log.flush()
                    progress.write(line)
                    progress.flush()
                    if line.startswith(("FIDELITY_V2_BEGIN", "V2_", "ERROR:")):
                        print(line, end="", flush=True)
                code = process.wait()
            valid = record_attempt(folder, attempt, code, protocol)
            if valid:
                try:
                    _, manifest, _ = fidelity.validate_batch(batch, mode, first, count, runs, protocol)
                    if state.get("fingerprints") is not None and state["fingerprints"] != manifest["fingerprints"]:
                        raise ValueError("Cambio de codigo entre procesos")
                    state["fingerprints"] = manifest["fingerprints"]
                    if mode != "preflight" and manifest["authorization_sha256"] != auth_hash:
                        raise ValueError("Manifiesto sin autorizacion esperada")
                except Exception as error:
                    attempt["status"] = "invalid"
                    attempt["integrity_failure"] = True
                    attempt["validation_error"] = str(error)
                    valid = False
            if attempt["quality_failure"] or attempt["integrity_failure"]:
                state["quality_failure"] = attempt["quality_failure"]
                state["status"] = "failed_quality" if attempt["quality_failure"] else "failed_integrity"
                stopped = True
            elif valid:
                state["completed_batches"][str(first)] = attempt["folder"]
            save(state_path, state)
            with (folder / "progress.log").open("a", encoding="utf-8") as progress:
                progress.write(f'BATCH_END seed={first} attempt={number} status={attempt["status"]}\n')
            if valid or stopped:
                break
        else:
            rejected = [a for a in state["attempts"] if a["first_seed"] == first]
            state["excluded_pairs"] = [{"seed": seed, "run": run, "reason": "no_complete_clean_attempt_within_limit",
                                        "attempts": rejected} for seed in range(first, first + count) for run in range(runs)]
            state["status"] = "incomplete_retry_limit"
            stopped = True
        if stopped:
            state["unstarted_pairs"] = [{"seed": seed, "run": run} for seed in range(first + count, sample["first_seed"] + sample["count"]) for run in range(runs)]
            save(state_path, state)
            break
    if not stopped:
        state["status"] = "preflight_complete" if mode == "preflight" else "complete"
    state["finished"] = time.time()
    save(state_path, state)
    return fidelity.analyze(folder, save)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("folder", type=Path)
    parser.add_argument("--godot", type=Path, required=True)
    parser.add_argument("--mode", choices=("preflight", "acceptance", "reserve"), default="preflight")
    parser.add_argument("--authorization-file", type=Path)
    args = parser.parse_args()
    result = execute(args.folder.resolve(), args.godot.resolve(), args.mode,
                     args.authorization_file.resolve() if args.authorization_file else None)
    print(json.dumps({"verdict": result["verdict"], "pairs": result["pairs"], "mode": args.mode}, ensure_ascii=False))
    return 0 if result.get("preflight_valid") or result["verdict"] == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())

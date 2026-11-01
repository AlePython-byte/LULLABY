"""Una sola partida real de diagnostico; nunca reintenta ni abre otra muestra."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "ai-training/validation"))
import fidelity_v2 as fidelity
from run_fidelity_v2 import assert_godot_idle


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("folder", type=Path)
    parser.add_argument("--godot", type=Path, required=True)
    args = parser.parse_args()
    folder = args.folder.resolve()
    if not folder.is_relative_to(ROOT / "ai-training/reports/v3"):
        raise ValueError("Salida fuera de informes v3")
    assert_godot_idle()
    provenance = fidelity.verify_sources(fidelity.load_protocol())
    # mkdir sin exist_ok impide reutilizar un intento o ejecutar otra partida por accidente.
    folder.mkdir()
    env = os.environ.copy()
    env.pop("DOTNET_TieredCompilation", None)
    env.pop("DOTNET_gcConcurrent", None)
    command = [str(args.godot.resolve()), "--headless", "--fixed-fps", "60", "--path", str(ROOT),
               "--log-file", str(folder / "engine.log"),
               "res://ai-training/diagnostics/RealPairTrace610006.tscn", "--",
               f"--out-dir={folder / 'run'}"]
    launch = {"command": command, "worker_count": 1, "max_wall_seconds": 900,
              "max_attempts": 1, "started": time.time(), "status": "launching_once",
              "protocol_sha256": fidelity.PROTOCOL_SHA256, "provenance": provenance,
              "environment": {"DOTNET_TieredCompilation": "unset", "DOTNET_gcConcurrent": "unset"}}
    path = folder / "launch.json"
    path.write_text(json.dumps(launch, indent=2) + "\n", encoding="utf-8")
    timed_out = False
    with (folder / "process.log").open("xb") as log:
        process = subprocess.Popen(command, cwd=ROOT, env=env, stdout=log, stderr=subprocess.STDOUT)
        launch["pid"] = process.pid
        launch["status"] = "running"
        path.write_text(json.dumps(launch, indent=2) + "\n", encoding="utf-8")
        print(f"SINGLE_REAL_STARTED pid={process.pid} wall_limit=900", flush=True)
        try:
            code = process.wait(timeout=900)
        except subprocess.TimeoutExpired:
            timed_out = True
            process.kill()
            code = process.wait()
    launch.update(finished=time.time(), exit_code=code, wall_limit_reached=timed_out,
                  status="stopped_without_retry", final_provenance=fidelity.verify_sources(fidelity.load_protocol()))
    path.write_text(json.dumps(launch, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"exit_code": code, "wall_limit_reached": timed_out,
                      "result_written": (folder / "run/result.json").exists()}), flush=True)
    return 1 if timed_out or code != 0 else 0


if __name__ == "__main__":
    raise SystemExit(main())

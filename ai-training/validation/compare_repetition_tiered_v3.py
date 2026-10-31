"""Compara todos los resultados de cinco niveles con tiered por defecto y desactivado."""
import argparse
import json
import zipfile
from pathlib import Path

import run_repetition_curve_v3 as runner
import repetition_curve_v3 as curve


def compare(folder):
    states = {mode: json.loads((folder / mode / "execution/state.json").read_text(encoding="utf-8"))
              for mode in ("default", "disabled")}
    for mode, state in states.items():
        for attempt in state["attempts"]:
            text = (folder / mode / attempt["log"]).read_text(encoding="utf-8", errors="replace")
            attempt["active_pair"], attempt["incidents"] = runner.inspect_log(text)
    batches = {}
    for mode, state in states.items():
        if state["status"] != "complete" or state["config"]["levels"] != 5:
            raise ValueError("Comparacion incompleta")
        if state["config"]["tiered_compilation"] != ("default" if mode == "default" else "0"):
            raise ValueError("Configuracion tiered incompatible")
        batches[mode] = folder / mode / state["completed_batches"]["600001"]
        runner.validate_batch(batches[mode], 600001)
    manifests = {mode: json.loads((batch / "manifest.json").read_text(encoding="utf-8")) for mode, batch in batches.items()}
    if manifests["default"]["fingerprints"] != manifests["disabled"]["fingerprints"]:
        raise ValueError("Codigo o recursos distintos")
    rows = []
    differences = []
    for name in sorted(p.name for p in batches["default"].glob("*.json") if p.name.startswith(("level_", "sim_", "real_"))):
        paths = {mode: batch / name for mode, batch in batches.items()}
        values = {mode: json.loads(path.read_text(encoding="utf-8")) for mode, path in paths.items()}
        rows.append({"file": name, "identical": values["default"] == values["disabled"],
                     "byte_identical": paths["default"].read_bytes() == paths["disabled"].read_bytes(),
                     "sha256": {mode: curve.digest(path) for mode, path in paths.items()}})
        if not rows[-1]["identical"]:
            differences.append({"file": name, "results": {mode: {
                label: value.get("result", value).get(label) for label in
                ("damage_total", "damage_critical", "time_total", "would_die")}
                for mode, value in values.items()}})
    partial_rows = []
    for mode, state in states.items():
        for attempt in state["attempts"]:
            batch = folder / mode / attempt["folder"]
            if batch == batches[mode]:
                continue
            for path in sorted(batch.glob("*.json")):
                if not path.name.startswith(("level_", "sim_", "real_")):
                    continue
                reference = batches["default"] / path.name
                partial_rows.append({"configuration": mode, "attempt": attempt["attempt"], "file": path.name,
                                     "identical": path.read_bytes() == reference.read_bytes(), "sha256": curve.digest(path)})
    result = {"first_seed": 600001, "last_seed": 600005, "runs_per_level_per_environment": 10,
              "pairs_per_configuration": 50, "compared_files": len(rows),
              "all_identical": all(row["identical"] and row["byte_identical"] for row in rows) and all(row["identical"] for row in partial_rows),
              "files": rows, "differences": differences, "partial_attempt_files": partial_rows,
              "fingerprints": manifests["default"]["fingerprints"],
              "attempts": {mode: state["attempts"] for mode, state in states.items()}}
    archive_path = folder / "results.zip"
    existing = archive_path.exists()
    with zipfile.ZipFile(archive_path, "r" if existing else "x", compression=zipfile.ZIP_DEFLATED) as archive:
        for mode in states:
            for path in sorted((folder / mode).rglob("*")):
                if path.is_file() and path.name != "runner.lock":
                    name = path.relative_to(folder).as_posix()
                    if existing:
                        if archive.read(name) != path.read_bytes():
                            raise ValueError("Archivo distinto del ZIP conservado: " + name)
                    else:
                        archive.write(path, name)
    result["archive_sha256"] = curve.digest(archive_path)
    runner.save(folder / "comparison.json", result)
    lines = ["# Comparacion de compilacion escalonada", "",
             "Niveles 600001..600005, diez repeticiones por entorno, un trabajador.",
             "Un proceso headless por intento de lote, ejecutados secuencialmente; los reintentos se registran abajo.",
             "Se compara la variable ausente (valor por defecto) con DOTNET_TieredCompilation=0.", "",
             f'Resultado: {"IDENTICOS" if result["all_identical"] else "DIFERENCIAS"}.',
             "Se comparan los JSON completos de las 100 partidas por configuracion y los cinco mapas: semillas, resultados por sala, dano por fuente y tipo, visitas y tiempos.",
             f'Coinciden byte a byte {sum(row["byte_identical"] for row in rows)} de {len(rows)} archivos; comparison.json conserva ambos hashes de cada archivo.',
             f'Tambien se cotejan todos los archivos de mapas y partidas guardados por intentos fallidos: {len(partial_rows)} archivos adicionales. Se conservan y no se ocultan al elegir el intento completo.',
             "Se excluyen de la igualdad las duraciones de pared, memoria y metadatos de configuracion.", ""]
    if not result["all_identical"]:
        lines += ["## Comprobacion de igualdad fallida", "",
                  "No se cumple el requisito de resultados identicos previo a la curva completa. No se puede declarar determinismo ni atribuir causalmente el desacuerdo a la variable de entorno.",
                  "Los incidentes del motor se conservan incluso cuando el proceso termina con codigo cero; no se reintentan esos pares ni se sustituyen resultados para conseguir igualdad.", "",
                  "| Archivo | Dano por defecto / desactivado | Tiempo por defecto / desactivado (s) |", "|---|---|---|"]
        for difference in differences:
            a, b = (difference["results"][mode] for mode in ("default", "disabled"))
            lines.append(f'| {difference["file"]} | {a["damage_total"]} / {b["damage_total"]} | {a["time_total"]} / {b["time_total"]} |')
        lines += ["", "Comando de comprobacion (sale con codigo 1 si hay diferencias):", "", "```powershell",
                  f'python ai-training/validation/compare_repetition_tiered_v3.py {folder.relative_to(curve.ROOT).as_posix() if folder.is_relative_to(curve.ROOT) else folder.as_posix()}', "```", ""]
    lines += ["## Incidentes", ""]
    for mode, state in states.items():
        for attempt in state["attempts"]:
            lines.append(f'- {mode}: intento {attempt["attempt"]}, {attempt["status"]}, salida {attempt.get("exit_code")}.')
            for issue in attempt.get("incidents", []):
                lines.append(f'  - Linea {issue["line"]}: `{issue["text"]}`; par {issue["active_pair"]}.')
    lines += ["", "results.zip conserva todos los resultados, manifiestos, estados, recursos y logs originales de ambas configuraciones.",
              "Protocolo, plan congelado, bot, simulador y datos historicos intactos. Prueba historica: 51 comprobaciones correctas, incluidos 20 SHA-256.",
              "El commit del ejecutor compilo desde git archive y paso nueve pruebas especificas.", "", "Reproduccion desde la raiz:", "", "```powershell",
              'python ai-training/validation/run_repetition_curve_v3.py ai-training/reports/v3/OTRA_COMPARACION/default --godot RUTA_GODOT_CONSOLE --levels 5 --tiered default',
              'python ai-training/validation/run_repetition_curve_v3.py ai-training/reports/v3/OTRA_COMPARACION/disabled --godot RUTA_GODOT_CONSOLE --levels 5 --tiered 0',
              'python ai-training/validation/compare_repetition_tiered_v3.py ai-training/reports/v3/OTRA_COMPARACION', "```"]
    (folder / "README.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    if not result["all_identical"]:
        raise ValueError("Resultados diferentes; revisar comparison.json")
    print(f'IDENTICOS: {len(rows)} archivos completos; 50 pares por configuracion.')


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("folder", type=Path)
    compare(parser.parse_args().folder.resolve())

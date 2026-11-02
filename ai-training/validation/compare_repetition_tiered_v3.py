"""Compara exclusivamente intentos completos con logs limpios, conservando los rechazados."""
import argparse
import json
import zipfile
from pathlib import Path

import run_repetition_curve_v3 as runner
import repetition_curve_v3 as curve
import clean_repetition_v3 as clean


def compare(folder):
    states = {mode: json.loads((folder / mode / "execution/state.json").read_text(encoding="utf-8"))
              for mode in ("default", "disabled")}
    batches = {}
    counts = {}
    for mode, state in states.items():
        if state["status"] not in ("complete", "complete_with_exclusions") or state["config"]["levels"] != 5:
            raise ValueError("Comparacion incompleta")
        if state["config"]["tiered_compilation"] != ("default" if mode == "default" else "0"):
            raise ValueError("Configuracion tiered incompatible")
        selected = clean.selected_batches(folder / mode, state, runner.validate_batch)
        clean.load_selected_data(selected)
        batches[mode] = selected.get(600001)
        counts[mode] = clean.attempt_totals(folder / mode, state["attempts"])
    manifests = {mode: json.loads((batch / "manifest.json").read_text(encoding="utf-8")) for mode, batch in batches.items() if batch}
    if len(manifests) == 2 and manifests["default"]["fingerprints"] != manifests["disabled"]["fingerprints"]:
        raise ValueError("Codigo o recursos distintos")
    rows, differences = [], []
    if all(batches.values()):
        names = sorted(p.name for p in batches["default"].glob("*.json") if p.name.startswith(("level_", "sim_", "real_")))
        for name in names:
            paths = {mode: batch / name for mode, batch in batches.items()}
            values = {mode: json.loads(path.read_text(encoding="utf-8")) for mode, path in paths.items()}
            rows.append({"file": name, "identical": values["default"] == values["disabled"],
                         "byte_identical": paths["default"].read_bytes() == paths["disabled"].read_bytes(),
                         "sha256": {mode: curve.digest(path) for mode, path in paths.items()}})
            if not rows[-1]["identical"]:
                differences.append({"file": name, "full_results": values})
    identical = len(rows) == 105 and all(row["identical"] and row["byte_identical"] for row in rows)
    # Desempate conservador: variable ausente. Se cuentan todos los intentos, no solo el elegido.
    selected_mode = min(("default", "disabled"), key=lambda mode: (counts[mode]["invalidating_lines"], mode != "default")) if identical else None
    previous = curve.ROOT / "ai-training/reports/v3/tiered_comparison_20261031_01/results.zip"
    previous_counts = {}
    if previous.exists():
        with zipfile.ZipFile(previous) as archive:
            for mode in states:
                previous_counts[mode] = {key: 0 for key in clean.log_counts("")}
                for name in archive.namelist():
                    if Path(name).parent.as_posix() != f"{mode}/execution" or not name.endswith(".log"):
                        continue
                    for key, value in clean.log_counts(archive.read(name).decode("utf-8", errors="replace")).items():
                        previous_counts[mode][key] += value
    result = {"validity_policy": clean.POLICY, "pairs_per_configuration": 50, "compared_files": len(rows),
              "all_identical": identical, "files": rows, "differences": differences,
              "excluded_pairs": {mode: state.get("excluded_pairs", []) for mode, state in states.items()},
              "all_attempt_counts": counts, "previous_comparison_counts": previous_counts,
              "selected_configuration": selected_mode,
              "selection_rule": "fewest_invalidating_log_lines_over_all_attempts; tie=default; requires_50_clean_pairs_in_both",
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
    lines = ["# Comparacion de configuraciones con intentos limpios", "",
             "Cinco niveles (600001..600005), diez repeticiones por entorno, un trabajador, procesos headless secuenciales.",
             "default: variable DOTNET_TieredCompilation ausente. disabled: DOTNET_TieredCompilation=0.", "",
             f'Resultado: {"IDENTICOS" if identical else "DIFERENCIAS O PARES SIN INTENTO LIMPIO"}.',
             f'Archivos comparados: {len(rows)} de 105 (100 partidas y cinco mapas). Solo se comparan los primeros intentos completos limpios.',
             "Los datos rechazados se conservan en results.zip y se enumeran; nunca se consideran evidencia de igualdad ni entran en las medias.", "",
             "| Configuracion | Intentos | Lineas GCHandle | ERROR: System. | Illegal instruction | Lineas invalidantes sin duplicar |",
             "|---|---|---|---|---|---|"]
    for mode, totals in counts.items():
        lines.append(f'| {mode} | {len(states[mode]["attempts"])} | {totals["gchandle_lines"]} | {totals["system_error_lines"]} | {totals["illegal_instruction_lines"]} | {totals["invalidating_lines"]} |')
    lines += ["", "Los recuentos incluyen pila y backtrace: son lineas, no excepciones independientes. Distintos numeros de intentos y partidas expuestas impiden inferir una reduccion causal de excepciones.",
              "Comparacion anterior: 0 lineas GCHandle sin variable (un intento), 16 con valor 0 (dos intentos: 1 fatal + 15 de cinco excepciones no fatales). El par 600005:2 se contamino con valor 0.", ""]
    if differences:
        lines += ["## Diferencias entre intentos validos", ""]
        for difference in differences:
            lines.append(f'- `{difference["file"]}`: comparison.json conserva ambos objetos completos para localizar cada campo distinto.')
    if identical:
        lines += [f'Configuracion elegida: {selected_mode}, por menor numero total de lineas invalidantes; empate a favor de default.', ""]
    else:
        lines += ["La comprobacion de igualdad no se cumple; no se lanza la curva completa.", ""]
    for mode, state in states.items():
        lines += [f'## Configuracion {mode}', "", *clean.report_lines(state), ""]
    lines += ["Protocolo y plan originales intactos. results.zip conserva todos los intentos, resultados, recursos, estados y logs; comparison.json registra su SHA-256."]
    (folder / "README.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    if not identical:
        raise ValueError("Intentos limpios diferentes o comparacion incompleta; revisar comparison.json")
    print(f'IDENTICOS: 50 pares limpios por configuracion; configuracion elegida: {selected_mode}.')
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("folder", type=Path)
    compare(parser.parse_args().folder.resolve())

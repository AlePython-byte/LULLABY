"""Verifica el informe y el denominador fijo cuando se excluye un lote."""
import json
import tempfile
import unittest
import zipfile
from pathlib import Path

import clean_repetition_v3 as clean
import run_repetition_curve_v3 as runner


class CleanReportTest(unittest.TestCase):
    def test_excluded_batch_reduces_levels_without_filling_or_replacing_runs(self):
        archive_path = clean.curve.ROOT / "ai-training/reports/v3/tiered_comparison_20261031_01/results.zip"
        with tempfile.TemporaryDirectory() as temporary, zipfile.ZipFile(archive_path) as archive:
            folder = Path(temporary)
            batch = folder / "execution/batch"
            batch.mkdir(parents=True)
            prefix = "default/execution/batch_600001_attempt_1/"
            for name in archive.namelist():
                if name.startswith(prefix) and name.endswith(".json"):
                    (batch / Path(name).name).write_bytes(archive.read(name))
            log = folder / "execution/clean.log"
            log.write_bytes(archive.read("default/execution/batch_600001_attempt_1.log"))
            attempt = {"first_seed": 600001, "last_seed": 600005, "attempt": 1, "folder": "execution/batch", "log": "execution/clean.log",
                       "status": "complete", "exit_code": 0, "log_sha256": clean.curve.digest(log), "incidents": []}
            rejected = []
            for number in range(1, 4):
                path = folder / f"execution/rejected_{number}.log"
                path.write_text("CURVE_BEGIN seed=600006 run=0 phase=real\nGCHandle\n", encoding="utf-8")
                rejected.append({"first_seed": 600006, "last_seed": 600010, "attempt": number,
                                 "status": "invalid", "exit_code": 0, "rejection_reasons": ["gchandle"],
                                 "log": str(path.relative_to(folder)), "incidents": []})
            state = {"config": {"levels": 10, "validity_policy": clean.POLICY, "tiered_compilation": "default"},
                     "completed_batches": {"600001": "execution/batch"}, "attempts": [attempt, *rejected],
                     "excluded_pairs": clean.exclusions(600006, rejected), "started": 0, "finished": 1}
            clean.write_report(folder, state, runner.validate_batch, runner.save)
            result = json.loads((folder / "analysis.json").read_text(encoding="utf-8"))
            self.assertEqual(result["included_levels"], list(range(600001, 600006)))
            self.assertEqual(result["levels"], 5)
            self.assertEqual([row["repetitions"] for row in result["curve"]], [3, 5, 10])
            self.assertEqual(len(result["excluded_pairs"]), 50)
            self.assertEqual(result["all_attempt_counts"]["gchandle_lines"], 3)
            self.assertEqual(len(result["excluded_pairs"][0]["attempts"]), 3)
            self.assertIn("Niveles incluidos: 5 de 10", (folder / "README.md").read_text(encoding="utf-8"))

    def test_empty_cohort_produces_report_without_fake_correlations(self):
        with tempfile.TemporaryDirectory() as temporary:
            folder = Path(temporary)
            state = {"config": {"levels": 30, "validity_policy": clean.POLICY, "tiered_compilation": "default"},
                     "completed_batches": {}, "attempts": [], "excluded_pairs": [], "started": 0, "finished": 1}
            clean.write_report(folder, state, runner.validate_batch, runner.save)
            result = json.loads((folder / "analysis.json").read_text(encoding="utf-8"))
            self.assertEqual(result["reason"], "insufficient_clean_levels")
            self.assertEqual(result["curve"], [])


if __name__ == "__main__":
    unittest.main()

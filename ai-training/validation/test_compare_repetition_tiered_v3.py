"""Comprueba que solo logs limpios pueden sustentar igualdad."""
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import compare_repetition_tiered_v3 as comparison


class CompareTest(unittest.TestCase):
    def fixture(self, folder, difference=False, dirty=False):
        for mode in ("default", "disabled"):
            batch = folder / mode / "execution/batch"
            batch.mkdir(parents=True)
            log = batch.parent / "attempt.log"
            log.write_text("GCHandle\n" if dirty and mode == "disabled" else "CURVE_COMPLETE pairs=50\n", encoding="utf-8")
            attempt = {"first_seed": 600001, "last_seed": 600005, "attempt": 1, "folder": "execution/batch", "log": "execution/attempt.log",
                       "status": "complete", "exit_code": 0, "log_sha256": comparison.curve.digest(log), "incidents": []}
            (batch.parent / "state.json").write_text(json.dumps({
                "status": "complete", "config": {"levels": 5, "tiered_compilation": "default" if mode == "default" else "0", "validity_policy": comparison.clean.POLICY},
                "completed_batches": {"600001": "execution/batch"}, "attempts": [attempt]}), encoding="utf-8")
            (batch / "manifest.json").write_text('{"fingerprints": {}}', encoding="utf-8")
            for seed in range(600001, 600006):
                (batch / f"level_{seed}.json").write_text('{"map": {}}', encoding="utf-8")
                for run in range(10):
                    for env in ("sim", "real"):
                        row = {"damage_total": 10, "visits": [mode if difference and env == "real" and seed == 600001 and run == 0 else "same"]}
                        (batch / f"{env}_{seed}_{run}.json").write_text(json.dumps(row), encoding="utf-8")

    @patch.object(comparison.clean, "load_selected_data")
    @patch.object(comparison.runner, "validate_batch")
    def test_internal_difference_is_rejected_even_if_totals_match(self, validate, load):
        with tempfile.TemporaryDirectory() as temporary:
            folder = Path(temporary)
            self.fixture(folder, difference=True)
            with self.assertRaisesRegex(ValueError, "Intentos limpios diferentes"):
                comparison.compare(folder)
            report = json.loads((folder / "comparison.json").read_text(encoding="utf-8"))
            self.assertFalse(report["all_identical"])
            self.assertEqual(report["differences"][0]["file"], "real_600001_0.json")

    @patch.object(comparison.clean, "load_selected_data")
    @patch.object(comparison.runner, "validate_batch")
    def test_clean_identical_results_pass_and_tie_selects_default(self, validate, load):
        with tempfile.TemporaryDirectory() as temporary:
            folder = Path(temporary)
            self.fixture(folder)
            result = comparison.compare(folder)
            self.assertTrue(result["all_identical"])
            self.assertEqual(result["compared_files"], 105)
            self.assertEqual(result["selected_configuration"], "default")
            self.assertEqual(result["previous_comparison_counts"]["default"]["gchandle_lines"], 0)
            self.assertEqual(result["previous_comparison_counts"]["disabled"]["gchandle_lines"], 16)

    @patch.object(comparison.clean, "load_selected_data")
    @patch.object(comparison.runner, "validate_batch")
    def test_contaminated_log_cannot_be_selected_even_with_same_results(self, validate, load):
        with tempfile.TemporaryDirectory() as temporary:
            folder = Path(temporary)
            self.fixture(folder, dirty=True)
            with self.assertRaisesRegex(ValueError, "contaminado"):
                comparison.compare(folder)


if __name__ == "__main__":
    unittest.main()

"""Detecta diferencias internas aunque los totales de ambas configuraciones coincidan."""
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import compare_repetition_tiered_v3 as comparison


class CompareTest(unittest.TestCase):
    def test_physics_warning_is_associated_with_active_pair(self):
        _, issues = comparison.runner.inspect_log("CURVE_BEGIN seed=600004 run=1 phase=real\nWARNING: Jolt Physics job system exceeded the maximum number of jobs.\n")
        self.assertEqual(issues[0]["active_pair"], {"seed": 600004, "run": 1, "phase": "real"})

    @patch.object(comparison.runner, "validate_batch")
    def test_full_result_difference_is_rejected(self, validate):
        with tempfile.TemporaryDirectory() as temporary:
            folder = Path(temporary)
            for mode in ("default", "disabled"):
                batch = folder / mode / "execution/batch"
                batch.mkdir(parents=True)
                (batch.parent / "state.json").write_text(json.dumps({
                    "status": "complete", "config": {"levels": 5, "tiered_compilation": "default" if mode == "default" else "0"},
                    "completed_batches": {"600001": "execution/batch"}, "attempts": []}), encoding="utf-8")
                (batch / "manifest.json").write_text('{"fingerprints": {}}', encoding="utf-8")
                (batch / "real_600001_0.json").write_text(json.dumps({"damage_total": 10, "visits": [mode]}), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "Resultados diferentes"):
                comparison.compare(folder)
            report = json.loads((folder / "comparison.json").read_text(encoding="utf-8"))
            self.assertFalse(report["all_identical"])
            self.assertTrue((folder / "results.zip").exists())


if __name__ == "__main__":
    unittest.main()

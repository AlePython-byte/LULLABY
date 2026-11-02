"""Pruebas de prefijos y bootstrap pareado sin ejecutar partidas."""
import copy
import json
import tempfile
import unittest
from pathlib import Path

import repetition_curve_v3 as curve


class CurveTest(unittest.TestCase):
    def setUp(self):
        self.plan = json.loads(curve.PLAN_FILE.read_text(encoding="utf-8"))
        self.plan["statistics"]["bootstrap_resamples"] = 50

    def data(self):
        return {seed: {env: [{"damage_total": seed, "time_total": seed + 10} for _ in range(10)]
                       for env in ("sim", "real")} for seed in range(1, 11)}

    def test_means_use_first_k_runs_not_best_subset_or_all_ten(self):
        data = self.data()
        for seed in data:
            for run in range(10):
                data[seed]["real"][run]["damage_total"] = 11 - seed if run < 3 else 10 * seed
        result = curve.curve_statistics(data, self.plan)
        self.assertEqual([r["damage_total"]["spearman"] for r in result["curve"]], [-1, 1, 1])
        self.assertTrue(result["damage_at_10_reaches_target"])
        difference = next(r for r in result["paired_differences"] if r["label"] == "damage_total" and r["prefix_difference"] == "10-3")
        self.assertEqual(difference["delta_spearman"], 2)
        self.assertEqual(difference["ci95"]["low"], 2)
        self.assertEqual(difference["ci95"]["high"], 2)

    def test_paired_levels_preserve_identical_environment_correlation(self):
        result = curve.curve_statistics(self.data(), self.plan)
        for row in result["curve"]:
            for label in ("damage_total", "time_total"):
                self.assertEqual(row[label]["ci95"]["low"], 1)
                self.assertEqual(row[label]["ci95"]["high"], 1)
        self.assertTrue(result["not_an_acceptance_verdict"])

    def test_undefined_correlation_is_never_a_pass(self):
        data = self.data()
        for seed in data:
            for row in data[seed]["real"]:
                row["damage_total"] = 5
        result = curve.curve_statistics(data, self.plan)
        self.assertFalse(result["damage_at_10_reaches_target"])
        self.assertIsNone(result["curve"][-1]["damage_total"]["spearman"])
        self.assertEqual(result["curve"][-1]["damage_total"]["ci95"]["valid_resamples"], 0)

    def test_bootstrap_reproducible_and_does_not_mutate_data(self):
        data = self.data()
        original = copy.deepcopy(data)
        self.assertEqual(curve.curve_statistics(data, self.plan), curve.curve_statistics(data, self.plan))
        self.assertEqual(data, original)

    def test_incomplete_batch_is_rejected_before_statistics(self):
        plan = json.loads(curve.PLAN_FILE.read_text(encoding="utf-8"))
        with tempfile.TemporaryDirectory() as temporary:
            folder = Path(temporary)
            manifest = {"plan": plan, "plan_sha256": curve.digest(curve.PLAN_FILE),
                        "protocol_sha256": curve.PROTOCOL_SHA256, "worker_count": 1,
                        "concurrent_environments": False, "diagnostic_only": True,
                        "mode": "development_repetition_curve_diagnostic"}
            (folder / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
            (folder / "status.json").write_text(json.dumps({"status": "repetition_curve_complete_pending_analysis", "completed_pairs": 299}), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "incompleta"):
                curve.load_curve(folder, plan)


if __name__ == "__main__":
    unittest.main()

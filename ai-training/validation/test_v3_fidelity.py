"""Casos de aceptacion estadistica con datos sinteticos, sin combates ni datos reservados."""
import copy
import json
import tempfile
import unittest
from pathlib import Path

import v3_fidelity as fidelity


class FidelityCriteriaTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.protocol = json.loads(fidelity.PROTOCOL_FILE.read_text(encoding="utf-8"))
        # Pruebas rapidas: el ejecutable usa siempre los 2000 remuestreos del protocolo.
        cls.protocol["uncertainty"]["resamples"] = 50

    def fixtures(self):
        groups = self.protocol["criteria"]["composition_time"]["groups"]
        pairs = []
        for seed in range(600001, 600031):
            for run in range(3):
                visits = [{"cell": [i, 0], "enter_time": 0, "clear_time": 10,
                           "damage": 1, "damage_by_source": [1, 0, 0], "contact_by_kind": [1, 0, 0, 0, 0, 0]}
                          for i in range(len(groups))]
                result = {"damage_total": seed - 599990, "damage_critical": 3, "time_total": seed - 599900,
                          "time_to_boss": 50, "damage_by_source": [seed - 599990, 0, 0],
                          "hits_by_source": [10, 0, 0], "visits": visits, "timed_out": False}
                pairs.append({"seed": seed, "run": run,
                              "map": {"rooms": [{"cell": [i, 0], "categories": [g]} for i, g in enumerate(groups)]},
                              "sim": copy.deepcopy(result), "real": copy.deepcopy(result)})
        return pairs

    def test_equal_results_pass_and_rare_sources_are_not_claimed_validated(self):
        result = fidelity.evaluate(self.fixtures(), self.protocol, complete=True)
        self.assertEqual(result["verdict"], "pass")
        self.assertEqual(result["sources"]["projectile"]["verdict"], "insufficient_evidence")
        self.assertEqual(result["sources"]["explosion"]["verdict"], "insufficient_evidence")

    def test_contact_bias_blocks_even_with_perfect_rank_correlation(self):
        pairs = self.fixtures()
        for pair in pairs:
            pair["sim"]["damage_total"] *= 0.74
            pair["sim"]["damage_by_source"][0] *= 0.74
        result = fidelity.evaluate(pairs, self.protocol, complete=True)
        self.assertAlmostEqual(result["correlations"]["damage_total"]["spearman"], 1)
        self.assertIn("source_contact", result["failures"])
        self.assertEqual(result["verdict"], "fail")

    def test_room_bias_is_not_hidden_by_aggregate_time(self):
        pairs = self.fixtures()
        for pair in pairs:
            pair["sim"]["visits"][-1]["clear_time"] = 14
        result = fidelity.evaluate(pairs, self.protocol, complete=True)
        self.assertIn("composition_with_digger", result["failures"])

    def test_thirty_real_hits_activate_projectile_criterion(self):
        pairs = self.fixtures()
        for pair in pairs[:30]:
            pair["real"]["hits_by_source"][1] = 1
            pair["real"]["damage_by_source"][1] = 1
        result = fidelity.evaluate(pairs, self.protocol, complete=True)
        self.assertEqual(result["sources"]["projectile"]["verdict"], "fail")

    def test_pilot_can_never_pass_gate(self):
        result = fidelity.evaluate(self.fixtures()[:1], self.protocol, complete=False)
        self.assertEqual(result["verdict"], "incomplete_not_a_quality_gate")

    def test_constant_series_does_not_pass_spearman(self):
        pairs = self.fixtures()
        for pair in pairs:
            pair["sim"]["damage_total"] = 10
        result = fidelity.evaluate(pairs, self.protocol, complete=True)
        self.assertIsNone(result["correlations"]["damage_total"]["spearman"])
        self.assertIn("spearman_damage_total", result["failures"])

    def test_zero_real_contact_is_undefined_not_pass(self):
        pairs = self.fixtures()
        for pair in pairs:
            pair["real"]["hits_by_source"][0] = pair["real"]["damage_by_source"][0] = 0
        result = fidelity.evaluate(pairs, self.protocol, complete=True)
        self.assertEqual(result["sources"]["contact"]["verdict"], "fail")

    def test_bootstrap_is_paired_by_level_and_reproducible(self):
        uncertainty = self.protocol["uncertainty"]
        interval = fidelity.bootstrap_bias([7.5, 75], [10, 100], uncertainty)
        self.assertEqual(interval["low"], -0.25)
        self.assertEqual(interval["high"], -0.25)
        self.assertEqual(interval, fidelity.bootstrap_bias([7.5, 75], [10, 100], uncertainty))

    def test_ties_receive_average_ranks(self):
        self.assertEqual(fidelity.ranks([2, 1, 2]), [2.5, 1, 2.5])

    def test_missing_rooms_cannot_be_declared_complete(self):
        pair = self.fixtures()[0]
        fidelity.validate_result(pair["real"], pair["map"])
        pair["real"]["visits"].pop()
        with self.assertRaises(ValueError):
            fidelity.validate_result(pair["real"], pair["map"])

    def experiment(self, folder):
        pair = self.fixtures()[0]
        seed, run = pair["seed"], pair["run"]
        bot_seed = (seed * 0x9E3779B97F4A7C15 + run + 1) & 0xFFFFFFFFFFFFFFFF
        data = {"requested_seed": seed, "effective_seed": seed, "run": run, "bot_seed": str(bot_seed)}
        files = {"manifest.json": {"protocol_sha256": fidelity.PROTOCOL_SHA256, "protocol": self.protocol,
                                   "mode": "pilot", "worker_count": 1, "concurrent_environments": False},
                 f"level_{seed}.json": {"effective_seed": seed, "map": pair["map"]},
                 f"sim_{seed}_{run}.json": dict(data, result=pair["sim"]),
                 f"real_{seed}_{run}.json": dict(data, result=pair["real"])}
        for name, value in files.items():
            (folder / name).write_text(json.dumps(value), encoding="utf-8")
        status = {"status": "pilot_complete_not_a_quality_gate", "completed_pairs": 1,
                  "data_sha256": {name: fidelity.digest(folder / name) for name in files}}
        (folder / "status.json").write_text(json.dumps(status), encoding="utf-8")

    def test_saved_pilot_remains_incomplete(self):
        with tempfile.TemporaryDirectory() as temporary:
            folder = Path(temporary)
            self.experiment(folder)
            pairs, _, _, complete = fidelity.load_experiment(folder, self.protocol)
            self.assertEqual(len(pairs), 1)
            self.assertFalse(complete)

    def test_changed_artifact_is_rejected_before_analysis(self):
        with tempfile.TemporaryDirectory() as temporary:
            folder = Path(temporary)
            self.experiment(folder)
            real = folder / "real_600001_0.json"
            real.write_text(real.read_text(encoding="utf-8") + " ", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "Artefacto modificado"):
                fidelity.load_experiment(folder, self.protocol)

    def test_missing_hash_is_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            folder = Path(temporary)
            self.experiment(folder)
            status = json.loads((folder / "status.json").read_text(encoding="utf-8"))
            status["data_sha256"].pop("real_600001_0.json")
            (folder / "status.json").write_text(json.dumps(status), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "hashes"):
                fidelity.load_experiment(folder, self.protocol)


if __name__ == "__main__":
    unittest.main()

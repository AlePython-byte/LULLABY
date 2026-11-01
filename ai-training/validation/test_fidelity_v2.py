"""Pruebas sinteticas v2. No ejecuta Godot ni abre resultados reservados."""
import copy
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock, patch

import fidelity_v2 as fidelity
import run_fidelity_v2 as runner


BEGIN = "FIDELITY_V2_BEGIN orchestration=2\n"
END = "V2_BATCH_COMPLETE mode=preflight pairs=1\n"


class LogTests(unittest.TestCase):
    def setUp(self):
        self.protocol = fidelity.load_protocol()

    def test_only_exact_complete_startup_line_is_allowed(self):
        result = fidelity.scan_log(fidelity.CERTIFICATE_LINE + "\n" + BEGIN + END, self.protocol)
        self.assertTrue(result["valid"])
        self.assertEqual(result["allowed_certificate_lines"], 1)

    def test_variants_spaces_case_suffix_and_prefix_are_rejected(self):
        for line in (" " + fidelity.CERTIFICATE_LINE, fidelity.CERTIFICATE_LINE + " ",
                     fidelity.CERTIFICATE_LINE.lower(), fidelity.CERTIFICATE_LINE + " Extra", "prefix " + fidelity.CERTIFICATE_LINE):
            with self.subTest(line=line):
                self.assertFalse(fidelity.scan_log(line + "\n" + BEGIN, self.protocol)["valid"])

    def test_same_line_during_measurement_is_not_allowed(self):
        self.assertFalse(fidelity.scan_log(BEGIN + fidelity.CERTIFICATE_LINE, self.protocol)["valid"])

    def test_second_startup_occurrence_is_not_allowed(self):
        result = fidelity.scan_log((fidelity.CERTIFICATE_LINE + "\n") * 2 + BEGIN, self.protocol)
        self.assertFalse(result["valid"])
        self.assertEqual(result["allowed_certificate_lines"], 1)

    def test_other_error_gchandle_and_illegal_instruction_always_block(self):
        for line in ("ERROR: Something else", "ERROR: System.InvalidOperationException", "at GCHandle.FromIntPtr", "Illegal instruction"):
            with self.subTest(line=line):
                self.assertFalse(fidelity.scan_log(BEGIN + line, self.protocol)["valid"])

    def test_missing_or_repeated_start_marker_blocks(self):
        self.assertFalse(fidelity.scan_log(fidelity.CERTIFICATE_LINE, self.protocol)["valid"])
        self.assertFalse(fidelity.scan_log(BEGIN * 2, self.protocol)["valid"])

    def test_warning_is_preserved_without_becoming_an_error(self):
        result = fidelity.scan_log(BEGIN + "WARNING: Jolt capacity\n", self.protocol)
        self.assertTrue(result["valid"])
        self.assertEqual(result["issues"][0]["kind"], "warning")

    def test_error_is_associated_with_active_pair(self):
        result = fidelity.scan_log(BEGIN + "V2_PAIR_BEGIN seed=600001 run=0 phase=real\nERROR: Other\n", self.protocol)
        self.assertEqual(result["blocking"][0]["active_pair"], {"seed": 600001, "run": 0, "phase": "real"})


def fixture_pairs(first=610001, count=90, runs=10):
    pairs = []
    groups = fidelity.load_protocol()["criteria"]["composition_time"]["groups"]
    for seed in range(first, first + count):
        offset = seed - first + 1
        for run in range(runs):
            damage = offset + run % 3
            duration = 10 + offset
            visits = [{"cell": [i, 0], "enter_time": i * duration, "clear_time": (i + 1) * duration,
                       "damage": damage if i == 0 else 0, "damage_by_source": [damage if i == 0 else 0, 0, 0],
                       "contact_by_kind": [damage if i == 0 else 0, 0, 0, 0, 0, 0]} for i in range(6)]
            result = {"damage_total": damage, "damage_critical": damage, "time_total": duration * 6,
                      "time_to_boss": duration * 5, "timed_out": False, "would_die": damage >= 5,
                      "damage_by_source": [damage, 0, 0], "hits_by_source": [damage, 0, 0], "visits": visits}
            pairs.append({"seed": seed, "run": run, "map": {"rooms": [{"cell": [i, 0], "categories": [group]} for i, group in enumerate(groups)]},
                          "sim": copy.deepcopy(result), "real": copy.deepcopy(result)})
    return pairs


class CriteriaTests(unittest.TestCase):
    def setUp(self):
        self.protocol = fidelity.load_protocol()
        self.protocol["uncertainty"]["resamples"] = 20
        self.pairs = fixture_pairs()

    def evaluate(self, pairs=None, **kwargs):
        return fidelity.evaluate_pairs(self.pairs if pairs is None else pairs, self.protocol, "acceptance", 900,
                                       kwargs.pop("log_valid", True), **kwargs)

    def test_ninety_complete_levels_pass_without_claiming_rare_sources(self):
        result = self.evaluate()
        self.assertEqual(result["verdict"], "pass")
        self.assertTrue(result["complete_sample"])
        self.assertEqual(result["sources"]["projectile"]["verdict"], "insufficient_evidence")
        self.assertEqual(result["correlations"]["damage_total"]["ci95"]["unit"], "level")

    def test_one_missing_pair_or_level_cannot_pass(self):
        for pairs in (self.pairs[:-1], self.pairs[:-10]):
            self.assertFalse(self.evaluate(pairs)["quality_gate_evaluated"])

    def test_duplicate_pair_cannot_fill_missing_identity(self):
        self.assertFalse(self.evaluate(self.pairs[:-1] + [self.pairs[0]])["complete_sample"])

    def test_exclusions_block_despite_good_correlations(self):
        self.assertFalse(self.evaluate(exclusions=[{"seed": 610001, "run": 0}])["complete_sample"])

    def test_bad_log_blocks_despite_all_results(self):
        self.assertFalse(self.evaluate(log_valid=False)["complete_sample"])

    def test_quality_failure_blocks_even_all_results_and_good_rho(self):
        self.assertEqual(self.evaluate(quality_failures=True)["verdict"], "fail_quality")

    def test_contact_twenty_six_percent_bias_fails(self):
        for pair in self.pairs:
            pair["sim"]["damage_by_source"][0] *= 0.74
            pair["sim"]["damage_total"] *= 0.74
        self.assertIn("source_contact", self.evaluate()["failures"])

    def test_composition_time_thirty_one_percent_bias_fails(self):
        for pair in self.pairs:
            visit = pair["sim"]["visits"][-1]
            visit["clear_time"] = visit["enter_time"] + (visit["clear_time"] - visit["enter_time"]) * 1.31
        self.assertIn("composition_with_digger", self.evaluate()["failures"])

    def test_thirty_real_projectile_hits_activate_bias_gate(self):
        for pair in self.pairs[:30]:
            pair["real"]["hits_by_source"][1] = 1
            pair["real"]["damage_by_source"][1] = 1
            pair["real"]["damage_total"] += 1
        self.assertEqual(self.evaluate()["sources"]["projectile"]["verdict"], "fail")

    def test_one_pair_preflight_never_approves_acceptance(self):
        result = fidelity.evaluate_pairs(fixture_pairs(600001, 1, 1), self.protocol, "preflight", 1, True)
        self.assertTrue(result["preflight_valid"])
        self.assertFalse(result["quality_gate_evaluated"])
        self.assertFalse(result["acceptance_authorized_by_preflight"])


class ArtifactTests(unittest.TestCase):
    def experiment(self, folder):
        protocol = fidelity.load_protocol()
        pair = fixture_pairs(600001, 1, 1)[0]
        seed, run = pair["seed"], pair["run"]
        bot = str((seed * 0x9E3779B97F4A7C15 + 1) & 0xFFFFFFFFFFFFFFFF)
        files = {"manifest.json": {"schema_version": 2, "bot_policy_version": 1, "acceptance_authorized_by_preflight": False,
                 "mode": "preflight", "protocol": protocol, "protocol_sha256": fidelity.PROTOCOL_SHA256,
                 "worker_count": 1, "concurrent_environments": False, "batch_first_seed": seed, "batch_level_count": 1,
                 "runs": 1, "diagnostic_only": True, "physics_ticks_per_second": 60, "time_scale": 1,
                 "tiered_compilation_environment": "unset", "fingerprints": {}},
                 "coverage.json": {"all_sample_coverage_executed": False, "reserve_used": False},
                 f"level_{seed}.json": {"requested_seed": seed, "effective_seed": seed, "map": pair["map"]}}
        for env in ("sim", "real"):
            files[f"{env}_{seed}_{run}.json"] = {"requested_seed": seed, "effective_seed": seed, "run": run, "bot_seed": bot, "result": pair[env]}
        files[f"resources_{seed}_{run}.json"] = {"sim_seconds": 0.1, "real_seconds": 3}
        for name, value in files.items():
            (folder / name).write_text(json.dumps(value), encoding="utf-8")
        runner.save(folder / "status.json", {"status": "batch_complete_pending_log_validation", "completed_pairs": 1,
                    "data_sha256": {name: fidelity.digest(folder / name) for name in files}})
        return protocol

    def test_complete_one_pair_batch_validates(self):
        with tempfile.TemporaryDirectory() as temporary:
            folder = Path(temporary)
            protocol = self.experiment(folder)
            pairs, _, _ = fidelity.validate_batch(folder, "preflight", 600001, 1, 1, protocol)
            self.assertEqual(len(pairs), 1)

    def test_altered_or_missing_artifact_blocks(self):
        for missing in (False, True):
            with tempfile.TemporaryDirectory() as temporary:
                folder = Path(temporary)
                protocol = self.experiment(folder)
                path = folder / "real_600001_0.json"
                if missing:
                    path.unlink()
                else:
                    path.write_text(path.read_text(encoding="utf-8") + " ", encoding="utf-8")
                with self.assertRaises(ValueError):
                    fidelity.validate_batch(folder, "preflight", 600001, 1, 1, protocol)

    def test_uncleared_room_cannot_pass_even_with_new_hash(self):
        with tempfile.TemporaryDirectory() as temporary:
            folder = Path(temporary)
            protocol = self.experiment(folder)
            path = folder / "real_600001_0.json"
            value = json.loads(path.read_text(encoding="utf-8"))
            value["result"]["visits"].pop()
            runner.save(path, value)
            status = json.loads((folder / "status.json").read_text(encoding="utf-8"))
            status["data_sha256"][path.name] = fidelity.digest(path)
            runner.save(folder / "status.json", status)
            with self.assertRaisesRegex(ValueError, "salas pendientes"):
                fidelity.validate_batch(folder, "preflight", 600001, 1, 1, protocol)


class OrchestrationTests(unittest.TestCase):
    def setUp(self):
        self.protocol = fidelity.load_protocol()

    def test_unapproved_acceptance_never_creates_output_or_launches(self):
        with tempfile.TemporaryDirectory() as temporary, patch.object(runner.subprocess, "Popen") as launch:
            target = Path(temporary) / "not_created"
            with self.assertRaisesRegex(ValueError, "autorizacion externa"):
                runner.execute(target, Path("godot.exe"), "acceptance")
            launch.assert_not_called()
            self.assertFalse(target.exists())

    def fake_process(self, text, code=0):
        def launch(command, **kwargs):
            native = Path(command[command.index("--log-file") + 1])
            native.write_text(text, encoding="utf-8")
            process = Mock()
            process.stdout = iter(text.splitlines(keepends=True))
            process.wait.return_value = code
            return process
        return launch

    def run_fixture(self, text, mode="preflight", code=0):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        folder = Path(temporary.name)
        execution = folder / "execution"
        execution.mkdir()
        auth = None
        auth_hash = None
        if mode != "preflight":
            auth = folder / "authorization.json"
            runner.save(auth, {"mode": mode, "protocol_sha256": fidelity.PROTOCOL_SHA256, "user_approved": True,
                               "machine_confirmed_free": True, "approved_at_utc": "2026-11-01T00:00:00Z"})
            auth_hash = fidelity.authorization(auth, mode)
        with patch.object(runner, "assert_godot_idle"), patch.object(fidelity, "verify_sources", return_value={}), \
             patch.object(fidelity, "validate_batch", return_value=([], {"fingerprints": {}}, {})), \
             patch.object(fidelity, "analyze", return_value={}), \
             patch.object(runner.subprocess, "Popen", side_effect=self.fake_process(text, code)) as process:
            runner.execute_locked(folder, execution, Path("godot.exe"), mode, auth, auth_hash, self.protocol)
            state = json.loads((execution / "state.json").read_text(encoding="utf-8"))
            calls = list(process.call_args_list)
        return state, calls

    def test_preflight_executes_one_pair_only_with_unset_tiered(self):
        state, calls = self.run_fixture(fidelity.CERTIFICATE_LINE + "\n" + BEGIN + END)
        self.assertEqual(len(calls), 1)
        self.assertEqual(state["status"], "preflight_complete")
        self.assertNotIn("DOTNET_TieredCompilation", calls[0].kwargs["env"])
        self.assertIn("--level-count=1", calls[0].args[0])
        self.assertIn("--mode=preflight", calls[0].args[0])

    def test_preflight_does_not_retry_an_unclean_pair(self):
        state, calls = self.run_fixture(BEGIN + "ERROR: System.InvalidOperationException\n" + END)
        self.assertEqual(len(calls), 1)
        self.assertEqual(state["status"], "incomplete_retry_limit")
        self.assertEqual(state["excluded_pairs"][0]["seed"], 600001)

    def test_native_channel_alone_can_invalidate(self):
        with tempfile.TemporaryDirectory() as temporary:
            folder = Path(temporary)
            (folder / "stdout.log").write_text(BEGIN + END, encoding="utf-8")
            (folder / "native.log").write_text(BEGIN + "ERROR: Other\n" + END, encoding="utf-8")
            attempt = {"log": "stdout.log", "engine_log": "native.log", "folder": "batch"}
            self.assertFalse(runner.record_attempt(folder, attempt, 0, self.protocol))

    def test_three_unclean_acceptance_attempts_stop_with_explicit_exclusions(self):
        state, calls = self.run_fixture(BEGIN + "GCHandle\n", "acceptance", code=1)
        self.assertEqual(len(calls), 3)
        self.assertEqual(len(state["excluded_pairs"]), 50)
        self.assertEqual(len(state["unstarted_pairs"]), 850)
        self.assertEqual(state["status"], "incomplete_retry_limit")

    def test_quality_failure_is_not_hidden_by_three_technical_retries(self):
        state, calls = self.run_fixture(BEGIN + "V2_QUALITY_FAILURE\nERROR: Timeout\n", "acceptance", code=1)
        self.assertEqual(len(calls), 1)
        self.assertEqual(state["status"], "failed_quality")

    def resume_fixture(self, calls, mode):
        command = calls[0].args[0]
        folder = Path(command[command.index("--log-file") + 1]).parent.parent
        auth = folder / "authorization.json" if mode != "preflight" else None
        with patch.object(runner, "assert_godot_idle"), patch.object(fidelity, "verify_sources", return_value={}), \
             patch.object(fidelity, "validate_batch"), patch.object(fidelity, "analyze", return_value={}), \
             patch.object(runner.subprocess, "Popen") as launch:
            runner.execute_locked(folder, folder / "execution", Path("godot.exe"), mode, auth,
                                  fidelity.authorization(auth, mode), self.protocol)
            launch.assert_not_called()
        return json.loads((folder / "execution/state.json").read_text(encoding="utf-8"))

    def test_valid_resume_does_not_repeat_selected_pair(self):
        _, calls = self.run_fixture(BEGIN + END)
        result = self.resume_fixture(calls, "preflight")
        self.assertEqual(result["status"], "preflight_complete")
        self.assertEqual(len(result["attempts"]), 1)

    def test_interrupted_quality_failure_is_not_retried_on_resume(self):
        state, calls = self.run_fixture(BEGIN + "V2_QUALITY_FAILURE\nERROR: Timeout\n", "acceptance", code=1)
        command = calls[0].args[0]
        path = Path(command[command.index("--log-file") + 1]).parent / "state.json"
        state["status"] = "running"
        state["attempts"][-1]["status"] = "running"
        runner.save(path, state)
        result = self.resume_fixture(calls, "acceptance")
        self.assertEqual(result["status"], "failed_quality")
        self.assertEqual(len(result["attempts"]), 1)


if __name__ == "__main__":
    unittest.main()

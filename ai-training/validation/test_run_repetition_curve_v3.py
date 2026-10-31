"""Comprueba recuperacion de fallos y reanudacion sin ejecutar combates."""
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock, patch

import run_repetition_curve_v3 as runner


class RunnerTest(unittest.TestCase):
    def test_fatal_only_when_process_dies(self):
        self.assertFalse(runner.fatal_exit(0, "GCHandle"))
        self.assertFalse(runner.fatal_exit(1, "timeout"))
        self.assertTrue(runner.fatal_exit(1, "GCHandle.FromIntPtr"))
        self.assertTrue(runner.fatal_exit(1, "Illegal instruction"))
        self.assertTrue(runner.fatal_exit(-1073741795, ""))

    def test_incident_keeps_seed_run_and_phase(self):
        active, issues = runner.inspect_log("CURVE_BEGIN seed=600008 run=1 phase=real\nFatal error. GCHandle\n")
        self.assertEqual(active, {"seed": 600008, "run": 1, "phase": "real"})
        self.assertEqual(issues[0]["active_pair"], active)

    def run_fake(self, codes):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        folder = Path(temporary.name)
        execution = folder / "execution"
        execution.mkdir()
        processes = []
        for code in codes:
            process = Mock()
            process.stdout = iter(["CURVE_BEGIN seed=600001 run=2 phase=real\n", "GCHandle\n"] if code else ["CURVE_COMPLETE pairs=50\n"])
            process.wait.return_value = code
            processes.append(process)
        return folder, execution, processes

    @patch.object(runner, "assert_godot_idle")
    @patch.object(runner, "validate_batch")
    def test_two_retries_then_resume_skips_completed_batch(self, validate, idle):
        folder, execution, processes = self.run_fake([1, 1, 0])
        with patch.object(runner.subprocess, "Popen", side_effect=processes) as launch:
            state = runner.execute_locked(folder, execution, Path("godot.exe"), 5, "0")
            self.assertEqual(launch.call_count, 3)
            self.assertTrue(all(call.kwargs["env"]["DOTNET_TieredCompilation"] == "0" for call in launch.call_args_list))
        self.assertEqual([a["status"] for a in state["attempts"]], ["fatal", "fatal", "complete"])
        with patch.object(runner.subprocess, "Popen") as launch:
            runner.execute_locked(folder, execution, Path("godot.exe"), 5, "0")
            launch.assert_not_called()

    @patch.object(runner, "assert_godot_idle")
    def test_retry_limit_preserves_all_attempts(self, idle):
        folder, execution, processes = self.run_fake([1, 1, 1])
        with patch.object(runner.subprocess, "Popen", side_effect=processes) as launch:
            with self.assertRaisesRegex(RuntimeError, "agotados tres"):
                runner.execute_locked(folder, execution, Path("godot.exe"), 5, "0")
            self.assertEqual(launch.call_count, 3)
        state = json.loads((execution / "state.json").read_text(encoding="utf-8"))
        self.assertEqual(state["status"], "failed_retry_limit")
        self.assertEqual(len(state["attempts"]), 3)


if __name__ == "__main__":
    unittest.main()

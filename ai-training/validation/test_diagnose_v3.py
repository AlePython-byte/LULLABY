"""Comprobaciones del diagnostico exploratorio, sin ejecutar partidas."""
import unittest

from diagnose_v3 import icc, log_diagnostic


class DiagnosticTest(unittest.TestCase):
    def test_identical_repetitions_have_unit_reliability(self):
        result = icc([[1, 1, 1], [2, 2, 2], [3, 3, 3], [4, 4, 4]])
        self.assertEqual(result["single"], 1)
        self.assertEqual(result["mean_of_three"], 1)

    def test_no_between_level_signal_is_not_reported_as_reliable(self):
        result = icc([[1, 2, 3], [2, 3, 1], [3, 1, 2]])
        self.assertLess(result["single"], 0)
        self.assertIsNone(result["mean_of_three"])

    def test_log_search_is_case_insensitive_and_keeps_both_exceptions(self):
        result = log_diagnostic("Par 55/90: anterior\nERROR: primera\nGCHandle.FromIntPtr\nERROR: segunda\nILLEGAL INSTRUCTION\n")
        self.assertEqual(len(result["gchandle_lines"]), 1)
        self.assertEqual(len(result["illegal_instruction_lines"]), 1)
        self.assertEqual(len(result["errors"]), 2)
        self.assertEqual(result["errors"][1]["last_completed_pair"], "Par 55/90: anterior")
        self.assertNotIn("segunda", result["errors"][0]["block"])


if __name__ == "__main__":
    unittest.main()

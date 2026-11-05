"""Boundary checks for the shared CLI/MCP functions (standard library only)."""
import unittest
from unittest.mock import patch
import harness as h


class HarnessTests(unittest.TestCase):
    def test_scenario_must_be_inventory_member(self):
        for name in ("../rules", "../../outside.json", "C:/Windows/file.json"):
            with self.assertRaises(ValueError):
                h.scenario_path(name)

    def test_reports_reject_paths(self):
        for name in ("../report", "x/y", "C:\\outside", ""):
            with self.assertRaises(ValueError):
                h.get_report(name)

    def test_run_bounds(self):
        with patch.object(h, "execute") as execute:
            for runs in (0, -1, 10001):
                with self.assertRaises(ValueError):
                    h.core("run", runs=runs)
            with self.assertRaises(ValueError):
                h.core("run", seed=2**63)
            execute.assert_not_called()

    def test_character_stats_are_named(self):
        data = h.read_json(h.ARPG / "Data/characters.json")
        c = data["characters"][0]
        found = h.get_character(c["id"])
        self.assertEqual(found["total"], 1)
        self.assertEqual(found["characters"][0]["stats"]["HP"], c["stats"][data["statNames"].index("HP")])

    def test_reject_empty_search(self):
        with self.assertRaises(ValueError):
            h.get_character("")


if __name__ == "__main__":
    unittest.main()

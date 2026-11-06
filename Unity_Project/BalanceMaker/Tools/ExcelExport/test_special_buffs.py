"""Workbook-to-runtime regression checks for HP_RECOVERY and ADD_AGGRO."""
import tempfile
from pathlib import Path
import unittest
from unittest.mock import patch
import openpyxl
import export_tables as exporter


class SpecialBuffTests(unittest.TestCase):
    def convert(self, heal=25000, taunt=1):
        scratch = Path(__file__).resolve().parents[4] / "Tools/AIHarness/artifacts"
        scratch.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=scratch) as tmp:
            directory = Path(tmp)
            wb = openpyxl.Workbook()
            ws = wb.active
            ws.title = "Ability"
            ws.append(["Index", "ENUM_Name", "Data_Type", "~name"])
            ws.append([105, "HP_RECOVERY", 1, "heal"])
            ws.append([106, "ADD_AGGRO", 1, "taunt"])
            wb.save(directory / "ABILITY.xlsm")
            wb.close()
            wb = openpyxl.Workbook()
            ws = wb.active
            ws.title = "BUFF"
            ws.append(["Buff_ID", "is_use", "Buff_Ability1", "Buff_Ability1_Stat"])
            ws.append([5, "All", 105, heal])
            ws.append([8, "All", 106, taunt])
            wb.save(directory / "BUFF.xlsx")
            wb.close()
            report = exporter.Report()
            opened = []
            original_open = exporter.open_wb
            def track_open(path):
                pair = original_open(path)
                opened.extend(pair)
                return pair
            try:
                with patch.object(exporter, "open_wb", track_open):
                    buffs = exporter.read_buffs(str(directory), {}, report, [], [])
            finally:
                for workbook in opened:
                    workbook.close()
                    if workbook.vba_archive is not None:
                        workbook.vba_archive.close()
            return buffs, report

    def test_special_effects_without_character_stat_columns(self):
        buffs, report = self.convert()
        self.assertFalse(report.errors)
        self.assertFalse(report.warnings)
        self.assertEqual(buffs[0]["effects"][0]["stat"], "HP_RECOVERY")
        self.assertEqual(buffs[0]["effects"][0]["value"], 25000)
        self.assertEqual(buffs[1]["effects"][0]["stat"], "ADD_AGGRO")
        self.assertEqual(buffs[1]["effects"][0]["value"], 1)

    def test_invalid_recovery_and_taunt_rejected(self):
        for heal, taunt in ((-1, 1), (100001, 1), (25000, -1)):
            with self.subTest(heal=heal, taunt=taunt):
                self.assertTrue(self.convert(heal, taunt)[1].errors)

    def test_zero_disables_effect(self):
        buffs, report = self.convert(0, 0)
        self.assertFalse(report.errors)
        self.assertTrue(all(not b["effects"] for b in buffs))


if __name__ == "__main__":
    unittest.main()

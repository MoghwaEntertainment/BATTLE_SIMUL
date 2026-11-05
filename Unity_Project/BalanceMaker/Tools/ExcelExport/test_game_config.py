"""GameConfig conversion regression checks; all workbooks are temporary copies."""
import tempfile
from pathlib import Path
import unittest
from unittest.mock import patch
import openpyxl
import export_tables as exporter


class GameConfigTests(unittest.TestCase):
    def setUp(self):
        scratch = Path(__file__).resolve().parents[4] / "Tools/AIHarness/artifacts"
        scratch.mkdir(parents=True, exist_ok=True)
        self.tmp = tempfile.TemporaryDirectory(dir=scratch)
        self.directory = Path(self.tmp.name)

    def tearDown(self):
        self.tmp.cleanup()

    def convert(self, rows):
        wb = openpyxl.Workbook()
        ws = wb.active
        ws.title = "GameConfig"
        ws.append(["Index", "is_use", "Value_Type", "Value"])
        ws.append([None, None, None, None])
        for row in rows:
            ws.append(row)
        wb.save(self.directory / "GameConfig.xlsm")
        wb.close()
        report, sources = exporter.Report(), {}
        result = exporter.read_game_config(str(self.directory), sources, report)
        return result, report, sources

    def test_rate_flat_and_zero(self):
        entries, report, sources = self.convert([
            ["STUN_TIME", "All", "FLAT", 3],
            ["STUN_ADD_DAMAGE_PCT", "All", "RATE", 0],
            ["WOUND_DAMAGE_PCT", "All", "RATE", 10000],
            ["STAGGER_TIME", "All", "FLAT", 0.5],
        ])
        self.assertFalse(report.errors)
        self.assertEqual([e["value"] for e in entries], [3, 0, 0.1, 0.5])
        self.assertIn("GameConfig.xlsm", sources)

    def test_disabled_and_unknown(self):
        entries, report, _ = self.convert([["STUN_TIME", "None", "FLAT", -1], ["OTHER", "All", "FLAT", 8]])
        self.assertFalse(report.errors)
        self.assertEqual(len(entries), 1)
        self.assertEqual(len(report.warnings), 1)

    def test_duplicate_invalid_units_and_missing_cache(self):
        for rows in (
            [["STUN_TIME", "All", "FLAT", 3], ["STUN_TIME", "All", "FLAT", 4]],
            [["STUN_TIME", "All", "RATE", 100000]],
            [["FROST_SLOWDOWN_PCT", "All", "RATE", 100001]],
            [["STUN_TIME", "All", "FLAT", "=1+2"]],
        ):
            with self.subTest(rows=rows):
                self.assertTrue(self.convert(rows)[1].errors)

    def test_bad_config_does_not_publish_existing_data(self):
        tables = Path(__file__).resolve().parents[4] / "Table_ARPG"
        out = self.directory / "output"
        out.mkdir()
        sentinel = out / "gameconfig.json"
        sentinel.write_text("previous data", encoding="utf-8")
        def fail(tables_dir, sources, report):
            report.error("GameConfig", "invalid test value")
            return []
        with patch.object(exporter, "read_game_config", fail):
            report, manifest = exporter.export(str(tables), str(out), str(self.directory / "scenarios"), False)
        self.assertIsNone(manifest)
        self.assertTrue(report.errors)
        self.assertEqual(sentinel.read_text(encoding="utf-8"), "previous data")


if __name__ == "__main__":
    unittest.main()

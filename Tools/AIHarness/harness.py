"""Project-local CLI; also used by the MCP adapter. No LLM/API key needed."""
from __future__ import annotations
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
from datetime import datetime, timezone
from uuid import uuid4

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
PROJECT = ROOT / "Unity_Project/BalanceMaker"
ARPG = PROJECT / "Assets/StreamingAssets/ARPG"
OUTPUT = HERE / "artifacts"
RUNNER = HERE / "CoreRunner/CoreRunner.csproj"


def read_json(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def fingerprint():
    """Hash actual inputs; manifest hash alone does not detect unexported edits."""
    files = list((PROJECT / "Assets/ARPG/Core").rglob("*.cs"))
    files += list((ARPG / "Data").glob("*.json")) + [ARPG / "rules.json"]
    h = hashlib.sha256()
    for p in sorted(files):
        h.update(p.relative_to(ROOT).as_posix().encode())
        h.update(p.read_bytes())
    return h.hexdigest()


def save_report(kind, result):
    report_id = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ") + "_" + kind + "_" + uuid4().hex[:8]
    OUTPUT.mkdir(parents=True, exist_ok=True)
    result = {"report_id": report_id, "created_utc": datetime.now(timezone.utc).isoformat(), **result}
    (OUTPUT / (report_id + ".json")).write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    return result


def execute(args, timeout=180):
    env = {**os.environ, "PYTHONIOENCODING": "utf-8", "DOTNET_CLI_TELEMETRY_OPTOUT": "1"}
    try:
        p = subprocess.run([str(a) for a in args], cwd=ROOT, env=env, capture_output=True,
                           text=True, encoding="utf-8", errors="replace", timeout=timeout,
                           creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
        return {"ok": p.returncode == 0, "exit_code": p.returncode, "stdout": p.stdout, "stderr": p.stderr}
    except (OSError, subprocess.TimeoutExpired) as e:
        return {"ok": False, "error": str(e)}


def source_status():
    manifest = read_json(ARPG / "Data/manifest.json")
    changed = []
    for source in manifest.get("sources", []):
        p = ROOT / "Table_ARPG" / source["file"]
        if not p.is_file() or hashlib.sha256(p.read_bytes()).hexdigest() != source["sha256"]:
            changed.append(source["file"])
    return {"data_hash": manifest.get("dataHash"), "changed_sources": changed,
            "fresh": not changed}


def unity_path():
    override = os.environ.get("ARPG_UNITY_EDITOR")
    if override:
        return str(Path(override).resolve())
    version = (PROJECT / "ProjectSettings/ProjectVersion.txt").read_text().splitlines()[0].split(": ")[1]
    p = Path(os.environ.get("ProgramFiles", "C:/Program Files")) / "Unity/Hub/Editor" / version / "Editor/Unity.exe"
    return str(p) if p.is_file() else None


def doctor():
    return {"python": sys.executable, "python_version": sys.version.split()[0],
            "dotnet": shutil.which("dotnet"),
            "packages": {m: importlib.util.find_spec(m) is not None for m in ("openpyxl", "mcp", "playwright")},
            "unity_editor": unity_path(), "source_status": source_status(),
            "core_framework": "net10.0", "note": "Unity availability is not proof of Unity compilation; PNG renderer is optional."}


def list_scenarios():
    return [{"id": p.stem, "name": read_json(p).get("name")} for p in sorted((ARPG / "Scenarios").glob("*.json"))]


def scenario_path(scenario):
    files = sorted((ARPG / "Scenarios").glob("*.json"))
    if not files:
        raise ValueError("No scenario files")
    if not scenario:
        return files[0]
    # Choose from inventory, never interpret client arguments as a filesystem path.
    for p in files:
        if scenario in (p.name, p.stem):
            return p
    raise ValueError("Unknown scenario ID. Run scenarios first.")


def get_character(query, limit=10):
    if not isinstance(query, str) or not query.strip() or not 1 <= limit <= 50:
        raise ValueError("Nonempty query and limit 1..50 required")
    data = read_json(ARPG / "Data/characters.json")
    chars = data["characters"]
    exact = [c for c in chars if c["id"] == query]
    matches = exact or [c for c in chars if query.casefold() in c.get("displayName", "").casefold()]
    records = []
    for c in matches[:limit]:
        records.append({**c, "stats": dict(zip(data["statNames"], c["stats"]))})
    return {"total": len(matches), "characters": records, "source_status": source_status()}


def validate_tables():
    staging = OUTPUT / ("validation_" + uuid4().hex)
    staging.mkdir(parents=True)
    result = execute([sys.executable, PROJECT / "Tools/ExcelExport/export_tables.py",
                      "--tables", ROOT / "Table_ARPG", "--out", staging,
                      "--rules", ARPG / "rules.json", "--no-scenarios"])
    p = staging / "validation_report.md"
    if p.exists():
        result["validation_report"] = p.read_text(encoding="utf-8")
    result["source_status"] = source_status()
    result["staging"] = str(staging)
    result["runtime_data_updated"] = False
    return save_report("validate", result)


def core(mode, scenario="", runs=10, seed=12345):
    if mode not in ("test", "run") or not isinstance(runs, int) or not 1 <= runs <= 10000:
        raise ValueError("mode test/run; runs 1..10000")
    if not isinstance(seed, int) or not -(2**63) <= seed < 2**63:
        raise ValueError("seed must be a signed 64-bit integer")
    path = scenario_path(scenario)
    before = fingerprint()
    build = execute(["dotnet", "build", RUNNER, "--configfile", HERE / "CoreRunner/NuGet.Config", "--nologo"], 120)
    if not build["ok"]:
        return save_report(mode, {"ok": False, "stage": "build", "build": build})
    dll = HERE / "CoreRunner/bin/Debug/net10.0/CoreRunner.dll"
    result = execute(["dotnet", dll, mode, ARPG, path, str(runs), str(seed)], 180)
    try:
        result["result"] = json.loads(result.pop("stdout"))
    except (ValueError, KeyError):
        pass
    after = fingerprint()
    result.update({"input_hash": before, "inputs_unchanged": before == after,
                   "scenario_sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
                   "source_status": source_status(), "engine": "actual C# core under .NET; Unity runtime not exercised"})
    if before != after:
        result["ok"] = False
    return save_report(mode, result)


def unity_test():
    editor = unity_path()
    if not editor or not Path(editor).is_file():
        return save_report("unity", {"ok": False, "error": "Unity Editor not found; set ARPG_UNITY_EDITOR"})
    if (PROJECT / "Temp/UnityLockfile").exists():
        return save_report("unity", {"ok": False, "error": "Unity project is open; close it before batch verification"})
    work = OUTPUT / ("unity_" + uuid4().hex)
    work.mkdir(parents=True)
    result = execute([editor, "-batchmode", "-nographics", "-projectPath", PROJECT,
                      "-executeMethod", "ARPG.EditorTools.HarnessBatch.Verify", "-logFile", work / "editor.log",
                      "-arpgTestReport", work / "tests.json"], 180)
    p = work / "tests.json"
    result["log"] = str(work / "editor.log")
    if p.exists():
        result["result"] = read_json(p)
        result["ok"] = result["ok"] and result["result"]["failed"] == 0
    else:
        result["ok"] = False
        result["error"] = result.get("error", "Unity did not produce a test report; inspect log")
    return save_report("unity", result)


def get_report(report_id):
    if not report_id or any(c not in "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-" for c in report_id):
        raise ValueError("Invalid report ID")
    return read_json(OUTPUT / (report_id + ".json"))


def check():
    stages = {"environment": doctor(), "excel": validate_tables(), "core": core("test")}
    # Missing optional MCP/PNG packages do not invalidate combat verification.
    return save_report("check", {"ok": stages["excel"]["ok"] and stages["core"]["ok"] and stages["environment"]["source_status"]["fresh"],
                                 "stages": stages, "unity": "not run; use unity-test separately"})


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("command", choices=["doctor", "scenarios", "character", "validate", "test", "run", "unity-test", "check", "report"])
    parser.add_argument("--scenario", default="")
    parser.add_argument("--runs", type=int, default=10)
    parser.add_argument("--seed", type=int, default=12345)
    parser.add_argument("--query", default="")
    parser.add_argument("--report-id", default="")
    args = parser.parse_args()
    try:
        actions = {"doctor": doctor, "scenarios": list_scenarios, "character": lambda: get_character(args.query),
                   "validate": validate_tables, "test": lambda: core("test", args.scenario),
                   "run": lambda: core("run", args.scenario, args.runs, args.seed), "unity-test": unity_test,
                   "check": check, "report": lambda: get_report(args.report_id)}
        result = actions[args.command]()
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 1 if isinstance(result, dict) and result.get("ok") is False else 0
    except Exception as e:
        print(json.dumps({"ok": False, "error": str(e)}, ensure_ascii=False))
        return 2


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    raise SystemExit(main())

using System;
using System.Globalization;
using System.IO;
using ARPG.Core;
using ARPG.Core.Batch;
using ARPG.Core.Events;
using UnityEngine;

namespace ARPG.Runtime
{
    /// <summary>
    /// 화면 없는 실행 파일 진입점(구현계획 8.2).
    ///   BalanceMaker.exe -batchmode -nographics -logFile batch.log -arpgBatch
    ///       --scenario 01_근접1대1_검사37_vs_기사단장37 --runs 1000 --seed 12345 --output results --events first:3
    /// --scenario/--runs/--seed/--output/--events/--maxsec 는 직접 구현한 사용자 정의 인자다(Unity 내장 옵션 아님).
    /// 종료 코드: 0 성공, 2 인자/입력 오류, 3 실행 오류.
    /// </summary>
    public static class HeadlessBatch
    {
        public static bool IsBatchRequested { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Entry()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (!HasFlag(args, "-arpgBatch") && !HasFlag(args, "--arpg-batch")) return;
            IsBatchRequested = true;
            int code;
            try
            {
                code = Execute(args);
            }
            catch (Exception ex)
            {
                Debug.LogError("[ARPG batch] 실행 오류: " + ex);
                code = 3;
            }
            Debug.Log("[ARPG batch] 종료 코드 " + code);
            UnityEngine.Application.Quit(code);
        }

        private static bool HasFlag(string[] args, string flag)
        {
            foreach (var a in args) if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string Arg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }

        public static int Execute(string[] args)
        {
            var data = DataLoader.Load();
            if (data.Catalog == null || data.Catalog.BlockingIssueCount > 0)
            {
                Debug.LogError("[ARPG batch] 데이터 오류: " + string.Join(" / ", data.Errors.ToArray()));
                return 2;
            }
            string scenarioArg = Arg(args, "--scenario");
            if (string.IsNullOrEmpty(scenarioArg)) { Debug.LogError("[ARPG batch] --scenario 필요"); return 2; }
            string path = scenarioArg;
            if (!File.Exists(path)) path = Path.Combine(ArpgPaths.ScenarioDir, scenarioArg.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? scenarioArg : scenarioArg + ".json");
            string err;
            var sc = File.Exists(path) ? ScenarioStore.Load(path, out err) : null;
            if (sc == null) { Debug.LogError("[ARPG batch] 시나리오 없음: " + scenarioArg); return 2; }

            int runs;
            string s = Arg(args, "--runs");
            if (s != null)
            {
                if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out runs) || runs <= 0) { Debug.LogError("[ARPG batch] --runs 오류"); return 2; }
                sc.runs = runs;
            }
            s = Arg(args, "--seed");
            if (s != null)
            {
                long seed;
                if (!long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out seed)) { Debug.LogError("[ARPG batch] --seed 오류"); return 2; }
                sc.masterSeed = seed;
            }
            s = Arg(args, "--maxsec");
            if (s != null)
            {
                double maxSec;
                if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out maxSec) || maxSec <= 0) { Debug.LogError("[ARPG batch] --maxsec 오류"); return 2; }
                sc.maxBattleSec = maxSec;
            }
            var errors = sc.Validate(data.Catalog);
            if (errors.Count > 0) { Debug.LogError("[ARPG batch] 시나리오 오류: " + string.Join(" / ", errors.ToArray())); return 2; }

            string runId = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + "_headless_" + ScenarioStore.MakeSafeFileName(sc.name);
            string outRoot = Arg(args, "--output");
            string dir = string.IsNullOrEmpty(outRoot) ? Path.Combine(ArpgPaths.ExportRoot, runId) : Path.Combine(Path.GetFullPath(outRoot), runId);
            Directory.CreateDirectory(dir);

            string eventsArg = Arg(args, "--events") ?? "none";
            CsvEventSink events = null;
            if (eventsArg == "all") events = new CsvEventSink(Path.Combine(dir, "events.csv"));
            else if (eventsArg.StartsWith("first:", StringComparison.OrdinalIgnoreCase))
            {
                int n;
                if (!int.TryParse(eventsArg.Substring(6), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) || n <= 0) { Debug.LogError("[ARPG batch] --events first:N 오류"); return 2; }
                events = new CsvEventSink(Path.Combine(dir, "events.csv"), i => i < n);
            }

            var results = new ResultsCsvWriter(Path.Combine(dir, "results.csv"));
            IBattleEventSink sink = events;
            var batch = new BatchCoordinator(data.Catalog, data.Rules, sc, runId, sc.runs, i => sink, r => results.Write(r));
            Debug.Log("[ARPG batch] 시작 " + sc.name + " runs=" + sc.runs + " seed=" + sc.masterSeed + " → " + dir);
            batch.Run(0);
            bool partial = batch.ErrorMessage != null;
            results.Close(partial);
            string ev = events != null ? events.Close(partial) : null;
            var sum = batch.Summary;
            SummaryExporter.WriteSummary(Path.Combine(dir, "summary.csv"), sum, sc, runId, partial);
            SummaryExporter.WriteSkillStats(Path.Combine(dir, "skill_stats.csv"), sum);
            SummaryExporter.WriteManifest(Path.Combine(dir, "manifest.json"), new RunManifest
            {
                runId = runId,
                createdAt = DateTime.Now.ToString("s", CultureInfo.InvariantCulture),
                appVersion = UnityEngine.Application.version,
                unityVersion = UnityEngine.Application.unityVersion,
                mode = "headless",
                scenarioName = sc.name,
                scenarioSignature = sc.Signature(),
                masterSeed = sc.masterSeed,
                runsRequested = sum.Requested,
                runsCompleted = sum.Completed,
                runsAborted = sum.Aborted,
                partial = partial,
                dataHash = data.Catalog.DataHash,
                dataGeneratedAt = data.Catalog.DataGeneratedAt,
                rulesVersion = data.Rules.rulesVersion,
                rulesHash = StableHash.Hex(StableHash.Fnv1a64(data.Rules.Describe())),
                rulesSource = data.RulesSource,
                eventsCsv = ev,
                realSeconds = sum.ElapsedRealSec,
                scenario = sc,
            });
            Debug.Log("[ARPG batch] 완료 " + sum.Completed + "/" + sum.Requested + " 아군 승률 " + (sum.WinRate(0) * 100).ToString("0.0", CultureInfo.InvariantCulture) + "% → " + dir);
            if (batch.ErrorMessage != null) { Debug.LogError("[ARPG batch] " + batch.ErrorMessage); return 3; }
            return 0;
        }
    }
}

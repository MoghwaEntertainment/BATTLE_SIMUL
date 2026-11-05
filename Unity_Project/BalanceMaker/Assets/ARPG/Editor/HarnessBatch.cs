using System;
using System.IO;
using ARPG.Core;
using ARPG.Core.Tests;
using ARPG.Runtime;
using UnityEditor;
using UnityEngine;

namespace ARPG.EditorTools
{
    /// <summary>Noninteractive Editor validation; no menu/dialog dependency.</summary>
    public static class HarnessBatch
    {
        [Serializable] private class Report
        {
            public int passed;
            public int failed;
            public SelfTestEntry[] tests;
        }
        [Serializable] private class SelfTestEntry
        {
            public string name;
            public bool passed;
            public string detail;
        }
        public static void Verify()
        {
            int code = 2;
            try
            {
                string path = null;
                var args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                    if (args[i] == "-arpgTestReport") path = args[i + 1];
                if (path == null) throw new ArgumentException("-arpgTestReport is required");
                var data = DataLoader.Load();
                if (data.Catalog == null || data.Errors.Count > 0 || data.Catalog.BlockingIssueCount > 0)
                    throw new Exception("Data load failed: " + string.Join(" / ", data.Errors.ToArray()));
                var files = ScenarioStore.ListFiles();
                if (files.Count == 0) throw new Exception("No scenarios");
                string error;
                var scenario = ScenarioStore.Load(files[0], out error);
                if (scenario == null || scenario.Validate(data.Catalog).Count > 0) throw new Exception("Invalid scenario: " + error);
                var tests = CoreSelfTests.RunAll(data.Catalog, data.Rules, scenario);
                var report = new Report { tests = new SelfTestEntry[tests.Count] };
                for (int i = 0; i < tests.Count; i++)
                {
                    var t = tests[i];
                    report.tests[i] = new SelfTestEntry { name = t.Name, passed = t.Passed, detail = t.Detail };
                    if (t.Passed) report.passed++; else report.failed++;
                }
                File.WriteAllText(path, JsonUtility.ToJson(report, true));
                code = report.failed == 0 ? 0 : 1;
            }
            catch (Exception ex) { Debug.LogException(ex); }
            finally { EditorApplication.Exit(code); }
        }
    }
}

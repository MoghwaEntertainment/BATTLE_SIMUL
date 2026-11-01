using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using ARPG.Core;
using ARPG.Core.Tests;
using ARPG.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace ARPG.EditorTools
{
    /// <summary>에디터 메뉴: 장면 생성, 엑셀 변환, Self Test, 폴더 열기.</summary>
    public static class ArpgEditorMenu
    {
        private const string ScenePath = "Assets/ARPG/Scenes/Simulator.unity";

        [MenuItem("ARPG/1. Simulator 장면 만들기", false, 1)]
        public static void CreateSimulatorScene()
        {
            if (File.Exists(ScenePath))
            {
                if (!EditorUtility.DisplayDialog("ARPG", "이미 " + ScenePath + " 가 있습니다. 열까요?", "열기", "취소")) return;
                if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Additive);
            var go = new GameObject("ARPG Simulator");
            go.AddComponent<SimulationBootstrap>();
            SceneManager.MoveGameObjectToScene(go, scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.CloseScene(scene, true);
            AddSceneToBuild(ScenePath);
            AssetDatabase.Refresh();
            Debug.Log("[ARPG] 장면 생성: " + ScenePath + " (빌드 설정 첫 장면으로 등록)");
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }

        private static void AddSceneToBuild(string path)
        {
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            list.RemoveAll(s => s.path == path);
            list.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        [MenuItem("ARPG/2. 엑셀 → JSON 변환 실행", false, 2)]
        public static void RunExporter()
        {
            string project = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
            string script = Path.Combine(project, "Tools", "ExcelExport", "export_tables.py");
            if (!File.Exists(script))
            {
                EditorUtility.DisplayDialog("ARPG", "변환 스크립트가 없습니다:\n" + script, "확인");
                return;
            }
            string python = FindPython(project);
            var psi = new ProcessStartInfo
            {
                FileName = python,
                Arguments = "\"" + script + "\"",
                WorkingDirectory = project,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            try
            {
                EditorUtility.DisplayProgressBar("ARPG", "엑셀 변환 중... (" + python + ")", 0.5f);
                using (var p = Process.Start(psi))
                {
                    string stdout = p.StandardOutput.ReadToEnd();
                    string stderr = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    if (p.ExitCode == 0) Debug.Log("[ARPG] 변환 완료\n" + stdout);
                    else Debug.LogError("[ARPG] 변환 실패(코드 " + p.ExitCode + ")\n" + stdout + "\n" + stderr);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[ARPG] python 실행 실패: " + ex.Message + "\n python 과 openpyxl 이 필요합니다 (pip install openpyxl).");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
            }
        }

        private static string FindPython(string project)
        {
            // 상위 폴더의 기존 venv(ARPG_BATTLESIMULATOR/venv) 우선
            string root = Path.GetFullPath(Path.Combine(project, "..", ".."));
            string[] candidates =
            {
                Path.Combine(root, "ARPG_BATTLESIMULATOR", "venv", "Scripts", "python.exe"),
                Path.Combine(root, "ARPG_BATTLESIMULATOR", "venv", "bin", "python"),
            };
            foreach (var c in candidates) if (File.Exists(c)) return c;
            return UnityEngine.Application.platform == RuntimePlatform.WindowsEditor ? "python" : "python3";
        }

        [MenuItem("ARPG/3. Self Test 실행", false, 3)]
        public static void RunSelfTests()
        {
            var data = DataLoader.Load();
            BattleScenario sc = null;
            var files = ScenarioStore.ListFiles();
            if (files.Count > 0)
            {
                string err;
                sc = ScenarioStore.Load(files[0], out err);
            }
            var results = CoreSelfTests.RunAll(data.Catalog, data.Rules ?? new CombatRules(), sc);
            int pass = 0;
            foreach (var r in results)
            {
                if (r.Passed) { pass++; Debug.Log("[ARPG SelfTest] " + r); }
                else Debug.LogError("[ARPG SelfTest] " + r);
            }
            string msg = "Self Test " + pass + "/" + results.Count + " 통과";
            if (data.Catalog == null) msg += "\n(데이터가 없어 실제 데이터 결정성 테스트는 생략)";
            Debug.Log("[ARPG] " + msg);
            EditorUtility.DisplayDialog("ARPG Self Test", msg + "\n자세한 내용은 Console 참고", "확인");
        }

        [MenuItem("ARPG/데이터 폴더 열기", false, 20)]
        public static void OpenDataFolder() { EditorUtility.RevealInFinder(ArpgPaths.DataDir); }

        [MenuItem("ARPG/검증 보고서 열기", false, 21)]
        public static void OpenReport() { UnityEngine.Application.OpenURL(ArpgPaths.ToFileUrl(ArpgPaths.ValidationReport)); }

        [MenuItem("ARPG/Export 폴더 열기", false, 22)]
        public static void OpenExportFolder()
        {
            Directory.CreateDirectory(ArpgPaths.ExportRoot);
            EditorUtility.RevealInFinder(ArpgPaths.ExportRoot);
        }
    }
}

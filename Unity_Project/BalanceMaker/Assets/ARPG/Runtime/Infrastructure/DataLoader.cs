using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ARPG.Core;
using UnityEngine;

namespace ARPG.Runtime
{
    public static class ArpgPaths
    {
        public static string Root { get { return Path.Combine(UnityEngine.Application.streamingAssetsPath, "ARPG"); } }
        public static string DataDir { get { return Path.Combine(Root, "Data"); } }
        public static string ScenarioDir { get { return Path.Combine(Root, "Scenarios"); } }
        public static string RulesFile { get { return Path.Combine(Root, "rules.json"); } }
        public static string ValidationReport { get { return Path.Combine(DataDir, "validation_report.md"); } }
        public static string ExportRoot { get { return Path.Combine(UnityEngine.Application.persistentDataPath, "Exports"); } }

        public static string ToFileUrl(string path)
        {
            return "file:///" + path.Replace('\\', '/');
        }
    }

    public sealed class LoadedData
    {
        public DataCatalog Catalog;
        public CombatRules Rules;
        public string RulesSource = "기본값";
        public readonly List<string> Errors = new List<string>();
        public bool Ok { get { return Catalog != null && Errors.Count == 0 && Catalog.BlockingIssueCount == 0; } }
    }

    /// <summary>StreamingAssets/ARPG 의 정규화 JSON → 읽기 전용 DataCatalog.</summary>
    public static class DataLoader
    {
        private static T ReadJson<T>(string path, List<string> errors) where T : class
        {
            if (!File.Exists(path))
            {
                errors.Add("파일 없음: " + path);
                return null;
            }
            try
            {
                return JsonUtility.FromJson<T>(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                errors.Add("JSON 읽기 실패: " + Path.GetFileName(path) + " - " + ex.Message);
                return null;
            }
        }

        public static LoadedData Load()
        {
            var ld = new LoadedData();
            string dir = ArpgPaths.DataDir;
            var chars = ReadJson<CharacterFileDto>(Path.Combine(dir, "characters.json"), ld.Errors);
            var skills = ReadJson<SkillFileDto>(Path.Combine(dir, "skills.json"), ld.Errors);
            var moves = ReadJson<MovesetFileDto>(Path.Combine(dir, "movesets.json"), ld.Errors);
            var ai = ReadJson<AiFileDto>(Path.Combine(dir, "ai.json"), ld.Errors);
            var manifest = ReadJson<DataManifestDto>(Path.Combine(dir, "manifest.json"), ld.Errors);
            // buffs.json 은 선택(변환기 1.2.0 부터 생성). 없으면 버프 없이 실행한다.
            string buffPath = Path.Combine(dir, "buffs.json");
            var buffs = File.Exists(buffPath) ? ReadJson<BuffFileDto>(buffPath, ld.Errors) : null;
            ld.Rules = LoadRules(out ld.RulesSource, ld.Errors);
            if (chars == null || skills == null || moves == null || ai == null)
            {
                ld.Errors.Add("Tools/ExcelExport/export_tables.py 로 데이터를 먼저 생성하세요 (메뉴 ARPG > 엑셀 → JSON 변환)");
                return ld;
            }
            ld.Catalog = DataCatalog.Build(chars, skills, moves, ai, manifest, buffs);
            ld.Catalog.SortRules(ld.Rules.commonRowsFirst);
            return ld;
        }

        public static CombatRules LoadRules(out string source, List<string> errors)
        {
            var r = new CombatRules();
            source = "기본값(코드)";
            string path = ArpgPaths.RulesFile;
            if (File.Exists(path))
            {
                try
                {
                    JsonUtility.FromJsonOverwrite(File.ReadAllText(path, Encoding.UTF8), r);
                    source = "rules.json";
                }
                catch (Exception ex)
                {
                    errors.Add("rules.json 읽기 실패(기본값 사용): " + ex.Message);
                }
            }
            if (r.tickRate <= 0) { errors.Add("tickRate 는 1 이상"); r.tickRate = 30; }
            if (r.aiTickInterval <= 0) r.aiTickInterval = 1;
            return r;
        }
    }

    public static class ScenarioStore
    {
        public static List<string> ListFiles()
        {
            var list = new List<string>();
            string dir = ArpgPaths.ScenarioDir;
            if (Directory.Exists(dir))
            {
                list.AddRange(Directory.GetFiles(dir, "*.json"));
                list.Sort(StringComparer.Ordinal);
            }
            return list;
        }

        public static BattleScenario Load(string path, out string error)
        {
            error = null;
            try
            {
                var sc = JsonUtility.FromJson<BattleScenario>(File.ReadAllText(path, Encoding.UTF8));
                if (sc == null) { error = "빈 시나리오"; return null; }
                if (sc.teams == null) sc.teams = new List<ScenarioTeam>();
                foreach (var t in sc.teams) if (t.members == null) t.members = new List<ScenarioMember>();
                if (string.IsNullOrEmpty(sc.name)) sc.name = Path.GetFileNameWithoutExtension(path);
                return sc;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        public static string Save(BattleScenario sc, out string error)
        {
            error = null;
            try
            {
                string dir = ArpgPaths.ScenarioDir;
                Directory.CreateDirectory(dir);
                string safe = MakeSafeFileName(string.IsNullOrEmpty(sc.name) ? "scenario" : sc.name);
                string path = Path.Combine(dir, safe + ".json");
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(sc, true), new UTF8Encoding(false));
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                return path;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        public static string MakeSafeFileName(string name)
        {
            var sb = new StringBuilder();
            var invalid = Path.GetInvalidFileNameChars();
            foreach (char ch in name) sb.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
            return sb.ToString();
        }
    }
}

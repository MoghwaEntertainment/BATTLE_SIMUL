using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using ARPG.Core;
using ARPG.Core.Batch;
using ARPG.Core.Tests;

class Program
{
    static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
    static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json);
    static int Main(string[] args)
    {
        try
        {
            if (args.Length < 3) throw new ArgumentException("test|run <ARPG directory> <scenario.json> [runs] [seed]");
            string data = Path.Combine(args[1], "Data");
            var rules = Read<CombatRules>(Path.Combine(args[1], "rules.json"));
            string configPath = Path.Combine(data, "gameconfig.json");
            if (File.Exists(configPath)) rules = GameConfigRules.Apply(rules, Read<GameConfigFileDto>(configPath));
            if (rules.tickRate <= 0 || rules.aiTickInterval <= 0) throw new ArgumentException("Invalid tick settings");
            var catalog = DataCatalog.Build(
                Read<CharacterFileDto>(Path.Combine(data, "characters.json")),
                Read<SkillFileDto>(Path.Combine(data, "skills.json")),
                Read<MovesetFileDto>(Path.Combine(data, "movesets.json")),
                Read<AiFileDto>(Path.Combine(data, "ai.json")),
                Read<DataManifestDto>(Path.Combine(data, "manifest.json")),
                File.Exists(Path.Combine(data, "buffs.json")) ? Read<BuffFileDto>(Path.Combine(data, "buffs.json")) : null);
            catalog.SortRules(rules.commonRowsFirst);
            if (catalog.BlockingIssueCount > 0) throw new ArgumentException("Blocking catalog issues: " + catalog.BlockingIssueCount);
            var scenario = Read<BattleScenario>(args[2]);
            var errors = scenario.Validate(catalog);
            if (errors.Count > 0) throw new ArgumentException(string.Join(" / ", errors));
            object result;
            int exit = 0;
            if (args[0] == "test")
            {
                var tests = CoreSelfTests.RunAll(catalog, rules, scenario);
                int failed = tests.Count(t => !t.Passed);
                result = new { mode = "core-test", passed = tests.Count - failed, failed, tests };
                exit = failed == 0 ? 0 : 1;
            }
            else if (args[0] == "run")
            {
                int runs = args.Length > 3 ? int.Parse(args[3]) : 10;
                if (runs < 1 || runs > 10000) throw new ArgumentException("runs: 1..10000");
                if (args.Length > 4) scenario.masterSeed = long.Parse(args[4]);
                var battles = new System.Collections.Generic.List<object>();
                var batch = new BatchCoordinator(catalog, rules, scenario, "ai-harness", runs, null,
                    r => battles.Add(new { index = battles.Count, seed = BatchCoordinator.SeedFor(scenario, battles.Count).ToString(),
                        reason = r.Reason.ToString(), winner = r.WinnerTeam, seconds = r.DurationSec, ticks = r.Ticks }));
                batch.Run(0);
                if (batch.ErrorMessage != null) throw new Exception(batch.ErrorMessage);
                result = new { mode = "core-run", scenario = scenario.name, seed = scenario.masterSeed, runs,
                    dataHash = catalog.DataHash, rulesVersion = rules.rulesVersion, effectiveRules = rules,
                    completed = batch.Summary.Completed, timeouts = batch.Summary.Timeouts,
                    mutualWipes = batch.Summary.MutualWipes, wins = batch.Summary.TeamWins,
                    meanSeconds = batch.Summary.MeanDuration(), battles };
            }
            else throw new ArgumentException("Unknown mode");
            Console.WriteLine(JsonSerializer.Serialize(result, Json));
            return exit;
        }
        catch (Exception ex)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { error = ex.ToString() }, Json));
            return 2;
        }
    }
}

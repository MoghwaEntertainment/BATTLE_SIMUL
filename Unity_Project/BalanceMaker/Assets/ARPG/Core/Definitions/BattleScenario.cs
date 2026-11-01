using System;
using System.Collections.Generic;

namespace ARPG.Core
{
    [Serializable]
    public sealed class ScenarioMember
    {
        public string definitionId;
        public int count = 1;
    }

    [Serializable]
    public sealed class ScenarioTeam
    {
        public int teamId;
        public string name;
        public List<ScenarioMember> members = new List<ScenarioMember>();
    }

    /// <summary>팀·개체·배치·시드·반복 수를 담는 시나리오 프리셋(StreamingAssets/ARPG/Scenarios/*.json).</summary>
    [Serializable]
    public sealed class BattleScenario
    {
        public string name = "새 시나리오";
        public string description = "";
        public long masterSeed = 12345;
        public int runs = 100;
        public double maxBattleSec = 300;
        public double spawnDistance = 10;
        public double spawnSpacing = 1.5;
        public List<ScenarioTeam> teams = new List<ScenarioTeam>();

        public BattleScenario Clone()
        {
            var c = new BattleScenario
            {
                name = name,
                description = description,
                masterSeed = masterSeed,
                runs = runs,
                maxBattleSec = maxBattleSec,
                spawnDistance = spawnDistance,
                spawnSpacing = spawnSpacing,
            };
            foreach (var t in teams)
            {
                var nt = new ScenarioTeam { teamId = t.teamId, name = t.name };
                foreach (var m in t.members) nt.members.Add(new ScenarioMember { definitionId = m.definitionId, count = m.count });
                c.teams.Add(nt);
            }
            return c;
        }

        public ScenarioTeam GetOrAddTeam(int teamId)
        {
            foreach (var t in teams) if (t.teamId == teamId) return t;
            var nt = new ScenarioTeam { teamId = teamId, name = teamId == 0 ? "아군" : "적군" };
            teams.Add(nt);
            teams.Sort((a, b) => a.teamId.CompareTo(b.teamId));
            return nt;
        }

        /// <summary>실행 전 검증. 전투 필수 참조 오류는 실행을 막는다.</summary>
        public List<string> Validate(DataCatalog catalog)
        {
            var errors = new List<string>();
            int teamsWithMembers = 0;
            foreach (var t in teams)
            {
                int n = 0;
                foreach (var m in t.members)
                {
                    if (m.count <= 0) continue;
                    CharacterDefinition def;
                    if (!catalog.Characters.TryGetValue(m.definitionId ?? string.Empty, out def))
                        errors.Add("팀 " + t.teamId + ": 캐릭터 ID '" + m.definitionId + "' 없음");
                    else if (!def.Enabled)
                        errors.Add("팀 " + t.teamId + ": 캐릭터 '" + def.DisplayName + "' 비활성(검증 보고서 참조)");
                    n += m.count;
                }
                if (n > 0) teamsWithMembers++;
            }
            if (teamsWithMembers < 2) errors.Add("최소 두 팀에 1명 이상 배치해야 합니다");
            if (runs <= 0) errors.Add("반복 횟수는 1 이상");
            if (maxBattleSec <= 0) errors.Add("제한 시간은 0보다 커야 함");
            return errors;
        }

        public string Signature()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(name).Append('|').Append(masterSeed).Append('|').Append(maxBattleSec).Append('|').Append(spawnDistance).Append('|').Append(spawnSpacing);
            foreach (var t in teams)
            {
                sb.Append("|T").Append(t.teamId);
                foreach (var m in t.members) sb.Append(',').Append(m.definitionId).Append('x').Append(m.count);
            }
            return sb.ToString();
        }
    }
}

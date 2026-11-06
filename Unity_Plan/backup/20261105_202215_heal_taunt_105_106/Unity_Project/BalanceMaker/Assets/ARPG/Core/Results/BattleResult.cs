using System;
using System.Collections.Generic;
using ARPG.Core.Events;
using ARPG.Core.Simulation;

namespace ARPG.Core
{
    public sealed class CombatantSummary
    {
        public int EntityId;
        public string Label;
        public int TeamId;
        public string DefinitionId;
        public string Name;
        public bool Alive;
        public double Hp, MaxHp, Fp, MaxFp, Stamina, MaxStamina;
        public double DamageDealt, DamageTaken;
        public long HitsLanded, HitsTaken, HitsGuarded, HitsAvoided, StaggerCount, ResourceBlockedCount;
        public long DeathTick;
    }

    public sealed class BattleResult
    {
        public string RunId;
        public int BattleIndex;
        public ulong Seed;
        public string ScenarioName;
        public string DataHash;
        public string RulesVersion;
        public BattleEndReason Reason;
        public int WinnerTeam = -1;
        public long Ticks;
        public double DurationSec;
        public ulong EventHash;
        public long EventCount;
        public readonly List<CombatantSummary> Combatants = new List<CombatantSummary>();
        public readonly Dictionary<string, SkillStat> SkillStats = new Dictionary<string, SkillStat>();

        public int AliveCount(int team)
        {
            int n = 0;
            foreach (var c in Combatants) if (c.TeamId == team && c.Alive) n++;
            return n;
        }

        public int MemberCount(int team)
        {
            int n = 0;
            foreach (var c in Combatants) if (c.TeamId == team) n++;
            return n;
        }

        public double TeamHp(int team)
        {
            double s = 0;
            foreach (var c in Combatants) if (c.TeamId == team) s += Math.Max(0, c.Hp);
            return s;
        }

        public double TeamMaxHp(int team)
        {
            double s = 0;
            foreach (var c in Combatants) if (c.TeamId == team) s += c.MaxHp;
            return s;
        }

        public double TeamDamageDealt(int team)
        {
            double s = 0;
            foreach (var c in Combatants) if (c.TeamId == team) s += c.DamageDealt;
            return s;
        }

        public string OutcomeText
        {
            get
            {
                switch (Reason)
                {
                    case BattleEndReason.TeamVictory: return WinnerTeam == 0 ? "아군 승리" : (WinnerTeam == 1 ? "적군 승리" : "팀" + WinnerTeam + " 승리");
                    case BattleEndReason.MutualWipe: return "동시 전멸";
                    case BattleEndReason.Timeout: return "시간 초과";
                    case BattleEndReason.UserAborted: return "중단";
                    case BattleEndReason.Error: return "오류";
                    default: return "진행 중";
                }
            }
        }
    }

    /// <summary>
    /// 원본 이벤트로 스킬별 집계를 만든다. 시뮬레이션이 항상 내부에 연결하므로 Console/CSV 필터와 무관하다.
    /// </summary>
    public sealed class SkillStatsCollector : IBattleEventSink
    {
        private BattleStartInfo _info;
        public readonly Dictionary<string, SkillStat> Stats = new Dictionary<string, SkillStat>();

        public void OnBattleStarted(BattleStartInfo info)
        {
            _info = info;
            Stats.Clear();
        }

        private SkillStat Get(int actorId, string skillId)
        {
            int team = -1;
            var ci = _info != null ? _info.Find(actorId) : null;
            if (ci != null) team = ci.TeamId;
            string key = team + "|" + skillId;
            SkillStat s;
            if (!Stats.TryGetValue(key, out s))
            {
                s = new SkillStat { TeamId = team, SkillId = skillId };
                Stats[key] = s;
            }
            return s;
        }

        public void OnEvent(in BattleEvent e)
        {
            switch (e.Type)
            {
                case BattleEventType.ActionStarted:
                    if (!string.IsNullOrEmpty(e.SkillId)) Get(e.ActorId, e.SkillId).Uses++;
                    break;
                case BattleEventType.HitResolved:
                    if (!string.IsNullOrEmpty(e.SkillId))
                    {
                        var s = Get(e.ActorId, e.SkillId);
                        s.Hits++;
                        if (!double.IsNaN(e.Damage)) s.Damage += e.Damage;
                    }
                    break;
                case BattleEventType.Guarded:
                    if (!string.IsNullOrEmpty(e.SkillId))
                    {
                        var s = Get(e.ActorId, e.SkillId);
                        s.Guarded++;
                        if (!double.IsNaN(e.Damage)) s.Damage += e.Damage;
                    }
                    break;
                case BattleEventType.HitAvoided:
                    if (!string.IsNullOrEmpty(e.SkillId)) Get(e.ActorId, e.SkillId).Avoided++;
                    break;
            }
        }

        public void OnBattleEnded(BattleResult result) { }
    }
}

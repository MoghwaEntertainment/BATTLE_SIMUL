using System;
using System.Collections.Generic;

namespace ARPG.Core.Events
{
    public enum BattleEventType
    {
        BattleStarted,
        ActionStarted,
        ActionRejected,
        ActionEnded,
        ActionInterrupted,
        HitResolved,
        HitAvoided,      // 무적(회피) 프레임
        Guarded,
        GuardBroken,
        Staggered,
        ResourceChanged, // 비용 차감/회복 외 특이 변화(가드 스태미나 등)
        StatusBuildup,
        StatusApplied,
        StatusTick,
        StatusExpired,
        PhaseChanged,
        TargetChanged,
        EntityDied,
        BattleEnded,
        // 아래는 버프 추가(2026-11-02). 기존 값의 번호가 바뀌지 않도록 끝에 붙인다(이벤트 해시 호환).
        BuffApplied,     // ActorId = 받은 개체, TargetId = 건 개체, StatusId = BUFF_<id>, Result = APPLY/REFRESH
        BuffExpired,     // ActorId = 받은 개체, StatusId = BUFF_<id>
    }

    /// <summary>
    /// 값으로 복사되는 전투 사건. 비어 있는 수치는 NaN 으로 두어 실제 0 과 구별한다(CSV 에서는 빈칸).
    /// </summary>
    public struct BattleEvent
    {
        public long Sequence;
        public long Tick;
        public BattleEventType Type;
        public int ActorId;
        public int TargetId;
        public string ActionId;
        public string SkillId;
        public int HitIndex;          // 1부터. 해당 없음 0
        public string DamageType;
        public double Damage;
        public double ActorHpBefore, ActorHpAfter;
        public double ActorFpBefore, ActorFpAfter;
        public double ActorStaminaBefore, ActorStaminaAfter;
        public double TargetHpBefore, TargetHpAfter;
        public double PoiseBefore, PoiseAfter;
        public string StatusId;
        public double DistanceM;
        public string Result;
        public string Reason;

        public static BattleEvent Create(long seq, long tick, BattleEventType type, int actorId)
        {
            return new BattleEvent
            {
                Sequence = seq,
                Tick = tick,
                Type = type,
                ActorId = actorId,
                TargetId = -1,
                Damage = double.NaN,
                ActorHpBefore = double.NaN, ActorHpAfter = double.NaN,
                ActorFpBefore = double.NaN, ActorFpAfter = double.NaN,
                ActorStaminaBefore = double.NaN, ActorStaminaAfter = double.NaN,
                TargetHpBefore = double.NaN, TargetHpAfter = double.NaN,
                PoiseBefore = double.NaN, PoiseAfter = double.NaN,
                DistanceM = double.NaN,
            };
        }
    }

    /// <summary>전투 시작 시점의 개체 식별 정보(같은 이름 여러 개체를 EntityId/Label 로 구별).</summary>
    public sealed class CombatantInfo
    {
        public int EntityId;
        public string Label;          // 예: A01, B03
        public int TeamId;
        public string DefinitionId;
        public string DisplayName;
        public bool IsMonster;
        public double MaxHp, MaxFp, MaxStamina;
    }

    public sealed class BattleStartInfo
    {
        public string RunId;
        public int BattleIndex;
        public ulong Seed;
        public string ScenarioName;
        public string DataHash;
        public string RulesVersion;
        public double TickRate;
        public List<CombatantInfo> Combatants = new List<CombatantInfo>();

        public CombatantInfo Find(int entityId)
        {
            if (entityId >= 0 && entityId < Combatants.Count && Combatants[entityId].EntityId == entityId) return Combatants[entityId];
            foreach (var c in Combatants) if (c.EntityId == entityId) return c;
            return null;
        }
    }

    public interface IBattleEventSink
    {
        void OnBattleStarted(BattleStartInfo info);
        void OnEvent(in BattleEvent e);
        void OnBattleEnded(BattleResult result);
    }

    public sealed class CompositeEventSink : IBattleEventSink
    {
        private readonly List<IBattleEventSink> _sinks = new List<IBattleEventSink>();

        public void Add(IBattleEventSink s) { if (s != null) _sinks.Add(s); }
        public void Remove(IBattleEventSink s) { _sinks.Remove(s); }
        public int Count { get { return _sinks.Count; } }

        public void OnBattleStarted(BattleStartInfo info) { for (int i = 0; i < _sinks.Count; i++) _sinks[i].OnBattleStarted(info); }
        public void OnEvent(in BattleEvent e) { for (int i = 0; i < _sinks.Count; i++) _sinks[i].OnEvent(in e); }
        public void OnBattleEnded(BattleResult result) { for (int i = 0; i < _sinks.Count; i++) _sinks[i].OnBattleEnded(result); }
    }

    /// <summary>모든 이벤트 필드를 해시로 누적. 1배속/8배속/비시각 결과 동등성 검증에 사용.</summary>
    public sealed class EventHashSink : IBattleEventSink
    {
        public ulong Hash { get; private set; }
        public long Count { get; private set; }

        public void OnBattleStarted(BattleStartInfo info) { Hash = 14695981039346656037UL; Count = 0; }

        public void OnEvent(in BattleEvent e)
        {
            ulong h = Hash;
            h = StableHash.Mix(h, e.Sequence);
            h = StableHash.Mix(h, e.Tick);
            h = StableHash.Mix(h, (long)e.Type);
            h = StableHash.Mix(h, e.ActorId);
            h = StableHash.Mix(h, e.TargetId);
            h = StableHash.Fnv1a64(e.ActionId, h);
            h = StableHash.Fnv1a64(e.SkillId, h);
            h = StableHash.Mix(h, e.HitIndex);
            h = StableHash.Mix(h, e.Damage);
            h = StableHash.Mix(h, e.ActorHpAfter);
            h = StableHash.Mix(h, e.ActorStaminaAfter);
            h = StableHash.Mix(h, e.TargetHpAfter);
            h = StableHash.Mix(h, e.PoiseAfter);
            h = StableHash.Fnv1a64(e.StatusId, h);
            h = StableHash.Fnv1a64(e.Result, h);
            Hash = h;
            Count++;
        }

        public void OnBattleEnded(BattleResult result) { }
    }
}
